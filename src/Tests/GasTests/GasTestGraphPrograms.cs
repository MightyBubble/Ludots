using Ludots.Core.Gameplay.GAS;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Tests.GAS;

internal static class GasTestGraphPrograms
{
    public static PhaseHandler BuiltinGraph(
        GraphProgramRegistry programs,
        int graphId,
        BuiltinHandlerId handlerId,
        GraphKind kind = GraphKind.Effect)
    {
        RegisterBuiltinGraph(programs, graphId, handlerId, kind);
        return PhaseHandler.Graph(graphId);
    }

    public static void RegisterBuiltinGraph(
        GraphProgramRegistry programs,
        int graphId,
        BuiltinHandlerId handlerId,
        GraphKind kind = GraphKind.Effect)
    {
        programs.Register(graphId,
        [
            new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.InvokeBuiltin,
                Imm = (int)handlerId,
            },
        ], kind);
    }
}
