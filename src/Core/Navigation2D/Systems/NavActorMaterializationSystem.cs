using System;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Navigation2D.Systems
{
    public sealed class NavActorMaterializationSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<NavActor, NavProfileRef, WorldPositionCm, NavActorRuntimeState>();

        private readonly Navigation2DContractCatalog _catalog;
        private readonly CommandBuffer _commandBuffer = new();

        public NavActorMaterializationSystem(World world, Navigation2DContractCatalog catalog) : base(world)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public override void Update(in float dt)
        {
            foreach (ref var chunk in World.Query(in Query))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                chunk.GetSpan<NavActor, NavProfileRef, WorldPositionCm, NavActorRuntimeState>(
                    out var actors,
                    out var navProfileRefs,
                    out var worldPositions,
                    out var runtimeStates);

                bool hasCrowdProfileRef = chunk.Has<NavCrowdProfileRef>();
                Span<NavCrowdProfileRef> crowdProfileRefs = hasCrowdProfileRef ? chunk.GetSpan<NavCrowdProfileRef>() : default;
                bool hasNavPhysicalOverride = chunk.Has<NavPhysicalOverride>();
                Span<NavPhysicalOverride> physicalOverrides = hasNavPhysicalOverride ? chunk.GetSpan<NavPhysicalOverride>() : default;

                foreach (int index in chunk)
                {
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    NavActor actor = actors[index];
                    if (!actor.Enabled)
                    {
                        continue;
                    }

                    ref NavActorRuntimeState runtimeState = ref runtimeStates[index];
                    if (runtimeState.IsValidated == 0)
                    {
                        continue;
                    }

                    if (!_catalog.TryGetNavProfile(navProfileRefs[index].ProfileId, out NavProfileDefinition navProfile))
                    {
                        continue;
                    }

                    NavCrowdProfileDefinition crowdProfile = default;
                    bool hasCrowdProfile = hasCrowdProfileRef && _catalog.TryGetCrowdProfile(crowdProfileRefs[index].ProfileId, out crowdProfile);
                    Fix64 radiusCm = hasCrowdProfile ? crowdProfile.GeometryRadiusCm : navProfile.RadiusCm;
                    Fix64Vec2 worldCm = worldPositions[index].Value;

                    EnsurePositionState(entity, worldCm);
                    EnsureVelocity(entity);
                    EnsureAgent(entity);
                    EnsureGoal(entity);
                    EnsureSteeringOutputs(entity);
                    EnsureKinematics(entity, navProfile, radiusCm);
                    EnsureCrowdAgent(entity, hasCrowdProfile, crowdProfile);
                    EnsureSolverMode(entity, actor, hasCrowdProfile, crowdProfile);

                    NavPhysicsMode effectivePhysicsMode = actor.PhysicsMode;
                    if (hasNavPhysicalOverride && physicalOverrides[index].Active)
                    {
                        effectivePhysicsMode = NavPhysicsMode.FullPhysics2D;
                    }

                    EnsurePhysicsLayer(entity, ref runtimeState, effectivePhysicsMode);
                    runtimeState.IsMaterialized = 1;
                    runtimeState.EffectivePhysicsMode = effectivePhysicsMode;
                }
            }

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        private void EnsurePositionState(Entity entity, Fix64Vec2 worldCm)
        {
            if (!World.Has<Position2D>(entity))
            {
                _commandBuffer.Add(entity, new Position2D { Value = worldCm });
            }
            else
            {
                ref Position2D position = ref World.Get<Position2D>(entity);
                if (position.Value != worldCm)
                {
                    position.Value = worldCm;
                }
            }

            if (!World.Has<PreviousPosition2D>(entity))
            {
                _commandBuffer.Add(entity, new PreviousPosition2D { Value = worldCm });
            }
            else
            {
                ref PreviousPosition2D previous = ref World.Get<PreviousPosition2D>(entity);
                previous.Value = worldCm;
            }

            if (!World.Has<PreviousWorldPositionCm>(entity))
            {
                _commandBuffer.Add(entity, new PreviousWorldPositionCm { Value = worldCm });
            }
            else
            {
                ref PreviousWorldPositionCm previousWorld = ref World.Get<PreviousWorldPositionCm>(entity);
                previousWorld.Value = worldCm;
            }
        }

        private void EnsureVelocity(Entity entity)
        {
            if (!World.Has<Velocity2D>(entity))
            {
                _commandBuffer.Add(entity, Velocity2D.Zero);
            }
        }

        private void EnsureAgent(Entity entity)
        {
            if (!World.Has<NavAgent2D>(entity))
            {
                _commandBuffer.Add(entity, new NavAgent2D());
            }
        }

        private void EnsureGoal(Entity entity)
        {
            if (!World.Has<NavGoal2D>(entity))
            {
                _commandBuffer.Add(entity, new NavGoal2D());
            }
        }

        private void EnsureSteeringOutputs(Entity entity)
        {
            if (!World.Has<ForceInput2D>(entity))
            {
                _commandBuffer.Add(entity, new ForceInput2D { Force = Fix64Vec2.Zero });
            }

            if (!World.Has<NavDesiredVelocity2D>(entity))
            {
                _commandBuffer.Add(entity, new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero });
            }
        }

        private void EnsureKinematics(Entity entity, NavProfileDefinition profile, Fix64 radiusCm)
        {
            var kinematics = new NavKinematics2D
            {
                MaxSpeedCmPerSec = profile.MaxSpeedCmPerSec,
                MaxAccelCmPerSec2 = profile.MaxAccelCmPerSec2,
                RadiusCm = radiusCm,
                NeighborDistCm = profile.NeighborDistCm,
                TimeHorizonSec = profile.TimeHorizonSec,
                MaxNeighbors = profile.MaxNeighbors,
            };

            if (World.Has<NavKinematics2D>(entity))
            {
                World.Set(entity, kinematics);
            }
            else
            {
                _commandBuffer.Add(entity, kinematics);
            }
        }

        private void EnsureCrowdAgent(Entity entity, bool hasCrowdProfile, NavCrowdProfileDefinition crowdProfile)
        {
            if (!hasCrowdProfile)
            {
                if (World.Has<NavCrowdAgent2D>(entity))
                {
                    _commandBuffer.Remove<NavCrowdAgent2D>(entity);
                }

                return;
            }

            var crowdAgent = new NavCrowdAgent2D
            {
                GeometryRadiusCm = crowdProfile.GeometryRadiusCm,
                NavMass = crowdProfile.NavMass,
                YieldWeight = crowdProfile.YieldWeight,
                PushClassValue = (byte)crowdProfile.PushClass,
                PreferredSolverModeValue = (byte)crowdProfile.SolverPreference,
                RetryLimit = crowdProfile.RetryLimit,
                TimeoutTicks = crowdProfile.TimeoutTicks,
                AbandonTicks = crowdProfile.AbandonTicks,
            };

            if (World.Has<NavCrowdAgent2D>(entity))
            {
                World.Set(entity, crowdAgent);
            }
            else
            {
                _commandBuffer.Add(entity, crowdAgent);
            }
        }

        private void EnsureSolverMode(Entity entity, NavActor actor, bool hasCrowdProfile, NavCrowdProfileDefinition crowdProfile)
        {
            var solverMode = new NavSolverModeComponent
            {
                Value = (byte)(hasCrowdProfile ? crowdProfile.SolverPreference : actor.DefaultSolverMode),
                RuleId = 0,
            };

            if (World.Has<NavSolverModeComponent>(entity))
            {
                ref NavSolverModeComponent current = ref World.Get<NavSolverModeComponent>(entity);
                if (current.RuleId == 0)
                {
                    current = solverMode;
                }
            }
            else
            {
                _commandBuffer.Add(entity, solverMode);
            }
        }

        private void EnsurePhysicsLayer(Entity entity, ref NavActorRuntimeState runtimeState, NavPhysicsMode effectivePhysicsMode)
        {
            if (effectivePhysicsMode == NavPhysicsMode.FullPhysics2D)
            {
                if (!World.Has<Mass2D>(entity))
                {
                    _commandBuffer.Add(entity, Mass2D.FromFloat(1f, 1f));
                    runtimeState.AddedMass2D = 1;
                }

                return;
            }

            if (runtimeState.AddedMass2D != 0 && World.Has<Mass2D>(entity))
            {
                _commandBuffer.Remove<Mass2D>(entity);
                runtimeState.AddedMass2D = 0;
            }
        }
    }
}
