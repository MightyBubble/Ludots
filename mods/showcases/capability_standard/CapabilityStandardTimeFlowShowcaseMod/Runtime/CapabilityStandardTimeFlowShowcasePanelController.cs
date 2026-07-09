using System;
using System.Collections.Generic;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Surface;

namespace CapabilityStandardTimeFlowShowcaseMod.Runtime;

internal sealed class CapabilityStandardTimeFlowShowcasePanelController
{
    private readonly CapabilityStandardTimeFlowShowcaseRuntime _runtime;
    private ReactivePage<CapabilityStandardTimeFlowShowcasePanelState>? _page;
    private CapabilityStandardTimeFlowShowcasePanelState _lastState = CapabilityStandardTimeFlowShowcasePanelState.Empty;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public CapabilityStandardTimeFlowShowcasePanelController(CapabilityStandardTimeFlowShowcaseRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public bool MountOrSync(UIRoot root, GameEngine engine, in CapabilityStandardTimeFlowShowcasePanelState state)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(engine);
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
        {
            return false;
        }

        _engine = engine;
        ReactivePage<CapabilityStandardTimeFlowShowcasePanelState>? page = EnsurePage();
        if (page == null)
        {
            return false;
        }

        bool changed = !_lease.IsValid || !surfaceHost.Revalidate(_lease);
        if (!StateEquals(in _lastState, in state))
        {
            CapabilityStandardTimeFlowShowcasePanelState snapshot = state;
            page.SetState(_ => snapshot);
            _lastState = snapshot;
            changed = true;
        }

        surfaceHost.PublishReactivePage(
            ref _lease,
            new UiSurfaceLeaseRequest("Showcase.CapabilityStandardTimeFlow.Panel", UiSurfaceSegment.Overlay, priority: 45),
            page);
        return changed;
    }

    public void ClearIfOwned(UIRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (_lease.IsValid &&
            _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost)
        {
            surfaceHost.ReleaseLease(ref _lease);
        }

        _engine = null;
        _lastState = CapabilityStandardTimeFlowShowcasePanelState.Empty;
        _page?.SetState(_ => CapabilityStandardTimeFlowShowcasePanelState.Empty);
    }

    private ReactivePage<CapabilityStandardTimeFlowShowcasePanelState>? EnsurePage()
    {
        if (_page != null)
        {
            return _page;
        }

        GameEngine engine = RequireEngine();
        if (engine.GetService(CoreServiceKeys.UiTextMeasurer) is not IUiTextMeasurer textMeasurer ||
            engine.GetService(CoreServiceKeys.UiImageSizeProvider) is not IUiImageSizeProvider imageSizeProvider)
        {
            return null;
        }

        _page = new ReactivePage<CapabilityStandardTimeFlowShowcasePanelState>(
            textMeasurer,
            imageSizeProvider,
            CapabilityStandardTimeFlowShowcasePanelState.Empty,
            BuildRoot);
        return _page;
    }

    private UiElementBuilder BuildRoot(ReactiveContext<CapabilityStandardTimeFlowShowcasePanelState> context)
    {
        CapabilityStandardTimeFlowShowcasePanelState state = context.State;
        return Ui.Panel(
                BuildWorldLabels(state),
                BuildTopHud(state),
                BuildClockPanel(state),
                BuildBottomHud(state),
                BuildModalLayer(state))
            .Id("capability-standard-timeflow-panel")
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .ZIndex(45);
    }

