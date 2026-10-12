using System.Collections.Generic;
using System.IO;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace CrowdSimulationTests.Integration;

/// <summary>
/// 标准参数 Mod 的一份群体配置给多张图共用。每张图在 navmesh 里写自己的 solver。
/// 聚焦创建会话，挂起释放，恢复再创建。
/// </summary>
public sealed class SharedConfigSessionLifecycleTests
{
    [Test]
    public void TwoCrowdMaps_ShareOneConfig_AndSessionsFollowFocus()
    {
        const string mapA = "crowd_simulation_s1337";
        const string mapB = "crowd_simulation_s7";
        string repoRoot = FindRepoRoot();
        using var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(new List<string>
        {
            Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
            Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationMod"),
            Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationStandardMod"),
            Path.Combine(repoRoot, "mods", "showcases", "crowd_simulation", "CrowdSimulationS1Terrain1337Mod"),
            Path.Combine(repoRoot, "mods", "showcases", "crowd_simulation", "CrowdSimulationS1Terrain7Mod"),
        }, Path.Combine(repoRoot, "assets"));
        engine.SetService(CoreServiceKeys.CameraBehaviorInputState, new Ludots.Core.Gameplay.Camera.CameraBehaviorInputState());
        engine.SetService(CoreServiceKeys.ViewController, new StubViewController());
        engine.Start();

        engine.LoadMap(mapA);
        CrowdSimSession sessionA = RequireSession(engine);
        Assert.That(sessionA.Config.MapId, Is.EqualTo(mapA));
        Assert.That(sessionA.Config.SurfaceAsset, Does.Contain(mapA));

        engine.PushMap(mapB);
        CrowdSimSession sessionB = RequireSession(engine);
        Assert.That(sessionB, Is.Not.SameAs(sessionA));
        Assert.That(sessionB.Config.MapId, Is.EqualTo(mapB));
        Assert.That(sessionB.Config.SurfaceAsset, Does.Contain(mapB));
        Assert.That(sessionB.Config.WorldSeed, Is.EqualTo(sessionA.Config.WorldSeed));
        Assert.That(sessionB.Config.FixedHz, Is.EqualTo(sessionA.Config.FixedHz));

        engine.PopMap();
        CrowdSimSession resumed = RequireSession(engine);
        Assert.That(resumed, Is.Not.SameAs(sessionB));
        Assert.That(resumed.Config.MapId, Is.EqualTo(mapA));
        Assert.That(resumed.Config.WorldSeed, Is.EqualTo(sessionA.Config.WorldSeed));
    }

    private static CrowdSimSession RequireSession(GameEngine engine)
    {
        CrowdSimSession session = engine.GetService(CoreServiceKeys.CrowdSimulationSession);
        Assert.That(session, Is.Not.Null);
        return session;
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
