using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Trees;
using BepuUtilities;
using BepuUtilities.Collections;
using BepuUtilities.Memory;
using Ludots.Core.Layers;

namespace Ludots.Core.Physics3D;

internal sealed class Physics3DQueryEngine
{
    private readonly Simulation _simulation;
    private readonly BufferPool _pool;
    private readonly Physics3DBodyStore _bodies;

    public Physics3DQueryEngine(Simulation simulation, BufferPool pool, Physics3DBodyStore bodies)
    {
        _simulation = simulation;
        _pool = pool;
        _bodies = bodies;
    }

    public unsafe int Raycast(
        Vector3 originCm,
        Vector3 direction,
        float maximumDistanceCm,
        in LayerMask queryLayer,
        Span<Physics3DRaycastHit> hits)
    {
        ValidateLinearQuery(originCm, direction, maximumDistanceCm);
        Vector3 normalizedDirection = Vector3.Normalize(direction);
        fixed (Physics3DRaycastHit* hitPointer = hits)
        {
            var collector = new RayHitCollector(
                _bodies,
                queryLayer,
                originCm,
                normalizedDirection,
                hitPointer,
                hits.Length);
            _simulation.RayCast(originCm, normalizedDirection, maximumDistanceCm, ref collector);
            if (collector.Overflowed)
            {
                throw new Physics3DCapacityExceededException("raycast hits", hits.Length);
            }

            return collector.Count;
        }
    }

    public int BoxCast(
        Vector3 centerCm,
        Vector3 sizeCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in LayerMask queryLayer,
        Span<Physics3DShapeCastHit> hits)
    {
        ValidateBoxSize(sizeCm);
        return Sweep(
            new Box(sizeCm.X, sizeCm.Y, sizeCm.Z),
            centerCm,
            orientation,
            direction,
            maximumDistanceCm,
            queryLayer,
            hits);
    }

    public int SphereCast(
        Vector3 centerCm,
        float radiusCm,
        Vector3 direction,
        float maximumDistanceCm,
        in LayerMask queryLayer,
        Span<Physics3DShapeCastHit> hits)
    {
        Physics3DValidation.RequireFinitePositive(radiusCm, nameof(radiusCm));
        return Sweep(
            new Sphere(radiusCm),
            centerCm,
            Quaternion.Identity,
            direction,
            maximumDistanceCm,
            queryLayer,
            hits);
    }

    public int CapsuleCast(
        Vector3 centerCm,
        float radiusCm,
        float cylinderLengthCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in LayerMask queryLayer,
        Span<Physics3DShapeCastHit> hits)
    {
        ValidateCapsule(radiusCm, cylinderLengthCm);
        return Sweep(
            new Capsule(radiusCm, cylinderLengthCm),
            centerCm,
            orientation,
            direction,
            maximumDistanceCm,
            queryLayer,
            hits);
    }

    public int OverlapBox(
        Vector3 centerCm,
        Vector3 sizeCm,
        Quaternion orientation,
        in LayerMask queryLayer,
        Span<Physics3DOverlapHit> hits)
    {
        ValidateBoxSize(sizeCm);
        return Overlap(
            new Box(sizeCm.X, sizeCm.Y, sizeCm.Z),
            centerCm,
            orientation,
            queryLayer,
            hits);
    }

    public int OverlapSphere(
        Vector3 centerCm,
        float radiusCm,
        in LayerMask queryLayer,
        Span<Physics3DOverlapHit> hits)
    {
        Physics3DValidation.RequireFinitePositive(radiusCm, nameof(radiusCm));
        return Overlap(new Sphere(radiusCm), centerCm, Quaternion.Identity, queryLayer, hits);
    }

    public int OverlapCapsule(
        Vector3 centerCm,
        float radiusCm,
        float cylinderLengthCm,
        Quaternion orientation,
        in LayerMask queryLayer,
        Span<Physics3DOverlapHit> hits)
    {
        ValidateCapsule(radiusCm, cylinderLengthCm);
        return Overlap(
            new Capsule(radiusCm, cylinderLengthCm),
            centerCm,
            orientation,
            queryLayer,
            hits);
    }

