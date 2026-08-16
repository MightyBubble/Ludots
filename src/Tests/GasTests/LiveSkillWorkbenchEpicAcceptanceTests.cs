using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.LiveSkillWorkbench;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Epic #615 remaining slices: #620 Immediate attrs, #621 tracer, #623 AI draft, #624 save.
    /// </summary>
    [TestFixture]
    public sealed class LiveSkillWorkbenchEpicAcceptanceTests
    {
        [SetUp]
        public void SetUp()
        {
            GraphIdRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
            TagRegistry.Clear();
            AbilityIdRegistry.Clear();
            if (!AttributeRegistry.IsFrozen)
            {
                AttributeRegistry.Clear();
            }
        }

        [Test]
        [Category("ci-gate")]
        public void Lsw620_ImmediateAttribute_SetCurrent_ThroughMutationOps()
        {
            using var world = World.Create();
            int healthId = AttributeRegistry.IsFrozen
                ? AttributeRegistry.GetId("Health")
                : AttributeRegistry.Register("Health");
            if (healthId == AttributeRegistry.InvalidId)
            {
                Assert.Ignore("Health attribute not available in frozen registry.");
            }

            AttributeRegistry.SetConstraints(
                healthId,
                AttributeRegistry.AttributeConstraints.Create(false, true, 0f, true, 200f));

            var entity = world.Create(
                new AttributeBuffer(),
                new DirtyFlags());
            ref AttributeBuffer buf = ref world.Get<AttributeBuffer>(entity);
            buf.SetBase(healthId, 100f);
            buf.SetCurrent(healthId, 25f);

            var tagOps = new TagOps(new DirtyEntityQueue(8), new TagRuleRegistry());
            var executor = new LiveAttributeCommandExecutor(world, tagOps);
            executor.SetSelectedEntity(entity);

            var provenance = new LiveEditProvenance(LiveEditSource.ManualWorkbench, "workbench://actor/health");
            executor.Apply(LiveDebugPatchOperation.SelectedActorAttribute(
                ActorTargetSelection.FromEntityIdSurrogate(entity.Id),
                AttributeRegistry.GetName(healthId),
                ActorAttributeMutationKind.Set,
                100d,
                provenance));

            That(world.Get<AttributeBuffer>(entity).GetCurrent(healthId), Is.EqualTo(100f));
        }

        [Test]
        [Category("ci-gate")]
        public void Lsw620_ImmediateAttribute_RejectsMissingSelection()
        {
            using var world = World.Create();
            var executor = new LiveAttributeCommandExecutor(world, new TagOps(new DirtyEntityQueue(8), new TagRuleRegistry()));
            Throws<InvalidOperationException>(() => executor.Apply(
                LiveDebugPatchOperation.SelectedActorAttribute(
                    ActorTargetSelection.FromDescriptor("nobody"),
                    "Health",
                    ActorAttributeMutationKind.Set,
                    1d,
                    new LiveEditProvenance(LiveEditSource.ManualWorkbench, "x"))));
        }

        [Test]
        [Category("ci-gate")]
        public void Lsw621_Tracer_IngestsCastAndReportsDroppedWhenFull()
        {
            using var world = World.Create();
            Entity actor = world.Create();
            var tracer = new LiveEffectChainTracer(capacity: 16);
            AbilityIdRegistry.Register("ability.TraceDemo");
            EffectTemplateIdRegistry.Register("effect.TraceDemo");

            for (int i = 0; i < 40; i++)
            {
                tracer.Ingest(new GasPresentationEvent
                {
                    Kind = GasPresentationEventKind.CastStarted,
                    Actor = actor,
                    AbilityId = AbilityIdRegistry.GetId("ability.TraceDemo"),
                    AbilitySlot = 0
                });
                tracer.Ingest(new GasPresentationEvent
                {
                    Kind = GasPresentationEventKind.EffectApplied,
                    Actor = actor,
                    Target = actor,
                    EffectTemplateId = EffectTemplateIdRegistry.GetId("effect.TraceDemo")
                });
            }

            That(tracer.DroppedCount, Is.GreaterThan(0));
            var recent = tracer.SnapshotRecent(32);
            That(recent.Count, Is.GreaterThan(0));
            bool sawDropped = false;
            for (int i = 0; i < recent.Count; i++)
            {
                if (recent[i].Phase == LiveEffectChainPhase.Dropped)
                {
                    sawDropped = true;
                    break;
                }
            }

            That(sawDropped, Is.True, "Overflow must emit explicit Dropped events.");
        }

        [Test]
        [Category("ci-gate")]
        public void Lsw623_AiDraft_StagesThroughPipeline_AndBindsPlaytest()
        {
            int graphId = GraphIdRegistry.Register(DeterministicFakeAiSkillDraftGenerator.FrostNovaGraphKey);
            var graphs = new GraphProgramRegistry();
            graphs.Register(graphId, new[]
            {
                new Ludots.Core.GraphRuntime.GraphInstruction
                {
                    Op = (ushort)Ludots.Core.NodeLibraries.GASGraph.GraphNodeOp.ConstInt,
                    Dst = 0,
                    Imm = 1
                },
                new Ludots.Core.GraphRuntime.GraphInstruction
                {
                    Op = (ushort)Ludots.Core.NodeLibraries.GASGraph.GraphNodeOp.HaltReturnInt,
                    A = 0
                }
            }, GraphKind.Script);

            int effectId = EffectTemplateIdRegistry.Register(DeterministicFakeAiSkillDraftGenerator.FrostNovaEffectKey);
            var effects = new EffectTemplateRegistry();
            effects.Register(effectId, new EffectTemplateData { DurationTicks = 10, PeriodTicks = 0 });

            var pipeline = new LiveGasEditPipeline(graphs, new GraphFunctionCatalog(), effects);
            var generator = new DeterministicFakeAiSkillDraftGenerator();
            var provenance = new LiveEditProvenance(LiveEditSource.AiGeneratedDraft, "ai://frost");
            LiveAiSkillDraft draft = generator.Generate("做一个小范围冰冻技能", provenance);

            LiveEditSession session = LiveEditSession.Start(LiveEditSource.AiGeneratedDraft);
            for (int i = 0; i < draft.Operations.Count; i++)
            {
                LiveEditStageResult stage = session.TryStage(draft.Operations[i]);
                That(stage.Succeeded, Is.True, $"op {i} should stage");
            }

            LiveApplyClassificationReport report = pipeline.Classify(session);
            That(report.CanCommitNextCast, Is.True);
            pipeline.BeginSafeFrame();
            That(pipeline.CommitNextCastSafeFrame().Succeeded, Is.True);
            pipeline.EndSafeFrame();

            var binder = new LiveAiDraftBinder();
            LiveAiDraftPlaytestBind bind = binder.Bind(draft, actorEntityId: 42);
            That(bind.AbilityKey, Is.EqualTo(DeterministicFakeAiSkillDraftGenerator.FrostNovaAbilityKey));
            That(effects.TryGet(effectId, out EffectTemplateData data), Is.True);
            That(data.DurationTicks, Is.EqualTo(30));
        }

        [Test]
        [Category("ci-gate")]
        public void Lsw623_AiDraft_RejectPrompt_DoesNotCommitLive()
        {
            var pipeline = new LiveGasEditPipeline(new GraphProgramRegistry(), new GraphFunctionCatalog(), new EffectTemplateRegistry());
            var generator = new DeterministicFakeAiSkillDraftGenerator();
            LiveAiSkillDraft draft = generator.Generate("REJECT this", new LiveEditProvenance(LiveEditSource.AiGeneratedDraft, "ai://bad"));
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.AiGeneratedDraft);
            That(session.TryStage(draft.Operations[0]).Succeeded, Is.True);
            LiveApplyClassificationReport report = pipeline.Classify(session);
            That(report.CanCommitNextCast, Is.False);
            That(report.RequiresMapReload || report.RequiresEngineRestart, Is.True);
        }

        [Test]
        [Category("ci-gate")]
        public void Lsw624_SavePreviewAndWrite_ExcludesImmediateAttrs()
        {
            string modRoot = Path.Combine(Path.GetTempPath(), "lsw-save-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(modRoot, "assets/Configs/GAS"));
            try
            {
                int graphId = GraphIdRegistry.Register("Graph.Save.Demo");
                var graphs = new GraphProgramRegistry();
                graphs.Register(graphId, new[]
                {
                    new Ludots.Core.GraphRuntime.GraphInstruction
                    {
                        Op = (ushort)Ludots.Core.NodeLibraries.GASGraph.GraphNodeOp.ConstInt,
                        Dst = 0,
                        Imm = 2
                    },
                    new Ludots.Core.GraphRuntime.GraphInstruction
                    {
                        Op = (ushort)Ludots.Core.NodeLibraries.GASGraph.GraphNodeOp.HaltReturnInt,
                        A = 0
                    }
                }, GraphKind.Script);

                LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench);
                var provenance = new LiveEditProvenance(LiveEditSource.ManualWorkbench, "workbench://save");
                string doc = """
                    {
                      "id": "Graph.Save.Demo",
                      "kind": "Script",
                      "entry": "c",
                      "nodes": [
                        { "id": "c", "op": "ConstInt", "intValue": 9 },
                        { "id": "h", "op": "HaltReturnInt" }
                      ],
                      "controlEdges": [ { "from": "c", "fromPort": "next", "to": "h" } ],
                      "valueEdges": [ { "from": "c", "fromPort": "value", "to": "h", "toPort": "value" } ]
                    }
                    """;
                That(session.TryStage(LiveDebugPatchOperation.GraphBodyReplace("Graph.Save.Demo", doc, provenance)).Succeeded, Is.True);
                That(session.TryStage(LiveDebugPatchOperation.SelectedActorAttribute(
                    ActorTargetSelection.FromEntityIdSurrogate(1),
                    "Health",
                    ActorAttributeMutationKind.Set,
                    50d,
                    provenance)).Succeeded, Is.True);

                var save = new LiveEditModSaveService();
                LiveEditSavePreview preview = save.Preview(session, "TestMod", modRoot);
                That(preview.CanSave, Is.True);
                That(preview.ExcludedImmediateOps.Count, Is.EqualTo(1));
                LiveEditSaveResult result = save.Save(session, preview);
                That(result.Succeeded, Is.True);
                That(File.Exists(Path.Combine(modRoot, "assets/Configs/GAS/graphs.json")), Is.True);
                string text = File.ReadAllText(Path.Combine(modRoot, "assets/Configs/GAS/graphs.json"));
                That(text, Does.Contain("Graph.Save.Demo"));
                That(text, Does.Contain("9"));
            }
            finally
            {
                if (Directory.Exists(modRoot))
                {
                    Directory.Delete(modRoot, recursive: true);
                }
            }
        }
    }
}
