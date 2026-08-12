using System.Collections.Generic;
using CapabilityStandardGraphOpsScriptMod.Runtime;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
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
            // Patrol Call target graph ids are ActionLib-resolved; CoreGraphs assert Call/Yield IR.
            // Keep this vignette focused on Yield resume + drink halt under budget.
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

        [Test]
        public void ScriptControl_CoreGraphs_EmitControlFlowOps()
        {
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(
                out GraphFunctionCatalog catalog,
                out GraphActionCatalog actions);

            int drinkId = GraphRegistryScriptResolver.RequireActionId(actions, GraphOpsScriptRuntime.DrinkActionName);
            int patrolId = GraphRegistryScriptResolver.RequireActionId(actions, BehaviorTreeScriptKeys.Patrol);
            int invokeId = GraphRegistryScriptResolver.RequireId(GraphOpsScriptRuntime.InvokeConstGraphKey);
            _ = catalog.Require(GraphOpsScriptRuntime.ConstFunctionName);

            var drink = CollectOps(programs, drinkId);
            var patrol = CollectOps(programs, patrolId);
            var invoke = CollectOps(programs, invokeId);

            Assert.Multiple(() =>
            {
                Assert.That(drink, Does.Contain(GraphNodeOp.Call));
                Assert.That(drink, Does.Contain(GraphNodeOp.Yield));
                Assert.That(drink, Does.Contain(GraphNodeOp.Return));
                Assert.That(drink, Does.Contain(GraphNodeOp.MoveInt));
                Assert.That(drink, Does.Contain(GraphNodeOp.HaltReturnInt));
                Assert.That(
                    drink.Contains(GraphNodeOp.Jump) || drink.Contains(GraphNodeOp.JumpIfFalse),
                    Is.True,
                    "Drink graph must compile BranchBool into Jump/JumpIfFalse.");
                Assert.That(patrol, Does.Contain(GraphNodeOp.Call));
                Assert.That(patrol, Does.Contain(GraphNodeOp.Yield));
                Assert.That(patrol, Does.Contain(GraphNodeOp.Return));
                Assert.That(invoke, Does.Contain(GraphNodeOp.InvokeScript));
                Assert.That(invoke, Does.Contain(GraphNodeOp.HaltReturnInt));
            });
        }

        private static HashSet<GraphNodeOp> CollectOps(GraphProgramRegistry programs, int graphId)
        {
            ReadOnlySpan<GraphInstruction> program = GraphRegistryScriptResolver.RequireProgram(programs, graphId);
            var ops = new HashSet<GraphNodeOp>();
            for (int i = 0; i < program.Length; i++)
            {
                ops.Add((GraphNodeOp)program[i].Op);
            }

            return ops;
        }
    }
}
