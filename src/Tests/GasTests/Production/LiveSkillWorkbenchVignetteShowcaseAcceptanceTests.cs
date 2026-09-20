using CapabilityStandardLiveSkillWorkbenchShowcaseMod.Runtime;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.LiveSkillWorkbench;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    [TestFixture]
    public sealed class LiveSkillWorkbenchVignetteShowcaseAcceptanceTests
    {
        [SetUp]
        public void SetUp()
        {
            GraphIdRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
            if (!AttributeRegistry.IsFrozen)
            {
                AttributeRegistry.Clear();
            }
        }

        [Test]
        [Category("ci-gate")]
        public void ProductionHotApply_GraphBodyReplace_ChangesExecuteSliceReturn()
        {
            int graphId = GraphIdRegistry.Register(LiveSkillWorkbenchVignetteRuntime.HotDamageGraphKey);
            var graphs = new GraphProgramRegistry();
            graphs.Register(graphId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = LiveSkillWorkbenchVignetteRuntime.HotDamageBefore },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            }, GraphKind.Script);

            var pipeline = new LiveGasEditPipeline(graphs, new GraphFunctionCatalog());
            var runtime = new LiveSkillWorkbenchVignetteRuntime();
            runtime.Bind(graphs, pipeline, new LiveEffectChainTracer(32));
            runtime.EnsureWorld();

            // Drive until hot-apply + second cast complete.
            for (int i = 0; i < 60 * 12; i++)
            {
                runtime.Tick(1f / 60f);
                if (runtime.CurrentBeat == LiveSkillWorkbenchVignetteRuntime.Beat.HealMage ||
                    runtime.CurrentBeat == LiveSkillWorkbenchVignetteRuntime.Beat.EffectChain ||
                    runtime.CurrentBeat == LiveSkillWorkbenchVignetteRuntime.Beat.FrostDraft ||
                    runtime.CurrentBeat == LiveSkillWorkbenchVignetteRuntime.Beat.LoopHold)
                {
                    break;
                }
            }

            Assert.That(runtime.HotApplied, Is.True);
            Assert.That(runtime.LastClassify, Is.EqualTo(nameof(LiveApplyMode.NextCastLiveApply)));
            Assert.That(runtime.LastReturnInt, Is.EqualTo(LiveSkillWorkbenchVignetteRuntime.HotDamageAfter));
            Assert.That(runtime.DummyHp01, Is.LessThan(0.5f));
        }
    }
}
