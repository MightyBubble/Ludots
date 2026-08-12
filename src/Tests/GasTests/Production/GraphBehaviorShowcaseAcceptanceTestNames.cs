using CapabilityStandardAbilityGraphSandboxMod.Runtime;
using CapabilityStandardBehaviorTreeArenaMod.Runtime;
using CapabilityStandardGraphBehaviorIntegrationMod.Runtime;
using CapabilityStandardHfsmSentryArenaMod.Runtime;
using CapabilityStandardLevelBlueprintTrialMod.Runtime;
using Ludots.Core.GraphRuntime;
using Ludots.Tests.Gas.Graph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    // Named fixtures referenced by showcase.registry.json acceptanceTest fields.

    [TestFixture]
    [Category("ci-gate")]
    public sealed class BehaviorTreeArenaShowcaseAcceptanceTests
    {
        [Test]
        public void RegistryName_DelegatesToSeparatedSuite()
        {
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out GraphActionCatalog actions);
            var runtime = new BehaviorTreeArenaRuntime();
            runtime.Bind(programs, actions);
            runtime.EnsureWorld();
            for (int i = 0; i < 8; i++) runtime.Tick(0.2f);
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(5.0));
        }
    }

    [TestFixture]
    [Category("ci-gate")]
    public sealed class HfsmSentryArenaShowcaseAcceptanceTests
    {
        [Test]
        public void RegistryName_DelegatesToSeparatedSuite()
        {
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out GraphActionCatalog actions);
            var runtime = new HfsmSentryArenaRuntime();
            runtime.Bind(programs, actions);
            runtime.EnsureWorld();
            for (int i = 0; i < 8; i++) runtime.Tick(0.2f);
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(5.0));
        }
    }

    [TestFixture]
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
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out GraphFunctionCatalog catalog);
            var runtime = new AbilityGraphSandboxRuntime();
            runtime.Bind(programs, catalog);
            runtime.EnsureWorld();
            for (int i = 0; i < 8; i++) runtime.Tick(0.2f);
            Assert.Multiple(() =>
            {
                Assert.That(runtime.Metrics.Detail, Does.Contain("查一圈"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("挂状态"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("加好感"));
                Assert.That(runtime.Metrics.Detail, Does.Contain("状态牌"));
            });
        }
    }

    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphBehaviorIntegrationShowcaseAcceptanceTests
    {
        [Test]
        public void RegistryName_DelegatesToSeparatedSuite()
        {
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(out _, out GraphActionCatalog actions);
            var runtime = new GraphBehaviorIntegrationRuntime();
            runtime.Bind(programs, actions);
            runtime.EnsureWorld();
            for (int i = 0; i < 15; i++) runtime.Tick(0.2f);
            Assert.That(runtime.GuardCount, Is.EqualTo(6));
        }
    }
}
