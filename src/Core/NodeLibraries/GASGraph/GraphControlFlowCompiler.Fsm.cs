using System;
using System.Collections.Generic;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// FSM 作者面糖：FsmState 节点的形状校验与编译展开。
    /// 展开固定为「ReadMapVarInt(stateVar) → SwitchInt 式 case 臂检查链 → default 尾跳」，
    /// 与手写 ReadMapVarInt+SwitchInt 序列指令级全等（见 GraphFsmSugarTests 消融对照），
    /// 不引入新 opcode、新执行器或新副作用 profile。
    /// </summary>
    public static partial class GraphControlFlowCompiler
    {
        public const string FsmStateOp = GraphAuthoringSugar.FsmState;

        /// <summary>
        /// FsmState 必填 default 臂且至少 1 条 case 臂；enumType/stateVar 缺失一律 fail closed。
        /// （enumType 未注册/成员名未知已由 BuildEnumCaseTable 报断；这里只管形状。）
        /// </summary>
        private static void ValidateFsmStateEdges(
            GraphControlFlowNode node,
            Dictionary<ControlKey, string> controlEdges,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(node.EnumType))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                    $"FsmState node '{node.Id}' requires an enumType binding.", node.Id));
            }

            if (string.IsNullOrWhiteSpace(node.StateVar))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                    $"FsmState node '{node.Id}' requires a non-empty stateVar (map variable holding the enum-valued state).",
                    node.Id));
            }

            RequireControlEdge(node, GraphControlFlowPorts.Default, controlEdges, graphId, diagnostics);

            var seenCases = new HashSet<int>();
            int caseCount = 0;
            foreach (ControlKey key in controlEdges.Keys)
            {
                if (!string.Equals(key.NodeId, node.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!GraphControlFlowPorts.TryParseCasePort(key.Port, out int caseValue))
                {
                    continue;
                }

                if (!seenCases.Add(caseValue))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.DuplicateControlEdge,
                        $"Duplicate FsmState case value {caseValue} on node '{node.Id}'.", node.Id));
                }

                caseCount++;
            }

            if (caseCount == 0)
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingControlEdge,
                    $"FsmState node '{node.Id}' requires at least one case:{{n}} control edge.", node.Id));
            }
        }

        /// <summary>
        /// 展开形状（n=case 臂数，共 2+4n 条）：
        /// body+0 ReadMapVarInt（Dst=状态寄存器，A=0xFF 走 graph.MapScope 上下文，Imm=stateVar 符号）
        /// body+1 Jump 相对 0（与手写 read→switch 链的 next 关系指令级对齐）
        /// 每臂 4 条与 SwitchInt 逐字节一致（ConstInt/CompareEqInt/JumpIfFalse/Jump，selector=状态寄存器）
        /// 尾部 default Jump。
        /// </summary>
        private static void CompileFsmState(
            GraphControlFlowDocument document,
            GraphControlFlowNode node,
            SugarScratch scratch,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<string, int> nodeIndices,
            NodeLayout[] layouts,
            GraphInstruction[] program,
            GraphInstructionSource[] sources,
            byte[] outputRegisters,
            bool[] definedInts,
            Dictionary<string, int> symbolToIndex,
            List<string> symbols,
            string graphId,
            List<GraphDiagnostic> diagnostics,
            EnumCaseTable? enumCases)
        {
            int nodeIndex = nodeIndices[node.Id];
            int bodyIndex = layouts[nodeIndex].BodyIndex;
            byte stateReg = outputRegisters[nodeIndex];
            int sym = RequireSymbol(node.StateVar, "stateVar", node, symbolToIndex, symbols, graphId, diagnostics);

            program[bodyIndex] = new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.ReadMapVarInt,
                Dst = stateReg,
                A = byte.MaxValue,
                Imm = sym
            };
            definedInts[stateReg] = true;
            SetSource(sources, bodyIndex, graphId, node, FsmStateOp, "stateVar");

            program[bodyIndex + 1] = new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.Jump,
                Imm = RelativeOffset(bodyIndex + 1, bodyIndex + 2)
            };
            SetSource(sources, bodyIndex + 1, graphId, node, FsmStateOp, "stateVar");

            List<SwitchCaseArm> arms = CollectSwitchCaseArms(document, node, enumCases);
            for (int i = 0; i < arms.Count; i++)
            {
                SwitchCaseArm arm = arms[i];
                int armBase = bodyIndex + 2 + (i * 4);
                int nextCheck = i + 1 < arms.Count
                    ? bodyIndex + 2 + ((i + 1) * 4)
                    : bodyIndex + 2 + (arms.Count * 4);
                string armPort = enumCases != null
                    ? enumCases.AuthoredPortOrNumeric(node.Id, arm.CaseValue)
                    : GraphControlFlowPorts.Case(arm.CaseValue);

                program[armBase] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ConstInt,
                    Dst = scratch.IntReg,
                    Imm = arm.CaseValue
                };
                SetSource(sources, armBase, graphId, node, FsmStateOp, armPort);

                program[armBase + 1] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.CompareEqInt,
                    Dst = scratch.BoolReg,
                    A = stateReg,
                    B = scratch.IntReg
                };
                SetSource(sources, armBase + 1, graphId, node, FsmStateOp, armPort);

                program[armBase + 2] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.JumpIfFalse,
                    A = scratch.BoolReg,
                    Imm = RelativeOffset(armBase + 2, nextCheck)
                };
                SetSource(sources, armBase + 2, graphId, node, FsmStateOp, armPort);

                int armAbs = layouts[nodeIndices[arm.TargetNodeId]].BodyIndex;
                program[armBase + 3] = new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.Jump,
                    Imm = RelativeOffset(armBase + 3, armAbs)
                };
                SetSource(sources, armBase + 3, graphId, node, FsmStateOp, armPort);
            }

            int defaultJumpIndex = bodyIndex + 2 + (arms.Count * 4);
            int defaultAbs = ResolveControlTarget(node, GraphControlFlowPorts.Default, controlEdges, nodeIndices, layouts);
            program[defaultJumpIndex] = new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.Jump,
                Imm = RelativeOffset(defaultJumpIndex, defaultAbs)
            };
            SetSource(sources, defaultJumpIndex, graphId, node, FsmStateOp, GraphControlFlowPorts.Default);
        }
    }
}
