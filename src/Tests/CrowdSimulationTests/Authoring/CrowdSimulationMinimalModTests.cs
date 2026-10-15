using System.Text;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Tool;
using NUnit.Framework;

namespace CrowdSimulationTests.Authoring;

/// <summary>
/// 从空目录写出来的最小群体 Mod，走真实的 Mod / 地图加载。
/// 两名玩家各 200 个步兵，从墙西侧绕到墙东侧；墙南是水，步兵只能从北端绕。
/// 每 tick 查所有单位不在阻挡格、不在水格；到墙东的单位必须曾越过墙的南北端。
/// 2000 tick 内至少 95% 到达墙东且处于 Arrived。
/// 散开度按玩家算：到达点最近两人距离不小于两倍单位半径，包围半径（离质心最远者）不小于 半径 × √到达人数。
/// 马达贴墙滑动只去掉最后一个接触法线方向的速度（和 Web 参考同一算法）。墙南端与水面夹出的凹角里，
/// 朝水的速度一直留着，被人群推进角里的单位出不来，所以到达比例不要求 100%。
/// </summary>
[TestFixture]
public sealed class CrowdSimulationMinimalModTests
{
    private const string ModId = "CrowdSimulationMinimalMod";
    private const string MapId = "crowd_minimal";
    private const int UnitsPerPlayer = 200;
    private const int Players = 2;
    private const int TickBudget = 2000;
    private const double MinArrivedFraction = 0.95;
    private const int UnitRadiusCm = 150;
    private const int WallCol = 128;
    private const int WallMinRow = 112;
    private const int WallMaxRow = 142;
    private const string Water = "water";

    [Test]
    public void StoredNavSurface_IsFreshLudotsBake()
    {
        string repo = RepoRoot();
        NavMeshBakeConfigContext ctx = NavMeshBakeConfigLoader.LoadContextFromRepoRoot(repo, ModId);
        string modAssets = ModAssets(repo);
        NavSurfaceAsset stored = ReadStoredSurface(modAssets);
        LogicTerrainField terrain = ReactGridTerrainBinary.Read(
            Path.Combine(modAssets, ctx.Config.Maps[MapId].LogicTerrainAsset),
            stored.NavCellSizeCm);
        NavSurfaceAsset baked = CrowdNavSurfaceAuthoring.BakeAuthored(repo, ModId, MapId, terrain);

        Assert.Multiple(() =>
        {
            Assert.That(baked.NavCellsX, Is.EqualTo(stored.NavCellsX));
            Assert.That(baked.NavCellsY, Is.EqualTo(stored.NavCellsY));
            Assert.That(baked.NavCellSizeCm, Is.EqualTo(stored.NavCellSizeCm));
            Assert.That(baked.TerrainTypeIds, Is.EqualTo(stored.TerrainTypeIds));
            Assert.That(baked.TerrainCells, Is.EqualTo(stored.TerrainCells),
                $"入库的 {MapId}.navsurface 不是当前 Ludots 烘焙结果，重跑 Ludots.Tool nav bake-crowd-navsurface。");
            Assert.That(baked.JumpCandidates, Is.Empty);
            Assert.That(stored.JumpCandidates, Is.Empty);
        });
    }

