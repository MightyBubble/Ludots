using System;
using System.Linq;
using CrowdSimulationS4DeployMod.Runtime;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// S4 分层视图在迷雾开启后组导航号是认知变体(槽号 × BeliefStride + 真相号),
/// 不在会话真相字典里。仿真侧 ResolveNavContext 物化变体之后,呈现只读该变体。
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
        Assert.That(projector.WalkableNavPending, Is.False);
        Assert.That(S4DeployDemoPresentationSystem.NavVariantPending(session, demo.ViewMode), Is.False);
    }

    [Test]
    public void BeliefNav_NotInTruthDict_WireframeNonEmpty_RevealRebuilds()
    {
        var (session, service, beliefId, variant) = MaterializedBelief("s7");
        using (service)
        {
            Assert.That(session.Navs.ContainsKey(beliefId), Is.False);
            var wire = new S4DeployNavWireframe();
            Assert.That(wire.TryProject(session, out bool pending, out bool rebuilt), Is.True);
            Assert.That(pending, Is.False);
            Assert.That(rebuilt, Is.True);
            Assert.That(wire.Lines, Is.Not.Empty);
            Assert.That(wire.CachedVersion, Is.EqualTo(variant.Version));

            Assert.That(wire.TryProject(session, out _, out bool rebuiltAgain), Is.True);
            Assert.That(rebuiltAgain, Is.False);

            int version = variant.Version;
            session.Beliefs!.Reveal(1, new List<int> { 0 });
            Assert.That(variant.Version, Is.GreaterThan(version), "原位揭示没有递增变体 Version,线框缓存键断言没有观察对象");
            Assert.That(session.LastRebakeReport, Is.Null, "原位揭示不应改结构重烘序");

            Assert.That(wire.TryProject(session, out _, out bool rebuiltAfterReveal), Is.True);
            Assert.That(rebuiltAfterReveal, Is.True);
            Assert.That(wire.CachedVersion, Is.EqualTo(variant.Version));
            Assert.That(wire.Lines, Is.Not.Empty);
        }
    }

    [Test]
    public void PresentationRead_UnmaterializedVariant_DoesNotBuild()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        int truthId = session.Navs.Keys.Min();
        int clusters = session.Navs[truthId].Hpa!.ClustersPerSide;
        var unknown = Enumerable.Range(0, clusters * clusters / 2).ToList();
        session.Beliefs!.Register(1, Array.Empty<(int Id, int TplIndex, CrowdStructureFootprint Fp)>(), unknown);
        int beliefId = CrowdBeliefNavs.BeliefStride + truthId;
        var group = session.Groups.Alloc(player: 1, layerIdx: 0, rIdx: 0, navId: beliefId);
        group.Count = 4;
        group.Flow = EmptyFlow();

        int variants = session.Beliefs.VariantCount();
        int hits = session.NavTileCache!.Hits;
        int misses = session.NavTileCache.Misses;
        Assert.That(variants, Is.EqualTo(0));

        var demo = new S4DeployDemoRuntime(null!);
        demo.EnqueueCycleView();
        demo.Tick();
        var projector = new S4DeployDemoViewProjector(demo, () => session);
        var buffer = new GlobalFieldVisualBuffer(recordCapacity: 4, cellCapacity: 1 << 18, dirtyRectCapacity: 4);
        buffer.BeginFrame();
        projector.Project(buffer);
        Assert.That(session.Beliefs.VariantCount(), Is.EqualTo(variants));
        Assert.That(session.NavTileCache.Hits, Is.EqualTo(hits));
        Assert.That(session.NavTileCache.Misses, Is.EqualTo(misses));

        var wire = new S4DeployNavWireframe();
        Assert.That(wire.TryProject(session, out bool pending, out bool rebuilt), Is.False);
        Assert.That(session.Beliefs.VariantCount(), Is.EqualTo(variants));
        Assert.That(session.NavTileCache.Hits, Is.EqualTo(hits));
        Assert.That(session.NavTileCache.Misses, Is.EqualTo(misses));
        Assert.That(projector.WalkableNavPending, Is.True);
        Assert.That(S4DeployDemoPresentationSystem.NavVariantPending(session, demo.ViewMode), Is.True);
        Assert.That(pending, Is.True);
        Assert.That(rebuilt, Is.False);
        Assert.That(wire.HasCache, Is.False);
        Assert.That(buffer.GetRecords().ToArray().Count(record => record.IsActive), Is.EqualTo(0));
    }

    private static (CrowdSimSession Session, PathQueryService Service, int BeliefId, NavContext Variant) MaterializedBelief(string seed)
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession(seed);
        var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        int truthId = session.Navs.Keys.Min();
        int clusters = session.Navs[truthId].Hpa!.ClustersPerSide;
        var unknown = Enumerable.Range(0, clusters * clusters / 2).ToList();
        session.Beliefs!.Register(1, Array.Empty<(int Id, int TplIndex, CrowdStructureFootprint Fp)>(), unknown);
        int beliefId = CrowdBeliefNavs.BeliefStride + truthId;
        var variant = session.ResolveNavContext(beliefId);
        var group = session.Groups.Alloc(player: 1, layerIdx: 0, rIdx: 0, navId: beliefId);
        group.Count = 4;
        group.Flow = EmptyFlow();
        return (session, service, beliefId, variant);
    }

    private static FlowField EmptyFlow() => new()
    {
        Integ = Array.Empty<Fix64>(),
        Len = Array.Empty<Fix64>(),
        Wp = Array.Empty<int>(),
        Reached = 1,
    };
}
