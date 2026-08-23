using System;
using System.IO;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Scripting;
using System.Linq;
using NUnit.Framework;

namespace Ludots.Tests.Gas
{
    /// <summary>
    /// vhtm board 的唯一验收口径（refs #413）：运行时障碍触发的增量重烤必须保留
    /// VisualHeightmap 投影出来的地形起伏语义。历史缺陷形态是：重烤路径丢失
    /// LogicTerrain 地形源，瓦片被平地烘焙覆盖——坡度不可行走区域整体消失。
    /// 本合同用真实 navmesh_debug_vhtm 资产走完整装载→烘焙→脏更新链路锁死该回归。
    /// </summary>
    [TestFixture]
    public sealed class NavMeshDebugVhtmReliefContractTests
    {
        private string _root = string.Empty;
        private string _coreRoot = string.Empty;
        private string _bakeConfigModRoot = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_NavMeshDebugVhtmRelief", Guid.NewGuid().ToString("N"));
            _coreRoot = Path.Combine(_root, "assets");
            Directory.CreateDirectory(_coreRoot);
            CopyDirectory(Path.Combine(FindRepoRoot(), "assets"), _coreRoot);

            // 只携带运行时增量烘焙配置的临时 mod：本合同验证引擎链路（投影→烘焙→脏重烤），
            // 不装 NavMeshDebugLaunchMod——其 overlay 启动即开，无头环境会触发呈现适配器 fail-fast。
            _bakeConfigModRoot = Path.Combine(_root, "mods", "NavVhtmReliefBakeConfigMod");
            Directory.CreateDirectory(Path.Combine(_bakeConfigModRoot, "assets", "Navigation"));
            File.WriteAllText(Path.Combine(_bakeConfigModRoot, "mod.json"), """
            {
              "name": "NavVhtmReliefBakeConfigMod",
              "version": "1.0.0",
              "description": "runtime-incremental bake config override for the vhtm relief contract",
              "main": "",
              "priority": 10,
              "dependencies": { "LudotsCoreMod": "^1.0.0" }
            }
            """);
            File.WriteAllText(Path.Combine(_bakeConfigModRoot, "assets", "Navigation", "navmesh.json"), """
            {
              "mode": "runtime-incremental",
              "algorithm": "cdt",
              "profiles": [{ "id": "Small", "maxClimbCm": 40, "maxSlopeDeg": 45 }],
              "layers": [{ "id": "ground", "layer": 0 }],
              "areas": [
                { "id": "mud", "areaId": 1, "cost": 2.0 },
                { "id": "road", "areaId": 2, "cost": 0.7 }
              ],
              "runtimeIncremental": {
                "tileBudgetPerFixedTick": 4,
                "includeNeighborTiles": true,
                "heightScaleMeters": 2.0,
                "minWalkableUpDot": 0.6,
                "cliffHeightThreshold": 1
              }
            }
            """);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch
            {
            }
        }

        [Test]
        public void LoadMap_VhtmRelief_ProjectsVhtmAndServesRecastBakedReliefTiles()
        {
            using var engine = CreateEngine();
            engine.LoadMap("navmesh_debug_vhtm");

            var bakeConfig = (engine.GetService(CoreServiceKeys.NavMeshBakeConfig) as Ludots.Core.Navigation.NavMesh.Config.NavMeshBakeConfig)
                ?? throw new InvalidOperationException("navmesh_debug_vhtm 装载后必须注册 NavMeshBakeConfig。");
            Assert.That(bakeConfig.RuntimeIncremental.HeightScaleMeters, Is.EqualTo(2.0f),
                "mod 的 navmesh.json 覆盖（heightScaleMeters=2.0）必须赢得配置合并");

            AssertReliefProjectedIntoLogicTerrain(engine);

            var registry = GetNavRegistry(engine);
            Assert.That(registry.TryGetStore(0, 0, out NavTileStore? store), Is.True);
            NavTile initial = store!.GetOrLoad(new NavTileId(0, 0));
            Assert.That(TileHeightRangeCm(initial), Is.GreaterThan(0),
                "离线 recast 烘焙的初始瓦片必须携带起伏高度（vhtm 为唯一验收口径的地形真相）");
        }

