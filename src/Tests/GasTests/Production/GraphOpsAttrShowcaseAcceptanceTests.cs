using CapabilityStandardGraphOpsAttrMod.Runtime;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class GraphOpsAttrShowcaseAcceptanceTests
    {
        [SetUp]
        public void ClearGraphIdsForStandaloneBootstrap()
        {
            GraphIdRegistry.Clear();
        }

        [Test]
        public void AttrVignette_ReadHealthStrikeApplyRemove_CompletesUnderBudget()
        {
            using var runtime = new GraphOpsAttrRuntime();
            runtime.BindStandaloneFromModAssets();
            runtime.EnsureWorld();

            for (int i = 0; i < 3; i++) runtime.Tick(0.2f);
            runtime.Metrics.MaxThinkMs = 0;
            runtime.Metrics.LastThinkMs = 0;

            for (int i = 0; i < 10 && !runtime.AllPhasesComplete; i++)
            {
                runtime.Tick(0.2f);
            }

            TestContext.WriteLine(
                $"{runtime.Metrics.ShowcaseId}: waves={runtime.Metrics.ThinkWaves} max={runtime.Metrics.MaxThinkMs:F3} detail={runtime.Metrics.Detail}");

            Assert.Multiple(() =>
            {
                Assert.That(runtime.AllPhasesComplete, Is.True);
                Assert.That(runtime.Metrics.Detail, Does.Contain("卸效果"));
                Assert.That(runtime.TargetHealth, Is.EqualTo(GraphOpsAttrRuntime.OpeningHealth - GraphOpsAttrRuntime.FullHit - GraphOpsAttrRuntime.GlanceHit).Within(0.01f));
                Assert.That(runtime.CasterHealth, Is.EqualTo(100f).Within(0.01f));
                Assert.That(runtime.HitEnemy, Is.True);
                Assert.That(runtime.TargetIsSelf, Is.False);
                Assert.That(runtime.StrikeTally, Is.EqualTo(2f).Within(0.01f));
                Assert.That(runtime.PendingEffectRequests, Is.GreaterThan(0));
                Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(25.0));
                AssertBannedEnglish(runtime.Metrics.Detail);
            });
        }

        [Test]
        public void AttrVignette_PlayerFacingPhrases_ArePresentAcrossWaves()
        {
            using var runtime = new GraphOpsAttrRuntime();
            runtime.BindStandaloneFromModAssets();
            runtime.EnsureWorld();

            var details = new List<string>();
            bool sawFull = false;
            bool sawGlance = false;
            float previousHealth = runtime.TargetHealth;
            for (int i = 0; i < 14 && !runtime.AllPhasesComplete; i++)
            {
                runtime.Tick(0.2f);
                details.Add(runtime.Metrics.Detail);
                AssertBannedEnglish(runtime.Metrics.Detail);
                if (runtime.Metrics.Detail.Contains("全力", StringComparison.Ordinal))
                {
                    sawFull = true;
                    Assert.That(runtime.TargetHealth, Is.LessThan(previousHealth));
                    Assert.That(runtime.LastHitPower, Is.EqualTo(GraphOpsAttrRuntime.FullHit).Within(0.01f));
                }

                if (runtime.Metrics.Detail.Contains("轻击", StringComparison.Ordinal))
                {
                    sawGlance = true;
                    Assert.That(runtime.TargetHealth, Is.LessThan(previousHealth));
                    Assert.That(runtime.LastHitPower, Is.EqualTo(GraphOpsAttrRuntime.GlanceHit).Within(0.01f));
                }

                previousHealth = runtime.TargetHealth;
            }

            string joined = string.Join('\n', details);
            Assert.Multiple(() =>
            {
                Assert.That(joined, Does.Contain("读血量"));
                Assert.That(joined, Does.Contain("目标不是自己"));
                Assert.That(joined, Does.Contain("选出对面挨打"));
                Assert.That(joined, Does.Contain("本轮出手次数记为"));
                Assert.That(joined, Does.Contain("加伤"));
                Assert.That(joined, Does.Contain("上效果"));
                Assert.That(joined, Does.Contain("卸效果"));
                Assert.That(sawFull, Is.True, "Player should hear 全力 when HP is still high.");
                Assert.That(sawGlance, Is.True, "Player should hear 轻击 after the target is wounded.");
                Assert.That(runtime.TargetHealth, Is.LessThan(GraphOpsAttrRuntime.OpeningHealth));
            });
        }

        [Test]
        public void FrontDoor_AttrGraphs_EmitCompareSelectAndBranch()
        {
            GraphProgramRegistry programs = GraphOpsAttrGraphBootstrap.LoadModGraphs(GraphOpsAttrGraphBootstrap.FindModAssetsRoot());
            var emitted = new HashSet<GraphNodeOp>();
            Collect(programs, GraphOpsAttrGraphKeys.ReadHealth, emitted);
            Collect(programs, GraphOpsAttrGraphKeys.Strike, emitted);
            Collect(programs, GraphOpsAttrGraphKeys.ApplyMark, emitted);
            Collect(programs, GraphOpsAttrGraphKeys.RemoveMark, emitted);

            GraphNodeOp[] required =
            [
                GraphNodeOp.ConstInt,
                GraphNodeOp.LoadCaster,
                GraphNodeOp.LoadExplicitTarget,
                GraphNodeOp.LoadAttribute,
                GraphNodeOp.AddInt,
                GraphNodeOp.CompareLtInt,
                GraphNodeOp.CompareEqInt,
                GraphNodeOp.CompareEqEntity,
                GraphNodeOp.SelectEntity,
                GraphNodeOp.JumpIfFalse,
                GraphNodeOp.ApplyEffectTemplate,
                GraphNodeOp.RemoveEffectTemplate,
                GraphNodeOp.ModifyAttributeAdd,
                GraphNodeOp.LoadSelfAttribute,
                GraphNodeOp.LoadContextTarget,
                GraphNodeOp.WriteSelfAttribute
            ];

            foreach (GraphNodeOp op in required)
            {
                Assert.That(emitted, Does.Contain(op), $"Attr graphs missing {op}");
            }
        }

        private static void Collect(GraphProgramRegistry programs, string graphKey, HashSet<GraphNodeOp> emitted)
        {
            int graphId = GraphIdRegistry.GetId(graphKey);
            Assert.That(programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program), Is.True, graphKey);
            foreach (GraphInstruction instruction in program)
            {
                emitted.Add((GraphNodeOp)instruction.Op);
            }
        }

        private static void AssertBannedEnglish(string detail)
        {
            Assert.That(detail, Does.Not.Contain("tally"));
            Assert.That(detail, Does.Not.Contain("Validation"));
            Assert.That(detail, Does.Not.Contain("FuncLib"));
            Assert.That(detail, Does.Not.Contain("True"));
            Assert.That(detail, Does.Not.Contain("False"));
        }
    }
}
