using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.UI.Runtime;

namespace CrowdSimulationUiMod.UI;

/// <summary>
/// HUD 视图态。文字行是绑定，控制器改写后只重量变了尺寸的叶子；
/// 开关、段数、配色类这些结构位变了才整页重组一次。
/// </summary>
internal sealed class CrowdHudState
{
    public const int NavLineCount = 8;
    public const int TelemetryLineCount = 16;
    public const int FpsLine = 1;
    public const int SimLine = 2;
    public const int AvoidLine = 3;
    public const int HeadingLine = 7;
    public const int FlipLine = 8;
    public const int SparkSeries = 5;

    public CrowdHudState(int maxPlayers, int history)
    {
        Swatches = new UiColor?[maxPlayers + 1];
        SparkText = new UiTextBinding[SparkSeries];
        for (int i = 0; i < SparkText.Length; i++) SparkText[i] = new UiTextBinding(Math.Max(1, history));
        TelemetryAlert = new UiTextBinding[TelemetryLineCount];
        for (int i = 0; i < TelemetryAlert.Length; i++) TelemetryAlert[i] = new UiTextBinding(CrowdHudFormat.AlertLabel.Length);
    }

    public CrowdHudKind Kind;
    public string Title = "";
    public readonly HudLine Tools = new();
    public readonly HudLine Keys = new();

    public bool ShowStatus;
    public readonly HudLine Status = new();
    public readonly HudLine Rebake = new();
    public readonly HudLine Replay = new();
    public bool ReplayWarn;
    public readonly HudLine Counters = new();
    public readonly HudLine CounterDetail = new();
    public readonly HudLine Validation = new();
    public string ValidationTone = "crowd-dim";
    public readonly HudLine Fuzz = new();
    public string FuzzTone = "crowd-dim";

    public bool ShowPlayer;
    public int PlayerCount;
    public readonly UiColor?[] Swatches;
    public readonly HudLine TeamLine = new();
    public readonly HudLine CardLine = new();
    public readonly HudLine ModeLine = new();
    public readonly HudLine ClickLine = new();
    public readonly HudLine DeployLine = new();

    public bool ShowInspect;
    public readonly HudLine Inspect = new();
    public readonly HudLine Belief = new();

    public bool ShowNav;
    public readonly HudLine[] NavLines = HudLine.Many(NavLineCount);

    public bool ShowTelemetry;
    public readonly HudLine[] TelemetryLines = HudLine.Many(TelemetryLineCount);
    public readonly bool[] TelemetryHot = new bool[TelemetryLineCount];
    /// <summary>
    /// 告警角标的文字，越线时是 <see cref="CrowdHudFormat.AlertLabel"/>，否则为空。
    /// 用绑定而不是给行加类：50k 下避让、航向读数贴着阈值来回跳，改类就得整页重组重算样式。
    /// </summary>
    public readonly UiTextBinding[] TelemetryAlert;
    /// <summary>曲线按时间先后排成一串块字符，一个样本一个字，高度按零基线占比分八档。</summary>
    public readonly UiTextBinding[] SparkText;
    public int SparkCount;

    public bool ShowFault;
    public readonly HudLine Fault = new();

    public static bool IsAlertable(int line) => line is FpsLine or SimLine or AvoidLine or HeadingLine or FlipLine;

    public static int SparkSeriesOf(int line) => line switch
    {
        FpsLine => 0,
        SimLine => 1,
        AvoidLine => 2,
        HeadingLine => 3,
        FlipLine => 4,
        _ => -1,
    };
}