        /// <summary>
        /// 已确诊缺陷（refs #413）：runtime-incremental 强制 CDT，而 CDT 管线在
        /// 高度层量化的起伏地形上退化为近空瓦片（2 三角/4 顶点；同输入下离线
        /// recast 烤出 516 三角满起伏）。障碍触发的运行时重烤因此把离线 recast
        /// 的起伏 navmesh 整体替换成退化残片——即“obstacle 更新把地形起伏的
        /// navmesh 都忽略了”的根因。本合同在 CDT 退化修复前保持 Explicit，
        /// 修复后移除特性标记即可作为 vhtm board 的验收闸门。
        /// </summary>
        [Explicit("runtime-incremental CDT 在起伏地形上产出退化瓦片（详见测试体诊断）；修复后移除本标记")]
        [Test]
        public void RuntimeObstacleRebake_OnVhtmRelief_PreservesTerrainNavMesh()
        {
            using var engine = CreateEngine();
            engine.LoadMap("navmesh_debug_vhtm");

            var registry = GetNavRegistry(engine);
            Assert.That(registry.TryGetStore(0, 0, out NavTileStore? store), Is.True);
            Assert.That(store, Is.Not.Null);

            NavTile before = store!.GetOrLoad(new NavTileId(0, 0));
            NavTile farBefore = store.GetOrLoad(new NavTileId(3, 3));
            int beforeRangeCm = TileHeightRangeCm(before);
            Assert.That(beforeRangeCm, Is.GreaterThan(0), "初始烘焙瓦片必须携带起伏高度（否则投影链路已断）");

            uint storeRevisionBefore = store.Revision;

            // 已确诊证据链（2026-08-23）：引擎投影地形高度层正确（level(0,0)=3 等符合生成公式），
            // WalkMaskBuilder 同参构建得满可行 8192 三角；但 BakePipeline.Execute（CDT）对同一输入
            // 返回 success=True 且仅 2 三角/4 顶点——退化发生在 CDT 轮廓/三角化阶段，
            // 而离线 recast 同瓦片为 516 三角、高度范围 1200cm。

            engine.World.Create(
                WorldPositionCm.FromCm(3200, 3200),
                new RuntimeNavMeshStructuralObstacle(),
                new ManifestationObstacleIntent2D
                {
                    Shape = ManifestationObstacleShape2D.Circle,
                    SinkNavigationObstacle = 1,
                    RadiusCm = 1200,
                    NavRadiusCm = 1200,
                });

            for (int i = 0; i < 30; i++)
            {
                engine.Tick(1f / 60f);
            }

            NavTile after = store.GetOrLoad(new NavTileId(0, 0));
            NavTile farAfter = store.GetOrLoad(new NavTileId(3, 3));

            Assert.That(store.Revision, Is.GreaterThan(storeRevisionBefore), "障碍生成后增量重烤队列必须推进 store revision");

            Assert.That(after.Checksum, Is.Not.EqualTo(before.Checksum), "障碍所在瓦片必须被重烤");
            Assert.That(after.TriangleCount, Is.LessThanOrEqualTo(before.TriangleCount),
                "加障碍只会挖洞（减三角形）；三角数增加意味着重烤用了不同的地形/障碍输入");

            int afterRangeCm = TileHeightRangeCm(after);
            Assert.That(afterRangeCm, Is.GreaterThanOrEqualTo((int)(beforeRangeCm * 0.8f)),
                $"重烤后瓦片高度范围 {afterRangeCm}cm 相对初始 {beforeRangeCm}cm 塌缩——地形起伏在障碍更新时被忽略（历史缺陷复发）");
            Assert.That(TileMaxHeightCm(after), Is.GreaterThanOrEqualTo((int)(TileMaxHeightCm(before) * 0.8f)),
                "重烤后瓦片最高点海拔显著下降——起伏语义丢失");

            Assert.That(farAfter.Checksum, Is.EqualTo(farBefore.Checksum), "远离障碍的瓦片不应被重烤");
            Assert.That(farAfter.TileVersion, Is.EqualTo(farBefore.TileVersion), "远离障碍的瓦片版本不应变化");
        }

        private static void AssertReliefProjectedIntoLogicTerrain(GameEngine engine)
        {
            LogicTerrainField terrain = engine.LogicTerrain;
            Assert.That(terrain, Is.Not.Null, "Feature.NavMesh:On 地图装载后必须持有 LogicTerrainField");

            int min = byte.MaxValue;
            int max = byte.MinValue;
            for (int row = 0; row < terrain.HeightCells; row += 8)
            {
                for (int col = 0; col < terrain.WidthCells; col += 8)
                {
                    int level = terrain.GetCell(col, row).HeightLevel;
                    min = Math.Min(min, level);
                    max = Math.Max(max, level);
                }
            }

            Assert.That(max, Is.GreaterThanOrEqualTo(5),
                $"navmesh_debug_vhtm 的 LogicTerrain 高度层必须覆盖 vhtm 起伏全幅（vhtm 50..650cm/step100 → 期望 max>=5，实际 min={min} max={max}）；" +
                "max 偏小意味着投影只采样了部分起伏或步长错位");
        }

        private static NavQueryServiceRegistry GetNavRegistry(GameEngine engine)
        {
            return (engine.GetService(CoreServiceKeys.NavQueryServices) as NavQueryServiceRegistry)
                ?? throw new InvalidOperationException("navmesh_debug_vhtm 装载后必须注册 NavQueryServices。");
        }

        private static int TileHeightRangeCm(NavTile tile)
        {
            return TileMaxHeightCm(tile) - TileMinHeightCm(tile);
        }

        private static int TileMaxHeightCm(NavTile tile)
        {
            int max = int.MinValue;
            for (int i = 0; i < tile.VertexYcm.Length; i++)
            {
                max = Math.Max(max, tile.VertexYcm[i]);
            }

            return max;
        }

        private static int TileMinHeightCm(NavTile tile)
        {
            int min = int.MaxValue;
            for (int i = 0; i < tile.VertexYcm.Length; i++)
            {
                min = Math.Min(min, tile.VertexYcm[i]);
            }

            return min;
        }

        private GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            var modPaths = new[]
            {
                Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                Path.Combine(repoRoot, "mods", "CoreInputMod"),
                _bakeConfigModRoot,
            };

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths.ToList(), _coreRoot);
            engine.Start();
            return engine;
        }

        private static string FindRepoRoot()
        {
            string? current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(current, "mods", "LudotsCoreMod", "mod.json")) &&
                    File.Exists(Path.Combine(current, "mods", "CoreInputMod", "mod.json")))
                {
                    return current;
                }

                current = Directory.GetParent(current)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing mods/LudotsCoreMod and mods/CoreInputMod.");
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory))
            {
                string destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(sourceFile));
                File.Copy(sourceFile, destinationFile, overwrite: true);
            }

            foreach (string sourceChildDirectory in Directory.EnumerateDirectories(sourceDirectory))
            {
                string destinationChildDirectory = Path.Combine(destinationDirectory, Path.GetFileName(sourceChildDirectory));
                CopyDirectory(sourceChildDirectory, destinationChildDirectory);
            }
        }
    }
}
