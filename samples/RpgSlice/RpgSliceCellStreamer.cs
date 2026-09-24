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

/// <summary>Adapts the engine cell streamer to RpgSlice terrain and blockout physics resources.</summary>
internal sealed class RpgSliceCellStreamer : IDisposable
{
    private const int TerrainPatchIntervals = 16;

    private readonly WorldManifest _world;
    private readonly PhysicsWorld _physics;
    private readonly ExteriorCellCollisionGate _collisionGate;
    private readonly ITerrainHeightMaterialSource _terrainSource;
    private readonly TerrainChunkSettings _terrainSettings;
    private readonly WorldPersistenceSession _persistence;
    private readonly CellAssetReferencePool<TerrainPatchSize, TerrainPatchTopology> _terrainTopologyPool = new();
    private readonly WorldCellStreamer<PreparedCell, ActiveCell> _streamer;
    private bool _disposed;

    public RpgSliceCellStreamer(WorldManifest world, PhysicsWorld physics,
        ExteriorCellCollisionGate collisionGate, ITerrainHeightMaterialSource terrainSource,
        TerrainChunkSettings terrainSettings, WorldPersistenceSession persistence)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        _collisionGate = collisionGate ?? throw new ArgumentNullException(nameof(collisionGate));
        _terrainSource = terrainSource ?? throw new ArgumentNullException(nameof(terrainSource));
        _terrainSettings = terrainSettings ?? throw new ArgumentNullException(nameof(terrainSettings));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        if (MathF.Abs(terrainSettings.ChunkSize - world.ExteriorCellWidth) > 1e-4f)
            throw new ArgumentException("Terrain chunks and exterior cells must have the same dimensions.", nameof(terrainSettings));

