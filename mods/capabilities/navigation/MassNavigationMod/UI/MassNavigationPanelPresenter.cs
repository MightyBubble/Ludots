using Ludots.Core.Engine;
using Ludots.Core.MassCrowd;
using Ludots.Core.MassCrowd.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.UI;

namespace MassNavigationMod.UI;

internal sealed class MassNavigationPanelPresenter
{
    private readonly MassNavigationPanelController _panelController = new();

    public void RefreshPanel(GameEngine engine)
    {
        MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new System.InvalidOperationException("MassNavigationMod panel requires core MassCrowd simulation runtime.");
        if (!simulation.Config.ScenarioRuntime.Panel.IsOwned)
        {
            ClearPanelIfOwned(engine);
            return;
        }

        if (!MassNavigationIds.IsCurrentNavigationMap(engine))
        {
            ClearPanelIfOwned(engine);
            return;
        }

        if (engine.GetService(CoreServiceKeys.RenderDebugState) is RenderDebugState renderDebug)
        {
            renderDebug.DrawSkiaUi = true;
        }

        _panelController.MountOrSync(engine, simulation);
    }

    public void ClearPanelIfOwned(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.ClearIfOwned(root);
        }
    }
}
