using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuPhysics.Trees;
using BepuUtilities;
using BepuUtilities.Memory;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Numerics;
using XnaQuaternion = Microsoft.Xna.Framework.Quaternion;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;
using NumericsQuaternion = System.Numerics.Quaternion;
using NumericsVector3 = System.Numerics.Vector3;

namespace Ember.Physics;

/// <summary>Owns one BEPU simulation and presents stable engine IDs and MonoGame poses.</summary>
public sealed class PhysicsWorld : IDisposable
{
    private readonly BufferPool _bufferPool = new();
    private readonly CollidableProperty<PhysicsCollisionFilter> _filters;
    private readonly Dictionary<PhysicsObjectId, BodyHandle> _dynamicBodies = new();
    private readonly Dictionary<PhysicsObjectId, TypedIndex> _dynamicShapes = new();
    private readonly Dictionary<PhysicsObjectId, StaticHandle> _staticBodies = new();
    private readonly Dictionary<PhysicsObjectId, TypedIndex> _staticShapes = new();
    private readonly Dictionary<PhysicsObjectId, PhysicsPoseHistory> _poseHistory = new();
    private readonly Dictionary<CollidableReference, PhysicsObjectId> _objectIds = new();
    private readonly List<PhysicsCharacterController> _characters = new();
    private Simulation? _simulation;
    private int _nextObjectId = 1;
    private bool _disposed;

    public PhysicsWorld(XnaVector3? gravity = null)
    {
        _filters = new CollidableProperty<PhysicsCollisionFilter>(_bufferPool);
        try
        {
            _simulation = Simulation.Create(
                _bufferPool,
                new NarrowPhaseCallbacks(_filters),
                new GravityPoseIntegratorCallbacks(PhysicsConversions.ToNumerics(gravity ?? new XnaVector3(0f, -9.81f, 0f))),
                new SolveDescription(8, 1));
        }
        catch
        {
            try { _filters.Dispose(); }
            finally { _bufferPool.Clear(); }
            throw;
        }
    }

    public bool IsDisposed => _disposed;

    public PhysicsObjectId AddStaticBox(XnaVector3 center, XnaVector3 size,
        PhysicsCollisionFilter? filter = null)
        => AddStaticBox(center, size, XnaQuaternion.Identity, filter);

    public PhysicsObjectId AddStaticBox(XnaVector3 center, XnaVector3 size,
        XnaQuaternion orientation, PhysicsCollisionFilter? filter = null)
    {
        ThrowIfDisposed();
        ValidateBox(size, nameof(size));
        ValidateFinite(center, nameof(center));
        ValidateFinite(orientation, nameof(orientation));
        if (orientation.LengthSquared() < 1e-8f)
            throw new ArgumentOutOfRangeException(nameof(orientation), "Box orientation must be nonzero.");

        var shape = new Box(size.X, size.Y, size.Z);
        var shapeIndex = Simulation.Shapes.Add(shape);
        var handle = Simulation.Statics.Add(new StaticDescription(
            PhysicsConversions.ToNumerics(center),
            PhysicsConversions.ToNumerics(XnaQuaternion.Normalize(orientation)), shapeIndex));
        var id = NextId();
        _filters.Allocate(handle) = filter ?? PhysicsCollisionFilter.DefaultWorld;
        _objectIds.Add(new CollidableReference(handle), id);
        _staticBodies.Add(id, handle);
        _staticShapes.Add(id, shapeIndex);
        return id;
    }

