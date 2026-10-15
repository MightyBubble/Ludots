using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;

namespace CrowdSimulationUiMod.UI;

/// <summary>
/// HUD 结构，分区照 Web 沙盒：左栏（标题、玩家显示、调试视图）、右栏（遥测）、
/// 顶部居中（队伍条与按键提示）、右上指令卡、底部居中指令坞（会话状态与工具条）。
/// 这里只出节点与 id/class，位置、尺寸、配色全在 hud.css。
/// </summary>
internal static class CrowdHudView
{
    public const string RootId = "crowd-hud";
    public const string ChipSeparator = " · ";
    private static readonly string[] NavIds = Ids("crowd-nav-", CrowdHudState.NavLineCount);
    private static readonly string[] TelemetryIds = Ids("crowd-tel-", CrowdHudState.TelemetryLineCount);

    public static UiElementBuilder Build(ReactiveContext<CrowdHudState> context)
    {
        CrowdHudState s = context.State;
        var root = Box("div").Id(RootId);
        root.Child(LeftRail(s));
        root.Child(Center(s));
        root.Child(RightRail(s));
        return root;
    }

    private static UiElementBuilder LeftRail(CrowdHudState s)
    {
        var rail = Box("div").Id("crowd-left").Class("crowd-rail");
        rail.Child(Panel("crowd-header").Child(Ui.Text(s.Title).Id("crowd-title")));
        if (s.ShowPlayer)
        {
            var player = Panel("crowd-player");
            player.Child(Chips(s.ModeLine).Id("crowd-mode-line"));
            player.Child(Chips(s.ClickLine).Id("crowd-click-line"));
            player.Child(Chips(s.DeployLine).Id("crowd-deploy-line"));
            rail.Child(player);
        }

        if (s.ShowInspect || s.ShowNav)
        {
            var view = Panel("crowd-debugview");
            if (s.ShowInspect)
            {
                var inspect = Section("crowd-inspect");
                if (s.Inspect.Length > 0) inspect.Child(Chips(s.Inspect).Id("crowd-inspect-line"));
                if (s.Belief.Length > 0) inspect.Child(Chips(s.Belief).Id("crowd-belief-line"));
                view.Child(inspect);
            }

            if (s.ShowNav)
            {
                var nav = Section("crowd-nav");
                for (int i = 0; i < s.NavLines.Length; i++)
                {
                    nav.Child((i == 0 ? Ui.Text(s.NavLines[i]).Class("crowd-heading") : Chips(s.NavLines[i])).Id(NavIds[i]));
                }

                view.Child(nav);
            }

            rail.Child(view);
        }

        return rail;
    }

    private static UiElementBuilder Center(CrowdHudState s)
    {
        var center = Box("div").Id("crowd-center");
        var top = Box("div").Id("crowd-top");
        top.Child(Box("div").Class("crowd-top-side"));

        var middle = Box("div").Id("crowd-top-middle");
        if (s.ShowPlayer) middle.Child(PlayerBar(s));
        middle.Child(Panel("crowd-legend").Child(Chips(s.Keys).Id("crowd-keys")));
        if (s.ShowFault) middle.Child(Panel("crowd-fault").Child(Line(s.Fault).Id("crowd-fault-line")));
        top.Child(middle);

        var notices = Box("div").Class("crowd-top-side").Id("crowd-notices");
        if (s.ShowPlayer) notices.Child(Panel("crowd-order").Child(Line(s.CardLine).Id("crowd-card-line")));
        top.Child(notices);
        center.Child(top);

        var dock = Panel("crowd-dock");
        if (s.ShowStatus)
        {
            var status = Box("div").Id("crowd-status");
            status.Child(Line(s.Status).Id("crowd-status-line"));
            status.Child(Chips(s.Rebake).Id("crowd-rebake-line"));
            status.Child(Line(s.Replay).Id("crowd-replay-line").Class(s.ReplayWarn ? "crowd-warn" : "crowd-ok"));
            if (s.Counters.Length > 0) status.Child(Line(s.Counters).Id("crowd-counters-line").Class("crowd-dim"));
            if (s.CounterDetail.Length > 0) status.Child(Line(s.CounterDetail).Id("crowd-counter-detail-line").Class("crowd-dim"));
            status.Child(Line(s.Validation).Id("crowd-validation-line").Class(s.ValidationTone));
            status.Child(Line(s.Fuzz).Id("crowd-fuzz-line").Class(s.FuzzTone));
            dock.Child(status);
        }

        dock.Child(Box("div").Id("crowd-toolbar").Child(Line(s.Tools).Id("crowd-tools")));
        center.Child(dock);
        return center;
    }

    private static UiElementBuilder PlayerBar(CrowdHudState s)
    {
        var bar = Panel("crowd-playerbar");
        var swatches = Box("div").Id("crowd-team-swatches");
        for (int p = 1; p <= s.PlayerCount && p < s.Swatches.Length; p++)
        {
            if (s.Swatches[p] is { } color) swatches.Child(Box("div").Class("crowd-swatch").Background(color));
        }

        bar.Child(swatches);
        bar.Child(Line(s.TeamLine).Id("crowd-team-line"));
        return bar;
    }

    private static UiElementBuilder RightRail(CrowdHudState s)
    {
        var rail = Box("div").Id("crowd-right").Class("crowd-rail");
        if (!s.ShowTelemetry) return rail;

        var tel = Panel("crowd-telemetry");
        for (int i = 0; i < s.TelemetryLines.Length; i++)
        {
            var row = Box("div").Id(TelemetryIds[i]).Class("crowd-tel-row");
            if (s.TelemetryHot[i]) row.Class("hot");
            row.Child(i == 0 ? Ui.Text(s.TelemetryLines[i]).Class("crowd-heading") : Chips(s.TelemetryLines[i]));
            int series = CrowdHudState.SparkSeriesOf(i);
            if (series >= 0 && s.SparkCount >= 2) row.Child(Ui.Text(s.SparkText[series]).Class("crowd-spark"));
            tel.Child(row);
        }

        rail.Child(tel);
        return rail;
    }

    /// <summary>
    /// 长文案按 “ · ” 拆成不换行的小段，由 flex-wrap 折行。
    /// Skia 的文字换行逐前缀量宽，整句交给它折行每次重绘都是平方级开销。
    /// </summary>
    private static UiElementBuilder Chips(string text)
    {
        var row = Box("div").Class("crowd-chips");
        int start = 0;
        while (start <= text.Length)
        {
            int cut = text.IndexOf(ChipSeparator, start, StringComparison.Ordinal);
            int end = cut < 0 ? text.Length : cut;
            if (end > start) row.Child(Ui.Text(text.Substring(start, end - start)).Class("crowd-chip"));
            if (cut < 0) break;
            start = cut + ChipSeparator.Length;
        }

        return row;
    }

    private static UiElementBuilder Box(string tag) => new UiElementBuilder(UiNodeKind.Container, tag).PointerEvents(UiPointerEvents.None);

    private static UiElementBuilder Panel(string id) => Box("section").Id(id).Class("crowd-panel");

    private static UiElementBuilder Section(string id) => Box("div").Id(id).Class("crowd-section");

    private static UiElementBuilder Line(string text) => Ui.Text(text).Class("crowd-line");

    private static string[] Ids(string prefix, int count)
    {
        var ids = new string[count];
        for (int i = 0; i < count; i++) ids[i] = prefix + i;
        return ids;
    }
}
