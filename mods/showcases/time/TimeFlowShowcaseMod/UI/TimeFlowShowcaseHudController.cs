using System;
using System.Collections.Generic;
using System.Linq;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;

namespace TimeFlowShowcaseMod.UI;

internal sealed class TimeFlowShowcaseHudController
{
    private ReactivePage<HudState>? _page;

    public void MountOrRefresh(UIRoot root, GameEngine engine, TimeFlowShowcaseSnapshot snapshot)
    {
        HudState nextState = BuildState(snapshot, root);
        if (_page == null)
        {
            var textMeasurer = engine.GetService(CoreServiceKeys.UiTextMeasurer) as IUiTextMeasurer;
            var imageSizeProvider = engine.GetService(CoreServiceKeys.UiImageSizeProvider) as IUiImageSizeProvider;
            if (textMeasurer == null || imageSizeProvider == null)
            {
                return;
            }

            _page = new ReactivePage<HudState>(textMeasurer, imageSizeProvider, nextState, BuildRoot);
        }
        else
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

    private UiElementBuilder BuildRoot(ReactiveContext<HudState> context)
    {
        HudState state = context.State;
        var children = new List<UiElementBuilder>
        {
            Ui.Text(" ").WidthPercent(100f).HeightPercent(100f).Absolute(0f, 0f).Background("#02060A14").ZIndex(34),
            BuildGlassCard(
                    Ui.Text("TIMEFLOW MINI-GAME").FontSize(11f).Bold().Color("#7DD3FC"),
                    Ui.Text(state.Title).FontSize(28f).Bold().Color("#F6D77C"),
                    Ui.Text(state.Subtitle).FontSize(11f).Color("#D6E0EA").WhiteSpace(UiWhiteSpace.Normal))
                .Width(360f).Padding(16f, 14f).Gap(8f).Absolute(24f, 22f),
            BuildGlassCard(
                    BuildBadge(state.StatusBadge, BadgeColor(state.StatusBadge)),
                    Ui.Text(state.Objective).FontSize(22f).Bold().Color("#F8FAFC").WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Row(
                            BuildBadge(state.Goal, "#7DD3FC"),
                            BuildBadge(state.Beat, "#86EFAC"))
                        .Gap(8f).Wrap())
                .Width(480f).Padding(16f, 14f).Gap(8f).Absolute((state.ViewportWidth - 480f) * 0.5f, 22f),
            BuildGlassCard(
                    Ui.Row(
                            Ui.Text("TIME FLOW").FontSize(11f).Bold().Color("#8FB5D3"),
                            BuildBadge(state.TimeBadge, BadgeColor(state.TimeBadge)))
                        .Justify(UiJustifyContent.SpaceBetween).Align(UiAlignItems.Center),
                    Ui.Text(state.TimeSummary).FontSize(18f).Bold().Color("#F8FAFC").WhiteSpace(UiWhiteSpace.Normal),
                    BuildBadge(state.TimeDetail, "#7DD3FC"))
                .Width(280f).Padding(14f, 12f).Gap(8f).Absolute(state.ViewportWidth - 304f, 22f),
            BuildGlassCard(
                    Ui.Text(state.MechanicLabel).FontSize(11f).Bold().Color("#8FB5D3"),
                    Ui.Text(state.MechanicValue).FontSize(20f).Bold().Color("#F8FAFC"),
                    BuildBar(248f, state.MechanicProgress, "#F6D77C", "#152230", 10f),
                    Ui.Text(state.MechanicFooter).FontSize(10f).Color("#B3C0CE").WhiteSpace(UiWhiteSpace.Normal))
                .Width(280f).Padding(14f, 12f).Gap(8f).Absolute(state.ViewportWidth - 304f, 156f),
            Ui.Panel(
                    Ui.Text(" ").Width(state.BattlefieldWidth).Height(state.BattlefieldHeight).Background("#091520E6").Border(1f, Color("#35536B")).Radius(24f),
                    Ui.Text(" ").Width(2f).Height(state.BattlefieldHeight - 52f).Absolute((state.BattlefieldWidth * 0.5f) - 1f, 34f).Background("#223445").Radius(2f),
                    Ui.Text("ALLIED SIDE").FontSize(11f).Bold().Color("#78D9FF").Absolute(24f, 18f),
                    Ui.Text("HOSTILE SIDE").FontSize(11f).Bold().Color("#FF8F79").Absolute(state.BattlefieldWidth - 126f, 18f),
                    Ui.Text(state.StatusLine).FontSize(14f).Color("#E7EDF5").Absolute(24f, state.BattlefieldHeight - 28f))
                .Width(state.BattlefieldWidth).Height(state.BattlefieldHeight).Absolute(state.BattlefieldX, state.BattlefieldY).ZIndex(38),
            BuildGlassCard(
                    Ui.Row(
                            Ui.Text("LIVE FEED").FontSize(11f).Bold().Color("#8FB5D3"),
                            BuildBadge("NOW", "#F6D77C"))
                        .Justify(UiJustifyContent.SpaceBetween).Align(UiAlignItems.Center),
                    Ui.Text(state.EventLine).FontSize(13f).Color("#F8FAFC").WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text(state.CastLine).FontSize(11f).Color("#9FB1C5").WhiteSpace(UiWhiteSpace.Normal))
                .Width(460f).Padding(12f, 10f).Gap(6f).Absolute((state.ViewportWidth - 460f) * 0.5f, state.BattlefieldY + state.BattlefieldHeight + 14f),
            BuildTeamCard("ALLIES", state.Allies, "#78D9FF", "#0A1B27", 24f, state.ViewportHeight - 230f),
            BuildTeamCard("ENEMIES", state.Enemies, "#FF8F79", "#25110F", state.ViewportWidth - 344f, state.ViewportHeight - 230f),
            BuildGlassCard(
                    Ui.Text("NEXT INPUT").FontSize(11f).Bold().Color("#8FB5D3"),
                    Ui.Text(state.ActionFooter).FontSize(18f).Bold().Color("#F8FAFC").WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Row(state.ActionPrompts.Select(BuildPromptChip).ToArray())
                        .Gap(10f).Wrap(),
                    Ui.Text(state.StatusLine).FontSize(11f).Color("#B3C0CE").WhiteSpace(UiWhiteSpace.Normal))
                .Width(544f).Padding(14f, 12f).Gap(8f).Absolute((state.ViewportWidth - 544f) * 0.5f, state.ViewportHeight - 170f)
        };

        children.AddRange(state.Tokens.Select(BuildToken));
        return Ui.Panel(children.ToArray()).WidthPercent(100f).HeightPercent(100f).Absolute(0f, 0f).ZIndex(40);
    }

    private static UiElementBuilder BuildTeamCard(string title, IReadOnlyList<ActorState> actors, string accent, string background, float left, float top)
    {
        var rows = new List<UiElementBuilder>
        {
            Ui.Row(
                    Ui.Text(title).FontSize(12f).Bold().Color(accent),
                    Ui.Text($"{actors.Count} units").FontSize(11f).Color("#A8B6C4"))
                .Justify(UiJustifyContent.SpaceBetween).Align(UiAlignItems.Center)
        };

        rows.AddRange(actors.Select(actor => Ui.Card(
                Ui.Row(
                        Ui.Column(
                                Ui.Text(actor.Name).FontSize(14f).Bold().Color("#F8FAFC"),
                                Ui.Text(actor.Status).FontSize(11f).Color(actor.Highlighted ? "#F6D77C" : "#9FB1C5")).Gap(4f),
                        Ui.Text(actor.MetricText).FontSize(11f).Color(accent))
                    .Justify(UiJustifyContent.SpaceBetween).Align(UiAlignItems.Center),
                BuildMetricLine("HP", actor.HealthText, actor.HealthProgress, actor.HealthColor),
                BuildMetricLine(actor.MetricLabel, actor.MetricText, actor.MetricProgress, actor.MetricColor))
            .Gap(6f).Padding(10f).Radius(16f).Background(actor.Highlighted ? "#132535" : "#0D1822").Border(1f, actor.Highlighted ? Color("#F6D77C") : Color("#2B4559"))));

        return Ui.Card(rows.ToArray())
            .Width(320f).Height(206f).Padding(14f, 12f).Gap(10f).Radius(22f)
            .Background(background).Border(1f, Color("#35536B")).BoxShadow(0f, 10f, 24f, Color("#70000000"))
            .Absolute(left, top).ZIndex(40);
    }

    private static UiElementBuilder BuildMetricLine(string label, string value, float progress, string color)
    {
        return Ui.Column(
                Ui.Row(
                        Ui.Text(label).FontSize(10f).Bold().Color("#8FA6BD"),
                        Ui.Text(value).FontSize(10f).Color("#E7EDF5"))
                    .Justify(UiJustifyContent.SpaceBetween),
                BuildBar(272f, progress, color, "#162633", 8f))
            .Gap(4f);
    }

    private static UiElementBuilder BuildToken(TokenState token)
    {
        return Ui.Card(
                Ui.Row(
                        Ui.Text(token.Name).FontSize(12f).Bold().Color("#F8FAFC"),
                        Ui.Text(token.Team == 1 ? "ALLY" : "ENEMY").FontSize(10f).Bold().Color(token.Highlighted ? "#F6D77C" : "#8FB5D3"))
                    .Justify(UiJustifyContent.SpaceBetween).Align(UiAlignItems.Center),
                BuildBar(104f, token.HealthProgress, token.Team == 1 ? "#63C9FF" : "#FF866F", "#183041", 7f),
                Ui.Text(token.Status).FontSize(10f).Color("#B3C0CE").WhiteSpace(UiWhiteSpace.Normal))
            .Width(124f).Padding(10f, 8f).Gap(6f).Radius(18f)
            .Background(token.Team == 1 ? "#0E2232F2" : "#2D1816F2")
            .Border(1f, token.Highlighted ? Color("#F6D77C") : Color(token.Team == 1 ? "#63C9FF" : "#FF866F"))
            .BoxShadow(0f, 8f, 18f, Color("#70000000"))
            .Absolute(token.Left, token.Top).ZIndex(token.Highlighted ? 44 : 42);
    }

    private static UiElementBuilder BuildGlassCard(params UiElementBuilder[] children)
    {
        return Ui.Card(children).Radius(22f).Background("#08131DDD").Border(1f, Color("#35536B")).BoxShadow(0f, 12f, 28f, Color("#70000000")).BackdropBlur(4f);
    }

    private static UiElementBuilder BuildPromptChip(ActionPromptState prompt)
    {
        string background = prompt.Active ? "#F6D77C" : prompt.Enabled ? "#132232" : "#101820";
        string textColor = prompt.Active ? "#07111C" : prompt.Enabled ? "#F8FAFC" : "#6F7C8A";
        UiColor border = prompt.Active ? Color("#F6D77C") : prompt.Enabled ? Color("#35536B") : Color("#24313D");
        return Ui.Card(Ui.Text(prompt.Label).FontSize(11f).Bold().Color(textColor))
            .Padding(12f, 10f)
            .Radius(999f)
            .Background(background)
            .Border(1f, border);
    }

    private static UiElementBuilder BuildBadge(string text, string background)
    {
        return Ui.Card(Ui.Text(text).FontSize(10f).Bold().Color("#07111C")).Padding(10f, 6f).Radius(999f).Background(background);
    }

    private static UiElementBuilder BuildBar(float width, float value, string fill, string empty, float height)
    {
        float clamped = Math.Clamp(value, 0f, 1f);
        float fillWidth = width * clamped;
        float emptyWidth = width - fillWidth;
        if (fillWidth <= 0.5f)
        {
            return Ui.Row(Ui.Text(" ").Width(width).Height(height).Background(empty).Radius(height * 0.5f)).Gap(0f);
        }

        if (emptyWidth <= 0.5f)
        {
            return Ui.Row(Ui.Text(" ").Width(width).Height(height).Background(fill).Radius(height * 0.5f)).Gap(0f);
        }

        return Ui.Row(
                Ui.Text(" ").Width(fillWidth).Height(height).Background(fill).Radius(height * 0.5f),
                Ui.Text(" ").Width(emptyWidth).Height(height).Background(empty).Radius(height * 0.5f))
            .Gap(0f).Width(width).Height(height);
    }

    private static HudState BuildState(TimeFlowShowcaseSnapshot snapshot, UIRoot root)
    {
        float viewportWidth = root.Width > 0f ? root.Width : 1280f;
        float viewportHeight = root.Height > 0f ? root.Height : 720f;
        float battlefieldWidth = MathF.Min(700f, MathF.Max(620f, viewportWidth - 580f));
        float battlefieldHeight = MathF.Min(332f, MathF.Max(300f, viewportHeight - 362f));
        float battlefieldX = (viewportWidth - battlefieldWidth) * 0.5f;
        float battlefieldY = 142f;
        TimeFlowMiniGameDescriptor descriptor = TimeFlowShowcaseMiniGames.Describe(snapshot.ScenarioKind);
        CardState mechanic = BuildMechanicState(snapshot);

        return new HudState(
            viewportWidth,
            viewportHeight,
            battlefieldX,
            battlefieldY,
            battlefieldWidth,
            battlefieldHeight,
            descriptor.MenuTitle,
            descriptor.Pitch,
            TimeFlowShowcaseMiniGames.DescribePrimaryPrompt(snapshot),
            TimeFlowShowcaseMiniGames.DescribeFocusChip(snapshot),
            TimeFlowShowcaseMiniGames.DescribeBeatChip(snapshot),
            TimeFlowShowcaseMiniGames.DescribeTimeBadge(snapshot),
            snapshot.StatusLine,
            TimeFlowShowcaseMiniGames.DescribeTimeBadge(snapshot),
            BuildTimeSummary(snapshot),
            TimeFlowShowcaseMiniGames.DescribeTimeChip(snapshot),
            mechanic.Label,
            mechanic.Value,
            mechanic.Progress,
            mechanic.Footer,
            snapshot.RecentEvents.Count == 0 ? descriptor.Success : snapshot.RecentEvents[^1],
            TimeFlowShowcaseMiniGames.DescribeCast(snapshot),
            TimeFlowShowcaseMiniGames.DescribeActionPrompts(snapshot).Select(prompt => new ActionPromptState(prompt.Label, prompt.Active, prompt.Enabled)).ToArray(),
            TimeFlowShowcaseMiniGames.DescribePrimaryAction(snapshot),
            snapshot.Actors.Where(actor => actor.Team == 1).Select(actor => BuildActorState(snapshot, actor)).ToArray(),
            snapshot.Actors.Where(actor => actor.Team != 1).Select(actor => BuildActorState(snapshot, actor)).ToArray(),
            snapshot.Actors.Select(actor => BuildTokenState(snapshot, actor, battlefieldX, battlefieldY, battlefieldWidth, battlefieldHeight)).ToArray());
    }

    private static ActorState BuildActorState(TimeFlowShowcaseSnapshot snapshot, TimeFlowShowcaseActorSnapshot actor)
    {
        float baseHealth = BaseHealth(snapshot.ScenarioKind, actor.Name, actor.Team);
        MetricState metric = BuildMetric(snapshot, actor);
        return new ActorState(
            actor.Name,
            BuildActorStatus(snapshot, actor),
            Highlighted(snapshot, actor),
            $"{actor.Health:0}/{baseHealth:0}",
            Math.Clamp(actor.Health / baseHealth, 0f, 1f),
            actor.Team == 1 ? "#63C9FF" : "#FF866F",
            metric.Label,
            metric.Text,
            metric.Progress,
            metric.Color);
    }

    private static TokenState BuildTokenState(TimeFlowShowcaseSnapshot snapshot, TimeFlowShowcaseActorSnapshot actor, float fieldX, float fieldY, float fieldWidth, float fieldHeight)
    {
        float left = fieldX + 20f + (Normalize(actor.X, 180f, 1120f) * (fieldWidth - 164f));
        float top = fieldY + 46f + (Normalize(actor.Y, 160f, 460f) * (fieldHeight - 112f));
        return new TokenState(actor.Name, BuildActorStatus(snapshot, actor), actor.Team, Highlighted(snapshot, actor), left, top, Math.Clamp(actor.Health / BaseHealth(snapshot.ScenarioKind, actor.Name, actor.Team), 0f, 1f));
    }

    private static MetricState BuildMetric(TimeFlowShowcaseSnapshot snapshot, TimeFlowShowcaseActorSnapshot actor)
    {
        return snapshot.ScenarioKind switch
        {
            TimeFlowScenarioKind.AtbWait => new MetricState("ATB", $"{actor.Charge:0}%", Math.Clamp(actor.Charge / 100f, 0f, 1f), "#F6D77C"),
            TimeFlowScenarioKind.DotaManualUlt => new MetricState("ENERGY", $"{actor.Energy:0}%", Math.Clamp(actor.Energy / 100f, 0f, 1f), "#7DD3FC"),
            TimeFlowScenarioKind.BreakFever when actor.Team == 1 => new MetricState("BURST", $"{actor.Charge:0}%", Math.Clamp(actor.Charge / 100f, 0f, 1f), "#F59E0B"),
            TimeFlowScenarioKind.BreakFever => new MetricState("BREAK", $"{snapshot.BreakGauge:0}%", Math.Clamp(snapshot.BreakGauge / 100f, 0f, 1f), "#F97316"),
            TimeFlowScenarioKind.SentinelCommandPause => new MetricState("ETA", $"{actor.WaitTicks}", 1f - Math.Clamp(actor.WaitTicks / 150f, 0f, 1f), "#A78BFA"),
            TimeFlowScenarioKind.Ck3Macro => new MetricState("ORDERS", $"{actor.OrdersQueued}", Math.Clamp(actor.OrdersQueued / 3f, 0f, 1f), "#34D399"),
            TimeFlowScenarioKind.BadNorthActivePause => new MetricState("ORDERS", $"{actor.OrdersQueued}", Math.Clamp(actor.OrdersQueued / 2f, 0f, 1f), "#34D399"),
            _ => new MetricState("STATE", $"{actor.OrdersQueued}", Math.Clamp(actor.OrdersQueued / 3f, 0f, 1f), "#34D399")
        };
    }

    private static CardState BuildMechanicState(TimeFlowShowcaseSnapshot snapshot)
    {
        TimeFlowShowcaseActorSnapshot? captain = snapshot.Actors.FirstOrDefault(actor => actor.Name == "Captain");
        TimeFlowShowcaseActorSnapshot? focused = snapshot.Actors.FirstOrDefault(actor => string.Equals(actor.Name, snapshot.SelectedActor, StringComparison.OrdinalIgnoreCase))
            ?? snapshot.Actors.FirstOrDefault(actor => actor.Team == 1);

        return snapshot.ScenarioKind switch
        {
            TimeFlowScenarioKind.AtbWait => new CardState("ACTION GAUGE", $"{focused?.Name ?? "Knight"} {focused?.Charge ?? 0f:0}%", Math.Clamp((focused?.Charge ?? 0f) / 100f, 0f, 1f), "Freeze on ally-ready."),
            TimeFlowScenarioKind.DotaManualUlt when snapshot.Phase == "Dota.BulletTime" => new CardState("SLOW WINDOW", $"{snapshot.TimeFlow.SimulationScalePermille / 10f:0}% world speed", Math.Clamp(snapshot.TimeFlow.SimulationScalePermille / 1000f, 0f, 1f), "Short ult reward window."),
            TimeFlowScenarioKind.DotaManualUlt => new CardState("ULTIMATE ENERGY", $"Captain {captain?.Energy ?? 0f:0}%", Math.Clamp((captain?.Energy ?? 0f) / 100f, 0f, 1f), "Freeze at 100% ult."),
            TimeFlowScenarioKind.BreakFever when snapshot.Phase == "Break.Fever" => new CardState("FEVER MODE", $"{snapshot.TimeFlow.SimulationScalePermille / 10f:0}% world / ally overclock", 1f - Math.Clamp(snapshot.TimeFlow.SimulationScalePermille / 1000f, 0f, 1f), "Slow world, fast allies."),
            TimeFlowScenarioKind.BreakFever => new CardState("BREAK GAUGE", $"{snapshot.BreakGauge:0}%", Math.Clamp(snapshot.BreakGauge / 100f, 0f, 1f), "Fill to open fever."),
            TimeFlowScenarioKind.SentinelCommandPause => new CardState("READY CLOCK", focused != null && focused.WaitTicks == 0 ? $"{focused.Name} ready" : $"Next ETA {snapshot.Actors.Where(actor => actor.Team == 1).Min(actor => actor.WaitTicks)}", focused != null && focused.WaitTicks == 0 ? 1f : 1f - Math.Clamp(snapshot.Actors.Where(actor => actor.Team == 1).Min(actor => actor.WaitTicks) / 150f, 0f, 1f), "Zero ETA opens pause."),
            TimeFlowScenarioKind.Ck3Macro => new CardState("SPEED LADDER", snapshot.Phase.Replace("CK3.", string.Empty, StringComparison.Ordinal), MacroProgress(snapshot.Phase), "Pause, step up, stop on event."),
            TimeFlowScenarioKind.BadNorthActivePause => new CardState("COMMAND CYCLE", snapshot.Phase.Replace("BadNorth.", string.Empty, StringComparison.Ordinal), BadNorthProgress(snapshot.Phase), "Pause, assign, resume, re-pause."),
            _ => new CardState("TIME SHIFT", snapshot.TimeFlow.ActiveProfileId, 0f, snapshot.StatusLine)
        };
    }

    private static string BuildActorStatus(TimeFlowShowcaseSnapshot snapshot, TimeFlowShowcaseActorSnapshot actor)
    {
        return snapshot.ScenarioKind switch
        {
            TimeFlowScenarioKind.AtbWait when actor.Charge >= 100f => "Ready to act",
            TimeFlowScenarioKind.DotaManualUlt when actor.Name == "Captain" && actor.Energy >= 100f => "Ultimate ready",
            TimeFlowScenarioKind.BreakFever when snapshot.Phase == "Break.Fever" && actor.Team == 1 => "Overclocked",
            TimeFlowScenarioKind.SentinelCommandPause when actor.WaitTicks == 0 => "Ready",
            _ when actor.OrdersQueued > 0 => $"{actor.OrdersQueued} queued",
            _ => "Holding"
        };
    }

    private static string BuildTimeSummary(TimeFlowShowcaseSnapshot snapshot)
    {
        if (snapshot.TimeFlow.SimulationScalePermille <= 0)
        {
            return "World fully stopped.";
        }

        if (snapshot.ScenarioKind == TimeFlowScenarioKind.Ck3Macro)
        {
            return snapshot.Phase switch
            {
                "CK3.Pause" => "Realm planning pause.",
                "CK3.EventPause" => "Event card pause.",
                "CK3.Speed1" => "Campaign running at 1x.",
                "CK3.Speed2" => "Campaign running at 2x.",
                "CK3.Speed3" => "Campaign running at 3x.",
                "CK3.Speed4" => "Campaign running at 4x.",
                "CK3.Complete" => "Macro ladder complete.",
                _ => "Realm clock active."
            };
        }

        if (snapshot.TimeFlow.SimulationScalePermille < 1000)
        {
            return $"World at {snapshot.TimeFlow.SimulationScalePermille / 10f:0}% speed.";
        }

        return "Baseline realtime active.";
    }

    private static bool Highlighted(TimeFlowShowcaseSnapshot snapshot, TimeFlowShowcaseActorSnapshot actor)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.SelectedActor) && string.Equals(snapshot.SelectedActor, actor.Name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return snapshot.ScenarioKind switch
        {
            TimeFlowScenarioKind.DotaManualUlt => string.Equals(actor.Name, "Captain", StringComparison.OrdinalIgnoreCase),
            TimeFlowScenarioKind.BreakFever when snapshot.Phase == "Break.Fever" => actor.Team == 1,
            _ => false
        };
    }

