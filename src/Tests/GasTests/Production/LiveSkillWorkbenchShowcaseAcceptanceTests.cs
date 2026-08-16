using System;
using System.IO;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.LiveSkillWorkbench;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    /// <summary>
    /// #625 player-path acceptance (headless): edit → apply → trace → AI bind → save.
    /// </summary>
    [TestFixture]
    public sealed class LiveSkillWorkbenchShowcaseAcceptanceTests
    {
        [Test]
        [Category("ci-gate")]
        public void PlayerLoop_EditPrecheckApplyTraceAiBindSave_Succeeds()
        {
            GraphIdRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
            AbilityIdRegistry.Clear();

            int frostGraphId = GraphIdRegistry.Register(DeterministicFakeAiSkillDraftGenerator.FrostNovaGraphKey);
            var graphs = new GraphProgramRegistry();
            graphs.Register(frostGraphId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            }, GraphKind.Script);

            string effectKey = DeterministicFakeAiSkillDraftGenerator.FrostNovaEffectKey;
            int effectId = EffectTemplateIdRegistry.Register(effectKey);
            var effects = new EffectTemplateRegistry();
            effects.Register(effectId, new EffectTemplateData { DurationTicks = 10, PeriodTicks = 0 });

            var pipeline = new LiveGasEditPipeline(graphs, new GraphFunctionCatalog(), effects);
            var tracer = new LiveEffectChainTracer(64);
            var ai = new DeterministicFakeAiSkillDraftGenerator();
            var binder = new LiveAiDraftBinder();
            var save = new LiveEditModSaveService();

            string modRoot = Path.Combine(Path.GetTempPath(), "lsw-showcase-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(modRoot, "assets/Configs/GAS"));

            try
            {
                // Edit effect duration → NextCast
                LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench);
                var provenance = new LiveEditProvenance(LiveEditSource.ManualWorkbench, "showcase://effect");
                Assert.That(session.TryStage(LiveDebugPatchOperation.SkillEffectNumeric(
                    effectKey, "duration.durationTicks", 40d, provenance)).Succeeded, Is.True);
                Assert.That(pipeline.Classify(session).CanCommitNextCast, Is.True);
                pipeline.BeginSafeFrame();
                Assert.That(pipeline.CommitNextCastSafeFrame().Succeeded, Is.True);
                pipeline.EndSafeFrame();
                Assert.That(effects.TryGet(effectId, out EffectTemplateData after), Is.True);
                Assert.That(after.DurationTicks, Is.EqualTo(40));

                // Trace
                AbilityIdRegistry.Register("ability.Fireball");
                tracer.Ingest(new GasPresentationEvent
                {
                    Kind = GasPresentationEventKind.CastStarted,
                    AbilityId = AbilityIdRegistry.GetId("ability.Fireball"),
                });
                tracer.Ingest(new GasPresentationEvent
                {
                    Kind = GasPresentationEventKind.EffectApplied,
                    EffectTemplateId = effectId,
                });
                Assert.That(tracer.SnapshotRecent(8).Count, Is.GreaterThanOrEqualTo(2));

                // AI draft → classify/commit → bind
                LiveAiSkillDraft draft = ai.Generate(
                    "做一个小范围冰冻技能",
                    new LiveEditProvenance(LiveEditSource.AiGeneratedDraft, "ai://frost"));
                LiveEditSession aiSession = LiveEditSession.Start(LiveEditSource.AiGeneratedDraft);
                for (int i = 0; i < draft.Operations.Count; i++)
                {
                    Assert.That(aiSession.TryStage(draft.Operations[i]).Succeeded, Is.True);
                }

                Assert.That(pipeline.Classify(aiSession).CanCommitNextCast, Is.True);
                pipeline.BeginSafeFrame();
                Assert.That(pipeline.CommitNextCastSafeFrame().Succeeded, Is.True);
                pipeline.EndSafeFrame();
                LiveAiDraftPlaytestBind bind = binder.Bind(draft, actorEntityId: 99);
                Assert.That(bind.AbilityKey, Is.EqualTo(DeterministicFakeAiSkillDraftGenerator.FrostNovaAbilityKey));

                // Save
                LiveEditSavePreview preview = save.Preview(aiSession, "ShowcaseMod", modRoot);
                Assert.That(preview.CanSave, Is.True);
                Assert.That(save.Save(aiSession, preview).Succeeded, Is.True);
                Assert.That(File.Exists(Path.Combine(modRoot, "assets/Configs/GAS/graphs.json")), Is.True);
            }
            finally
            {
                if (Directory.Exists(modRoot))
                {
                    Directory.Delete(modRoot, true);
                }
            }
        }
    }
}
