using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace CrowdSimulationTests.Integration;

/// <summary>
/// 调试目录项不在能力 Mod 上。S1 不带调试文件时正式配置照常装，聚焦建会话并接路径服务。
/// 登记了调试项但没有任何片段，聚焦时抛错。
/// </summary>
public class CrowdDebugCatalogTests
{
    [Test]
    public void S1Focus_WithoutDebugFile_ActivatesSessionWithPlanner()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(CrowdMods(repoRoot), Path.Combine(repoRoot, "assets"));
        engine.SetService(CoreServiceKeys.CameraBehaviorInputState, new Ludots.Core.Gameplay.Camera.CameraBehaviorInputState());
        engine.SetService(CoreServiceKeys.ViewController, new StubView());
        engine.Start();
        Assert.DoesNotThrow(() => engine.LoadMap("crowd_simulation_s1337"));

        Assert.That(engine.CrowdRuntime.HasFormalConfig, Is.True);
        Assert.That(engine.CrowdRuntime.FormalConfigMapId, Is.EqualTo("crowd_simulation_s1337"));
        Assert.That(engine.CrowdRuntime.HasDebugConfig, Is.False);
        var session = engine.GetService(CoreServiceKeys.CrowdSimulationSession);
        Assert.That(session, Is.Not.Null);
        Assert.That(session!.Config.MapId, Is.EqualTo("crowd_simulation_s1337"));
        Assert.That(session.Planner, Is.Not.Null);
    }

    [Test]
    public void RegisteredDebugEntry_WithNoFragment_Throws()
    {
        string repoRoot = FindRepoRoot();
        string probeModRoot = Path.Combine(Path.GetTempPath(), "Ludots_EmptyDebugCatalogMod_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(probeModRoot, "assets"));
        try
        {
            File.WriteAllText(Path.Combine(probeModRoot, "mod.json"),
                """{"name":"LudotsEmptyDebugCatalogMod","version":"1.0.0","priority":0,"dependencies":{"CrowdSimulationS1Terrain1337Mod":"^0.1.0"}}""");
            File.WriteAllText(Path.Combine(probeModRoot, "assets", "config_catalog.json"), """
[
  { "Path": "CrowdSimulationDebug.json", "Policy": "DeepObject" }
]
""");
            var mods = CrowdMods(repoRoot);
            mods.Add(probeModRoot);
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(mods, Path.Combine(repoRoot, "assets"));
            engine.SetService(CoreServiceKeys.CameraBehaviorInputState, new Ludots.Core.Gameplay.Camera.CameraBehaviorInputState());
            engine.SetService(CoreServiceKeys.ViewController, new StubView());
            engine.Start();
            var ex = Assert.Throws<Ludots.Core.Config.ConfigLoadException>(() => engine.LoadMap("crowd_simulation_s1337"));
            Assert.That(ex!.Message, Does.Contain("CrowdSimulationDebug.json"));
            Assert.That(ex.Message, Does.Contain("存在目录登记但合并为空"));
        }
        finally
        {
            if (Directory.Exists(probeModRoot)) Directory.Delete(probeModRoot, recursive: true);
        }
    }

    private static List<string> CrowdMods(string repoRoot) =>
    [
        Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
        Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationMod"),
        Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationStandardMod"),
        Path.Combine(repoRoot, "mods", "showcases", "crowd_simulation", "CrowdSimulationDemoContentMod"),
        Path.Combine(repoRoot, "mods", "showcases", "crowd_simulation", "CrowdSimulationS1Terrain1337Mod"),
    ];

    private sealed class StubView : Ludots.Core.Presentation.Camera.IViewController
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
