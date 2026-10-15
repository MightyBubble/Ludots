using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Navigation.NavMesh.Config;
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

        Assert.That(ex!.Message, Is.EqualTo("格（0,0）的 AreaId = 2，超出 terrainTypes（2 项）。"));
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
    public void TerrainStep_MustMatchCrowdCell()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Bake(Grid(2, 100), TwoTypes, Sources(crowdCellCm: 50)));

        Assert.That(ex!.Message, Does.Contain("Maps/synthetic.json"));
        Assert.That(ex.Message, Does.Contain("步长 100×100 cm"));
        Assert.That(ex.Message, Does.Contain("Navigation/navmesh.json"));
        Assert.That(ex.Message, Does.Contain("50 cm"));
    }

    [Test]
    public void LogicTerrainAsset_RejectsParentSegment()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            NavMeshBakeConfigLoader.RequireModRelativeAsset("../terrain/x.bin", "Navigation/navmesh.json: maps.m.logicTerrainAsset"));

        Assert.That(ex!.Message, Does.Contain(".."));
    }

    [Test]
    public void Rebake_ReplacesExistingTarget_EvenWithFewerLinkCandidates()
    {
        string path = Path.Combine(Path.GetTempPath(), "ludots-jump-" + Guid.NewGuid().ToString("N") + ".navsurface");
        try
        {
            NavSurfaceAsset baked = Bake(Grid(2, 100), TwoTypes, Sources());
            NavSurfaceAsset existing = new()
            {
                NavCellsX = baked.NavCellsX,
                NavCellsY = baked.NavCellsY,
                NavCellSizeCm = baked.NavCellSizeCm,
                TerrainTypeIds = baked.TerrainTypeIds,
                LinkKindIds = ["jump"],
                BakeInputsDigest = baked.BakeInputsDigest,
                TerrainCells = baked.TerrainCells,
                LinkCandidates =
                [
                    new NavSurfaceLinkCandidate
                    {
                        Steps = 1,
                        Diagonal = true,
                        FromX = 0,
                        FromY = 0,
                        ToX = 1,
                        ToY = 1,
                        DropCm = 10,
                    },
                ],
            };
            CrowdNavSurfaceAuthoring.WriteNavSurface(path, existing);

            CrowdNavSurfaceAuthoring.WriteNavSurface(path, baked);

            using FileStream replaced = File.OpenRead(path);
            Assert.That(NavSurfaceAsset.Read(replaced).LinkCandidates, Is.Empty);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
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
                JsonNode board = map["Boards"]![0]!;
                Assert.That(board["DataFile"], Is.Null, name);
                Assert.That(board["Grid"]!["CellSizeCm"]!.GetValue<int>(), Is.EqualTo(100), name);
                string surfacePath = Path.Combine(modAssets, "terrain", mapId + ".navsurface");
                NavSurfaceAsset fixture;
                using (FileStream input = File.OpenRead(surfacePath))
                {
                    fixture = NavSurfaceAsset.Read(input);
                }

                string navPath = Path.Combine(modAssets, "Navigation", "navmesh.json");
                JsonNode nav = JsonNode.Parse(File.ReadAllText(navPath))!;
                string logicTerrainAsset = nav["maps"]![mapId]!["logicTerrainAsset"]!.GetValue<string>();
                Assert.That(logicTerrainAsset, Is.EqualTo("terrain/" + mapId + ".map_data.bin"), name);
                string binPath = Path.Combine(modAssets, logicTerrainAsset);
                LogicTerrainField terrain = ReactGridTerrainBinary.Read(binPath, fixture.NavCellSizeCm);
                NavSurfaceAsset baked = CrowdNavSurfaceAuthoring.BakeAuthored(
                    repo,
                    modId,
                    mapId,
                    terrain);
                Assert.That(baked.TerrainTypeIds, Is.EqualTo(fixture.TerrainTypeIds), name);
                Assert.That(baked.TerrainCells, Is.EqualTo(fixture.TerrainCells), name);
                Assert.That(baked.NavCellsX, Is.EqualTo(fixture.NavCellsX), name);
                Assert.That(baked.NavCellsY, Is.EqualTo(fixture.NavCellsY), name);
                Assert.That(baked.NavCellSizeCm, Is.EqualTo(fixture.NavCellSizeCm), name);
                Assert.That(baked.LinkKindIds, Is.EqualTo(fixture.LinkKindIds), name);
                Assert.That(baked.BakeInputsDigest, Is.EqualTo(fixture.BakeInputsDigest), name);
                Assert.That(baked.LinkCandidates, Is.EqualTo(fixture.LinkCandidates), name);
                Assert.That(fixture.LinkCandidates, Is.Not.Empty, name);
            }
        });
    }

    private static NavSurfaceAsset Bake(
        LogicTerrainField terrain,
        IReadOnlyList<string> terrainTypeIds,
        CrowdNavSurfaceBakeSources sources)
        => CrowdNavSurfaceAuthoring.Bake(terrain, terrainTypeIds, sources, linkCandidateBake: null);

    private static SparseGridLogicTerrainField Grid(int cells, int cellCm)
        => new(cells, cells, cellCm, chunkSizeCells: 64);

    private static CrowdNavSurfaceBakeSources Sources(int samples = 2, int crowdCellCm = 100)
        => new(
            "Maps/synthetic.json",
            200,
            200,
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
