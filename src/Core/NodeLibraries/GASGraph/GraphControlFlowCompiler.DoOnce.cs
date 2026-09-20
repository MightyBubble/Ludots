using System.Collections.Generic;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// DoOnce 作者面糖的编译展开（#1467）：UE DoOnce 的图体同构物。
    /// 展开形状（7 条，状态存 map variable，Reset ≡ WriteMapVarInt(var, 0)）：
    /// body+0 ReadMapVarInt（Dst=节点输出寄存器，A=0xFF 走 graph.MapScope，Imm=var 符号）
    /// body+1 ConstInt 0（比较临时）
    /// body+2 CompareEqInt（state == 0 → 未触发）
    /// body+3 JumpIfFalse → false 口目标（已触发分支）
    /// body+4 ConstInt 1
    /// body+5 WriteMapVarInt（闩住触发事实）
    /// body+6 Jump → true 口目标（首过分支）
    /// 与手写 read→compare→branch→write 链指令级同构（教科书 mod 的闸门即其手写形态）。
    /// </summary>
    public static partial class GraphControlFlowCompiler
    {
        private static void CompileDoOnce(
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
            List<GraphDiagnostic> diagnostics)
        {
            int nodeIndex = nodeIndices[node.Id];
            int bodyIndex = layouts[nodeIndex].BodyIndex;
            byte stateReg = scratch.StateReg
                ?? throw new System.InvalidOperationException($"DoOnce node '{node.Id}' compiled without its state scratch register.");
            int varSym = RequireSymbol(node.Var, "var", node, symbolToIndex, symbols, graphId, diagnostics);
            int falseAbs = ResolveControlTarget(node, GraphControlFlowPorts.False, controlEdges, nodeIndices, layouts);

            program[bodyIndex] = new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.ReadMapVarInt,
                Dst = stateReg,
                A = byte.MaxValue,
                Imm = varSym
            };
            definedInts[stateReg] = true;
            SetSource(sources, bodyIndex, graphId, node, GraphAuthoringSugar.DoOnce, GraphControlFlowPorts.Enter);

            program[bodyIndex + 1] = new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.ConstInt,
                Dst = scratch.IntReg,
                Imm = 0
            };
            SetSource(sources, bodyIndex + 1, graphId, node, GraphAuthoringSugar.DoOnce, GraphControlFlowPorts.Enter);

            program[bodyIndex + 2] = new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.CompareEqInt,
                Dst = scratch.BoolReg,
                A = stateReg,
                B = scratch.IntReg
            };
            SetSource(sources, bodyIndex + 2, graphId, node, GraphAuthoringSugar.DoOnce, GraphControlFlowPorts.Enter);

            program[bodyIndex + 3] = new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.JumpIfFalse,
                A = scratch.BoolReg,
                Imm = RelativeOffset(bodyIndex + 3, falseAbs)
            };
            SetSource(sources, bodyIndex + 3, graphId, node, GraphAuthoringSugar.DoOnce, GraphControlFlowPorts.False);

            program[bodyIndex + 4] = new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.ConstInt,
                Dst = scratch.IntReg,
                Imm = 1
            };
            SetSource(sources, bodyIndex + 4, graphId, node, GraphAuthoringSugar.DoOnce, GraphControlFlowPorts.True);

            program[bodyIndex + 5] = new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.WriteMapVarInt,
                A = scratch.IntReg,
                Imm = varSym
            };
            SetSource(sources, bodyIndex + 5, graphId, node, GraphAuthoringSugar.DoOnce, GraphControlFlowPorts.True);

            EmitRelativeJump(
                document, node, GraphControlFlowPorts.True, bodyIndex + 6,
                controlEdges, nodeIndices, layouts, program, sources, graphId);
        }
    }
}
