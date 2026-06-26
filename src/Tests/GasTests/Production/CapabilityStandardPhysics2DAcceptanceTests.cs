using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Ticking;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[NonParallelizable]
public sealed class CapabilityStandardPhysics2DAcceptanceTests
{
    private const string BindingName = "capability_standard_physics2d";
    private const string PresetId = "capability_standard_physics2d_raylib";
    private const string ShowcaseModId = "CapabilityStandardPhysics2DMod";
    private const string MapId = "capability_standard_physics2d";
    private const string PolygonWallTemplateId = "capability_standard_physics2d_polygon_wall";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod",
        ShowcaseModId
    };

    [Test]
    public void RootMod_LoadsPurePhysicsScenarioThroughFormalRuntimePath()
    {
        string repoRoot = FindRepoRoot();
        AssertLauncherBinding(repoRoot);
        AssertLauncherPreset(repoRoot);

        using var engine = CapabilityStandardShowcaseTestHarness.CreateEngine(repoRoot, AcceptanceMods);
        Assert.That(engine.MergedConfig.StartupMapId, Is.EqualTo(MapId));

        var physics = CapabilityStandardShowcaseTestHarness.FindSystem<Physics2DSimulationSystem>(
            engine,
            SystemGroup.InputCollection);
        Assert.That(physics, Is.Not.Null);

        engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");
        Assert.That(spawnQueue.Count, Is.GreaterThan(0),
            "MapLoaded should enqueue the capability-standard Physics2D scenario through RuntimeEntitySpawnQueue.");

        var frameTimes = new List<double>();
        CapabilityStandardShowcaseTestHarness.TickUntil(
            engine,
            frameTimes,
            () => spawnQueue.Count == 0 && CountTemplate(engine, PolygonWallTemplateId) == 1,
            maxFrames: 12);

        Entity wall = CapabilityStandardShowcaseTestHarness.FindSingleByTemplate(engine, MapId, PolygonWallTemplateId);
        Assert.That(engine.World.Has<Collider2D>(wall), Is.True);
        Assert.That(engine.World.Get<Collider2D>(wall).Type, Is.EqualTo(ColliderType2D.Polygon));

        WriteReceipt(repoRoot, frameTimes.Count);
    }

    private static int CountTemplate(GameEngine engine, string templateId)
    {
        EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("EntityTemplateKeyRegistry missing.");
        if (!templateKeys.TryGetId(templateId, out int templateKeyId) || templateKeyId <= 0)
        {
            return 0;
        }

        int count = 0;
        var expectedMap = new MapId(MapId);
        var query = new QueryDescription().WithAll<EntityTemplateKeyRef, MapEntity>();
        engine.World.Query(in query, (ref EntityTemplateKeyRef keyRef, ref MapEntity mapEntity) =>
        {
            if (keyRef.TemplateKeyId == templateKeyId && mapEntity.MapId == expectedMap)
            {
                count++;
            }
        });

        return count;
    }

    private static void AssertLauncherBinding(string repoRoot)
    {
        string launcherConfig = File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json"));
        Assert.That(launcherConfig, Does.Contain($"\"name\": \"{BindingName}\""));
        Assert.That(launcherConfig, Does.Contain("mods/showcases/capability_standard/CapabilityStandardPhysics2DMod"));
    }

    private static void AssertLauncherPreset(string repoRoot)
    {
        string launcherPresets = File.ReadAllText(Path.Combine(repoRoot, "launcher.presets.json"));
        Assert.That(launcherPresets, Does.Contain($"\"id\": \"{PresetId}\""));
        Assert.That(launcherPresets, Does.Contain($"\"${BindingName}\""));
    }

    private static void WriteReceipt(string repoRoot, int framesToSpawn)
    {
        string artifactDir = Path.Combine(repoRoot, "artifacts", "showcases", "capability-standard-physics2d");
        Directory.CreateDirectory(artifactDir);
        File.WriteAllText(
            Path.Combine(artifactDir, "acceptance.txt"),
            $"binding={BindingName}{Environment.NewLine}map={MapId}{Environment.NewLine}framesToSpawn={framesToSpawn}{Environment.NewLine}");
    }

    private static string FindRepoRoot()
    {
        string? current = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "launcher.config.json")) &&
                Directory.Exists(Path.Combine(current, "mods")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate Ludots repo root.");
    }
}
