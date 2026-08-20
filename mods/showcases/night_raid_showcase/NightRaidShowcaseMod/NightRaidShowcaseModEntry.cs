using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace NightRaidShowcaseMod;

public sealed class NightRaidShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.OnEvent(GameEvents.GameStart, script =>
        {
            GameEngine? engine = script.GetEngine();
            if (engine == null || engine.GlobalContext.ContainsKey("NightRaidShowcaseMod.Installed"))
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext["NightRaidShowcaseMod.Installed"] = true;
            engine.RegisterSystem(new NightRaidShowcaseInteractionSystem(engine), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new NightRaidShowcasePresentationSystem(engine));
            context.Log("[NightRaidShowcaseMod] Readable Night Raid HUD and selection feedback registered.");
            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}
