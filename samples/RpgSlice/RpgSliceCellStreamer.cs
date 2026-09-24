using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ember.Physics;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;

namespace RpgSlice;

/// <summary>Prepares cell data off-thread and incrementally owns collision through the cell lifecycle.</summary>
internal sealed class RpgSliceCellStreamer : IDisposable
{
    private const int TerrainPatchIntervals = 16;
    private const long ActivationWorkUnitsPerFrame = 8;
    private const int ActivationStepsPerFrame = 4;
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RetryMaximumDelay = TimeSpan.FromSeconds(8);

    private readonly WorldManifest _world;
    private readonly PhysicsWorld _physics;
    private readonly ExteriorCellCollisionGate _collisionGate;
    private readonly ITerrainHeightMaterialSource _terrainSource;
    private readonly TerrainChunkSettings _terrainSettings;
    private readonly ExteriorCellLoadingRing _loadingRing;
    private readonly CellActivationQueue<PreparedCell, ActiveCell> _activationQueue = new(
        ActivationWorkUnitsPerFrame, ActivationStepsPerFrame, TimeSpan.FromMilliseconds(4));
    private readonly CellAssetReferencePool<TerrainPatchSize, TerrainPatchTopology> _terrainTopologyPool = new();
    private readonly Dictionary<ExteriorCellCoordinate, CellOperation> _cells = new();
    private readonly List<RetiredOperation> _retired = new();
    private ExteriorCellCoordinate? _statusCell;
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

    public int ActiveCellCount
    {
        get
        {
            var count = 0;
            foreach (var entry in _cells.Values)
                if (entry.Operation.State == CellLifecycleState.Active) count++;
            return count;
        }
    }

    public int ActivationAttemptCount { get; private set; }
    public double LongestActivationMilliseconds { get; private set; }
    public string? LoadingStatus { get; private set; }

    public void Start(Vector3 playerPosition)
    {
        ThrowIfDisposed();
        UpdateRequests(playerPosition);
        var coordinate = _world.GetExteriorCoordinate(playerPosition);
        if (!_cells.TryGetValue(coordinate, out var center))
            throw new InvalidOperationException($"No exterior cell exists at ({coordinate.X}, {coordinate.Z}).");

        while (center.Operation.State != CellLifecycleState.Active)
        {
            if (center.Operation.State == CellLifecycleState.Preparing)
                center.Preparation.GetAwaiter().GetResult();

            PumpOperations();
            if (center.Operation.State == CellLifecycleState.Failed)
            {
                if (center.RetryCount >= 3)
                    throw new InvalidOperationException(
                        $"The starting terrain cell ({coordinate.X}, {coordinate.Z}) failed after {center.RetryCount} attempts.",
                        center.Operation.Failure);
                var delay = center.RetryAt - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero) Thread.Sleep(delay);
            }
        }
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
        var entry = new CellOperation(operation);
        _cells.Add(coordinate, entry);
        BeginPreparation(coordinate, definition.Id, entry);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        try { _activationQueue.Dispose(); }
        catch (Exception exception) { (failures ??= new()).Add(exception); }

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
        try { _terrainTopologyPool.Dispose(); }
        catch (Exception exception) { (failures ??= new()).Add(exception); }

