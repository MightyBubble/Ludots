using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Evaluates region volume entities each fixed step and fires the
    /// volume's enter/exit events (engine <see cref="GameEvents.RegionEntered"/>/
    /// <see cref="GameEvents.RegionExited"/> by default, an authored emission
    /// contract when the volume carries <see cref="RegionVolumeEmissionCm"/>).
    ///
    /// Semantics carried over from the retired dictionary-based region system:
    /// - Cadence: active maps evaluate once per fixed step; suspended maps never evaluate.
    /// - Inside-sets survive suspend/resume (suspended entities cannot move), so no
    ///   spurious exit/enter pair fires.
    /// - Eligible mover: MapEntity + WorldPositionCm, not SuspendedTag, not
    ///   PresentationDestroyPending, and — when the volume declares a tag filter —
    ///   carrying at least one of the declared GameplayTags (any-of).
    /// - Candidate filter: the map's spatial partition answers who is standing
    ///   inside the volume's box. Entities the partition does not contain are
    ///   still read directly. A mover whose travel segment meets the box is
    ///   read even when the sampled position lies outside it. The partition
    ///   keeps one cell per entity, so that meeting test is an X-interval
    ///   sweep over the travelers and the rings: a tick where the whole army
    ///   moves does not compare every traveler with every ring.
    /// - The per-tick scan is an inline chunk job into reused columns. Enter
    ///   and exit events run after those jobs return, so the scan itself does
    ///   not record structural commands. A tick with no crossing allocates nothing.
    /// - Dead movers leave the inside-set silently: death is not a crossing.
    /// - Boundary positions count as inside; a mover that stops matching the tag
    ///   filter (or loses its position/map components) counts as an exit.
    /// - Volume entities are evaluated wherever they stand: runtime-spawned volumes
    ///   join on the next wave, and a volume whose entity dies stops evaluating —
    ///   its alive occupants receive the volume's exit event on the next update of
    ///   a still-active map (a map being torn down drops its orphans silently).
    /// </summary>
    public sealed class RegionVolumeTriggerSystem : BaseSystem<World, float>
    {
        private const int BroadphaseExtentPadCm = 1;
        private const int InitialBroadphaseCapacity = 64;

        private readonly Func<MapSessionManager?> _sessions;
        private readonly TriggerManager _triggerManager;
        private readonly Func<ScriptContext> _contextFactory;
        private readonly ISpatialQueryService _spatialQueries;
        private readonly Dictionary<Entity, VolumeRuntimeState> _states = new Dictionary<Entity, VolumeRuntimeState>();
        private readonly List<VolumeRuntimeState> _orphanScratch = new List<VolumeRuntimeState>();
        private static readonly QueryDescription VolumeQuery = new QueryDescription()
            .WithAll<MapEntity, RegionVolumeCm, WorldPositionCm>();
        private static readonly QueryDescription UnindexedQuery = new QueryDescription()
            .WithAll<MapEntity, WorldPositionCm>()
            .WithNone<SpatialCellRef, PreviousWorldPositionCm, GameplayTagContainer, SuspendedTag, PresentationDestroyPending, RegionVolumeCm>();
        private static readonly QueryDescription UnindexedTaggedQuery = new QueryDescription()
            .WithAll<MapEntity, WorldPositionCm, GameplayTagContainer>()
            .WithNone<SpatialCellRef, PreviousWorldPositionCm, SuspendedTag, PresentationDestroyPending, RegionVolumeCm>();
        private static readonly QueryDescription UnindexedMovedQuery = new QueryDescription()
            .WithAll<MapEntity, WorldPositionCm, PreviousWorldPositionCm>()
            .WithNone<SpatialCellRef, GameplayTagContainer, SuspendedTag, PresentationDestroyPending, RegionVolumeCm>();
        private static readonly QueryDescription UnindexedMovedTaggedQuery = new QueryDescription()
            .WithAll<MapEntity, WorldPositionCm, PreviousWorldPositionCm, GameplayTagContainer>()
            .WithNone<SpatialCellRef, SuspendedTag, PresentationDestroyPending, RegionVolumeCm>();
        private static readonly QueryDescription InactiveQuery = new QueryDescription()
            .WithAll<MapEntity, WorldPositionCm, SpatialCellRef>()
            .WithNone<PreviousWorldPositionCm, GameplayTagContainer, SuspendedTag, PresentationDestroyPending, RegionVolumeCm>();
        private static readonly QueryDescription InactiveTaggedQuery = new QueryDescription()
            .WithAll<MapEntity, WorldPositionCm, SpatialCellRef, GameplayTagContainer>()
            .WithNone<PreviousWorldPositionCm, SuspendedTag, PresentationDestroyPending, RegionVolumeCm>();
        private static readonly QueryDescription MembershipMovedQuery = new QueryDescription()
            .WithAll<MapEntity, WorldPositionCm, PreviousWorldPositionCm, SpatialCellRef>()
            .WithNone<GameplayTagContainer, SuspendedTag, PresentationDestroyPending, RegionVolumeCm>();
        private static readonly QueryDescription MembershipMovedTaggedQuery = new QueryDescription()
            .WithAll<MapEntity, WorldPositionCm, PreviousWorldPositionCm, SpatialCellRef, GameplayTagContainer>()
            .WithNone<SuspendedTag, PresentationDestroyPending, RegionVolumeCm>();
        private readonly CandidateColumns _direct = new CandidateColumns();
        private readonly CandidateColumns _movers = new CandidateColumns();
        private readonly HashSet<Entity> _considered = new HashSet<Entity>();
        private readonly HashSet<Entity> _matchedBuffer = new HashSet<Entity>();
        private readonly List<Entity> _exitBuffer = new List<Entity>();
        private readonly List<Entity> _silentRemovalBuffer = new List<Entity>();
        private Entity[] _broadphaseBuffer = new Entity[InitialBroadphaseCapacity];
        private VolumeRuntimeState[] _sweepVolumes = Array.Empty<VolumeRuntimeState>();
        private WorldAabbCm[] _sweepBounds = Array.Empty<WorldAabbCm>();
        private SweepEndpoint[] _sweepEndpoints = Array.Empty<SweepEndpoint>();
        private double[] _moverMinY = Array.Empty<double>();
        private double[] _moverMaxY = Array.Empty<double>();
        private int[] _activeVolumes = Array.Empty<int>();
        private int[] _activeVolumeSlot = Array.Empty<int>();
        private int[] _activeMovers = Array.Empty<int>();
        private int[] _activeMoverSlot = Array.Empty<int>();
        private int[] _travelMover = Array.Empty<int>();
        private int[] _travelNext = Array.Empty<int>();
        private int _sweepVolumeCount;
        private int _activeVolumeCount;
        private int _activeMoverCount;
        private int _travelHitCount;

        public RegionVolumeTriggerSystem(
            World world,
            Func<MapSessionManager?> sessions,
            TriggerManager triggerManager,
            Func<ScriptContext> contextFactory,
            ISpatialQueryService spatialQueries)
            : base(world)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _spatialQueries = spatialQueries ?? throw new ArgumentNullException(nameof(spatialQueries));
        }

        public override void Initialize()
        {
            World.SubscribeEntityDestroyed(OnVolumeEntityDestroyed);
        }

        public override void Update(in float t)
        {
            FlushOrphanedVolumes();
            MapSessionManager? sessions = _sessions();
            if (sessions == null)
            {
                return;
            }

            foreach (KeyValuePair<MapId, MapSession> sessionPair in sessions.EnumerateSessions())
            {
                MapSession session = sessionPair.Value;
                if (session.State != MapSessionState.Active) continue;
                SyncVolumeStates(session);
                if (!PrepareVolumes(session.MapId))
                {
                    continue;
                }

                CollectCandidates(session.MapId);
                PairTravelers();
                foreach (KeyValuePair<Entity, VolumeRuntimeState> pair in _states)
                {
                    if (pair.Value.MapId != session.MapId || pair.Value.Orphaned) continue;
                    EvaluateVolume(session, pair.Value);
                }
            }
        }

        private void SyncVolumeStates(MapSession session)
        {
            var job = new SyncVolumeJob
            {
                MapId = session.MapId,
                System = this,
            };
            World.InlineEntityQuery<SyncVolumeJob, MapEntity, RegionVolumeCm, WorldPositionCm>(in VolumeQuery, ref job);
        }

        private void UpsertVolume(MapId mapId, Entity entity, RegionVolumeCm volume, Fix64Vec2 anchor)
        {
            if (!_states.TryGetValue(entity, out VolumeRuntimeState? state))
            {
                state = new VolumeRuntimeState(mapId);
                _states[entity] = state;
            }

            state.Refresh(World, entity, volume, anchor, _triggerManager.EventSchemas);
        }

        private void CollectCandidates(MapId mapId)
        {
            _direct.Reset();
            _movers.Reset();

            var plain = new CollectPositionJob { MapId = mapId, Columns = _direct };
            World.InlineEntityQuery<CollectPositionJob, MapEntity, WorldPositionCm>(in UnindexedQuery, ref plain);
            var tagged = new CollectTaggedJob { MapId = mapId, Columns = _direct };
            World.InlineEntityQuery<CollectTaggedJob, MapEntity, WorldPositionCm, GameplayTagContainer>(in UnindexedTaggedQuery, ref tagged);
            var moved = new CollectMovedJob { MapId = mapId, Columns = _direct };
            World.InlineEntityQuery<CollectMovedJob, MapEntity, WorldPositionCm, PreviousWorldPositionCm>(in UnindexedMovedQuery, ref moved);
            var movedTagged = new CollectMovedTaggedJob { MapId = mapId, Columns = _direct };
            World.InlineEntityQuery<CollectMovedTaggedJob, MapEntity, WorldPositionCm, PreviousWorldPositionCm, GameplayTagContainer>(in UnindexedMovedTaggedQuery, ref movedTagged);

            var inactive = new CollectInactiveJob { MapId = mapId, Columns = _direct };
            World.InlineEntityQuery<CollectInactiveJob, MapEntity, WorldPositionCm, SpatialCellRef>(in InactiveQuery, ref inactive);
            var inactiveTagged = new CollectInactiveTaggedJob { MapId = mapId, Columns = _direct };
            World.InlineEntityQuery<CollectInactiveTaggedJob, MapEntity, WorldPositionCm, SpatialCellRef, GameplayTagContainer>(in InactiveTaggedQuery, ref inactiveTagged);
            var membershipMoved = new CollectMembershipMovedJob { MapId = mapId, Direct = _direct, Movers = _movers };
            World.InlineEntityQuery<CollectMembershipMovedJob, MapEntity, WorldPositionCm, PreviousWorldPositionCm, SpatialCellRef>(in MembershipMovedQuery, ref membershipMoved);
            var membershipMovedTagged = new CollectMembershipMovedTaggedJob { MapId = mapId, Direct = _direct, Movers = _movers };
            World.InlineEntityQuery<CollectMembershipMovedTaggedJob, MapEntity, WorldPositionCm, PreviousWorldPositionCm, SpatialCellRef, GameplayTagContainer>(in MembershipMovedTaggedQuery, ref membershipMovedTagged);
        }

        private void EvaluateVolume(MapSession session, VolumeRuntimeState volume)
        {
            _matchedBuffer.Clear();
            _considered.Clear();

            // The partition records the cell an entity occupies now. Padding keeps
            // a unit standing on a cell edge inside the query. The travel-segment
            // pass covers a crossing that ends outside that box.
            WorldAabbCm bounds = volume.TickBounds;
            int count = QueryCandidates(in bounds);
            for (int i = 0; i < count; i++)
            {
                if (!TryReadTracked(_broadphaseBuffer[i], volume.MapId, out TrackedEntity tracked))
                {
                    continue;
                }

                if (_considered.Add(tracked.Entity))
                {
                    Consider(session, volume, tracked);
                }
            }

            for (int i = 0; i < _direct.Count; i++)
            {
                TrackedEntity tracked = _direct.At(i);
                if (_considered.Add(tracked.Entity))
                {
                    Consider(session, volume, tracked);
                }
            }

            for (int node = volume.TravelHead; node >= 0; node = _travelNext[node])
            {
                int mover = _travelMover[node];
                Entity entity = _movers.EntityAt(mover);
                if (_considered.Contains(entity) || !_movers.SegmentOverlaps(mover, in bounds))
                {
                    continue;
                }

                _considered.Add(entity);
                Consider(session, volume, _movers.At(mover));
            }

            _exitBuffer.Clear();
            _silentRemovalBuffer.Clear();
            foreach (Entity entity in volume.Inside)
            {
                if (_matchedBuffer.Contains(entity))
                {
                    continue;
                }

                if (!World.IsAlive(entity))
                {
                    _silentRemovalBuffer.Add(entity);
                    continue;
                }

                if (World.Has<PresentationDestroyPending>(entity))
                {
                    _silentRemovalBuffer.Add(entity);
                    continue;
                }

                if (World.Has<SuspendedTag>(entity))
                {
                    continue;
                }

                _exitBuffer.Add(entity);
            }

            for (int i = 0; i < _silentRemovalBuffer.Count; i++)
            {
                volume.Inside.Remove(_silentRemovalBuffer[i]);
            }

            for (int i = 0; i < _exitBuffer.Count; i++)
            {
                Entity entity = _exitBuffer[i];
                volume.Inside.Remove(entity);
                FireVolumeEvent(session, volume, entering: false, entity);
            }
        }

        private void Consider(MapSession session, VolumeRuntimeState volume, TrackedEntity tracked)
        {
            if (volume.HasTagFilter && (!tracked.HasTags || !tracked.Tags.Intersects(volume.TagFilter)))
            {
                return;
            }

            if (!volume.Shape.Contains(tracked.Position, volume.Anchor))
            {
                // Swept crossing (#1475): a mover whose travel segment passed
                // through the volume between waves still reports the crossing as
                // an enter+exit pair, without joining the occupancy inside-set.
                if (tracked.HasPreviousPosition &&
                    !volume.Inside.Contains(tracked.Entity) &&
                    volume.Shape.IntersectsPath(tracked.PreviousPosition, tracked.Position, volume.Anchor))
                {
                    FireVolumeEvent(session, volume, entering: true, tracked.Entity);
                    FireVolumeEvent(session, volume, entering: false, tracked.Entity);
                }

                return;
            }

            _matchedBuffer.Add(tracked.Entity);
            if (volume.Inside.Add(tracked.Entity))
            {
                FireVolumeEvent(session, volume, entering: true, tracked.Entity);
            }
        }

        private TrackedEntity ReadTracked(Entity entity, Fix64Vec2 position)
        {
            bool hasTags = World.TryGet<GameplayTagContainer>(entity, out GameplayTagContainer tags);
            bool hasPrevious = World.TryGet(entity, out PreviousWorldPositionCm previous);
            return new TrackedEntity(entity, position, previous.Value, hasPrevious, tags, hasTags);
        }

        private bool TryReadTracked(Entity entity, MapId mapId, out TrackedEntity tracked)
        {
            tracked = default;
            if (!World.IsAlive(entity))
            {
                return false;
            }

            if (!World.TryGet(entity, out MapEntity mapEntity) || mapEntity.MapId != mapId)
            {
                return false;
            }

            if (!World.TryGet(entity, out WorldPositionCm position))
            {
                return false;
            }

            if (World.Has<SuspendedTag>(entity) ||
                World.Has<PresentationDestroyPending>(entity) ||
                World.Has<RegionVolumeCm>(entity))
            {
                return false;
            }

            tracked = ReadTracked(entity, position.Value);
            return true;
        }

        private int QueryCandidates(in WorldAabbCm bounds)
        {
            SpatialQueryResult result = _spatialQueries.QueryAabb(in bounds, _broadphaseBuffer);
            if (!result.Overflowed)
            {
                return result.Count;
            }

            long needed = (long)result.Count + result.Dropped;
            if (needed <= _broadphaseBuffer.Length || needed > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "Region volume broadphase dropped hits that the buffer should already hold.");
            }

            _broadphaseBuffer = new Entity[(int)needed];
            result = _spatialQueries.QueryAabb(in bounds, _broadphaseBuffer);
            if (result.Overflowed)
            {
                throw new InvalidOperationException(
                    "Region volume broadphase dropped hits after the buffer grew to the reported count.");
            }

            return result.Count;
        }

        private static WorldAabbCm ToBroadphaseBounds(in RegionVolumeShape shape, Fix64Vec2 anchor)
        {
            shape.GetWorldExtents(anchor, out double minX, out double minY, out double maxX, out double maxY);
            long minXi = IntegralCentimeter(minX - BroadphaseExtentPadCm, ceiling: false);
            long minYi = IntegralCentimeter(minY - BroadphaseExtentPadCm, ceiling: false);
            long maxXi = IntegralCentimeter(maxX + BroadphaseExtentPadCm, ceiling: true);
            long maxYi = IntegralCentimeter(maxY + BroadphaseExtentPadCm, ceiling: true);
            long width = maxXi - minXi;
            long height = maxYi - minYi;
            if (width < 0 || height < 0 || width > int.MaxValue || height > int.MaxValue)
            {
                throw new InvalidOperationException("Region volume extents do not fit an integer centimeter box.");
            }

            return new WorldAabbCm((int)minXi, (int)minYi, (int)width, (int)height);
        }

        private static long IntegralCentimeter(double value, bool ceiling)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new InvalidOperationException("Region volume extents are not finite.");
            }

            double integral = ceiling ? Math.Ceiling(value) : Math.Floor(value);
            if (integral < int.MinValue || integral > int.MaxValue)
            {
                throw new InvalidOperationException("Region volume extents do not fit integer centimeters.");
            }

            return (long)integral;
        }

        private static bool SegmentOverlapsBox(Fix64Vec2 start, Fix64Vec2 end, in WorldAabbCm box)
        {
            SegmentBounds(start, end, out double minX, out double minY, out double maxX, out double maxY);
            return maxX >= box.Left && minX <= box.Right && maxY >= box.Top && minY <= box.Bottom;
        }

        private static void SegmentBounds(
            Fix64Vec2 start,
            Fix64Vec2 end,
            out double minX,
            out double minY,
            out double maxX,
            out double maxY)
        {
            double ax = start.X.ToDouble();
            double ay = start.Y.ToDouble();
            double bx = end.X.ToDouble();
            double by = end.Y.ToDouble();
            minX = Math.Min(ax, bx);
            maxX = Math.Max(ax, bx);
            minY = Math.Min(ay, by);
            maxY = Math.Max(ay, by);
        }

        private bool PrepareVolumes(MapId mapId)
        {
            _sweepVolumeCount = 0;
            _travelHitCount = 0;
            bool any = false;
            foreach (KeyValuePair<Entity, VolumeRuntimeState> pair in _states)
            {
                VolumeRuntimeState volume = pair.Value;
                if (volume.MapId != mapId || volume.Orphaned)
                {
                    continue;
                }

                any = true;
                volume.TravelHead = -1;
                if (_sweepVolumeCount == _sweepVolumes.Length)
                {
                    GrowSweepVolumes();
                }

                WorldAabbCm bounds = ToBroadphaseBounds(volume.Shape, volume.Anchor);
                volume.TickBounds = bounds;
                _sweepVolumes[_sweepVolumeCount] = volume;
                _sweepBounds[_sweepVolumeCount] = bounds;
                _sweepVolumeCount++;
            }

            return any;
        }

        private void PairTravelers()
        {
            int moverCount = _movers.Count;
            if (moverCount == 0 || _sweepVolumeCount == 0)
            {
                return;
            }

            EnsureMoverScratch(moverCount);
            int endpointCount = (_sweepVolumeCount + moverCount) * 2;
            if (endpointCount > _sweepEndpoints.Length)
            {
                Grow(ref _sweepEndpoints, endpointCount);
            }

            int endpoint = 0;
            for (int i = 0; i < _sweepVolumeCount; i++)
            {
                WorldAabbCm box = _sweepBounds[i];
                _sweepEndpoints[endpoint++] = new SweepEndpoint(box.Left, i, SweepEndpointKind.VolumeStart);
                _sweepEndpoints[endpoint++] = new SweepEndpoint(box.Right, i, SweepEndpointKind.VolumeEnd);
            }

            for (int i = 0; i < moverCount; i++)
            {
                _movers.SegmentBounds(i, out double minX, out double minY, out double maxX, out double maxY);
                _moverMinY[i] = minY;
                _moverMaxY[i] = maxY;
                _sweepEndpoints[endpoint++] = new SweepEndpoint(minX, i, SweepEndpointKind.MoverStart);
                _sweepEndpoints[endpoint++] = new SweepEndpoint(maxX, i, SweepEndpointKind.MoverEnd);
            }

            Array.Sort(_sweepEndpoints, 0, endpoint);
            _activeVolumeCount = 0;
            _activeMoverCount = 0;
            for (int i = 0; i < endpoint; i++)
            {
                SweepEndpoint item = _sweepEndpoints[i];
                switch (item.Kind)
                {
                    case SweepEndpointKind.VolumeStart:
                        RecordVolumeAgainstActiveMovers(item.Index);
                        ActivateVolume(item.Index);
                        break;
                    case SweepEndpointKind.MoverStart:
                        RecordMoverAgainstActiveVolumes(item.Index);
                        ActivateMover(item.Index);
                        break;
                    case SweepEndpointKind.VolumeEnd:
                        DeactivateVolume(item.Index);
                        break;
                    default:
                        DeactivateMover(item.Index);
                        break;
                }
            }

            if (_activeVolumeCount != 0 || _activeMoverCount != 0)
            {
                throw new InvalidOperationException("Region volume travel sweep left an interval active.");
            }
        }

        private void RecordVolumeAgainstActiveMovers(int volume)
        {
            WorldAabbCm box = _sweepBounds[volume];
            for (int i = 0; i < _activeMoverCount; i++)
            {
                int mover = _activeMovers[i];
                if (_moverMaxY[mover] >= box.Top && _moverMinY[mover] <= box.Bottom)
                {
                    PushTravelHit(volume, mover);
                }
            }
        }

        private void RecordMoverAgainstActiveVolumes(int mover)
        {
            double minY = _moverMinY[mover];
            double maxY = _moverMaxY[mover];
            for (int i = 0; i < _activeVolumeCount; i++)
            {
                int volume = _activeVolumes[i];
                WorldAabbCm box = _sweepBounds[volume];
                if (maxY >= box.Top && minY <= box.Bottom)
                {
                    PushTravelHit(volume, mover);
                }
            }
        }

        private void PushTravelHit(int volume, int mover)
        {
            if (_travelHitCount == _travelMover.Length)
            {
                int next = Math.Max(64, _travelMover.Length * 2);
                Grow(ref _travelMover, next);
                Grow(ref _travelNext, next);
            }

            int node = _travelHitCount++;
            VolumeRuntimeState state = _sweepVolumes[volume];
            _travelMover[node] = mover;
            _travelNext[node] = state.TravelHead;
            state.TravelHead = node;
        }

        private void ActivateVolume(int volume)
        {
            if (_activeVolumeSlot[volume] != -1)
            {
                throw new InvalidOperationException("Region volume travel sweep activated a ring that was already open.");
            }

            _activeVolumeSlot[volume] = _activeVolumeCount;
            _activeVolumes[_activeVolumeCount++] = volume;
        }

        private void DeactivateVolume(int volume)
        {
            int slot = _activeVolumeSlot[volume];
            if ((uint)slot >= (uint)_activeVolumeCount)
            {
                throw new InvalidOperationException("Region volume travel sweep closed a ring that was not open.");
            }

            int last = --_activeVolumeCount;
            int moved = _activeVolumes[last];
            _activeVolumes[slot] = moved;
            _activeVolumeSlot[moved] = slot;
            _activeVolumeSlot[volume] = -1;
        }

        private void ActivateMover(int mover)
        {
            if (_activeMoverSlot[mover] != -1)
            {
                throw new InvalidOperationException("Region volume travel sweep activated a traveler that was already open.");
            }

            _activeMoverSlot[mover] = _activeMoverCount;
            _activeMovers[_activeMoverCount++] = mover;
        }

        private void DeactivateMover(int mover)
        {
            int slot = _activeMoverSlot[mover];
            if ((uint)slot >= (uint)_activeMoverCount)
            {
                throw new InvalidOperationException("Region volume travel sweep closed a traveler that was not open.");
            }

            int last = --_activeMoverCount;
            int moved = _activeMovers[last];
            _activeMovers[slot] = moved;
            _activeMoverSlot[moved] = slot;
            _activeMoverSlot[mover] = -1;
        }

        private void GrowSweepVolumes()
        {
            int n = Math.Max(64, _sweepVolumes.Length * 2);
            int oldSlots = _activeVolumeSlot.Length;
            Array.Resize(ref _sweepVolumes, n);
            Array.Resize(ref _sweepBounds, n);
            Array.Resize(ref _activeVolumes, n);
            Array.Resize(ref _activeVolumeSlot, n);
            for (int i = oldSlots; i < n; i++)
            {
                _activeVolumeSlot[i] = -1;
            }
        }

        private void EnsureMoverScratch(int moverCount)
        {
            if (moverCount <= _moverMinY.Length)
            {
                return;
            }

            int n = Math.Max(64, _moverMinY.Length);
            while (n < moverCount)
            {
                n *= 2;
            }

            int oldSlots = _activeMoverSlot.Length;
            Array.Resize(ref _moverMinY, n);
            Array.Resize(ref _moverMaxY, n);
            Array.Resize(ref _activeMovers, n);
            Array.Resize(ref _activeMoverSlot, n);
            for (int i = oldSlots; i < n; i++)
            {
                _activeMoverSlot[i] = -1;
            }
        }

        private static void Grow<T>(ref T[] array, int needed)
        {
            int n = Math.Max(64, array.Length);
            while (n < needed)
            {
                n *= 2;
            }

            Array.Resize(ref array, n);
        }

        private void OnVolumeEntityDestroyed(in Entity entity)
        {
            if (_states.TryGetValue(entity, out VolumeRuntimeState? state))
            {
                state.Orphaned = true;
            }
        }

        private void FlushOrphanedVolumes()
        {
            if (_states.Count == 0)
            {
                return;
            }

            _orphanScratch.Clear();
            foreach (KeyValuePair<Entity, VolumeRuntimeState> pair in _states)
            {
                if (pair.Value.Orphaned)
                {
                    _orphanScratch.Add(pair.Value);
                }
            }

            for (int i = 0; i < _orphanScratch.Count; i++)
            {
                VolumeRuntimeState volume = _orphanScratch[i];
                MapSession? session = _sessions()?.GetSession(volume.MapId);
                if (session == null || session.State != MapSessionState.Active)
                {
                    _states.Remove(volume.VolumeEntity);
                    continue;
                }

                foreach (Entity entity in volume.Inside)
                {
                    if (World.IsAlive(entity) && !World.Has<SuspendedTag>(entity))
                    {
                        FireVolumeEvent(session, volume, entering: false, entity);
                    }
                }

                volume.Inside.Clear();
                _states.Remove(volume.VolumeEntity);
            }
        }

        private void FireVolumeEvent(MapSession session, VolumeRuntimeState volume, bool entering, Entity entity)
        {
            EventKey eventKey = entering ? volume.EnterEvent : volume.ExitEvent;
            if (!_triggerManager.HasDispatchTarget(session.MapId, eventKey.Value))
            {
                return;
            }

            // Shared outlet with the field membership line (#1468); semantics
            // documented on RegionEmissionFiring.
            RegionEmissionFiring.Fire(
                _triggerManager,
                _contextFactory,
                session,
                eventKey,
                volume.VolumeKey,
                entity,
                entering ? volume.EnterSchema : volume.ExitSchema,
                volume.Payload);
        }

        private sealed class CandidateColumns
        {
            private Entity[] _entities = new Entity[InitialBroadphaseCapacity];
            private Fix64Vec2[] _positions = new Fix64Vec2[InitialBroadphaseCapacity];
            private Fix64Vec2[] _previous = new Fix64Vec2[InitialBroadphaseCapacity];
            private GameplayTagContainer[] _tags = new GameplayTagContainer[InitialBroadphaseCapacity];
            private byte[] _hasPrevious = new byte[InitialBroadphaseCapacity];
            private byte[] _hasTags = new byte[InitialBroadphaseCapacity];

            public int Count { get; private set; }

            public void Reset() => Count = 0;

            public void Add(
                Entity entity,
                Fix64Vec2 position,
                Fix64Vec2 previous,
                bool hasPrevious,
                in GameplayTagContainer tags,
                bool hasTags)
            {
                if (Count == _entities.Length)
                {
                    int next = _entities.Length * 2;
                    Array.Resize(ref _entities, next);
                    Array.Resize(ref _positions, next);
                    Array.Resize(ref _previous, next);
                    Array.Resize(ref _tags, next);
                    Array.Resize(ref _hasPrevious, next);
                    Array.Resize(ref _hasTags, next);
                }

                int index = Count++;
                _entities[index] = entity;
                _positions[index] = position;
                _previous[index] = previous;
                _tags[index] = tags;
                _hasPrevious[index] = hasPrevious ? (byte)1 : (byte)0;
                _hasTags[index] = hasTags ? (byte)1 : (byte)0;
            }

            public Entity EntityAt(int index) => _entities[index];

            public bool SegmentOverlaps(int index, in WorldAabbCm box)
            {
                return SegmentOverlapsBox(_previous[index], _positions[index], in box);
            }

            public void SegmentBounds(int index, out double minX, out double minY, out double maxX, out double maxY)
            {
                RegionVolumeTriggerSystem.SegmentBounds(_previous[index], _positions[index], out minX, out minY, out maxX, out maxY);
            }

            public TrackedEntity At(int index)
            {
                return new TrackedEntity(
                    _entities[index],
                    _positions[index],
                    _previous[index],
                    _hasPrevious[index] != 0,
                    _tags[index],
                    _hasTags[index] != 0);
            }
        }

        private struct SyncVolumeJob : IForEachWithEntity<MapEntity, RegionVolumeCm, WorldPositionCm>
        {
            public MapId MapId;
            public RegionVolumeTriggerSystem System;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref MapEntity map, ref RegionVolumeCm volume, ref WorldPositionCm position)
            {
                if (map.MapId != MapId)
                {
                    return;
                }

                System.UpsertVolume(MapId, entity, volume, position.Value);
            }
        }

        private struct CollectPositionJob : IForEachWithEntity<MapEntity, WorldPositionCm>
        {
            public MapId MapId;
            public CandidateColumns Columns;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref MapEntity map, ref WorldPositionCm position)
            {
                if (map.MapId != MapId)
                {
                    return;
                }

                Columns.Add(entity, position.Value, default, false, default, false);
            }
        }

        private struct CollectTaggedJob : IForEachWithEntity<MapEntity, WorldPositionCm, GameplayTagContainer>
        {
            public MapId MapId;
            public CandidateColumns Columns;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref MapEntity map, ref WorldPositionCm position, ref GameplayTagContainer tags)
            {
                if (map.MapId != MapId)
                {
                    return;
                }

                Columns.Add(entity, position.Value, default, false, tags, true);
            }
        }

        private struct CollectMovedJob : IForEachWithEntity<MapEntity, WorldPositionCm, PreviousWorldPositionCm>
        {
            public MapId MapId;
            public CandidateColumns Columns;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref MapEntity map, ref WorldPositionCm position, ref PreviousWorldPositionCm previous)
            {
                if (map.MapId != MapId)
                {
                    return;
                }

                Columns.Add(entity, position.Value, previous.Value, true, default, false);
            }
        }

        private struct CollectMovedTaggedJob : IForEachWithEntity<MapEntity, WorldPositionCm, PreviousWorldPositionCm, GameplayTagContainer>
        {
            public MapId MapId;
            public CandidateColumns Columns;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(
                Entity entity,
                ref MapEntity map,
                ref WorldPositionCm position,
                ref PreviousWorldPositionCm previous,
                ref GameplayTagContainer tags)
            {
                if (map.MapId != MapId)
                {
                    return;
                }

                Columns.Add(entity, position.Value, previous.Value, true, tags, true);
            }
        }

        private struct CollectInactiveJob : IForEachWithEntity<MapEntity, WorldPositionCm, SpatialCellRef>
        {
            public MapId MapId;
            public CandidateColumns Columns;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref MapEntity map, ref WorldPositionCm position, ref SpatialCellRef cell)
            {
                if (map.MapId != MapId || cell.State == SpatialMembershipState.Active)
                {
                    return;
                }

                Columns.Add(entity, position.Value, default, false, default, false);
            }
        }

        private struct CollectInactiveTaggedJob : IForEachWithEntity<MapEntity, WorldPositionCm, SpatialCellRef, GameplayTagContainer>
        {
            public MapId MapId;
            public CandidateColumns Columns;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(
                Entity entity,
                ref MapEntity map,
                ref WorldPositionCm position,
                ref SpatialCellRef cell,
                ref GameplayTagContainer tags)
            {
                if (map.MapId != MapId || cell.State == SpatialMembershipState.Active)
                {
                    return;
                }

                Columns.Add(entity, position.Value, default, false, tags, true);
            }
        }

        private struct CollectMembershipMovedJob : IForEachWithEntity<MapEntity, WorldPositionCm, PreviousWorldPositionCm, SpatialCellRef>
        {
            public MapId MapId;
            public CandidateColumns Direct;
            public CandidateColumns Movers;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(
                Entity entity,
                ref MapEntity map,
                ref WorldPositionCm position,
                ref PreviousWorldPositionCm previous,
                ref SpatialCellRef cell)
            {
                if (map.MapId != MapId)
                {
                    return;
                }

                if (cell.State == SpatialMembershipState.Active)
                {
                    if (previous.Value != position.Value)
                    {
                        Movers.Add(entity, position.Value, previous.Value, true, default, false);
                    }

                    return;
                }

                Direct.Add(entity, position.Value, previous.Value, true, default, false);
            }
        }

        private struct CollectMembershipMovedTaggedJob : IForEachWithEntity<MapEntity, WorldPositionCm, PreviousWorldPositionCm, SpatialCellRef, GameplayTagContainer>
        {
            public MapId MapId;
            public CandidateColumns Direct;
            public CandidateColumns Movers;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(
                Entity entity,
                ref MapEntity map,
                ref WorldPositionCm position,
                ref PreviousWorldPositionCm previous,
                ref SpatialCellRef cell,
                ref GameplayTagContainer tags)
            {
                if (map.MapId != MapId)
                {
                    return;
                }

                if (cell.State == SpatialMembershipState.Active)
                {
                    if (previous.Value != position.Value)
                    {
                        Movers.Add(entity, position.Value, previous.Value, true, tags, true);
                    }

                    return;
                }

                Direct.Add(entity, position.Value, previous.Value, true, tags, true);
            }
        }

        private enum SweepEndpointKind : byte
        {
            VolumeStart = 0,
            MoverStart = 1,
            VolumeEnd = 2,
            MoverEnd = 3,
        }

        private readonly struct SweepEndpoint : IComparable<SweepEndpoint>
        {
            public readonly double X;
            public readonly int Index;
            public readonly SweepEndpointKind Kind;

            public SweepEndpoint(double x, int index, SweepEndpointKind kind)
            {
                X = x;
                Index = index;
                Kind = kind;
            }

            public int CompareTo(SweepEndpoint other)
            {
                int order = X.CompareTo(other.X);
                if (order != 0)
                {
                    return order;
                }

                return Kind - other.Kind;
            }
        }

        private readonly struct TrackedEntity
        {
            public readonly Entity Entity;
            public readonly Fix64Vec2 Position;
            public readonly Fix64Vec2 PreviousPosition;
            public readonly bool HasPreviousPosition;
            public readonly GameplayTagContainer Tags;
            public readonly bool HasTags;

            public TrackedEntity(
                Entity entity,
                Fix64Vec2 position,
                Fix64Vec2 previousPosition,
                bool hasPreviousPosition,
                GameplayTagContainer tags,
                bool hasTags)
            {
                Entity = entity;
                Position = position;
                PreviousPosition = previousPosition;
                HasPreviousPosition = hasPreviousPosition;
                Tags = tags;
                HasTags = hasTags;
            }
        }

        private sealed class VolumeRuntimeState
        {
            public VolumeRuntimeState(MapId mapId)
            {
                MapId = mapId;
                EnterEvent = GameEvents.RegionEntered;
                ExitEvent = GameEvents.RegionExited;
            }

            public MapId MapId { get; }
            public Entity VolumeEntity { get; set; }
            public string VolumeKey { get; set; } = string.Empty;
            public RegionVolumeShape Shape { get; set; }
            public Fix64Vec2 Anchor { get; set; }
            public GameplayTagContainer TagFilter { get; set; }
            public bool HasTagFilter { get; set; }
            public EventKey EnterEvent { get; set; }
            public EventKey ExitEvent { get; set; }
            public EventSchema? EnterSchema { get; set; }
            public EventSchema? ExitSchema { get; set; }
            public RegionVolumePayloadEntry[]? Payload { get; set; }
            public bool Orphaned { get; set; }
            public int TravelHead { get; set; } = -1;
            public WorldAabbCm TickBounds { get; set; }
            public HashSet<Entity> Inside { get; } = new HashSet<Entity>();

            public void Refresh(
                World world,
                Entity entity,
                RegionVolumeCm volume,
                Fix64Vec2 anchor,
                EventSchemaRegistry? schemas)
            {
                VolumeEntity = entity;
                VolumeKey = volume.VolumeKey;
                Shape = volume.Shape;
                Anchor = anchor;
                HasTagFilter = world.TryGet(entity, out RegionVolumeTagFilterCm filter);
                if (HasTagFilter)
                {
                    TagFilter = filter.Filter;
                }

                if (world.TryGet(entity, out RegionVolumeEmissionCm emission))
                {
                    EnterEvent = emission.EnterEvent;
                    ExitEvent = emission.ExitEvent;
                    Payload = emission.Payload;
                }
                else
                {
                    EnterEvent = GameEvents.RegionEntered;
                    ExitEvent = GameEvents.RegionExited;
                    Payload = null;
                }

                EnterSchema = schemas != null && schemas.TryGet(EnterEvent.Value, out EventSchema enterSchema) ? enterSchema : null;
                ExitSchema = schemas != null && schemas.TryGet(ExitEvent.Value, out EventSchema exitSchema) ? exitSchema : null;
                Orphaned = false;
            }
        }
    }
}
