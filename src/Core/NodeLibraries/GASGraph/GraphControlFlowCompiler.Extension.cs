using System.Collections.Generic;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public static partial class GraphControlFlowCompiler
    {
        private static bool IsExtensionAuthorableKind(GraphKind graphKind)
            => graphKind is GraphKind.Script
                or GraphKind.Effect
                or GraphKind.Score
                or GraphKind.Validation
                or GraphKind.Derived
                or GraphKind.TriggerGraph;

        private static string ExtensionInputPort(int index)
            => index switch
            {
                0 => GraphControlFlowPorts.A,
                1 => GraphControlFlowPorts.B,
                2 => GraphControlFlowPorts.C,
                _ => throw new InvalidOperationException(
                    $"Extension graph ops support at most 3 inputs; port index {index} is not authorable.")
            };

        private static bool IsAllowedExtensionInputPort(AuthoredOp op, string port)
        {
            GasGraphOpDefinition? definition = op.Extension;
            if (definition == null)
            {
                return false;
            }

            GraphValueType[] inputs = definition.InputTypes;
            for (int i = 0; i < inputs.Length; i++)
            {
                if (port == ExtensionInputPort(i))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAllowedExtensionOutputPort(AuthoredOp op, string port)
        {
            GasGraphOpDefinition? definition = op.Extension;
            if (definition == null || definition.OutputType == GraphValueType.Void)
            {
                return false;
            }

            return port == GraphControlFlowPorts.Value;
        }

        private static void ValidateExtensionNode(
            GraphControlFlowNode node,
            AuthoredOp op,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            GraphValueType[] outputTypes,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            GasGraphOpDefinition definition = op.Extension
                ?? throw new InvalidOperationException($"Extension op on node '{node.Id}' is missing its registry definition.");

            GraphValueType[] inputs = definition.InputTypes;
            for (int i = 0; i < inputs.Length; i++)
            {
                RequireValueInput(
                    node,
                    ExtensionInputPort(i),
                    inputs[i],
                    valueEdges,
                    nodeIndices,
                    outputTypes,
                    graphId,
                    diagnostics);
            }
        }

        private static void EmitExtensionNode(
            GraphControlFlowDocument document,
            GraphControlFlowNode node,
            AuthoredOp op,
            byte[] outputRegisters,
            GraphValueType[] outputTypes,
            byte[] boolScratches,
            byte[] droppedRegisters,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            NodeLayout[] layouts,
            GraphInstruction[] program,
            GraphInstructionSource[] sources,
            bool[] definedInts,
            bool[] definedBools,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            GasGraphOpDefinition definition = op.Extension
                ?? throw new InvalidOperationException($"Extension op on node '{node.Id}' is missing its registry definition.");

            int nodeIndex = nodeIndices[node.Id];
            int bodyIndex = layouts[nodeIndex].BodyIndex;
            var instruction = new GraphInstruction
            {
                Op = checked((ushort)definition.OpCode),
                Dst = outputRegisters[nodeIndex],
                Imm = node.IntValue,
                ImmF = node.FloatValue,
                Flags = node.BoolValue ? (byte)1 : (byte)0
            };

            GraphValueType[] inputs = definition.InputTypes;
            for (int i = 0; i < inputs.Length; i++)
            {
                byte reg = ResolveValueInput(
                    node,
                    ExtensionInputPort(i),
                    inputs[i],
                    valueEdges,
                    nodeIndices,
                    outputTypes,
                    outputRegisters,
                    boolScratches,
                    droppedRegisters,
                    definedInts,
                    definedBools,
                    graphId,
                    diagnostics);
                switch (i)
                {
                    case 0:
                        instruction.A = reg;
                        break;
                    case 1:
                        instruction.B = reg;
                        break;
                    case 2:
                        instruction.C = reg;
                        break;
                }
            }

            program[bodyIndex] = instruction;
            SetSource(sources, bodyIndex, graphId, node, definition.Key, GraphControlFlowPorts.Enter);

            if (outputTypes[nodeIndex] == GraphValueType.Int)
            {
                definedInts[outputRegisters[nodeIndex]] = true;
            }

            if (outputTypes[nodeIndex] == GraphValueType.Bool)
            {
                definedBools[outputRegisters[nodeIndex]] = true;
            }

            if (controlEdges.ContainsKey(new ControlKey(node.Id, GraphControlFlowPorts.Next)))
            {
                EmitRelativeJump(
                    document,
                    node,
                    GraphControlFlowPorts.Next,
                    bodyIndex + 1,
                    controlEdges,
                    nodeIndices,
                    layouts,
                    program,
                    sources,
                    graphId);
            }
            else
            {
                EmitExplicitHalt(program, sources, bodyIndex + 1, graphId, node);
            }
        }
    }
}
