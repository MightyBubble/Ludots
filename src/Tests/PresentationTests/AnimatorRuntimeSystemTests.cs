using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class AnimatorRuntimeSystemTests
    {
        [Test]
        public void AnimatorRuntimeSystem_InitializesDefaultStateAndAdvancesLoopingTime()
        {
            using var world = World.Create();
            var controllers = new AnimatorControllerRegistry();
            int controllerId = controllers.Register(
                "hero.controller",
                new AnimatorControllerDefinition
                {
                    DefaultStateIndex = 0,
                    States =
                    [
                        new AnimatorStateDefinition { PackedStateIndex = 11, DurationSeconds = 1f, PlaybackSpeed = 1f, Loop = true },
                        new AnimatorStateDefinition { PackedStateIndex = 22, DurationSeconds = 0.5f, PlaybackSpeed = 1f, Loop = true },
                    ],
                    Transitions =
                    [
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 0,
                            ToStateIndex = 1,
                            ConditionKind = AnimatorConditionKind.FloatGreaterOrEqual,
                            ParameterIndex = 10,
                            Threshold = 0.5f,
                            DurationSeconds = 0.2f,
                        },
                    ],
                });

            var definitions = new PerformerDefinitionRegistry();
            int defId = RegisterAnimatorDefinition(definitions, controllerId, stateParamKey: 20);
            var instances = new PerformerInstanceBuffer(4);
            var animatorStates = new PerformerAnimatorStateBuffer(4);
            int handle = AllocateActiveAnimator(instances, world.Create(), defId);
            instances.SetParam(handle, 10, ParamLane.Float, 0.75f, 0, default);

            using var system = new AnimatorRuntimeSystem(world, controllers, instances, definitions, animatorStates);
            system.Update(0.1f);

            ref readonly AnimatorPackedState packedAfterFirstTick = ref animatorStates.GetPackedState(handle);
            Assert.That(packedAfterFirstTick.GetControllerId(), Is.EqualTo(controllerId));
            Assert.That(packedAfterFirstTick.GetPrimaryStateIndex(), Is.EqualTo(11));
            Assert.That(packedAfterFirstTick.GetSecondaryStateIndex(), Is.EqualTo(22));
            Assert.That((packedAfterFirstTick.GetFlags() & AnimatorPackedStateFlags.Active) != 0, Is.True);
            Assert.That((packedAfterFirstTick.GetFlags() & AnimatorPackedStateFlags.Looping) != 0, Is.True);
            Assert.That((packedAfterFirstTick.GetFlags() & AnimatorPackedStateFlags.InTransition) != 0, Is.True);
            Assert.That(packedAfterFirstTick.GetTransitionProgress01(), Is.EqualTo(0.5f).Within(0.05f));
            Assert.That(animatorStates.GetFeedbackBuffer(handle).Count, Is.GreaterThanOrEqualTo(2));

            system.Update(0.1f);

            ref readonly AnimatorRuntimeState runtime = ref animatorStates.GetRuntimeState(handle);
            ref readonly AnimatorPackedState packedAfterSecondTick = ref animatorStates.GetPackedState(handle);
            Assert.That(runtime.CurrentStateIndex, Is.EqualTo(1));
            Assert.That(runtime.IsTransitioning, Is.False);
            Assert.That(packedAfterSecondTick.GetPrimaryStateIndex(), Is.EqualTo(22));
            Assert.That(packedAfterSecondTick.GetSecondaryStateIndex(), Is.EqualTo(0));
            Assert.That((packedAfterSecondTick.GetFlags() & AnimatorPackedStateFlags.InTransition) == 0, Is.True);
            Assert.That(instances.ResolveInt(handle, 20), Is.EqualTo(1));
        }

        [Test]
        public void AnimatorRuntimeSystem_ConsumesTriggerTransitions_AndWritesAcceptanceArtifacts()
        {
            using var world = World.Create();
            var controllers = new AnimatorControllerRegistry();
            int controllerId = controllers.Register(
                "hero.attack",
                new AnimatorControllerDefinition
                {
                    DefaultStateIndex = 0,
                    States =
                    [
                        new AnimatorStateDefinition { PackedStateIndex = 5, DurationSeconds = 1f, PlaybackSpeed = 1f, Loop = true },
                        new AnimatorStateDefinition { PackedStateIndex = 9, DurationSeconds = 0.4f, PlaybackSpeed = 1f, Loop = false },
                    ],
                    Transitions =
                    [
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 0,
                            ToStateIndex = 1,
                            ConditionKind = AnimatorConditionKind.Trigger,
                            ParameterIndex = 12,
                            DurationSeconds = 0f,
                            ConsumeTrigger = true,
                        },
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 1,
                            ToStateIndex = 0,
                            ConditionKind = AnimatorConditionKind.AutoOnNormalizedTime,
                            Threshold = 1f,
                            DurationSeconds = 0f,
                        },
                    ],
                });

            var definitions = new PerformerDefinitionRegistry();
            int defId = RegisterAnimatorDefinition(definitions, controllerId, stateParamKey: 20);
            var instances = new PerformerInstanceBuffer(4);
            var animatorStates = new PerformerAnimatorStateBuffer(4);
            int handle = AllocateActiveAnimator(instances, world.Create(), defId);
            instances.SetParam(handle, 12, ParamLane.Int, 0f, 1, default);

            using var system = new AnimatorRuntimeSystem(world, controllers, instances, definitions, animatorStates);

            var trace = new StringBuilder();
            system.Update(0.1f);
            AppendTrace(trace, tick: 1, instances.Get(handle).StableId, animatorStates.GetPackedState(handle), animatorStates.GetRuntimeState(handle));

            Assert.That(animatorStates.GetPackedState(handle).GetPrimaryStateIndex(), Is.EqualTo(9));
            Assert.That(instances.ResolveInt(handle, 12), Is.EqualTo(0), "Trigger should be consumed through performer blackboard.");

            system.Update(0.4f);
            AppendTrace(trace, tick: 2, instances.Get(handle).StableId, animatorStates.GetPackedState(handle), animatorStates.GetRuntimeState(handle));

            Assert.That(animatorStates.GetPackedState(handle).GetPrimaryStateIndex(), Is.EqualTo(5));
            Assert.That(animatorStates.GetFeedbackBuffer(handle).GetNewest(0).Kind, Is.EqualTo(AnimatorFeedbackKind.TransitionStarted));

            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "animator-runtime-mvp");
            Directory.CreateDirectory(artifactDir);

            string tracePath = Path.Combine(artifactDir, "trace.jsonl");
            string battleReportPath = Path.Combine(artifactDir, "battle-report.md");
            string pathPath = Path.Combine(artifactDir, "path.mmd");

            File.WriteAllText(tracePath, trace.ToString().TrimEnd());
            File.WriteAllText(battleReportPath, BuildBattleReport(controllerId));
            File.WriteAllText(pathPath, BuildPathArtifact());

            Assert.That(File.Exists(tracePath), Is.True);
            Assert.That(File.Exists(battleReportPath), Is.True);
            Assert.That(File.Exists(pathPath), Is.True);
        }

        [Test]
        public void AnimatorRuntimeSystem_WritesFeedbackBackToBlackboard()
        {
            using var world = World.Create();
            var controllers = new AnimatorControllerRegistry();
            int controllerId = controllers.Register(
                "hero.feedback",
                new AnimatorControllerDefinition
                {
                    DefaultStateIndex = 0,
                    States =
                    [
                        new AnimatorStateDefinition { PackedStateIndex = 5, DurationSeconds = 1f, PlaybackSpeed = 1f, Loop = true },
                        new AnimatorStateDefinition { PackedStateIndex = 9, DurationSeconds = 0.4f, PlaybackSpeed = 1f, Loop = false },
                    ],
                    Transitions =
                    [
                        new AnimatorTransitionDefinition
                        {
                            FromStateIndex = 0,
                            ToStateIndex = 1,
                            ConditionKind = AnimatorConditionKind.Trigger,
                            ParameterIndex = 12,
                            DurationSeconds = 0.25f,
                            ConsumeTrigger = true,
                        },
                    ],
                });

            var definitions = new PerformerDefinitionRegistry();
            int stateParamKey = 20;
            int defId = RegisterAnimatorDefinition(definitions, controllerId, stateParamKey);
            var instances = new PerformerInstanceBuffer(4);
            var animatorStates = new PerformerAnimatorStateBuffer(4);
            int handle = AllocateActiveAnimator(instances, world.Create(), defId);
            instances.SetParam(handle, 12, ParamLane.Int, 0f, 1, default);

            using var system = new AnimatorRuntimeSystem(world, controllers, instances, definitions, animatorStates);
            system.Update(0.1f);

            Assert.That(instances.ResolveInt(handle, stateParamKey), Is.EqualTo(0));
            Assert.That(instances.ResolveInt(handle, stateParamKey + 1), Is.EqualTo((int)AnimatorFeedbackKind.TransitionStarted));
            Assert.That(instances.ResolveInt(handle, stateParamKey + 2), Is.EqualTo(0));
            Assert.That(instances.ResolveInt(handle, stateParamKey + 3), Is.EqualTo(1));
            Assert.That(instances.ResolveFloat(handle, stateParamKey + 4), Is.EqualTo(0.1f).Within(0.05f));
            Assert.That(instances.ResolveFloat(handle, stateParamKey + 5), Is.EqualTo(0.25f).Within(0.001f));

            system.Update(0.25f);

            Assert.That(instances.ResolveInt(handle, stateParamKey), Is.EqualTo(1));
            Assert.That(instances.ResolveInt(handle, stateParamKey + 1), Is.EqualTo((int)AnimatorFeedbackKind.TransitionCompleted));
            Assert.That(instances.ResolveInt(handle, stateParamKey + 2), Is.EqualTo(0));
            Assert.That(instances.ResolveInt(handle, stateParamKey + 3), Is.EqualTo(1));
            Assert.That(instances.ResolveFloat(handle, stateParamKey + 4), Is.EqualTo(1f).Within(0.001f));
            Assert.That(instances.ResolveFloat(handle, stateParamKey + 5), Is.EqualTo(0f).Within(0.001f));
        }

        private static int RegisterAnimatorDefinition(PerformerDefinitionRegistry definitions, int controllerId, int stateParamKey)
        {
            return definitions.Register("animated", new PerformerDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.Animator,
                        ActiveByDefault = true,
                        Animator = new AnimatorConfig
                        {
                            AnimatorControllerId = controllerId,
                            SpeedParamKey = -1,
                            StateParamKey = stateParamKey,
                        },
                    },
                ],
            });
        }

        private static int AllocateActiveAnimator(PerformerInstanceBuffer instances, Entity owner, int defId)
        {
            Assert.That(
                instances.TryAllocate(defId, owner, 0, PresentationAnchorKind.Entity, default, stableId: 7001, parentHandle: -1, out int handle),
                Is.True);
            instances.Get(handle).BehaviorActiveMask = 1u;
            return handle;
        }

        private static void AppendTrace(StringBuilder trace, int tick, int stableId, AnimatorPackedState packed, AnimatorRuntimeState runtime)
        {
            if (trace.Length > 0)
            {
                trace.AppendLine();
            }

            trace.Append(JsonSerializer.Serialize(new
            {
                tick,
                stable_id = stableId,
                controller_id = packed.GetControllerId(),
                primary_state = packed.GetPrimaryStateIndex(),
                secondary_state = packed.GetSecondaryStateIndex(),
                normalized_time = packed.GetNormalizedTime01(),
                transition_progress = packed.GetTransitionProgress01(),
                flags = packed.GetFlags().ToString(),
                runtime_state = runtime.CurrentStateIndex,
            }));
        }

        private static string BuildBattleReport(int controllerId)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Scenario: animator-runtime-mvp");
            sb.AppendLine();
            sb.AppendLine("## Header");
            sb.AppendLine("- scenario name: trigger-driven performer-blackboard animator runtime progression");
            sb.AppendLine("- build/version: local PresentationTests");
            sb.AppendLine("- seed/map/clock: deterministic unit fixture / in-memory world / 2 ticks");
            sb.AppendLine($"- controller id: {controllerId}");
            sb.AppendLine($"- execution timestamp: {DateTime.UtcNow:O}");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.AppendLine("- [T+001] blackboard trigger param #12 consumed -> attack state entered immediately");
            sb.AppendLine("- [T+002] attack clip reached end -> controller returned to idle");
            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success/failure decision: success");
            sb.AppendLine("- failed assertions: none");
            sb.AppendLine("- reason codes: trigger_consumed, state_progression_valid");
            return sb.ToString();
        }

        private static string BuildPathArtifact()
        {
            return
                """
                flowchart TD
                    A[start idle state] --> B{blackboard trigger param #12 set}
                    B -->|yes| C[consume trigger]
                    C --> D[enter attack state]
                    D --> E{normalized time >= 1.0}
                    E -->|yes| F[return to idle]
                    E -->|no| D
                """;
        }

        private static string FindRepoRoot()
        {
            string current = TestContext.CurrentContext.WorkDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "mods")) &&
                    File.Exists(Path.Combine(current, "AGENTS.md")))
                {
                    return current;
                }

                current = Path.GetDirectoryName(current)!;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }
    }
}
