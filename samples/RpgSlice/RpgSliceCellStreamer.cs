using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ember.Physics;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;

namespace RpgSlice;

/// <summary>Prepares terrain and cell scene data off-thread, then owns collision through cell lifecycle.</summary>
internal sealed class RpgSliceCellStreamer : IDisposable
{
    private readonly WorldManifest _world;
    private readonly PhysicsWorld _physics;
    private readonly ExteriorCellCollisionGate _collisionGate;
    private readonly ITerrainHeightMaterialSource _terrainSource;
    private readonly TerrainChunkSettings _terrainSettings;
    private readonly ExteriorCellLoadingRing _loadingRing;
    private readonly Dictionary<ExteriorCellCoordinate, CellOperation> _cells = new();
    private readonly List<RetiredOperation> _retired = new();
    private bool _disposed;

    public RpgSliceCellStreamer(WorldManifest world, PhysicsWorld physics,
        ExteriorCellCollisionGate collisionGate, ITerrainHeightMaterialSource terrainSource,
        TerrainChunkSettings terrainSettings)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        _collisionGate = collisionGate ?? throw new ArgumentNullException(nameof(collisionGate));
        _terrainSource = terrainSource ?? throw new ArgumentNullException(nameof(terrainSource));
        _terrainSettings = terrainSettings ?? throw new ArgumentNullException(nameof(terrainSettings));
        if (MathF.Abs(terrainSettings.ChunkSize - world.ExteriorCellWidth) > 1e-4f)
            throw new ArgumentException("Terrain chunks and exterior cells must have the same dimensions.", nameof(terrainSettings));
        _loadingRing = world.CreateLoadingRing(radiusInCells: 1, retentionRadiusInCells: 2);
    }

    public IEnumerable<ActiveCell> ActiveCells
    {
        get
        {
            foreach (var entry in _cells.Values)
                if (entry.Operation.State == CellLifecycleState.Active
                    && entry.Operation.ActiveResources is { } active)
                    yield return active;
        }
    }

    public void Start(Vector3 playerPosition)
    {
        ThrowIfDisposed();
        UpdateRequests(playerPosition);
        var coordinate = _world.GetExteriorCoordinate(playerPosition);
        if (!_cells.TryGetValue(coordinate, out var center))
            throw new InvalidOperationException($"No exterior cell exists at ({coordinate.X}, {coordinate.Z}).");

        while (center.Operation.State == CellLifecycleState.Preparing)
        {
            center.Preparation.GetAwaiter().GetResult();
            PumpOperations();
        }
        if (center.Operation.State == CellLifecycleState.Failed)
            throw new InvalidOperationException($"The starting terrain cell ({coordinate.X}, {coordinate.Z}) failed to load.",
                center.Operation.Failure);
        if (center.Operation.State != CellLifecycleState.Active)
            PumpOperations();
        if (center.Operation.State != CellLifecycleState.Active)
            throw new InvalidOperationException("The starting terrain cell did not become active.");
    }

    public void Update(Vector3 playerPosition)
    {
        ThrowIfDisposed();
        UpdateRequests(playerPosition);
        PumpOperations();
        DrainRetired();
    }

    public void Request(ExteriorCellCoordinate coordinate)
    {
        ThrowIfDisposed();
        if (_cells.ContainsKey(coordinate) || !_world.TryGetExterior(coordinate, out var definition) || definition is null)
            return;

        var operation = new WorldCellLoadOperation<PreparedCell, ActiveCell>();
        var preparation = operation.PrepareAsync(token =>
        {
            token.ThrowIfCancellationRequested();
            var scene = SceneFile.Load(_world.ResolveScenePath(definition.Id));
            var terrain = TerrainChunkMeshBuilder.Build(
                _terrainSource, coordinate.X, coordinate.Z, _terrainSettings);
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new PreparedCell(coordinate, scene, terrain));
        });
        _cells.Add(coordinate, new CellOperation(operation, preparation));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        foreach (var pair in _cells.ToArray())
        {
            _collisionGate.MarkCollisionUnavailable(pair.Key);
            try
            {
                if (pair.Value.Operation.State == CellLifecycleState.Preparing)
                {
                    pair.Value.Operation.Cancel();
                    _retired.Add(new RetiredOperation(pair.Value.Operation, pair.Value.Preparation));
                }
                else
                {
                    StopOperation(pair.Value);
                }
            }
            catch (Exception exception) { (failures ??= new()).Add(exception); }
        }
        _cells.Clear();

        foreach (var retired in _retired)
        {
            try
            {
                retired.Preparation.GetAwaiter().GetResult();
                retired.Operation.PumpCompletions();
            }
            catch (Exception exception) { (failures ??= new()).Add(exception); }
        }
        _retired.Clear();
        if (failures is { Count: 1 }) throw failures[0];
        if (failures is { Count: > 1 }) throw new AggregateException(failures);
    }

    private void UpdateRequests(Vector3 playerPosition)
    {
        var update = _loadingRing.UpdatePlayerPosition(playerPosition);
        foreach (var coordinate in update.Entered) Request(coordinate);
        foreach (var coordinate in update.Left)
        {
            if (!_cells.Remove(coordinate, out var leaving)) continue;
            _collisionGate.MarkCollisionUnavailable(coordinate);
            if (leaving.Operation.State == CellLifecycleState.Preparing)
            {
                leaving.Operation.Cancel();
                _retired.Add(new RetiredOperation(leaving.Operation, leaving.Preparation));
            }
            else
            {
                StopOperation(leaving);
            }
        }
    }

    private void PumpOperations()
    {
        foreach (var pair in _cells.ToArray())
        {
            var operation = pair.Value.Operation;
            operation.PumpCompletions();
            if (operation.State == CellLifecycleState.Ready)
            {
                try
                {
                    operation.Activate(prepared => ActiveCell.Create(_physics, prepared));
                    _collisionGate.MarkCollisionReady(pair.Key);
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"RpgSlice: cell ({pair.Key.X}, {pair.Key.Z}) activation failed: {exception}");
                }
            }
            if (operation.State == CellLifecycleState.Failed && !pair.Value.FailureReported)
            {
                Console.WriteLine($"RpgSlice: cell ({pair.Key.X}, {pair.Key.Z}) could not activate: {operation.Failure}");
                pair.Value.FailureReported = true;
            }
        }
    }

    private static void StopOperation(CellOperation entry)
    {
        switch (entry.Operation.State)
        {
            case CellLifecycleState.Active:
                entry.Operation.Unload();
                break;
            case CellLifecycleState.Ready:
            case CellLifecycleState.Failed:
                entry.Operation.Discard();
                break;
            case CellLifecycleState.Preparing:
                entry.Operation.Cancel();
                break;
        }
    }

    private void DrainRetired()
    {
        for (var index = _retired.Count - 1; index >= 0; index--)
        {
            var retired = _retired[index];
            if (!retired.Preparation.IsCompleted) continue;
            retired.Operation.PumpCompletions();
            _retired.RemoveAt(index);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class CellOperation(
        WorldCellLoadOperation<PreparedCell, ActiveCell> operation,
        Task preparation)
    {
        public WorldCellLoadOperation<PreparedCell, ActiveCell> Operation { get; } = operation;
        public Task Preparation { get; } = preparation;
        public bool FailureReported { get; set; }
    }

    private sealed record RetiredOperation(
        WorldCellLoadOperation<PreparedCell, ActiveCell> Operation,
        Task Preparation);

    internal sealed class PreparedCell(
        ExteriorCellCoordinate coordinate,
        SceneGraph scene,
        TerrainChunkMeshData terrain) : IDisposable
    {
        public ExteriorCellCoordinate Coordinate { get; } = coordinate;
        public SceneGraph Scene { get; } = scene;
        public TerrainChunkMeshData Terrain { get; } = terrain;
        public void Dispose() { }
    }

    internal sealed class ActiveCell : IDisposable
    {
        private readonly PhysicsWorld _physics;
        private readonly List<PhysicsObjectId> _colliders;
        private bool _disposed;

        private ActiveCell(PhysicsWorld physics, ExteriorCellCoordinate coordinate,
            SceneGraph scene, List<PhysicsObjectId> colliders)
        {
            _physics = physics;
            Coordinate = coordinate;
            Scene = scene;
            _colliders = colliders;
        }

        public ExteriorCellCoordinate Coordinate { get; }
        public SceneGraph Scene { get; }

        internal static ActiveCell Create(PhysicsWorld physics, PreparedCell prepared)
        {
            var colliders = new List<PhysicsObjectId>();
            try
            {
                var vertices = new Vector3[prepared.Terrain.Vertices.Count];
                for (var index = 0; index < vertices.Length; index++)
                    vertices[index] = prepared.Terrain.Vertices[index].Position;
                var indices = new int[prepared.Terrain.TriangleIndices.Count];
                for (var index = 0; index < indices.Length; index++)
                    indices[index] = prepared.Terrain.TriangleIndices[index];
                colliders.Add(physics.AddStaticTriangleMesh(vertices, indices));

                foreach (var item in prepared.Scene.Objects.Where(item => item.Enabled && item.Name != "Ground"))
                {
                    if (item.GltfAsset is not null || item.CharacterSettings is not null)
                        throw new InvalidOperationException(
                            $"RpgSlice blockout cell does not support GLB collider '{item.Name}'.");
                    var world = prepared.Scene.GetWorldMatrix(item.Id);
                    if (!world.Decompose(out var scale, out var rotation, out var position))
                        throw new InvalidOperationException($"Cell object '{item.Name}' has an invalid transform.");
                    scale = new Vector3(MathF.Abs(scale.X), MathF.Abs(scale.Y), MathF.Abs(scale.Z));
                    if (scale.X <= 0f || scale.Y <= 0f || scale.Z <= 0f)
                        throw new InvalidOperationException($"Cell object '{item.Name}' must have positive dimensions.");
                    colliders.Add(physics.AddStaticBox(position, scale, rotation));
                }

                return new ActiveCell(physics, prepared.Coordinate, prepared.Scene, colliders);
            }
            catch
            {
                for (var index = colliders.Count - 1; index >= 0; index--)
                    physics.RemoveStatic(colliders[index]);
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (var index = _colliders.Count - 1; index >= 0; index--)
                _physics.RemoveStatic(_colliders[index]);
            _colliders.Clear();
        }
    }
}
