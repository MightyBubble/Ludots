using System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.LiveSkillWorkbench;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class LiveGasEditPipelineTests
    {
        private const string GraphKey = "Graph.Live.HotConst";

        [SetUp]
        public void SetUp()
        {
            GraphIdRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
        }

        [Test]
        [Category("ci-gate")]
        public void ClassifyAndCommit_GraphBodyReplace_NextCast_ReplacesProgramWithoutClear()
        {
            int graphId = GraphIdRegistry.Register(GraphKey);
            var graphs = new GraphProgramRegistry();
            GraphInstruction[] original =
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            graphs.Register(graphId, original, GraphKind.Script);

            var pipeline = new LiveGasEditPipeline(graphs);
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench);
            var provenance = new LiveEditProvenance(
                LiveEditSource.ManualWorkbench,
                sourceUri: "workbench://graph/" + GraphKey);

            string documentJson = $$"""
                {
                  "id": "{{GraphKey}}",
                  "kind": "Script",
                  "entry": "c",
                  "nodes": [
                    { "id": "c", "op": "ConstInt", "intValue": 42 },
                    { "id": "h", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "c", "fromPort": "next", "to": "h" }
                  ],
                  "valueEdges": [
                    { "from": "c", "fromPort": "value", "to": "h", "toPort": "value" }
                  ]
                }
                """;

            LiveEditStageResult stage = session.TryStage(
                LiveDebugPatchOperation.GraphBodyReplace(GraphKey, documentJson, provenance));
            That(stage.Succeeded, Is.True);

            LiveApplyClassificationReport report = pipeline.Classify(session);
            That(report.CanCommitNextCast, Is.True);
            That(report.RequiresEngineRestart, Is.False);
            That(report.Items[0].Mode, Is.EqualTo(LiveApplyMode.NextCastLiveApply));

            // Without safe frame → fail closed
            LiveApplyCommitResult denied = pipeline.CommitNextCastSafeFrame();
            That(denied.Succeeded, Is.False);
            That(denied.Diagnostics[0].Code, Is.EqualTo(LiveEditDiagnosticCodes.SafeFrameRequired));

            // Live still original
            That(graphs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> before), Is.True);
            That(ExecuteReturn(before), Is.EqualTo(1));

            pipeline.BeginSafeFrame();
            LiveApplyCommitResult committed = pipeline.CommitNextCastSafeFrame();
            pipeline.EndSafeFrame();
            That(committed.Succeeded, Is.True);
            That(committed.AppliedCount, Is.EqualTo(1));

            That(graphs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> after), Is.True);
            That(ExecuteReturn(after), Is.EqualTo(42));
        }

        [Test]
        [Category("ci-gate")]
        public void Classify_GraphKindChange_RequiresEngineRestart_DoesNotTouchLive()
        {
            int graphId = GraphIdRegistry.Register(GraphKey);
            var graphs = new GraphProgramRegistry();
            // Live identity is Score; candidate below is Script → EngineRestartRequired.
            graphs.Register(graphId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, Imm = BitConverter.SingleToInt32Bits(1f) },
            }, GraphKind.Score);

            var pipeline = new LiveGasEditPipeline(graphs);
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.FileChange);
            string documentJson = $$"""
                {
                  "id": "{{GraphKey}}",
                  "kind": "Script",
                  "entry": "c",
                  "nodes": [
                    { "id": "c", "op": "ConstInt", "intValue": 3 },
                    { "id": "h", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "c", "fromPort": "next", "to": "h" }
                  ],
                  "valueEdges": [
                    { "from": "c", "fromPort": "value", "to": "h", "toPort": "value" }
                  ]
                }
                """;

            That(session.TryStage(LiveDebugPatchOperation.GraphBodyReplace(
                GraphKey,
                documentJson,
                new LiveEditProvenance(LiveEditSource.FileChange, "file://graphs.json"))).Succeeded, Is.True);

            LiveApplyClassificationReport report = pipeline.Classify(session);
            That(report.RequiresEngineRestart, Is.True);
            That(report.CanCommitNextCast, Is.False);
            That(graphs.TryGetKind(graphId, out GraphKind kind), Is.True);
            That(kind, Is.EqualTo(GraphKind.Score));
        }

        [Test]
        [Category("ci-gate")]
        public void ClassifyAndCommit_EffectDurationTicks_NextCast()
        {
            string effectName = "Effect.Live.HotDuration";
            int templateId = EffectTemplateIdRegistry.Register(effectName);
            var effects = new EffectTemplateRegistry();
            effects.Register(templateId, new EffectTemplateData { DurationTicks = 10, PeriodTicks = 0 });

            var graphs = new GraphProgramRegistry();
            var pipeline = new LiveGasEditPipeline(graphs, effects);
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench);
            That(session.TryStage(LiveDebugPatchOperation.SkillEffectNumeric(
                effectName,
                "duration.durationTicks",
                25d,
                new LiveEditProvenance(LiveEditSource.ManualWorkbench, "workbench://effect/" + effectName))).Succeeded,
                Is.True);

            LiveApplyClassificationReport report = pipeline.Classify(session);
            That(report.CanCommitNextCast, Is.True);
            That(report.Items[0].Mode, Is.EqualTo(LiveApplyMode.NextCastLiveApply));

            pipeline.BeginSafeFrame();
            LiveApplyCommitResult committed = pipeline.CommitNextCastSafeFrame();
            pipeline.EndSafeFrame();
            That(committed.Succeeded, Is.True);
            That(effects.TryGet(templateId, out EffectTemplateData data), Is.True);
            That(data.DurationTicks, Is.EqualTo(25));
        }

        [Test]
        [Category("ci-gate")]
        public void Classify_UnknownEffectField_MapReloadRequired()
        {
            string effectName = "Effect.Live.UnknownField";
            int templateId = EffectTemplateIdRegistry.Register(effectName);
            var effects = new EffectTemplateRegistry();
            effects.Register(templateId, new EffectTemplateData { DurationTicks = 1 });

            var pipeline = new LiveGasEditPipeline(new GraphProgramRegistry(), effects);
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench);
            That(session.TryStage(LiveDebugPatchOperation.SkillEffectNumeric(
                effectName,
                "damage",
                99d,
                new LiveEditProvenance(LiveEditSource.ManualWorkbench, "workbench://effect/damage"))).Succeeded,
                Is.True);

            LiveApplyClassificationReport report = pipeline.Classify(session);
            That(report.RequiresMapReload, Is.True);
            That(report.CanCommitNextCast, Is.False);
            That(report.Items[0].Mode, Is.EqualTo(LiveApplyMode.MapReloadRequired));
        }

        [Test]
        [Category("ci-gate")]
        public void CommitImmediate_Attribute_UsesSink()
        {
            var pipeline = new LiveGasEditPipeline(new GraphProgramRegistry());
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench);
            That(session.TryStage(LiveDebugPatchOperation.SelectedActorAttribute(
                ActorTargetSelection.FromEntityIdSurrogate(7),
                "Health",
                ActorAttributeMutationKind.Set,
                100d,
                new LiveEditProvenance(LiveEditSource.ManualWorkbench, "workbench://actor/7/health"))).Succeeded,
                Is.True);

            LiveApplyClassificationReport report = pipeline.Classify(session);
            That(report.CanCommitImmediate, Is.True);
            That(report.Items[0].Mode, Is.EqualTo(LiveApplyMode.ImmediateCommand));

            var sink = new RecordingSink();
            LiveApplyCommitResult result = pipeline.CommitImmediate(sink);
            That(result.Succeeded, Is.True);
            That(result.AppliedCount, Is.EqualTo(1));
            That(sink.LastAttribute, Is.EqualTo("Health"));
            That(sink.LastValue, Is.EqualTo(100d));
        }

        private sealed class RecordingSink : ILiveAttributeCommandSink
        {
            public string? LastAttribute;
            public double LastValue;

            public void Apply(in LiveDebugPatchOperation operation)
            {
                LastAttribute = operation.AttributeName;
                LastValue = operation.NumericValue;
            }
        }

        private static int ExecuteReturn(ReadOnlySpan<GraphInstruction> program)
        {
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Arch.Core.Entity> e = stackalloc Arch.Core.Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Arch.Core.Entity> targets = stackalloc Arch.Core.Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var state = new GraphExecutionState
            {
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                CallStack = callStack,
            };
            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
            return state.ReturnInt;
        }
    }
}
