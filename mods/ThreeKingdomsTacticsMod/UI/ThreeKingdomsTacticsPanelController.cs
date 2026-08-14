using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using ThreeKingdomsTacticsMod.Runtime;

namespace ThreeKingdomsTacticsMod.UI;

internal sealed class ThreeKingdomsTacticsPanelController
{
    private UiSurfaceLeaseHandle _lease;
    private GameEngine? _engine;

    public void MountOrRefresh(UIRoot root, GameEngine engine, ThreeKingdomsTacticsRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(runtime);

        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
        {
            return;
        }

        _engine = engine;
        UiSurfaceLeaseRequest request = new(
            "ThreeKingdomsTactics.NativeHud",
            UiSurfaceSegment.Main,
            priority: 12,
            exclusive: true);
        _lease = surfaceHost.EnsureLease(ref _lease, request);
        surfaceHost.Publish(
            _lease,
            UiSurfaceContribution.FromBuilder(() => BuildRoot(engine, runtime)));
    }

    public void ClearIfOwned(UIRoot root)
    {
        if (_engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost)
        {
            surfaceHost.ReleaseLease(ref _lease);
        }
        _engine = null;
    }

    private static UiElementBuilder BuildRoot(GameEngine engine, ThreeKingdomsTacticsRuntime runtime)
    {
        ThreeKingdomsTacticsSnapshot snapshot = runtime.Snapshot;
        return Ui.Panel(
                BuildTopBar(snapshot),
                Ui.Row(
                        BuildMap(runtime),
                        BuildRightRail(snapshot))
                    .Gap(14f)
                    .FlexGrow(1f),
                BuildCommandBar(engine, runtime, snapshot))
            .Id("three-kingdoms-tactics-root")
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Padding(16f)
            .Gap(12f)
            .Background("#10141B");
    }

    private static UiElementBuilder BuildTopBar(ThreeKingdomsTacticsSnapshot snapshot)
    {
        return Ui.Row(
                Ui.Column(
                        Ui.Text("Three Kingdoms Grand Tactics").FontSize(24f).Bold().Color("#F7D774"),
                        Ui.Text($"Round {snapshot.Round} | Turn {snapshot.Turn} | {snapshot.Phase} | {snapshot.PlayerUnitsAlive} vs {snapshot.EnemyUnitsAlive}")
                            .FontSize(14f)
                            .Color("#DDE7F2"))
                    .Gap(4f)
                    .FlexGrow(1f),
                BuildMetric("Generals", snapshot.AllGenerals.ToString()),
                BuildMetric("Skills", snapshot.UniqueSkills.ToString()),
                BuildMetric("Troops", snapshot.TroopTypes.ToString()),
                BuildMetric("Arsenal", snapshot.ArsenalItems.ToString()),
                BuildMetric("GAS", snapshot.GasAbilityDefinitions.ToString()),
                BuildMetric("Graph", snapshot.GraphPrograms.ToString()))
            .Id("three-kingdoms-top-bar")
            .Padding(12f)
            .Gap(10f)
            .Background("#182231")
            .Radius(6f)
            .Border(1f, RequireColor("#36516E"));
    }

    private static UiElementBuilder BuildMetric(string label, string value)
    {
        return Ui.Column(
                Ui.Text(label).FontSize(11f).Color("#9FB3C8"),
                Ui.Text(value).FontSize(18f).Bold().Color("#FFFFFF"))
            .Width(84f)
            .Height(50f)
            .Padding(8f)
            .Gap(2f)
            .Background("#213147")
            .Radius(6f);
    }

    private static UiElementBuilder BuildMap(ThreeKingdomsTacticsRuntime runtime)
    {
        string[] rows = runtime.BuildMapRows();
        UiElementBuilder[] rowNodes = new UiElementBuilder[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            rowNodes[i] = Ui.Text(rows[i])
                .FontFamily("Consolas")
                .FontSize(10f)
                .Color("#CFE1F3")
                .WhiteSpace(UiWhiteSpace.Pre);
        }

        return Ui.Column(
                Ui.Text("Seamless Campaign Map").FontSize(16f).Bold().Color("#F7D774"),
                Ui.Column(rowNodes)
                    .Gap(0f)
                    .Padding(10f)
                    .Background("#0A1017")
                    .Border(1f, RequireColor("#35597D"))
                    .Radius(4f)
                    .FlexGrow(1f))
            .Id("three-kingdoms-map")
            .Gap(8f)
            .FlexGrow(1f);
    }