        _streamer = new WorldCellStreamer<PreparedCell, ActiveCell>(
            world,
            (coordinate, cellId, token) => PrepareCellAsync(cellId, token),
            _ => CreateActivationStepper(),
            (coordinate, available) =>
            {
                if (available) _collisionGate.MarkCollisionReady(coordinate);
                else _collisionGate.MarkCollisionUnavailable(coordinate);
            },
            new WorldCellStreamingOptions
            {
                LoadingRadiusInCells = 1,
                RetentionRadiusInCells = 2,
                MaximumActivationCostPerFrame = 8,
                MaximumActivationStepsPerFrame = 4,
                MaximumActivationTimePerFrame = TimeSpan.FromMilliseconds(4),
                RetryBaseDelay = TimeSpan.FromMilliseconds(250),
                RetryMaximumDelay = TimeSpan.FromSeconds(8),
                MaximumStartupAttempts = 3
            });
        _streamer.RetryScheduled += (coordinate, failure, delay) => Console.WriteLine(
            $"RpgSlice: cell ({coordinate.X}, {coordinate.Z}) failed; retrying in {delay.TotalMilliseconds:0} ms: {failure}");
    }

    public IEnumerable<ActiveCell> ActiveCells => _streamer.ActiveCells;
    public int ActiveCellCount => _streamer.ActiveCellCount;
    public int ActivationAttemptCount => _streamer.ActivationAttemptCount;
    public double LongestActivationMilliseconds => _streamer.LongestActivationMilliseconds;
    public string? LoadingStatus => _streamer.LoadingStatus;

    public void Start(Vector3 playerPosition) => _streamer.Start(playerPosition);
    public void Update(Vector3 playerPosition) => _streamer.Update(playerPosition);
    public void Request(ExteriorCellCoordinate coordinate) => _streamer.Request(coordinate);

    public bool TryGetActiveCell(Guid cellId,
        out WorldCellLoadOperation<PreparedCell, ActiveCell>? operation, out ActiveCell? active)
    {
        var found = _streamer.TryGetActiveOperation(cellId, out operation);
        active = found ? operation!.ActiveResources : null;
        return found;
    }

    public bool ForgetUnloadedCell(Guid cellId) => _streamer.ForgetUnloadedCell(cellId);

    public bool UnloadAndForgetCell(Guid cellId) => _streamer.UnloadAndForgetCell(cellId);

    public void AdoptActiveCell(Guid cellId, WorldCellLoadOperation<PreparedCell, ActiveCell> operation) =>
        _streamer.AdoptActiveCell(cellId, operation);

    public Task<PreparedCell> PrepareCellAsync(Guid cellId, CancellationToken cancellationToken) =>
        PrepareCellCoreAsync(cellId, cancellationToken);

    public WorldCellLoadOperation<PreparedCell, ActiveCell> LoadCell(Guid cellId)
    {
        var operation = new WorldCellLoadOperation<PreparedCell, ActiveCell>();
        var preparation = operation.PrepareAsync(token => PrepareCellAsync(cellId, token));
        preparation.GetAwaiter().GetResult();
        operation.PumpCompletions();
        operation.Activate(ActivatePreparedCell);
        return operation;
    }

    public ActiveCell ActivatePreparedCell(PreparedCell prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var stepper = CreateActivationStepper();
        try
        {
            while (true)
            {
                var result = stepper.Step(prepared, long.MaxValue);
                if (result.IsComplete)
                    return result.ActiveResources
                        ?? throw new InvalidOperationException("Cell activation completed without active resources.");
            }
        }
        finally
        {
            stepper.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        try { _streamer.Dispose(); }
        catch (Exception exception) { (failures ??= new()).Add(exception); }
        try { _terrainTopologyPool.Dispose(); }
        catch (Exception exception) { (failures ??= new()).Add(exception); }
        if (failures is { Count: 1 }) throw failures[0];
        if (failures is { Count: > 1 }) throw new AggregateException(failures);
    }

    private Task<PreparedCell> PrepareCellCoreAsync(Guid cellId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definition = _world.FindCell(cellId)
            ?? throw new InvalidOperationException($"World manifest has no cell with ID {cellId}.");
        var scene = SceneFile.Load(_world.ResolveScenePath(cellId));
        var coordinate = definition.ExteriorCoordinate ?? default;
        var hasTerrain = definition.Kind == WorldCellKind.Exterior;
        var terrainStride = hasTerrain ? _terrainSettings.GetVertexStride() : 0;
        var terrainVertices = hasTerrain
            ? TerrainChunkMeshBuilder.BuildVertices(_terrainSource, coordinate.X, coordinate.Z, _terrainSettings)
            : Array.Empty<TerrainChunkVertex>();
        var worldTransform = hasTerrain
            ? CreateWorldTransform(coordinate, _world.ExteriorCellWidth)
            : Matrix.Identity;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PreparedCell(cellId, coordinate, scene,
            worldTransform, terrainStride,
            terrainVertices, TerrainPatchIntervals));
    }

    private ActivationStepper CreateActivationStepper() =>
        new(_physics, _terrainTopologyPool, prepared =>
        {
            _persistence.PrepareCell(prepared.CellId, prepared.Scene);
            prepared.RefreshColliderObjects();
        });

    internal readonly record struct TerrainPatchSize(int XIntervals, int ZIntervals);

    internal sealed class TerrainPatchTopology : IDisposable
    {
        private int[]? _indices;

        public TerrainPatchTopology(TerrainPatchSize size)
        {
            if (size.XIntervals <= 0 || size.ZIntervals <= 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            var stride = size.XIntervals + 1;
            var indices = new int[checked(size.XIntervals * size.ZIntervals * 6)];
            var next = 0;
            for (var z = 0; z < size.ZIntervals; z++)
            for (var x = 0; x < size.XIntervals; x++)
            {
                var corner = z * stride + x;
                indices[next++] = corner;
                indices[next++] = corner + 1;
                indices[next++] = corner + stride;
                indices[next++] = corner + 1;
                indices[next++] = corner + stride + 1;
                indices[next++] = corner + stride;
            }
            _indices = indices;
        }

        public IReadOnlyList<int> Indices => _indices
            ?? throw new ObjectDisposedException(nameof(TerrainPatchTopology));

        public void Dispose() => _indices = null;
    }

    internal sealed class PreparedCell : IDisposable, ICellActivationCost
    {
        private SceneObject[] _colliderObjects;
        private readonly int _patchIntervals;

        public PreparedCell(Guid cellId, ExteriorCellCoordinate coordinate, SceneGraph scene,
            Matrix worldTransform, int terrainStride, TerrainChunkVertex[] terrainVertices, int patchIntervals)
        {
            CellId = cellId != Guid.Empty
                ? cellId
                : throw new ArgumentException("Prepared cell ID cannot be empty.", nameof(cellId));
            Coordinate = coordinate;
            Scene = scene ?? throw new ArgumentNullException(nameof(scene));
            WorldTransform = worldTransform;
            TerrainStride = terrainStride;
            TerrainVertices = terrainVertices ?? throw new ArgumentNullException(nameof(terrainVertices));
            _patchIntervals = patchIntervals > 0
                ? patchIntervals
                : throw new ArgumentOutOfRangeException(nameof(patchIntervals));
            if (terrainStride != 0 && (terrainStride < 2
                || terrainVertices.Length != checked(terrainStride * terrainStride)))
                throw new ArgumentException("Terrain vertex data does not match its stride.", nameof(terrainVertices));
            if (terrainStride == 0 && terrainVertices.Length != 0)
                throw new ArgumentException("A cell without terrain cannot contain terrain vertices.", nameof(terrainVertices));
            _colliderObjects = scene.Objects
                .Where(item => item.Enabled && (terrainStride == 0 || item.Name != "Ground") && item.Door is null)
                .ToArray();
            var patchesAcross = terrainStride == 0 ? 0 : (terrainStride - 2 + patchIntervals) / patchIntervals;
            EstimatedActivationCost = checked((long)patchesAcross * patchesAcross + _colliderObjects.Length);
        }

        public ExteriorCellCoordinate Coordinate { get; }
        public Guid CellId { get; }
        public SceneGraph Scene { get; }
        public Matrix WorldTransform { get; }
        public int TerrainStride { get; }
        public TerrainChunkVertex[] TerrainVertices { get; }
        public long EstimatedActivationCost { get; }
        public int TerrainPatchIntervals => _patchIntervals;
        public int TerrainPatchesAcross => TerrainStride == 0 ? 0 : (TerrainStride - 2 + _patchIntervals) / _patchIntervals;
        public int TerrainPatchCount => checked(TerrainPatchesAcross * TerrainPatchesAcross);
        public int ColliderCount => _colliderObjects.Length;
        public int WorkItemCount => checked(TerrainPatchCount + ColliderCount);
        public SceneObject GetColliderObject(int index) => _colliderObjects[index];
        public void RefreshColliderObjects() => _colliderObjects = Scene.Objects
            .Where(item => item.Enabled && (TerrainStride == 0 || item.Name != "Ground") && item.Door is null)
            .ToArray();
        public void Dispose() { }
    }

    private sealed class ActivationStepper : ICellActivationStepper<PreparedCell, ActiveCell>
    {
        private readonly PhysicsWorld _physics;
        private readonly CellAssetReferencePool<TerrainPatchSize, TerrainPatchTopology> _topologyPool;
        private readonly Action<PreparedCell> _prepareCell;
        private List<PhysicsObjectId> _colliders = new();
        private Dictionary<TerrainPatchSize,
            CellAssetReference<TerrainPatchSize, TerrainPatchTopology>> _topologyLeases = new();
        private int _nextWorkItem;
        private bool _transferred;
        private bool _disposed;
        private bool _prepared;

        public ActivationStepper(PhysicsWorld physics,
            CellAssetReferencePool<TerrainPatchSize, TerrainPatchTopology> topologyPool,
            Action<PreparedCell> prepareCell)
        {
            _physics = physics;
            _topologyPool = topologyPool;
            _prepareCell = prepareCell;
        }

        public CellActivationStepResult<ActiveCell> Step(PreparedCell prepared, long maximumCost)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ActivationStepper));
            if (maximumCost < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumCost));

            if (!_prepared)
            {
                _prepareCell(prepared);
                _prepared = true;
            }

            if (_nextWorkItem < prepared.TerrainPatchCount)
                ActivateTerrainPatch(prepared, _nextWorkItem);
            else if (_nextWorkItem < prepared.WorkItemCount)
                ActivateSceneCollider(prepared, _nextWorkItem - prepared.TerrainPatchCount);
            _nextWorkItem++;

            var isComplete = _nextWorkItem >= prepared.WorkItemCount;
            if (!isComplete)
                return new CellActivationStepResult<ActiveCell>(1, false, null);

            var worldTransform = prepared.WorldTransform;
            var colliders = _colliders;
            var topologyLeases = _topologyLeases.Values.ToList();
            var active = new ActiveCell(_physics, prepared.Coordinate, prepared.Scene,
                worldTransform, colliders, topologyLeases);
            _colliders = new List<PhysicsObjectId>();
            _topologyLeases = new Dictionary<TerrainPatchSize,
                CellAssetReference<TerrainPatchSize, TerrainPatchTopology>>();
            _transferred = true;
            return new CellActivationStepResult<ActiveCell>(1, true, active);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_transferred) return;
            List<Exception>? failures = null;
            for (var index = _colliders.Count - 1; index >= 0; index--)
            {
                try { _physics.RemoveStatic(_colliders[index]); }
                catch (Exception exception) { (failures ??= new()).Add(exception); }
            }
            foreach (var lease in _topologyLeases.Values)
            {
                try { lease.Dispose(); }
                catch (Exception exception) { (failures ??= new()).Add(exception); }
            }
            _colliders.Clear();
            _topologyLeases.Clear();
            if (failures is { Count: 1 }) throw failures[0];
            if (failures is { Count: > 1 }) throw new AggregateException(failures);
        }

        private void ActivateTerrainPatch(PreparedCell prepared, int patchIndex)
        {
            var across = prepared.TerrainPatchesAcross;
            var patchX = patchIndex % across;
            var patchZ = patchIndex / across;
            var firstX = patchX * prepared.TerrainPatchIntervals;
            var firstZ = patchZ * prepared.TerrainPatchIntervals;
            var xIntervals = Math.Min(prepared.TerrainPatchIntervals,
                prepared.TerrainStride - 1 - firstX);
            var zIntervals = Math.Min(prepared.TerrainPatchIntervals,
                prepared.TerrainStride - 1 - firstZ);
            var size = new TerrainPatchSize(xIntervals, zIntervals);
            if (!_topologyLeases.TryGetValue(size, out var lease))
            {
                lease = _topologyPool.Acquire(size, () => new TerrainPatchTopology(size));
                _topologyLeases.Add(size, lease);
            }

            var patchStride = xIntervals + 1;
            var vertices = new Vector3[checked(patchStride * (zIntervals + 1))];
            for (var z = 0; z <= zIntervals; z++)
            for (var x = 0; x <= xIntervals; x++)
            {
                var sourceIndex = (firstZ + z) * prepared.TerrainStride + firstX + x;
                vertices[z * patchStride + x] = prepared.TerrainVertices[sourceIndex].Position;
            }
            _colliders.Add(_physics.AddStaticTriangleMesh(vertices, lease.Asset.Indices));
        }

        private void ActivateSceneCollider(PreparedCell prepared, int objectIndex)
        {
            var item = prepared.GetColliderObject(objectIndex);
            if (item.GltfAsset is not null || item.CharacterSettings is not null)
                throw new InvalidOperationException(
                    $"RpgSlice blockout cell does not support GLB collider '{item.Name}'.");
            var world = prepared.Scene.GetWorldMatrix(item.Id)
                * prepared.WorldTransform;
            if (!world.Decompose(out var scale, out var rotation, out var position))
                throw new InvalidOperationException($"Cell object '{item.Name}' has an invalid transform.");
            scale = new Vector3(MathF.Abs(scale.X), MathF.Abs(scale.Y), MathF.Abs(scale.Z));
            if (scale.X <= 0f || scale.Y <= 0f || scale.Z <= 0f)
                throw new InvalidOperationException($"Cell object '{item.Name}' must have positive dimensions.");
            _colliders.Add(_physics.AddStaticBox(position, scale, rotation));
        }
    }

    internal sealed class ActiveCell : IDisposable
    {
        private readonly PhysicsWorld _physics;
        private readonly List<PhysicsObjectId> _colliders;
        private readonly List<CellAssetReference<TerrainPatchSize, TerrainPatchTopology>> _topologyLeases;
        private bool _disposed;

        internal ActiveCell(PhysicsWorld physics, ExteriorCellCoordinate coordinate,
            SceneGraph scene, Matrix worldTransform, List<PhysicsObjectId> colliders,
            List<CellAssetReference<TerrainPatchSize, TerrainPatchTopology>> topologyLeases)
        {
            _physics = physics;
            Coordinate = coordinate;
            Scene = scene;
            WorldTransform = worldTransform;
            _colliders = colliders;
            _topologyLeases = topologyLeases;
        }

        public ExteriorCellCoordinate Coordinate { get; }
        public SceneGraph Scene { get; }
        public Matrix WorldTransform { get; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            List<Exception>? failures = null;
            for (var index = _colliders.Count - 1; index >= 0; index--)
            {
                try { _physics.RemoveStatic(_colliders[index]); }
                catch (Exception exception) { (failures ??= new()).Add(exception); }
            }
            foreach (var lease in _topologyLeases)
            {
                try { lease.Dispose(); }
                catch (Exception exception) { (failures ??= new()).Add(exception); }
            }
            _colliders.Clear();
            _topologyLeases.Clear();
            if (failures is { Count: 1 }) throw failures[0];
            if (failures is { Count: > 1 }) throw new AggregateException(failures);
        }
    }

    private static Matrix CreateWorldTransform(ExteriorCellCoordinate coordinate, float cellWidth)
    {
        var offsetX = (float)(coordinate.X * (double)cellWidth);
        var offsetZ = (float)(coordinate.Z * (double)cellWidth);
        if (!float.IsFinite(offsetX) || !float.IsFinite(offsetZ))
            throw new InvalidOperationException($"Cell ({coordinate.X}, {coordinate.Z}) exceeds finite world space.");
        return Matrix.CreateTranslation(offsetX, 0f, offsetZ);
    }
}
