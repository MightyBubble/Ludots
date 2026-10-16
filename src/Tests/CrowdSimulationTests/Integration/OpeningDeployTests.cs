using System.Linq;
using System.Runtime;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace CrowdSimulationTests.Integration;

/// <summary>
/// 开局人数只来自 deploy.initialUnits。S4 调试脚本不再写 spawn。
/// 地图聚焦后由会话宿主排成 tick 0 的 spawn，再走原来的生成指令。
/// </summary>
public sealed class OpeningDeployTests
{
    [Test]
    public void S4Chain_ResolvesInitialUnits_AndDebugScriptHasNoSpawn()
    {
        string repo = FindRepoRoot();
        JsonObject snapshot = CrowdSimulationEditorApi.Load(repo, "CrowdSimulationS4DeployMod");
        Assert.That(snapshot["ok"]!.GetValue<bool>(), Is.True, snapshot.ToJsonString());
        int initial = snapshot["scenario"]!["deploy"]!["initialUnits"]!.GetValue<int>();
        Assert.That(initial, Is.EqualTo(12000));

        var debug = ModConfigChain.Open("CrowdSimulationS4DeployMod").Merged("CrowdSimulationDebug.json");
        CrowdDebugScriptEntry[] script = CrowdSimulationDebugConfig.Load(debug).Session?.Script
            ?? throw new InvalidOperationException("S4 调试脚本缺失。");
        for (int i = 0; i < script.Length; i++)
        {
            Assert.That(script[i].Cmd["type"]!.GetValue<string>(), Is.Not.EqualTo("spawn"), $"script[{i}]");
        }

        JsonArray rows = CrowdSimulationRuntime.ComposeDemoScript(initial, script);
        Assert.That(rows[0]!["tick"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(rows[0]!["cmd"]!["type"]!.GetValue<string>(), Is.EqualTo("spawn"));
        Assert.That(rows[0]!["cmd"]!["count"]!.GetValue<int>(), Is.EqualTo(initial));
        Assert.That(rows.Count, Is.EqualTo(script.Length + 1));

        JsonArray none = CrowdSimulationRuntime.ComposeDemoScript(0, script);
        Assert.That(none.Count, Is.EqualTo(script.Length));
        Assert.That(none[0]!["cmd"]!["type"]!.GetValue<string>(), Is.Not.EqualTo("spawn"));
    }

    [TestCase("CrowdSimulationS1Terrain1337Mod")]
    [TestCase("CrowdSimulationS1Terrain7Mod")]
    [TestCase("CrowdSimulationS1Terrain2024Mod")]
    [TestCase("CrowdSimulationS3PathMod")]
    public void MapOnlyStages_OpenWithoutUnits(string modId)
    {
        JsonObject snapshot = CrowdSimulationEditorApi.Load(FindRepoRoot(), modId);
        Assert.That(snapshot["ok"]!.GetValue<bool>(), Is.True, snapshot.ToJsonString());
        Assert.That(snapshot["scenario"]!["deploy"]!["initialUnits"]!.GetValue<int>(), Is.EqualTo(0));
    }

    [Test]
    public void ProductionSession_OpeningCountMatchesInitialUnits()
        => AssertOpeningCount(4);

    [Test]
    public void ProductionSession_ZeroInitialUnitsSpawnsNothing()
        => AssertOpeningCount(0);

    [Test]
    public void EngineDispose_ReleasesCrowdSession()
    {
        WeakReference session = RunAndDisposeEngine();
        for (int i = 0; i < 10 && session.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.That(session.IsAlive, Is.False, "引擎销毁后群体会话仍被引用（路径服务工作线程没有停）。");
    }

    [Test]
    public void EngineDispose_ReleasesModLoadContexts()
    {
        WeakReference loadContext = RunAndDisposeEngineLoadContext();
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        for (int i = 0; i < 24 && loadContext.IsAlive; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        Assert.That(loadContext.IsAlive, Is.False, "引擎销毁后 Mod 的 collectible ALC 仍被强引用。");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RunAndDisposeEngine()
    {
        string probe = CreateProbeMod(4);
        try
        {
            using var engine = StartS4Engine(probe);
            CrowdSimSession session = engine.GetService(CoreServiceKeys.CrowdSimulationSession)
                ?? throw new InvalidOperationException("没有群体会话。");
            TickUntilFirstStep(engine, session);
            return new WeakReference(session);
        }
        finally
        {
            Directory.Delete(probe, recursive: true);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RunAndDisposeEngineLoadContext()
    {
        string probe = CreateProbeMod(4);
        try
        {
            using var engine = StartS4Engine(probe);
            CrowdSimSession session = engine.GetService(CoreServiceKeys.CrowdSimulationSession)
                ?? throw new InvalidOperationException("没有群体会话。");
            TickUntilFirstStep(engine, session);
            AssemblyLoadContext? loadContext = null;
            foreach (Assembly assembly in engine.ModLoader.LoadedAssemblies)
            {
                var alc = AssemblyLoadContext.GetLoadContext(assembly);
                if (alc == null || !alc.IsCollectible) continue;
                string? name = assembly.GetName().Name;
                if (name != null && name.StartsWith("CrowdSimulation", StringComparison.Ordinal))
                {
                    loadContext = alc;
                    break;
                }
            }

            if (loadContext == null)
            {
                throw new InvalidOperationException("未找到 CrowdSimulation collectible ALC。");
            }

            return new WeakReference(loadContext);
        }
        finally
        {
            Directory.Delete(probe, recursive: true);
        }
    }

    private static void AssertOpeningCount(int initialUnits)
    {
        string probe = CreateProbeMod(initialUnits);
        try
        {
            using var engine = StartS4Engine(probe);
            CrowdSimSession session = engine.GetService(CoreServiceKeys.CrowdSimulationSession)
                ?? throw new InvalidOperationException("没有群体会话。");
            Assert.That(session.Config.Deploy.InitialUnits, Is.EqualTo(initialUnits));

            TickUntilFirstStep(engine, session);

            Assert.That(session.Units.Count, Is.EqualTo(initialUnits));
            int spawns = 0;
            foreach (var entry in session.Commands.Log)
            {
                if (entry.Cmd.Kind != CrowdSimCommandKind.Spawn) continue;
                spawns++;
                Assert.That(entry.Tick, Is.EqualTo(0));
                Assert.That(entry.Cmd.Count, Is.EqualTo(initialUnits));
            }

            Assert.That(spawns, Is.EqualTo(initialUnits > 0 ? 1 : 0));
            Assert.That(session.Commands.PendingCount, Is.GreaterThan(0));
        }
        finally
        {
            Directory.Delete(probe, recursive: true);
        }
    }

    private static string CreateProbeMod(int initialUnits)
    {
        string probe = Path.Combine(Path.GetTempPath(), "Ludots_OpeningDeployProbe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(probe, "assets"));
        File.WriteAllText(Path.Combine(probe, "mod.json"),
            """{"name":"LudotsOpeningDeployProbeMod","version":"1.0.0","priority":0,"dependencies":{"CrowdSimulationS4DeployMod":"^0.4.0"}}""");
        File.WriteAllText(Path.Combine(probe, "assets", "CrowdSimulationConfig.json"),
            $$"""
            {
              "deploy": { "initialUnits": {{initialUnits}} }
            }
            """);
        return probe;
    }

    private static GameEngine StartS4Engine(string probe)
    {
        string repo = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(new List<string>
        {
            Path.Combine(repo, "mods", "LudotsCoreMod"),
            Path.Combine(repo, "mods", "capabilities", "navigation", "CrowdSimulationMod"),
            Path.Combine(repo, "mods", "capabilities", "navigation", "CrowdSimulationUiMod"),
            Path.Combine(repo, "mods", "capabilities", "navigation", "CrowdSimulationStandardMod"),
            Path.Combine(repo, "mods", "showcases", "crowd_simulation", "CrowdSimulationDemoContentMod"),
            Path.Combine(repo, "mods", "showcases", "crowd_simulation", "CrowdSimulationS1Terrain1337Mod"),
            Path.Combine(repo, "mods", "showcases", "crowd_simulation", "CrowdSimulationS4DeployMod"),
            probe,
        }, Path.Combine(repo, "assets"));
        engine.SetService(CoreServiceKeys.CameraBehaviorInputState, new Ludots.Core.Gameplay.Camera.CameraBehaviorInputState());
        engine.SetService(CoreServiceKeys.ViewController, new StubView());
        engine.Start();
        engine.LoadMap("crowd_simulation_s1337");
        return engine;
    }

    private static void TickUntilFirstStep(GameEngine engine, CrowdSimSession session)
    {
        int guard = 0;
        while (session.TickCount < 1 && guard++ < 180)
        {
            engine.Tick(1f / 30f);
        }

        Assert.That(session.TickCount, Is.GreaterThan(0));
    }

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
