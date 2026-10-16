using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;
using CrowdSimulationTests.Presentation;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>放置判定和指令执行用同一份结论。预览按栅格化的格画，稳态帧不分配。</summary>
public sealed class CrowdPlacementAgreementTests
{
    [Test]
    public void JudgeAndExecute_Agree()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s1337");
        var store = session.Structures!;
        int cell = store.NavCellSizeCm;
        int land = Terrain(session, "land");
        var free = FindCell(store, land, -1);
        var blocked = FindCell(store, land, free.Index);
        var run = FindLandRun(store, land, 6);
        var building = session.Config.Structures.Templates[store.TemplateIndexOf("building")];
        var bridge = session.Config.Structures.Templates[store.TemplateIndexOf("bridge")];
        var size = Fix64.FromInt(cell);

        Agree(session, building, Fix64.FromInt(-cell), Fix64.FromInt(free.Y * cell + cell / 2), size, Fix64.Zero, Fix64.Zero, CrowdPlacementReject.OutOfBounds);
        Agree(
            session, bridge,
            Fix64.FromInt(run.X * cell + cell / 2), Fix64.FromInt(run.Y * cell + cell / 2), size,
            Fix64.FromInt((run.X + 5) * cell + cell / 2), Fix64.FromInt(run.Y * cell + cell / 2),
            CrowdPlacementReject.InteriorArea);
        store.Blocked[blocked.Index] = 1;
        Agree(session, building, Fix64.FromInt(blocked.X * cell + cell / 2), Fix64.FromInt(blocked.Y * cell + cell / 2), size, Fix64.Zero, Fix64.Zero, CrowdPlacementReject.OverlapsBlocked);
        var at = (Fix64.FromInt(free.X * cell + cell / 2), Fix64.FromInt(free.Y * cell + cell / 2));
        Agree(session, building, at.Item1, at.Item2, size, Fix64.Zero, Fix64.Zero, CrowdPlacementReject.None);
        Agree(session, building, at.Item1, at.Item2, size, Fix64.Zero, Fix64.Zero, CrowdPlacementReject.OverlapsStructure);
    }

    [Test]
    public void Preview_DrawsRasterCells_AndSecondFrameAllocatesNothing()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s1337");
        var store = session.Structures!;
        int cell = store.NavCellSizeCm;
        int land = Terrain(session, "land");
        var free = FindCell(store, land, -1);
        int hoverX = free.X * cell + cell / 2;
        int hoverY = free.Y * cell + cell / 2;
        var demo = new S4DeployDemoRuntime(null);
        demo.UseViewLabels(CrowdViewCatalog.Named(("route", "路线")));
        demo.UsePalette(new[]
        {
            new S4DeployDemoRuntime.PaletteChoice("building", "building", "建筑", CrowdPlacementGesture.Point, cell, 0, 0, "CrowdSimulation.S4.Template1"),
        });
        demo.EnqueueTool(CrowdClickTool.Structure);
        demo.Tick();
        demo.HoverCm = (hoverX, hoverY);
        var routes = new RouteVisualBuffer();
        var overlay = new ScreenOverlayBuffer();
        var presentation = Presentation();
        var feed = new CrowdHudFeed(demo, presentation, () => session, () => null, null);
        var system = new S4DeployDemoPresentationSystem(demo, routes, overlay, () => session, presentation, feed);
        system.Update(0f);

        var template = session.Config.Structures.Templates[store.TemplateIndexOf("building")];
        var footprint = CrowdStructureFootprint.Make(
            template, Fix64.FromInt(hoverX), Fix64.FromInt(hoverY), Fix64.FromInt(cell), Fix64.Zero, Fix64.Zero);
        int[] cells = Array.Empty<int>();
        byte[] flags = Array.Empty<byte>();
        var reason = CrowdStructurePlacement.Judge(store, template, footprint, ref cells, ref flags, out int count);
        Assert.That(reason, Is.EqualTo(CrowdPlacementReject.None));
        Assert.That(count, Is.GreaterThan(0));
        Assert.That(routes.Polylines.Count, Is.EqualTo(count));
        var legal = CrowdPresentationConfig.Rgba(TestRgba(89, 230, 115, 242), "deploy.placeLegal");
        for (int i = 0; i < count; i++)
        {
            Assert.That(routes.Polylines[i].PointsMeters.Length, Is.EqualTo(5));
            Assert.That(routes.Polylines[i].Color, Is.EqualTo(legal));
        }

        store.Blocked[free.Index] = 1;
        system.Update(0f);
        reason = CrowdStructurePlacement.Judge(store, template, footprint, ref cells, ref flags, out count);
        Assert.That(reason, Is.EqualTo(CrowdPlacementReject.OverlapsBlocked));
        var illegal = CrowdPresentationConfig.Rgba(TestRgba(255, 77, 64, 242), "deploy.placeIllegal");
        int illegalCells = 0;
        for (int i = 0; i < routes.Polylines.Count; i++)
        {
            if (routes.Polylines[i].Color == illegal) illegalCells++;
        }

        Assert.That(illegalCells, Is.EqualTo(count));
        var hud = default(CrowdDeployHud);
        Assert.That(feed.ReadDeploy(ref hud), Is.True);
        Assert.That(hud.PreviewShown, Is.True);
        Assert.That(hud.PreviewValid, Is.False);
        Assert.That(hud.PreviewReason, Is.EqualTo(CrowdPlacementReject.OverlapsBlocked));
        using (var ui = new CrowdHudTestHost())
        {
            ui.Frame(feed);
            Assert.That(ui.Text("crowd-keys"), Does.Contain("(压到阻挡格)"));
        }

        for (int i = 0; i < 4; i++)
        {
            overlay.Clear();
            system.Update(0f);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        _ = GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 32; i++)
        {
            overlay.Clear();
            system.Update(0f);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.EqualTo(0L), $"放置预览稳态分配了 {allocated} 字节");
    }

    private static void Agree(
        CrowdSimSession session,
        Ludots.Core.CrowdSimulation.Config.RuntimeStructureTemplate template,
        Fix64 x,
        Fix64 y,
        Fix64 size,
        Fix64 toX,
        Fix64 toY,
        CrowdPlacementReject expected)
    {
        var store = session.Structures!;
        var footprint = CrowdStructureFootprint.Make(template, x, y, size, toX, toY);
        int[] cells = Array.Empty<int>();
        byte[] flags = Array.Empty<byte>();
        var judged = CrowdStructurePlacement.Judge(store, template, footprint, ref cells, ref flags, out int count);
        Assert.That(judged, Is.EqualTo(expected), template.Id);
        if (expected != CrowdPlacementReject.OutOfBounds)
        {
            Assert.That(count, Is.GreaterThan(0), template.Id);
        }

        int before = store.EntityIds.Count;
        CrowdStructureOps.PlaceStructure(session, template.Id, x, y, size, toX, toY);
        Assert.That(session.LastPlacementReject, Is.EqualTo(expected), template.Id);
        Assert.That(store.EntityIds.Count, Is.EqualTo(expected == CrowdPlacementReject.None ? before + 1 : before), template.Id);
    }

    private static int Terrain(CrowdSimSession session, string id)
    {
        var ids = session.Config.TerrainTypeIds;
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] == id) return i;
        }

        throw new InvalidOperationException($"地形类型 \"{id}\" 不在这张图上。");
    }

    private static (int X, int Y, int Index) FindCell(CrowdStructuresStore store, int terrain, int skipIndex)
    {
        int side = store.NavCellsPerSide;
        int cell = store.NavCellSizeCm;
        var grid = store.SnapshotSurface().TerrainType;
        for (int y = 2; y < side - 2; y++)
        {
            for (int x = 2; x < side - 2; x++)
            {
                int index = y * side + x;
                if (index == skipIndex || grid[index] != terrain || store.Blocked[index] != 0) continue;
                int cx = x * cell + cell / 2;
                int cy = y * cell + cell / 2;
                if (store.EntityAt(Fix64.FromInt(cx), Fix64.FromInt(cy)) >= 0) continue;
                return (x, y, index);
            }
        }

        throw new InvalidOperationException("没有符合条件的地形格。");
    }

    private static (int X, int Y) FindLandRun(CrowdStructuresStore store, int terrain, int length)
    {
        int side = store.NavCellsPerSide;
        int cell = store.NavCellSizeCm;
        var grid = store.SnapshotSurface().TerrainType;
        for (int y = 2; y < side - 2; y++)
        {
            int run = 0;
            for (int x = 2; x < side - 2; x++)
            {
                int index = y * side + x;
                int cx = x * cell + cell / 2;
                int cy = y * cell + cell / 2;
                bool open = grid[index] == terrain
                    && store.Blocked[index] == 0
                    && store.EntityAt(Fix64.FromInt(cx), Fix64.FromInt(cy)) < 0;
                run = open ? run + 1 : 0;
                if (run >= length) return (x - length + 1, y);
            }
        }

        throw new InvalidOperationException($"没有连续 {length} 格陆地。");
    }

    private static CrowdPresentationConfig Presentation() => new()
    {
        Title = "放置",
        ViewLabels = CrowdViewCatalog.Named(("route", "路线")),
        Hud = new CrowdHudConfig
        {
            Keys = "{activity}",
        },
        Deploy = new CrowdDeployPresentation
        {
            Actions = new CrowdDeployActions
            {
                CycleView = "CrowdSimulation.S4.CycleView",
                ToggleBuild = "CrowdSimulation.S4.ToggleBuild",
                Remove = "CrowdSimulation.S4.RemoveStructure",
                Place = "CrowdSimulation.S4.Place",
            },
            BuildActivity = "建造{placeHint}",
            ObserveActivity = "观察",
            PlaceOk = "(此处可放)",
            PlaceBlocked = "(此处不可放)",
            PlaceOffMap = "(光标不在地图上)",
            PlaceOutOfBounds = "(越界)",
            PlaceOverlapsStructure = "(压到已有结构)",
            PlaceOverlapsBlocked = "(压到阻挡格)",
            PlaceInteriorArea = "(跨越段地形不可放)",
            PlaceEndpointArea = "(桥端不可锚)",
            PlaceLegal = TestRgba(89, 230, 115, 242),
            PlaceIllegal = TestRgba(255, 77, 64, 242),
            PlaceEndpoint = TestRgba(255, 214, 64, 242),
            PlacePortal = TestRgba(64, 196, 255, 242),
            PendingNav = "",
        },
    };

    private static CrowdDebugRgba TestRgba(int r, int g, int b, int a) =>
        new() { Red = r, Green = g, Blue = b, Alpha = a };
}
