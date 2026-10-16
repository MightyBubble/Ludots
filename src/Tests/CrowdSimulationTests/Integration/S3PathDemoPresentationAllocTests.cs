using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using Arch.System;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace CrowdSimulationTests.Integration;

[TestFixture]
public sealed class S3PathDemoPresentationAllocTests
{
    [Test]
    public void SteadyRoutePolyline_AllocatesNothing()
    {
        string repoRoot = FindRepoRoot();
        string clockModRoot = Path.Combine(Path.GetTempPath(), "Ludots_S3AllocClockMod_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(clockModRoot, "assets", "Engine"));
        try
        {
            File.WriteAllText(Path.Combine(clockModRoot, "mod.json"),
                """{"name":"LudotsS3AllocClockMod","version":"1.0.0","priority":0,"dependencies":{"CrowdSimulationS3PathMod":"^0.1.0"}}""");
            File.WriteAllText(Path.Combine(clockModRoot, "assets", "Engine", "clock.json"),
                """{"FixedHz":30}""");
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(new List<string>
            {
                Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationMod"),
                Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationUiMod"),
                Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationStandardMod"),
                Path.Combine(repoRoot, "mods", "showcases", "crowd_simulation", "CrowdSimulationDemoContentMod"),
                Path.Combine(repoRoot, "mods", "showcases", "crowd_simulation", "CrowdSimulationS1Terrain1337Mod"),
                Path.Combine(repoRoot, "mods", "showcases", "crowd_simulation", "CrowdSimulationS3PathMod"),
                clockModRoot,
            }, Path.Combine(repoRoot, "assets"));
            engine.SetService(CoreServiceKeys.CameraBehaviorInputState, new Ludots.Core.Gameplay.Camera.CameraBehaviorInputState());
            engine.SetService(CoreServiceKeys.ViewController, new StubViewController());
            engine.Start();
            engine.LoadMap("crowd_simulation_s1337");

            var crowdRuntime = engine.GetService(CoreServiceKeys.CrowdSimulationRuntime)
                ?? throw new InvalidOperationException("S3 地图聚焦后应有 CrowdSimulationRuntime。");
            var demo = GetInstanceField<S3PathDemoRuntime>(crowdRuntime, "_pathDemo")
                ?? throw new InvalidOperationException("S3 路径演示运行时未安装。");
            var presentation = GetInstanceField<ISystem<float>>(crowdRuntime, "_pathPresentation")
                ?? throw new InvalidOperationException("S3 路径呈现系统未安装。");

            var path = new PathResult
            {
                Reachable = true,
                PointCount = 4,
                Points =
                [
                    Fix64.FromInt(10), Fix64.FromInt(20),
                    Fix64.FromInt(30), Fix64.FromInt(40),
                ],
            };
            typeof(S3PathDemoRuntime).GetProperty(nameof(S3PathDemoRuntime.Current))!
                .SetValue(demo, path);

            for (int i = 0; i < 32; i++)
            {
                presentation.Update(0.016f);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 32; i++)
            {
                presentation.Update(0.016f);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0L), $"稳态 S3 路线呈现分配了 {allocated} 字节");
        }
        finally
        {
            if (Directory.Exists(clockModRoot)) Directory.Delete(clockModRoot, recursive: true);
        }
    }

    private static T? GetInstanceField<T>(object owner, string name) where T : class
    {
        FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"字段 {name} 不存在。");
        return field.GetValue(owner) as T;
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mods")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到仓库根。");
    }

    private sealed class StubViewController : Ludots.Core.Presentation.Camera.IViewController
    {
        public Vector2 Resolution { get; } = new(1280, 720);
        public float Fov { get; } = 50f;
        public float AspectRatio { get; } = 1280f / 720f;
    }
}
