using Ludots.Core.Modding;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using NavBakeIslandShowcaseMod.Runtime;

namespace NavBakeIslandShowcaseMod;

public sealed class NavBakeIslandShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Log("[NavBakeIslandShowcaseMod] Configuration-driven island navigation showcase loaded.");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine engine = ctx.Get(CoreServiceKeys.Engine)
                ?? throw new InvalidOperationException("NavBake Island showcase requires the engine service.");
            ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("NavBake Island showcase requires ScreenOverlayBuffer.");
            PresenterAnimatorStateBuffer animatorStates = engine.GetService(CoreServiceKeys.PresenterAnimatorStateBuffer)
                ?? throw new InvalidOperationException("NavBake Island showcase requires PresenterAnimatorStateBuffer.");
            AnimatorControllerRegistry controllers = engine.GetService(CoreServiceKeys.AnimatorControllerRegistry)
                ?? throw new InvalidOperationException("NavBake Island showcase requires AnimatorControllerRegistry.");
            engine.RegisterPresentationSystem(new NavBakeIslandShowcasePresentationSystem(
                engine,
                overlay,
                animatorStates,
                controllers));
            return System.Threading.Tasks.Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