    private UiElementBuilder BuildWorldLabels(CapabilityStandardTimeFlowShowcasePanelState state)
    {
        var children = new List<UiElementBuilder>
        {
            WorldLabel(
                "Hero",
                BuildHeroWorldStatus(state),
                600f,
                585f,
                "#103544",
                "#51D9F0"),
            WorldLabel(
                "Enemy",
                state.HeroLocalBurstActive ? $"hits {state.HeroComboHitCount}" : "target",
                1045f,
                430f,
                "#3A2E14",
                "#FFD25C"),
            WorldLabel(
                "MassNav Ally",
                state.SimulationPaused || state.HeroLocalBurstActive ? "held" : "pathing",
                705f,
                440f,
                "#162D26",
                "#62D58A"),
            WorldLabel(
                "Physics2D Orb",
                state.SimulationPaused || state.HeroLocalBurstActive ? "held" : "rolling",
                625f,
                315f,
                "#332B16",
                "#E6C35A"),
            WorldLabel(
                "GAS Beat",
                state.GasPaused ? "paused" : $"step {state.GasStep}",
                900f,
                260f,
                "#2A2338",
                "#B794FF")
        };

        if (state.SkillIndicatorPauseActive)
        {
            children.Add(
                Ui.Panel(
                        Ui.Text("Skill target indicator")
                            .FontSize(18f)
                            .Bold()
                            .Color("#DFF8FF"),
                        Ui.Text("World time is paused while the player chooses the landing point.")
                            .FontSize(12f)
                            .Color("#B8DCE8")
                            .WhiteSpace(UiWhiteSpace.Normal))
                    .Width(360f)
                    .Padding(14f)
                    .Gap(6f)
                    .Radius(8f)
                    .Background("#102333")
                    .Border(1f, ParseColor("#4BB7DF"))
                    .Absolute(795f, 520f)
                    .ZIndex(32));
        }

        if (state.HeroSkillCastAgeSteps < 90)
        {
            children.Add(
                Ui.Panel(
                        Ui.Text(state.HeroLocalBurstActive ? "Hero local combo" : "Time Rift landed")
                            .FontSize(18f)
                            .Bold()
                            .Color("#FFE08A"),
                        Ui.Text(state.HeroLocalBurstActive
                                ? $"hits: {state.HeroComboHitCount}"
                                : $"casts: {state.HeroSkillCastCount}")
                            .FontSize(12f)
                            .Color("#FFF1BF"))
                    .Width(230f)
                    .Padding(12f)
                    .Gap(4f)
                    .Radius(8f)
                    .Background("#3A241B")
                    .Border(1f, ParseColor("#E8A54F"))
                    .Absolute(1055f, 350f)
                    .ZIndex(32));
        }

        return Ui.Panel(children.ToArray())
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .PointerEvents(UiPointerEvents.None)
            .ZIndex(28);
    }

    private UiElementBuilder BuildTopHud(CapabilityStandardTimeFlowShowcasePanelState state)
    {
        return Ui.Row(
                Ui.Column(
                        Ui.Text("Aster")
                            .FontSize(22f)
                            .Bold()
                            .Color("#FFE08A"),
                        Ui.Text(state.SimulationPaused ? "Battle paused" : "Battle running")
                            .FontSize(12f)
                            .Color(state.SimulationPaused ? "#FFD3D3" : "#BEEFD3"))
                    .Gap(2f)
                    .FlexGrow(1f),
                HudButton(
                        state.SettingsPauseActive ? "Close Settings" : "Settings",
                        state.SettingsPauseActive,
                        _ => Execute(runtime =>
                        {
                            if (state.SettingsPauseActive)
                            {
                                runtime.CloseSettingsPause();
                            }
                            else
                            {
                                runtime.OpenSettingsPause();
                            }
                        }))
                    .Id("capability-standard-timeflow-settings-toggle"),
                HudButton(
                        state.MenuPauseActive ? "Close Menu" : "Menu",
                        state.MenuPauseActive,
                        _ => Execute(runtime =>
                        {
                            if (state.MenuPauseActive)
                            {
                                runtime.CloseMenuPause();
                            }
                            else
                            {
                                runtime.OpenMenuPause();
                            }
                        }))
                    .Id("capability-standard-timeflow-menu-toggle"),
                HudButton("Reset Cam", false, _ => Execute(runtime => runtime.ResetCamera()))
                    .Id("capability-standard-timeflow-reset-camera"))
            .Width(1568f)
            .Height(68f)
            .Padding(14f, 10f)
            .Gap(10f)
            .Radius(8f)
            .Background("#111B25")
            .Border(1f, ParseColor("#314155"))
            .Absolute(16f, 16f)
            .ZIndex(65);
    }