    private static UiElementBuilder BuildRightRail(ThreeKingdomsTacticsSnapshot snapshot)
    {
        ThreeKingdomsUnitView selected = snapshot.Units.FirstOrDefault(unit => unit.Selected)
            ?? new ThreeKingdomsUnitView(0, "None", "", 0, 0, 0, 0, 0, 0, "", "", "", false, false);
        return Ui.Column(
                BuildSelectedPanel(selected),
                BuildRosterPanel(snapshot),
                BuildLogPanel(snapshot))
            .Width(420f)
            .Gap(10f);
    }

    private static UiElementBuilder BuildSelectedPanel(ThreeKingdomsUnitView unit)
    {
        return Ui.Column(
                Ui.Text(unit.Name).FontSize(22f).Bold().Color("#F7D774"),
                Ui.Text($"{unit.Faction} | {unit.Troop} | ({unit.X},{unit.Y})").FontSize(13f).Color("#DDE7F2"),
                Ui.Text(unit.Skill).FontSize(15f).Bold().Color("#82D6A7"),
                Ui.Text($"HP {unit.Health} | Morale {unit.Morale} | Supplies {unit.Supplies} | {unit.Status}")
                    .FontSize(13f)
                    .Color("#FFFFFF"))
            .Id("three-kingdoms-selected")
            .Padding(12f)
            .Gap(6f)
            .Background("#1A2637")
            .Border(1f, RequireColor("#3D6A93"))
            .Radius(6f);
    }

    private static UiElementBuilder BuildRosterPanel(ThreeKingdomsTacticsSnapshot snapshot)
    {
        UiElementBuilder[] rows = snapshot.Units
            .Where(static unit => unit.Alive)
            .Take(12)
            .Select(unit => Ui.Text($"{(unit.Selected ? ">" : " ")} {unit.Name,-8} T{unit.TeamId} {unit.Troop,-12} HP {unit.Health,3}")
                .FontFamily("Consolas")
                .FontSize(12f)
                .Color(unit.TeamId == 1 ? "#A8D8FF" : "#FFB3A6"))
            .ToArray();

        return Ui.Column(
                Ui.Text("Fielded Units").FontSize(16f).Bold().Color("#F7D774"),
                Ui.Column(rows).Gap(2f))
            .Padding(12f)
            .Gap(8f)
            .Background("#172131")
            .Radius(6f);
    }

    private static UiElementBuilder BuildLogPanel(ThreeKingdomsTacticsSnapshot snapshot)
    {
        UiElementBuilder[] rows = snapshot.LogLines
            .Take(8)
            .Select(line => Ui.Text(line).FontSize(12f).Color("#DDE7F2"))
            .ToArray();

        return Ui.Column(
                Ui.Text("Battle Report").FontSize(16f).Bold().Color("#F7D774"),
                Ui.Column(rows).Gap(3f))
            .Padding(12f)
            .Gap(8f)
            .Background("#172131")
            .Radius(6f)
            .FlexGrow(1f);
    }

    private static UiElementBuilder BuildCommandBar(GameEngine engine, ThreeKingdomsTacticsRuntime runtime, ThreeKingdomsTacticsSnapshot snapshot)
    {
        return Ui.Row(
                Command("Next", () => runtime.SelectNext(engine)),
                Command("Move N", () => runtime.MoveSelected(engine, 0, -2)),
                Command("Move W", () => runtime.MoveSelected(engine, -2, 0)),
                Command("Move E", () => runtime.MoveSelected(engine, 2, 0)),
                Command("Move S", () => runtime.MoveSelected(engine, 0, 2)),
                Command("Attack", () => runtime.AttackNearest(engine)),
                Command(snapshot.SelectedSkill, () => runtime.CastSelectedSkill(engine), wide: true),
                Command("Troop", () => runtime.CycleTroopType(engine)),
                Command("End Turn", () => runtime.EndTurn(engine)))
            .Id("three-kingdoms-command-bar")
            .Gap(8f)
            .Padding(10f)
            .Background("#182231")
            .Radius(6f);
    }

    private static UiElementBuilder Command(string label, Action action, bool wide = false)
    {
        return Ui.Button(label, _ => action())
            .Width(wide ? 190f : 94f)
            .Height(44f)
            .Radius(6f)
            .Background(wide ? "#2E6F58" : "#2F5F93")
            .Color("#FFFFFF")
            .FontSize(13f);
    }

    private static UiColor RequireColor(string color)
    {
        if (!UiColor.TryParse(color, out UiColor parsed))
        {
            throw new InvalidOperationException($"Invalid color literal: {color}");
        }

        return parsed;
    }
}
