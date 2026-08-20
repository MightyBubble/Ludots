using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Scripting;
using RngCapabilityMod.Rng;
using RngCapabilityMod.Triggers;

namespace RngCapabilityMod;

public sealed class RngCapabilityModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var opContext = new RngGraphOpContext();
        context.Extensions.Gas.RegisterGraphOp(
            "RngCapabilityMod.WeightedPick",
            GraphValueType.Int,
            WeightedPickGraphOp.CreateHandler(opContext),
            GraphValueType.Int);
        context.OnEvent(GameEvents.GameStart, new InstallRngCapabilityOnGameStartTrigger(context, opContext).ExecuteAsync);
        context.Log("[RngCapabilityMod] Loaded.");
    }

    public void OnUnload()
    {
    }
}
