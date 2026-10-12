using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests;

[TestFixture]
public class MapSizeAgreementTests
{
    private const string SurfacePath = "assets/terrain/fixture.navsurface";
    private const string HeightPath = "assets/terrain/fixture.height";

    [Test]
    public void S1Seeds_BoardNavsurfaceAndHeightAgree([ValueSource(typeof(S1SurfaceTruthTests), nameof(S1SurfaceTruthTests.Seeds))] string seed)
    {
        var runtime = S1SurfaceTruthTests.LoadRuntime(seed);
        var surface = S1SurfaceTruthTests.ReadSurface(seed);
        string mapPath = Path.Combine(S1SurfaceTruthTests.SeedDir(seed), "Maps", $"crowd_simulation_{seed}.json");
        var map = JsonNode.Parse(File.ReadAllText(mapPath))!;
        int boardWidthCm = map["Boards"]![0]!["WidthCm"]!.GetValue<int>();
        int boardHeightCm = map["Boards"]![0]!["HeightCm"]!.GetValue<int>();
        string heightAssetPath = map["ContinuousHeightmap"]!["Asset"]!.GetValue<string>();
        var height = ContinuousHeightmapBinary.Read(File.OpenRead(Path.Combine(S1SurfaceTruthTests.SeedDir(seed), heightAssetPath["assets/".Length..])));

        Assert.That(boardWidthCm, Is.EqualTo(1_600_000));
        Assert.That(boardHeightCm, Is.EqualTo(1_600_000));
        Assert.That(runtime.NavCellSizeCm, Is.EqualTo(6250));
        Assert.That(runtime.NavCellsPerSide, Is.EqualTo(256));
        Assert.That(surface.NavCellsX, Is.EqualTo(256));
        Assert.That(surface.NavCellsY, Is.EqualTo(256));
        Assert.That(surface.NavCellSizeCm, Is.EqualTo(6250));
        Assert.That(height.Bounds.Width, Is.EqualTo(1_600_000));
        Assert.That(height.Bounds.Height, Is.EqualTo(1_600_000));
        Assert.That(height.SampleColumns, Is.EqualTo(1024));
        Assert.That(height.SampleRows, Is.EqualTo(1024));

        Assert.DoesNotThrow(() => NavSurfaceContract.ValidateWorldSize(
            boardWidthCm,
            boardHeightCm,
            runtime.NavCellsPerSide,
            runtime.NavCellSizeCm,
            surface,
            "assets/" + runtime.SurfaceAsset,
            height,
            heightAssetPath));
    }

    [Test]
    public void BoardNavsurfaceMismatch_NamesNavsurface()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => NavSurfaceContract.ValidateWorldSize(
            1600, 1600, 16, 100,
            Surface(15, 16, 100),
            SurfacePath,
            Height(1600, 1600, 16),
            HeightPath));

        Assert.That(ex!.Message, Does.StartWith(SurfacePath));
        Assert.That(ex.Message, Does.Contain("15×16"));
        Assert.That(ex.Message, Does.Contain("16×16"));
        Assert.That(ex.Message, Does.Contain("1600×1600"));
        Assert.That(ex.Message, Does.Contain(HeightPath));
        Assert.That(ex.Message, Does.Contain("采样 16×16"));
    }

    [Test]
    public void NavsurfaceCellSizeMismatch_NamesNavsurface()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => NavSurfaceContract.ValidateWorldSize(
            1600, 1600, 16, 100,
            Surface(16, 16, 200),
            SurfacePath,
            Height(1600, 1600, 16),
            HeightPath));

        Assert.That(ex!.Message, Does.StartWith(SurfacePath));
        Assert.That(ex.Message, Does.Contain("navCellSizeCm = 200"));
        Assert.That(ex.Message, Does.Contain("profiles.cellSizeCm = 100"));
        Assert.That(ex.Message, Does.Contain("推出 16×16 格"));
        Assert.That(ex.Message, Does.Contain(HeightPath));
        Assert.That(ex.Message, Does.Contain("采样 16×16"));
    }

    [Test]
    public void HeightBboxMismatch_NamesHeight()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => NavSurfaceContract.ValidateWorldSize(
            1600, 1600, 16, 100,
            Surface(16, 16, 100),
            SurfacePath,
            Height(1601, 1600, 16),
            HeightPath));

        Assert.That(ex!.Message, Does.StartWith(HeightPath));
        Assert.That(ex.Message, Does.Contain("1601×1600"));
        Assert.That(ex.Message, Does.Contain("1600×1600"));
        Assert.That(ex.Message, Does.Contain("推出 16×16 格"));
        Assert.That(ex.Message, Does.Contain(SurfacePath));
        Assert.That(ex.Message, Does.Contain("头 16×16"));
        Assert.That(ex.Message, Does.Contain("采样 16×16"));
    }

    [Test]
    public void HeightSamplesNotSquareMultiple_NamesHeight()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => NavSurfaceContract.ValidateWorldSize(
            1600, 1600, 16, 100,
            Surface(16, 16, 100),
            SurfacePath,
            Height(1600, 1600, 20),
            HeightPath));

        Assert.That(ex!.Message, Does.StartWith(HeightPath));
        Assert.That(ex.Message, Does.Contain("采样 20×20"));
        Assert.That(ex.Message, Does.Contain("16×16"));
    }

    [Test]
    public void NonDividingBoard_NamesBoard()
    {
        var map = TestDefaults.DemoMap();
        map.Boards[0].WidthCm = 1_600_001;
        map.Boards[0].HeightCm = 1_600_001;
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TestDefaults.Assemble(map: map, pairRules: TestDefaults.DemoPairRules()));

        Assert.That(ex!.Message, Does.Contain("Maps/crowd_simulation_demo.json"));
        Assert.That(ex.Message, Does.Contain("求解器板 \"default\""));
        Assert.That(ex.Message, Does.Contain("WidthCm（1600001）"));
    }

    private static NavSurfaceAsset Surface(int cellsX, int cellsY, int navCellSizeCm) => new()
    {
        NavCellsX = cellsX,
        NavCellsY = cellsY,
        NavCellSizeCm = navCellSizeCm,
        TerrainTypeIds = new[] { "land" },
        TerrainCells = new byte[cellsX * cellsY],
        JumpCandidates = Array.Empty<NavSurfaceJumpCandidate>(),
    };

    private static ContinuousHeightmapAsset Height(int widthCm, int heightCm, int samples)
        => ContinuousHeightmapAsset.CreateSingleLayerFromRawUInt16(
            new WorldAabbCm(0, 0, widthCm, heightCm),
            samples,
            samples,
            new ushort[samples * samples],
            ContinuousHeightSampleScale.IdentityCentimeters);
}
