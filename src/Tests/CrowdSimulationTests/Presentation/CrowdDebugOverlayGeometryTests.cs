using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;

namespace CrowdSimulationTests.Presentation;

[TestFixture]
public class CrowdDebugOverlayGeometryTests
{
    [Test]
    public void CorridorTiles_AreUnionOfMatchingGroupMasks()
    {
        var first = Flow(new byte[] { 1, 0, 1, 0 });
        var second = Flow(new byte[] { 0, 1, 0, 0 });
        var otherPlayer = Flow(new byte[] { 1, 1, 1, 1 });
        var otherRadius = Flow(new byte[] { 1, 1, 1, 1 });
        CrowdNavGroupSet.Group?[] groups =
        {
            null,
            Group(player: 0, layer: 1, radius: 2, first),
            Group(player: 0, layer: 1, radius: 2, second),
            Group(player: 1, layer: 1, radius: 2, otherPlayer),
            Group(player: 0, layer: 1, radius: 0, otherRadius),
        };
        var union = new byte[4];
        var tiles = new int[4];
        int count = CrowdDebugOverlayGeometry.UnionCorridorTiles(groups, player: 0, layer: 1, radius: 2, union, tiles, out int firstGroup);
        Assert.That(firstGroup, Is.EqualTo(1));
        Assert.That(tiles[..count], Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void JumpLinks_SplitTwoWayAndOneWay()
    {
        var links = new NavLinkSet
        {
            Count = 2,
            From = [0, 1],
            To = [2, 3],
            Cost = [Fix64.OneValue, Fix64.OneValue],
            TwoWay = [1, 0],
            LengthCells = [1f, 1f],
            OutStart = [0, 1],
            OutList = [0, 1],
            InStart = [0, 1],
            InList = [0, 1],
            CompOut = new Dictionary<int, HashSet<int>>(),
        };
        var dest = new CrowdDebugSegment[2];
        int count = CrowdDebugOverlayGeometry.AppendJumps(links, navCellsPerSide: 4, cellCm: 10, dest, 0);
        Assert.That(count, Is.EqualTo(2));
        Assert.That(dest[0].Kind, Is.EqualTo(CrowdDebugSegmentKind.TwoWay));
        Assert.That(dest[1].Kind, Is.EqualTo(CrowdDebugSegmentKind.OneWay));
    }

    [Test]
    public void DirtyTiles_MatchLastRebakeList()
    {
        var changed = new List<int> { 4, 9, 9 };
        var dest = new int[3];
        int count = CrowdDebugOverlayGeometry.CopyDirtyTiles(changed, dest);
        Assert.That(dest[..count], Is.EqualTo(changed));
    }

    [Test]
    public void HpaEdges_DedupMutualAdjacency()
    {
        var block = new HpaTileBlock
        {
            Cells = [1, 4],
            Index = new Dictionary<int, int> { [1] = 0, [4] = 1 },
            AdjStart = [0, 1, 2],
            AdjTo = [4, 1],
            AdjCost = [Fix64.OneValue, Fix64.OneValue],
            EdgeCount = 2,
            CellCount = 2,
        };
        var graph = new HpaGraph
        {
            NavCellsPerTile = 2,
            NavTilesPerSide = 1,
            NavCellTotal = 4,
            MaxEntranceWidthNavCells = 1,
            BorderE = [[]],
            BorderS = [[]],
            UpE = [[]],
            UpECost = [[]],
            UpS = [[]],
            UpSCost = [[]],
            Cells = [[]],
            Intra = [[]],
            IntraDist = [[]],
            CrossLayer = [[]],
            Blocks = [block],
            LinkOut = new Dictionary<int, List<(int To, Fix64 Cost)>>(),
            NodeCount = 2,
            EdgeCount = 1,
        };
        var dest = new CrowdDebugSegment[4];
        int edges = CrowdDebugOverlayGeometry.AppendHpaEdges(graph, navCellsPerSide: 4, cellCm: 10, dest, 0);
        var nodes = new int[4];
        int nodeCount = CrowdDebugOverlayGeometry.CopyHpaNodes(graph, nodes);
        Assert.That(edges, Is.EqualTo(1));
        Assert.That(dest[0].Kind, Is.EqualTo(CrowdDebugSegmentKind.Plan));
        Assert.That(nodes[..nodeCount], Is.EqualTo(new[] { 1, 4 }));
    }

    [Test]
    public void BeliefCells_AreAreaOrBlockedDifference_GhostsAreBeliefOnlyBlocks()
    {
        byte[] beliefArea = [0, 1, 0, 0];
        byte[] beliefBlocked = [0, 0, 1, 0];
        byte[] truthArea = [0, 0, 0, 0];
        byte[] truthBlocked = [0, 0, 0, 1];
        var cells = new int[4];
        var ghost = new byte[4];
        int count = CrowdBeliefNavs.DiffCells(beliefArea, beliefBlocked, truthArea, truthBlocked, 2, 0, 0, 2, 2, 1, cells, ghost);
        Assert.That(cells[..count], Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(ghost[..count], Is.EqualTo(new byte[] { 0, 1, 0 }));
    }

    [Test]
    public void UnregisteredBeliefSlot_IsEmpty()
    {
        Assert.That(CrowdBeliefNavs.DiffCells(
            [0], [0], [0], [0], 1, 0, 0, 1, 1, 1, new int[1], new byte[1]), Is.EqualTo(0));
    }

    [Test]
    public void Clip_BoundsSegmentCount()
    {
        var lines = new CrowdDebugSegment[1024];
        for (int i = 0; i < lines.Length; i++)
            lines[i] = new CrowdDebugSegment { X0 = i, Y0 = 0f, X1 = i, Y1 = 1000f, Kind = CrowdDebugSegmentKind.Nav };
        var dest = new CrowdDebugSegment[8];
        int count = CrowdDebugOverlayGeometry.ClipSegments(lines, 10f, 0f, 13f, 1000f, dest);
        Assert.That(count, Is.LessThan(lines.Length));
        Assert.That(count, Is.LessThanOrEqualTo(dest.Length));
        Assert.That(count, Is.EqualTo(4));
    }

    [Test]
    public void BlockedCells_MatchNonZeroInRect()
    {
        var blocked = new byte[16];
        blocked[1 * 4 + 2] = 1;
        blocked[0] = 1;
        var dest = new int[4];
        int count = CrowdDebugOverlayGeometry.CopyBlockedCells(blocked, 4, 1, 1, 4, 4, 1, dest);
        Assert.That(dest[..count], Is.EqualTo(new[] { 6 }));
    }

    [Test]
    public void Funnel_RemainingPath_DedupsSharedLeader_SkipsDoneAndOtherPlayer()
    {
        var nav = MinimalNav();
        var shared = new CrowdLeader(8);
        shared.Reset(
            [new Fix64Vec2(0, 0), new Fix64Vec2(100, 0), new Fix64Vec2(100, 100)],
            0, nav, false, null, Fix64.FromInt(10), Fix64.OneValue);
        var done = new CrowdLeader(8);
        done.Reset(
            [new Fix64Vec2(0, 0), new Fix64Vec2(10, 0)],
            0, nav, false, null, Fix64.FromInt(10), Fix64.OneValue);
        done.Step(Fix64.FromInt(1000), Fix64.OneValue, Fix64.OneValue, Fix64.OneValue, Fix64.FromInt(10), 100000, 2);
        Assert.That(done.Done, Is.True);
        var other = new CrowdLeader(8);
        other.Reset(
            [new Fix64Vec2(0, 0), new Fix64Vec2(5, 0)],
            0, nav, false, null, Fix64.FromInt(10), Fix64.OneValue);
        CrowdNavGroupSet.Group?[] groups =
        {
            Group(0, 0, 0, null, shared),
            Group(0, 0, 0, null, shared),
            Group(0, 0, 0, null, done),
            Group(1, 0, 0, null, other),
        };
        var dest = new CrowdDebugSegment[8];
        var seen = new CrowdLeader?[4];
        int seenCount = 0;
        int count = CrowdDebugOverlayGeometry.AppendFunnel(groups, player: 0, seen, ref seenCount, dest, 0);
        Assert.That(seenCount, Is.EqualTo(1));
        Assert.That(count, Is.EqualTo(2));
        Assert.That(dest[0].Kind, Is.EqualTo(CrowdDebugSegmentKind.Leader));
        Assert.That(dest[1].Kind, Is.EqualTo(CrowdDebugSegmentKind.Funnel));
        Assert.That(dest[0].X1, Is.EqualTo(100f));
        Assert.That(dest[1].Y1, Is.EqualTo(100f));
    }

    [Test]
    public void Polygons_ReadTileEdges()
    {
        var nav = MinimalNav();
        nav.Tiles =
        [
            new NavTileEntry
            {
                Uid = 1,
                Count = 1,
                RegionCount = 1,
                Vx = [0, 2, 0],
                Vy = [0, 0, 2],
                PolyStart = [0, 3],
                PolyVerts = [0, 1, 2],
                NeiStart = [0, 0],
                Nei = [],
                NeiA = [],
                NeiB = [],
                BMinX = [0],
                BMinY = [0],
                BMaxX = [2],
                BMaxY = [2],
                PolyOf = [0],
                BorderPoly = [],
                BorderSide = [],
                BorderLo = [],
                BorderHi = [],
                BorderRev = [],
            },
        ];
        var dest = new CrowdDebugSegment[8];
        int count = CrowdDebugOverlayGeometry.AppendPolygons(nav, navCellsPerSide: 2, navCellsPerTile: 2, cellCm: 10, dest, 0);
        Assert.That(count, Is.EqualTo(3));
        Assert.That(dest[0].Kind, Is.EqualTo(CrowdDebugSegmentKind.Nav));
    }

    [Test]
    public void TemplateIndex_IsLayerTimesRadiusCountPlusRadius()
    {
        Assert.That(CrowdDebugOverlayGeometry.TemplateIndex(2, 1, 4), Is.EqualTo(9));
    }

    [Test]
    public void PaletteIndex_OutOfRange_NamesField()
    {
        var palette = new[] { new CrowdDebugRgba { R = 1, G = 2, B = 3, A = 4 } };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CrowdDebugViewColors.At(palette, 3, "CrowdSimulationDebugView.json", "colors.teams"));
        Assert.That(ex!.Message, Does.Contain("CrowdSimulationDebugView.json"));
        Assert.That(ex.Message, Does.Contain("colors.teams"));
    }

    [Test]
    public void FlowRect_SkipsCellsOutsideAndUsesLayer()
    {
        const int n = 8;
        int n2 = n * n;
        var integ = new Fix64[2 * n2];
        var len = new Fix64[2 * n2];
        var wp = new int[2 * n2];
        Array.Fill(integ, Fix64.MaxValue);
        Array.Fill(wp, -1);
        int goal = 3 * n + 6;
        int cell = 3 * n + 4;
        integ[cell] = Fix64.FromInt(2);
        len[cell] = Fix64.FromInt(8);
        wp[cell] = goal;
        var flow = new FlowField { Integ = integ, Len = len, Wp = wp, Goal = goal, Reached = 1 };
        var source = new CrowdFlowFieldVisualSource { Flow = flow, NavCellsPerSide = n, NavCellSizeCm = 100 };
        var projector = new CrowdFlowFieldVisualProjector(source)
        {
            LimitToRect = true,
            LayerKeyId = 3,
            MinCellX = 0,
            MinCellY = 0,
            MaxCellX = 2,
            MaxCellY = 2,
            CellStep = 1,
            MaxCells = 16,
        };
        var buffer = new GlobalFieldVisualBuffer(4, 64, 4);
        buffer.BeginFrame();
        projector.Project(buffer);
        Assert.That(buffer.GetRecords().ToArray().Count(record => record.IsActive), Is.EqualTo(0));

        projector.MaxCellX = n;
        projector.MaxCellY = n;
        buffer.BeginFrame();
        projector.Project(buffer);
        var record = buffer.GetRecords().ToArray().Single(r => r.IsActive);
        Assert.That(record.Descriptor.Id.LayerKeyId, Is.EqualTo(3));
        Assert.That(record.CellCount, Is.EqualTo(1));
        Assert.That(CrowdDebugViewFrame.ArrowsAllowed(9f, 10), Is.False);
        Assert.That(CrowdDebugViewFrame.ArrowsAllowed(10f, 10), Is.True);
    }

    [Test]
    public void SpatialHash_CopiesOnlyStampedCellsInRect()
    {
        var hash = new CrowdSpatialHash(1000, Fix64.FromInt(100), 8, 1, Fix64.FromInt(50));
        var positions = new[]
        {
            new Fix64Vec2(Fix64.FromInt(50), Fix64.FromInt(50)),
            new Fix64Vec2(Fix64.FromInt(800), Fix64.FromInt(800)),
            new Fix64Vec2(Fix64.FromInt(60), Fix64.FromInt(40)),
        };
        var radii = new[] { Fix64.FromInt(10), Fix64.FromInt(10), Fix64.FromInt(10) };
        hash.Build(positions, radii, 3);
        hash.CellRect(Fix64.Zero, Fix64.Zero, Fix64.FromInt(150), Fix64.FromInt(150), out int x0, out int y0, out int x1, out int y1);
        var dest = new int[8];
        int count = hash.CopyItemsInRect(x0, y0, x1, y1, dest);
        Assert.That(dest[..count], Is.EquivalentTo(new[] { 0, 2 }));
    }

    [Test]
    public void StepFor_KeepsCoarseCellCountInsideBudget()
    {
        int step = CrowdDebugOverlayGeometry.StepFor(200, 200, 100);
        int bw = (200 + step - 1) / step;
        int bh = (200 + step - 1) / step;
        Assert.That(step, Is.GreaterThan(1));
        Assert.That(bw * bh, Is.LessThanOrEqualTo(100));
    }

    private static FlowField Flow(byte[] mask) => new()
    {
        Integ = [],
        Len = [],
        Wp = [],
        Mask = mask,
    };

    private static CrowdNavGroupSet.Group Group(int player, int layer, int radius, FlowField? flow, CrowdLeader? leader = null)
    {
        var group = new CrowdNavGroupSet.Group
        {
            Player = player,
            LayerIdx = layer,
            RIdx = radius,
            Flow = flow,
            Leader = leader,
        };
        return group;
    }

    private static NavContext MinimalNav()
    {
        var cost = new Fix64[4];
        var bits = new byte[4];
        return new NavContext
        {
            Id = 1,
            LayerIndex = 0,
            ClearanceNavCells = 0,
            NavCellsPerSide = 2,
            Cost = cost,
            SpeedDivisor = cost,
            Walk = bits,
            Passable = bits,
            Cells = new int[1],
            Comp = new int[1],
            CompCount = 0,
            Links = null,
            UpPass = bits,
            Portal = bits,
            UpArea = bits,
            UpCost = cost,
            UpSpeedDivisor = cost,
            MinCost = Fix64.OneValue,
        };
    }
}
