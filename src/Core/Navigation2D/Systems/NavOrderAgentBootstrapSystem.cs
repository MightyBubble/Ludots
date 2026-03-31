using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Navigation2D.Systems
{
    /// <summary>
    /// Ensures order-driven units have the minimum Navigation2D + Physics2D components
    /// required for nav-goal movement, while also keeping Position2D aligned with
    /// WorldPositionCm after teleports or other direct world-position writes.
    /// </summary>
    public sealed class NavOrderAgentBootstrapSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<OrderBuffer, WorldPositionCm>();

        private readonly int _moveSpeedAttributeId;
        private readonly Fix64 _defaultRadiusCm;
        private readonly Fix64 _defaultNeighborDistCm;
        private readonly Fix64 _defaultTimeHorizonSec;
        private readonly int _defaultMaxNeighbors;
        private readonly float _defaultSpeedCmPerSec;
        private readonly float _defaultAccelCmPerSec2;

        public NavOrderAgentBootstrapSystem(
            World world,
            float defaultSpeedCmPerSec = 600f,
            float defaultAccelCmPerSec2 = 6000f,
            float defaultRadiusCm = 40f,
            float defaultNeighborDistCm = 400f,
            float defaultTimeHorizonSec = 2f,
            int defaultMaxNeighbors = 16)
            : base(world)
        {
            _moveSpeedAttributeId = AttributeRegistry.Register("MoveSpeed");
            _defaultSpeedCmPerSec = Math.Max(0f, defaultSpeedCmPerSec);
            _defaultAccelCmPerSec2 = Math.Max(0f, defaultAccelCmPerSec2);
            _defaultRadiusCm = Fix64.FromFloat(Math.Max(1f, defaultRadiusCm));
            _defaultNeighborDistCm = Fix64.FromFloat(Math.Max(1f, defaultNeighborDistCm));
            _defaultTimeHorizonSec = Fix64.FromFloat(Math.Max(0.25f, defaultTimeHorizonSec));
            _defaultMaxNeighbors = Math.Max(1, defaultMaxNeighbors);
        }

        public override void Update(in float dt)
        {
            foreach (ref var chunk in World.Query(in Query))
            {
                Span<WorldPositionCm> worldPositions = chunk.GetSpan<WorldPositionCm>();
                ref var entityFirst = ref chunk.Entity(0);

                foreach (int index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    if (!World.IsAlive(entity))
                    {
                        continue;
                    }

                    Fix64Vec2 worldCm = worldPositions[index].Value;
                    EnsurePreviousWorldPosition(entity, worldCm);
                    EnsurePosition(entity, worldCm);
                    EnsureDynamics(entity, worldCm);
                    EnsureAgent(entity);
                    EnsureKinematics(entity);
                }
            }
        }

        private void EnsurePreviousWorldPosition(Entity entity, Fix64Vec2 worldCm)
        {
            if (!World.Has<PreviousWorldPositionCm>(entity))
            {
                World.Add(entity, new PreviousWorldPositionCm { Value = worldCm });
            }
        }

        private void EnsurePosition(Entity entity, Fix64Vec2 worldCm)
        {
            bool snappedToWorld = false;
            if (!World.Has<Position2D>(entity))
            {
                World.Add(entity, new Position2D { Value = worldCm });
                snappedToWorld = true;
            }
            else
            {
                ref var position = ref World.Get<Position2D>(entity);
                if (position.Value != worldCm)
                {
                    position.Value = worldCm;
                    snappedToWorld = true;
                }
            }

            if (!snappedToWorld)
            {
                return;
            }

            if (World.Has<PreviousPosition2D>(entity))
            {
                ref var previousPosition = ref World.Get<PreviousPosition2D>(entity);
                previousPosition.Value = worldCm;
            }

            if (World.Has<PreviousWorldPositionCm>(entity))
            {
                ref var previousWorldPosition = ref World.Get<PreviousWorldPositionCm>(entity);
                previousWorldPosition.Value = worldCm;
            }
        }

        private void EnsureDynamics(Entity entity, Fix64Vec2 worldCm)
        {
            if (!World.Has<Velocity2D>(entity))
            {
                World.Add(entity, Velocity2D.Zero);
            }

            if (!World.Has<Mass2D>(entity))
            {
                World.Add(entity, Mass2D.FromFloat(1f, 1f));
            }

            if (!World.Has<PreviousPosition2D>(entity))
            {
                World.Add(entity, new PreviousPosition2D { Value = worldCm });
            }

        }

        private void EnsureAgent(Entity entity)
        {
            if (!World.Has<NavAgent2D>(entity))
            {
                World.Add(entity, new NavAgent2D());
            }
        }

        private void EnsureKinematics(Entity entity)
        {
            float moveSpeedCmPerSec = ResolveMoveSpeed(entity);
            Fix64 maxSpeed = Fix64.FromFloat(moveSpeedCmPerSec);
            Fix64 maxAccel = Fix64.FromFloat(Math.Max(_defaultAccelCmPerSec2, moveSpeedCmPerSec * 12f));

            if (!World.Has<NavKinematics2D>(entity))
            {
                World.Add(entity, new NavKinematics2D
                {
                    MaxSpeedCmPerSec = maxSpeed,
                    MaxAccelCmPerSec2 = maxAccel,
                    RadiusCm = _defaultRadiusCm,
                    NeighborDistCm = _defaultNeighborDistCm,
                    TimeHorizonSec = _defaultTimeHorizonSec,
                    MaxNeighbors = _defaultMaxNeighbors
                });
                return;
            }

            ref var kinematics = ref World.Get<NavKinematics2D>(entity);
            kinematics.MaxSpeedCmPerSec = maxSpeed;
            if (kinematics.MaxAccelCmPerSec2 <= Fix64.Zero)
            {
                kinematics.MaxAccelCmPerSec2 = maxAccel;
            }
            if (kinematics.RadiusCm <= Fix64.Zero)
            {
                kinematics.RadiusCm = _defaultRadiusCm;
            }
            if (kinematics.NeighborDistCm <= Fix64.Zero)
            {
                kinematics.NeighborDistCm = _defaultNeighborDistCm;
            }
            if (kinematics.TimeHorizonSec <= Fix64.Zero)
            {
                kinematics.TimeHorizonSec = _defaultTimeHorizonSec;
            }
            if (kinematics.MaxNeighbors <= 0)
            {
                kinematics.MaxNeighbors = _defaultMaxNeighbors;
            }
        }

        private float ResolveMoveSpeed(Entity entity)
        {
            float configured = 0f;
            if (_moveSpeedAttributeId != AttributeRegistry.InvalidId &&
                World.TryGet(entity, out AttributeBuffer attributes))
            {
                configured = attributes.GetCurrent(_moveSpeedAttributeId);
                if (configured > 0f)
                {
                    return GameplayControlStateResolver.ResolveMoveSpeed(World, entity, configured);
                }
            }

            return GameplayControlStateResolver.ResolveMoveSpeed(
                World,
                entity,
                configured > 0f ? configured : _defaultSpeedCmPerSec);
        }
    }
}