    private UiElementBuilder BuildClockPanel(CapabilityStandardTimeFlowShowcasePanelState state)
    {
        CapabilityStandardTimeFlowShowcaseConfig config = RequireConfig();
        return Ui.Panel(
                Ui.Row(
                        Ui.Text("Time Flow")
                            .FontSize(17f)
                            .Bold()
                            .Color("#FFE08A")
                            .FlexGrow(1f),
                        Pill(state.SimulationPaused ? "Paused" : "Live", state.SimulationPaused ? "#57313B" : "#214635", "#F5FAFF"))
                    .Align(UiAlignItems.Center)
                    .Gap(8f),
                Ui.Row(
                        Stat("World", FormatScale(state.SimulationScalePermille), state.SimulationPaused),
                        Stat("Skill", FormatScale(state.GasEffectiveScalePermille), state.GasPaused),
                        Stat("Stack", $"{state.ActiveTokenCount}", state.ActivePauseTokenCount > 0))
                    .Gap(8f),
                Ui.Text("Live probes")
                    .FontSize(10f)
                    .Bold()
                    .Color("#8EA0B5"),
                Ui.Row(
                        Stat("MassNav", $"{state.NavigationStepCount}", state.SimulationPaused || state.HeroLocalBurstActive),
                        Stat("Physics", $"{state.PhysicsPositionXCm:0}", state.SimulationPaused || state.HeroLocalBurstActive),
                        Stat("GAS", $"{state.GasStep}", state.GasPaused))
                    .Gap(8f),
                Ui.Text("Pause stack")
                    .FontSize(10f)
                    .Bold()
                    .Color("#8EA0B5"),
                BuildPauseChips(state),
                Ui.Text("Game speed")
                    .FontSize(10f)
                    .Bold()
                    .Color("#8EA0B5"),
                BuildScaleButtons(config.SimulationScaleRequests, state.SimulationScaleLayerOnePermille, index => Execute(runtime => runtime.ApplySimulationScaleLayerOne(index))),
                Ui.Row(
                        SmallButton("Stack 0.5x", state.SimulationScaleLayerTwoPermille == 500, _ => Execute(runtime => runtime.ApplySimulationScaleLayerTwo(0))),
                        SmallButton("Stack 2x", state.SimulationScaleLayerTwoPermille == 2000, _ => Execute(runtime => runtime.ApplySimulationScaleLayerTwo(2))),
                        SmallButton("Clear", false, _ =>
                        {
                            Execute(runtime =>
                            {
                                runtime.ReleaseSimulationScaleLayerOne();
                                runtime.ReleaseSimulationScaleLayerTwo();
                            });
                        }))
                    .Gap(6f)
                    .Wrap(),
                Ui.Text("Skill timer")
                    .FontSize(10f)
                    .Bold()
                    .Color("#8EA0B5"),
                BuildSkillScaleButtons(config.GasScaleRequests, state.GasScaleTokenPermille),
                Ui.Text(state.LastEvent)
                    .FontSize(11f)
                    .Color("#BAC8D8")
                    .WhiteSpace(UiWhiteSpace.Normal))
            .Width(360f)
            .Padding(14f)
            .Gap(9f)
            .Radius(8f)
            .Background("#101820")
            .Border(1f, ParseColor("#40566B"))
            .Absolute(1208f, 104f)
            .ZIndex(64);
    }

