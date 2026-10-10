using System;
using System.Linq;
using CrowdSimulationS4DeployMod.Runtime;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// S4 分层视图在迷雾开启后组导航号是认知变体(槽号 × BeliefStride + 真相号),
/// 不在会话真相字典里。可走区域必须经 ResolveNavContext 取到变体,否则缓冲为空。
/// </summary>
[TestFixture]
public class S4DeployViewProjectorTests
{
    [Test]
    public void WalkableView_BeliefNavId_PublishesWalkableField()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));

        int truthId = session.Navs.Keys.Min();
        int clusters = session.Navs[truthId].Hpa!.ClustersPerSide;
        var unknown = Enumerable.Range(0, clusters * clusters / 2).ToList();
        session.Beliefs!.Register(1, Array.Empty<(int Id, int TplIndex, CrowdStructureFootprint Fp)>(), unknown);
        int beliefId = CrowdBeliefNavs.BeliefStride + truthId;
        var variant = session.ResolveNavContext(beliefId);
        int blocked = 0;
        for (int i = 0; i < variant.Passable.Length; i++)
        {
            if (variant.Passable[i] == 0) blocked++;
        }

        Assert.That(blocked, Is.GreaterThan(0), "认知变体没有不可走格,可走层断言没有观察对象");
        Assert.That(session.Navs.ContainsKey(beliefId), Is.False);

        var group = session.Groups.Alloc(player: 1, layerIdx: 0, rIdx: 0, navId: beliefId);
        group.Count = 4;
        group.Flow = new FlowField
        {
            Integ = Array.Empty<Fix64>(),
            Len = Array.Empty<Fix64>(),
            Wp = Array.Empty<int>(),
            Reached = 1,
        };

        var demo = new S4DeployDemoRuntime(null!);
        demo.EnqueueCycleView();
        demo.Tick();
        Assert.That(demo.ViewMode, Is.EqualTo(1));

        var projector = new S4DeployDemoViewProjector(demo, () => session);
        var buffer = new GlobalFieldVisualBuffer(recordCapacity: 4, cellCapacity: 1 << 18, dirtyRectCapacity: 4);
        buffer.BeginFrame();
        projector.Project(buffer);

        var active = buffer.GetRecords().ToArray().Where(record => record.IsActive).ToArray();
        Assert.That(active, Has.Length.EqualTo(1));
        Assert.That(active[0].Descriptor.Id.Kind, Is.EqualTo(GlobalFieldVisualKind.Walkable));
        Assert.That(active[0].CellCount, Is.GreaterThan(0));
    }
}
