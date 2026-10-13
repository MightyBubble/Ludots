using System.Collections.Generic;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Map.Board;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 两座区域都是 deck、彼此不相连的桥。四条陆地被水隔开，桥 A 只接 1–2，桥 B 只接 3–4。
/// 按区域并成一个桥面节点时，桥 A 的桥面会到达桥 B 的对岸；按桥面 4 连通则不能。
/// </summary>
[TestFixture]
public class CrowdDeckComponentTests
{
    [Test]
    public void DisconnectedSameAreaBridges_AreNotMutuallyReachable()
    {
        var world = StripWorld.Bake();
        var nav = world.Nav;
        int side = world.Side;
        int deckA = Cell(2, StripWorld.BridgeRow, side);
        int deckA2 = Cell(3, StripWorld.BridgeRow, side);
        int deckB = Cell(10, StripWorld.BridgeRow, side);
        int land1 = Cell(0, 4, side);
        int land2 = Cell(5, 4, side);
        int land4 = Cell(13, 4, side);

        Assert.That(nav.UpPass[deckA], Is.EqualTo(1), "桥 A 的水面格没有桥面");
        Assert.That(nav.UpPass[deckB], Is.EqualTo(1), "桥 B 的水面格没有桥面");
        Assert.That(nav.UpComp![deckA], Is.EqualTo(nav.UpComp[deckA2]));
        Assert.That(nav.UpComp[deckA], Is.Not.EqualTo(nav.UpComp[deckB]));
        Assert.That(nav.UpComp[deckA], Is.GreaterThanOrEqualTo(nav.CompCount));
        Assert.That(nav.UpComp[deckB], Is.EqualTo(nav.UpComp[deckA] + 1));
        Assert.That(nav.CompCountTotal, Is.EqualTo(nav.CompCount + 2));
        Assert.That(nav.Comp[land1], Is.EqualTo(nav.Comp[land2]));
        Assert.That(nav.Comp[land1], Is.Not.EqualTo(nav.Comp[land4]));

        Assert.That(nav.CanReach(nav.Comp[land1], nav.Comp[land2]), Is.True);
        Assert.That(nav.CanReach(nav.UpComp[deckA], nav.Comp[land1]), Is.True);
        Assert.That(nav.CanReach(nav.UpComp[deckA], nav.Comp[land4]), Is.False);
        Assert.That(nav.CanReach(nav.Comp[land1], nav.Comp[land4]), Is.False);
        Assert.That(nav.CanReach(nav.UpComp[deckA], nav.UpComp[deckB]), Is.False);
        Assert.That(nav.CanReach(nav.UpComp[deckB], nav.Comp[land4]), Is.True);
        Assert.That(nav.CanReach(nav.UpComp[deckB], nav.Comp[land1]), Is.False);
    }

    [Test]
    public void IncrementalApproachRebake_MatchesFullBakePartition()
    {
        var world = StripWorld.Bake();
        var nav = world.Nav;
        int side = world.Side;
        int approach = Cell(1, StripWorld.BridgeRow, side);
        int far = Cell(5, 4, side);
        int deckA = Cell(2, StripWorld.BridgeRow, side);
        int deckB = Cell(10, StripWorld.BridgeRow, side);
        Assert.That(nav.CanReach(nav.Comp[approach], nav.Comp[far]), Is.True);

        int building = world.Store.TemplateIndexOf("building");
        var footprint = CrowdStructureFootprint.Make(
            world.Config.Structures.Templates[building],
            Fix64.FromInt(Center(1, world.Cell)),
            Fix64.FromInt(Center(StripWorld.BridgeRow, world.Cell)),
            Fix64.FromInt(world.Cell),
            Fix64.Zero,
            Fix64.Zero);
        var (_, rect) = world.Store.PlaceEntity(building, footprint, -1);
        CrowdNavRebake.RebakeNav(nav, world.Sources, world.Store, rect, world.Cache);
        CrowdNavRebake.VerifyAgainstFullBake(world.Navs, world.Sources, world.Store, tick: 0);

        Assert.That(nav.Passable[approach], Is.EqualTo(0));
        Assert.That(nav.CanReach(nav.Comp[Cell(0, 4, side)], nav.Comp[far]), Is.False);
        Assert.That(nav.UpComp![deckA], Is.Not.EqualTo(nav.UpComp[deckB]));
        Assert.That(nav.CanReach(nav.UpComp[deckA], nav.Comp[Cell(13, 4, side)]), Is.False);

        var removed = world.Store.RemoveEntity(world.Store.EntityIds[^1]);
        Assert.That(removed, Is.Not.Null);
        CrowdNavRebake.RebakeNav(nav, world.Sources, world.Store, removed!.Value.Rect, world.Cache);
        CrowdNavRebake.VerifyAgainstFullBake(world.Navs, world.Sources, world.Store, tick: 0);
        Assert.That(nav.CanReach(nav.Comp[approach], nav.Comp[far]), Is.True);
        Assert.That(nav.UpComp![deckA], Is.Not.EqualTo(nav.UpComp[deckB]));
    }