    private UiElementBuilder BuildBottomHud(CapabilityStandardTimeFlowShowcasePanelState state)
    {
        return Ui.Row(
                Ui.Panel(
                        Ui.Text("HERO")
                            .FontSize(10f)
                            .Bold()
                            .Color("#B8DCE8"),
                        Ui.Text("Aster")
                            .FontSize(18f)
                            .Bold()
                            .Color("#F4F8FF"),
                        Ui.Text(state.HeroLocalBurstActive
                                ? $"hits {state.HeroComboHitCount}"
                                : $"casts {state.HeroSkillCastCount}")
                            .FontSize(11f)
                            .Color("#AAB8C8"))
                    .Width(130f)
                    .Height(104f)
                    .Padding(12f)
                    .Gap(4f)
                    .Radius(8f)
                    .Background("#12283A")
                    .Border(1f, ParseColor("#4BB7DF")),
                Ui.Column(
                        Ui.Row(
                                Ui.Text("Time Rift")
                                    .FontSize(16f)
                                    .Bold()
                                    .Color("#FFE08A")
                                    .FlexGrow(1f),
                                Pill(
                                    BuildSkillPill(state),
                                    state.SkillIndicatorPauseActive || state.HeroLocalBurstActive ? "#243D54" : "#214635",
                                    "#F5FAFF"))
                            .Align(UiAlignItems.Center)
                            .Gap(8f),
                        Ui.Text(BuildSkillLine(state))
                            .FontSize(11f)
                            .Color("#BAC8D8")
                            .WhiteSpace(UiWhiteSpace.Normal),
                        BuildSkillButtons(state))
                    .Gap(8f)
                    .FlexGrow(1f))
            .Width(760f)
            .Height(132f)
            .Padding(14f)
            .Gap(14f)
            .Radius(8f)
            .Background("#111820")
            .Border(1f, ParseColor("#40566B"))
            .Absolute(420f, 742f)
            .ZIndex(66);
    }

    private UiElementBuilder BuildSkillButtons(CapabilityStandardTimeFlowShowcasePanelState state)
    {
        if (!state.SkillIndicatorPauseActive)
        {
            return Ui.Row(
                    PrimaryButton("Aim Skill", _ => Execute(runtime => runtime.ShowSkillAimMoment()))
                        .Id("capability-standard-timeflow-skill-button"),
                    HudButton("Resume Time", false, _ => Execute(runtime => runtime.ShowRunningMoment())))
                .Gap(8f)
                .Wrap();
        }

        return Ui.Row(
                PrimaryButton("Cast Skill", _ => Execute(runtime => runtime.CastHeroSkill()))
                    .Id("capability-standard-timeflow-cast-skill"),
                HudButton("Cancel Aim", false, _ => Execute(runtime => runtime.CancelHeroSkillAim()))
                    .Id("capability-standard-timeflow-cancel-aim"),
                HudButton(
                        state.SystemGuidePauseActive ? "Close Guide" : "System Guide",
                        state.SystemGuidePauseActive,
                        _ => Execute(runtime =>
                        {
                            if (state.SystemGuidePauseActive)
                            {
                                runtime.DismissSystemGuidePause();
                            }
                            else
                            {
                                runtime.ShowGuideDuringSkillMoment();
                            }
                        }))
                    .Id("capability-standard-timeflow-guide-toggle"))
            .Gap(8f)
            .Wrap();
    }

    private UiElementBuilder BuildModalLayer(CapabilityStandardTimeFlowShowcasePanelState state)
    {
        var children = new List<UiElementBuilder>();
        if (state.SettingsPauseActive)
        {
            children.Add(BuildSettingsWindow());
        }

        if (state.MenuPauseActive)
        {
            children.Add(BuildMenuWindow());
        }

        if (state.SystemGuidePauseActive)
        {
            children.Add(BuildGuideWindow());
        }

        if (state.SimulationPaused)
        {
            children.Add(
                Ui.Panel(
                        Ui.Text("PAUSED")
                            .FontSize(24f)
                            .Bold()
                            .Color("#FFE6A3"),
                        Ui.Text(BuildPauseHeadline(state))
                            .FontSize(12f)
                            .Color("#F3C3C3"))
                    .Width(220f)
                    .Padding(14f)
                    .Gap(4f)
                    .Radius(8f)
                    .Background("#301923")
                    .Border(1f, ParseColor("#7C4B58"))
                    .Absolute(690f, 98f)
                    .ZIndex(58));
        }

        return Ui.Panel(children.ToArray())
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .PointerEvents(UiPointerEvents.None)
            .ZIndex(70);
    }

