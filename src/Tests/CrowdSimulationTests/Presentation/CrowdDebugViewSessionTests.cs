using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Utils;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests.Presentation;

[TestFixture]
public class CrowdDebugViewSessionTests
{
    [Test]
    public void LayersOnAndOff_KeepPerTickChecksum()
    {
        var (runtimeA, onSession) = S4DeployTruthTests.BuildSession("s7");
        using var serviceA = new PathQueryService(onSession.Navs, runtimeA, 1, TimeSpan.FromSeconds(30), onSession.ResolveNavContext);
        onSession.EnableMovement(CrowdMovementKernel.Create(onSession), new CrowdSimPlanner(onSession, serviceA));
        var (runtimeB, offSession) = S4DeployTruthTests.BuildSession("s7");
        using var serviceB = new PathQueryService(offSession.Navs, runtimeB, 1, TimeSpan.FromSeconds(30), offSession.ResolveNavContext);
        offSession.EnableMovement(CrowdMovementKernel.Create(offSession), new CrowdSimPlanner(offSession, serviceB));

        var frameOn = new CrowdDebugViewFrame(LoadView());
        frameOn.Funnel = frameOn.Corridor = frameOn.HpaGraph = frameOn.NavPolygons = frameOn.TileGrid = true;
        frameOn.Flow = frameOn.Density = frameOn.Blocked = frameOn.Links = frameOn.Belief = true;
        frameOn.UnitColorMode = CrowdSimulationDebugViewConfig.ColorTeam;
        frameOn.UsePalette(new TeamColorPalette(new[]
        {
            default,
            TeamColorPalette.ParseHex("#4DB2FF", "test"),
            TeamColorPalette.ParseHex("#7ED957", "test"),
            TeamColorPalette.ParseHex("#FFC53D", "test"),
            TeamColorPalette.ParseHex("#C792FF", "test"),
        }));
        frameOn.SetViewport(480000f, 540000f, 560000f, 620000f, 20f);
        frameOn.ControllingPlayer = 0;
        var frameOff = new CrowdDebugViewFrame(LoadView());
        frameOff.SetViewport(480000f, 540000f, 560000f, 620000f, 20f);
        var overlay = new ScreenOverlayBuffer();
        var buffer = new GlobalFieldVisualBuffer(16, 1 << 16, 16);
        for (int i = 0; i < 8; i++)
        {
            Assert.That(onSession.Step(out ulong onHash), Is.True);
            Assert.That(offSession.Step(out ulong offHash), Is.True);
            overlay.Clear();
            buffer.BeginFrame();
            frameOn.Draw(onSession, overlay);
            frameOn.Project(buffer, onSession);
            frameOff.Draw(offSession, overlay);
            frameOff.Project(buffer, offSession);
            Assert.That(onHash, Is.EqualTo(offHash));
        }
    }

    [Test]
    public void BeliefDifference_MatchesFieldAgainstTruth_AndPolygonsClip()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        var store = session.Structures!;
        CrowdStructureOps.PlaceStructure(session, "building", Fix64.FromInt(503125), Fix64.FromInt(565625), Fix64.FromInt(14000), Fix64.Zero, Fix64.Zero);
        var beforeArea = (byte[])store.Area.Clone();
        var beforeBlocked = (byte[])store.Blocked.Clone();
        session.Beliefs!.Register(1, Entities(store), null);
        int side = store.NavCellsPerSide;
        var cells = new int[side * side];
        var ghost = new byte[side * side];
        Assert.That(session.Beliefs.CopyDivergentCells(0, 0, 0, side, side, 1, cells, ghost), Is.EqualTo(0));
        Assert.That(session.Beliefs.CopyDivergentCells(1, 0, 0, side, side, 1, cells, ghost), Is.EqualTo(0));

        int removed = store.EntityAt(Fix64.FromInt(503125), Fix64.FromInt(565625));
        Assert.That(removed, Is.GreaterThanOrEqualTo(0));
        CrowdStructureOps.RemoveStructure(session, removed);
        int count = session.Beliefs.CopyDivergentCells(1, 0, 0, side, side, 1, cells, ghost);
        var expected = new List<int>();
        var expectedGhost = new List<byte>();
        for (int i = 0; i < beforeArea.Length; i++)
        {
            if (beforeArea[i] == store.Area[i] && beforeBlocked[i] == store.Blocked[i]) continue;
            expected.Add(i);
            expectedGhost.Add((byte)(beforeBlocked[i] != 0 && store.Blocked[i] == 0 ? 1 : 0));
        }

