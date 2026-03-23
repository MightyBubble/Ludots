using System;
using System.Collections.Generic;
using ItemSystemShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;

namespace ItemSystemShowcaseMod.UI;

internal sealed class ItemSystemShowcasePanelController
{
    private readonly ItemSystemShowcaseRuntime _runtime;
    private ReactivePage<ItemSystemShowcasePanelState>? _page;
    private GameEngine? _engine;

    public ItemSystemShowcasePanelController(ItemSystemShowcaseRuntime runtime)
    {
        _runtime = runtime;
    }

    public void MountOrRefresh(UIRoot root, GameEngine engine)
    {
        _engine = engine;
        ItemSystemShowcasePanelState nextState = _runtime.BuildState(engine);
        if (_page == null)
        {
            var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<ItemSystemShowcasePanelState>(textMeasurer, imageSizeProvider, nextState, BuildRoot);
        }
        else if (!_page.State.Equals(nextState))
        {
            _page.SetState(_ => nextState);
        }

        if (!ReferenceEquals(root.Scene, _page.Scene))
        {
            root.MountScene(_page.Scene);
        }

        root.IsDirty = true;
    }

    public void ClearIfOwned(UIRoot root)
    {
        if (_page != null && ReferenceEquals(root.Scene, _page.Scene))
        {
            root.ClearScene();
        }
    }

    private UiElementBuilder BuildRoot(ReactiveContext<ItemSystemShowcasePanelState> context)
    {
        ItemSystemShowcasePanelState state = context.State;
        return Ui.Column(
                Ui.ScrollView(
                        Ui.Column(
                                Ui.Row(
                                        BuildHeroPanel(state),
                                        BuildResourcePanel(state),
                                        BuildActionPanel())
                                    .Gap(12f)
                                    .Wrap()
                                    .Align(UiAlignItems.Start),
                                Ui.Row(
                                        BuildSection("Stats", "#F0C36B", state.StatLines, width: 220f),
                                        BuildSection("Abilities", "#7DD3FC", state.AbilityLines, width: 280f),
                                        BuildSection("Buffs + Tags", "#8DE3AE", state.BuffLines, width: 280f),
                                        BuildSection("Equipment + Sockets", "#FFB38A", state.EquipmentLines, width: 280f))
                                    .Gap(12f)
                                    .Wrap()
                                    .Align(UiAlignItems.Start),
                                Ui.Row(
                                        BuildSection("Backpack", "#F0C36B", state.BackpackLines, width: 250f, height: 254f),
                                        BuildSection("Secure", "#7DD3FC", state.SecureLines, width: 220f, height: 254f),
                                        BuildSection("Stash", "#8DE3AE", state.StashLines, width: 280f, height: 254f),
                                        BuildSection("Vendor", "#FFB38A", state.VendorLines, width: 250f, height: 254f))
                                    .Gap(12f)
                                    .Wrap()
                                    .Align(UiAlignItems.Start),
                                BuildSection("Scenario Log", "#F0C36B", state.LogLines, width: 620f, height: 210f))
                            .Gap(12f)
                            .Padding(16f))
                    .WidthPercent(100f)
                    .HeightPercent(100f))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Background("#16050A12")
            .ZIndex(35);
    }