    private unsafe int Sweep<TShape>(
        TShape shape,
        Vector3 centerCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in LayerMask queryLayer,
        Span<Physics3DShapeCastHit> hits)
        where TShape : unmanaged, IConvexShape
    {
        ValidateLinearQuery(centerCm, direction, maximumDistanceCm);
        Quaternion normalizedOrientation = Physics3DValidation.NormalizeOrientation(orientation, nameof(orientation));
        Vector3 normalizedDirection = Vector3.Normalize(direction);
        fixed (Physics3DShapeCastHit* hitPointer = hits)
        {
            var collector = new SweepHitCollector(_bodies, queryLayer, centerCm, hitPointer, hits.Length);
            _simulation.Sweep(
                shape,
                new RigidPose(centerCm, normalizedOrientation),
                new BodyVelocity(normalizedDirection, Vector3.Zero),
                maximumDistanceCm,
                _pool,
                ref collector);
            if (collector.Overflowed)
            {
                throw new Physics3DCapacityExceededException("shape cast hits", hits.Length);
            }

            return collector.Count;
        }
    }

    private unsafe int Overlap<TShape>(
        TShape shape,
        Vector3 centerCm,
        Quaternion orientation,
        in LayerMask queryLayer,
        Span<Physics3DOverlapHit> hits)
        where TShape : unmanaged, IConvexShape
    {
        Physics3DValidation.RequireFinite(centerCm, nameof(centerCm));
        Quaternion normalizedOrientation = Physics3DValidation.NormalizeOrientation(orientation, nameof(orientation));
        var pose = new RigidPose(centerCm, normalizedOrientation);
        shape.ComputeBounds(normalizedOrientation, out Vector3 minimum, out Vector3 maximum);
        minimum += centerCm;
        maximum += centerCm;
        fixed (Physics3DOverlapHit* hitPointer = hits)
        {
            var callbacks = new OverlapCallbacks(_bodies, hitPointer, hits.Length);
            var enumerator = new OverlapCandidateEnumerator<TShape>(
                _simulation,
                _pool,
                _bodies,
                shape,
                pose,
                queryLayer,
                callbacks);
            _simulation.BroadPhase.GetOverlaps(minimum, maximum, ref enumerator);
            enumerator.Flush(out int count, out bool overflowed);
            if (overflowed)
            {
                throw new Physics3DCapacityExceededException("overlap hits", hits.Length);
            }

            return count;
        }
    }

