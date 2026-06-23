using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[NonParallelizable]
public sealed class CapabilityStandardNavSink2DShowcaseAcceptanceTests
{
    private const string ShowcaseModId = "CapabilityStandardNavSink2DMod";
    private const string MapId = "capability_standard_nav_sink2d";
    private const string ConfigRelativePath = "CapabilityStandardNavSink2DConfig.json";
    private const string AgentTemplateId = "capability_standard_nav_sink2d_agent";
    private const string ObstacleTemplateId = "capability_standard_nav_sink2d_bridge_obstacle";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod",
        ShowcaseModId
    };

    [Test]
    public void CapabilityStandardNavSink2D_OrderSteeringAndObstacleBridge_WritesAcceptance()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        AssertShowcaseCatalog(repoRoot);
        AssertTemplateBoundaries(repoRoot);

        using var engine = CapabilityStandardShowcaseTestHarness.CreateEngine(repoRoot, AcceptanceMods);
        Assert.That(engine.MergedConfig.StartupMapId, Is.EqualTo(MapId));
        Assert.That(engine.MergedConfig.Physics2D.Enabled, Is.True);
        Assert.That(engine.MergedConfig.Navigation2D.Enabled, Is.True);
        Navigation2DRuntime navRuntime = engine.GetService(CoreServiceKeys.Navigation2DRuntime)
            ?? throw new InvalidOperationException("Navigation2DRuntime missing.");

        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");

        engine.LoadMap(MapId);
        Assert.That(spawnQueue.Count, Is.EqualTo(2));

        var frameTimesMs = new List<double>(128);
        CapabilityStandardShowcaseTestHarness.TickUntil(engine, frameTimesMs, () => spawnQueue.Count == 0, maxFrames: 8);
        Assert.That(spawnQueue.Count, Is.EqualTo(0));

        Entity agent = CapabilityStandardShowcaseTestHarness.FindSingleByTemplate(engine, MapId, AgentTemplateId);
        Entity obstacle = CapabilityStandardShowcaseTestHarness.FindSingleByTemplate(engine, MapId, ObstacleTemplateId);

        CapabilityStandardShowcaseTestHarness.TickUntil(
            engine,
            frameTimesMs,
            () => engine.World.Has<NavAgent2D>(agent) &&
                  engine.World.Has<Position2D>(agent) &&
                  engine.World.Has<Velocity2D>(agent) &&
                  engine.World.Has<NavObstacle2D>(obstacle) &&
                  engine.World.Has<Collider2D>(obstacle) &&
                  engine.World.Has<Physics2DStaticBodyState>(obstacle),
            maxFrames: 16);

        Assert.That(engine.World.Has<ManifestationObstacleIntent2D>(obstacle), Is.True);
        Assert.That(engine.World.Has<NavKinematics2D>(obstacle), Is.True);
        Assert.That(engine.World.Has<Mass2D>(obstacle), Is.True);
        Assert.That(engine.World.Get<Mass2D>(obstacle).IsStatic, Is.True);

        Fix64Vec2 initialPosition = engine.World.Get<Position2D>(agent).Value;
        SubmitMoveTo(engine, agent, 900, 0);

        var keyframes = new List<NavSinkKeyframe>(16)
        {
            Capture(engine, navRuntime, 0, agent, obstacle)
        };

        CapabilityStandardShowcaseTestHarness.TickUntil(
            engine,
            frameTimesMs,
            () => engine.World.Has<NavGoal2D>(agent) &&
                  engine.World.Get<NavGoal2D>(agent).Kind == NavGoalKind2D.Point,
            maxFrames: 16);
        keyframes.Add(Capture(engine, navRuntime, frameTimesMs.Count, agent, obstacle));

        CapabilityStandardShowcaseTestHarness.TickUntil(
            engine,
            frameTimesMs,
            () => engine.World.Has<NavDesiredVelocity2D>(agent) &&
                  engine.World.Get<NavDesiredVelocity2D>(agent).ValueCmPerSec.LengthSquared() > Fix64.Zero,
            maxFrames: 32);

        NavDesiredVelocity2D desiredAfterNav = engine.World.Get<NavDesiredVelocity2D>(agent);
        Velocity2D velocityAfterNav = engine.World.Get<Velocity2D>(agent);
        Assert.That(desiredAfterNav.ValueCmPerSec.X, Is.GreaterThan(Fix64.Zero),
            "Navigation2D should produce the steering output for the active move order.");
        Assert.That(velocityAfterNav.Linear.X, Is.GreaterThan(Fix64.Zero),
            "Physics2D velocity should be committed by NavToPhysicsVelocitySyncSystem, not by template authoring.");
        keyframes.Add(Capture(engine, navRuntime, frameTimesMs.Count, agent, obstacle));

        CapabilityStandardShowcaseTestHarness.TickMeasured(engine, 24, frameTimesMs);
        Fix64Vec2 finalPosition = engine.World.Get<Position2D>(agent).Value;
        WorldPositionCm finalWorldPosition = engine.World.Get<WorldPositionCm>(agent);
        Assert.That(finalPosition.X, Is.GreaterThan(initialPosition.X + Fix64.FromInt(80)),
            "Physics2D integration should own the actual position movement after Nav submits desired velocity.");
        Assert.That(finalWorldPosition.Value, Is.EqualTo(finalPosition),
            "PostMovement sync should copy Physics2D Position2D into WorldPositionCm.");
        Assert.That(navRuntime.AgentSoA.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(navRuntime.Config.Spatial.CellSizeCm, Is.GreaterThan(0));
        keyframes.Add(Capture(engine, navRuntime, frameTimesMs.Count, agent, obstacle));

        Physics2DPerfStats stats = CapabilityStandardShowcaseTestHarness.ReadPhysicsPerfStats(engine.World);
        WriteAcceptanceArtifacts(repoRoot, keyframes, frameTimesMs, stats);
    }

    private static void SubmitMoveTo(GameEngine engine, Entity actor, int xCm, int yCm)
    {
        OrderQueue orderQueue = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("OrderQueue missing.");
        OrderTypeRegistry orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("OrderTypeRegistry missing.");

        int moveTo = orderTypes.GetId("moveTo");
        Assert.That(moveTo, Is.GreaterThan(0));
        var order = new Order
        {
            Actor = actor,
            OrderTypeId = moveTo,
            SubmitMode = OrderSubmitMode.Immediate,
            PlayerId = 1,
            Args = new OrderArgs
            {
                Spatial = new OrderSpatial
                {
                    Kind = OrderSpatialKind.WorldCm,
                    Mode = OrderCollectionMode.Single,
                    WorldCm = new Vector3(xCm, 0f, yCm)
                }
            }
        };

        Assert.That(orderQueue.TryEnqueue(in order), Is.True);
    }

    private static NavSinkKeyframe Capture(
        GameEngine engine,
        Navigation2DRuntime navRuntime,
        int frame,
        Entity agent,
        Entity obstacle)
    {
        Fix64Vec2 position = engine.World.Has<Position2D>(agent)
            ? engine.World.Get<Position2D>(agent).Value
            : Fix64Vec2.Zero;
        Fix64Vec2 desired = engine.World.Has<NavDesiredVelocity2D>(agent)
            ? engine.World.Get<NavDesiredVelocity2D>(agent).ValueCmPerSec
            : Fix64Vec2.Zero;
        Fix64Vec2 velocity = engine.World.Has<Velocity2D>(agent)
            ? engine.World.Get<Velocity2D>(agent).Linear
            : Fix64Vec2.Zero;
        bool hasGoal = engine.World.Has<NavGoal2D>(agent) &&
            engine.World.Get<NavGoal2D>(agent).Kind == NavGoalKind2D.Point;
        bool obstacleNav = engine.World.Has<NavObstacle2D>(obstacle);
        bool obstaclePhysics = engine.World.Has<Physics2DStaticBodyState>(obstacle);

        return new NavSinkKeyframe(
            frame,
            ToFloat(position.X),
            ToFloat(position.Y),
            ToFloat(desired.X),
            ToFloat(desired.Y),
            ToFloat(velocity.X),
            ToFloat(velocity.Y),
            hasGoal,
            obstacleNav,
            obstaclePhysics,
            navRuntime.AgentSoA.Count);
    }

    private static void AssertShowcaseCatalog(string repoRoot)
    {
        string catalogPath = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardNavSink2DMod",
            "assets",
            "Configs",
            "config_catalog.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(catalogPath));
        AssertCatalogEntry(document.RootElement, ConfigRelativePath, "Replace", null);
        AssertCatalogEntry(document.RootElement, "Entities/templates.json", "ArrayById", "id");
    }

    private static void AssertCatalogEntry(JsonElement catalog, string path, string policy, string? idField)
    {
        foreach (JsonElement entry in catalog.EnumerateArray())
        {
            string? entryPath = entry.GetProperty("Path").GetString();
            if (!string.Equals(entryPath, path, StringComparison.Ordinal))
            {
                continue;
            }

            Assert.That(entry.GetProperty("Policy").GetString(), Is.EqualTo(policy));
            if (idField != null)
            {
                Assert.That(entry.GetProperty("IdField").GetString(), Is.EqualTo(idField));
            }

            return;
        }

        Assert.Fail($"Catalog entry '{path}' is missing.");
    }

    private static void AssertTemplateBoundaries(string repoRoot)
    {
        string templatePath = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardNavSink2DMod",
            "assets",
            "Entities",
            "templates.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(templatePath));
        foreach (JsonElement template in document.RootElement.EnumerateArray())
        {
            string id = RequireString(template, "id");
            JsonElement components = template.GetProperty("components");
            if (string.Equals(id, AgentTemplateId, StringComparison.Ordinal))
            {
                Assert.That(components.TryGetProperty("NavDesiredVelocity2D", out _), Is.False);
                Assert.That(components.TryGetProperty("NavGoal2D", out _), Is.False);
                Assert.That(components.TryGetProperty("NavObstacle2D", out _), Is.False);
                Assert.That(components.TryGetProperty("Position2D", out _), Is.False);
                Assert.That(components.TryGetProperty("Velocity2D", out _), Is.False);
            }

            if (string.Equals(id, ObstacleTemplateId, StringComparison.Ordinal))
            {
                Assert.That(components.TryGetProperty("ManifestationObstacleIntent2D", out _), Is.True);
                Assert.That(components.TryGetProperty("NavObstacle2D", out _), Is.False);
                Assert.That(components.TryGetProperty("Collider2D", out _), Is.False);
            }
        }
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
        IReadOnlyList<NavSinkKeyframe> keyframes,
        IReadOnlyList<double> frameTimesMs,
        in Physics2DPerfStats stats)
    {
        string artifactDir = Path.Combine(repoRoot, "artifacts", "showcases", "capability-standard-nav-sink2d");
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

        double maxMs = 0d;
        double sumMs = 0d;
        for (int i = 0; i < frameTimesMs.Count; i++)
        {
            double value = frameTimesMs[i];
            maxMs = Math.Max(maxMs, value);
            sumMs += value;
        }

        double avgMs = frameTimesMs.Count > 0 ? sumMs / frameTimesMs.Count : 0d;
        NavSinkKeyframe final = keyframes[^1];
        var builder = new StringBuilder();
        builder.AppendLine("# Capability Standard Nav Sink 2D Acceptance");
        builder.AppendLine();
        builder.AppendLine("| Check | Evidence |");
        builder.AppendLine("| --- | --- |");
        builder.AppendLine("| Startup boundary | `physics2D.enabled=true`, `navigation2D.enabled=true`, `Navigation2DRuntime` service present |");
        builder.AppendLine("| Spawn path | `ConfigPipeline` catalog -> map focus event -> `RuntimeEntitySpawnQueue.EnqueueMany` -> `RuntimeEntitySpawnSystem` |");
        builder.AppendLine("| Agent boundary | template authors order/input facts; `NavOrderAgentBootstrapSystem` derives `NavAgent2D`, `Position2D`, `Velocity2D`, `NavKinematics2D` |");
        builder.AppendLine("| Nav steering boundary | active `moveTo` order produces `NavDesiredVelocity2D`; Physics2D sync commits `Velocity2D` |");
        builder.AppendLine("| Obstacle bridge | authored `ManifestationObstacleIntent2D` derives `Collider2D`, `Physics2DStaticBodyState`, and `NavObstacle2D` |");
        builder.AppendLine($"| Position authority | final Position2D X `{Format(final.AgentX)}` cm, WorldPositionCm synced from Physics2D |");
        builder.AppendLine($"| Runtime counts | nav agents `{final.NavAgentCount}`, obstacle nav `{final.ObstacleNav}`, obstacle physics `{final.ObstaclePhysics}` |");
        builder.AppendLine($"| Physics stats | Hz `{stats.PhysicsHz}`, potential pairs `{stats.PotentialPairs}`, contact pairs `{stats.ContactPairs}`, last update `{stats.PhysicsUpdateMs:F4}` ms |");
        builder.AppendLine($"| Test tick timings | frames `{frameTimesMs.Count}`, avg `{avgMs:F4}` ms, max `{maxMs:F4}` ms |");
        builder.AppendLine();
        builder.AppendLine("## Keyframes");
        builder.AppendLine();
        builder.AppendLine("| Frame | Agent X | Desired X | Velocity X | Goal | Nav Obstacle | Physics Obstacle | Nav Agents |");
        builder.AppendLine("| ---: | ---: | ---: | ---: | :---: | :---: | :---: | ---: |");
        for (int i = 0; i < keyframes.Count; i++)
        {
            NavSinkKeyframe keyframe = keyframes[i];
            builder.AppendLine(
                $"| {keyframe.Frame} | {Format(keyframe.AgentX)} | {Format(keyframe.DesiredX)} | {Format(keyframe.VelocityX)} | {keyframe.HasGoal} | {keyframe.ObstacleNav} | {keyframe.ObstaclePhysics} | {keyframe.NavAgentCount} |");
        }

        File.WriteAllText(mdPath, builder.ToString(), Encoding.UTF8);
    }

    private static float ToFloat(Fix64 value)
    {
        return value.ToFloat();
    }

    private static string Format(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private readonly record struct NavSinkKeyframe(
        int Frame,
        float AgentX,
        float AgentY,
        float DesiredX,
        float DesiredY,
        float VelocityX,
        float VelocityY,
        bool HasGoal,
        bool ObstacleNav,
        bool ObstaclePhysics,
        int NavAgentCount);
}
