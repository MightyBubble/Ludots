using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace NightRaidShowcaseMod;

public sealed class NightRaidShowcaseModEntry : IMod
{
    private const string MapId = "night_raid";

    public void OnLoad(IModContext context)
    {
        context.Log("[NightRaidShowcaseMod] Loaded - interaction layer (click/query/HUD); level flow is map data");
        context.OnEvent(GameEvents.MapLoaded, ctx => OnMapLoaded(ctx));
    }

    public void OnUnload() { }

    private static Task OnMapLoaded(ScriptContext context)
    {
        if (context.TryGet(CoreServiceKeys.MapId, out Ludots.Core.Map.MapId mapId) &&
            string.Equals(mapId.Value, MapId, System.StringComparison.OrdinalIgnoreCase))
        {
            if (context.Get(CoreServiceKeys.Engine) is GameEngine engine)
            {
                engine.RegisterSystem(new NightRaidShowcaseInteractionSystem(engine), SystemGroup.InputCollection);
            }
        }

        return Task.CompletedTask;
    }
}
