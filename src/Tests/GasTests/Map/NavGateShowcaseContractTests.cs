using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Scripting;
using NUnit.Framework;
using Arch.Core;

namespace Ludots.Tests.Gas
{
    /// <summary>
    /// NavGate showcase（refs #413 运行时更新可读演示）的引擎级合同：
    /// 时间线落门 → 增量重烤推进 store revision → navmesh 路径真实绕开城门 →
    /// 小队抵达 B 营；冻结队列（消融）→ revision 停滞且路径仍穿门。
    /// </summary>
    [TestFixture]
    public sealed class NavGateShowcaseContractTests
    {
        private string _root = string.Empty;
        private string _coreRoot = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_NavGateShowcase", Guid.NewGuid().ToString("N"));
            _coreRoot = Path.Combine(_root, "assets");
            Directory.CreateDirectory(_coreRoot);
            CopyDirectory(Path.Combine(FindRepoRoot(), "assets"), _coreRoot);
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
        public void NavGate_GateDrops_RebakeAdvances_PathDetours_SquadArrives()
        {
            using var engine = CreateEngine();
            engine.LoadMap("nav_gate_valley");

            var registry = (engine.GetService(CoreServiceKeys.NavQueryServices) as NavQueryServiceRegistry)
                ?? throw new InvalidOperationException("nav_gate_valley 必须注册 NavQueryServices");
            Assert.That(registry.TryGetStore(0, 0, out NavTileStore? store), Is.True);
            uint revisionBefore = store!.Revision;

            int gateDroppedTick = -1;
            for (int tick = 0; tick < 600; tick++)
            {
                engine.Tick(1f / 60f);
                if (tick % 120 == 0)
                {
                    var squad = SquadPositions(engine);
                    string head = squad.Count > 0 ? $"({squad[0].x},{squad[0].y})" : "none";
                    registry.TryCreateQuery(0, 0, null, out var svc0);
                    var probe = svc0!.TryFindPath(squad.Count > 0 ? squad[0].x : 1500, squad.Count > 0 ? squad[0].y : 1500, 5500, 5500);
                    TestContext.Out.WriteLine($"DIAG tick={tick} head={head} rev={store.Revision} probe={probe.Status}/{probe.PathXcm?.Length ?? -1}");
                }

                if (gateDroppedTick < 0 && HasStructuralObstacleNear(engine, 3600, 3600, 1300))
                {
                    gateDroppedTick = tick;
                    break;
                }
            }

            Assert.That(gateDroppedTick, Is.GreaterThanOrEqualTo(0), "时间线必须在行军途中落下城门（结构障碍 @ 3600,3600）");

            TickUntilRevisionAdvances(engine, store, revisionBefore, timeoutMs: 20000);

            Assert.That(store.Revision, Is.GreaterThan(revisionBefore), "落门必须推进增量重烤（store revision 增长）");

            registry.TryCreateQuery(0, 0, null, out var querySvc);
            NavPathResult detour = querySvc!.TryFindPath(1500, 1500, 5500, 5500);
            Assert.That(detour.Status, Is.EqualTo(NavPathStatus.Ok), "落门后 A→B 必须仍可达（绕行）");
            int minDist = int.MaxValue;
            for (int i = 0; i < detour.PathXcm.Length; i++)
            {
                long dx = detour.PathXcm[i] - 3600;
                long dy = detour.PathZcm[i] - 3600;
                minDist = (int)Math.Min(minDist, Math.Sqrt((dx * dx) + (dy * dy)));
            }

            TestContext.Out.WriteLine($"DIAG detour minDistToGate={minDist} pts={detour.PathXcm.Length}");
            Assert.That(minDist, Is.GreaterThan(1100), "重烤后的 A→B 路径必须绕开城门圆（r=1100）——否则挖洞没有反映到查询结果");

            bool allArrived = false;
            int arrivedTick = -1;
            for (int tick = 0; tick < 1200 && !allArrived; tick++)
            {
                engine.Tick(1f / 60f);
                var positions = SquadPositions(engine);
                if (positions.Count == 0)
                {
                    continue;
                }

                allArrived = positions.All(p =>
                {
                    long dx = p.x - 5500;
                    long dy = p.y - 5500;
                    return (dx * dx) + (dy * dy) <= 700 * 700;
                });
                if (allArrived)
                {
                    arrivedTick = tick;
                }
            }

            Assert.That(allArrived, Is.True, $"落门后小队必须全部绕行抵达 B 营（arrivedTick={arrivedTick}）");
        }

        [Test]
        public void NavGate_FreezeAblation_RevisionStalls_PathStillCrossesGate()
        {
            using var engine = CreateEngine();
            engine.LoadMap("nav_gate_valley");

            var queue = (engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue) as RuntimeIncrementalNavMeshRebuildQueue)
                ?? throw new InvalidOperationException("nav_gate_valley 必须注册运行时增量重烤队列");
            queue.ProcessingEnabled = false;

            var registry = (engine.GetService(CoreServiceKeys.NavQueryServices) as NavQueryServiceRegistry)!;
            registry.TryGetStore(0, 0, out NavTileStore? store);
            uint revisionBefore = store!.Revision;

