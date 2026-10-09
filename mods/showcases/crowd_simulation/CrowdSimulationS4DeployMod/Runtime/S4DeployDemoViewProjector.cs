using System;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Presentation.Fields;
using Ludots.Core.Presentation.Rendering;

namespace CrowdSimulationS4DeployMod.Runtime;

/// <summary>S4 演示视图投影器:按视图模式分发(与 S3 的 DemoViewProjector 同形)——
/// 路线 = 主力组(成员最多的带流场组)的流场方向图,可走区域 = 该组导航上下文的遮罩层,
/// NavMesh 模式不写场(线框走路线通道,由呈现系统画)。会话未激活时不写场。
/// 流场源逐帧重绑:结构重烘与重规划都会换组、换流场,不能在注册时固化。</summary>
internal sealed class S4DeployDemoViewProjector : IGlobalFieldVisualProjector
{
    private readonly S4DeployDemoRuntime _demo;
    private readonly Func<CrowdSimSession?> _sessionSource;
    private readonly CrowdFlowFieldVisualSource _flowSource = new();
    private readonly CrowdFlowFieldVisualProjector _flow;
    private readonly CrowdWalkableVisualProjector _walkable;
    private readonly Func<NavContext?> _navSource;

    public S4DeployDemoViewProjector(S4DeployDemoRuntime demo, Func<CrowdSimSession?> sessionSource)
    {
        _demo = demo;
        _sessionSource = sessionSource;
        _flow = new CrowdFlowFieldVisualProjector(_flowSource);
        _navSource = () =>
        {
            var s = _sessionSource();
            return DominantGroup(s)?.NavId is { } navId && s.Navs.TryGetValue(navId, out var nav) ? nav : null;
        };
        _walkable = new CrowdWalkableVisualProjector(_flowSource, _navSource);
    }

    public void Project(GlobalFieldVisualBuffer buffer)
    {
        var session = _sessionSource();
        if (session == null) return;
        var group = DominantGroup(session);
        _flowSource.CellCount = session.Config.NavCellCount;
        _flowSource.CellSizeCm = session.Config.NavCellSizeCm;
        _flowSource.Flow = group?.Flow;
        switch (_demo.ViewMode)
        {
            case 0: _flow.Project(buffer); break;
            case 1: _walkable.Project(buffer); break;
        }
    }

    /// <summary>主力组:成员最多且带流场的组(演示口径——行军主力的路线就是当前值得看的路线)。</summary>
    internal static CrowdNavGroupSet.Group? DominantGroup(CrowdSimSession? session)
    {
        if (session == null) return null;
        CrowdNavGroupSet.Group? best = null;
        foreach (var g in session.Groups.Groups)
        {
            if (g == null || g.Flow == null || g.Count == 0) continue;
            if (best == null || g.Count > best.Count) best = g;
        }

        return best;
    }
}