    private static float BaseHealth(TimeFlowScenarioKind kind, string actorName, int team)
    {
        return (kind, actorName, team) switch
        {
            (TimeFlowScenarioKind.AtbWait, "Knight", 1) => 100f,
            (TimeFlowScenarioKind.AtbWait, "Mage", 1) => 85f,
            (TimeFlowScenarioKind.AtbWait, "Goblin", 2) => 92f,
            (TimeFlowScenarioKind.AtbWait, "Ogre", 2) => 128f,
            (TimeFlowScenarioKind.DotaManualUlt, "Captain", 1) => 110f,
            (TimeFlowScenarioKind.DotaManualUlt, "Brute", 2) => 140f,
            (TimeFlowScenarioKind.BreakFever, "Striker", 1) => 120f,
            (TimeFlowScenarioKind.BreakFever, "Support", 1) => 95f,
            (TimeFlowScenarioKind.BreakFever, "Guardian", 2) => 180f,
            (TimeFlowScenarioKind.SentinelCommandPause, "Aegis", 1) => 125f,
            (TimeFlowScenarioKind.SentinelCommandPause, "Gunner", 1) => 110f,
            (TimeFlowScenarioKind.SentinelCommandPause, "Drone Swarm", 2) => 160f,
            (TimeFlowScenarioKind.Ck3Macro, "North Army", 1) => 100f,
            (TimeFlowScenarioKind.Ck3Macro, "South Army", 1) => 100f,
            (TimeFlowScenarioKind.Ck3Macro, "Border Raid", 2) => 88f,
            (TimeFlowScenarioKind.BadNorthActivePause, "Pikes", 1) => 100f,
            (TimeFlowScenarioKind.BadNorthActivePause, "Archers", 1) => 100f,
            (TimeFlowScenarioKind.BadNorthActivePause, "Raiders", 2) => 92f,
            (TimeFlowScenarioKind.BadNorthActivePause, "Flankers", 2) => 92f,
            _ => 100f
        };
    }