    private UiElementBuilder BuildSettingsWindow()
    {
        return ModalWindow(
            "Settings",
            560f,
            170f,
            480f,
            Ui.Text("Audio")
                .FontSize(12f)
                .Bold()
                .Color("#8EA0B5"),
            Meter("Master volume", 0.72f, "#43D6C9"),
            Meter("Interface scale", 0.55f, "#8DBBFF"),
            Ui.Text("The settings page owns a system pause request until it closes.")
                .FontSize(11f)
                .Color("#BAC8D8")
                .WhiteSpace(UiWhiteSpace.Normal),
            Ui.Row(
                    PrimaryButton("Close Settings", _ => Execute(runtime => runtime.CloseSettingsPause()))
                        .Id("capability-standard-timeflow-close-settings"),
                    HudButton("Resume Time", false, _ => Execute(runtime => runtime.ShowRunningMoment())))
                .Gap(8f)
                .Wrap());
    }

    private UiElementBuilder BuildMenuWindow()
    {
        return ModalWindow(
            "Battle Menu",
            560f,
            185f,
            480f,
            Ui.Row(MenuTile("Inventory", "3 items"), MenuTile("Orders", "hold position"))
                .Gap(10f),
            Ui.Row(MenuTile("Codex", "new guide"), MenuTile("Party", "1 hero"))
                .Gap(10f),
            Ui.Text("This interface owns a UI pause request. Closing it lets the world continue.")
                .FontSize(11f)
                .Color("#BAC8D8")
                .WhiteSpace(UiWhiteSpace.Normal),
            Ui.Row(
                    PrimaryButton("Close Menu", _ => Execute(runtime => runtime.CloseMenuPause()))
                        .Id("capability-standard-timeflow-close-menu"),
                    HudButton("Resume Time", false, _ => Execute(runtime => runtime.ShowRunningMoment())))
                .Gap(8f)
                .Wrap());
    }

    private UiElementBuilder BuildGuideWindow()
    {
        return ModalWindow(
            "System Guide",
            615f,
            135f,
            430f,
            Ui.Text("A guide can appear while the hero is aiming. It adds a second pause request above the skill indicator.")
                .FontSize(12f)
                .Color("#E8EEF7")
                .WhiteSpace(UiWhiteSpace.Normal),
            Ui.Row(
                    PrimaryButton("Got It", _ => Execute(runtime => runtime.DismissSystemGuidePause()))
                        .Id("capability-standard-timeflow-close-guide"),
                    HudButton("Cast Anyway", false, _ => Execute(runtime => runtime.CastHeroSkill()))
                        .Id("capability-standard-timeflow-cast-anyway"))
                .Gap(8f)
                .Wrap());
    }

    private UiElementBuilder BuildScaleButtons(
        TimeFlowScaleRequestConfig[] requests,
        int activeScalePermille,
        Action<int> apply)
    {
        var buttons = new UiElementBuilder[requests.Length];
        for (int i = 0; i < requests.Length; i++)
        {
            int index = i;
            TimeFlowScaleRequestConfig request = requests[i];
            buttons[i] = SmallButton(
                FormatScale(request.ScalePermille),
                activeScalePermille == request.ScalePermille,
                _ => apply(index));
        }

        return Ui.Row(buttons).Gap(6f).Wrap();
    }

    private UiElementBuilder BuildSkillScaleButtons(TimeFlowScaleRequestConfig[] requests, int activeScalePermille)
    {
        var buttons = new UiElementBuilder[requests.Length + 1];
        for (int i = 0; i < requests.Length; i++)
        {
            int index = i;
            TimeFlowScaleRequestConfig request = requests[i];
            buttons[i] = SmallButton(
                FormatScale(request.ScalePermille),
                activeScalePermille == request.ScalePermille,
                _ => Execute(runtime => runtime.ApplyGasScale(index)));
        }

        buttons[^1] = SmallButton("Clear", false, _ => Execute(runtime => runtime.ReleaseGasScale()));
        return Ui.Row(buttons).Gap(6f).Wrap();
    }

