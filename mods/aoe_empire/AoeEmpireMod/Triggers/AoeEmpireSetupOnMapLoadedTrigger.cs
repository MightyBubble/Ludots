using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace AoeEmpireMod.Triggers;

public sealed class AoeEmpireSetupOnMapLoadedTrigger : Trigger
{
    private readonly IModContext _ctx;

    public AoeEmpireSetupOnMapLoadedTrigger(IModContext ctx)
    {
        _ctx = ctx;
        EventKey = GameEvents.MapLoaded;
    }

    public override Task ExecuteAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine?.CurrentMapSession?.MapConfig?.Id != "rts_empire_like")
        {
            return Task.CompletedTask;
        }

        _ctx.Log("[AoeEmpireMod] AoE empire skirmish map loaded.");
        return Task.CompletedTask;
    }
}
