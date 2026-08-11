using System;
using System.Collections.Generic;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.Gameplay.AI.BehaviorTree
{
    public static class BehaviorTreePatrolScripts
    {
        /// <summary>
        /// Sensor-driven condition: HaltReturnInt(I[0]). Feed writes I[0]=0/1 before run.
        /// Action scripts: ConstInt(intent) → HaltReturnInt.
        /// </summary>
        public static Dictionary<int, GraphInstruction[]> CreatePatrolChaseAttackPrograms()
        {
            return new Dictionary<int, GraphInstruction[]>
            {
                [BehaviorTreeScriptBindings.SeeEnemy] = CompileSensorHalt("bt.see"),
                [BehaviorTreeScriptBindings.InAttackRange] = CompileSensorHalt("bt.inrange"),
                [BehaviorTreeScriptBindings.Chase] = CompileConstHalt("bt.chase", 1),
                [BehaviorTreeScriptBindings.Attack] = CompileConstHalt("bt.attack", 2),
                [BehaviorTreeScriptBindings.Patrol] = CompileConstHalt("bt.patrol", 0),
            };
        }

        private static GraphInstruction[] CompileSensorHalt(string id)
        {
            // HaltReturnInt reads I[0] written by IBehaviorTreeSensorFeed.
            var doc = new GraphControlFlowDocument
            {
                Id = id,
                Entry = "h",
                Nodes = { new GraphControlFlowNode { Id = "h", Op = nameof(GraphNodeOp.HaltReturnInt) } },
                ControlEdges = { },
                ValueEdges =
                {
                    // HaltReturnInt value pin unused when PinRegister default; body uses A=0 register.
                }
            };
            // Minimal program: single HaltReturnInt A=0
            return new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 }
            };
        }

        private static GraphInstruction[] CompileConstHalt(string id, int value)
        {
            var doc = new GraphControlFlowDocument
            {
                Id = id,
                Entry = "c",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "c", Op = nameof(GraphNodeOp.ConstInt), IntValue = value },
                    new GraphControlFlowNode { Id = "h", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = { new GraphControlFlowEdge("c", GraphControlFlowPorts.Next, "h") },
                ValueEdges =
                {
                    new GraphControlFlowValueEdge("c", GraphControlFlowPorts.Value, "h", GraphControlFlowPorts.Value)
                }
            };
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc);
            if (!compiled.Succeeded)
            {
                throw new InvalidOperationException($"Failed to compile BT Script '{id}'.");
            }

            return compiled.Program;
        }
    }
}
