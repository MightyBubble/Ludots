using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Trees;
using BepuUtilities;
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
        in Physics3DQueryFilter filter,
        Span<Physics3DRaycastHit> hits)
    {
        PreparedQueryFilter prepared = PrepareFilter(in filter);
        ValidateLinearQuery(originCm, direction, maximumDistanceCm);
        Vector3 normalizedDirection = Vector3.Normalize(direction);
        fixed (Physics3DRaycastHit* hitPointer = hits)
        {
            var collector = new RayHitCollector(
                _bodies,
                prepared,
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

    public bool RaycastClosest(
        Vector3 originCm,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter,
        out Physics3DRaycastHit hit)
    {
        PreparedQueryFilter prepared = PrepareFilter(in filter);
        ValidateLinearQuery(originCm, direction, maximumDistanceCm);
        Vector3 normalizedDirection = Vector3.Normalize(direction);
        var collector = new ClosestRayHitCollector(_bodies, prepared, originCm, normalizedDirection);
        _simulation.RayCast(originCm, normalizedDirection, maximumDistanceCm, ref collector);
        hit = collector.Hit;
        return collector.HasHit;
    }

    public bool RaycastAny(
        Vector3 originCm,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter)
    {
        PreparedQueryFilter prepared = PrepareFilter(in filter);
        ValidateLinearQuery(originCm, direction, maximumDistanceCm);
        Vector3 normalizedDirection = Vector3.Normalize(direction);
        var collector = new AnyRayHitCollector(_bodies, prepared);
        _simulation.RayCast(originCm, normalizedDirection, maximumDistanceCm, ref collector);
        return collector.HasHit;
    }

    public unsafe void RaycastClosestBatch(
        ReadOnlySpan<Physics3DRaycastQuery> requests,
        in Physics3DQueryFilter filter,
        Span<Physics3DBatchedRaycastClosestResult> results)
    {
        if (requests.Length != results.Length)
        {
            throw new ArgumentException(
                $"RaycastClosestBatch requires equal request/result lengths (requests={requests.Length}, results={results.Length}).",
                nameof(results));
        }

        PreparedQueryFilter prepared = PrepareFilter(in filter);
        results.Clear();
        if (requests.Length == 0)
        {
            return;
        }

        fixed (Physics3DBatchedRaycastClosestResult* resultPointer = results)
        {
            var handler = new BatchedClosestRayHitHandler(_bodies, prepared, resultPointer, results.Length);
            var batcher = new SimulationRayBatcher<BatchedClosestRayHitHandler>(_pool, _simulation, handler, requests.Length);
            try
            {
                for (int i = 0; i < requests.Length; i++)
                {
                    Physics3DRaycastQuery request = requests[i];
                    ValidateLinearQuery(request.OriginCm, request.Direction, request.MaximumDistanceCm);
                    Vector3 origin = request.OriginCm;
                    Vector3 direction = Vector3.Normalize(request.Direction);
                    batcher.Add(ref origin, ref direction, request.MaximumDistanceCm, i);
                }

                batcher.Flush();
            }
            finally
            {
                batcher.Dispose();
            }
        }
    }

    public int BoxCast(
        Vector3 centerCm,
        Vector3 sizeCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter,
        Span<Physics3DShapeCastHit> hits)
    {
        ValidateBoxSize(sizeCm);
        return Sweep(
            new Box(sizeCm.X, sizeCm.Y, sizeCm.Z),
            centerCm,
            orientation,
            direction,
            maximumDistanceCm,
            filter,
            hits);
    }

    public bool BoxCastClosest(
        Vector3 centerCm,
        Vector3 sizeCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter,
        out Physics3DShapeCastHit hit)
    {
        ValidateBoxSize(sizeCm);
        return SweepClosest(
            new Box(sizeCm.X, sizeCm.Y, sizeCm.Z),
            centerCm,
            orientation,
            direction,
            maximumDistanceCm,
            filter,
            out hit);
    }

    public bool BoxCastAny(
        Vector3 centerCm,
        Vector3 sizeCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter)
    {
        ValidateBoxSize(sizeCm);
        return SweepAny(
            new Box(sizeCm.X, sizeCm.Y, sizeCm.Z),
            centerCm,
            orientation,
            direction,
            maximumDistanceCm,
            filter);
    }

    public int SphereCast(
        Vector3 centerCm,
        float radiusCm,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter,
        Span<Physics3DShapeCastHit> hits)
    {
        Physics3DValidation.RequireFinitePositive(radiusCm, nameof(radiusCm));
        return Sweep(
            new Sphere(radiusCm),
            centerCm,
            Quaternion.Identity,
            direction,
            maximumDistanceCm,
            filter,
            hits);
    }

    public bool SphereCastClosest(
        Vector3 centerCm,
        float radiusCm,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter,
        out Physics3DShapeCastHit hit)
    {
        Physics3DValidation.RequireFinitePositive(radiusCm, nameof(radiusCm));
        return SweepClosest(
            new Sphere(radiusCm),
            centerCm,
            Quaternion.Identity,
            direction,
            maximumDistanceCm,
            filter,
            out hit);
    }

    public bool SphereCastAny(
        Vector3 centerCm,
        float radiusCm,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter)
    {
        Physics3DValidation.RequireFinitePositive(radiusCm, nameof(radiusCm));
        return SweepAny(
            new Sphere(radiusCm),
            centerCm,
            Quaternion.Identity,
            direction,
            maximumDistanceCm,
            filter);
    }

    public int CapsuleCast(
        Vector3 centerCm,
        float radiusCm,
        float cylinderLengthCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter,
        Span<Physics3DShapeCastHit> hits)
    {
        ValidateCapsule(radiusCm, cylinderLengthCm);
        return Sweep(
            new Capsule(radiusCm, cylinderLengthCm),
            centerCm,
            orientation,
            direction,
            maximumDistanceCm,
            filter,
            hits);
    }

    public bool CapsuleCastClosest(
        Vector3 centerCm,
        float radiusCm,
        float cylinderLengthCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter,
        out Physics3DShapeCastHit hit)
    {
        ValidateCapsule(radiusCm, cylinderLengthCm);
        return SweepClosest(
            new Capsule(radiusCm, cylinderLengthCm),
            centerCm,
            orientation,
            direction,
            maximumDistanceCm,
            filter,
            out hit);
    }

    public bool CapsuleCastAny(
        Vector3 centerCm,
        float radiusCm,
        float cylinderLengthCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter)
    {
        ValidateCapsule(radiusCm, cylinderLengthCm);
        return SweepAny(
            new Capsule(radiusCm, cylinderLengthCm),
            centerCm,
            orientation,
            direction,
            maximumDistanceCm,
            filter);
    }

    /// <summary>
    /// Overlap hits are ordered by ascending <see cref="Physics3DBodyId.Slot"/> after collection.
    /// Capacity overflow throws <see cref="Physics3DCapacityExceededException"/> and never returns a silent truncated set.
    /// </summary>
    public int OverlapBox(
        Vector3 centerCm,
        Vector3 sizeCm,
        Quaternion orientation,
        in Physics3DQueryFilter filter,
        Span<Physics3DOverlapHit> hits)
    {
        ValidateBoxSize(sizeCm);
        return Overlap(
            new Box(sizeCm.X, sizeCm.Y, sizeCm.Z),
            centerCm,
            orientation,
            filter,
            hits);
    }

    public int OverlapSphere(
        Vector3 centerCm,
        float radiusCm,
        in Physics3DQueryFilter filter,
        Span<Physics3DOverlapHit> hits)
    {
        Physics3DValidation.RequireFinitePositive(radiusCm, nameof(radiusCm));
        return Overlap(new Sphere(radiusCm), centerCm, Quaternion.Identity, filter, hits);
    }

    public int OverlapCapsule(
        Vector3 centerCm,
        float radiusCm,
        float cylinderLengthCm,
        Quaternion orientation,
        in Physics3DQueryFilter filter,
        Span<Physics3DOverlapHit> hits)
    {
        ValidateCapsule(radiusCm, cylinderLengthCm);
        return Overlap(
            new Capsule(radiusCm, cylinderLengthCm),
            centerCm,
            orientation,
            filter,
            hits);
    }

    private unsafe int Sweep<TShape>(
        TShape shape,
        Vector3 centerCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter,
        Span<Physics3DShapeCastHit> hits)
        where TShape : unmanaged, IConvexShape
    {
        PreparedQueryFilter prepared = PrepareFilter(in filter);
        ValidateLinearQuery(centerCm, direction, maximumDistanceCm);
        Quaternion normalizedOrientation = Physics3DValidation.NormalizeOrientation(orientation, nameof(orientation));
        Vector3 normalizedDirection = Vector3.Normalize(direction);
        fixed (Physics3DShapeCastHit* hitPointer = hits)
        {
            var collector = new SweepHitCollector(_bodies, prepared, centerCm, hitPointer, hits.Length);
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

    private bool SweepClosest<TShape>(
        TShape shape,
        Vector3 centerCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter,
        out Physics3DShapeCastHit hit)
        where TShape : unmanaged, IConvexShape
    {
        PreparedQueryFilter prepared = PrepareFilter(in filter);
        ValidateLinearQuery(centerCm, direction, maximumDistanceCm);
        Quaternion normalizedOrientation = Physics3DValidation.NormalizeOrientation(orientation, nameof(orientation));
        Vector3 normalizedDirection = Vector3.Normalize(direction);
        var collector = new ClosestSweepHitCollector(_bodies, prepared, centerCm);
        _simulation.Sweep(
            shape,
            new RigidPose(centerCm, normalizedOrientation),
            new BodyVelocity(normalizedDirection, Vector3.Zero),
            maximumDistanceCm,
            _pool,
            ref collector);
        hit = collector.Hit;
        return collector.HasHit;
    }

    private bool SweepAny<TShape>(
        TShape shape,
        Vector3 centerCm,
        Quaternion orientation,
        Vector3 direction,
        float maximumDistanceCm,
        in Physics3DQueryFilter filter)
        where TShape : unmanaged, IConvexShape
    {
        PreparedQueryFilter prepared = PrepareFilter(in filter);
        ValidateLinearQuery(centerCm, direction, maximumDistanceCm);
        Quaternion normalizedOrientation = Physics3DValidation.NormalizeOrientation(orientation, nameof(orientation));
        Vector3 normalizedDirection = Vector3.Normalize(direction);
        var collector = new AnySweepHitCollector(_bodies, prepared);
        _simulation.Sweep(
            shape,
            new RigidPose(centerCm, normalizedOrientation),
            new BodyVelocity(normalizedDirection, Vector3.Zero),
            maximumDistanceCm,
            _pool,
            ref collector);
        return collector.HasHit;
    }

    private unsafe int Overlap<TShape>(
        TShape shape,
        Vector3 centerCm,
        Quaternion orientation,
        in Physics3DQueryFilter filter,
        Span<Physics3DOverlapHit> hits)
        where TShape : unmanaged, IConvexShape
    {
        PreparedQueryFilter prepared = PrepareFilter(in filter);
        Physics3DValidation.RequireFinite(centerCm, nameof(centerCm));
        Quaternion normalizedOrientation = Physics3DValidation.NormalizeOrientation(orientation, nameof(orientation));
        var pose = new RigidPose(centerCm, normalizedOrientation);
        shape.ComputeBounds(normalizedOrientation, out Vector3 minimum, out Vector3 maximum);
        minimum += centerCm;
        maximum += centerCm;
        fixed (Physics3DOverlapHit* hitPointer = hits)
        {
            var callbacks = new OverlapCallbacks(_bodies, prepared, hitPointer, hits.Length);
            var enumerator = new OverlapCandidateEnumerator<TShape>(
                _simulation,
                _pool,
                _bodies,
                shape,
                pose,
                prepared,
                callbacks);
            _simulation.BroadPhase.GetOverlaps(minimum, maximum, ref enumerator);
            enumerator.Flush(out int count, out bool overflowed);
            if (overflowed)
            {
                throw new Physics3DCapacityExceededException("overlap hits", hits.Length);
            }

            // Overlap results are unordered: append-only collection avoids O(n^2) insertion.
            return count;
        }
    }

    private PreparedQueryFilter PrepareFilter(in Physics3DQueryFilter filter)
    {
        // IncludeSensors is rejected at Physics3DQueryFilter construction; keep the invariant explicit here.
        if (filter.IncludeSensors)
        {
            throw new NotSupportedException(
                "Physics3D sensors are not implemented; Physics3DQueryFilter.IncludeSensors must be false until sensor colliders exist.");
        }

        int ignoredSlot = -1;
        if (filter.IgnoredBody.IsValid)
        {
            ignoredSlot = _bodies.RequireSlot(filter.IgnoredBody);
        }

        return new PreparedQueryFilter(filter.LayerMask, ignoredSlot);
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

    private readonly struct PreparedQueryFilter
    {
        public PreparedQueryFilter(LayerMask queryLayer, int ignoredSlot)
        {
            QueryLayer = queryLayer;
            IgnoredSlot = ignoredSlot;
        }

        public LayerMask QueryLayer { get; }
        public int IgnoredSlot { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Allow(Physics3DBodyStore bodies, CollidableReference collidable)
        {
            int slot = bodies.RequireSlot(collidable);
            if (IgnoredSlot >= 0 && slot == IgnoredSlot)
            {
                return false;
            }

            LayerMask queryLayer = QueryLayer;
            LayerMask targetLayer = bodies.GetLayer(slot);
            return LayerMask.Test(in queryLayer, in targetLayer);
        }
    }

    private unsafe struct RayHitCollector : IRayHitHandler
    {
        private readonly Physics3DBodyStore _bodies;
        private readonly PreparedQueryFilter _filter;
        private readonly Vector3 _origin;
        private readonly Vector3 _direction;
        private readonly Physics3DRaycastHit* _hits;
        private readonly int _capacity;

        public RayHitCollector(
            Physics3DBodyStore bodies,
            PreparedQueryFilter filter,
            Vector3 origin,
            Vector3 direction,
            Physics3DRaycastHit* hits,
            int capacity)
        {
            _bodies = bodies;
            _filter = filter;
            _origin = origin;
            _direction = direction;
            _hits = hits;
            _capacity = capacity;
            Count = 0;
            Overflowed = false;
        }

        public int Count { get; private set; }
        public bool Overflowed { get; private set; }

        public bool AllowTest(CollidableReference collidable) => _filter.Allow(_bodies, collidable);
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

    private struct ClosestRayHitCollector : IRayHitHandler
    {
        private readonly Physics3DBodyStore _bodies;
        private readonly PreparedQueryFilter _filter;
        private readonly Vector3 _origin;
        private readonly Vector3 _direction;

        public ClosestRayHitCollector(
            Physics3DBodyStore bodies,
            PreparedQueryFilter filter,
            Vector3 origin,
            Vector3 direction)
        {
            _bodies = bodies;
            _filter = filter;
            _origin = origin;
            _direction = direction;
            HasHit = false;
            Hit = default;
        }

        public bool HasHit { get; private set; }
        public Physics3DRaycastHit Hit { get; private set; }

        public bool AllowTest(CollidableReference collidable) => _filter.Allow(_bodies, collidable);
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

            int slot = _bodies.RequireSlot(collidable);
            if (HasHit &&
                (t > Hit.DistanceCm || (t == Hit.DistanceCm && slot >= Hit.Body.Slot)))
            {
                return;
            }

            maximumT = t;
            Hit = new Physics3DRaycastHit(
                _bodies.GetId(slot),
                _bodies.GetEntity(slot),
                _origin + _direction * t,
                normal,
                t);
            HasHit = true;
        }
    }

    private struct AnyRayHitCollector : IRayHitHandler
    {
        private readonly Physics3DBodyStore _bodies;
        private readonly PreparedQueryFilter _filter;

        public AnyRayHitCollector(Physics3DBodyStore bodies, PreparedQueryFilter filter)
        {
            _bodies = bodies;
            _filter = filter;
            HasHit = false;
        }

        public bool HasHit { get; private set; }

        public bool AllowTest(CollidableReference collidable)
            => !HasHit && _filter.Allow(_bodies, collidable);

        public bool AllowTest(CollidableReference collidable, int childIndex) => AllowTest(collidable);

        public void OnRayHit(
            in RayData ray,
            ref float maximumT,
            float t,
            in Vector3 normal,
            CollidableReference collidable,
            int childIndex)
        {
            if (HasHit || !AllowTest(collidable))
            {
                return;
            }

            HasHit = true;
            maximumT = 0f;
        }
    }

    private unsafe struct BatchedClosestRayHitHandler : IRayHitHandler
    {
        private readonly Physics3DBodyStore _bodies;
        private readonly PreparedQueryFilter _filter;
        private readonly Physics3DBatchedRaycastClosestResult* _results;
        private readonly int _capacity;

        public BatchedClosestRayHitHandler(
            Physics3DBodyStore bodies,
            PreparedQueryFilter filter,
            Physics3DBatchedRaycastClosestResult* results,
            int capacity)
        {
            _bodies = bodies;
            _filter = filter;
            _results = results;
            _capacity = capacity;
        }

        public bool AllowTest(CollidableReference collidable) => _filter.Allow(_bodies, collidable);
        public bool AllowTest(CollidableReference collidable, int childIndex) => AllowTest(collidable);

        public void OnRayHit(
            in RayData ray,
            ref float maximumT,
            float t,
            in Vector3 normal,
            CollidableReference collidable,
            int childIndex)
        {
            if ((uint)ray.Id >= (uint)_capacity || !AllowTest(collidable))
            {
                return;
            }

            int slot = _bodies.RequireSlot(collidable);
            ref Physics3DBatchedRaycastClosestResult current = ref _results[ray.Id];
            if (current.Hit &&
                (t > current.Value.DistanceCm || (t == current.Value.DistanceCm && slot >= current.Value.Body.Slot)))
            {
                return;
            }

            maximumT = t;
            var hit = new Physics3DRaycastHit(
                _bodies.GetId(slot),
                _bodies.GetEntity(slot),
                ray.Origin + ray.Direction * t,
                normal,
                t);
            current = new Physics3DBatchedRaycastClosestResult(hit: true, hit);
        }
    }

    private unsafe struct SweepHitCollector : ISweepHitHandler
    {
        private readonly Physics3DBodyStore _bodies;
        private readonly PreparedQueryFilter _filter;
        private readonly Vector3 _start;
        private readonly Physics3DShapeCastHit* _hits;
        private readonly int _capacity;

        public SweepHitCollector(
            Physics3DBodyStore bodies,
            PreparedQueryFilter filter,
            Vector3 start,
            Physics3DShapeCastHit* hits,
            int capacity)
        {
            _bodies = bodies;
            _filter = filter;
            _start = start;
            _hits = hits;
            _capacity = capacity;
            Count = 0;
            Overflowed = false;
        }

        public int Count { get; private set; }
        public bool Overflowed { get; private set; }

        public bool AllowTest(CollidableReference collidable) => _filter.Allow(_bodies, collidable);
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

    private struct ClosestSweepHitCollector : ISweepHitHandler
    {
        private readonly Physics3DBodyStore _bodies;
        private readonly PreparedQueryFilter _filter;
        private readonly Vector3 _start;

        public ClosestSweepHitCollector(Physics3DBodyStore bodies, PreparedQueryFilter filter, Vector3 start)
        {
            _bodies = bodies;
            _filter = filter;
            _start = start;
            HasHit = false;
            Hit = default;
        }

        public bool HasHit { get; private set; }
        public Physics3DShapeCastHit Hit { get; private set; }

        public bool AllowTest(CollidableReference collidable) => _filter.Allow(_bodies, collidable);
        public bool AllowTest(CollidableReference collidable, int child) => AllowTest(collidable);

        public void OnHit(
            ref float maximumT,
            float t,
            in Vector3 hitLocation,
            in Vector3 hitNormal,
            CollidableReference collidable)
        {
            Consider(t, hitLocation, hitNormal, collidable, startedOverlapping: false, ref maximumT);
        }

        public void OnHitAtZeroT(ref float maximumT, CollidableReference collidable)
        {
            Consider(0f, _start, Vector3.Zero, collidable, startedOverlapping: true, ref maximumT);
        }

        private void Consider(
            float distance,
            in Vector3 position,
            in Vector3 normal,
            CollidableReference collidable,
            bool startedOverlapping,
            ref float maximumT)
        {
            if (!AllowTest(collidable))
            {
                return;
            }

            int slot = _bodies.RequireSlot(collidable);
            if (HasHit &&
                (distance > Hit.DistanceCm || (distance == Hit.DistanceCm && slot >= Hit.Body.Slot)))
            {
                return;
            }

            maximumT = distance;
            Hit = new Physics3DShapeCastHit(
                _bodies.GetId(slot),
                _bodies.GetEntity(slot),
                position,
                normal,
                distance,
                startedOverlapping);
            HasHit = true;
        }
    }

    private struct AnySweepHitCollector : ISweepHitHandler
    {
        private readonly Physics3DBodyStore _bodies;
        private readonly PreparedQueryFilter _filter;

        public AnySweepHitCollector(Physics3DBodyStore bodies, PreparedQueryFilter filter)
        {
            _bodies = bodies;
            _filter = filter;
            HasHit = false;
        }

        public bool HasHit { get; private set; }

        public bool AllowTest(CollidableReference collidable)
            => !HasHit && _filter.Allow(_bodies, collidable);

        public bool AllowTest(CollidableReference collidable, int child) => AllowTest(collidable);

        public void OnHit(
            ref float maximumT,
            float t,
            in Vector3 hitLocation,
            in Vector3 hitNormal,
            CollidableReference collidable)
        {
            if (HasHit || !AllowTest(collidable))
            {
                return;
            }

            HasHit = true;
            maximumT = 0f;
        }

        public void OnHitAtZeroT(ref float maximumT, CollidableReference collidable)
        {
            if (HasHit || !AllowTest(collidable))
            {
                return;
            }

            HasHit = true;
            maximumT = 0f;
        }
    }

    private unsafe struct OverlapCallbacks : ICollisionCallbacks
    {
        private readonly Physics3DBodyStore _bodies;
        private readonly PreparedQueryFilter _filter;
        private readonly Physics3DOverlapHit* _hits;
        private readonly int _capacity;

        public OverlapCallbacks(
            Physics3DBodyStore bodies,
            PreparedQueryFilter filter,
            Physics3DOverlapHit* hits,
            int capacity)
        {
            _bodies = bodies;
            _filter = filter;
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
            if (_filter.IgnoredSlot >= 0 && slot == _filter.IgnoredSlot)
            {
                return;
            }

            if (Count >= _capacity)
            {
                Overflowed = true;
                return;
            }

            _hits[Count++] = new Physics3DOverlapHit(_bodies.GetId(slot), _bodies.GetEntity(slot));
        }
    }

    private unsafe struct OverlapCandidateEnumerator<TShape> : IBreakableForEach<CollidableReference>
        where TShape : unmanaged, IConvexShape
    {
        private readonly Simulation _simulation;
        private readonly Physics3DBodyStore _bodies;
        private readonly TShape _queryShape;
        private readonly RigidPose _queryPose;
        private readonly PreparedQueryFilter _filter;
        private CollisionBatcher<OverlapCallbacks> _batcher;

        public OverlapCandidateEnumerator(
            Simulation simulation,
            BufferPool pool,
            Physics3DBodyStore bodies,
            TShape queryShape,
            RigidPose queryPose,
            PreparedQueryFilter filter,
            OverlapCallbacks callbacks)
        {
            _simulation = simulation;
            _bodies = bodies;
            _queryShape = queryShape;
            _queryPose = queryPose;
            _filter = filter;
            _batcher = new CollisionBatcher<OverlapCallbacks>(
                pool,
                simulation.Shapes,
                simulation.NarrowPhase.CollisionTaskRegistry,
                0f,
                callbacks);
        }

        public bool LoopBody(CollidableReference collidable)
        {
            if (!_filter.Allow(_bodies, collidable))
            {
                return true;
            }

            int slot = _bodies.RequireSlot(collidable);
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
