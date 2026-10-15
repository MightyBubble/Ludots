using Ludots.Core.CrowdSimulation.Presentation;

namespace CrowdSimulationUiMod.UI;

/// <summary>HUD 视图态。控制器只在采样且读数变化时改写，随后整页重组一次。</summary>
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

    public CrowdHudState(int history, int maxPlayers)
    {
        Spark = new float[SparkSeries][];
        for (int i = 0; i < SparkSeries; i++) Spark[i] = new float[history];
        Swatches = new string?[maxPlayers + 1];
    }

    public CrowdHudKind Kind;
    public string Title = "";
    public string Tools = "";
    public string Keys = "";

    public bool ShowStatus;
    public string Status = "";
    public string Rebake = "";
    public string Replay = "";
    public bool ReplayWarn;
    public string Counters = "";
    public string CounterDetail = "";
    public string Validation = "";
    public string ValidationTone = "crowd-dim";
    public string Fuzz = "";
    public string FuzzTone = "crowd-dim";

    public bool ShowPlayer;
    public int PlayerCount;
    public readonly string?[] Swatches;
    public string TeamLine = "";
    public string CardLine = "";
    public string ModeLine = "";
    public string ClickLine = "";
    public string DeployLine = "";

    public bool ShowInspect;
    public string Inspect = "";
    public string Belief = "";

    public bool ShowNav;
    public readonly string[] NavLines = Filled(NavLineCount);

    public bool ShowTelemetry;
    public readonly string[] TelemetryLines = Filled(TelemetryLineCount);
    public readonly bool[] TelemetryHot = new bool[TelemetryLineCount];
    /// <summary>曲线按时间先后排列，已归一到 0..1；只有前 <see cref="SparkCount"/> 项有效。</summary>
    public readonly float[][] Spark;
    public int SparkCount;

    public bool ShowFault;
    public string Fault = "";

    public static int SparkSeriesOf(int line) => line switch
    {
        FpsLine => 0,
        SimLine => 1,
        AvoidLine => 2,
        HeadingLine => 3,
        FlipLine => 4,
        _ => -1,
    };

    private static string[] Filled(int n)
    {
        var lines = new string[n];
        Array.Fill(lines, "");
        return lines;
    }
}
