using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace CrowdSimulationTests.Integration;

using CrowdSimulationTests.Parity;

/// <summary>
/// 真实系统组集成门:真引擎装载 crowd 地图(临时资产 mod 携带 autostart + spawn/order
/// 脚本),会话由引擎自身的 CrowdSimulationRuntime 在图聚焦时激活、由系统组 FixedHz 驱动
/// (不经无头直调),单位行军跨格;断言每个单位的 SpatialCellRef 恒等于当前位置所在格。
/// 步进若注册在帧末组(晚于 SpatialPartitionUpdateSystem),跨格当帧成员格落后一拍、
/// 新建实体错过当帧索引——把注册改回 Cleanup 的突变下本门即刻变红(已验)。
/// </summary>
public class EnginePipelineIntegrationTests
{
    [Test]
    public void SessionStepRunsBeforeSpatialPartitionSync_ThroughRealSystemGroups()
    {
        string repoRoot = FindRepoRoot();
        // 临时资产 mod 只带 CrowdSimulationDebug.json(autostart + spawn/order 脚本):
        // 会话脚本即测试输入,不依赖任何演示 mod 的入口代码。
        string probeModRoot = Path.Combine(Path.GetTempPath(), "Ludots_SessionStepOrderingProbeMod_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(probeModRoot, "assets"));
        try
        {
            File.WriteAllText(Path.Combine(probeModRoot, "mod.json"),
                """{"name":"LudotsSessionStepOrderingProbeMod","version":"1.0.0","priority":0}""");
            File.WriteAllText(Path.Combine(probeModRoot, "assets", "CrowdSimulationDebug.json"), """
{
  "session": {
    "autostart": true,
    "script": [
      { "tick": 0, "cmd": { "type": "spawn", "count": 24 } },
      { "tick": 2, "cmd": { "type": "order", "player": 1, "xCm": 1200000, "yCm": 800000, "shape": "box" } }
    ]
  }
}
""");

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(new List<string>
            {
                Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationMod"),
                Path.Combine(repoRoot, "mods", "showcases", "crowd_simulation", "CrowdSimulationS1Terrain1337Mod"),
                probeModRoot,
            }, Path.Combine(repoRoot, "assets"));
            // 无头引擎没有宿主:相机行为输入与视口是宿主服务,补最小实例让默认相机的
            // 运行时接线(高度图 provider)在装载期完成;高度图本体由引擎从地图 .height 装载。
            engine.SetService(CoreServiceKeys.CameraBehaviorInputState, new Ludots.Core.Gameplay.Camera.CameraBehaviorInputState());
            engine.SetService(CoreServiceKeys.ViewController, new StubViewController());
            engine.Start();
            engine.LoadMap("crowd_simulation_s1337");

            var session = engine.GetService(CoreServiceKeys.CrowdSimulationSession);
            Assert.That(session, Is.Not.Null, "crowd 地图聚焦后引擎自身的运行时应激活会话");
            var converter = engine.GetService(CoreServiceKeys.SpatialCoordinateConverter);
            Assert.That(converter, Is.Not.Null, "引擎应提供空间坐标转换服务");

            // 初始格记账,步进引擎直到有单位跨过至少一个分区格(路径答复为引擎态异步,
            // 会话按固定生效帧自行停摆/恢复;上限内没跨格则本门空转,直接判失败)
            var initialCells = new Dictionary<uint, (int X, int Y)>();
            int maxFrames = 1500;
            for (int f = 0; f < maxFrames && session.TickCount < 400; f++)
            {
                engine.Tick(1f / 30f);
                if (session.TickCount == 40 && initialCells.Count == 0)
                {
                    for (int i = 0; i < session.Units.Count; i++)
                    {
                        var grid = converter.WorldToGrid(
                            engine.World.Get<WorldPositionCm>(session.Units.EntityAt(i)).Value.ToWorldCmInt2());
                        initialCells[session.Units.HandleAt(i)] = (grid.X, grid.Y);
                    }
                }
            }

            Assert.That(session.Units.Count, Is.GreaterThan(0), "spawn 应已落地");
            Assert.That(initialCells.Count, Is.GreaterThan(0), "行军前应完成初始格记账");

            int crossed = 0;
            for (int i = 0; i < session.Units.Count; i++)
            {
                uint handle = session.Units.HandleAt(i);
                var grid = converter.WorldToGrid(
                    engine.World.Get<WorldPositionCm>(session.Units.EntityAt(i)).Value.ToWorldCmInt2());
                if (initialCells.TryGetValue(handle, out var initial) && initial != (grid.X, grid.Y)) crossed++;
            }

            Assert.That(crossed, Is.GreaterThan(0), "没有任何单位跨过分区格——执行序断言空转");

            for (int i = 0; i < session.Units.Count; i++)
            {
                uint handle = session.Units.HandleAt(i);
                var entity = session.Units.EntityAt(i);
                var grid = converter.WorldToGrid(
                    engine.World.Get<WorldPositionCm>(entity).Value.ToWorldCmInt2());
                Assert.That(engine.World.Has<SpatialCellRef>(entity), Is.True,
                    $"单位 {handle:x} 无分区成员格——步进晚于同步会让同帧新建实体错过当帧索引");
                var cellRef = engine.World.Get<SpatialCellRef>(entity);
                Assert.That(cellRef.State, Is.EqualTo(SpatialMembershipState.Active), $"单位 {handle:x} 成员格状态");
                Assert.That((cellRef.CellX, cellRef.CellY), Is.EqualTo((grid.X, grid.Y)),
                    $"单位 {handle:x} 成员格滞后于位置(会话步进应先于 SpatialPartitionUpdateSystem)");
            }

            TestContext.Out.WriteLine(
                $"tick {session.TickCount},{session.Units.Count} 单位,{crossed} 个跨过分区格,成员格全程与位置一致");
        }
        finally
        {
            if (Directory.Exists(probeModRoot)) Directory.Delete(probeModRoot, recursive: true);
        }
    }

    private static (int X, int Y) CurrentCell(GameEngine engine, ISpatialCoordinateConverter converter, int dense)
    {
        var pos = engine.World.Get<WorldPositionCm>(engine.GetService(CoreServiceKeys.CrowdSimulationSession)!.Units.EntityAt(dense)).Value;
        var grid = converter.WorldToGrid(pos.ToWorldCmInt2());
        return (grid.X, grid.Y);
    }

    private sealed class StubViewController : Ludots.Core.Presentation.Camera.IViewController
    {
        public System.Numerics.Vector2 Resolution { get; } = new(1280, 720);
        public float Fov { get; } = 50f;
        public float AspectRatio { get; } = 1280f / 720f;
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mods")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到仓库根(向上查找 mods 目录失败)");
    }
}
