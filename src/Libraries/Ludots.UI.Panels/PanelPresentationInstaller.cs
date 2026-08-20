using System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelActivation;
using Ludots.Core.UI.PanelHosting;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace Ludots.UI.Panels;

/// <summary>
/// Single installation entry for engine-side panel presentation. Called by host
/// composers (raylib/web) and test harnesses after UIRoot/UiSurfaceHost exist.
/// Selection is read from merged game.json ("panelSkin"); panels appear with zero
/// mod code — the 0-encoding promise of #858 contract four.
/// </summary>
public static class PanelPresentationInstaller
{
    public static void Install(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        string? requestedSkin = engine.MergedConfig?.PanelSkin;
        if (PanelSkinCatalog.IsBrowserStackSkin(requestedSkin))
        {
            // The browser UI stack owns rendering for the "web" skin; when no browser
            // runtime is provisioned (headless hosts) the panels stay data-alive with
            // no surface — the ControlPlane headless precedent.
            return;
        }

        PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("Panel presentation requires PanelHost engine service.");
        PanelTemplateRegistry templates = engine.GetService(CoreServiceKeys.PanelTemplateRegistry)
            ?? throw new InvalidOperationException("Panel presentation requires PanelTemplateRegistry engine service.");
        UiPanelActivationStore activation = engine.GetService(CoreServiceKeys.PanelActivationStore)
            ?? throw new InvalidOperationException("Panel presentation requires PanelActivationStore engine service.");
        IUiSurfaceHost surfaceHost = engine.GetService(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("Panel presentation requires UiSurfaceHost engine service.");
        UIRoot root = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("Panel presentation requires UIRoot engine service.");

        PanelSkinDescriptor skin = PanelSkinCatalog.Resolve(engine.MergedConfig?.PanelSkin);
        engine.RegisterPresentationSystem(new PanelPresentationSystem(
            panelHost,
            templates,
            activation,
            surfaceHost,
            root,
            skin));
    }
}