    /// <summary>Adds a removable static triangle mesh, for example a streamed terrain cell.</summary>
    public PhysicsObjectId AddStaticTriangleMesh(IReadOnlyList<XnaVector3> vertices,
        IReadOnlyList<int> triangleIndices, PhysicsCollisionFilter? filter = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(triangleIndices);
        if (vertices.Count < 3)
            throw new ArgumentException("A static triangle mesh needs at least three vertices.", nameof(vertices));
        if (triangleIndices.Count < 3 || triangleIndices.Count % 3 != 0)
            throw new ArgumentException("Triangle indices must contain one or more complete triangles.", nameof(triangleIndices));
        foreach (var vertex in vertices) ValidateFinite(vertex, nameof(vertices));

        var triangleCount = triangleIndices.Count / 3;
        _bufferPool.Take(triangleCount, out Buffer<Triangle> triangles);
        var bufferOwned = true;
        Mesh mesh = default;
        var meshCreated = false;
        TypedIndex shapeIndex = default;
        var shapeAdded = false;
        StaticHandle staticHandle = default;
        var staticAdded = false;
        PhysicsObjectId id = default;
        var idAllocated = false;
        try
        {
            for (var triangle = 0; triangle < triangleCount; triangle++)
            {
                var offset = triangle * 3;
                var a = triangleIndices[offset];
                var b = triangleIndices[offset + 1];
                var c = triangleIndices[offset + 2];
                ValidateVertexIndex(a, vertices.Count, nameof(triangleIndices));
                ValidateVertexIndex(b, vertices.Count, nameof(triangleIndices));
                ValidateVertexIndex(c, vertices.Count, nameof(triangleIndices));
                triangles[triangle] = new Triangle(
                    PhysicsConversions.ToNumerics(vertices[a]),
                    PhysicsConversions.ToNumerics(vertices[b]),
                    PhysicsConversions.ToNumerics(vertices[c]));
            }

            mesh = new Mesh(triangles, NumericsVector3.One, _bufferPool, null);
            meshCreated = true;
            bufferOwned = false;
            shapeIndex = Simulation.Shapes.Add(mesh);
            shapeAdded = true;
            staticHandle = Simulation.Statics.Add(new StaticDescription(
                NumericsVector3.Zero, NumericsQuaternion.Identity, shapeIndex));
            staticAdded = true;
            id = NextId();
            idAllocated = true;
            _filters.Allocate(staticHandle) = filter ?? PhysicsCollisionFilter.DefaultWorld;
            _objectIds.Add(new CollidableReference(staticHandle), id);
            _staticBodies.Add(id, staticHandle);
            _staticShapes.Add(id, shapeIndex);
            return id;
        }
        catch
        {
            if (idAllocated)
            {
                _staticBodies.Remove(id);
                _staticShapes.Remove(id);
            }
            if (staticAdded)
            {
                _objectIds.Remove(new CollidableReference(staticHandle));
                Simulation.Statics.Remove(staticHandle);
            }
            if (shapeAdded)
                Simulation.Shapes.RemoveAndDispose(shapeIndex, _bufferPool);
            else if (meshCreated)
                mesh.Dispose(_bufferPool);
            else if (bufferOwned)
                _bufferPool.Return(ref triangles);
            throw;
        }
    }

    /// <summary>Removes a static box or triangle mesh and releases its uniquely owned shape.</summary>
    public void RemoveStatic(PhysicsObjectId id)
    {
        ThrowIfDisposed();
        if (!_staticBodies.Remove(id, out var handle))
            throw new KeyNotFoundException($"Physics object {id.Value} is not a removable static body.");

        _objectIds.Remove(new CollidableReference(handle));
        Simulation.Statics.Remove(handle);
        if (_staticShapes.Remove(id, out var shapeIndex))
            Simulation.Shapes.RemoveAndDispose(shapeIndex, _bufferPool);
    }

    public PhysicsObjectId AddDynamicBox(XnaVector3 position, XnaVector3 size, float mass,
        PhysicsCollisionFilter? filter = null)
    {
        ThrowIfDisposed();
        ValidateBox(size, nameof(size));
        ValidateFinite(position, nameof(position));
        if (!float.IsFinite(mass) || mass <= 0f)
            throw new ArgumentOutOfRangeException(nameof(mass), "Dynamic box mass must be finite and positive.");

        var shape = new Box(size.X, size.Y, size.Z);
        var shapeIndex = Simulation.Shapes.Add(shape);
        var handle = Simulation.Bodies.Add(BodyDescription.CreateDynamic(
            PhysicsConversions.ToNumerics(position), shape.ComputeInertia(mass), shapeIndex, 0.01f));
        var id = NextId();
        _dynamicBodies.Add(id, handle);
        _filters.Allocate(handle) = filter ?? PhysicsCollisionFilter.DefaultDynamic;
        var collidable = Simulation.Bodies[handle].CollidableReference;
        _objectIds.Add(collidable, id);

        var pose = ToPhysicsPose(Simulation.Bodies[handle].Pose);
        _dynamicShapes.Add(id, shapeIndex);
        _poseHistory.Add(id, new PhysicsPoseHistory(pose, pose));
        return id;
    }