    private static float Normalize(float value, float min, float max) => max - min <= 0.001f ? 0.5f : Math.Clamp((value - min) / (max - min), 0f, 1f);
    private static float MacroProgress(string phase) => phase switch { "CK3.Pause" => 0.12f, "CK3.Speed1" => 0.26f, "CK3.Speed2" => 0.44f, "CK3.Speed3" => 0.62f, "CK3.EventPause" => 0.76f, "CK3.Speed4" => 0.92f, "CK3.Complete" => 1f, _ => 0f };
    private static float BadNorthProgress(string phase) => phase switch { "BadNorth.ActivePause" => 0.16f, "BadNorth.Realtime" => 0.40f, "BadNorth.RevectorPause" => 0.68f, "BadNorth.Finish" => 0.88f, "BadNorth.Complete" => 1f, _ => 0f };

    private static string BadgeColor(string badge)
    {
        if (badge.Contains("PAUSED", StringComparison.OrdinalIgnoreCase)) return "#FFD166";
        if (badge.Contains("BULLET", StringComparison.OrdinalIgnoreCase) || badge.Contains("SLOW", StringComparison.OrdinalIgnoreCase)) return "#7DD3FC";
        if (badge.Contains("FEVER", StringComparison.OrdinalIgnoreCase)) return "#F97316";
        if (badge.Contains("COMPLETE", StringComparison.OrdinalIgnoreCase)) return "#86EFAC";
        return "#86EFAC";
    }

