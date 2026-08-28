using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using SavePanelMod.Runtime;
using SavePanelMod.Systems;

namespace SavePanelMod;

public sealed class SavePanelModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.Get(CoreServiceKeys.Engine) is not GameEngine engine ||
                (engine.GlobalContext.TryGetValue(SavePanelIds.InstalledKey, out object? value) && value is true))
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[SavePanelIds.InstalledKey] = true;
            var runtime = new SavePanelRuntime(engine);
            engine.GlobalContext[SavePanelIds.RuntimeKey] = runtime;
            if (engine.GetService(CoreServiceKeys.InputHandler) is Ludots.Core.Input.Runtime.PlayerInputHandler inputHandler)
            {
                inputHandler.PushContext(SavePanelIds.InputContext);
            }

            engine.RegisterSystem(new SavePanelInputSystem(engine, runtime), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new SavePanelPresentationSystem(engine, runtime));
            context.Log("[SavePanelMod] Generic save panel installed (F5 toggle, ShowPanel graph op, PanelActivationApi).");
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