    private UiElementBuilder BuildHeroPanel(ItemSystemShowcasePanelState state)
    {
        return Ui.Card(
                Ui.Text(state.Header)
                    .FontSize(24f)
                    .Bold()
                    .Color("#F5F7FA"),
                Ui.Text(state.HeroSummary)
                    .FontSize(12f)
                    .Color("#D5DEE8")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.DummySummary)
                    .FontSize(12f)
                    .Color("#F0C36B")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("Unified entity-first model: equipment, backpack, stash, vendor, secure case, and rifle sockets all run through the same container + item placement rules.")
                    .FontSize(11f)
                    .Color("#93A4B8")
                    .WhiteSpace(UiWhiteSpace.Normal))
            .Width(360f)
            .Padding(16f)
            .Gap(10f)
            .Radius(22f)
            .Background("#09131C")
            .Border(1f, Color("#29435A"));
    }

    private UiElementBuilder BuildResourcePanel(ItemSystemShowcasePanelState state)
    {
        return Ui.Card(
                Ui.Text("Economy + Ammo")
                    .FontSize(12f)
                    .Bold()
                    .Color("#F0C36B"),
                Ui.Text(state.CreditsSummary)
                    .FontSize(13f)
                    .Color("#F5F7FA")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("Coverage: MOBA passives, ARPG jewelry, extraction storage, modular rifle attachments, ammo stacks, reload loop, vendor buy/sell, split stack, and secure loot routing.")
                    .FontSize(11f)
                    .Color("#93A4B8")
                    .WhiteSpace(UiWhiteSpace.Normal))
            .Width(360f)
            .Padding(16f)
            .Gap(10f)
            .Radius(22f)
            .Background("#101C29")
            .Border(1f, Color("#2D4860"));
    }

    private UiElementBuilder BuildActionPanel()
    {
        return Ui.Card(
                Ui.Text("Playable Controls")
                    .FontSize(12f)
                    .Bold()
                    .Color("#F0C36B"),
                Ui.Row(
                        BuildActionButton("Toggle Boots", "#5E4518", _ => Run(engine => _runtime.ToggleBoots(engine))),
                        BuildActionButton("Equip Ring", "#23415C", _ => Run(engine => _runtime.EquipRing(engine))),
                        BuildActionButton("Attach Grip", "#20493A", _ => Run(engine => _runtime.AttachGrip(engine))))
                    .Gap(8f)
                    .Wrap(),
                Ui.Row(
                        BuildActionButton("Reload", "#5E4518", _ => Run(engine => _runtime.Reload(engine))),
                        BuildActionButton("Fire Rifle", "#7D3326", _ => Run(engine => _runtime.FirePrimary(engine))),
                        BuildActionButton("Mythic Pulse", "#23415C", _ => Run(engine => _runtime.CastMythicPulse(engine))),
                        BuildActionButton("Second Wind", "#20493A", _ => Run(engine => _runtime.CastSecondWind(engine))))
                    .Gap(8f)
                    .Wrap(),
                Ui.Row(
                        BuildActionButton("Move Artifact", "#5E4518", _ => Run(engine => _runtime.StoreArtifact(engine))),
                        BuildActionButton("Buy AP Ammo", "#23415C", _ => Run(engine => _runtime.BuyApAmmo(engine))),
                        BuildActionButton("Sell Artifact", "#7D3326", _ => Run(engine => _runtime.SellArtifact(engine))),
                        BuildActionButton("Split Ammo", "#20493A", _ => Run(engine => _runtime.SplitAmmo(engine))))
                    .Gap(8f)
                    .Wrap())
            .Width(620f)
            .Padding(16f)
            .Gap(10f)
            .Radius(22f)
            .Background("#0D1722")
            .Border(1f, Color("#2F475E"));
    }

    private static UiElementBuilder BuildSection(string title, string accent, IReadOnlyList<string> lines, float width, float height = 220f)
    {
        return Ui.Card(
                Ui.Text(title)
                    .FontSize(12f)
                    .Bold()
                    .Color(accent),
                Ui.ScrollView(
                        Ui.Column(BuildLineNodes(lines).ToArray())
                            .Gap(6f))
                    .Height(height - 42f))
            .Width(width)
            .Height(height)
            .Padding(14f)
            .Gap(10f)
            .Radius(18f)
            .Background("#0E1823")
            .Border(1f, Color("#284154"));
    }

    private static List<UiElementBuilder> BuildLineNodes(IReadOnlyList<string> lines)
    {
        var nodes = new List<UiElementBuilder>(Math.Max(1, lines.Count));
        if (lines.Count == 0)
        {
            nodes.Add(Ui.Text("(empty)").FontSize(11f).Color("#93A4B8"));
            return nodes;
        }

        for (int i = 0; i < lines.Count; i++)
        {
            nodes.Add(
                Ui.Text(lines[i])
                    .FontSize(11f)
                    .Color(i == 0 ? "#F5F7FA" : "#C7D0DD")
                    .WhiteSpace(UiWhiteSpace.Normal));
        }

        return nodes;
    }

    private static UiElementBuilder BuildActionButton(string label, string background, Action<UiActionContext> onClick)
    {
        return Ui.Button(label, onClick)
            .Padding(10f, 8f)
            .Radius(10f)
            .Background(background)
            .Border(1f, Color("#44FFFFFF"))
            .Color("#F5F7FA");
    }

    private void Run(Action<GameEngine> action)
    {
        if (_engine == null)
        {
            return;
        }

        action(_engine);
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
