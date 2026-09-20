using System.Text.Json.Nodes;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Config;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class GraphOpsNodeGalleryRngAcceptanceTests
    {
        [Test]
        public void WeightedPickVignette_PicksFromDeclaredDistributionAndSettles()
        {
            using var runtime = new GraphOpsNodeGalleryRuntime();
            runtime.BindOp("WeightedPick");
            runtime.EnsureWorld();
            float before = runtime.Context.ActorHealth[1];
            runtime.Tick(0.35f);
            float after = runtime.Context.ActorHealth[1];

            Assert.That(after, Is.LessThan(before), "weighted pick vignette must settle real damage on the target");
            Assert.That(runtime.Metrics.Detail, Does.Contain("命运袋"), runtime.Metrics.Detail);
        }

        [Test]
        public void WeightedPick_AuthoringWithEmptyDistribution_FailsClosedAtCompile()
        {
            var graph = new JsonObject
            {
                ["id"] = "test.graph.weighted_pick.negative",
                ["kind"] = "Effect",
                ["entry"] = "permille",
                ["nodes"] = new JsonArray
                {
                    new JsonObject { ["id"] = "permille", ["op"] = "ConstInt", ["intValue"] = 0 },
                    new JsonObject { ["id"] = "pick", ["op"] = "WeightedPick" },
                    new JsonObject { ["id"] = "explicit", ["op"] = "LoadExplicitTarget" },
                    new JsonObject { ["id"] = "hit", ["op"] = "ModifyAttributeAdd", ["attribute"] = "Health" },
                },
                ["controlEdges"] = new JsonArray
                {
                    new JsonObject { ["from"] = "permille", ["fromPort"] = "next", ["to"] = "pick" },
                    new JsonObject { ["from"] = "pick", ["fromPort"] = "next", ["to"] = "explicit" },
                    new JsonObject { ["from"] = "explicit", ["fromPort"] = "next", ["to"] = "hit" },
                },
                ["valueEdges"] = new JsonArray
                {
                    new JsonObject { ["from"] = "permille", ["fromPort"] = "value", ["to"] = "pick", ["toPort"] = "value" },
                    new JsonObject { ["from"] = "explicit", ["fromPort"] = "value", ["to"] = "hit", ["toPort"] = "target" },
                },
            };

            var options = StrictJsonOptions.CreateCamelCase(includeFields: true);
            var compiled = GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(graph, "test.graph.weighted_pick.negative", options);

            Assert.That(compiled.Succeeded, Is.False, "WeightedPick without a distribution symbol must fail closed at compile time");
            Assert.That(
                string.Join("; ", compiled.Diagnostics),
                Does.Contain("distribution"),
                "diagnostics must point at the missing distribution field");
        }
    }
}