    /// <summary>Adds an upright dynamic capsule suitable for a player character.</summary>
    public PhysicsObjectId AddDynamicCapsule(XnaVector3 position, float radius, float cylinderLength,
        float mass, PhysicsCollisionFilter? filter = null)
    {
        ThrowIfDisposed();
        ValidateFinite(position, nameof(position));
        if (!float.IsFinite(radius) || radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius), "Capsule radius must be finite and positive.");
        if (!float.IsFinite(cylinderLength) || cylinderLength < 0f)
            throw new ArgumentOutOfRangeException(nameof(cylinderLength), "Capsule cylinder length must be finite and nonnegative.");
        if (!float.IsFinite(mass) || mass <= 0f)
            throw new ArgumentOutOfRangeException(nameof(mass), "Dynamic capsule mass must be finite and positive.");

        var shape = new Capsule(radius, cylinderLength);
        var shapeIndex = Simulation.Shapes.Add(shape);
        var inertia = shape.ComputeInertia(mass);
        inertia.InverseInertiaTensor = default;
        var handle = Simulation.Bodies.Add(BodyDescription.CreateDynamic(
            PhysicsConversions.ToNumerics(position), inertia, shapeIndex, 0.01f));
        var id = NextId();
        _dynamicBodies.Add(id, handle);
        _dynamicShapes.Add(id, shapeIndex);
        _filters.Allocate(handle) = filter ?? new PhysicsCollisionFilter(
            PhysicsCollisionLayer.Player, PhysicsCollisionLayer.World | PhysicsCollisionLayer.Dynamic);
        var collidable = Simulation.Bodies[handle].CollidableReference;
        _objectIds.Add(collidable, id);