            int gateDroppedTick = -1;
            for (int tick = 0; tick < 900; tick++)
            {
                engine.Tick(1f / 60f);
                if (HasStructuralObstacleNear(engine, 3600, 3600, 1300))
                {
                    gateDroppedTick = tick;
                    break;
                }
            }

            Assert.That(gateDroppedTick, Is.GreaterThanOrEqualTo(0), "冻结不应阻止时间线落门（世界仍会改变）");
            for (int i = 0; i < 30; i++)
            {
                engine.Tick(1f / 60f);
            }

            Assert.That(store.Revision, Is.EqualTo(revisionBefore), "冻结时增量重烤必须完全停滞（消融成立的前提）");
            Assert.That(queue.PendingTileCount, Is.GreaterThan(0), "脏瓦片已入队待重烤（解冻后应立即消费）");

            registry.TryCreateQuery(0, 0, null, out var staleSvc);
            NavPathResult stale = staleSvc!.TryFindPath(1500, 1500, 5500, 5500);
            Assert.That(stale.Status, Is.EqualTo(NavPathStatus.Ok));
            Assert.That(PathClearsCircle(stale, 3600, 3600, 1100 + 400), Is.False,
                "冻结消融：旧 navmesh 的 A→B 路径必须仍穿过城门位置——这正是'没有 runtime 更新'的代价，也是可读对比的一半");

            queue.ProcessingEnabled = true;
            TickUntilRevisionAdvances(engine, store, revisionBefore, timeoutMs: 20000);

            Assert.That(store.Revision, Is.GreaterThan(revisionBefore), "解冻后待重烤瓦片必须被消费并推进 revision");
        }

        /// <summary>
        /// 增量重烤在后台线程执行（单瓦片 Recast 可达秒级），发布依赖后续 tick 泵送；
        /// 用小间隔 tick 按墙钟等待 revision 推进，固定帧数窗口会在烘焙完成前耗尽。
        /// </summary>
        private static void TickUntilRevisionAdvances(GameEngine engine, NavTileStore store, uint revisionBefore, double timeoutMs)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (store.Revision <= revisionBefore && stopwatch.Elapsed.TotalMilliseconds < timeoutMs)
            {
                engine.Tick(1f / 60f);
                Thread.Sleep(20);
            }
        }

        private static bool HasStructuralObstacleNear(GameEngine engine, int xCm, int yCm, int radiusCm)
        {
            var queryDesc = new QueryDescription().WithAll<WorldPositionCm, RuntimeNavMeshStructuralObstacle>();
            int count = engine.World.CountEntities(in queryDesc);
            if (count == 0)
            {
                return false;
            }

            Span<Entity> span = new Entity[count];
            engine.World.GetEntities(in queryDesc, span);
            for (int i = 0; i < span.Length; i++)
            {
                var pos = engine.World.Get<WorldPositionCm>(span[i]);
                long dx = (long)pos.Value.X.RoundToInt() - xCm;
                long dy = (long)pos.Value.Y.RoundToInt() - yCm;
                if ((dx * dx) + (dy * dy) <= (long)radiusCm * radiusCm)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<(int x, int y)> SquadPositions(GameEngine engine)
        {
            var result = new List<(int x, int y)>();
            var queryDesc = new QueryDescription().WithAll<WorldPositionCm>().WithNone<RuntimeNavMeshStructuralObstacle>();
            int count = engine.World.CountEntities(in queryDesc);
            if (count == 0)
            {
                return result;
            }

            Span<Entity> span = new Entity[count];
            engine.World.GetEntities(in queryDesc, span);
            for (int i = 0; i < span.Length; i++)
            {
                var pos = engine.World.Get<WorldPositionCm>(span[i]);
                result.Add((pos.Value.X.RoundToInt(), pos.Value.Y.RoundToInt()));
            }

            return result;
        }

        private static bool PathClearsCircle(NavPathResult path, int cx, int cy, int radiusCm)
        {
            for (int i = 0; i < path.PathXcm.Length; i++)
            {
                long dx = path.PathXcm[i] - cx;
                long dy = path.PathZcm[i] - cy;
                if ((dx * dx) + (dy * dy) <= (long)radiusCm * radiusCm)
                {
                    return false;
                }
            }

            return true;
        }

        private GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            var modPaths = new[]
            {
                Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                Path.Combine(repoRoot, "mods", "CoreInputMod"),
                Path.Combine(repoRoot, "mods", "capabilities", "camera", "CameraProfilesMod"),
                Path.Combine(repoRoot, "mods", "showcases", "navmesh_runtime_gate", "NavGateShowcaseMod"),
            }.ToList();

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, _coreRoot);
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

            throw new DirectoryNotFoundException("Could not locate repo root.");
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory))
            {
                File.Copy(sourceFile, Path.Combine(destinationDirectory, Path.GetFileName(sourceFile)), overwrite: true);
            }

            foreach (string sourceChildDirectory in Directory.EnumerateDirectories(sourceDirectory))
            {
                CopyDirectory(sourceChildDirectory, Path.Combine(destinationDirectory, Path.GetFileName(sourceChildDirectory)));
            }
        }
    }
}