        if (failures is { Count: 1 }) throw failures[0];
        if (failures is { Count: > 1 }) throw new AggregateException(failures);
    }

    private void BeginPreparation(ExteriorCellCoordinate coordinate, Guid cellId, CellOperation entry)
    {
        entry.Preparation = entry.Operation.PrepareAsync(token =>
        {
            token.ThrowIfCancellationRequested();
            var scene = SceneFile.Load(_world.ResolveScenePath(cellId));
            var terrainStride = _terrainSettings.GetVertexStride();
            var terrainVertices = TerrainChunkMeshBuilder.BuildVertices(
                _terrainSource, coordinate.X, coordinate.Z, _terrainSettings);
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new PreparedCell(coordinate, scene, terrainStride, terrainVertices,
                TerrainPatchIntervals));
        });
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
            var entry = pair.Value;
            var operation = entry.Operation;
            operation.PumpCompletions();
            if (operation.State == CellLifecycleState.Ready && !entry.ActivationQueued)
            {
                var stepper = new ActivationStepper(_physics, _terrainTopologyPool,
                    _world.ExteriorCellWidth);
                try
                {
                    _activationQueue.Enqueue(operation, stepper);
                    entry.ActivationQueued = true;
                    entry.Stepper = stepper;
                    entry.ObservedStepCount = stepper.StepCount;
                }
                catch
                {
                    stepper.Dispose();
                    throw;
                }
            }
        }

        var frameClock = Stopwatch.StartNew();
        try
        {
            _activationQueue.ProcessFrame();
        }
        catch (Exception exception)
        {
            Console.WriteLine($"RpgSlice: incremental cell activation failed: {exception}");
        }
        finally
        {
            frameClock.Stop();
            LongestActivationMilliseconds = Math.Max(LongestActivationMilliseconds,
                frameClock.Elapsed.TotalMilliseconds);
            foreach (var pair in _cells)
            {
                var entry = pair.Value;
                if (entry.Stepper is { } stepper && stepper.StepCount > entry.ObservedStepCount)
                {
                    ActivationAttemptCount += stepper.StepCount - entry.ObservedStepCount;
                    entry.ObservedStepCount = stepper.StepCount;
                }
                if (entry.Stepper is { } measured)
                    LongestActivationMilliseconds = Math.Max(LongestActivationMilliseconds,
                        measured.LongestStepMilliseconds);
                if (entry.Operation.State != CellLifecycleState.Ready)
                    entry.ActivationQueued = false;
                if (entry.Operation.State == CellLifecycleState.Active && !entry.CollisionReady)
                {
                    _collisionGate.MarkCollisionReady(pair.Key);
                    entry.CollisionReady = true;
                    if (_statusCell == pair.Key)
                    {
                        _statusCell = null;
                        LoadingStatus = null;
                    }
                }
            }
        }

        foreach (var pair in _cells)
        {
            var entry = pair.Value;
            if (entry.Operation.State != CellLifecycleState.Failed) continue;
            if (!entry.FailureReported)
            {
                entry.RetryCount++;
                var exponent = Math.Min(entry.RetryCount - 1, 5);
                var delay = TimeSpan.FromMilliseconds(Math.Min(
                    RetryBaseDelay.TotalMilliseconds * Math.Pow(2, exponent),
                    RetryMaximumDelay.TotalMilliseconds));
                entry.RetryAt = DateTimeOffset.UtcNow + delay;
                _statusCell = pair.Key;
                LoadingStatus = $"Cell ({pair.Key.X}, {pair.Key.Z}) failed; retrying in {delay.TotalSeconds:0.##}s";
                Console.WriteLine($"RpgSlice: cell ({pair.Key.X}, {pair.Key.Z}) failed; retry {entry.RetryCount} "
                    + $"in {delay.TotalMilliseconds:0} ms: {entry.Operation.Failure}");
                entry.FailureReported = true;
            }
            if (DateTimeOffset.UtcNow < entry.RetryAt) continue;

            entry.Operation.Discard();
            entry.FailureReported = false;
            if (_world.TryGetExterior(pair.Key, out var definition) && definition is not null)
                BeginPreparation(pair.Key, definition.Id, entry);
        }
    }

    private void StopOperation(CellOperation entry)
    {
        if (entry.ActivationQueued)
        {
            _activationQueue.Cancel(entry.Operation);
            entry.ActivationQueued = false;
            entry.Stepper = null;
        }

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

    private sealed class CellOperation(WorldCellLoadOperation<PreparedCell, ActiveCell> operation)
    {
        public WorldCellLoadOperation<PreparedCell, ActiveCell> Operation { get; } = operation;
        public Task Preparation { get; set; } = Task.CompletedTask;
        public bool ActivationQueued { get; set; }
        public bool CollisionReady { get; set; }
        public bool FailureReported { get; set; }
        public int RetryCount { get; set; }
        public DateTimeOffset RetryAt { get; set; }
        public ActivationStepper? Stepper { get; set; }
        public int ObservedStepCount { get; set; }
    }

    private sealed record RetiredOperation(
        WorldCellLoadOperation<PreparedCell, ActiveCell> Operation,
        Task Preparation);

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
        private readonly SceneObject[] _colliderObjects;
        private readonly int _patchIntervals;

        public PreparedCell(ExteriorCellCoordinate coordinate, SceneGraph scene,
            int terrainStride, TerrainChunkVertex[] terrainVertices, int patchIntervals)
        {
            Coordinate = coordinate;
            Scene = scene;
            TerrainStride = terrainStride;
            TerrainVertices = terrainVertices ?? throw new ArgumentNullException(nameof(terrainVertices));
            _patchIntervals = patchIntervals > 0
                ? patchIntervals
                : throw new ArgumentOutOfRangeException(nameof(patchIntervals));
            _colliderObjects = scene.Objects.Where(item => item.Enabled && item.Name != "Ground").ToArray();
            var patchesAcross = (terrainStride - 2 + patchIntervals) / patchIntervals;
            EstimatedActivationCost = checked((long)patchesAcross * patchesAcross + _colliderObjects.Length);
        }

        public ExteriorCellCoordinate Coordinate { get; }
        public SceneGraph Scene { get; }
        public int TerrainStride { get; }
        public TerrainChunkVertex[] TerrainVertices { get; }
        public long EstimatedActivationCost { get; }
        public int TerrainPatchIntervals => _patchIntervals;
        public int TerrainPatchesAcross => (TerrainStride - 2 + _patchIntervals) / _patchIntervals;
        public int TerrainPatchCount => checked(TerrainPatchesAcross * TerrainPatchesAcross);
        public int ColliderObjectCount => _colliderObjects.Length;
        public SceneObject GetColliderObject(int index) => _colliderObjects[index];
        public void Dispose() { }
    }

    private sealed class ActivationStepper : ICellActivationStepper<PreparedCell, ActiveCell>
    {
        private readonly PhysicsWorld _physics;
        private readonly CellAssetReferencePool<TerrainPatchSize, TerrainPatchTopology> _topologyPool;
        private readonly float _cellWidth;
        private List<PhysicsObjectId> _colliders = new();
        private Dictionary<TerrainPatchSize,
            CellAssetReference<TerrainPatchSize, TerrainPatchTopology>> _topologyLeases = new();
        private int _nextWorkItem;
        private bool _transferred;
        private bool _disposed;

        public ActivationStepper(PhysicsWorld physics,
            CellAssetReferencePool<TerrainPatchSize, TerrainPatchTopology> topologyPool,
            float cellWidth)
        {
            _physics = physics;
            _topologyPool = topologyPool;
            _cellWidth = cellWidth;
        }

        public int StepCount { get; private set; }
        public double LongestStepMilliseconds { get; private set; }

        public CellActivationStepResult<ActiveCell> Step(PreparedCell prepared, long maximumCost)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ActivationStepper));
            if (maximumCost < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumCost));
            var stepClock = Stopwatch.StartNew();
            try
            {
                StepCount++;
                if (_nextWorkItem < prepared.TerrainPatchCount)
                    ActivateTerrainPatch(prepared, _nextWorkItem);
                else
                    ActivateSceneCollider(prepared, _nextWorkItem - prepared.TerrainPatchCount);
                _nextWorkItem++;

                var isComplete = _nextWorkItem >= prepared.EstimatedActivationCost;
                if (!isComplete)
                    return new CellActivationStepResult<ActiveCell>(1, false, null);

                var worldTransform = CreateWorldTransform(prepared.Coordinate, _cellWidth);
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
            finally
            {
                stepClock.Stop();
                LongestStepMilliseconds = Math.Max(LongestStepMilliseconds,
                    stepClock.Elapsed.TotalMilliseconds);
            }
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
                * CreateWorldTransform(prepared.Coordinate, _cellWidth);
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
