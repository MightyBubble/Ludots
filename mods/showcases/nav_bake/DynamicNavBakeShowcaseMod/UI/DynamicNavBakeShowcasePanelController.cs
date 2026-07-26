using System;
using DynamicNavBakeShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace DynamicNavBakeShowcaseMod.UI;

internal sealed class DynamicNavBakeShowcasePanelController
{
    private ReactivePage<DynamicNavBakeShowcasePanelState>? _page;
    private GameEngine? _engine;
    private readonly DynamicNavBakeShowcaseRuntime _runtime;
    private UiSurfaceLeaseHandle _lease;

    public DynamicNavBakeShowcasePanelController(DynamicNavBakeShowcaseRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public void MountOrSync(UIRoot root, GameEngine engine, in DynamicNavBakeShowcasePanelState state)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(engine);
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
        {
            throw new InvalidOperationException(
                "DynamicNavBake showcase panel requires CoreServiceKeys.UiSurfaceHost once UIRoot is present.");
        }

        _engine = engine;
        ReactivePage<DynamicNavBakeShowcasePanelState> page = EnsurePage();
        DynamicNavBakeShowcasePanelState snapshot = state;
        page.SetState(_ => snapshot);
        surfaceHost.Publish(
            surfaceHost.EnsureLease(
                ref _lease,
                new UiSurfaceLeaseRequest("DynamicNavBakeShowcase.Panel", UiSurfaceSegment.Overlay, priority: 30)),
            UiSurfaceContribution.FromReactivePage(page));
    }

    public void ClearIfOwned(UIRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (_lease.IsValid)
        {
            GameEngine engine = _engine
                ?? throw new InvalidOperationException(
                    "DynamicNavBake showcase panel has an active lease without its owning engine.");
            if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
            {
                throw new InvalidOperationException(
                    "DynamicNavBake showcase panel requires CoreServiceKeys.UiSurfaceHost to release its active lease.");
            }
            surfaceHost.Release(_lease);
            _lease = default;
        }

        _engine = null;
    }

    private ReactivePage<DynamicNavBakeShowcasePanelState> EnsurePage()
    {
        if (_page != null)
        {
            return _page;
        }

        GameEngine engine = RequireEngine();
        if (engine.GetService(CoreServiceKeys.UiTextMeasurer) is not IUiTextMeasurer textMeasurer)
        {
            throw new InvalidOperationException(
                "DynamicNavBake showcase panel requires CoreServiceKeys.UiTextMeasurer once UIRoot is present.");
        }

        if (engine.GetService(CoreServiceKeys.UiImageSizeProvider) is not IUiImageSizeProvider imageSizeProvider)
        {
            throw new InvalidOperationException(
                "DynamicNavBake showcase panel requires CoreServiceKeys.UiImageSizeProvider once UIRoot is present.");
        }

        _page = new ReactivePage<DynamicNavBakeShowcasePanelState>(
            textMeasurer,
            imageSizeProvider,
            DynamicNavBakeShowcasePanelState.Empty,
            BuildRoot);
        return _page;
    }

    private UiElementBuilder BuildRoot(ReactiveContext<DynamicNavBakeShowcasePanelState> context)
    {
        DynamicNavBakeShowcasePanelState state = context.State;
        DynamicNavBakeShowcaseConfig config = _runtime.ActiveConfig;
        string buildLabel = state.ConstructionMode ? "取消建造" : "建造建筑";
        string navLabel = state.NavMeshVisible ? "隐藏 NavMesh" : "显示 NavMesh";
        return Ui.Card(
                Ui.Text(state.Title).FontSize(15f).Bold().Color("#F7FAFF"),
                Ui.Text(state.Status).FontSize(11f).Color("#E7EEF5").WhiteSpace(UiWhiteSpace.Normal)
                    .Id(DynamicNavBakeShowcaseIds.StatusTextElementId),
                Ui.Row(
                    CommandButton(
                        buildLabel,
                        ToggleConstruction,
                        state.ConstructionMode ? "#A85A2B" : "#27604B",
                        DynamicNavBakeShowcaseIds.BuildBuildingButtonElementId),
                    CommandButton(
                        navLabel,
                        () => SetNavMeshVisible(!state.NavMeshVisible),
                        "#3E566D",
                        DynamicNavBakeShowcaseIds.NavMeshVisibilityButtonElementId))
                    .Gap(6f))
            .Id(DynamicNavBakeShowcaseIds.PanelElementId)
            .Width(config.Ui.Width)
            .Padding(10f)
            .Gap(6f)
            .Radius(8f)
            .Background("#D40B0E13")
            .Border(1f, Color("#33879AB3"))
            .BackdropBlur(8f)
            .Absolute(config.Ui.AbsoluteLeft, config.Ui.AbsoluteTop)
            .ZIndex(30);
    }

    private void ToggleConstruction()
    {
        InvokeCommand((GameEngine engine, out string error) =>
        {
            DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseActions.Require(engine);
            return actions.ConstructionMode
                ? actions.TryExitConstructionMode(engine, out error)
                : actions.TryEnterConstructionMode(engine, out error);
        });
    }

    private void SetNavMeshVisible(bool visible)
        => InvokeCommand((GameEngine engine, out string error) =>
            DynamicNavBakeShowcaseActions.Require(engine).TrySetNavMeshVisible(engine, visible, out error));

    private delegate bool ShowcaseCommand(GameEngine engine, out string error);

    private void InvokeCommand(ShowcaseCommand action)
    {
        GameEngine engine = _engine
            ?? throw new InvalidOperationException(
                "DynamicNavBake showcase command button requires a mounted engine before Invoke.");

        if (!action(engine, out string error))
        {
            if (string.IsNullOrEmpty(error))
            {
                throw new InvalidOperationException(
                    "DynamicNavBake showcase command returned false without an error; empty rejection is a broken contract.");
            }
        }

        if (!_lease.IsValid)
        {
            throw new InvalidOperationException(
                "DynamicNavBake showcase command completed without an active panel surface lease.");
        }

        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
        {
            throw new InvalidOperationException(
                "DynamicNavBake showcase command requires CoreServiceKeys.UiSurfaceHost while the panel is mounted.");
        }
        if (!surfaceHost.Revalidate(_lease))
        {
            throw new InvalidOperationException(
                "DynamicNavBake showcase panel surface lease became invalid while executing a command.");
        }

        surfaceHost.Invalidate(_lease);
    }

    private GameEngine RequireEngine()
        => _engine ?? throw new InvalidOperationException("DynamicNavBake showcase panel requires an active engine.");

    private static UiElementBuilder CommandButton(string label, Action onClick, string background, string elementId)
    {
        return Ui.Button(label, _ => onClick())
            .Id(elementId)
            .Padding(8f, 5f)
            .Radius(6f)
            .Background(background)
            .Color("#F7FAFF")
            .FontSize(11f);
    }

    private static UiColor Color(string hex)
    {
        if (!UiColor.TryParse(hex, out UiColor color))
        {
            throw new InvalidOperationException($"Unsupported color literal '{hex}'.");
        }

        return color;
    }
}
