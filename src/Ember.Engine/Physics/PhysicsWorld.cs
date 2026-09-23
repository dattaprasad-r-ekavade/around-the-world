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
    private readonly Dictionary<PhysicsObjectId, PhysicsPoseHistory> _poseHistory = new();
    private readonly Dictionary<CollidableReference, PhysicsObjectId> _objectIds = new();
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
    {
        ThrowIfDisposed();
        ValidateBox(size, nameof(size));
        ValidateFinite(center, nameof(center));

        var shape = new Box(size.X, size.Y, size.Z);
        var shapeIndex = Simulation.Shapes.Add(shape);
        var handle = Simulation.Statics.Add(new StaticDescription(
            PhysicsConversions.ToNumerics(center), shapeIndex));
        var id = NextId();
        _filters.Allocate(handle) = filter ?? PhysicsCollisionFilter.DefaultWorld;
        _objectIds.Add(new CollidableReference(handle), id);
        return id;
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
        _poseHistory.Add(id, new PhysicsPoseHistory(pose, pose));
        return id;
    }

    /// <summary>Advances BEPU by one caller-selected step. Use PhysicsFixedStepper for gameplay.</summary>
    public void Step(float seconds)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(seconds) || seconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(seconds), "Physics step must be finite and positive.");

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