    private static UiElementBuilder BuildPauseChips(CapabilityStandardTimeFlowShowcasePanelState state)
    {
        var chips = new List<UiElementBuilder>();
        if (state.SettingsPauseActive)
        {
            chips.Add(Chip("Settings", "#57313B"));
        }

        if (state.MenuPauseActive)
        {
            chips.Add(Chip("Menu", "#3E314F"));
        }

        if (state.SkillIndicatorPauseActive)
        {
            chips.Add(Chip("Aiming", "#243D54"));
        }

        if (state.SystemGuidePauseActive)
        {
            chips.Add(Chip("Guide", "#5A4323"));
        }

        if (chips.Count == 0)
        {
            chips.Add(Chip("None", "#203041"));
        }

        return Ui.Row(chips.ToArray()).Gap(6f).Wrap();
    }

    private static UiElementBuilder ModalWindow(string title, float left, float top, float width, params UiElementBuilder[] children)
    {
        UiElementBuilder[] all = new UiElementBuilder[children.Length + 1];
        all[0] = Ui.Text(title)
            .FontSize(22f)
            .Bold()
            .Color("#FFE08A");
        Array.Copy(children, 0, all, 1, children.Length);

        return Ui.Panel(all)
            .Width(width)
            .Padding(18f)
            .Gap(12f)
            .Radius(8f)
            .Background("#101820")
            .Border(2f, ParseColor("#5A7692"))
            .Absolute(left, top)
            .ZIndex(82);
    }

    private static UiElementBuilder MenuTile(string title, string value)
    {
        return Ui.Panel(
                Ui.Text(title)
                    .FontSize(13f)
                    .Bold()
                    .Color("#F4F8FF"),
                Ui.Text(value)
                    .FontSize(11f)
                    .Color("#AAB8C8"))
            .Width(210f)
            .Padding(12f)
            .Gap(4f)
            .Radius(8f)
            .Background("#172331")
            .Border(1f, ParseColor("#26384C"));
    }

    private static UiElementBuilder WorldLabel(string title, string status, float left, float top, string background, string border)
    {
        return Ui.Panel(
                Ui.Text(title)
                    .FontSize(12f)
                    .Bold()
                    .Color("#F4F8FF"),
                Ui.Text(status)
                    .FontSize(10f)
                    .Color("#BAC8D8"))
            .Width(120f)
            .Padding(9f)
            .Gap(2f)
            .Radius(8f)
            .Background(background)
            .Border(1f, ParseColor(border))
            .Absolute(left, top)
            .ZIndex(30);
    }

    private static UiElementBuilder Meter(string label, float value01, string color)
    {
        return Ui.Column(
                Ui.Row(
                        Ui.Text(label)
                            .FontSize(11f)
                            .Color("#E3EDF7")
                            .FlexGrow(1f),
                        Ui.Text($"{Math.Clamp(value01, 0f, 1f) * 100f:0}%")
                            .FontSize(11f)
                            .Color("#AAB8C8"))
                    .Align(UiAlignItems.Center),
                ProgressBar(value01, color))
            .Gap(5f);
    }

    private static UiElementBuilder ProgressBar(float progress01, string color)
    {
        float widthPercent = Math.Clamp(progress01, 0.03f, 1f) * 100f;
        return Ui.Panel(
                Ui.Panel()
                    .WidthPercent(widthPercent)
                    .Height(8f)
                    .Radius(8f)
                    .Background(color))
            .Height(8f)
            .WidthPercent(100f)
            .Radius(8f)
            .Background("#233142");
    }

    private static UiElementBuilder Stat(string label, string value, bool alert)
    {
        return Ui.Panel(
                Ui.Text(label)
                    .FontSize(10f)
                    .Color("#8EA0B5"),
                Ui.Text(value)
                    .FontSize(16f)
                    .Bold()
                    .Color(alert ? "#FFD3D3" : "#F4F8FF"))
            .Width(106f)
            .Padding(9f)
            .Gap(2f)
            .Radius(8f)
            .Background(alert ? "#2A202A" : "#172331")
            .Border(1f, ParseColor(alert ? "#7C4B58" : "#26384C"));
    }

    private static UiElementBuilder Chip(string text, string background)
    {
        return Ui.Text(text)
            .FontSize(10f)
            .Bold()
            .Color("#F3F7FB")
            .Padding(8f, 4f)
            .Radius(8f)
            .Background(background)
            .Border(1f, ParseColor("#40566B"));
    }

