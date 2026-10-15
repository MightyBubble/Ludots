using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Presentation.Terrain;
using Ludots.Tool;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests;

[TestFixture]
public class CrowdNavSurfaceWalkabilityTests
{
    [Test]
    public void LandPaintedToWater_FlipsWalkersWithoutJump()
    {
        const string seed = "s1337";
        var (runtime, before) = S4DeployTruthTests.BuildSession(seed);
        int side = runtime.NavCellsPerSide;
        int land = TerrainIndex(runtime, "land");
        int water = TerrainIndex(runtime, "water");
        int landArea = runtime.TerrainToArea[land];
        int waterArea = runtime.TerrainToArea[water];
        var walkerNavIds = WalkerNavIds(runtime, before, landArea, waterArea);
        Assert.That(walkerNavIds, Is.Not.Empty, "没有不带跳跃、陆地可走、水不可走的移动类型。");

        NavSurfaceAsset fixture = S1SurfaceTruthTests.ReadSurface(seed);
        int cell = FindPassableLandCell(fixture, land, before, walkerNavIds);
        Assert.That(cell, Is.GreaterThanOrEqualTo(0), "没有当前可通行的 land 格。");

        var passableBefore = new Dictionary<int, byte[]>(walkerNavIds.Count);
        foreach (int id in walkerNavIds)
        {
            passableBefore[id] = (byte[])before.Navs[id].Passable.Clone();
            Assert.That(passableBefore[id][cell], Is.EqualTo(1), $"nav {id} 在改格前不可通行。");
        }

        string seedDir = S1SurfaceTruthTests.SeedDir(seed);
        string mapPath = Path.Combine(seedDir, "Maps", $"crowd_simulation_{seed}.json");
        JsonNode map = JsonNode.Parse(File.ReadAllText(mapPath))!;
        JsonNode board = map["Boards"]![0]!;
        int cellSizeCm = board["Grid"]!["CellSizeCm"]!.GetValue<int>();
        Assert.That(cellSizeCm, Is.EqualTo(100));
        Assert.That(board["DataFile"], Is.Null);
        string heightAsset = map["ContinuousHeightmap"]!["Asset"]!.GetValue<string>();
        ContinuousHeightmapAsset heightmap;
        using (FileStream heightStream = File.OpenRead(Path.Combine(seedDir, heightAsset["assets/".Length..])))
        {
            heightmap = ContinuousHeightmapBinary.Read(heightStream);
        }

        string binPath = Path.Combine(seedDir, "terrain", $"crowd_simulation_{seed}.map_data.bin");
        var terrain = (SparseGridLogicTerrainField)ReactGridTerrainBinary.Read(binPath, runtime.NavCellSizeCm);
        int col = cell % side;
        int row = cell / side;
        LogicTerrainCell painted = terrain.GetCell(col, row);
        Assert.That(painted.AreaId, Is.EqualTo((byte)land));
        terrain.SetCell(col, row, new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.None, (byte)water));

        var sources = new CrowdNavSurfaceBakeSources(
            $"Maps/crowd_simulation_{seed}.json",
            board["WidthCm"]!.GetValue<int>(),
            board["HeightCm"]!.GetValue<int>(),
            heightAsset.Replace('\\', '/'),
            heightmap.Bounds.Width,
            heightmap.Bounds.Height,
            heightmap.SampleColumns,
            heightmap.SampleRows,
            "Navigation/navmesh.json",
            runtime.NavCellSizeCm);
        NavSurfaceAsset baked = CrowdNavSurfaceAuthoring.Bake(
            terrain,
            runtime.TerrainTypeIds,
            sources,
            linkCandidateBake: null);

        Assert.That(baked.JumpCandidates, Is.Empty);
        Assert.That(baked.TerrainTypeIds, Is.EqualTo(fixture.TerrainTypeIds.ToArray()));
        int cellDiffs = 0;
        for (int i = 0; i < fixture.TerrainCells.Length; i++)
        {
            byte expected = i == cell ? (byte)water : fixture.TerrainCells[i];
            if (baked.TerrainCells[i] != expected)
            {
                cellDiffs++;
            }
        }

        Assert.That(cellDiffs, Is.Zero);
        Assert.That(baked.TerrainCells[cell], Is.EqualTo((byte)water));

        var (_, after) = S4DeployTruthTests.BuildSession(seed, baked);
        foreach (int id in walkerNavIds)
        {
            byte[] passable = after.Navs[id].Passable;
            Assert.That(passable[cell], Is.EqualTo(0), $"nav {id} 改成 water 后仍可通行。");
            int changed = 0;
            int changedAt = -1;
            byte[] prior = passableBefore[id];
            for (int i = 0; i < passable.Length; i++)
            {
                if (passable[i] == prior[i]) continue;
                changed++;
                changedAt = i;
            }

            Assert.That(changed, Is.EqualTo(1), $"nav {id} 有 {changed} 格可通行性变化，最后一格 {changedAt}。");
            Assert.That(changedAt, Is.EqualTo(cell));
        }
    }

    private static List<int> WalkerNavIds(
        CrowdSimulationRuntimeConfig runtime,
        CrowdSimSession session,
        int landArea,
        int waterArea)
    {
        var ids = new List<int>();
        foreach (RuntimeAgentType agent in runtime.AgentTypes)
        {
            RuntimeNavProfile profile = runtime.NavProfiles[agent.NavProfileIndex];
            if (profile.Jump != null) continue;
            if (agent.CostByArea[landArea] <= Fix64.Zero) continue;
            if (agent.CostByArea[waterArea] != Fix64.Zero) continue;
            foreach (NavContext nav in session.Navs.Values)
            {
                if (nav.LayerIndex == agent.AgentTypeIndex)
                {
                    ids.Add(nav.Id);
                }
            }
        }

        return ids;
    }

    private static int FindPassableLandCell(
        NavSurfaceAsset surface,
        int land,
        CrowdSimSession session,
        List<int> walkerNavIds)
    {
        for (int i = 0; i < surface.TerrainCells.Length; i++)
        {
            if (surface.TerrainCells[i] != land) continue;
            bool passable = true;
            foreach (int id in walkerNavIds)
            {
                if (session.Navs[id].Passable[i] == 0)
                {
                    passable = false;
                    break;
                }
            }

            if (passable) return i;
        }

        return -1;
    }

    private static int TerrainIndex(CrowdSimulationRuntimeConfig runtime, string terrainId)
    {
        for (int i = 0; i < runtime.TerrainTypeIds.Count; i++)
        {
            if (string.Equals(runtime.TerrainTypeIds[i], terrainId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new InvalidOperationException($"terrainTypes 没有 {terrainId}。");
    }
}