    [Test]
    public void TwoPlayers_GoAroundTheWall_StayOffBlockedAndWater_AndArriveSpread()
    {
        string repo = RepoRoot();
        NavSurfaceAsset surface = ReadStoredSurface(ModAssets(repo));
        int water = Array.IndexOf(surface.TerrainTypeIds, Water);
        Assert.That(water, Is.GreaterThanOrEqualTo(0), "地表没有 water 类型。");

        using GameEngine engine = StartEngine(repo);
        CrowdSimSession session = engine.GetService(CoreServiceKeys.CrowdSimulationSession)
            ?? throw new InvalidOperationException("没有群体会话。");
        Assert.That(engine.GetService(CoreServiceKeys.FogDecalPalette), Is.Not.Null, "群体会话没有发布迷雾贴花调色板。");
        int n = session.Config.NavCellsPerSide;
        int cellCm = session.Config.NavCellSizeCm;
        Assert.That(n, Is.EqualTo(256));
        byte[] blocked = session.Structures?.Blocked ?? throw new InvalidOperationException("会话没有结构仓。");
        for (int row = WallMinRow; row <= WallMaxRow; row++)
        {
            Assert.That(blocked[row * n + WallCol], Is.Not.Zero, $"墙格 ({WallCol},{row}) 没被阻挡。");
        }

        var violations = new StringBuilder();
        int violationCount = 0;
        int guard = 0;
        int[] maxRow = Array.Empty<int>();
        int[] minRow = Array.Empty<int>();
        while (session.TickCount < TickBudget && guard++ < TickBudget * 4)
        {
            engine.Tick(1f / 30f);
            int count = session.Units.Count;
            if (count == 0) continue;
            if (maxRow.Length != count)
            {
                Assert.That(count, Is.EqualTo(UnitsPerPlayer * Players), $"tick {session.TickCount} 单位数");
                maxRow = new int[count];
                minRow = new int[count];
                Array.Fill(maxRow, int.MinValue);
                Array.Fill(minRow, int.MaxValue);
            }

            for (int i = 0; i < count; i++)
            {
                Entity entity = session.Units.EntityAt(i);
                Fix64Vec2 p = session.World.Get<WorldPositionCm>(entity).Value;
                int col = p.X.ToInt() / cellCm;
                int row = p.Y.ToInt() / cellCm;
                int cell = row * n + col;
                maxRow[i] = Math.Max(maxRow[i], row);
                minRow[i] = Math.Min(minRow[i], row);
                bool onBlocked = blocked[cell] != 0;
                bool inWater = surface.TerrainCells[cell] == water;
                if ((onBlocked || inWater) && violationCount++ < 10)
                {
                    violations.AppendLine(
                        $"tick {session.TickCount} 单位 {i} 在格 ({col},{row})：{(onBlocked ? "阻挡" : "水面")}");
                }
            }
        }

        Assert.That(session.TickCount, Is.GreaterThanOrEqualTo(TickBudget), "会话没跑到 tick 预算。");
        Assert.That(violationCount, Is.Zero, violations.ToString());

        var arrivedByPlayer = new List<Fix64Vec2>[Players];
        for (int p = 0; p < Players; p++) arrivedByPlayer[p] = new List<Fix64Vec2>();
        int arrived = 0;
        int crossedWithoutDetour = 0;
        for (int i = 0; i < session.Units.Count; i++)
        {
            Entity entity = session.Units.EntityAt(i);
            Fix64Vec2 p = session.World.Get<WorldPositionCm>(entity).Value;
            var state = session.World.Get<CrowdSimulationUnitState>(entity);
            int col = p.X.ToInt() / cellCm;
            if (col <= WallCol) continue;
            if (maxRow[i] <= WallMaxRow && minRow[i] >= WallMinRow) crossedWithoutDetour++;
            if (state.State != (byte)CrowdUnitState.Arrived) continue;
            arrived++;
            int player = session.Groups.Groups[state.GroupId]!.Player;
            arrivedByPlayer[player].Add(p);
        }

        int total = UnitsPerPlayer * Players;
        double fraction = arrived / (double)total;
        TestContext.Out.WriteLine($"ticks={session.TickCount} arrived={arrived}/{total} ({fraction:P1})");
        Assert.That(crossedWithoutDetour, Is.Zero, "有单位到了墙东却没绕过墙端（穿墙）。");
        Assert.That(fraction, Is.GreaterThanOrEqualTo(MinArrivedFraction),
            $"tick {TickBudget} 内到达墙东并处于 Arrived 的比例 {fraction:P1}，要求 ≥ {MinArrivedFraction:P0}。");

        for (int p = 0; p < Players; p++)
        {
            List<Fix64Vec2> points = arrivedByPlayer[p];
            Assert.That(points.Count, Is.GreaterThan(1), $"玩家 {p + 1} 到达不足两人。");
            (double minPair, double bounding) = Spread(points);
            double packedRadius = UnitRadiusCm * Math.Sqrt(points.Count);
            TestContext.Out.WriteLine(
                $"player={p + 1} arrived={points.Count} minPairCm={minPair:F0} boundingRadiusCm={bounding:F0} packedRadiusCm={packedRadius:F0}");
            Assert.That(minPair, Is.GreaterThanOrEqualTo(2 * UnitRadiusCm),
                $"玩家 {p + 1} 到达点最近两人相距 {minPair:F0} cm，小于两倍单位半径 {2 * UnitRadiusCm} cm（身体重叠）。");
            Assert.That(bounding, Is.GreaterThanOrEqualTo(packedRadius),
                $"玩家 {p + 1} 到达点包围半径 {bounding:F0} cm，小于 {points.Count} 个半径 {UnitRadiusCm} cm 的圆紧密排列所需的 {packedRadius:F0} cm。");
        }
    }

    private static (double MinPair, double Bounding) Spread(List<Fix64Vec2> points)
    {
        double cx = 0, cy = 0;
        for (int i = 0; i < points.Count; i++)
        {
            cx += points[i].X.ToDouble();
            cy += points[i].Y.ToDouble();
        }

        cx /= points.Count;
        cy /= points.Count;
        double minPair = double.PositiveInfinity;
        double bounding = 0;
        for (int i = 0; i < points.Count; i++)
        {
            double xi = points[i].X.ToDouble(), yi = points[i].Y.ToDouble();
            bounding = Math.Max(bounding, Math.Sqrt((xi - cx) * (xi - cx) + (yi - cy) * (yi - cy)));
            for (int j = i + 1; j < points.Count; j++)
            {
                double dx = xi - points[j].X.ToDouble(), dy = yi - points[j].Y.ToDouble();
                minPair = Math.Min(minPair, Math.Sqrt(dx * dx + dy * dy));
            }
        }

        return (minPair, bounding);
    }

    private static GameEngine StartEngine(string repo)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(new List<string>
        {
            Path.Combine(repo, "mods", "LudotsCoreMod"),
            Path.Combine(repo, "mods", "capabilities", "navigation", "CrowdSimulationMod"),
            Path.Combine(repo, "mods", "capabilities", "navigation", "CrowdSimulationStandardMod"),
            Path.Combine(repo, "mods", "showcases", "crowd_simulation", ModId),
        }, Path.Combine(repo, "assets"));
        engine.SetService(CoreServiceKeys.CameraBehaviorInputState, new Ludots.Core.Gameplay.Camera.CameraBehaviorInputState());
        engine.SetService(CoreServiceKeys.ViewController, new StubView());
        engine.Start();
        engine.LoadMap(MapId);
        return engine;
    }

    private static NavSurfaceAsset ReadStoredSurface(string modAssets)
    {
        using FileStream input = File.OpenRead(Path.Combine(modAssets, "terrain", MapId + ".navsurface"));
        return NavSurfaceAsset.Read(input);
    }

    private static string ModAssets(string repo)
        => Path.Combine(repo, "mods", "showcases", "crowd_simulation", ModId, "assets");

    private sealed class StubView : Ludots.Core.Presentation.Camera.IViewController
    {
        public System.Numerics.Vector2 Resolution { get; } = new(1280, 720);
        public float Fov { get; } = 50f;
        public float AspectRatio { get; } = 1280f / 720f;
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mods")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到仓库根(向上查找 mods 目录失败)");
    }
}