    private static void ValidateLinearQuery(Vector3 origin, Vector3 direction, float maximumDistance)
    {
        Physics3DValidation.RequireFinite(origin, nameof(origin));
        Physics3DValidation.RequireFinite(direction, nameof(direction));
        Physics3DValidation.RequireFinitePositive(maximumDistance, nameof(maximumDistance));
        if (!(direction.LengthSquared() > 1e-12f))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Query direction length must be greater than zero.");
        }
    }

    private static void ValidateBoxSize(Vector3 sizeCm)
    {
        Physics3DValidation.RequireFinite(sizeCm, nameof(sizeCm));
        Physics3DValidation.RequireFinitePositive(sizeCm.X, $"{nameof(sizeCm)}.X");
        Physics3DValidation.RequireFinitePositive(sizeCm.Y, $"{nameof(sizeCm)}.Y");
        Physics3DValidation.RequireFinitePositive(sizeCm.Z, $"{nameof(sizeCm)}.Z");
    }

    private static void ValidateCapsule(float radiusCm, float cylinderLengthCm)
    {
        Physics3DValidation.RequireFinitePositive(radiusCm, nameof(radiusCm));
        Physics3DValidation.RequireFiniteNonNegative(cylinderLengthCm, nameof(cylinderLengthCm));
    }

    private unsafe struct RayHitCollector : IRayHitHandler
    {
        private readonly Physics3DBodyStore _bodies;
        private readonly LayerMask _queryLayer;
        private readonly Vector3 _origin;
        private readonly Vector3 _direction;
        private readonly Physics3DRaycastHit* _hits;
        private readonly int _capacity;

        public RayHitCollector(
            Physics3DBodyStore bodies,
            LayerMask queryLayer,
            Vector3 origin,
            Vector3 direction,
            Physics3DRaycastHit* hits,
            int capacity)
        {
            _bodies = bodies;
            _queryLayer = queryLayer;
            _origin = origin;
            _direction = direction;
            _hits = hits;
            _capacity = capacity;
            Count = 0;
            Overflowed = false;
        }

        public int Count { get; private set; }
        public bool Overflowed { get; private set; }

        public bool AllowTest(CollidableReference collidable)
        {
            int slot = _bodies.RequireSlot(collidable);
            return LayerMask.Test(in _queryLayer, in _bodies.GetLayer(slot));
        }

        public bool AllowTest(CollidableReference collidable, int childIndex) => AllowTest(collidable);

        public void OnRayHit(
            in RayData ray,
            ref float maximumT,
            float t,
            in Vector3 normal,
            CollidableReference collidable,
            int childIndex)
        {
            if (!AllowTest(collidable))
            {
                return;
            }

            if (Count >= _capacity)
            {
                Overflowed = true;
                return;
            }

            int slot = _bodies.RequireSlot(collidable);
            var hit = new Physics3DRaycastHit(
                _bodies.GetId(slot),
                _bodies.GetEntity(slot),
                _origin + _direction * t,
                normal,
                t);
            int insertIndex = Count;
            while (insertIndex > 0)
            {
                ref Physics3DRaycastHit previous = ref _hits[insertIndex - 1];
                if (previous.DistanceCm < hit.DistanceCm ||
                    (previous.DistanceCm == hit.DistanceCm && previous.Body.Slot <= hit.Body.Slot))
                {
                    break;
                }

                _hits[insertIndex] = previous;
                insertIndex--;
            }

            _hits[insertIndex] = hit;
            Count++;
        }
    }

    private unsafe struct SweepHitCollector : ISweepHitHandler
    {
        private readonly Physics3DBodyStore _bodies;
        private readonly LayerMask _queryLayer;
        private readonly Vector3 _start;
        private readonly Physics3DShapeCastHit* _hits;
        private readonly int _capacity;

        public SweepHitCollector(
            Physics3DBodyStore bodies,
            LayerMask queryLayer,
            Vector3 start,
            Physics3DShapeCastHit* hits,
            int capacity)
        {
            _bodies = bodies;
            _queryLayer = queryLayer;
            _start = start;
            _hits = hits;
            _capacity = capacity;
            Count = 0;
            Overflowed = false;
        }

        public int Count { get; private set; }
        public bool Overflowed { get; private set; }

        public bool AllowTest(CollidableReference collidable)
        {
            int slot = _bodies.RequireSlot(collidable);
            return LayerMask.Test(in _queryLayer, in _bodies.GetLayer(slot));
        }

        public bool AllowTest(CollidableReference collidable, int child) => AllowTest(collidable);

        public void OnHit(
            ref float maximumT,
            float t,
            in Vector3 hitLocation,
            in Vector3 hitNormal,
            CollidableReference collidable)
        {
            Add(t, hitLocation, hitNormal, collidable, startedOverlapping: false);
        }

        public void OnHitAtZeroT(ref float maximumT, CollidableReference collidable)
        {
            Add(0f, _start, Vector3.Zero, collidable, startedOverlapping: true);
        }

        private void Add(
            float distance,
            in Vector3 position,
            in Vector3 normal,
            CollidableReference collidable,
            bool startedOverlapping)
        {
            if (Count >= _capacity)
            {
                Overflowed = true;
                return;
            }

            int slot = _bodies.RequireSlot(collidable);
            var hit = new Physics3DShapeCastHit(
                _bodies.GetId(slot),
                _bodies.GetEntity(slot),
                position,
                normal,
                distance,
                startedOverlapping);
            int insertIndex = Count;
            while (insertIndex > 0)
            {
                ref Physics3DShapeCastHit previous = ref _hits[insertIndex - 1];
                if (previous.DistanceCm < distance ||
                    (previous.DistanceCm == distance && previous.Body.Slot <= hit.Body.Slot))
                {
                    break;
                }

                _hits[insertIndex] = previous;
                insertIndex--;
            }

            _hits[insertIndex] = hit;
            Count++;
        }
    }

    private unsafe struct OverlapCallbacks : ICollisionCallbacks
    {
        private readonly Physics3DBodyStore _bodies;
        private readonly Physics3DOverlapHit* _hits;
        private readonly int _capacity;

        public OverlapCallbacks(Physics3DBodyStore bodies, Physics3DOverlapHit* hits, int capacity)
        {
            _bodies = bodies;
            _hits = hits;
            _capacity = capacity;
            Count = 0;
            Overflowed = false;
        }

        public int Count { get; private set; }
        public bool Overflowed { get; private set; }

        public bool AllowCollisionTesting(int pairId, int childA, int childB) => true;

        public void OnChildPairCompleted(int pairId, int childA, int childB, ref ConvexContactManifold manifold)
        {
        }

        public void OnPairCompleted<TManifold>(int pairId, ref TManifold manifold)
            where TManifold : unmanaged, IContactManifold<TManifold>
        {
            for (int contactIndex = 0; contactIndex < manifold.Count; contactIndex++)
            {
                if (manifold.GetDepth(ref manifold, contactIndex) >= 0f)
                {
                    Add(pairId);
                    break;
                }
            }
        }

        private void Add(int slot)
        {
            if (Count >= _capacity)
            {
                Overflowed = true;
                return;
            }

            var hit = new Physics3DOverlapHit(_bodies.GetId(slot), _bodies.GetEntity(slot));
            int insertIndex = Count;
            while (insertIndex > 0 && _hits[insertIndex - 1].Body.Slot > slot)
            {
                _hits[insertIndex] = _hits[insertIndex - 1];
                insertIndex--;
            }

            _hits[insertIndex] = hit;
            Count++;
        }
    }

    private unsafe struct OverlapCandidateEnumerator<TShape> : IBreakableForEach<CollidableReference>
        where TShape : unmanaged, IConvexShape
    {
        private readonly Simulation _simulation;
        private readonly Physics3DBodyStore _bodies;
        private readonly TShape _queryShape;
        private readonly RigidPose _queryPose;
        private readonly LayerMask _queryLayer;
        private CollisionBatcher<OverlapCallbacks> _batcher;

        public OverlapCandidateEnumerator(
            Simulation simulation,
            BufferPool pool,
            Physics3DBodyStore bodies,
            TShape queryShape,
            RigidPose queryPose,
            LayerMask queryLayer,
            OverlapCallbacks callbacks)
        {
            _simulation = simulation;
            _bodies = bodies;
            _queryShape = queryShape;
            _queryPose = queryPose;
            _queryLayer = queryLayer;
            _batcher = new CollisionBatcher<OverlapCallbacks>(
                pool,
                simulation.Shapes,
                simulation.NarrowPhase.CollisionTaskRegistry,
                0f,
                callbacks);
        }

        public bool LoopBody(CollidableReference collidable)
        {
            int slot = _bodies.RequireSlot(collidable);
            if (!LayerMask.Test(in _queryLayer, in _bodies.GetLayer(slot)))
            {
                return true;
            }

            GetPoseAndShape(collidable, out RigidPose targetPose, out TypedIndex targetShape);
            _simulation.Shapes[targetShape.Type].GetShapeData(targetShape.Index, out void* targetShapeData, out _);
            TShape queryShape = _queryShape;
            void* queryShapeData = Unsafe.AsPointer(ref queryShape);
            _batcher.CacheShapeB(
                targetShape.Type,
                queryShape.TypeId,
                queryShapeData,
                Unsafe.SizeOf<TShape>(),
                out void* cachedQueryShapeData);
            _batcher.AddDirectly(
                targetShape.Type,
                queryShape.TypeId,
                targetShapeData,
                cachedQueryShapeData,
                _queryPose.Position - targetPose.Position,
                targetPose.Orientation,
                _queryPose.Orientation,
                0f,
                new PairContinuation(slot));
            return true;
        }

        public void Flush(out int count, out bool overflowed)
        {
            _batcher.Flush();
            count = _batcher.Callbacks.Count;
            overflowed = _batcher.Callbacks.Overflowed;
        }

        private void GetPoseAndShape(
            CollidableReference collidable,
            out RigidPose pose,
            out TypedIndex shape)
        {
            if (collidable.Mobility == CollidableMobility.Static)
            {
                StaticReference reference = _simulation.Statics.GetStaticReference(collidable.StaticHandle);
                pose = reference.Pose;
                shape = reference.Shape;
            }
            else
            {
                BodyReference reference = _simulation.Bodies.GetBodyReference(collidable.BodyHandle);
                pose = reference.Pose;
                shape = reference.Collidable.Shape;
            }
        }
    }
}
