using System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelActivation;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace Ludots.UI.Panels;

/// <summary>
/// Single installation entry for engine-side panel presentation. Called by host
/// composers (raylib/web) and test harnesses after UIRoot/UiSurfaceHost exist.
/// Selection is read from merged game.json ("panelSkin"); panels appear with zero
/// mod code — the 0-encoding promise of contract four.
/// </summary>
public static class PanelPresentationInstaller
{
    public static void Install(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);


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

        PanelTheme? theme = PanelThemeCatalog.TryLoad(engine);
        var textMeasurer = engine.GetService(CoreServiceKeys.UiTextMeasurer) as Ludots.UI.Runtime.IUiTextMeasurer;
        var imageSizeProvider = engine.GetService(CoreServiceKeys.UiImageSizeProvider) as Ludots.UI.Runtime.IUiImageSizeProvider;
        var seats = engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out Ludots.Core.Client.ClientLocalSeatRegistry? seatRegistry)
            ? seatRegistry
            : null;
        var displayResolver = engine.GetService(CoreServiceKeys.PresentationDisplayResolver);

        PanelEventActionBridge? eventBridge = null;
        if (engine.TryGetService(CoreServiceKeys.ClientLocalSeatInputRuntime, out Ludots.Core.Client.ClientLocalSeatInputRuntime? seatInput) &&
            seatInput != null)
        {
            ValidateTemplateEventActions(templates, engine);
            eventBridge = new PanelEventActionBridge(
                activation,
                () => engine.TryGetService(CoreServiceKeys.ClientLocalSeatInputRuntime, out Ludots.Core.Client.ClientLocalSeatInputRuntime? runtime)
                    ? runtime
                    : null,
                () => engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out Ludots.Core.Client.ClientLocalSeatRegistry? seats)
                    ? seats
                    : null,
                () => engine.TryGetService(CoreServiceKeys.InputHandler, out Ludots.Core.Input.Runtime.PlayerInputHandler? handler)
                    ? handler
                    : null);
            engine.SetService(CoreServiceKeys.PanelEventActionBridge, eventBridge);
        }

        engine.RegisterPresentationSystem(new PanelPresentationSystem(
            panelHost,
            templates,
            activation,
            surfaceHost,
            root,
            engine.MergedConfig?.PanelSkin,
            theme?.StyleSheet,
            textMeasurer,
            imageSizeProvider,
            displayResolver,
            seats,
            eventBridge,
            new PanelTipOverlay((UiSurfaceHost)surfaceHost)));
    }

    /// <summary>
    /// Install-time contract check for the Button chain: a control-bound event fires as a
    /// semantic action (eventId 即 action id), so its id must exist in the input config
    /// action catalog. Control-less events keep the programmatic dispatch vocabulary of the
    /// #1013 MVP until that surface migrates. Failing here names the panel, the event, and
    /// the control — a clickable panel whose action cannot be attributed is an authoring
    /// bug, not a runtime concern.
    /// </summary>
    private static void ValidateTemplateEventActions(PanelTemplateRegistry templates, GameEngine engine)
    {
        if (!engine.TryGetService(CoreServiceKeys.InputActionIds, out System.Collections.Frozen.FrozenSet<string>? actionIds) ||
            actionIds == null)
        {
            return;
        }

        foreach (PanelTemplate template in templates.Snapshot())
        {
            foreach (PanelTemplateEvent declaration in template.Events)
            {
                if (declaration.Control == null)
                {
                    continue;
                }

                if (!actionIds.Contains(declaration.EventId))
                {
                    throw new InvalidOperationException(
                        $"PANEL.EVENT.ERR.ActionNotDeclared: panel template '{template.Id}' event '{declaration.EventId}' " +
                        "is not a declared input action — panel event ids must be registered in the input config action catalog.");
                }
            }
        }
    }
}