    private static UiElementBuilder Pill(string text, string background, string color)
    {
        return Ui.Text(text)
            .FontSize(10f)
            .Bold()
            .Color(color)
            .Padding(8f, 4f)
            .Radius(8f)
            .Background(background);
    }

    private static UiElementBuilder HudButton(string label, bool active, Action<UiActionContext> onClick)
    {
        return Ui.Button(label, onClick)
            .Padding(11f, 8f)
            .Radius(8f)
            .Background(active ? "#2A202A" : "#202D3D")
            .Border(1f, ParseColor(active ? "#7C4B58" : "#314155"))
            .Color("#F3F7FB")
            .FontSize(12f);
    }

    private static UiElementBuilder PrimaryButton(string label, Action<UiActionContext> onClick)
    {
        return Ui.Button(label, onClick)
            .Padding(13f, 9f)
            .Radius(8f)
            .Background("#2D5948")
            .Border(1f, ParseColor("#6FAF92"))
            .Color("#F3F7FB")
            .FontSize(13f)
            .Bold();
    }

    private static UiElementBuilder SmallButton(string label, bool active, Action<UiActionContext> onClick)
    {
        return Ui.Button(label, onClick)
            .Padding(8f, 6f)
            .Radius(8f)
            .Background(active ? "#2D5948" : "#202D3D")
            .Border(1f, ParseColor(active ? "#6FAF92" : "#314155"))
            .Color("#F3F7FB")
            .FontSize(10f);
    }

    private static string BuildSkillLine(CapabilityStandardTimeFlowShowcasePanelState state)
    {
        if (state.HeroLocalBurstPausedBySystem)
        {
            return "A system pause is holding the hero local combo, MassNav, Physics2D, and GAS together.";
        }

        if (state.HeroLocalBurstActive)
        {
            return "The hero local combo is running while the MassNav ally and Physics2D orb stay held.";
        }

        if (state.SystemGuidePauseActive)
        {
            return "A tutorial is stacked above the aim pause. Close it or cast anyway.";
        }

        if (state.SkillIndicatorPauseActive)
        {
            return "Choose the target point. The hero, runner, physics probe, and skill timer are all paused.";
        }

        return "Open the indicator, then cast. Settings and menu buttons pause the same world clock.";
    }

    private static string BuildHeroWorldStatus(CapabilityStandardTimeFlowShowcasePanelState state)
    {
        if (state.HeroLocalBurstPausedBySystem)
        {
            return "system paused";
        }

        if (state.HeroLocalBurstActive)
        {
            return "local combo";
        }

        return state.SimulationPaused ? "waiting" : "moving";
    }

    private static string BuildSkillPill(CapabilityStandardTimeFlowShowcasePanelState state)
    {
        if (state.HeroLocalBurstPausedBySystem)
        {
            return "Paused";
        }

        if (state.HeroLocalBurstActive)
        {
            return "Combo";
        }

        return state.SkillIndicatorPauseActive ? "Aiming" : "Ready";
    }

    private static string FormatScale(int scalePermille)
    {
        if (scalePermille <= 0)
        {
            return "0x";
        }

        return $"{scalePermille / 1000f:0.##}x";
    }

    private static string BuildPauseHeadline(CapabilityStandardTimeFlowShowcasePanelState state)
    {
        if (state.ActivePauseTokenCount <= 0)
        {
            return "No pause requests";
        }

        return state.ActivePauseTokenCount == 1
            ? "1 pause request"
            : $"{state.ActivePauseTokenCount} pause requests";
    }

    private void Execute(Action<CapabilityStandardTimeFlowShowcaseRuntime> action)
    {
        GameEngine engine = RequireEngine();
        action(_runtime);
        if (_lease.IsValid &&
            engine.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost)
        {
            surfaceHost.InvalidateLease(_lease);
        }
    }

    private CapabilityStandardTimeFlowShowcaseConfig RequireConfig()
    {
        return _runtime.ActiveConfig;
    }

