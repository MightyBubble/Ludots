using CrowdSimulationUiMod.Systems;
using CrowdSimulationUiMod.UI;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;

namespace CrowdSimulationUiMod;

public sealed class CrowdSimulationUiModEntry : IMod
{
    private const string InstalledKey = "CrowdSimulationUiMod.Installed";
    public const string StyleSheetPath = "assets/CrowdSimulationUi/hud.css";

    public void OnLoad(IModContext context)
    {
        CrowdHudSettings settings = CrowdHudSettings.Parse(ReadText(context, CrowdHudSettings.RelativePath), CrowdHudSettings.RelativePath);
        UiStyleSheet styleSheet = UiCssParser.ParseStyleSheet(ReadText(context, StyleSheetPath));
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.Get(CoreServiceKeys.Engine);
            if (engine == null) return Task.CompletedTask;
            if (engine.GlobalContext.TryGetValue(InstalledKey, out object? installed) && installed is true) return Task.CompletedTask;
            engine.GlobalContext[InstalledKey] = true;
            engine.RegisterPresentationSystem(new CrowdHudPresentationSystem(engine, new CrowdHudController(settings, styleSheet)));
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }

    private static string ReadText(IModContext context, string relativePath)
    {
        using Stream stream = context.GetResource($"{context.ModId}:{relativePath}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
