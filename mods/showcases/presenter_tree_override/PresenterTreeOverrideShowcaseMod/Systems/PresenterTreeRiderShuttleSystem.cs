using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Attachment;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using PresenterTreeOverrideShowcaseMod.Runtime;

namespace PresenterTreeOverrideShowcaseMod.Systems
{
    /// <summary>
    /// 按地图 metadata 的时间表，把乘客挂到巡逻车上、再散开卸下。走 Core 的 AttachmentOps，
    /// 和 GAS Relation 效果的 Attach/Detach 是同一套校验与语义。
    /// </summary>
    internal sealed class PresenterTreeRiderShuttleSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription RiderQuery = new QueryDescription()
            .WithAll<PresenterTreeRiderTag, WorldPositionCm>();

        private static readonly QueryDescription HostQuery = new QueryDescription()
            .WithAll<PresenterTreeRideHostTag, WorldPositionCm>();

        private readonly GameEngine _engine;
        private string? _configuredMapId;
        private bool _hasSchedule;
        private PresenterTreeRiderSchedule _schedule;
        private float _elapsedSeconds;

        public PresenterTreeRiderShuttleSystem(GameEngine engine)
            : base(engine?.World ?? throw new ArgumentNullException(nameof(engine)))
        {
            _engine = engine;
        }

        public override void Update(in float dt)
        {
            string? mapId = _engine.CurrentMapSession?.MapId.Value;
            if (!string.Equals(mapId, _configuredMapId, StringComparison.Ordinal))
            {
                _configuredMapId = mapId;
                _hasSchedule = PresenterTreeShowcaseMetadata.TryReadRiderSchedule(_engine, out _schedule);
                _elapsedSeconds = 0f;
            }

            if (!_hasSchedule)
            {
                return;
            }

            PresenterTreeRiderSchedule schedule = _schedule;
            _elapsedSeconds += dt;
            Entity rider = RequireSingle(in RiderQuery, nameof(PresenterTreeRiderTag));
            bool riding = World.Has<ChildOf>(rider);
            bool shouldRide = schedule.ShouldRide(_elapsedSeconds);
            if (shouldRide == riding)
            {
                return;
            }

            var arbiter = _engine.GetService(CoreServiceKeys.PoseAuthorityArbiter);
            if (shouldRide)
            {
                Entity host = RequireSingle(in HostQuery, nameof(PresenterTreeRideHostTag));
                var seat = new AttachedLocalPose
                {
                    OffsetCm = Fix64Vec2.FromFloat(schedule.SeatOffsetXCm, schedule.SeatOffsetYCm),
                    LocalFacingRad = Fix64.Zero,
                    OffsetRotation = AttachedOffsetRotation.ParentFacing,
                    InheritParentFacing = 1,
                };
                AttachmentOps.Attach(World, arbiter, rider, host, in seat);
                return;
            }

            AttachmentOps.Detach(World, arbiter, rider, DetachPlacement.ParentPerimeterRing, schedule.AlightRadiusCm);
        }

        private Entity RequireSingle(in QueryDescription query, string tagName)
        {
            Entity found = Entity.Null;
            int count = 0;
            foreach (ref var chunk in World.Query(in query))
            {
                foreach (int index in chunk)
                {
                    found = chunk.Entity(index);
                    count++;
                }
            }

            if (count != 1)
            {
                throw new InvalidOperationException(
                    $"Presenter tree showcase requires exactly one entity with {tagName}; found {count}.");
            }

            return found;
        }
    }
}
