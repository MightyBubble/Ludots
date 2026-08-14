using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Arch.Core;
using CapabilityStandardHfsmShowcaseMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[NonParallelizable]
[Category("acceptance")]
public sealed class CapabilityStandardHfsmShowcaseAcceptanceTests
{
    private const string BindingName = "capability_standard_hfsm_showcase";
    private const string PresetId = "capability_standard_hfsm_showcase_raylib";
    private const string ShowcaseModId = "CapabilityStandardHfsmShowcaseMod";
    private const string MapId = "capability_standard_hfsm_showcase";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        ShowcaseModId
    };

    [Test]
    public void RootMod_IsSingleResponsibilityCapabilityShowcase()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        string modDir = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            ShowcaseModId);

        Assert.That(File.Exists(Path.Combine(modDir, "mod.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(modDir, $"{ShowcaseModId}.csproj")), Is.True);
        Assert.That(File.Exists(Path.Combine(modDir, "assets", "game.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(modDir, "assets", "Maps", $"{MapId}.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(modDir, "assets", "HfsmShowcase", "showcase.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(modDir, "assets", "Entities", "templates.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(modDir, "assets", "Presentation", "presenters.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(modDir, "WebApp", "src", "main.jsx")), Is.True);
        Assert.That(File.Exists(Path.Combine(modDir, "WebApp", "src", "dataplane", "client.js")), Is.True);
        Assert.That(File.Exists(Path.Combine(modDir, "Assets", "hfsm-graph-debug-app", "index.html")), Is.True);

        AssertLauncherBinding(repoRoot);
        AssertLauncherPreset(repoRoot);
        AssertShowcaseRegistry(repoRoot);
        AssertGameJsonRequiresCef(modDir);
        AssertConfigStates(modDir);
        AssertGraphDebugContract(modDir);
        AssertNoMixedShowcaseSource(modDir);
    }

    [Test]
    public void PlayerSeesWaterLowDrinkFullRunAndAnyStateDeath()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        var input = new CapabilityStandardShowcaseTestHarness.TestInputBackend();
        using GameEngine engine = CapabilityStandardShowcaseTestHarness.CreateEngine(repoRoot, AcceptanceMods, input);
        engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
        CapabilityStandardHfsmShowcaseRuntime runtime = RequireRuntime(engine);
        Entity hero = GetMapEntity(engine, "hfsm-hero");
        AssertDynamicPresentationEntity(engine, hero);

        WorldCmInt2 start = GetPosition(engine, hero);
        Assert.That(runtime.Snapshot.StateId, Is.EqualTo(CapabilityStandardHfsmShowcaseRuntime.StateGoDrink));
        Assert.That(runtime.Snapshot.StatePath, Is.EqualTo("Alive > Hydrate > Go Drink"));
        Assert.That(runtime.Snapshot.Water, Is.LessThan(runtime.ActiveConfig.LowWaterThreshold));

        TickUntil(engine, runtime, s => s.StateId == CapabilityStandardHfsmShowcaseRuntime.StateDrinking, 300);
        WorldCmInt2 drinkingPosition = GetPosition(engine, hero);
        Assert.That(DistanceCm(start, drinkingPosition), Is.GreaterThan(400f));
        Assert.That(runtime.Snapshot.PlayerStory, Does.Contain("stays still"));

        TickUntil(engine, runtime, s => s.StateId == CapabilityStandardHfsmShowcaseRuntime.StateRunning, 720);
        WorldCmInt2 runningPosition = GetPosition(engine, hero);
        Assert.That(DistanceCm(drinkingPosition, runningPosition), Is.GreaterThan(900f));
        Assert.That(runtime.Snapshot.StatePath, Is.EqualTo("Alive > Exercise > Running"));
        Assert.That(runtime.Snapshot.Water, Is.GreaterThanOrEqualTo((int)MathF.Floor(runtime.ActiveConfig.LowWaterThreshold)));

        input.SetButton(runtime.ActiveConfig.Shortcuts.FatalDamage, true);
        Tick(engine, 1);
        input.SetButton(runtime.ActiveConfig.Shortcuts.FatalDamage, false);
        Tick(engine, 1);
        Assert.That(runtime.Snapshot.StateId, Is.EqualTo(CapabilityStandardHfsmShowcaseRuntime.StateDead));
        Assert.That(runtime.Snapshot.StatePath, Is.EqualTo("Dead"));
        Assert.That(runtime.Snapshot.LastEvent, Does.Contain("Any State"));

        WorldCmInt2 deathPosition = GetPosition(engine, hero);
        Tick(engine, 90);
        Assert.That(DistanceCm(deathPosition, GetPosition(engine, hero)), Is.LessThan(2f));

        input.SetButton(runtime.ActiveConfig.Shortcuts.Thirst, true);
        Tick(engine, 1);
        input.SetButton(runtime.ActiveConfig.Shortcuts.Thirst, false);
        Tick(engine, 30);
        Assert.That(runtime.Snapshot.StateId, Is.EqualTo(CapabilityStandardHfsmShowcaseRuntime.StateDead));
        Assert.That(DistanceCm(deathPosition, GetPosition(engine, hero)), Is.LessThan(2f));

        input.SetButton(runtime.ActiveConfig.Shortcuts.Reset, true);
        Tick(engine, 1);
        input.SetButton(runtime.ActiveConfig.Shortcuts.Reset, false);
        Tick(engine, 1);
        Assert.That(runtime.Snapshot.StateId, Is.EqualTo(CapabilityStandardHfsmShowcaseRuntime.StateGoDrink));
        Assert.That(runtime.Snapshot.Health, Is.EqualTo(runtime.ActiveConfig.StartHealth));
    }

    [Test]
    public void AnyStateDeathAlsoInterruptsDrinking()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        using GameEngine engine = CapabilityStandardShowcaseTestHarness.CreateEngine(repoRoot, AcceptanceMods);
        engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
        CapabilityStandardHfsmShowcaseRuntime runtime = RequireRuntime(engine);
        Entity hero = GetMapEntity(engine, "hfsm-hero");

        TickUntil(engine, runtime, s => s.StateId == CapabilityStandardHfsmShowcaseRuntime.StateDrinking, 300);
        WorldCmInt2 beforeDeath = GetPosition(engine, hero);
        runtime.ApplyFatalDamage();
        Tick(engine, 1);

        Assert.That(runtime.Snapshot.StateId, Is.EqualTo(CapabilityStandardHfsmShowcaseRuntime.StateDead));
        Assert.That(runtime.Snapshot.Dead, Is.True);
        Tick(engine, 60);
        Assert.That(DistanceCm(beforeDeath, GetPosition(engine, hero)), Is.LessThan(2f));
    }

    private static CapabilityStandardHfsmShowcaseRuntime RequireRuntime(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(CapabilityStandardHfsmShowcaseRuntime.RuntimeKey, out object? obj) &&
               obj is CapabilityStandardHfsmShowcaseRuntime runtime
            ? runtime
            : throw new InvalidOperationException("HFSM showcase runtime missing.");
    }

    private static void TickUntil(
        GameEngine engine,
        CapabilityStandardHfsmShowcaseRuntime runtime,
        Func<CapabilityStandardHfsmShowcaseSnapshot, bool> predicate,
        int maxFrames)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (predicate(runtime.Snapshot))
            {
                return;
            }

            Tick(engine, 1);
        }

        Assert.Fail($"HFSM showcase did not reach the expected state within {maxFrames} frames. Current state={runtime.Snapshot.StatePath}.");
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(CapabilityStandardShowcaseTestHarness.DeltaTime);
        }
    }

    private static Entity GetMapEntity(GameEngine engine, string instanceId)
    {
        if (engine.CurrentMapSession == null)
        {
            throw new InvalidOperationException("No active map session.");
        }

        return engine.CurrentMapSession.EntityIndex.TryGet(instanceId, out Entity entity)
            ? entity
            : throw new InvalidOperationException($"Map entity '{instanceId}' was not loaded.");
    }

    private static WorldCmInt2 GetPosition(GameEngine engine, Entity entity)
    {
        return engine.World.Get<WorldPositionCm>(entity).ToWorldCmInt2();
    }

    private static void AssertDynamicPresentationEntity(GameEngine engine, Entity entity)
    {
        Assert.Multiple(() =>
        {
            Assert.That(engine.World.Has<WorldPositionCm>(entity), Is.True);
            Assert.That(engine.World.Has<PreviousWorldPositionCm>(entity), Is.True);
            Assert.That(engine.World.Has<VisualTransform>(entity), Is.True);
            Assert.That(engine.World.Has<PresentationStaticTransform>(entity), Is.False);
        });
    }

    private static float DistanceCm(WorldCmInt2 a, WorldCmInt2 b)
    {
        int dx = a.X - b.X;
        int dy = a.Y - b.Y;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    private static void AssertLauncherBinding(string repoRoot)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json")));
        foreach (JsonElement binding in document.RootElement.GetProperty("bindings").EnumerateArray())
        {
            if (!string.Equals(binding.GetProperty("name").GetString(), BindingName, StringComparison.Ordinal))
            {
                continue;
            }

            JsonElement target = binding.GetProperty("target");
            Assert.That(target.GetProperty("value").GetString(), Is.EqualTo("mods/showcases/capability_standard/CapabilityStandardHfsmShowcaseMod"));
            Assert.That(target.GetProperty("projectPath").GetString(), Is.EqualTo("CapabilityStandardHfsmShowcaseMod.csproj"));
            return;
        }

        Assert.Fail($"Launcher binding '{BindingName}' is missing.");
    }

    private static void AssertLauncherPreset(string repoRoot)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "launcher.presets.json")));
        foreach (JsonElement preset in document.RootElement.GetProperty("presets").EnumerateArray())
        {
            if (!string.Equals(preset.GetProperty("id").GetString(), PresetId, StringComparison.Ordinal))
            {
                continue;
            }

            string[] selectors = preset.GetProperty("selectors").EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray();
            Assert.That(selectors, Is.EquivalentTo(new[] { "$" + BindingName }));
            Assert.That(preset.GetProperty("adapterId").GetString(), Is.EqualTo("raylib"));
            JsonElement browserRuntime = preset.GetProperty("browserRuntime");
            Assert.That(browserRuntime.GetProperty("enabled").GetBoolean(), Is.True);
            Assert.That(browserRuntime.GetProperty("required").GetBoolean(), Is.True);
            Assert.That(browserRuntime.GetProperty("provider").GetString(), Is.EqualTo("cef"));
            return;
        }

        Assert.Fail($"Launcher preset '{PresetId}' is missing.");
    }

    private static void AssertGameJsonRequiresCef(string modDir)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(modDir, "assets", "game.json")));
        JsonElement browserRuntime = document.RootElement.GetProperty("browserRuntime");
        Assert.That(browserRuntime.GetProperty("enabled").GetBoolean(), Is.True);
        Assert.That(browserRuntime.GetProperty("required").GetBoolean(), Is.True);
        Assert.That(browserRuntime.GetProperty("provider").GetString(), Is.EqualTo("cef"));
    }

    private static void AssertShowcaseRegistry(string repoRoot)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "showcase.registry.json")));
        foreach (JsonElement entry in document.RootElement.GetProperty("showcases").EnumerateArray())
        {
            if (!string.Equals(entry.GetProperty("id").GetString(), BindingName, StringComparison.Ordinal))
            {
                continue;
            }

            Assert.That(entry.GetProperty("category").GetString(), Is.EqualTo("capability"));
            Assert.That(entry.GetProperty("binding").GetString(), Is.EqualTo(BindingName));
            Assert.That(entry.GetProperty("preset").GetString(), Is.EqualTo(PresetId));
            return;
        }

        Assert.Fail($"Showcase registry entry '{BindingName}' is missing.");
    }

    private static void AssertConfigStates(string modDir)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(modDir, "assets", "HfsmShowcase", "showcase.json")));
        JsonElement root = document.RootElement;
        string[] stateIds = root.GetProperty("states").EnumerateArray().Select(x => x.GetProperty("id").GetString() ?? string.Empty).ToArray();
        Assert.That(stateIds, Does.Contain(CapabilityStandardHfsmShowcaseRuntime.StateGoDrink));
        Assert.That(stateIds, Does.Contain(CapabilityStandardHfsmShowcaseRuntime.StateDrinking));
        Assert.That(stateIds, Does.Contain(CapabilityStandardHfsmShowcaseRuntime.StateRunning));
        Assert.That(stateIds, Does.Contain(CapabilityStandardHfsmShowcaseRuntime.StateDead));
        Assert.That(root.GetProperty("anyState").GetProperty("condition").GetString(), Is.EqualTo("HealthAtOrBelowZero"));
        Assert.That(root.GetProperty("shortcuts").GetProperty("fatalDamage").GetString(), Is.EqualTo("<Keyboard>/k"));
    }

    private static void AssertGraphDebugContract(string modDir)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(modDir, "assets", "HfsmShowcase", "showcase.json")));
        JsonElement graphDebug = document.RootElement.GetProperty("graphDebug");
        Assert.That(graphDebug.GetProperty("rootGraphId").GetString(), Is.EqualTo("hfsm.root"));
        JsonElement nodes = graphDebug.GetProperty("nodes");
        Assert.That(nodes.EnumerateArray().Any(node =>
            node.GetProperty("id").GetString() == CapabilityStandardHfsmShowcaseRuntime.StateGoDrink &&
            node.GetProperty("implementationGraphId").GetString() == "impl.GoDrink"), Is.True);

        JsonElement implementations = graphDebug.GetProperty("implementations");
        Assert.That(implementations.GetArrayLength(), Is.GreaterThanOrEqualTo(4));
        Assert.That(implementations.EnumerateArray().Any(ImplementationHasPinsAndEdges), Is.True);

        string host = File.ReadAllText(Path.Combine(modDir, "Runtime", "CapabilityStandardHfsmGraphDebugBrowserHost.cs"));
        Assert.That(host, Does.Contain(CapabilityStandardHfsmGraphDebugIds.WebUiTopic));
        Assert.That(host, Does.Contain("BrowserSurfaceHitTestOptions.Alpha()"));
        Assert.That(host, Does.Contain("UiSurfaceSegment.Overlay"));
        Assert.That(host, Does.Contain("CapabilityStandardHfsm.GraphDebug"));

        string web = File.ReadAllText(Path.Combine(modDir, "WebApp", "src", "main.jsx"));
        Assert.That(web, Does.Contain("waitForLudotsDataPlaneTransport"));
        Assert.That(web, Does.Contain("onNodeDoubleClick"));
        Assert.That(web, Does.Contain("implementationGraphId"));
        Assert.That(web, Does.Contain("sourceHandle"));
        Assert.That(web, Does.Contain("targetHandle"));
    }

    private static bool ImplementationHasPinsAndEdges(JsonElement implementation)
    {
        if (!implementation.TryGetProperty("nodes", out JsonElement nodes) ||
            !implementation.TryGetProperty("edges", out JsonElement edges) ||
            edges.GetArrayLength() == 0)
        {
            return false;
        }

        return nodes.EnumerateArray().Any(node =>
            node.TryGetProperty("inputPins", out JsonElement inputPins) &&
            inputPins.GetArrayLength() > 0 &&
            node.TryGetProperty("outputPins", out JsonElement outputPins) &&
            outputPins.GetArrayLength() > 0);
    }

    private static void AssertNoMixedShowcaseSource(string modDir)
    {
        string[] forbidden =
        {
            "GraphAiShowcase",
            "BehaviorTree",
            "ComplexBt",
            "RtsStarCraft",
            "CncTriNation",
            "BrowserRts"
        };

        foreach (string file in Directory.EnumerateFiles(modDir, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            foreach (string token in forbidden)
            {
                Assert.That(text, Does.Not.Contain(token), $"{file} must stay single-responsibility for HFSM.");
            }
        }
    }
}