    private static int Cell(int x, int y, int side) => y * side + x;

    private static int Center(int cell, int size) => cell * size + size / 2;

    private static class StripWorld
    {
        public const int BridgeRow = 8;

        public static Baked Bake()
        {
            var map = TestDefaults.DemoMap();
            const int boardCm = 100_000;
            map.Boards[0] = new BoardConfig { WidthCm = boardCm, HeightCm = boardCm };
            var config = TestDefaults.Assemble(map: map, pairRules: TestDefaults.DemoPairRules());
            int side = config.NavCellsPerSide;
            int cell = config.NavCellSizeCm;
            Assert.That(side, Is.EqualTo(16));
            Assert.That(cell, Is.EqualTo(6250));

            int land = IndexOf(config.TerrainTypeIds, "land");
            int water = IndexOf(config.TerrainTypeIds, "water");
            int n2 = side * side;
            var terrain = new byte[n2];
            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    bool dry = x is >= 0 and <= 1 or >= 4 and <= 5 or >= 8 and <= 9 or >= 12 and <= 13;
                    terrain[y * side + x] = (byte)(dry ? land : water);
                }
            }

            var surface = SurfaceGrid.Build(config, new NavSurfaceAsset
            {
                NavCellsX = side,
                NavCellsY = side,
                NavCellSizeCm = cell,
                TerrainTypeIds = CopyIds(config.TerrainTypeIds),
                TerrainCells = terrain,
                JumpCandidates = System.Array.Empty<NavSurfaceJumpCandidate>(),
            }, System.Array.Empty<BlockerFootprint>());

            var bridges = new List<BridgeDeckRecord>
            {
                Bridge(1, 4, cell),
                Bridge(9, 12, cell),
            };
            var deck = UpperLayerBake.RasterizeDecks(bridges, config);
            var heights = new NavHeightField
            {
                NavCellsPerSide = side,
                NavCellSizeCm = cell,
                HeightsCm = new Fix64[n2],
                Slope = new Fix64[n2],
            };
            var cache = new NavTileCache(
                config.NavtileCacheCapacity, config.Hpa.NavCellsPerTile,
                config.Navmesh.MinRegionNavCellArea.ToDouble(),
                config.Navmesh.MaxSimplificationErrorNavCells.ToDouble(),
                config.Navmesh.MaxEdgeLenNavCells.ToDouble(),
                config.Navmesh.MaxVertsPerPoly);
            int foot = IndexOfAgent(config, "foot");
            int clearance = ClearanceOf(config, "foot_r100");
            Assert.That(clearance, Is.EqualTo(1));
            var nav = NavContextBaker.Bake(config, surface, heights, deck, foot, clearance, cache);
            var store = CrowdStructuresStore.Build(config, surface, System.Array.Empty<BlockerFootprint>());
            int bridgeTpl = store.TemplateIndexOf("bridge");
            store.InstallMapBridges(bridgeTpl, new[]
            {
                bridges[0].Span,
                bridges[1].Span,
            });
            var navs = new Dictionary<int, NavContext> { [nav.Id] = nav };
            var sources = new CrowdRebakeSources(config, heights, deck, surface.JumpCandidates);
            return new Baked(config, store, nav, navs, sources, cache, side, cell);
        }

        private static BridgeDeckRecord Bridge(int x0, int x1, int cell) => new(
            new CrowdSimulationBridgeSpan
            {
                X0Cm = Center(x0, cell),
                Y0Cm = Center(BridgeRow, cell),
                X1Cm = Center(x1, cell),
                Y1Cm = Center(BridgeRow, cell),
                WidthCm = cell,
            },
            "deck");

        private static int ClearanceOf(CrowdSimulationRuntimeConfig config, string profileId)
        {
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                if (config.Profiles[i].Id == profileId) return config.Profiles[i].ClearanceNavCells;
            }

            throw new System.InvalidOperationException($"没有档案 {profileId}");
        }

        private static int IndexOfAgent(CrowdSimulationRuntimeConfig config, string id)
        {
            for (int i = 0; i < config.AgentTypes.Count; i++)
            {
                if (config.AgentTypes[i].Id == id) return i;
            }

            throw new System.InvalidOperationException($"没有移动类型 {id}");
        }

        private static int IndexOf(IReadOnlyList<string> ids, string id)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id) return i;
            }

            throw new System.InvalidOperationException($"没有地形 {id}");
        }

        private static string[] CopyIds(IReadOnlyList<string> ids)
        {
            var copy = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++) copy[i] = ids[i];
            return copy;
        }
    }

    private sealed record Baked(
        CrowdSimulationRuntimeConfig Config,
        CrowdStructuresStore Store,
        NavContext Nav,
        Dictionary<int, NavContext> Navs,
        CrowdRebakeSources Sources,
        NavTileCache Cache,
        int Side,
        int Cell);
}