    private static UiColor Color(string hex)
    {
        return UiColor.TryParse(hex, out UiColor color)
            ? color
            : UiColor.White;
    }

    private sealed record HudState(float ViewportWidth, float ViewportHeight, float BattlefieldX, float BattlefieldY, float BattlefieldWidth, float BattlefieldHeight, string Title, string Subtitle, string Objective, string Goal, string Beat, string StatusBadge, string StatusLine, string TimeBadge, string TimeSummary, string TimeDetail, string MechanicLabel, string MechanicValue, float MechanicProgress, string MechanicFooter, string EventLine, string CastLine, IReadOnlyList<ActionPromptState> ActionPrompts, string ActionFooter, IReadOnlyList<ActorState> Allies, IReadOnlyList<ActorState> Enemies, IReadOnlyList<TokenState> Tokens);
    private sealed record ActionPromptState(string Label, bool Active, bool Enabled);
    private sealed record ActorState(string Name, string Status, bool Highlighted, string HealthText, float HealthProgress, string HealthColor, string MetricLabel, string MetricText, float MetricProgress, string MetricColor);
    private sealed record TokenState(string Name, string Status, int Team, bool Highlighted, float Left, float Top, float HealthProgress);
    private sealed record MetricState(string Label, string Text, float Progress, string Color);
    private sealed record CardState(string Label, string Value, float Progress, string Footer);
}