        var pose = ToPhysicsPose(Simulation.Bodies[handle].Pose);
        _poseHistory.Add(id, new PhysicsPoseHistory(pose, pose));
        return id;
    }

    /// <summary>Removes a dynamic body and releases its uniquely owned shape.</summary>
    internal void RemoveDynamicBody(PhysicsObjectId id)
    {
        ThrowIfDisposed();
        if (!_dynamicBodies.Remove(id, out var handle))
            throw new KeyNotFoundException($"Physics object {id.Value} is not a dynamic body.");

        _objectIds.Remove(Simulation.Bodies[handle].CollidableReference);
        _poseHistory.Remove(id);
        Simulation.Bodies.Remove(handle);
        if (_dynamicShapes.Remove(id, out var shapeIndex))
            Simulation.Shapes.RemoveAndDispose(shapeIndex, _bufferPool);
    }

    internal XnaVector3 GetLinearVelocity(PhysicsObjectId id)
    {
        ThrowIfDisposed();
        if (!_dynamicBodies.TryGetValue(id, out var handle))
            throw new KeyNotFoundException($"Physics object {id.Value} is not a dynamic body.");
        return PhysicsConversions.ToXna(Simulation.Bodies[handle].Velocity.Linear);
    }

    internal void SetLinearVelocity(PhysicsObjectId id, XnaVector3 velocity)
    {
        ThrowIfDisposed();
        ValidateFinite(velocity, nameof(velocity));
        if (!_dynamicBodies.TryGetValue(id, out var handle))
            throw new KeyNotFoundException($"Physics object {id.Value} is not a dynamic body.");
        Simulation.Bodies[handle].Velocity.Linear = PhysicsConversions.ToNumerics(velocity);
    }

    internal void RegisterCharacter(PhysicsCharacterController character)
    {
        ThrowIfDisposed();
        _characters.Add(character);
    }

    internal void UnregisterCharacter(PhysicsCharacterController character) => _characters.Remove(character);

    /// <summary>Advances BEPU by one caller-selected step. Use PhysicsFixedStepper for gameplay.</summary>
    public void Step(float seconds)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(seconds) || seconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(seconds), "Physics step must be finite and positive.");

        foreach (var character in _characters) character.PreparePhysicsStep(seconds);

        foreach (var (id, handle) in _dynamicBodies)
        {
            var history = _poseHistory[id];
            history.Previous = history.Current;
            _poseHistory[id] = history;
        }

        Simulation.Timestep(seconds);

        foreach (var (id, handle) in _dynamicBodies)
        {
            var history = _poseHistory[id];
            history.Current = ToPhysicsPose(Simulation.Bodies[handle].Pose);
            _poseHistory[id] = history;
        }

        foreach (var character in _characters) character.CompletePhysicsStep();
    }

    public PhysicsPose GetPose(PhysicsObjectId id)
    {
        ThrowIfDisposed();
        if (!_dynamicBodies.TryGetValue(id, out var handle))
            throw new KeyNotFoundException($"Physics object {id.Value} is not a dynamic body.");
        return ToPhysicsPose(Simulation.Bodies[handle].Pose);
    }

    /// <summary>Interpolates between the previous and current fixed-step poses for rendering.</summary>
    public PhysicsPose GetInterpolatedPose(PhysicsObjectId id, float alpha)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(alpha)) throw new ArgumentOutOfRangeException(nameof(alpha));
        if (!_poseHistory.TryGetValue(id, out var history))
            throw new KeyNotFoundException($"Physics object {id.Value} is not a dynamic body.");

        alpha = Math.Clamp(alpha, 0f, 1f);
        return new PhysicsPose(
            XnaVector3.Lerp(history.Previous.Position, history.Current.Position, alpha),
            XnaQuaternion.Slerp(history.Previous.Orientation, history.Current.Orientation, alpha));
    }

    /// <summary>Returns the nearest hit whose category matches the supplied layer mask.</summary>
    public PhysicsRaycastHit? Raycast(XnaVector3 origin, XnaVector3 direction, float maxDistance,
        PhysicsCollisionLayer layerMask = PhysicsCollisionLayer.All)
    {
        ThrowIfDisposed();
        ValidateFinite(origin, nameof(origin));
        ValidateFinite(direction, nameof(direction));
        if (direction.LengthSquared() < 1e-8f)
            throw new ArgumentOutOfRangeException(nameof(direction), "Ray direction must be nonzero.");
        if (!float.IsFinite(maxDistance) || maxDistance <= 0f)
            throw new ArgumentOutOfRangeException(nameof(maxDistance), "Ray distance must be finite and positive.");

        var normalizedDirection = XnaVector3.Normalize(direction);
        var handler = new RayHitHandler(_filters, _objectIds, layerMask);
        Simulation.RayCast(
            PhysicsConversions.ToNumerics(origin),
            PhysicsConversions.ToNumerics(normalizedDirection),
            maxDistance,
            _bufferPool,
            ref handler);
        if (handler.Hit is not { } hit) return null;

        return new PhysicsRaycastHit(
            hit.ObjectId,
            origin + normalizedDirection * hit.Distance,
            PhysicsConversions.ToXna(hit.Normal),
            hit.Distance,
            hit.Layer);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var character in _characters) character.OnWorldDisposed();
        _characters.Clear();
        try { _simulation?.Dispose(); }
        finally
        {
            _simulation = null;
            try { _filters.Dispose(); }
            finally
            {
                try { _bufferPool.Clear(); }
                finally
                {
                    _dynamicBodies.Clear();
                    _dynamicShapes.Clear();
                    _staticBodies.Clear();
                    _staticShapes.Clear();
                    _poseHistory.Clear();
                    _objectIds.Clear();
                }
            }
        }
    }

    private Simulation Simulation => _simulation
        ?? throw new ObjectDisposedException(nameof(PhysicsWorld));

    private PhysicsObjectId NextId() => new(_nextObjectId++);

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PhysicsWorld));
    }

    private static void ValidateBox(XnaVector3 size, string parameterName)
    {
        ValidateFinite(size, parameterName);
        if (size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
            throw new ArgumentOutOfRangeException(parameterName, "Box dimensions must be positive.");
    }

    private static void ValidateFinite(XnaVector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(parameterName, "Vector components must be finite.");
    }

    private static void ValidateVertexIndex(int index, int vertexCount, string parameterName)
    {
        if ((uint)index >= (uint)vertexCount)
            throw new ArgumentOutOfRangeException(parameterName, $"Triangle vertex index {index} is outside the mesh.");
    }

    private static void ValidateFinite(XnaQuaternion value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y)
            || !float.IsFinite(value.Z) || !float.IsFinite(value.W))
            throw new ArgumentOutOfRangeException(parameterName, "Quaternion components must be finite.");
    }

    private static PhysicsPose ToPhysicsPose(in RigidPose pose) => new(
        PhysicsConversions.ToXna(pose.Position), PhysicsConversions.ToXna(pose.Orientation));

    private struct PhysicsPoseHistory
    {
        public PhysicsPoseHistory(PhysicsPose previous, PhysicsPose current)
        {
            Previous = previous;
            Current = current;
        }

        public PhysicsPose Previous;
        public PhysicsPose Current;
    }

    private struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
    {
        private readonly CollidableProperty<PhysicsCollisionFilter> _filters;

        public NarrowPhaseCallbacks(CollidableProperty<PhysicsCollisionFilter> filters) => _filters = filters;

        public void Initialize(Simulation simulation) => _filters.Initialize(simulation);

        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b,
            ref float speculativeMargin)
        {
            if (a.Mobility != CollidableMobility.Dynamic && b.Mobility != CollidableMobility.Dynamic)
                return false;
            return _filters[a].AllowsCollisionWith(_filters[b]);
        }

        public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;

        public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair,
            ref TManifold manifold, out PairMaterialProperties pairMaterial)
            where TManifold : unmanaged, IContactManifold<TManifold>
        {
            pairMaterial.FrictionCoefficient = 0.8f;
            pairMaterial.MaximumRecoveryVelocity = 2f;
            pairMaterial.SpringSettings = new SpringSettings(30f, 1f);
            return true;
        }

        public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA,
            int childIndexB, ref ConvexContactManifold manifold) => true;

        public void Dispose() { }
    }

    private struct GravityPoseIntegratorCallbacks : IPoseIntegratorCallbacks
    {
        private readonly NumericsVector3 _gravity;
        private Vector3Wide _gravityWideDt;

        public GravityPoseIntegratorCallbacks(NumericsVector3 gravity) => _gravity = gravity;

        public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
        public readonly bool AllowSubstepsForUnconstrainedBodies => false;
        public readonly bool IntegrateVelocityForKinematics => false;

        public void Initialize(Simulation simulation) { }

        public void PrepareForIntegration(float dt) =>
            _gravityWideDt = Vector3Wide.Broadcast(_gravity * dt);

        public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position,
            QuaternionWide orientation, BodyInertiaWide localInertia, Vector<int> integrationMask,
            int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity) =>
            velocity.Linear += _gravityWideDt;
    }

    private struct RayHitHandler : IRayHitHandler
    {
        private readonly CollidableProperty<PhysicsCollisionFilter> _filters;
        private readonly Dictionary<CollidableReference, PhysicsObjectId> _objectIds;
        private readonly PhysicsCollisionLayer _layerMask;

        public RayHitHandler(CollidableProperty<PhysicsCollisionFilter> filters,
            Dictionary<CollidableReference, PhysicsObjectId> objectIds, PhysicsCollisionLayer layerMask)
        {
            _filters = filters;
            _objectIds = objectIds;
            _layerMask = layerMask;
            Hit = null;
        }

        public (PhysicsObjectId ObjectId, NumericsVector3 Normal, float Distance,
            PhysicsCollisionLayer Layer)? Hit { get; private set; }

        public bool AllowTest(CollidableReference collidable) =>
            _objectIds.ContainsKey(collidable) && _filters[collidable].IsIncludedIn(_layerMask);

        public bool AllowTest(CollidableReference collidable, int childIndex) => AllowTest(collidable);

        public void OnRayHit(in RayData ray, ref float maximumT, float t, NumericsVector3 normal,
            CollidableReference collidable, int childIndex)
        {
            if (!_objectIds.TryGetValue(collidable, out var id)) return;
            maximumT = t;
            var filter = _filters[collidable];
            Hit = (id, normal, t, filter.BelongsTo);
        }
    }
}