        Assert.That(cells[..count], Is.EqualTo(expected));
        Assert.That(ghost[..count], Is.EqualTo(expectedGhost));
        Assert.That(count, Is.GreaterThan(0));
        Assert.That(expectedGhost.Any(flag => flag != 0), Is.True);

        var nav = session.NavFor(0, 0);
        Assert.That(nav.ChangedTiles, Is.Not.Null);
        var dirty = new int[nav.ChangedTiles!.Count];
        int dirtyCount = CrowdDebugOverlayGeometry.CopyDirtyTiles(nav.ChangedTiles, dirty);
        Assert.That(dirty[..dirtyCount], Is.EqualTo(nav.ChangedTiles));
        var world = new CrowdDebugSegment[65536];
        int edges = CrowdDebugOverlayGeometry.AppendPolygons(nav, side, session.Config.Hpa.NavCellsPerTile, session.Config.NavCellSizeCm, world, 0);
        Assert.That(edges, Is.GreaterThan(0));
        var clipped = new CrowdDebugSegment[64];
        int cell = session.Config.NavCellSizeCm;
        int kept = CrowdDebugOverlayGeometry.ClipSegments(world.AsSpan(0, edges), 0f, 0f, cell * 3f, cell * 3f, clipped);
        Assert.That(kept, Is.LessThanOrEqualTo(clipped.Length));
        Assert.That(kept, Is.LessThan(edges));
    }

    private static List<(int Id, int TplIndex, CrowdStructureFootprint Fp)> Entities(CrowdStructuresStore store)
    {
        var list = new List<(int, int, CrowdStructureFootprint)>();
        foreach (int id in store.EntityIds)
        {
            if (store.TryGetFootprint(id, out var footprint) && store.TryGetTemplate(id, out int template))
                list.Add((id, template, footprint));
        }

        return list;
    }

    [Test]
    public void InspectIds_UnknownAgentProfileAndPlayer_FailAtBind()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        string path = ViewPath();
        var root = CrowdPresentationPaletteTestMerge.MergedDebugView(
            JsonNode.Parse(File.ReadAllText(path))!.AsObject());

        var oldKey = (JsonObject)root.DeepClone();
        oldKey["inspect"]!["layerIndex"] = 0;
        var key = Assert.Throws<InvalidOperationException>(() => CrowdSimulationDebugViewConfig.Load(oldKey, path));
        Assert.That(key!.Message, Does.Contain("layerIndex"));

        var oldBelief = (JsonObject)root.DeepClone();
        oldBelief["beliefGroup"] = 0;
        var beliefKey = Assert.Throws<InvalidOperationException>(() => CrowdSimulationDebugViewConfig.Load(oldBelief, path));
        Assert.That(beliefKey!.Message, Does.Contain("beliefGroup"));

        var agent = (JsonObject)root.DeepClone();
        agent["inspect"]!["agentType"] = "missing-move";
        var frame = new CrowdDebugViewFrame(CrowdSimulationDebugViewConfig.Load(agent, path));
        var missingAgent = Assert.Throws<InvalidOperationException>(() => frame.BindCatalog(session));
        Assert.That(missingAgent!.Message, Does.Contain("missing-move"));

        var body = (JsonObject)root.DeepClone();
        body["inspect"]!["profile"] = "missing-body";
        frame = new CrowdDebugViewFrame(CrowdSimulationDebugViewConfig.Load(body, path));
        var missingBody = Assert.Throws<InvalidOperationException>(() => frame.BindCatalog(session));
        Assert.That(missingBody!.Message, Does.Contain("missing-body"));

        var player = (JsonObject)root.DeepClone();
        player["beliefPlayer"] = 99;
        frame = new CrowdDebugViewFrame(CrowdSimulationDebugViewConfig.Load(player, path));
        var missingPlayer = Assert.Throws<InvalidOperationException>(() => frame.BindCatalog(session));
        Assert.That(missingPlayer!.Message, Does.Contain("99"));
    }

    private static string ViewPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mods")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null) throw new InvalidOperationException("找不到仓库根(向上查找 mods 目录失败)");
        return Path.Combine(dir, "mods", "capabilities", "navigation", "CrowdSimulationUiMod", "assets", "CrowdSimulationDebugView.json");
    }

    private static CrowdSimulationDebugViewConfig LoadView()
    {
        string path = ViewPath();
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        return CrowdSimulationDebugViewConfig.Load(CrowdPresentationPaletteTestMerge.MergedDebugView(root), path);
    }
}
