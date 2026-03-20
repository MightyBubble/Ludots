using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
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
                            ParameterIndex = 0,
                            Threshold = 0.5f,
                            DurationSeconds = 0.2f,
                        },
                    ],
                });

            var entity = world.Create(
                VisualRuntimeState.Create(
                    meshAssetId: 7,
                    materialId: 1,
                    baseScale: 1f,
                renderPath: VisualRenderPath.SkinnedMesh,
                animatorControllerId: controllerId),
                AnimatorPackedState.Create(controllerId),
                AnimatorRuntimeState.Create(controllerId),
                default(AnimatorParameterBuffer),
                default(AnimatorFeedbackBuffer));

            ref var parameters = ref world.Get<AnimatorParameterBuffer>(entity);
            parameters.SetFloat(0, 0.75f);
            parameters.SetBool(3, true);

            using var system = new AnimatorRuntimeSystem(world, controllers);
            system.Update(0.1f);

            ref readonly var packedAfterFirstTick = ref world.Get<AnimatorPackedState>(entity);
            Assert.That(packedAfterFirstTick.GetControllerId(), Is.EqualTo(controllerId));
            Assert.That(packedAfterFirstTick.GetPrimaryStateIndex(), Is.EqualTo(11));
            Assert.That(packedAfterFirstTick.GetSecondaryStateIndex(), Is.EqualTo(22));
            Assert.That((packedAfterFirstTick.GetFlags() & AnimatorPackedStateFlags.Active) != 0, Is.True);
            Assert.That((packedAfterFirstTick.GetFlags() & AnimatorPackedStateFlags.Looping) != 0, Is.True);
            Assert.That((packedAfterFirstTick.GetFlags() & AnimatorPackedStateFlags.InTransition) != 0, Is.True);
            Assert.That(packedAfterFirstTick.GetParameterBit(3), Is.True);
            Assert.That(packedAfterFirstTick.GetTransitionProgress01(), Is.EqualTo(0.5f).Within(0.05f));
            Assert.That(world.Get<AnimatorFeedbackBuffer>(entity).Count, Is.GreaterThanOrEqualTo(2));

            system.Update(0.1f);

            ref readonly var runtime = ref world.Get<AnimatorRuntimeState>(entity);
            ref readonly var packedAfterSecondTick = ref world.Get<AnimatorPackedState>(entity);
            Assert.That(runtime.CurrentStateIndex, Is.EqualTo(1));
            Assert.That(runtime.IsTransitioning, Is.False);
            Assert.That(packedAfterSecondTick.GetPrimaryStateIndex(), Is.EqualTo(22));
            Assert.That(packedAfterSecondTick.GetSecondaryStateIndex(), Is.EqualTo(0));
            Assert.That((packedAfterSecondTick.GetFlags() & AnimatorPackedStateFlags.InTransition) == 0, Is.True);
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
                            ParameterIndex = 2,
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

            var entity = world.Create(
                new PresentationStableId { Value = 7001 },
                VisualRuntimeState.Create(
                    meshAssetId: 7,
                    materialId: 1,
                    baseScale: 1f,
                renderPath: VisualRenderPath.SkinnedMesh,
                animatorControllerId: controllerId),
                AnimatorPackedState.Create(controllerId),
                AnimatorRuntimeState.Create(controllerId),
                default(AnimatorParameterBuffer),
                default(AnimatorFeedbackBuffer));

            ref var parameters = ref world.Get<AnimatorParameterBuffer>(entity);
            parameters.SetTrigger(2);

            using var system = new AnimatorRuntimeSystem(world, controllers);

            var trace = new StringBuilder();
            system.Update(0.1f);
            AppendTrace(trace, tick: 1, world.Get<PresentationStableId>(entity).Value, world.Get<AnimatorPackedState>(entity), world.Get<AnimatorRuntimeState>(entity));

            Assert.That(world.Get<AnimatorPackedState>(entity).GetPrimaryStateIndex(), Is.EqualTo(9));
            Assert.That(world.Get<AnimatorParameterBuffer>(entity).HasTrigger(2), Is.False, "Trigger should be consumed by the attack transition.");

            system.Update(0.4f);
            AppendTrace(trace, tick: 2, world.Get<PresentationStableId>(entity).Value, world.Get<AnimatorPackedState>(entity), world.Get<AnimatorRuntimeState>(entity));

            Assert.That(world.Get<AnimatorPackedState>(entity).GetPrimaryStateIndex(), Is.EqualTo(5));
            Assert.That(world.Get<AnimatorFeedbackBuffer>(entity).GetNewest(0).Kind, Is.EqualTo(AnimatorFeedbackKind.TransitionCompleted));

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
            sb.AppendLine("- scenario name: trigger-driven animator runtime progression");
            sb.AppendLine("- build/version: local PresentationTests");
            sb.AppendLine("- seed/map/clock: deterministic unit fixture / in-memory world / 2 ticks");
            sb.AppendLine($"- controller id: {controllerId}");
            sb.AppendLine($"- execution timestamp: {DateTime.UtcNow:O}");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.AppendLine("- [T+001] trigger bit #2 consumed -> attack state entered immediately");
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
                    A[start idle state] --> B{trigger bit #2 set}
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
