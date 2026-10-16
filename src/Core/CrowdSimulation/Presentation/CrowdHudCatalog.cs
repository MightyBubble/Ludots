using Ludots.Core.CrowdSimulation.Units;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// HUD 画选项用的名单与取值范围：兵种、移动层、半径级、阵型、放置面板格，以及部署数量 / 点击数量 / 避障频率的范围。
/// 跟着会话建一次；会话换了才换新的实例，HUD 按引用比较决定要不要重建选项。
/// </summary>
public sealed class CrowdHudCatalog
{
    internal CrowdHudCatalog(CrowdSimSession session, IReadOnlyList<S4DeployDemoRuntime.PaletteChoice> palette, CrowdPlayerViewConfig? playerView, CrowdDebugRates? rates)
    {
        var config = session.Config;
        UnitTypes = config.UnitTypes.Select(t => t.Id).ToArray();
        AgentTypes = config.AgentTypes.Select(t => t.Id).ToArray();
        RadiusCm = config.RadiusClasses.ToArray();
        Formations = config.Formations.Select(f => f.Label).ToArray();
        Templates = palette.Select(p => p.Label).ToArray();
        UnitCapacity = session.Units.Capacity;
        if (playerView != null)
        {
            DeployCount = new CrowdHudRange(playerView.DeployCountMin, playerView.DeployCountMax, playerView.DeployCountStep);
            ClickCount = new CrowdHudRange(playerView.ClickCountMin, playerView.ClickCountMax, playerView.ClickCountStep);
        }

        if (rates != null)
        {
            AvoidHzMin = rates.AvoidHzMin;
            AvoidHzMax = rates.AvoidHzMax;
            AvoidHzStep = rates.AvoidStep;
        }
    }

    public IReadOnlyList<string> UnitTypes { get; }
    public IReadOnlyList<string> AgentTypes { get; }
    public IReadOnlyList<int> RadiusCm { get; }
    public IReadOnlyList<string> Formations { get; }
    public IReadOnlyList<string> Templates { get; }
    public int UnitCapacity { get; }
    public CrowdHudRange DeployCount { get; }
    public CrowdHudRange ClickCount { get; }
    public double AvoidHzMin { get; } = double.NaN;
    public double AvoidHzMax { get; } = double.NaN;
    public double AvoidHzStep { get; } = double.NaN;
}

public readonly record struct CrowdHudRange(int Min, int Max, int Step);
