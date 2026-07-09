using Arch.Core;
using CapabilityStandardGraphOpProviderMod.Runtime;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;

namespace CapabilityStandardGraphOpProviderMod;

public sealed class CapabilityStandardGraphOpProviderModEntry : IMod
{
    public const string QueryThreatKey = "CapabilityStandardGraphOpProviderMod.QueryThreat";

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardGraphOpProviderMod] Loaded");
        context.Extensions.Gas.RegisterGraphOp(
            QueryThreatKey,
            GraphValueType.Float,
            QueryThreat,
            GraphValueType.Entity);
    }

    public void OnUnload()
    {
    }

    private static void QueryThreat(ref GraphExecutionState state, in GraphInstruction instruction, ref int pc)
    {
        Entity target = state.E[instruction.A];
        if (!state.World.IsAlive(target))
        {
            throw new InvalidOperationException("CapabilityStandardGraphOpProviderMod.QueryThreat requires a live target entity.");
        }

        if (!state.World.Has<CapabilityStandardGraphOpThreatScore>(target))
        {
            throw new InvalidOperationException("CapabilityStandardGraphOpProviderMod.QueryThreat requires CapabilityStandardGraphOpThreatScore on the target entity.");
        }

        state.F[instruction.Dst] = state.World.Get<CapabilityStandardGraphOpThreatScore>(target).Value;
    }
}
