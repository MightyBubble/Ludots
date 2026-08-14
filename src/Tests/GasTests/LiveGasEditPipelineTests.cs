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
            TagRegistry.Clear();
            AttributeRegistry.Clear();
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

            var pipeline = new LiveGasEditPipeline(graphs, new GraphFunctionCatalog());
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

            var pipeline = new LiveGasEditPipeline(graphs, new GraphFunctionCatalog());
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
        public void Classify_FuncLibGraphBodyReplaceThatReachesYield_MapReloadRequired()
        {
            const string graphKey = "Graph.Live.FuncLibHotPure";
            int graphId = GraphIdRegistry.Register(graphKey);
            var graphs = new GraphProgramRegistry();
            graphs.Register(graphId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            }, GraphKind.Script);

            var functions = new GraphFunctionCatalog();
            functions.Register("script.hotPure", graphId, GraphKind.Script);
            var pipeline = new LiveGasEditPipeline(graphs, functions);
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench);
            string documentJson = $$"""
                {
                  "id": "{{graphKey}}",
                  "kind": "Script",
                  "entry": "wait",
                  "nodes": [
                    { "id": "wait", "op": "Yield" },
                    { "id": "h", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "wait", "fromPort": "next", "to": "h" }
                  ],
                  "valueEdges": []
                }
                """;

            That(session.TryStage(LiveDebugPatchOperation.GraphBodyReplace(
                graphKey,
                documentJson,
                new LiveEditProvenance(LiveEditSource.ManualWorkbench, "workbench://graph/" + graphKey))).Succeeded,
                Is.True);

            LiveApplyClassificationReport report = pipeline.Classify(session);

            That(report.CanCommitNextCast, Is.False);
            That(report.RequiresMapReload, Is.True);
            That(report.Items[0].Mode, Is.EqualTo(LiveApplyMode.MapReloadRequired));
            That(report.Items[0].Diagnostics[0].Code, Is.EqualTo(LiveEditDiagnosticCodes.GraphCompileFailed));
            That(report.Items[0].Diagnostics[0].Message, Does.Contain("FuncLib graph"));
            That(report.Items[0].Diagnostics[0].Message, Does.Contain("Yield@pc"));
        }

        [Test]
        [Category("ci-gate")]
        public void Classify_GraphBodyReplaceThatCreatesInvokeCycle_MapReloadRequired()
        {
            const string graphKey = "Graph.Live.CycleHot";
            int graphId = GraphIdRegistry.Register(graphKey);
            var graphs = new GraphProgramRegistry();
            graphs.Register(graphId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            }, GraphKind.Script);

            var pipeline = new LiveGasEditPipeline(graphs, new GraphFunctionCatalog());
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench);
            string documentJson = $$"""
                {
                  "id": "{{graphKey}}",
                  "kind": "Script",
                  "entry": "invoke",
                  "nodes": [
                    { "id": "invoke", "op": "InvokeScript", "graphId": {{graphId}} },
                    { "id": "h", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "invoke", "fromPort": "next", "to": "h" }
                  ],
                  "valueEdges": [
                    { "from": "invoke", "fromPort": "value", "to": "h", "toPort": "value" }
                  ]
                }
                """;

            That(session.TryStage(LiveDebugPatchOperation.GraphBodyReplace(
                graphKey,
                documentJson,
                new LiveEditProvenance(LiveEditSource.ManualWorkbench, "workbench://graph/" + graphKey))).Succeeded,
                Is.True);

            LiveApplyClassificationReport report = pipeline.Classify(session);

            That(report.CanCommitNextCast, Is.False);
            That(report.RequiresMapReload, Is.True);
            That(report.Items[0].Mode, Is.EqualTo(LiveApplyMode.MapReloadRequired));
            That(report.Items[0].Diagnostics[0].Message, Does.Contain("GAS.GRAPH.ERR.InvokeCycle"));
            That(graphs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> live), Is.True);
            That(live[0].Op, Is.EqualTo((ushort)GraphNodeOp.ConstInt));
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
            var pipeline = new LiveGasEditPipeline(graphs, new GraphFunctionCatalog(), effects);
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

            var pipeline = new LiveGasEditPipeline(new GraphProgramRegistry(), new GraphFunctionCatalog(), effects);
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
            var pipeline = new LiveGasEditPipeline(new GraphProgramRegistry(), new GraphFunctionCatalog());
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

        [Test]
        [Category("ci-gate")]
        public void ClassifyAndCommit_TagRuleBody_NextCast_ReplacesWithoutNewIdentity()
        {
            int slowId = TagRegistry.Register("State.Slowed");
            int immuneId = TagRegistry.Register("State.SlowImmune");
            var rules = new TagRuleRegistry();
            var tagOps = new TagOps(new DirtyEntityQueue(8), rules);
            // Initial rule: empty disabledIf
            tagOps.RegisterTagRuleSet(slowId, default);
            That(tagOps.HasTagRule(slowId), Is.True);

            var pipeline = new LiveGasEditPipeline(new GraphProgramRegistry(), new GraphFunctionCatalog(), effects: null, tagOps);
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench);
            string documentJson = $$"""
                {
                  "disabledIfAny": [ "State.SlowImmune" ]
                }
                """;

            That(session.TryStage(LiveDebugPatchOperation.TagRuleBodyReplace(
                "State.Slowed",
                documentJson,
                new LiveEditProvenance(LiveEditSource.ManualWorkbench, "workbench://tag/State.Slowed"))).Succeeded,
                Is.True);

            LiveApplyClassificationReport report = pipeline.Classify(session);
            That(report.CanCommitNextCast, Is.True);
            That(report.RequiresEngineRestart, Is.False);
            That(report.Items[0].Mode, Is.EqualTo(LiveApplyMode.NextCastLiveApply));

            pipeline.BeginSafeFrame();
            LiveApplyCommitResult committed = pipeline.CommitNextCastSafeFrame();
            pipeline.EndSafeFrame();
            That(committed.Succeeded, Is.True);

            ref readonly TagRuleCompiled compiled = ref rules.Get(slowId);
            That(compiled.DisabledIfAny, Is.Not.EqualTo(0uL));
            That(TagRegistry.GetId("State.SlowImmune"), Is.EqualTo(immuneId));
        }

        [Test]
        [Category("ci-gate")]
        public void Classify_UnknownTagKey_RequiresEngineRestart()
        {
            var tagOps = new TagOps(new DirtyEntityQueue(8), new TagRuleRegistry());
            var pipeline = new LiveGasEditPipeline(new GraphProgramRegistry(), new GraphFunctionCatalog(), effects: null, tagOps);
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.FileChange);
            That(session.TryStage(LiveDebugPatchOperation.TagRuleBodyReplace(
                "State.NeverRegistered",
                """{ "attached": [] }""",
                new LiveEditProvenance(LiveEditSource.FileChange, "file://tag_rules.json"))).Succeeded,
                Is.True);

            LiveApplyClassificationReport report = pipeline.Classify(session);
            That(report.RequiresEngineRestart, Is.True);
            That(report.CanCommitNextCast, Is.False);
            That(TagRegistry.GetId("State.NeverRegistered"), Is.EqualTo(TagRegistry.InvalidId));
        }

        [Test]
        [Category("ci-gate")]
        public void ClassifyAndCommit_AttrConstraintMax_NextCast()
        {
            int healthId = AttributeRegistry.Register("Health");
            AttributeRegistry.SetConstraints(
                healthId,
                AttributeRegistry.AttributeConstraints.Create(
                    clampToBase: false,
                    hasMin: true,
                    min: 0f,
                    hasMax: true,
                    max: 100f));

            var pipeline = new LiveGasEditPipeline(new GraphProgramRegistry(), new GraphFunctionCatalog());
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench);
            That(session.TryStage(LiveDebugPatchOperation.AttrConstraintNumeric(
                "Health",
                "constraints.max",
                200d,
                new LiveEditProvenance(LiveEditSource.ManualWorkbench, "workbench://attr/Health/max"))).Succeeded,
                Is.True);

            LiveApplyClassificationReport report = pipeline.Classify(session);
            That(report.CanCommitNextCast, Is.True);
            That(report.Items[0].Mode, Is.EqualTo(LiveApplyMode.NextCastLiveApply));

            pipeline.BeginSafeFrame();
            LiveApplyCommitResult committed = pipeline.CommitNextCastSafeFrame();
            pipeline.EndSafeFrame();
            That(committed.Succeeded, Is.True);
            That(AttributeRegistry.TryGetConstraints(healthId, out var constraints), Is.True);
            That(constraints.Max, Is.EqualTo(200f));
            That(constraints.HasMin, Is.True);
            That(constraints.Min, Is.EqualTo(0f));
        }

        [Test]
        [Category("ci-gate")]
        public void Classify_UnknownAttribute_RequiresEngineRestart()
        {
            var pipeline = new LiveGasEditPipeline(new GraphProgramRegistry(), new GraphFunctionCatalog());
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench);
            That(session.TryStage(LiveDebugPatchOperation.AttrConstraintNumeric(
                "Attr.DoesNotExist",
                "constraints.max",
                10d,
                new LiveEditProvenance(LiveEditSource.ManualWorkbench, "workbench://attr/missing"))).Succeeded,
                Is.True);

            LiveApplyClassificationReport report = pipeline.Classify(session);
            That(report.RequiresEngineRestart, Is.True);
            That(report.CanCommitNextCast, Is.False);
            That(AttributeRegistry.GetId("Attr.DoesNotExist"), Is.EqualTo(AttributeRegistry.InvalidId));
        }

        [Test]
        [Category("ci-gate")]
        public void CommitNextCast_PartialFailure_RollsBackAllCandidates()
        {
            string okEffect = "Effect.Live.AtomicOk";
            string badEffect = "Effect.Live.AtomicBad";
            int okId = EffectTemplateIdRegistry.Register(okEffect);
            int badId = EffectTemplateIdRegistry.Register(badEffect);
            var effects = new EffectTemplateRegistry();
            effects.Register(okId, new EffectTemplateData { DurationTicks = 10, PeriodTicks = 0 });
            effects.Register(badId, new EffectTemplateData { DurationTicks = 3, PeriodTicks = 0 });

            var pipeline = new LiveGasEditPipeline(new GraphProgramRegistry(), new GraphFunctionCatalog(), effects);
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench);
            var provenance = new LiveEditProvenance(LiveEditSource.ManualWorkbench, "workbench://atomic");
            That(session.TryStage(LiveDebugPatchOperation.SkillEffectNumeric(
                okEffect, "duration.durationTicks", 99d, provenance)).Succeeded, Is.True);
            // Unknown field → Classify as MapReload, so force-stage a NextCast candidate then break at commit
            // by classifying only the ok path, then manually... instead: second op is valid classify path
            // but second template will fail replace via unsupported field after we use a staged path that
            // Classify accepts: use EffectTemplateRef with bad field to get MapReload. For atomic commit,
            // stage two duration ops then replace second template's field path by committing a mix where
            // the second TryReplace fails: use modifiers.0.value on template with zero modifiers.
            That(session.TryStage(LiveDebugPatchOperation.SkillEffectNumeric(
                badEffect, "modifiers.0.value", 1d, provenance)).Succeeded, Is.True);

            LiveApplyClassificationReport report = pipeline.Classify(session);
            That(report.CanCommitNextCast, Is.True);

            pipeline.BeginSafeFrame();
            LiveApplyCommitResult committed = pipeline.CommitNextCastSafeFrame();
            pipeline.EndSafeFrame();

            That(committed.Succeeded, Is.False);
            That(committed.AppliedCount, Is.EqualTo(0));
            That(committed.Diagnostics[0].Code, Is.EqualTo(LiveEditDiagnosticCodes.CommitRolledBack));
            That(effects.TryGet(okId, out EffectTemplateData okData), Is.True);
            That(okData.DurationTicks, Is.EqualTo(10));
            That(effects.TryGet(badId, out EffectTemplateData badData), Is.True);
            That(badData.DurationTicks, Is.EqualTo(3));
        }

        [Test]
        [Category("ci-gate")]
        public void Classify_UnknownGrantedTag_RequiresEngineRestart_DoesNotRegister()
        {
            string effectName = "Effect.Live.GrantedTag";
            int templateId = EffectTemplateIdRegistry.Register(effectName);
            var effects = new EffectTemplateRegistry();
            effects.Register(templateId, new EffectTemplateData { DurationTicks = 1 });

            var pipeline = new LiveGasEditPipeline(new GraphProgramRegistry(), new GraphFunctionCatalog(), effects);
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench);
            That(session.TryStage(LiveDebugPatchOperation.EffectGrantedTag(
                effectName,
                "State.NeverSeenInRegistry",
                1,
                new LiveEditProvenance(LiveEditSource.ManualWorkbench, "workbench://granted"))).Succeeded,
                Is.True);

            LiveApplyClassificationReport report = pipeline.Classify(session);
            That(report.RequiresEngineRestart, Is.True);
            That(report.CanCommitNextCast, Is.False);
            That(TagRegistry.GetId("State.NeverSeenInRegistry"), Is.EqualTo(TagRegistry.InvalidId));
        }

        [Test]
        [Category("ci-gate")]
        public void Commit_EffectTemplateRef_ImpactDoesNotMutateHit()
        {
            string launch = "Effect.Live.Launch";
            string impact = "Effect.Live.Impact";
            string hit = "Effect.Live.Hit";
            string ice = "Effect.Live.Ice";
            int launchId = EffectTemplateIdRegistry.Register(launch);
            int impactId = EffectTemplateIdRegistry.Register(impact);
            int hitId = EffectTemplateIdRegistry.Register(hit);
            int iceId = EffectTemplateIdRegistry.Register(ice);
            var effects = new EffectTemplateRegistry();
            effects.Register(impactId, new EffectTemplateData { DurationTicks = 1 });
            effects.Register(hitId, new EffectTemplateData { DurationTicks = 1 });
            effects.Register(iceId, new EffectTemplateData { DurationTicks = 1 });
            var launchData = new EffectTemplateData
            {
                PresetType = EffectPresetType.LaunchProjectile,
                DurationTicks = 1,
                Projectile = new ProjectileDescriptor
                {
                    ImpactEffectTemplateId = impactId,
                    HitEffectTemplateId = hitId
                }
            };
            effects.Register(launchId, in launchData);

            var pipeline = new LiveGasEditPipeline(new GraphProgramRegistry(), new GraphFunctionCatalog(), effects);
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench);
            That(session.TryStage(LiveDebugPatchOperation.EffectTemplateRef(
                launch,
                "projectile.impactEffect",
                ice,
                new LiveEditProvenance(LiveEditSource.ManualWorkbench, "workbench://impact"))).Succeeded,
                Is.True);

            That(pipeline.Classify(session).CanCommitNextCast, Is.True);
            pipeline.BeginSafeFrame();
            LiveApplyCommitResult committed = pipeline.CommitNextCastSafeFrame();
            pipeline.EndSafeFrame();
            That(committed.Succeeded, Is.True);
            That(effects.TryGet(launchId, out EffectTemplateData after), Is.True);
            That(after.Projectile.ImpactEffectTemplateId, Is.EqualTo(iceId));
            That(after.Projectile.HitEffectTemplateId, Is.EqualTo(hitId));
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
