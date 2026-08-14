using CapabilityStandardAbilityGraphSandboxMod.Runtime;
using CapabilityStandardBehaviorTreeArenaMod.Runtime;
using CapabilityStandardGraphBehaviorIntegrationMod.Runtime;
using CapabilityStandardHfsmSentryArenaMod.Runtime;
using CapabilityStandardLevelBlueprintTrialMod.Runtime;
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
        [Test]
        public void RegistryName_DelegatesToSeparatedSuite()
        {
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out GraphActionCatalog actions, out GraphBehaviorCatalog behavior);
            var runtime = new BehaviorTreeArenaRuntime();
            runtime.Bind(programs, actions, behavior);
            runtime.EnsureWorld();
            for (int i = 0; i < 8; i++) runtime.Tick(0.2f);
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(5.0));
        }
    }

    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class HfsmSentryArenaShowcaseAcceptanceTests
    {
        [Test]
        public void RegistryName_DelegatesToSeparatedSuite()
        {
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out GraphActionCatalog actions, out GraphBehaviorCatalog behavior);
            var runtime = new HfsmSentryArenaRuntime();
            runtime.Bind(programs, actions, behavior);
            runtime.EnsureWorld();
            for (int i = 0; i < 8; i++) runtime.Tick(0.2f);
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(5.0));
        }
    }

    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class LevelBlueprintTrialShowcaseAcceptanceTests
    {
        [Test]
        public void RegistryName_DelegatesToSeparatedSuite()
        {
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out GraphActionCatalog actions);
            var runtime = new LevelBlueprintTrialRuntime();
            runtime.Bind(programs, actions);
            runtime.EnsureWorld();
            for (int i = 0; i < 40; i++) runtime.Tick(0.2f);
            Assert.That(runtime.GateOpen, Is.True);
        }
    }

    [TestFixture]
    [Category("ci-gate")]
    public sealed class AbilityGraphSandboxShowcaseAcceptanceTests
    {
        [Test]
        public void RegistryName_DelegatesToSeparatedSuite()
        {
            using var runtime = new AbilityGraphSandboxRuntime();
            runtime.BindStandaloneFromModAssets();
            runtime.EnsureWorld();
            for (int i = 0; i < 8; i++) runtime.Tick(0.2f);
            Assert.Multiple(() =>
            {
                Assert.That(runtime.Metrics.Detail, Does.Contain("查一圈"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("挂状态"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("加好感"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("状态牌"));
                Assert.That(runtime.EffectApplications, Is.GreaterThan(0));
                Assert.That(runtime.RelationshipScore, Is.GreaterThan(0));
                Assert.That(runtime.StatusToken, Is.Not.EqualTo("无"));
                Assert.That(runtime.NearbyCount, Is.EqualTo(AbilityGraphSandboxGraphKeys.QueryLimit));
            });
        }

        [Test]
        public void SandboxGraphs_EmitAllCoveredOps()
        {
            using var runtime = new AbilityGraphSandboxRuntime();
            runtime.BindStandaloneFromModAssets();
            HashSet<GraphNodeOp> emitted = CollectOps(runtime);
            GraphNodeOp[] required =
            [
                GraphNodeOp.HasTag,
                GraphNodeOp.SelectTagInMask,
                GraphNodeOp.LookupTagDisplayToken,
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
        [Test]
        public void RegistryName_DelegatesToSeparatedSuite()
        {
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out GraphActionCatalog actions, out GraphBehaviorCatalog behavior);
            var runtime = new GraphBehaviorIntegrationRuntime();
            runtime.Bind(programs, actions, behavior);
            runtime.EnsureWorld();
            for (int i = 0; i < 15; i++) runtime.Tick(0.2f);
            Assert.That(runtime.GuardCount, Is.EqualTo(6));
        }
    }
}
