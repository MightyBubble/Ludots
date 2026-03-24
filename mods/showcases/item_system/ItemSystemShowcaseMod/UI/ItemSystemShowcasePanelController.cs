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
        return context.State.SceneKind switch
        {
            ItemSystemShowcaseSceneKind.Hub => BuildHubRoot(context.State),
            ItemSystemShowcaseSceneKind.LoadoutGarage => BuildFocusedRoot(
                context.State,
                "Loadout Garage",
                "Build a hero one slot at a time and watch passives, actives, and paper-doll changes stay in sync.",
                BuildLoadoutActions(),
                "Stats",
                "Paper Doll",
                "Granted Abilities",
                "Passives + Tags",
                "Deep Detail"),
            ItemSystemShowcaseSceneKind.WeaponBench => BuildFocusedRoot(
                context.State,
                "Weapon Bench",
                "Tune one rifle, prove socket rules, then fire and reload through the same item + GAS stack players would use in combat.",
                BuildWeaponActions(),
                "Bench Readout",
                "Ammo Supply",
                "Sockets",
                "Live Log",
                "Full Equipment"),
            ItemSystemShowcaseSceneKind.ForgeSocketLab => BuildFocusedRoot(
                context.State,
                "Forge & Socket Lab",
                "Craft gems from real resources, then click them into a socketed amulet so nested containers grant passives just like weapon attachments do.",
                BuildForgeActions(),
                "Forge Readout",
                "Gem Sockets",
                "Recipes",
                "Live Log",
                "Forge Stash"),
            _ => BuildFocusedRoot(
                context.State,
                "Raid Loop",
                "Move loot through stash, secure case, vendor, and backpack so the extraction-facing rules read like a compact playable run.",
                BuildRaidActions(),
                "Backpack",
                "Secure Case",
                "Stash",
                "Live Log",
                "Vendor")
        };
    }

    private UiElementBuilder BuildHubRoot(ItemSystemShowcasePanelState state)
    {
        return Ui.Column(
                Ui.ScrollView(
                        Ui.Column(
                                BuildHeroCard(state, includeBackButton: false),
                                Ui.Row(
                                        BuildHubCard(
                                            "Loadout Garage",
                                            "#F0C36B",
                                            state.PrimaryLines,
                                            "Open Loadout Garage",
                                            _ => LoadMap(ItemSystemShowcaseIds.LoadoutGarageMapId)),
                                        BuildHubCard(
                                            "Weapon Bench",
                                            "#7DD3FC",
                                            state.SecondaryLines,
                                            "Open Weapon Bench",
                                            _ => LoadMap(ItemSystemShowcaseIds.WeaponBenchMapId)),
                                        BuildHubCard(
                                            "Raid Loop",
                                            "#8DE3AE",
                                            state.TertiaryLines,
                                            "Open Raid Loop",
                                            _ => LoadMap(ItemSystemShowcaseIds.RaidLoopMapId)),
                                        BuildHubCard(
                                            "Forge & Socket Lab",
                                            "#FFB38A",
                                            state.QuaternaryLines,
                                            "Open Forge Lab",
                                            _ => LoadMap(ItemSystemShowcaseIds.ForgeSocketLabMapId)))
                                    .Gap(12f)
                                    .Wrap()
                                    .Align(UiAlignItems.Start),
                                BuildSection("Shared Tech Promise", "#FFB38A", BuildHubPromiseLines(), width: 620f, height: 180f),
                                BuildSection("Recent Run Log", "#F0C36B", state.LogLines, width: 620f, height: 180f))
                            .Gap(12f)
                            .Padding(16f))
                    .WidthPercent(100f)
                    .HeightPercent(100f))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Background("#16050A12")
            .ZIndex(35);
    }

    private UiElementBuilder BuildFocusedRoot(
        ItemSystemShowcasePanelState state,
        string sceneLabel,
        string playerGoal,
        UiElementBuilder actionPanel,
        string primaryTitle,
        string secondaryTitle,
        string tertiaryTitle,
        string logTitle,
        string deepDetailTitle)
    {
        return Ui.Column(
                Ui.ScrollView(
                        Ui.Column(
                                BuildHeroCard(state, includeBackButton: true),
                                BuildIntentCard(sceneLabel, playerGoal, state.SceneSummary),
                                actionPanel,
                                BuildBoardGallery(state.SceneKind),
                                Ui.Row(
                                        BuildSection(primaryTitle, "#F0C36B", state.PrimaryLines, width: 260f, height: 250f),
                                        BuildSection(secondaryTitle, "#7DD3FC", state.SecondaryLines, width: 280f, height: 250f),
                                        BuildSection(tertiaryTitle, "#8DE3AE", state.TertiaryLines, width: 280f, height: 250f),
                                        BuildSection(logTitle, "#FFB38A", state.LogLines, width: 280f, height: 250f))
                                    .Gap(12f)
                                    .Wrap()
                                    .Align(UiAlignItems.Start),
                                BuildSection(deepDetailTitle, "#FFB38A", state.QuaternaryLines, width: 620f, height: 220f))
                            .Gap(12f)
                            .Padding(16f))
                    .WidthPercent(100f)
                    .HeightPercent(100f))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Background("#16050A12")
            .ZIndex(35);
    }

    private UiElementBuilder BuildHeroCard(ItemSystemShowcasePanelState state, bool includeBackButton)
    {
        var children = new List<UiElementBuilder>
        {
            Ui.Text(state.Header)
                .FontSize(24f)
                .Bold()
                .Color("#F5F7FA"),
            Ui.Text(state.SceneSummary)
                .FontSize(12f)
                .Color("#D5DEE8")
                .WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text(state.HeroSummary)
                .FontSize(12f)
                .Color("#F0C36B")
                .WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text(state.CreditsSummary)
                .FontSize(12f)
                .Color("#93A4B8")
                .WhiteSpace(UiWhiteSpace.Normal)
        };

        if (!string.IsNullOrWhiteSpace(state.DummySummary))
        {
            children.Add(
                Ui.Text(state.DummySummary)
                    .FontSize(12f)
                    .Color("#8DE3AE")
                    .WhiteSpace(UiWhiteSpace.Normal));
        }

        if (!string.IsNullOrWhiteSpace(state.SelectionSummary))
        {
            children.Add(
                Ui.Text(state.SelectionSummary)
                    .FontSize(12f)
                    .Color("#FFB38A")
                    .WhiteSpace(UiWhiteSpace.Normal));
        }

        if (includeBackButton)
        {
            children.Add(
                Ui.Row(
                        BuildActionButton("Back To Hub", "#23415C", _ => LoadMap(ItemSystemShowcaseIds.HubMapId)),
                        BuildActionButton("Loadout", "#5E4518", _ => LoadMap(ItemSystemShowcaseIds.LoadoutGarageMapId)),
                        BuildActionButton("Bench", "#20493A", _ => LoadMap(ItemSystemShowcaseIds.WeaponBenchMapId)),
                        BuildActionButton("Raid", "#7D3326", _ => LoadMap(ItemSystemShowcaseIds.RaidLoopMapId)),
                        BuildActionButton("Forge", "#6A4B1B", _ => LoadMap(ItemSystemShowcaseIds.ForgeSocketLabMapId)))
                    .Gap(8f)
                    .Wrap());
        }

        return Ui.Card(children.ToArray())
            .Width(760f)
            .Padding(16f)
            .Gap(10f)
            .Radius(22f)
            .Background("#09131C")
            .Border(1f, Color("#29435A"));
    }

    private static UiElementBuilder BuildIntentCard(string sceneLabel, string playerGoal, string liveSummary)
    {
        return Ui.Card(
                Ui.Text(sceneLabel)
                    .FontSize(12f)
                    .Bold()
                    .Color("#F0C36B"),
                Ui.Text(playerGoal)
                    .FontSize(13f)
                    .Color("#F5F7FA")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(liveSummary)
                    .FontSize(11f)
                    .Color("#93A4B8")
                    .WhiteSpace(UiWhiteSpace.Normal))
            .Width(760f)
            .Padding(16f)
            .Gap(10f)
            .Radius(22f)
            .Background("#101C29")
            .Border(1f, Color("#2D4860"));
    }

    private UiElementBuilder BuildLoadoutActions()
    {
        return Ui.Card(
                Ui.Text("Loadout Moves")
                    .FontSize(12f)
                    .Bold()
                    .Color("#F0C36B"),
                Ui.Row(
                        BuildActionButton("Toggle Boots", "#5E4518", _ => Run(engine => _runtime.ToggleBoots(engine))),
                        BuildActionButton("Equip Ring", "#23415C", _ => Run(engine => _runtime.EquipRing(engine))),
                        BuildActionButton("Mythic Pulse", "#20493A", _ => Run(engine => _runtime.CastMythicPulse(engine))),
                        BuildActionButton("Second Wind", "#7D3326", _ => Run(engine => _runtime.CastSecondWind(engine))))
                    .Gap(8f)
                    .Wrap(),
                Ui.Text("Target player feeling: change one slot, immediately understand what changed in stats, passives, and active slots.")
                    .FontSize(11f)
                    .Color("#93A4B8")
                    .WhiteSpace(UiWhiteSpace.Normal))
            .Width(760f)
            .Padding(16f)
            .Gap(10f)
            .Radius(22f)
            .Background("#0D1722")
            .Border(1f, Color("#2F475E"));
    }

    private UiElementBuilder BuildWeaponActions()
    {
        return Ui.Card(
                Ui.Text("Weapon Bench Moves")
                    .FontSize(12f)
                    .Bold()
                    .Color("#F0C36B"),
                Ui.Row(
                        BuildActionButton("Attach Grip", "#20493A", _ => Run(engine => _runtime.AttachGrip(engine))),
                        BuildActionButton("Reload", "#5E4518", _ => Run(engine => _runtime.Reload(engine))),
                        BuildActionButton("Fire Rifle", "#7D3326", _ => Run(engine => _runtime.FirePrimary(engine))))
                    .Gap(8f)
                    .Wrap(),
                Ui.Text("Target player feeling: this is one rifle with one magazine and one target, not a spreadsheet of sockets.")
                    .FontSize(11f)
                    .Color("#93A4B8")
                    .WhiteSpace(UiWhiteSpace.Normal))
            .Width(760f)
            .Padding(16f)
            .Gap(10f)
            .Radius(22f)
            .Background("#0D1722")
            .Border(1f, Color("#2F475E"));
    }

    private UiElementBuilder BuildRaidActions()
    {
        return Ui.Card(
                Ui.Text("Raid Loop Moves")
                    .FontSize(12f)
                    .Bold()
                    .Color("#F0C36B"),
                Ui.Row(
                        BuildActionButton("Move Artifact", "#5E4518", _ => Run(engine => _runtime.StoreArtifact(engine))),
                        BuildActionButton("Buy AP Ammo", "#23415C", _ => Run(engine => _runtime.BuyApAmmo(engine))),
                        BuildActionButton("Sell Artifact", "#7D3326", _ => Run(engine => _runtime.SellArtifact(engine))),
                        BuildActionButton("Split Ammo", "#20493A", _ => Run(engine => _runtime.SplitAmmo(engine))))
                    .Gap(8f)
                    .Wrap(),
                Ui.Text("Vendor board is read-only for browsing. Buying and selling stay explicit so pricing rules cannot be bypassed by generic item moves.")
                    .FontSize(11f)
                    .Color("#F5F7FA")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("Target player feeling: route loot through secure case, stash, vendor, and backpack without needing to parse every subsystem at once.")
                    .FontSize(11f)
                    .Color("#93A4B8")
                    .WhiteSpace(UiWhiteSpace.Normal))
            .Width(760f)
            .Padding(16f)
            .Gap(10f)
            .Radius(22f)
            .Background("#0D1722")
            .Border(1f, Color("#2F475E"));
    }

    private UiElementBuilder BuildForgeActions()
    {
        return Ui.Card(
                Ui.Text("Forge Moves")
                    .FontSize(12f)
                    .Bold()
                    .Color("#F0C36B"),
                Ui.Text("Click a recipe card to craft. Click a gem in the forge stash to select it, then click an amulet socket to insert it. Click socketed gems to inspect them.")
                    .FontSize(11f)
                    .Color("#F5F7FA")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("Target player feeling: this is a real item interface for crafting and socketing, not a hidden data transform.")
                    .FontSize(11f)
                    .Color("#93A4B8")
                    .WhiteSpace(UiWhiteSpace.Normal))
            .Width(760f)
            .Padding(16f)
            .Gap(10f)
            .Radius(22f)
            .Background("#0D1722")
            .Border(1f, Color("#2F475E"));
    }

    private static UiElementBuilder BuildHubCard(string title, string accent, IReadOnlyList<string> lines, string ctaLabel, Action<UiActionContext> onClick)
    {
        return Ui.Card(
                Ui.Text(title)
                    .FontSize(18f)
                    .Bold()
                    .Color("#F5F7FA"),
                Ui.Column(BuildLineNodes(lines).ToArray())
                    .Gap(6f),
                BuildActionButton(ctaLabel, accent == "#F0C36B" ? "#5E4518" : accent == "#7DD3FC" ? "#23415C" : "#20493A", onClick))
            .Width(320f)
            .Padding(16f)
            .Gap(12f)
            .Radius(22f)
            .Background("#0E1823")
            .Border(1f, Color("#284154"));
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

    private UiElementBuilder BuildBoardGallery(ItemSystemShowcaseSceneKind sceneKind)
    {
        if (_engine == null)
        {
            return Ui.Card(
                    Ui.Text("Interactive Boards")
                        .FontSize(12f)
                        .Bold()
                        .Color("#F0C36B"),
                    Ui.Text("Engine unavailable.")
                        .FontSize(11f)
                        .Color("#93A4B8"))
                .Width(760f)
                .Padding(16f)
                .Gap(10f)
                .Radius(18f)
                .Background("#0E1823")
                .Border(1f, Color("#284154"));
        }

        ItemSystemShowcaseBoardModel[] boards = _runtime.BuildBoards(_engine, sceneKind);
        if (boards.Length == 0)
        {
            return Ui.Card(
                    Ui.Text("Interactive Boards")
                        .FontSize(12f)
                        .Bold()
                        .Color("#F0C36B"),
                    Ui.Text("No board content in this scene.")
                        .FontSize(11f)
                        .Color("#93A4B8"))
                .Width(760f)
                .Padding(16f)
                .Gap(10f)
                .Radius(18f)
                .Background("#0E1823")
                .Border(1f, Color("#284154"));
        }

        var boardCards = new List<UiElementBuilder>(boards.Length);
        for (int i = 0; i < boards.Length; i++)
        {
            boardCards.Add(BuildBoard(boards[i]));
        }

        return Ui.Card(
                Ui.Text("Interactive Item Boards")
                    .FontSize(12f)
                    .Bold()
                    .Color("#F0C36B"),
                Ui.Text("Click items to select them. Click empty grid cells or named slots to move them. Recipes execute directly from their cards.")
                    .FontSize(11f)
                    .Color("#93A4B8")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(boardCards.ToArray())
                    .Gap(12f)
                    .Wrap()
                    .Align(UiAlignItems.Start))
            .Width(1120f)
            .Padding(16f)
            .Gap(12f)
            .Radius(22f)
            .Background("#0D1722")
            .Border(1f, Color("#2F475E"));
    }

    private UiElementBuilder BuildBoard(ItemSystemShowcaseBoardModel board)
    {
        var rowBuilders = new List<UiElementBuilder>(board.Rows.Length);
        for (int row = 0; row < board.Rows.Length; row++)
        {
            ItemSystemShowcaseBoardCellModel[] cells = board.Rows[row];
            var cellBuilders = new List<UiElementBuilder>(cells.Length);
            for (int col = 0; col < cells.Length; col++)
            {
                cellBuilders.Add(BuildBoardCell(board, cells[col]));
            }

            rowBuilders.Add(
                Ui.Row(cellBuilders.ToArray())
                    .Gap(6f)
                    .Wrap());
        }

        float width = board.Kind switch
        {
            ItemSystemShowcaseBoardKind.Grid => 540f,
            ItemSystemShowcaseBoardKind.Slots => 520f,
            ItemSystemShowcaseBoardKind.Recipes => 320f,
            _ => 420f
        };

        return Ui.Card(
                Ui.Text(board.Title)
                    .FontSize(12f)
                    .Bold()
                    .Color(board.AccentColor),
                Ui.Column(rowBuilders.ToArray())
                    .Gap(6f))
            .Width(width)
            .Padding(14f)
            .Gap(10f)
            .Radius(18f)
            .Background("#0E1823")
            .Border(1f, Color("#284154"));
    }

    private UiElementBuilder BuildBoardCell(ItemSystemShowcaseBoardModel board, ItemSystemShowcaseBoardCellModel cell)
    {
        if (cell.Target.Kind == ItemSystemShowcaseClickTargetKind.None)
        {
            return Ui.Card(
                    Ui.Text(" ")
                        .FontSize(10f)
                        .Color("#0E1823"))
                .Width(board.Kind == ItemSystemShowcaseBoardKind.Recipes ? 260f : 76f)
                .Height(board.Kind == ItemSystemShowcaseBoardKind.Recipes ? 70f : 76f)
                .Padding(6f)
                .Background("#0E1823")
                .Border(1f, Color("#0E1823"));
        }

        string label = string.IsNullOrWhiteSpace(cell.SecondaryText)
            ? cell.PrimaryText
            : $"{cell.PrimaryText}\n{cell.SecondaryText}";
        float width = board.Kind == ItemSystemShowcaseBoardKind.Recipes ? 260f : board.Kind == ItemSystemShowcaseBoardKind.Slots ? 118f : 76f;
        float height = board.Kind == ItemSystemShowcaseBoardKind.Recipes ? 70f : 76f;
        string border = cell.IsSelected ? "#F0C36B" : cell.BorderColor;

        return Ui.Button(label, _ => Run(engine => _runtime.HandleBoardClick(engine, cell.Target)))
            .Width(width)
            .Height(height)
            .Padding(8f)
            .Radius(12f)
            .Background(cell.FillColor)
            .Border(1f, Color(border))
            .Color("#F5F7FA")
            .FontSize(10f)
            .WhiteSpace(UiWhiteSpace.Normal);
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

    private void LoadMap(string mapId)
    {
        if (_engine == null)
        {
            return;
        }

        string? currentMapId = _engine.CurrentMapSession?.MapId.Value;
        if (string.Equals(currentMapId, mapId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(currentMapId))
        {
            _engine.UnloadMap(currentMapId);
        }

        _engine.LoadMap(mapId);
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

    private static string[] BuildHubPromiseLines()
    {
        return new[]
        {
            "One architecture underneath all four rooms.",
            "Equipment, backpack, secure case, vendor, rifle sockets, and gem sockets are all containers plus item placements.",
            "Passive bonuses stay in GAS as real effects and tags.",
            "Active item powers still flow through item-granted ability slots instead of a second runtime."
        };
    }
}
