using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[NonParallelizable]
public sealed class CapabilityStandardPhysics2DStressShowcaseAcceptanceTests
{
    private const string ShowcaseModId = "CapabilityStandardPhysics2DStressMod";
    private const string ConfigRelativePath = "CapabilityStandardPhysics2DStressConfig.json";
    private const string DynamicTemplateId = "capability_standard_physics2d_stress_dynamic_circle";
    private const string StaticTemplateId = "capability_standard_physics2d_stress_static_column";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod",
        ShowcaseModId
    };

    [Test]
    public void CapabilityStandardPhysics2DStress_ThroughputAndSteadyState0Alloc_WritesAcceptance()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        StressConfigSnapshot config = ReadConfig(repoRoot);
        using var engine = CapabilityStandardShowcaseTestHarness.CreateEngine(repoRoot, AcceptanceMods);
        Assert.That(engine.MergedConfig.StartupMapId, Is.EqualTo(config.MapId));
        Assert.That(engine.MergedConfig.Physics2D.Enabled, Is.True);
        Assert.That(engine.MergedConfig.Navigation2D.Enabled, Is.False);
        Assert.That(engine.GetService(CoreServiceKeys.Navigation2DRuntime), Is.Null);

        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");

        engine.LoadMap(config.MapId);
        Assert.That(spawnQueue.Count, Is.EqualTo(config.SpawnCount));

        var frameTimesMs = new List<double>(config.WarmupFrames + config.MeasuredFrames + 16);
        CapabilityStandardShowcaseTestHarness.TickUntil(engine, frameTimesMs, () => spawnQueue.Count == 0, maxFrames: 16);
        Assert.That(CountTemplate(engine, DynamicTemplateId), Is.EqualTo(config.DynamicBodies));
        Assert.That(CountTemplate(engine, StaticTemplateId), Is.EqualTo(config.StaticColumns));

        CapabilityStandardShowcaseTestHarness.TickMeasured(engine, config.WarmupFrames, frameTimesMs);
        long initialHash = HashDynamicBodies(engine.World, sampleLimit: 96);
        Physics2DPerfStats warmStats = CapabilityStandardShowcaseTestHarness.ReadPhysicsPerfStats(engine.World);
        Assert.That(warmStats.PhysicsHz, Is.GreaterThan(0));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        int measurementStart = frameTimesMs.Count;
        CapabilityStandardShowcaseTestHarness.TickMeasured(engine, config.MeasuredFrames, frameTimesMs);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Physics2DPerfStats finalStats = CapabilityStandardShowcaseTestHarness.ReadPhysicsPerfStats(engine.World);
        long finalHash = HashDynamicBodies(engine.World, sampleLimit: 96);
        double avgMeasuredMs = Average(frameTimesMs, measurementStart, frameTimesMs.Count - measurementStart);

        Assert.That(avgMeasuredMs, Is.LessThan(config.AvgStepBudgetMs),
            "Physics2D stress showcase should stay inside the configured endpoint throughput budget.");
        Assert.That(allocatedBytes, Is.LessThanOrEqualTo(config.AllocationBudgetBytes),
            "Pipeline-level steady-state measurement should stay within the configured allocation budget.");
        Assert.That(finalHash, Is.Not.EqualTo(initialHash), "Stress bodies should keep moving during the measured window.");
        Assert.That(finalStats.PotentialPairs, Is.GreaterThan(0));

        var keyframes = new[]
        {
            new StressKeyframe(0, warmStats.PotentialPairs, warmStats.ContactPairs, warmStats.PhysicsUpdateMs, initialHash),
            new StressKeyframe(config.MeasuredFrames, finalStats.PotentialPairs, finalStats.ContactPairs, finalStats.PhysicsUpdateMs, finalHash)
        };
        WriteAcceptanceArtifacts(repoRoot, config, keyframes, avgMeasuredMs, allocatedBytes, finalStats);
    }

    private static int CountTemplate(GameEngine engine, string templateId)
    {
        EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("EntityTemplateKeyRegistry missing.");
        int templateKeyId = templateKeys.GetId(templateId);
        int count = 0;
        var query = new QueryDescription().WithAll<EntityTemplateKeyRef>();
        engine.World.Query(in query, (ref EntityTemplateKeyRef keyRef) =>
        {
            if (keyRef.TemplateKeyId == templateKeyId)
            {
                count++;
            }
        });

        return count;
    }

    private static long HashDynamicBodies(World world, int sampleLimit)
    {
        long hash = 1469598103934665603L;
        int sampled = 0;
        var query = new QueryDescription().WithAll<Position2D, Velocity2D, Mass2D>();
        world.Query(in query, (ref Position2D position, ref Velocity2D velocity, ref Mass2D mass) =>
        {
            if (!mass.IsDynamic || sampled >= sampleLimit)
            {
                return;
            }

            Mix(ref hash, position.Value.X.RawValue);
            Mix(ref hash, position.Value.Y.RawValue);
            Mix(ref hash, velocity.Linear.X.RawValue);
            Mix(ref hash, velocity.Linear.Y.RawValue);
            sampled++;
        });

        Assert.That(sampled, Is.GreaterThan(0));
        return hash;
    }

    private static void Mix(ref long hash, long value)
    {
        unchecked
        {
            hash ^= value;
            hash *= 1099511628211L;
        }
    }

    private static double Average(IReadOnlyList<double> values, int start, int count)
    {
        double sum = 0d;
        for (int i = 0; i < count; i++)
        {
            sum += values[start + i];
        }

        return count > 0 ? sum / count : 0d;
    }

    private static StressConfigSnapshot ReadConfig(string repoRoot)
    {
        string configPath = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardPhysics2DStressMod",
            "assets",
            ConfigRelativePath);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
        JsonElement root = document.RootElement;
        int dynamicBodies = root.GetProperty("dynamicBodies").GetInt32();
        int staticColumns = root.GetProperty("staticColumns").GetInt32();
        return new StressConfigSnapshot(
            RequireString(root, "mapId"),
            dynamicBodies,
            staticColumns,
            dynamicBodies + staticColumns,
            root.GetProperty("acceptanceWarmupFrames").GetInt32(),
            root.GetProperty("acceptanceMeasuredFrames").GetInt32(),
            root.GetProperty("acceptanceAvgStepBudgetMs").GetDouble(),
            root.GetProperty("acceptanceSteadyStateAllocationBudgetBytes").GetInt64());
    }

    private static string RequireString(JsonElement root, string propertyName)
    {
        string? value = root.GetProperty(propertyName).GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Config property '{propertyName}' is required.");
        }

        return value;
    }

    private static void WriteAcceptanceArtifacts(
        string repoRoot,
        in StressConfigSnapshot config,
        IReadOnlyList<StressKeyframe> keyframes,
        double avgMeasuredMs,
        long allocatedBytes,
        in Physics2DPerfStats finalStats)
    {
        string artifactDir = Path.Combine(repoRoot, "artifacts", "showcases", "capability-standard-physics2d-stress");
        Directory.CreateDirectory(artifactDir);
        string jsonlPath = Path.Combine(artifactDir, "keyframes.jsonl");
        string mdPath = Path.Combine(artifactDir, "acceptance.md");

        var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
        using (var writer = new StreamWriter(jsonlPath, append: false, Encoding.UTF8))
        {
            for (int i = 0; i < keyframes.Count; i++)
            {
                writer.WriteLine(JsonSerializer.Serialize(keyframes[i], jsonOptions));
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine("# Capability Standard Physics2D Stress Acceptance");
        builder.AppendLine();
        builder.AppendLine("| Check | Evidence |");
        builder.AppendLine("| --- | --- |");
        builder.AppendLine("| Pure Physics2D startup | `physics2D.enabled=true`, `navigation2D.enabled=false`, no `Navigation2DRuntime` service |");
        builder.AppendLine($"| Spawn path | Config-driven RuntimeEntitySpawnQueue batch produced `{config.DynamicBodies}` dynamic bodies and `{config.StaticColumns}` static columns |");
        builder.AppendLine($"| Throughput budget | avg measured tick `{avgMeasuredMs.ToString("0.###", CultureInfo.InvariantCulture)}` ms, budget `{config.AvgStepBudgetMs.ToString("0.###", CultureInfo.InvariantCulture)}` ms |");
        builder.AppendLine($"| Pipeline steady-state allocation | measured `{allocatedBytes}` bytes over `{config.MeasuredFrames}` frames, budget `{config.AllocationBudgetBytes}` bytes |");
        builder.AppendLine("| #358 blind spot closure | This is a pipeline-level measurement; the existing 0Alloc unit tests remain static hot-path guards and are not treated as endpoint throughput proof. |");
        builder.AppendLine($"| Physics stats | Hz `{finalStats.PhysicsHz}`, potential pairs `{finalStats.PotentialPairs}`, contact pairs `{finalStats.ContactPairs}`, last update `{finalStats.PhysicsUpdateMs:F4}` ms |");
        builder.AppendLine();
        builder.AppendLine("## Keyframes");
        builder.AppendLine();
        builder.AppendLine("| Frame | Potential Pairs | Contact Pairs | Step Ms | Hash |");
        builder.AppendLine("| ---: | ---: | ---: | ---: | ---: |");
        for (int i = 0; i < keyframes.Count; i++)
        {
            StressKeyframe keyframe = keyframes[i];
            builder.AppendLine(
                $"| {keyframe.Frame} | {keyframe.PotentialPairs} | {keyframe.ContactPairs} | {keyframe.PhysicsUpdateMs.ToString("0.###", CultureInfo.InvariantCulture)} | {keyframe.DeterminismHash} |");
        }

        File.WriteAllText(mdPath, builder.ToString(), Encoding.UTF8);
    }

    private readonly record struct StressConfigSnapshot(
        string MapId,
        int DynamicBodies,
        int StaticColumns,
        int SpawnCount,
        int WarmupFrames,
        int MeasuredFrames,
        double AvgStepBudgetMs,
        long AllocationBudgetBytes);

    private readonly record struct StressKeyframe(
        int Frame,
        int PotentialPairs,
        int ContactPairs,
        double PhysicsUpdateMs,
        long DeterminismHash);
}
