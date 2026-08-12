using CapabilityStandardGraphOpsScriptMod.Runtime;
using Ludots.Core.GraphRuntime;
using Ludots.Tests.Gas.Graph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphOpsScriptShowcaseAcceptanceTests
    {
        [Test]
        public void ScriptControl_DrinkAndPatrol_YieldThenHalt()
        {
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(
                out GraphFunctionCatalog catalog,
                out GraphActionCatalog actions);
            var runtime = new GraphOpsScriptRuntime();
            runtime.Bind(programs, actions, catalog);
            runtime.EnsureWorld();

            for (int i = 0; i < 30 && runtime.CompletedPatrolSteps == 0; i++)
            {
                runtime.Tick(0.2f);
            }

            Assert.That(runtime.SawYield, Is.True);
            Assert.That(runtime.CompletedWater, Is.EqualTo(runtime.DrinkLimit));
            Assert.That(runtime.CompletedPatrolSteps, Is.EqualTo(runtime.PatrolLimit));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(5.0));
        }

        [Test]
        public void ScriptControl_ConstPipeline_ReturnsSeven()
        {
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(
                out GraphFunctionCatalog catalog,
                out GraphActionCatalog actions);
            var runtime = new GraphOpsScriptRuntime();
            runtime.Bind(programs, actions, catalog);
            runtime.EnsureWorld();

            for (int i = 0; i < 40 && !runtime.AllPhasesComplete; i++)
            {
                runtime.Tick(0.2f);
            }

            Assert.That(runtime.AllPhasesComplete, Is.True);
            Assert.That(runtime.ConstValue, Is.EqualTo(7));
            Assert.That(runtime.Metrics.Detail, Does.Contain("常量管线"));
            Assert.That(runtime.Metrics.Detail, Does.Contain("7"));
        }
    }
}
