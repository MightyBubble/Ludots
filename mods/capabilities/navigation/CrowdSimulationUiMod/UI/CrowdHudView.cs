using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;

namespace CrowdSimulationUiMod.UI;

/// <summary>HUD 结构：只出节点与 id/class，位置、尺寸、配色全在 hud.css。</summary>
internal static class CrowdHudView
{
    public const string RootId = "crowd-hud";
    private static readonly string[] NavIds = Ids("crowd-nav-", CrowdHudState.NavLineCount);
    private static readonly string[] TelemetryIds = Ids("crowd-tel-", CrowdHudState.TelemetryLineCount);

    public static UiElementBuilder Build(ReactiveContext<CrowdHudState> context, CrowdSparkCanvas[] sparks)
    {
        CrowdHudState s = context.State;
        var root = Box("div").Id(RootId);
        root.Child(Toolbar(s));
        if (s.ShowPlayer) root.Child(Player(s));
        if (s.ShowInspect || s.ShowNav || s.ShowTelemetry) root.Child(DebugColumn(s, sparks));
        root.Child(Bottom(s));
        return root;
    }

    private static UiElementBuilder Toolbar(CrowdHudState s)
    {
        var bar = Panel("crowd-toolbar");
        bar.Child(Ui.Text(s.Title).Id("crowd-title"));
        bar.Child(Line(s.Tools).Id("crowd-tools"));
        return bar;
    }

    private static UiElementBuilder Player(CrowdHudState s)
    {
        var panel = Panel("crowd-player");
        var swatches = Box("div").Id("crowd-team-swatches");
        for (int p = 1; p <= s.PlayerCount && p < s.Swatches.Length; p++)
        {
            if (s.Swatches[p] is { } color) swatches.Child(Box("div").Class("crowd-swatch").Background(color));
        }

        panel.Child(swatches);
        panel.Child(Line(s.TeamLine).Id("crowd-team-line"));
        panel.Child(Line(s.CardLine).Id("crowd-card-line"));
        panel.Child(Line(s.ModeLine).Id("crowd-mode-line"));
        panel.Child(Line(s.ClickLine).Id("crowd-click-line"));
        panel.Child(Line(s.DeployLine).Id("crowd-deploy-line"));
        return panel;
    }

    private static UiElementBuilder DebugColumn(CrowdHudState s, CrowdSparkCanvas[] sparks)
    {
        var column = Box("div").Id("crowd-debug-column");
        if (s.ShowInspect)
        {
            var inspect = Panel("crowd-inspect");
            if (s.Inspect.Length > 0) inspect.Child(Line(s.Inspect).Id("crowd-inspect-line"));
            if (s.Belief.Length > 0) inspect.Child(Line(s.Belief).Id("crowd-belief-line"));
            column.Child(inspect);
        }

        if (s.ShowNav)
        {
            var nav = Panel("crowd-nav");
            for (int i = 0; i < s.NavLines.Length; i++)
            {
                nav.Child(Line(s.NavLines[i]).Id(NavIds[i]).Class(i == 0 ? "crowd-heading" : "crowd-line"));
            }

            column.Child(nav);
        }

        if (s.ShowTelemetry)
        {
            var tel = Panel("crowd-telemetry");
            for (int i = 0; i < s.TelemetryLines.Length; i++)
            {
                var row = Box("div").Id(TelemetryIds[i]).Class("crowd-tel-row");
                if (s.TelemetryHot[i]) row.Class("hot");
                row.Child(Line(s.TelemetryLines[i]).Class(i == 0 ? "crowd-heading" : "crowd-line"));
                int series = CrowdHudState.SparkSeriesOf(i);
                if (series >= 0 && s.SparkCount >= 2) row.Child(Ui.Canvas(sparks[series].Content).Class("crowd-spark").PointerEvents(UiPointerEvents.None));
                tel.Child(row);
            }

            column.Child(tel);
        }

        return column;
    }

    private static UiElementBuilder Bottom(CrowdHudState s)
    {
        var bottom = Box("div").Id("crowd-bottom");
        if (s.ShowFault) bottom.Child(Panel("crowd-fault").Child(Line(s.Fault).Id("crowd-fault-line")));
        if (s.ShowStatus)
        {
            var status = Panel("crowd-status");
            status.Child(Line(s.Status).Id("crowd-status-line"));
            status.Child(Line(s.Rebake).Id("crowd-rebake-line"));
            status.Child(Line(s.Replay).Id("crowd-replay-line").Class(s.ReplayWarn ? "crowd-warn" : "crowd-ok"));
            if (s.Counters.Length > 0) status.Child(Line(s.Counters).Id("crowd-counters-line").Class("crowd-dim"));
            if (s.CounterDetail.Length > 0) status.Child(Line(s.CounterDetail).Id("crowd-counter-detail-line").Class("crowd-dim"));
            status.Child(Line(s.Validation).Id("crowd-validation-line").Class(s.ValidationTone));
            status.Child(Line(s.Fuzz).Id("crowd-fuzz-line").Class(s.FuzzTone));
            bottom.Child(status);
        }

        bottom.Child(Panel("crowd-legend").Child(Ui.Text(s.Keys).Id("crowd-keys")));
        return bottom;
    }

    private static UiElementBuilder Box(string tag) => new UiElementBuilder(UiNodeKind.Container, tag).PointerEvents(UiPointerEvents.None);

    private static UiElementBuilder Panel(string id) => Box("section").Id(id).Class("crowd-panel");

    private static UiElementBuilder Line(string text) => Ui.Text(text).Class("crowd-line");

    private static string[] Ids(string prefix, int count)
    {
        var ids = new string[count];
        for (int i = 0; i < count; i++) ids[i] = prefix + i;
        return ids;
    }
}
