using CapabilityStandardAbilityGraphSandboxMod.Runtime;
using CapabilityStandardBehaviorTreeArenaMod.Runtime;
using CapabilityStandardGraphBehaviorIntegrationMod.Runtime;
using CapabilityStandardHfsmSentryArenaMod.Runtime;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Tests.Gas.Graph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    // Named fixtures referenced by showcase.registry.json acceptanceTest fields.

    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class BehaviorTreeArenaShowcaseAcceptanceTests
    {
        private const double ShowcaseThinkBudgetMs = 15.0;

    }

    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class HfsmSentryArenaShowcaseAcceptanceTests
    {
        private const double ShowcaseThinkBudgetMs = 15.0;

    }

    [TestFixture]
    [Category("ci-gate")]
    public sealed class AbilityGraphSandboxShowcaseAcceptanceTests
    {

        [Test]
        public void SandboxGraphs_EmitAllCoveredOps()
        {
            using var runtime = new AbilityGraphSandboxRuntime();
            runtime.BindStandaloneFromModAssets();
            HashSet<GraphNodeOp> emitted = CollectOps(runtime);
            GraphNodeOp[] required =
            [
                GraphNodeOp.HasTag,
                GraphNodeOp.QueryRadius,
                GraphNodeOp.QuerySortStable,
                GraphNodeOp.QueryLimit,
                GraphNodeOp.FanOutApplyEffect,
                GraphNodeOp.ApplyEffectDynamic,
                GraphNodeOp.FanOutApplyEffectDynamic,
                GraphNodeOp.RelationshipEnsureLink,
                GraphNodeOp.RelationshipSetMetric,
                GraphNodeOp.RelationshipAddMetric,
                GraphNodeOp.RelationshipHasFlag
            ];
            foreach (GraphNodeOp op in required)
            {
                Assert.That(emitted, Does.Contain(op), $"Sandbox graphs missing {op}");
            }
        }

        private static HashSet<GraphNodeOp> CollectOps(AbilityGraphSandboxRuntime runtime)
        {
            var ops = new HashSet<GraphNodeOp>();
            foreach (string graphKey in new[]
                     {
                         AbilityGraphSandboxGraphKeys.Scout,
                         AbilityGraphSandboxGraphKeys.Apply,
                         AbilityGraphSandboxGraphKeys.Bond
                     })
            {
                Assert.That(runtime.TryGetProgram(graphKey, out ReadOnlySpan<GraphInstruction> program), Is.True, graphKey);
                for (int i = 0; i < program.Length; i++)
                {
                    ops.Add((GraphNodeOp)program[i].Op);
                }
            }

            return ops;
        }
    }

    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class GraphBehaviorIntegrationShowcaseAcceptanceTests
    {
    }
}