    private GameEngine RequireEngine()
    {
        return _engine ?? throw new InvalidOperationException("TimeFlow showcase panel requires an active engine.");
    }

    private static UiColor ParseColor(string hex)
    {
        if (!UiColor.TryParse(hex, out UiColor color))
        {
            throw new InvalidOperationException($"Unsupported color literal '{hex}'.");
        }

        return color;
    }

    private static bool StateEquals(
        in CapabilityStandardTimeFlowShowcasePanelState left,
        in CapabilityStandardTimeFlowShowcasePanelState right)
    {
        return string.Equals(left.Title, right.Title, StringComparison.Ordinal) &&
               string.Equals(left.LastEvent, right.LastEvent, StringComparison.Ordinal) &&
               left.SimulationScalePermille == right.SimulationScalePermille &&
               left.GasEffectiveScalePermille == right.GasEffectiveScalePermille &&
               left.GasPolicyScalePermille == right.GasPolicyScalePermille &&
               left.SimulationPaused == right.SimulationPaused &&
               left.GasPaused == right.GasPaused &&
               left.ActiveTokenCount == right.ActiveTokenCount &&
               left.ActivePauseTokenCount == right.ActivePauseTokenCount &&
               left.ActiveScaleTokenCount == right.ActiveScaleTokenCount &&
               string.Equals(left.ActiveTokenSummary, right.ActiveTokenSummary, StringComparison.Ordinal) &&
               string.Equals(left.PauseTokenStackSummary, right.PauseTokenStackSummary, StringComparison.Ordinal) &&
               string.Equals(left.ScaleTokenStackSummary, right.ScaleTokenStackSummary, StringComparison.Ordinal) &&
               left.SettingsPauseActive == right.SettingsPauseActive &&
               left.MenuPauseActive == right.MenuPauseActive &&
               left.SkillIndicatorPauseActive == right.SkillIndicatorPauseActive &&
               left.SystemGuidePauseActive == right.SystemGuidePauseActive &&
               left.SimulationScaleLayerOnePermille == right.SimulationScaleLayerOnePermille &&
               left.SimulationScaleLayerTwoPermille == right.SimulationScaleLayerTwoPermille &&
               left.GasScaleTokenPermille == right.GasScaleTokenPermille &&
               left.HeroSkillCastCount == right.HeroSkillCastCount &&
               left.HeroSkillCastAgeSteps == right.HeroSkillCastAgeSteps &&
               left.HeroLocalBurstActive == right.HeroLocalBurstActive &&
               left.HeroLocalBurstPausedBySystem == right.HeroLocalBurstPausedBySystem &&
               left.HeroLocalBurstTick == right.HeroLocalBurstTick &&
               left.HeroComboHitCount == right.HeroComboHitCount &&
               Math.Abs(left.HeroLocalClockSeconds - right.HeroLocalClockSeconds) < 0.001f &&
               Math.Abs(left.HeroLocalPositionXCm - right.HeroLocalPositionXCm) < 0.01f &&
               Math.Abs(left.HeroLocalPositionYCm - right.HeroLocalPositionYCm) < 0.01f &&
               Math.Abs(left.EnemyPositionXCm - right.EnemyPositionXCm) < 0.01f &&
               Math.Abs(left.EnemyPositionYCm - right.EnemyPositionYCm) < 0.01f &&
               left.NavigationStepCount == right.NavigationStepCount &&
               Math.Abs(left.NavPositionXCm - right.NavPositionXCm) < 0.01f &&
               Math.Abs(left.NavPositionYCm - right.NavPositionYCm) < 0.01f &&
               Math.Abs(left.PhysicsPositionXCm - right.PhysicsPositionXCm) < 0.01f &&
               Math.Abs(left.PhysicsPositionYCm - right.PhysicsPositionYCm) < 0.01f &&
               Math.Abs(left.PhysicsVelocityXCm - right.PhysicsVelocityXCm) < 0.01f &&
               Math.Abs(left.PhysicsVelocityYCm - right.PhysicsVelocityYCm) < 0.01f &&
               left.GasFixedFrame == right.GasFixedFrame &&
               left.GasStep == right.GasStep;
    }
}
