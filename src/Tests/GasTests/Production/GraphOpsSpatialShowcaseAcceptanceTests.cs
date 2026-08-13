using System.Collections.Generic;
using CapabilityStandardGraphOpsSpatialMod.Runtime;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphOpsSpatialShowcaseAcceptanceTests
    {
        [Test]
        public void SpatialQueries_ConeRectLineHex_UnderBudgetWithPlayerReadableDetail()
        {
            GraphProgramRegistry programs = GraphOpsSpatialCatalogBootstrap.Load(out GraphFunctionCatalog catalog);
            using var runtime = new GraphOpsSpatialRuntime();
            runtime.Bind(programs, catalog);
            runtime.EnsureWorld();

            Warm(runtime.Tick);
            Drive(runtime.Tick, runtime.Metrics);

            Assert.Multiple(() =>
            {
                Assert.That(runtime.Metrics.Detail, Does.Contain("扇形"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("矩形"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("直线"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("六角圈人"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("最近目标"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("排除自己"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("只打敌对层"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("只打敌对关系"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("范围内"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("外环"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("邻格"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("名单上第一个命中"));
            });
            AssertForbiddenJargon(runtime.Metrics.Detail);
            Assert.That(runtime.TargetCount, Is.EqualTo(8));
            Assert.That(runtime.ConeHits + runtime.RectHits + runtime.LineHits, Is.GreaterThan(0));
            Assert.That(runtime.HexRangeHits + runtime.HexRingHits + runtime.HexNeighborHits, Is.GreaterThan(0));
            Assert.That(runtime.HasFirstHit, Is.True);
            Assert.That(runtime.HasNearest, Is.True);
            Assert.That(runtime.LastHitIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(runtime.LastHitIndex, Is.LessThan(runtime.TargetCount));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(25.0));
        }

        [Test]
        public void SpatialQueries_PlayerFacingPhrases_LockFiltersHexAndFirstHit()
        {
            GraphProgramRegistry programs = GraphOpsSpatialCatalogBootstrap.Load(out GraphFunctionCatalog catalog);
            AssertTargetListGetOnAllGraphs(programs);

            using var runtime = new GraphOpsSpatialRuntime();
            runtime.Bind(programs, catalog);
            runtime.EnsureWorld();

            var details = new List<string> { runtime.Metrics.Detail };
            Warm(runtime.Tick);
            for (int i = 0; i < 16; i++)
            {
                runtime.Tick(0.2f);
                details.Add(runtime.Metrics.Detail);
            }

            string joined = string.Join('\n', details);
            Assert.Multiple(() =>
            {
                Assert.That(joined, Does.Contain("排除自己"));
                Assert.That(joined, Does.Contain("只打敌对层"));
                Assert.That(joined, Does.Contain("只打敌对关系"));
                Assert.That(joined, Does.Contain("名单上第一个命中"));
                Assert.That(joined, Does.Contain("范围内"));
                Assert.That(joined, Does.Contain("外环"));
                Assert.That(joined, Does.Contain("邻格"));
                Assert.That(runtime.HasFirstHit, Is.True);
                Assert.That(runtime.ConeHits + runtime.RectHits + runtime.LineHits, Is.GreaterThan(0));
            });
            foreach (string detail in details)
            {
                AssertForbiddenJargon(detail);
            }
        }

        private static void AssertTargetListGetOnAllGraphs(GraphProgramRegistry programs)
        {
            string[] keys =
            {
                "Graph.SpatialShowcase.Cone",
                "Graph.SpatialShowcase.Rectangle",
                "Graph.SpatialShowcase.Line",
                "Graph.SpatialShowcase.HexRange",
                "Graph.SpatialShowcase.HexRing",
                "Graph.SpatialShowcase.HexNeighbors"
            };
            foreach (string key in keys)
            {
                int graphId = GraphIdRegistry.GetId(key);
                Assert.That(programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program), Is.True, key);
                var ops = new HashSet<GraphNodeOp>();
                for (int i = 0; i < program.Length; i++)
                {
                    ops.Add((GraphNodeOp)program[i].Op);
                }

                Assert.That(ops, Does.Contain(GraphNodeOp.TargetListGet), $"{key} must emit TargetListGet.");
                Assert.That(ops, Does.Contain(GraphNodeOp.QueryFilterNotEntity), key);
                Assert.That(ops, Does.Contain(GraphNodeOp.QueryFilterLayer), key);
                Assert.That(ops, Does.Contain(GraphNodeOp.QueryFilterRelationship), key);
            }
        }

        private static void AssertForbiddenJargon(string detail)
        {
            Assert.That(detail, Does.Not.Contain("FuncLib"));
            Assert.That(detail, Does.Not.Contain("图节点"));
            Assert.That(detail, Does.Not.Contain("ms"));
        }

        private static void Warm(System.Action<float> tick, int waves = 8)
        {
            for (int i = 0; i < waves; i++) tick(0.2f);
        }

        private static void Drive(System.Action<float> tick, GraphShowcaseMetrics metrics, int waves = 16)
        {
            for (int i = 0; i < 4; i++) tick(0.2f);
            metrics.MaxThinkMs = 0;
            metrics.LastThinkMs = 0;
            for (int i = 0; i < waves; i++) tick(0.2f);
            TestContext.WriteLine($"{metrics.ShowcaseId}: waves={metrics.ThinkWaves} max={metrics.MaxThinkMs:F3} detail={metrics.Detail}");
        }
    }
}
