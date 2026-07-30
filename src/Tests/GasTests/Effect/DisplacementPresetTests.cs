using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;
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
        public void Displacement_WithPhysicsPosition_SynchronizesWorldPosition()
        {
            using var world = World.Create();

            var source = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var mover = world.Create(
                new WorldPositionCm { Value = Fix64Vec2.Zero },
                Position2D.Zero);

            world.Create(new DisplacementState
            {
                TargetEntity = mover,
                SourceEntity = source,
                DirectionMode = DisplacementDirectionMode.Fixed,
                FixedDirectionRad = Fix64.Zero,
                TotalDistanceCm = 120,
                RemainingDistanceCm = Fix64.FromInt(120),
                TotalDurationTicks = 3,
                RemainingTicks = 3,
                OverrideNavigation = false,
            });

            var system = new DisplacementRuntimeSystem(world);
            for (int i = 0; i < 3; i++)
            {
                system.Update(0f);
            }

            var worldPos = world.Get<WorldPositionCm>(mover).Value;
            var physicsPos = world.Get<Position2D>(mover).Value;
            That(worldPos.X.ToFloat(), Is.EqualTo(120f).Within(0.01f));
            That(physicsPos.X.ToFloat(), Is.EqualTo(120f).Within(0.01f));
            That(worldPos.Y.ToFloat(), Is.EqualTo(physicsPos.Y.ToFloat()).Within(0.01f));
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
        public void EffectProposalProcessing_DisplacementInstant_ExecutesWithoutEffectEntity()
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
                },
            });

            var presetTypes = new PresetTypeRegistry();
            var preset = new PresetTypeDefinition
            {
                Type = EffectPresetType.Displacement,
                ActivePhases = PhaseFlags.InstantCore,
                AllowedLifetimes = LifetimeFlags.InstantOnly,
            };
            preset.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.ApplyDisplacement);
            presetTypes.Register(in preset);
            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            var graphPrograms = new GraphProgramRegistry();
            EffectExecutionPlanCompiler.FinalizeAll(
                templates,
                presetTypes,
                builtinHandlers,
                graphPrograms,
                GasGraphOpHandlerTable.Instance,
                "Test/displacement-effects.json");
            var phaseExecutor = new EffectPhaseExecutor(
                graphPrograms,
                presetTypes,
                builtinHandlers,
                GasGraphOpHandlerTable.Instance,
                templates);
            var graphApi = new GasGraphRuntimeApi(world);

            var source = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var target = world.Create(
                new WorldPositionCm { Value = Fix64Vec2.FromInt(100, 0) });
            var requests = new EffectRequestQueue();
            requests.Publish(new EffectRequest
            {
                Source = source,
                Target = target,
                TemplateId = templateId
            });

            var proposal = new EffectProposalProcessingSystem(
                world,
                requests,
                GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                new Ludots.Core.Engine.DiscreteClock(),
                budget: null,
                templates: templates,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                phaseExecutor: phaseExecutor,
                graphApi: graphApi);
            proposal.Update(0f);

            int effectCount = 0;
            world.Query(in EffectTemplateQuery, (Entity _, ref GameplayEffect _, ref EffectTemplateRef templateRef) =>
            {
                if (templateRef.TemplateId == templateId)
                {
                    effectCount++;
                }
            });

            That(effectCount, Is.EqualTo(0), "Instant behavior must execute through the shared phase executor without a transient Effect entity.");
            int displacementCount = 0;
            world.Query(in DisplacementQuery, (Entity _, ref DisplacementState state) =>
            {
                if (state.TargetEntity == target) displacementCount++;
            });
            That(displacementCount, Is.EqualTo(1));
        }

        [TestCase(0, TestName = "EffectProposalProcessing_DisplacementWithTargetListener_FailsBeforeMutation")]
        [TestCase(1, TestName = "EffectProposalProcessing_DisplacementWithSourceListener_FailsBeforeMutation")]
        [TestCase(2, TestName = "EffectProposalProcessing_DisplacementWithGlobalListener_FailsBeforeMutation")]
        public void EffectProposalProcessing_DisplacementWithMatchingListener_FailsBeforeMutation(int listenerLocation)
        {
            using var world = World.Create();

            var templates = new EffectTemplateRegistry();
            int templateId = EffectTemplateIdRegistry.Register("Effect.Test.DisplacementListenerConflict");
            templates.Register(templateId, new EffectTemplateData
            {
                PresetType = EffectPresetType.Displacement,
                LifetimeKind = EffectLifetimeKind.Instant,
                Displacement = new DisplacementDescriptor
                {
                    DirectionMode = DisplacementDirectionMode.AwayFromSource,
                    TotalDistanceCm = 300,
                    TotalDurationTicks = 3,
                },
            });

            var presetTypes = new PresetTypeRegistry();
            var preset = new PresetTypeDefinition
            {
                Type = EffectPresetType.Displacement,
                ActivePhases = PhaseFlags.InstantCore,
                AllowedLifetimes = LifetimeFlags.InstantOnly,
            };
            preset.DefaultPhaseHandlers[EffectPhaseId.OnApply] =
                PhaseHandler.Builtin(BuiltinHandlerId.ApplyDisplacement);
            presetTypes.Register(in preset);

            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            var graphPrograms = new GraphProgramRegistry();
            EffectExecutionPlanCompiler.FinalizeAll(
                templates,
                presetTypes,
                builtinHandlers,
                graphPrograms,
                GasGraphOpHandlerTable.Instance,
                "Test/displacement-listener-effects.json");

            var globalListeners = new GlobalPhaseListenerRegistry();
            if (listenerLocation == 2)
            {
                Assert.That(globalListeners.Register(
                    listenTagId: 0,
                    listenEffectId: templateId,
                    EffectPhaseId.OnApply,
                    PhaseListenerActionFlags.PublishEvent,
                    graphProgramId: 0,
                    eventTagId: 1,
                    priority: 0), Is.True);
            }
            var phaseExecutor = new EffectPhaseExecutor(
                graphPrograms,
                presetTypes,
                builtinHandlers,
                GasGraphOpHandlerTable.Instance,
                templates,
                globalListeners);
            var graphApi = new GasGraphRuntimeApi(world);
            var source = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var target = world.Create(new WorldPositionCm { Value = Fix64Vec2.FromInt(100, 0) });
            if (listenerLocation < 2)
            {
                var listener = new EffectPhaseListenerBuffer();
                PhaseListenerScope scope = listenerLocation == 0
                    ? PhaseListenerScope.Target
                    : PhaseListenerScope.Source;
                Assert.That(listener.TryAddTemplate(
                    listenTagId: 0,
                    listenEffectId: templateId,
                    EffectPhaseId.OnApply,
                    scope,
                    PhaseListenerActionFlags.PublishEvent,
                    graphProgramId: 0,
                    eventTagId: 1,
                    priority: 0), Is.True);
                world.Add(listenerLocation == 0 ? target : source, listener);
            }
            var requests = new EffectRequestQueue();
            requests.Publish(new EffectRequest
            {
                Source = source,
                Target = target,
                TemplateId = templateId,
            });
            using var proposal = new EffectProposalProcessingSystem(
                world,
                requests,
                GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                new Ludots.Core.Engine.DiscreteClock(),
                templates: templates,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                phaseExecutor: phaseExecutor,
                graphApi: graphApi);

            var error = Assert.Throws<InvalidOperationException>(() => proposal.Update(0f));

            Assert.That(error!.Message, Does.StartWith(EffectPhaseExecutor.ExternalAtomicListenerConflictError));
            int displacementCount = 0;
            world.Query(in DisplacementQuery, (ref DisplacementState _) => displacementCount++);
            Assert.That(displacementCount, Is.Zero);
        }

        [Test]
        public void Displacement_OverrideNavigation_InstallsAndClearsMovementSuppression()
        {
            using var world = World.Create();

            var source = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var target = world.Create(
                new WorldPositionCm { Value = Fix64Vec2.FromInt(1000, 0) },
                new ForceInput2D { Force = Fix64Vec2.FromInt(60, 0) });

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

            system.Update(0f);

            That(world.Has<MovementSuppressed2D>(target), Is.False);
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
