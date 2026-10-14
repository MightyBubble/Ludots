using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Navigation.Terrain;
using Ludots.Tool;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class CrowdNavSurfaceAuthoringTests
{
    private static readonly string[] TwoTypes = ["water", "land"];

    [Test]
    public void WriteAreaGrid_RoundTripsAreaIds()
    {
        const int cells = 64;
        var areaIds = new byte[cells * cells];
        areaIds[0] = 1;
        areaIds[cells - 1] = 4;
        areaIds[(cells - 1) * cells + 10] = 2;

        using var stream = new MemoryStream();
        ReactGridTerrainBinary.WriteAreaGrid(stream, cells, cells, areaIds);
        stream.Position = 0;
        LogicTerrainField terrain = ReactGridTerrainBinary.Read("roundtrip.map_data.bin", stream, cellSizeCm: 6250);

        Assert.Multiple(() =>
        {
            Assert.That(terrain.WidthCells, Is.EqualTo(cells));
            Assert.That(terrain.HeightCells, Is.EqualTo(cells));
            Assert.That(terrain.HorizontalStepCm, Is.EqualTo(6250));
            for (int row = 0; row < cells; row++)
            {
                for (int col = 0; col < cells; col++)
                {
                    LogicTerrainCell cell = terrain.GetCell(col, row);
                    Assert.That(cell.AreaId, Is.EqualTo(areaIds[row * cells + col]), $"格（{col},{row}）");
                    Assert.That(cell.HeightLevel, Is.Zero);
                    Assert.That(cell.WaterHeightLevel, Is.Zero);
                    Assert.That(cell.SurfaceFlags, Is.EqualTo(LogicTerrainSurfaceFlags.None));
                }
            }
        });
    }

    [Test]
    public void AreaIdOutOfRange_NamesCell()
    {
        var terrain = Grid(2, 100);
        terrain.SetCell(0, 0, new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.None, areaId: 2));

        var ex = Assert.Throws<InvalidOperationException>(() => Bake(terrain, TwoTypes, Sources()));

        Assert.That(ex!.Message, Is.EqualTo("格（0,0）的 AreaId = 2，超出 terrainAreas（2 项）。"));
    }

    [Test]
    public void WaterFlag_NamesCell()
    {
        var terrain = Grid(2, 100);
        terrain.SetCell(1, 0, new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.Water));

        var ex = Assert.Throws<InvalidOperationException>(() => Bake(terrain, TwoTypes, Sources()));

        Assert.That(ex!.Message, Is.EqualTo("格（1,0）的 Water 已设置。CrowdSimulation 地表不使用逻辑地形的水面标记。"));
    }

    [Test]
    public void BlockedFlag_NamesCell()
    {
        var terrain = Grid(2, 100);
        terrain.SetCell(1, 1, new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.Blocked));

        var ex = Assert.Throws<InvalidOperationException>(() => Bake(terrain, TwoTypes, Sources()));

        Assert.That(ex!.Message, Is.EqualTo("格（1,1）的 Blocked 已设置。阻挡物仍是地图实体，不写在逻辑地形格上。"));
    }

    [Test]
    public void HeightLevel_NamesCell()
    {
        var terrain = Grid(2, 100);
        terrain.SetCell(0, 1, new LogicTerrainCell(1, 0, LogicTerrainSurfaceFlags.None));

        var ex = Assert.Throws<InvalidOperationException>(() => Bake(terrain, TwoTypes, Sources()));

        Assert.That(ex!.Message, Is.EqualTo("格（0,1）的 HeightLevel = 1。CrowdSimulation 地表不使用逻辑地形的高度。"));
    }

    [Test]
    public void HeightSampleCountMismatch_NamesHeightSource()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Bake(Grid(2, 100), TwoTypes, Sources(samples: 3)));

        Assert.That(ex!.Message, Does.Contain("terrain/synthetic.height"));
        Assert.That(ex.Message, Does.Contain("采样 3×3"));
        Assert.That(ex.Message, Does.Contain("导航格 2×2"));
    }

    [Test]
    public void MapCellSizeMismatch_NamesBothSources()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Bake(Grid(2, 100), TwoTypes, Sources(crowdCellCm: 50)));

        Assert.That(ex!.Message, Does.Contain("Maps/synthetic.json"));
        Assert.That(ex.Message, Does.Contain("格边长 100 cm"));
        Assert.That(ex.Message, Does.Contain("Navigation/navmesh.json"));
        Assert.That(ex.Message, Does.Contain("50 cm"));
    }

    [Test]
    public void JumpCandidateOutOfGrid_NamesIndex()
    {
        NavSurfaceJumpCandidate jump = new()
        {
            FromX = 3,
            FromY = 0,
            ToX = 0,
            ToY = 0,
            DropCm = 100,
            LengthCells = 3,
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Bake(Grid(2, 100), TwoTypes, Sources(), new[] { jump }));

        Assert.That(ex!.Message, Does.Contain("跳跃候选 0"));
        Assert.That(ex.Message, Does.Contain("（3,0）"));
        Assert.That(ex.Message, Does.Contain("2×2"));
    }

    [Test]
    public void JumpCandidateInGrid_IsCopied()
    {
        NavSurfaceJumpCandidate jump = new()
        {
            FromX = 0,
            FromY = 0,
            ToX = 1,
            ToY = 1,
            DropCm = -40,
            LengthCells = 1.5f,
        };

        NavSurfaceAsset asset = Bake(Grid(2, 100), TwoTypes, Sources(), new[] { jump });

        Assert.That(asset.JumpCandidates, Has.Length.EqualTo(1));
        Assert.That(asset.JumpCandidates[0].FromX, Is.EqualTo(jump.FromX));
        Assert.That(asset.JumpCandidates[0].ToY, Is.EqualTo(jump.ToY));
        Assert.That(asset.JumpCandidates[0].DropCm, Is.EqualTo(jump.DropCm));
        Assert.That(asset.TerrainCells, Is.EqualTo(new byte[4]));
        Assert.That(asset.TerrainTypeIds, Is.EqualTo(TwoTypes));
    }

    [Test]
    public void S1Surfaces_RoundTripTerrainTypesAndCells()
    {
        string repo = FindRepoRoot();
        (string Name, string ModId)[] seeds =
        [
            ("s1337", "CrowdSimulationS1Terrain1337Mod"),
            ("s7", "CrowdSimulationS1Terrain7Mod"),
            ("s2024", "CrowdSimulationS1Terrain2024Mod"),
        ];

        Assert.Multiple(() =>
        {
            foreach ((string name, string modId) in seeds)
            {
                string mapId = "crowd_simulation_" + name;
                string modAssets = Path.Combine(
                    repo,
                    "mods",
                    "showcases",
                    "crowd_simulation",
                    modId,
                    "assets");
                string mapPath = Path.Combine(modAssets, "Maps", mapId + ".json");
                JsonNode map = JsonNode.Parse(File.ReadAllText(mapPath))!;
                int cellSizeCm = map["Boards"]![0]!["Grid"]!["CellSizeCm"]!.GetValue<int>();
                string binPath = Path.Combine(modAssets, "terrain", mapId + ".map_data.bin");
                string surfacePath = Path.Combine(modAssets, "terrain", mapId + ".navsurface");
                LogicTerrainField terrain = ReactGridTerrainBinary.Read(binPath, cellSizeCm);
                NavSurfaceAsset baked = CrowdNavSurfaceAuthoring.BakeAuthored(
                    repo,
                    modId,
                    mapId,
                    terrain,
                    Array.Empty<NavSurfaceJumpCandidate>());
                NavSurfaceAsset fixture;
                using (FileStream input = File.OpenRead(surfacePath))
                {
                    fixture = NavSurfaceAsset.Read(input);
                }

                Assert.That(baked.TerrainTypeIds, Is.EqualTo(fixture.TerrainTypeIds), name);
                Assert.That(baked.TerrainCells, Is.EqualTo(fixture.TerrainCells), name);
                Assert.That(baked.NavCellsX, Is.EqualTo(fixture.NavCellsX), name);
                Assert.That(baked.NavCellsY, Is.EqualTo(fixture.NavCellsY), name);
                Assert.That(baked.NavCellSizeCm, Is.EqualTo(fixture.NavCellSizeCm), name);
                Assert.That(baked.JumpCandidates, Is.Empty, name);
                Assert.That(fixture.JumpCandidates, Is.Not.Empty, name);
            }
        });
    }

    private static NavSurfaceAsset Bake(
        LogicTerrainField terrain,
        IReadOnlyList<string> terrainTypeIds,
        CrowdNavSurfaceBakeSources sources,
        IReadOnlyList<NavSurfaceJumpCandidate>? jumps = null)
        => CrowdNavSurfaceAuthoring.Bake(
            terrain,
            terrainTypeIds,
            sources,
            jumps ?? Array.Empty<NavSurfaceJumpCandidate>());

    private static SparseGridLogicTerrainField Grid(int cells, int cellCm)
        => new(cells, cells, cellCm, chunkSizeCells: 64);

    private static CrowdNavSurfaceBakeSources Sources(int samples = 2, int crowdCellCm = 100)
        => new(
            "Maps/synthetic.json",
            200,
            200,
            100,
            "terrain/synthetic.height",
            200,
            200,
            samples,
            samples,
            "Navigation/navmesh.json",
            crowdCellCm);

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
    }
}
