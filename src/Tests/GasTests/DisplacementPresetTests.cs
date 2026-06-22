using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class DisplacementPresetTests
    {
        private static readonly QueryDescription DisplacementQuery = new QueryDescription().WithAll<DisplacementState>();
        private static readonly QueryDescription EffectTemplateQuery = new QueryDescription().WithAll<GameplayEffect, EffectTemplateRef>();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            EffectParamKeys.Initialize();
        }

        [Test]
        public void Displacement_AwayFromSource_MovesTarget()
        {
            using var world = World.Create();

            var source = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var target = world.Create(new WorldPositionCm { Value = Fix64Vec2.FromInt(1000, 0) });

            int totalDist = 500;
            int totalTicks = 10;

            world.Create(new DisplacementState
            {
                TargetEntity = target,
                SourceEntity = source,
                DirectionMode = DisplacementDirectionMode.AwayFromSource,
                TotalDistanceCm = totalDist,
                RemainingDistanceCm = Fix64.FromInt(totalDist),
                TotalDurationTicks = totalTicks,
                RemainingTicks = totalTicks,
                OverrideNavigation = true,
            });

            var system = new DisplacementRuntimeSystem(world);
            for (int i = 0; i < totalTicks; i++)
            {
                system.Update(0f);
            }

            var finalPos = world.Get<WorldPositionCm>(target).Value;
            That(finalPos.X.ToFloat(), Is.EqualTo(1500f).Within(5f));
        }

        [Test]
        public void Displacement_TowardSource_PullsTarget()
        {
            using var world = World.Create();

            var source = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var target = world.Create(new WorldPositionCm { Value = Fix64Vec2.FromInt(2000, 0) });

            int totalDist = 600;
            int totalTicks = 10;

            world.Create(new DisplacementState
            {
                TargetEntity = target,
                SourceEntity = source,
                DirectionMode = DisplacementDirectionMode.TowardSource,
                TotalDistanceCm = totalDist,
                RemainingDistanceCm = Fix64.FromInt(totalDist),
                TotalDurationTicks = totalTicks,
                RemainingTicks = totalTicks,
                OverrideNavigation = true,
            });

            var system = new DisplacementRuntimeSystem(world);
            for (int i = 0; i < totalTicks; i++)
            {
                system.Update(0f);
            }

            var finalPos = world.Get<WorldPositionCm>(target).Value;
            That(finalPos.X.ToFloat(), Is.EqualTo(1400f).Within(5f));
        }

        [Test]
        public void Displacement_FixedDirection_MovesStraight()
        {
            using var world = World.Create();

            var source = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var target = world.Create(new WorldPositionCm { Value = Fix64Vec2.FromInt(500, 500) });

            int totalDist = 300;
            int totalTicks = 5;

            world.Create(new DisplacementState
            {
                TargetEntity = target,
                SourceEntity = source,
                DirectionMode = DisplacementDirectionMode.Fixed,
                FixedDirectionRad = Fix64.FromInt(90) * Fix64.Deg2Rad,
                TotalDistanceCm = totalDist,
                RemainingDistanceCm = Fix64.FromInt(totalDist),
                TotalDurationTicks = totalTicks,
                RemainingTicks = totalTicks,
                OverrideNavigation = true,
            });

            var system = new DisplacementRuntimeSystem(world);
            for (int i = 0; i < totalTicks; i++)
            {
                system.Update(0f);
            }

            var finalPos = world.Get<WorldPositionCm>(target).Value;
            That(finalPos.X.ToFloat(), Is.EqualTo(500f).Within(5f));
            That(finalPos.Y.ToFloat(), Is.EqualTo(800f).Within(5f));
        }

        [Test]
        public void Displacement_ToTarget_UsesResolvedTargetPoint()
        {
            using var world = World.Create();

            var source = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var mover = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });

            world.Create(new DisplacementState
            {
                TargetEntity = mover,
                SourceEntity = source,
                DirectionMode = DisplacementDirectionMode.ToTarget,
                TargetPointCm = Fix64Vec2.FromInt(300, 400),
                HasTargetPoint = true,
                TotalDistanceCm = 500,
                RemainingDistanceCm = Fix64.FromInt(500),
                TotalDurationTicks = 10,
                RemainingTicks = 10,
                OverrideNavigation = false,
            });

            var system = new DisplacementRuntimeSystem(world);
            for (int i = 0; i < 10; i++)
            {
                system.Update(0f);
            }

            var finalPos = world.Get<WorldPositionCm>(mover).Value;
            That(finalPos.X.ToFloat(), Is.EqualTo(300f).Within(5f));
            That(finalPos.Y.ToFloat(), Is.EqualTo(400f).Within(5f));
        }

        [Test]
        public void BuiltinHandler_ApplyDisplacement_UsesSourceTargetPointForToTarget()
        {
            using var world = World.Create();

            var source = world.Create(
                new WorldPositionCm { Value = Fix64Vec2.Zero },
                new AbilityExecInstance
                {
                    TargetPosCm = Fix64Vec2.FromInt(300, 400),
                    HasTargetPos = 1,
                });
            var target = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var context = new EffectContext { Source = source, Target = target, TargetContext = default };
            var template = new EffectTemplateData
            {
                Displacement = new DisplacementDescriptor
                {
                    DirectionMode = DisplacementDirectionMode.ToTarget,
                    TotalDistanceCm = 500,
                    TotalDurationTicks = 10,
                    OverrideNavigation = false,
                }
            };
            var mergedParams = default(EffectConfigParams);

            BuiltinHandlers.HandleApplyDisplacement(world, default, ref context, in mergedParams, in template);

            int count = 0;
            world.Query(in DisplacementQuery, (Entity _, ref DisplacementState state) =>
            {
                count++;
                That(state.DirectionMode, Is.EqualTo(DisplacementDirectionMode.ToTarget));
                That(state.HasTargetPoint, Is.True);
                That(state.TargetPointCm.X.ToFloat(), Is.EqualTo(300f).Within(0.01f));
                That(state.TargetPointCm.Y.ToFloat(), Is.EqualTo(400f).Within(0.01f));
            });

            That(count, Is.EqualTo(1));
        }

        [Test]
        public void BuiltinHandler_ApplyDisplacement_UsesPreservedCallerTargetPoint_WhenExecIsMissing()
        {
            using var world = World.Create();

            var source = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var target = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var context = new EffectContext { Source = source, Target = target, TargetContext = default };
            var template = new EffectTemplateData
            {
                Displacement = new DisplacementDescriptor
                {
                    DirectionMode = DisplacementDirectionMode.ToTarget,
                    TotalDistanceCm = 500,
                    TotalDurationTicks = 10,
                    OverrideNavigation = false,
                }
            };

            var mergedParams = default(EffectConfigParams);
            mergedParams.TryAddFloat(EffectParamKeys.TargetPosX, 420f);
            mergedParams.TryAddFloat(EffectParamKeys.TargetPosY, 180f);

            BuiltinHandlers.HandleApplyDisplacement(world, default, ref context, in mergedParams, in template);

            int count = 0;
            world.Query(in DisplacementQuery, (Entity _, ref DisplacementState state) =>
            {
                count++;
                That(state.DirectionMode, Is.EqualTo(DisplacementDirectionMode.ToTarget));
                That(state.HasTargetPoint, Is.True);
                That(state.TargetPointCm.X.ToFloat(), Is.EqualTo(420f).Within(0.01f));
                That(state.TargetPointCm.Y.ToFloat(), Is.EqualTo(180f).Within(0.01f));
            });

            That(count, Is.EqualTo(1));
        }

        [Test]
        public void EffectProposalProcessing_DisplacementInstant_RemainsEntityBacked()
        {
            using var world = World.Create();

            var templates = new EffectTemplateRegistry();
            int templateId = EffectTemplateIdRegistry.Register("Effect.Test.DisplacementEntityBacked");
            templates.Register(templateId, new EffectTemplateData
            {
                PresetType = EffectPresetType.Displacement,
                LifetimeKind = EffectLifetimeKind.Instant,
                Displacement = new DisplacementDescriptor
                {
                    DirectionMode = DisplacementDirectionMode.ToTarget,
                    TotalDistanceCm = 480,
                    TotalDurationTicks = 6,
                    OverrideNavigation = true,
                }
            });

            var source = world.Create();
            var target = world.Create();
            var requests = new EffectRequestQueue();
            requests.Publish(new EffectRequest
            {
                Source = source,
                Target = target,
                TemplateId = templateId
            });

            var proposal = new EffectProposalProcessingSystem(world, requests, budget: null, templates: templates);
            proposal.Update(0f);

            int effectCount = 0;
            world.Query(in EffectTemplateQuery, (Entity _, ref GameplayEffect _, ref EffectTemplateRef templateRef) =>
            {
                if (templateRef.TemplateId == templateId)
                {
                    effectCount++;
                }
            });

            That(effectCount, Is.EqualTo(1), "Displacement presets must stay entity-backed so OnApply builtins can execute.");
        }

        [Test]
        public void Displacement_OverrideNavigation_SuppressesMovementAuthorityWithoutMutatingNavigationState()
        {
            using var world = World.Create();

            int navMoveTagId = TagRegistry.Register("Ability.Nav.Move");
            var source = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var target = world.Create(
                new WorldPositionCm { Value = Fix64Vec2.FromInt(1000, 0) },
                new Position2D { Value = Fix64Vec2.FromInt(1000, 0) },
                new NavGoal2D
                {
                    Kind = NavGoalKind2D.Point,
                    TargetCm = Fix64Vec2.FromInt(2000, 0),
                    RadiusCm = Fix64.FromInt(25),
                },
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.FromInt(120, 0) },
                new ForceInput2D { Force = Fix64Vec2.FromInt(60, 0) },
                new GameplayTagContainer(),
                new AbilityExecInstance());
            ref var tags = ref world.Get<GameplayTagContainer>(target);
            tags.AddTag(navMoveTagId);

            world.Create(new DisplacementState
            {
                TargetEntity = target,
                SourceEntity = source,
                DirectionMode = DisplacementDirectionMode.AwayFromSource,
                TotalDistanceCm = 100,
                RemainingDistanceCm = Fix64.FromInt(100),
                TotalDurationTicks = 2,
                RemainingTicks = 2,
                OverrideNavigation = true,
            });

            var system = new DisplacementRuntimeSystem(world);
            system.Update(0f);

            That(world.Has<MovementSuppressed2D>(target), Is.True);
            That(world.Get<NavGoal2D>(target).Kind, Is.EqualTo(NavGoalKind2D.Point));
            That(world.Get<NavDesiredVelocity2D>(target).ValueCmPerSec.X.ToFloat(), Is.EqualTo(120f).Within(0.01f));
            That(world.Get<ForceInput2D>(target).Force.X.ToFloat(), Is.EqualTo(60f).Within(0.01f));
            That(world.Get<GameplayTagContainer>(target).HasTag(navMoveTagId), Is.True);
            That(world.Get<Position2D>(target).Value.X.ToFloat(), Is.EqualTo(1050f).Within(0.01f));
            That(world.Get<WorldPositionCm>(target).Value.X.ToFloat(), Is.EqualTo(1000f).Within(0.01f));

            system.Update(0f);

            That(world.Has<MovementSuppressed2D>(target), Is.False);
            var goal = world.Get<NavGoal2D>(target);
            That(goal.Kind, Is.EqualTo(NavGoalKind2D.Point));
            That(goal.TargetCm.X.ToFloat(), Is.EqualTo(2000f).Within(0.01f));
            That(goal.RadiusCm.ToFloat(), Is.EqualTo(25f).Within(0.01f));
            That(world.Get<NavDesiredVelocity2D>(target).ValueCmPerSec.X.ToFloat(), Is.EqualTo(120f).Within(0.01f));
            That(world.Get<ForceInput2D>(target).Force.X.ToFloat(), Is.EqualTo(60f).Within(0.01f));
            That(world.Get<GameplayTagContainer>(target).HasTag(navMoveTagId), Is.True);
            That(world.Get<Position2D>(target).Value.X.ToFloat(), Is.EqualTo(1100f).Within(0.01f));
            That(world.Get<WorldPositionCm>(target).Value.X.ToFloat(), Is.EqualTo(1000f).Within(0.01f));

            new Physics2DToWorldPositionSyncSystem(world).Update(0f);

            That(world.Get<WorldPositionCm>(target).Value.X.ToFloat(), Is.EqualTo(1100f).Within(0.01f));
        }

        [Test]
        public void Displacement_Completes_EntityDestroyed()
        {
            using var world = World.Create();

            var source = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var target = world.Create(new WorldPositionCm { Value = Fix64Vec2.FromInt(1000, 0) });

            var dispEntity = world.Create(new DisplacementState
            {
                TargetEntity = target,
                SourceEntity = source,
                DirectionMode = DisplacementDirectionMode.AwayFromSource,
                TotalDistanceCm = 100,
                RemainingDistanceCm = Fix64.FromInt(100),
                TotalDurationTicks = 5,
                RemainingTicks = 5,
                OverrideNavigation = true,
            });

            var system = new DisplacementRuntimeSystem(world);
            for (int i = 0; i < 5; i++)
            {
                system.Update(0f);
            }

            That(world.IsAlive(dispEntity), Is.False);
            That(world.IsAlive(target), Is.True);
        }

        [Test]
        public void Displacement_DeadTarget_ImmediateCleanup()
        {
            using var world = World.Create();

            var source = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var target = world.Create(new WorldPositionCm { Value = Fix64Vec2.FromInt(1000, 0) });

            var dispEntity = world.Create(new DisplacementState
            {
                TargetEntity = target,
                SourceEntity = source,
                DirectionMode = DisplacementDirectionMode.AwayFromSource,
                TotalDistanceCm = 500,
                RemainingDistanceCm = Fix64.FromInt(500),
                TotalDurationTicks = 20,
                RemainingTicks = 20,
                OverrideNavigation = true,
            });

            world.Destroy(target);

            var system = new DisplacementRuntimeSystem(world);
            system.Update(0f);

            That(world.IsAlive(dispEntity), Is.False);
        }
    }
}
