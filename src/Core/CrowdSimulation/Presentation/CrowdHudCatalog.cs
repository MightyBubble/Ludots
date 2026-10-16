using Ludots.Core.CrowdSimulation.Runtime;
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
        ProfileIds = config.Profiles.Select(p => p.Id).ToArray();
        ProfileRadiusCm = config.Profiles.Select(p => (int)p.RadiusCm.ToInt()).ToArray();
        if (playerView != null)
        {
            var formationLabels = new string[config.Formations.Count];
            for (int i = 0; i < config.Formations.Count; i++)
            {
                string id = config.Formations[i].Id;
                if (!playerView.ShapeLabels.TryGetValue(id, out string? label) || string.IsNullOrWhiteSpace(label))
                {
                    throw new InvalidOperationException($"玩家视图 shapeLabels 缺少阵型 {id}。");
                }

                formationLabels[i] = label;
            }

            Formations = formationLabels;
            DeployCount = new CrowdHudRange(playerView.DeployCountMin, playerView.DeployCountMax, playerView.DeployCountStep);
            ClickCount = new CrowdHudRange(playerView.ClickCountMin, playerView.ClickCountMax, playerView.ClickCountStep);
            string[] templates = playerView.ClickTemplates;
            ClickTemplates = templates;
            var unitTypeIds = new string[templates.Length];
            var templateRadiusCm = new int[templates.Length];
            for (int i = 0; i < templates.Length; i++)
            {
                var (unitType, radiusIndex) = CrowdDeployment.LocateTemplate(config, templates[i]);
                unitTypeIds[i] = config.UnitTypes[unitType].Id;
                templateRadiusCm[i] = config.RadiusClasses[radiusIndex];
            }

            ClickTemplateUnitTypeIds = unitTypeIds;
            ClickTemplateRadiusCm = templateRadiusCm;
        }
        else
        {
            Formations = config.Formations.Select(f => f.Id).ToArray();
            ClickTemplates = Array.Empty<string>();
            ClickTemplateUnitTypeIds = Array.Empty<string>();
            ClickTemplateRadiusCm = Array.Empty<int>();
        }

        Templates = palette.Select(p => p.Label).ToArray();
        UnitCapacity = session.Units.Capacity;

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
    public IReadOnlyList<string> ProfileIds { get; }
    public IReadOnlyList<int> ProfileRadiusCm { get; }
    public IReadOnlyList<string> Formations { get; }
    public IReadOnlyList<string> ClickTemplates { get; }
    public IReadOnlyList<string> ClickTemplateUnitTypeIds { get; }
    public IReadOnlyList<int> ClickTemplateRadiusCm { get; }
    public IReadOnlyList<string> Templates { get; }
    public int UnitCapacity { get; }
    public CrowdHudRange DeployCount { get; }
    public CrowdHudRange ClickCount { get; }
    public double AvoidHzMin { get; } = double.NaN;
    public double AvoidHzMax { get; } = double.NaN;
    public double AvoidHzStep { get; } = double.NaN;
}

public readonly record struct CrowdHudRange(int Min, int Max, int Step);
