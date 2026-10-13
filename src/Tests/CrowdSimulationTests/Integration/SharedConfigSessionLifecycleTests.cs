using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Navigation.NavMesh.Config;
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

    [Test]
    public void LoadMap_AwayFromCrowd_StopsTheOldSession()
    {
        const string crowdMap = "crowd_simulation_s1337";
        string repoRoot = FindRepoRoot();
        string probeModRoot = Path.Combine(Path.GetTempPath(), "Ludots_PlainHoldMod_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(probeModRoot, "assets", "Maps"));
        try
        {
            File.WriteAllText(Path.Combine(probeModRoot, "mod.json"),
                """{"name":"LudotsPlainHoldMod","version":"1.0.0","priority":0,"dependencies":{"LudotsCoreMod":"^1.0.0"}}""");
            File.WriteAllText(Path.Combine(probeModRoot, "assets", "Maps", "plain_hold.json"), """
{
  "Id": "plain_hold",
  "Teams": [ { "TeamId": 1, "RepresentativeInstanceId": "team_1" } ],
  "Players": [ { "PlayerId": 1, "TeamId": 1, "RepresentativeInstanceId": "player_1" } ],
  "Boards": [
    { "Name": "default", "SpatialType": "Grid", "WidthCm": 6400, "HeightCm": 6400, "Grid": { "CellSizeCm": 100 } }
  ],
  "Entities": [
    { "InstanceId": "team_1", "Template": "crowd_simulation_team" },
    { "InstanceId": "player_1", "Template": "crowd_simulation_player" }
  ]
}
""");
            var mods = CrowdMods(repoRoot);
            mods.Add(probeModRoot);
            using var engine = Start(mods, repoRoot);
            engine.LoadMap(crowdMap);
            CrowdSimSession session = RequireSession(engine);
            int guard = 0;
            while (session.TickCount == 0 && guard++ < 180)
            {
                engine.Tick(1f / 30f);
            }

            Assert.That(session.TickCount, Is.GreaterThan(0));
            int frozen = session.TickCount;
            engine.LoadMap("plain_hold");
            Assert.That(engine.GetService(CoreServiceKeys.CrowdSimulationSession), Is.Null);
            for (int i = 0; i < 30; i++)
            {
                engine.Tick(1f / 30f);
            }

            Assert.That(session.TickCount, Is.EqualTo(frozen));
        }
        finally
        {
            if (Directory.Exists(probeModRoot)) Directory.Delete(probeModRoot, recursive: true);
        }
    }

    [Test]
    public void LoadMap_FromCrowdMap_ToCrowdMap_ReplacesTheSession()
    {
        const string mapA = "crowd_simulation_s1337";
        const string mapB = "crowd_simulation_s7";
        string repoRoot = FindRepoRoot();
        using var engine = Start(new List<string>
        {
            Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
            Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationMod"),
            Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationStandardMod"),
            Path.Combine(repoRoot, "mods", "showcases", "crowd_simulation", "CrowdSimulationS1Terrain1337Mod"),
            Path.Combine(repoRoot, "mods", "showcases", "crowd_simulation", "CrowdSimulationS1Terrain7Mod"),
        }, repoRoot);

        engine.LoadMap(mapA);
        CrowdSimSession sessionA = RequireSession(engine);
        int guard = 0;
        while (sessionA.TickCount == 0 && guard++ < 180)
        {
            engine.Tick(1f / 30f);
        }

        Assert.That(sessionA.TickCount, Is.GreaterThan(0));
        int frozen = sessionA.TickCount;
        engine.LoadMap(mapB);
        CrowdSimSession sessionB = RequireSession(engine);
        Assert.That(sessionB, Is.Not.SameAs(sessionA));
        Assert.That(sessionB.Config.MapId, Is.EqualTo(mapB));
        Assert.That(sessionB.Planner, Is.Not.Null);
        for (int i = 0; i < 30; i++)
        {
            engine.Tick(1f / 30f);
        }

        Assert.That(sessionA.TickCount, Is.EqualTo(frozen));
    }

    [Test]
    public void CrowdSolver_WithoutConfigEntry_Throws()
    {
        InvalidOperationException ex = FocusProbe("crowd_missing", "crowd-simulation", includeCrowdMod: false);
        AssertMissingCrowdConfig(ex, "crowd_missing");
    }

    [Test]
    public void CrowdSolver_WhenConfigMergesNull_Throws()
    {
        string repoRoot = FindRepoRoot();
        string probeModRoot = WriteProbeMod("crowd_empty", "crowd-simulation");
        try
        {
            var mods = new List<string>
            {
                Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationMod"),
                probeModRoot,
            };
            using var engine = Start(mods, repoRoot);
            ConfigLoadException ex = Assert.Throws<ConfigLoadException>(() => engine.LoadMap("crowd_empty"))!;
            Assert.That(ex.InnerException, Is.TypeOf<InvalidOperationException>());
            AssertMissingCrowdConfig((InvalidOperationException)ex.InnerException!, "crowd_empty");
            Assert.That(ex.Message, Does.Contain("配置路径:"));
            Assert.That(ex.Message, Does.Contain("CrowdSimulationConfig.json"));
        }
        finally
        {
            if (Directory.Exists(probeModRoot)) Directory.Delete(probeModRoot, recursive: true);
        }
    }

    [Test]
    public void OtherSolver_WithoutCrowdConfig_DoesNotActivate()
    {
        string repoRoot = FindRepoRoot();
        string probeModRoot = WriteProbeMod("nav_hold", "navmesh");
        try
        {
            using var engine = Start(new List<string>
            {
                Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                probeModRoot,
            }, repoRoot);
            Assert.DoesNotThrow(() => engine.LoadMap("nav_hold"));
            Assert.That(engine.GetService(CoreServiceKeys.CrowdSimulationSession), Is.Null);
        }
        finally
        {
            if (Directory.Exists(probeModRoot)) Directory.Delete(probeModRoot, recursive: true);
        }
    }

    private static InvalidOperationException FocusProbe(string mapId, string solver, bool includeCrowdMod)
    {
        string repoRoot = FindRepoRoot();
        string probeModRoot = WriteProbeMod(mapId, solver);
        try
        {
            var mods = new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") };
            if (includeCrowdMod)
            {
                mods.Add(Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationMod"));
            }

            mods.Add(probeModRoot);
            using var engine = Start(mods, repoRoot);
            return Assert.Throws<InvalidOperationException>(() => engine.LoadMap(mapId))!;
        }
        finally
        {
            if (Directory.Exists(probeModRoot)) Directory.Delete(probeModRoot, recursive: true);
        }
    }

    private static void AssertMissingCrowdConfig(InvalidOperationException ex, string mapId)
    {
        Assert.That(ex.Message, Does.Contain(mapId));
        Assert.That(ex.Message, Does.Contain(NavSolverDeclaration.CrowdSimulation));
        Assert.That(ex.Message, Does.Contain("CrowdSimulationConfig.json"));
    }

    private static string WriteProbeMod(string mapId, string solver)
    {
        string probeModRoot = Path.Combine(Path.GetTempPath(), "Ludots_CrowdSolverProbe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(probeModRoot, "assets", "Maps"));
        Directory.CreateDirectory(Path.Combine(probeModRoot, "assets", "Navigation"));
        File.WriteAllText(Path.Combine(probeModRoot, "mod.json"),
            """{"name":"LudotsCrowdSolverProbe","version":"1.0.0","priority":0,"dependencies":{"LudotsCoreMod":"^1.0.0"}}""");
        File.WriteAllText(Path.Combine(probeModRoot, "assets", "Navigation", "navmesh.json"),
            $$"""
{
  "maps": {
    "{{mapId}}": { "solver": "{{solver}}" }
  }
}
""");
        File.WriteAllText(Path.Combine(probeModRoot, "assets", "Maps", mapId + ".json"), $$"""
{
  "Id": "{{mapId}}",
  "Boards": [
    { "Name": "default", "SpatialType": "Grid", "WidthCm": 6400, "HeightCm": 6400, "Grid": { "CellSizeCm": 100 } }
  ]
}
""");
        return probeModRoot;
    }

    private static List<string> CrowdMods(string repoRoot) =>
    [
        Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
        Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationMod"),
        Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationStandardMod"),
        Path.Combine(repoRoot, "mods", "showcases", "crowd_simulation", "CrowdSimulationS1Terrain1337Mod"),
    ];

    private static GameEngine Start(List<string> mods, string repoRoot)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(mods, Path.Combine(repoRoot, "assets"));
        engine.SetService(CoreServiceKeys.CameraBehaviorInputState, new Ludots.Core.Gameplay.Camera.CameraBehaviorInputState());
        engine.SetService(CoreServiceKeys.ViewController, new StubViewController());
        engine.Start();
        return engine;
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
