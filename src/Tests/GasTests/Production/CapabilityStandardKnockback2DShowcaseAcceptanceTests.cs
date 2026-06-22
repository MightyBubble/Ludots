using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
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
public sealed class CapabilityStandardKnockback2DShowcaseAcceptanceTests
{
    private const string ShowcaseModId = "CapabilityStandardKnockback2DMod";
    private const string MapId = "capability_standard_knockback2d";
    private const string SourceTemplateId = "capability_standard_knockback2d_source";
    private const string MovingTargetTemplateId = "capability_standard_knockback2d_moving_target";
    private const string StaticSourceTemplateId = "capability_standard_knockback2d_static_source";
    private const string StaticTargetTemplateId = "capability_standard_knockback2d_static_target";
    private const string BlockerSourceTemplateId = "capability_standard_knockback2d_blocker_source";
    private const string BlockerTargetTemplateId = "capability_standard_knockback2d_blocker_target";
    private const string BlockerWallTemplateId = "capability_standard_knockback2d_blocker_wall";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod",
        ShowcaseModId
    };

    [Test]
    public void CapabilityStandardKnockback2D_DisplacementSuppression_WritesKeyframeAcceptance()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        using var engine = CapabilityStandardShowcaseTestHarness.CreateEngine(repoRoot, AcceptanceMods);
        Assert.That(engine.MergedConfig.StartupMapId, Is.EqualTo(MapId));
        Assert.That(engine.MergedConfig.Physics2D.Enabled, Is.True);
        Assert.That(engine.MergedConfig.Navigation2D.Enabled, Is.False);
        Assert.That(engine.GetService(CoreServiceKeys.Navigation2DRuntime), Is.Null);

        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");

        engine.LoadMap(MapId);
        Assert.That(spawnQueue.Count, Is.EqualTo(7));

        var frameTimesMs = new List<double>(32);
        CapabilityStandardShowcaseTestHarness.TickUntil(engine, frameTimesMs, () => spawnQueue.Count == 0, maxFrames: 8);
        Assert.That(spawnQueue.Count, Is.EqualTo(0));

        Entity source = CapabilityStandardShowcaseTestHarness.FindSingleByTemplate(engine, MapId, SourceTemplateId);
        Entity movingTarget = CapabilityStandardShowcaseTestHarness.FindSingleByTemplate(engine, MapId, MovingTargetTemplateId);
        Entity staticSource = CapabilityStandardShowcaseTestHarness.FindSingleByTemplate(engine, MapId, StaticSourceTemplateId);
        Entity staticTarget = CapabilityStandardShowcaseTestHarness.FindSingleByTemplate(engine, MapId, StaticTargetTemplateId);
        Entity blockerSource = CapabilityStandardShowcaseTestHarness.FindSingleByTemplate(engine, MapId, BlockerSourceTemplateId);
        Entity blockerTarget = CapabilityStandardShowcaseTestHarness.FindSingleByTemplate(engine, MapId, BlockerTargetTemplateId);
        Entity blockerWall = CapabilityStandardShowcaseTestHarness.FindSingleByTemplate(engine, MapId, BlockerWallTemplateId);

        Assert.That(engine.World.Has<Collider2D>(blockerWall), Is.True);
        Assert.That(engine.World.Has<Position2D>(movingTarget), Is.True);
        Assert.That(engine.World.Has<Velocity2D>(movingTarget), Is.True);

        var keyframes = new List<KnockbackKeyframe>(16);
        Capture(engine, keyframes, frame: frameTimesMs.Count, movingTarget, staticTarget, blockerTarget);

        RunStaticAwayFromSource(engine, frameTimesMs, staticSource, staticTarget, keyframes, movingTarget, blockerTarget);
        RunMovingSuppressionRegression(engine, frameTimesMs, source, movingTarget, keyframes, staticTarget, blockerTarget);
        RunWallCorrection(engine, frameTimesMs, blockerSource, blockerTarget, blockerWall, keyframes, movingTarget, staticTarget);

        Assert.That(engine.World.Has<MovementSuppressed2D>(movingTarget), Is.False);
        Assert.That(engine.World.Has<NavDesiredVelocity2D>(movingTarget), Is.True);

        CapabilityStandardShowcaseTestHarness.TickMeasured(engine, 1, frameTimesMs);
        var recoveredVelocity = engine.World.Get<Velocity2D>(movingTarget).Linear;
        Assert.That(recoveredVelocity.X, Is.GreaterThan(Fix64.Zero),
            "After CC ends, NavDesiredVelocity2D should hand velocity back to Physics2D through the normal sync pass.");

        Physics2DPerfStats stats = CapabilityStandardShowcaseTestHarness.ReadPhysicsPerfStats(engine.World);
        WriteAcceptanceArtifacts(repoRoot, keyframes, frameTimesMs, stats, recoveredVelocity);
    }

    private static void RunStaticAwayFromSource(
        GameEngine engine,
        List<double> frameTimesMs,
        Entity staticSource,
        Entity staticTarget,
        List<KnockbackKeyframe> keyframes,
        Entity movingTarget,
        Entity blockerTarget)
    {
        Fix64Vec2 initial = engine.World.Get<Position2D>(staticTarget).Value;
        EntityCreationHelper.CreateDisplacement(engine.World, new DisplacementState
        {
            TargetEntity = staticTarget,
            SourceEntity = staticSource,
            DirectionMode = DisplacementDirectionMode.AwayFromSource,
            TotalDistanceCm = 180,
            RemainingDistanceCm = Fix64.FromInt(180),
            TotalDurationTicks = 6,
            RemainingTicks = 6,
            OverrideNavigation = true
        });

        TickUntilNoDisplacement(engine, frameTimesMs, maxFrames: 40);
        Fix64Vec2 final = engine.World.Get<Position2D>(staticTarget).Value;
        Assert.That(ToFloat(final.X), Is.EqualTo(ToFloat(initial.X + Fix64.FromInt(180))).Within(0.01f));
        Assert.That(ToFloat(final.Y), Is.EqualTo(ToFloat(initial.Y)).Within(0.01f));
        Assert.That(engine.World.Has<MovementSuppressed2D>(staticTarget), Is.False);
        Capture(engine, keyframes, frameTimesMs.Count, movingTarget, staticTarget, blockerTarget);
    }

    private static void RunMovingSuppressionRegression(
        GameEngine engine,
        List<double> frameTimesMs,
        Entity source,
        Entity movingTarget,
        List<KnockbackKeyframe> keyframes,
        Entity staticTarget,
        Entity blockerTarget)
    {
        ref var velocity = ref engine.World.Get<Velocity2D>(movingTarget);
        velocity.Linear = Fix64Vec2.FromInt(120, 0);
        if (!engine.World.Has<NavDesiredVelocity2D>(movingTarget))
        {
            engine.World.Add(movingTarget, new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.FromInt(120, 0) });
        }
        else
        {
            ref var desired = ref engine.World.Get<NavDesiredVelocity2D>(movingTarget);
            desired.ValueCmPerSec = Fix64Vec2.FromInt(120, 0);
        }

        if (!engine.World.Has<MovementSuppressed2D>(movingTarget))
        {
            engine.World.Add(movingTarget, new MovementSuppressed2D());
        }

        Fix64Vec2 initial = engine.World.Get<Position2D>(movingTarget).Value;
        EntityCreationHelper.CreateDisplacement(engine.World, new DisplacementState
        {
            TargetEntity = movingTarget,
            SourceEntity = source,
            DirectionMode = DisplacementDirectionMode.AwayFromSource,
            TotalDistanceCm = 200,
            RemainingDistanceCm = Fix64.FromInt(200),
            TotalDurationTicks = 4,
            RemainingTicks = 4,
            OverrideNavigation = true,
            MovementSuppressionApplied = true
        });

        TickUntilSuppressedVelocityCleared(engine, frameTimesMs, movingTarget, maxFrames: 12);

        int observedFrames = 0;
        while (HasActiveDisplacement(engine))
        {
            CapabilityStandardShowcaseTestHarness.TickMeasured(engine, 1, frameTimesMs);
            Fix64Vec2 actual = engine.World.Get<Position2D>(movingTarget).Value;
            Assert.That(ToFloat(actual.X), Is.LessThanOrEqualTo(ToFloat(initial.X + Fix64.FromInt(200)) + 0.01f));
            Assert.That(ToFloat(actual.Y), Is.EqualTo(ToFloat(initial.Y)).Within(0.01f));
            Assert.That(engine.World.Get<Velocity2D>(movingTarget).Linear, Is.EqualTo(Fix64Vec2.Zero));
            Capture(engine, keyframes, frameTimesMs.Count, movingTarget, staticTarget, blockerTarget);
            observedFrames++;

            if (observedFrames > 80)
            {
                Assert.Fail("Moving displacement did not complete within 80 frames.");
            }
        }

        Fix64Vec2 final = engine.World.Get<Position2D>(movingTarget).Value;
        Assert.That(ToFloat(final.X), Is.EqualTo(ToFloat(initial.X + Fix64.FromInt(200))).Within(0.01f),
            "Suppressed movement should contain only displacement distance, with no residual locomotion drift.");
        Assert.That(ToFloat(final.Y), Is.EqualTo(ToFloat(initial.Y)).Within(0.01f));
    }

    private static void RunWallCorrection(
        GameEngine engine,
        List<double> frameTimesMs,
        Entity blockerSource,
        Entity blockerTarget,
        Entity blockerWall,
        List<KnockbackKeyframe> keyframes,
        Entity movingTarget,
        Entity staticTarget)
    {
        EntityCreationHelper.CreateDisplacement(engine.World, new DisplacementState
        {
            TargetEntity = blockerTarget,
            SourceEntity = blockerSource,
            DirectionMode = DisplacementDirectionMode.AwayFromSource,
            TotalDistanceCm = 100,
            RemainingDistanceCm = Fix64.FromInt(100),
            TotalDurationTicks = 8,
            RemainingTicks = 8,
            OverrideNavigation = true
        });

        TickUntilNoDisplacement(engine, frameTimesMs, maxFrames: 80);
        CapabilityStandardShowcaseTestHarness.TickMeasured(engine, 4, frameTimesMs);
        Fix64 targetX = engine.World.Get<Position2D>(blockerTarget).Value.X;
        Fix64 wallX = engine.World.Get<Position2D>(blockerWall).Value.X;
        Assert.That(targetX, Is.LessThan(wallX),
            "Displacement should still be resolved by Physics2D collision/position correction when it reaches a static wall.");
        Capture(engine, keyframes, frameTimesMs.Count, movingTarget, staticTarget, blockerTarget);
    }

    private static void TickUntilNoDisplacement(GameEngine engine, List<double> frameTimesMs, int maxFrames)
    {
        CapabilityStandardShowcaseTestHarness.TickUntil(
            engine,
            frameTimesMs,
            () => !HasActiveDisplacement(engine),
            maxFrames);
    }

    private static void TickUntilSuppressedVelocityCleared(
        GameEngine engine,
        List<double> frameTimesMs,
        Entity entity,
        int maxFrames)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            CapabilityStandardShowcaseTestHarness.TickMeasured(engine, 1, frameTimesMs);
            if (engine.World.Has<MovementSuppressed2D>(entity) &&
                engine.World.Get<Velocity2D>(entity).Linear == Fix64Vec2.Zero)
            {
                return;
            }
        }

        Assert.Fail("Movement suppression did not clear locomotion velocity within the expected handshake window.");
    }

    private static bool HasActiveDisplacement(GameEngine engine)
    {
        bool found = false;
        var query = new QueryDescription().WithAll<DisplacementState>();
        engine.World.Query(in query, (Entity _) =>
        {
            found = true;
        });
        return found;
    }

    private static void Capture(
        GameEngine engine,
        List<KnockbackKeyframe> keyframes,
        int frame,
        Entity movingTarget,
        Entity staticTarget,
        Entity blockerTarget)
    {
        Position2D movingPosition = engine.World.Get<Position2D>(movingTarget);
        Velocity2D movingVelocity = engine.World.Get<Velocity2D>(movingTarget);
        Position2D staticPosition = engine.World.Get<Position2D>(staticTarget);
        Position2D blockerPosition = engine.World.Get<Position2D>(blockerTarget);
        bool suppressed = engine.World.Has<MovementSuppressed2D>(movingTarget);

        keyframes.Add(new KnockbackKeyframe(
            frame,
            ToFloat(movingPosition.Value.X),
            ToFloat(movingVelocity.Linear.X),
            suppressed,
            ToFloat(staticPosition.Value.X),
            ToFloat(blockerPosition.Value.X)));
    }

    private static void WriteAcceptanceArtifacts(
        string repoRoot,
        IReadOnlyList<KnockbackKeyframe> keyframes,
        IReadOnlyList<double> frameTimesMs,
        in Physics2DPerfStats stats,
        in Fix64Vec2 recoveredVelocity)
    {
        string artifactDir = Path.Combine(repoRoot, "artifacts", "showcases", "capability-standard-knockback2d");
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
        var builder = new StringBuilder();
        builder.AppendLine("# Capability Standard Knockback2D Acceptance");
        builder.AppendLine();
        builder.AppendLine("| Check | Evidence |");
        builder.AppendLine("| --- | --- |");
        builder.AppendLine("| Startup boundary | `physics2D.enabled=true`, `navigation2D.enabled=false`, no `Navigation2DRuntime` service |");
        builder.AppendLine("| Spawn path | `ConfigPipeline` catalog -> map focus event -> `RuntimeEntitySpawnQueue.EnqueueMany` -> `RuntimeEntitySpawnSystem` |");
        builder.AppendLine("| Static AwayFromSource displacement | target moved exactly 180 cm on X through `DisplacementRuntimeSystem` |");
        builder.AppendLine("| Moving CC no drift | suppressed target advanced 4 x 50 cm displacement steps with `Velocity2D.Linear=0` each frame |");
        builder.AppendLine($"| CC recovery | next sync restored velocity X `{Format(ToFloat(recoveredVelocity.X))}` cm/s from `NavDesiredVelocity2D` |");
        builder.AppendLine("| Wall correction | knockback into static wall stayed bounded by Physics2D position correction |");
        builder.AppendLine($"| Physics stats | Hz `{stats.PhysicsHz}`, potential pairs `{stats.PotentialPairs}`, contact pairs `{stats.ContactPairs}`, last update `{stats.PhysicsUpdateMs:F4}` ms |");
        builder.AppendLine($"| Test tick timings | frames `{frameTimesMs.Count}`, avg `{avgMs:F4}` ms, max `{maxMs:F4}` ms |");
        builder.AppendLine();
        builder.AppendLine("## Keyframes");
        builder.AppendLine();
        builder.AppendLine("| Frame | Moving X | Moving Vx | Suppressed | Static X | Wall Target X |");
        builder.AppendLine("| ---: | ---: | ---: | :---: | ---: | ---: |");
        for (int i = 0; i < keyframes.Count; i++)
        {
            KnockbackKeyframe keyframe = keyframes[i];
            builder.AppendLine(
                $"| {keyframe.Frame} | {Format(keyframe.MovingTargetX)} | {Format(keyframe.MovingTargetVelocityX)} | {keyframe.MovingTargetSuppressed} | {Format(keyframe.StaticTargetX)} | {Format(keyframe.BlockerTargetX)} |");
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

    private readonly record struct KnockbackKeyframe(
        int Frame,
        float MovingTargetX,
        float MovingTargetVelocityX,
        bool MovingTargetSuppressed,
        float StaticTargetX,
        float BlockerTargetX);
}
