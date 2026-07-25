using System;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using CapabilityStandardPhysics3DShowcaseMod.Runtime;
using Ludots.Core.Character3D;
using Ludots.Core.Physics3D;
using Ludots.Core.Traversal3D;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class Physics3DShowcaseTests
{
    [Test]
    public void ShowcaseConfig_IsStrictAndOfficialPresetsFitOwnedCapacity()
    {
        JsonObject json = LoadOfficialShowcaseJson();

        Physics3DShowcaseConfig config = Physics3DShowcaseConfig.Load(json);
        Assert.That(config.InitialScene, Is.EqualTo(Physics3DShowcaseScene.ScannerRange));
        Assert.That(config.MaximumBodies, Is.EqualTo(50_001));
        Assert.That(config.BenchmarkPresets, Is.EqualTo(new[] { 1_000, 10_000, 25_000, 50_000 }));
        Assert.That(config.ScaleCity.PerformanceWindowSampleCount, Is.EqualTo(120));

        JsonObject unknownField = (JsonObject)json.DeepClone();
        unknownField["silentFallback"] = true;
        Assert.Throws<System.Text.Json.JsonException>(() => Physics3DShowcaseConfig.Load(unknownField));

        JsonObject numericEnum = (JsonObject)json.DeepClone();
        numericEnum["initialScene"] = 3;
        Assert.Throws<System.Text.Json.JsonException>(() => Physics3DShowcaseConfig.Load(numericEnum));
    }

    [Test]
    public void SimulationSystem_PauseAndSingleStep_AdvanceExactlyOneThirtyHzStep()
    {
        using World ecsWorld = World.Create();
        using var physicsWorld = new Physics3DWorld(CreateWorldConfig(16, 4));
        var simulation = new Physics3DSimulationSystem(
            ecsWorld,
            physicsWorld,
            sourceFixedStepHz: 30,
            maximumPhysicsStepsPerSourceTick: 1);
        simulation.Enabled = false;

        simulation.Update(1f / 30f);
        Assert.That(simulation.PhysicsStepsLastUpdate, Is.Zero);
        Assert.That(simulation.TotalPhysicsSteps, Is.Zero);

        simulation.RequestManualSteps(1);
        simulation.Update(1f / 30f);
        Assert.That(simulation.PhysicsStepsLastUpdate, Is.EqualTo(1));
        Assert.That(simulation.TotalPhysicsSteps, Is.EqualTo(1));

        simulation.Update(1f / 30f);
        Assert.That(simulation.PhysicsStepsLastUpdate, Is.Zero);
        Assert.That(simulation.TotalPhysicsSteps, Is.EqualTo(1));

        simulation.Enabled = true;
        for (int i = 0; i < 10; i++)
        {
            simulation.Update(1f / 30f);
            Assert.That(simulation.PhysicsStepsLastUpdate, Is.EqualTo(1));
        }

        Assert.That(simulation.TotalPhysicsSteps, Is.EqualTo(11));
    }

    [Test]
    public void AllPlayerScenes_CreateRunAndExposeTheirCapabilityEvidence()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 1_200,
            benchmarkPresets: new[] { 100, 200, 500, 1_000 },
            replaySteps: 30);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(1_300, 256));

        foreach (Physics3DShowcaseScene scene in Enum.GetValues<Physics3DShowcaseScene>())
        {
            harness.SelectScene(scene);
            Assert.That(harness.Runtime.ActiveScene, Is.EqualTo(scene));
            Assert.That(harness.Runtime.BodyCount, Is.GreaterThan(0), $"{scene} must create visible physics content.");
            harness.Step();
        }

        harness.SelectScene(Physics3DShowcaseScene.ScannerRange);
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.RunScannerQuery));
        harness.Step();
        int totalQueryHits = 0;
        for (int i = 0; i < 7; i++)
        {
            totalQueryHits += harness.Runtime.GetQueryHitCount(i);
        }

        Assert.That(totalQueryHits, Is.GreaterThan(0));

        harness.SelectScene(Physics3DShowcaseScene.ConstraintForge);
        Assert.That(harness.Runtime.ConstraintCount, Is.GreaterThan(0));
        Assert.That(harness.PhysicsWorld.ActiveConstraintCount, Is.EqualTo(harness.Runtime.ConstraintCount));
    }

    [Test]
    public void Feature_ScannerRange_Scenario_PlayerChoosesOneScanLayerAndDistanceThenResets()
    {
        // Given a new player enters Scanner Range with no scan silently running for them.
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 16);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(320, 64));
        harness.SelectScene(Physics3DShowcaseScene.ScannerRange);
        Assert.That(harness.Runtime.ScannerHasResult, Is.False);

        // When the player chooses Box Cast, the longest distance, Amber targets, and presses Run Scan.
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
            Physics3DShowcaseCommandKind.SetScannerQueryKind,
            (int)Physics3DShowcaseQueryKind.BoxCast));
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.SetScannerDistancePreset, 2));
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.SetScannerLayerFilter, 0));
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.RunScannerQuery));
        harness.Step();
        Physics3DShowcasePanelState scanned = harness.Runtime.CapturePanelState();

        // Then only the chosen scan publishes its ordered matching hits.
        Assert.Multiple(() =>
        {
            Assert.That(scanned.ScannerQueryKind, Is.EqualTo(Physics3DShowcaseQueryKind.BoxCast));
            Assert.That(scanned.ScannerDistanceCm, Is.EqualTo(config.ScannerRange.DistancePresetsCm[2]));
            Assert.That(scanned.ScannerLayerFilterName, Is.EqualTo(config.ScannerRange.LayerFilters[0].Name));
            Assert.That(scanned.ScannerHasResult, Is.True);
            Assert.That(scanned.ScannerQueryFailed, Is.False);
            Assert.That(scanned.ScannerQueries.BoxCastHits, Is.EqualTo(2));
            Assert.That(harness.Runtime.GetQueryHitCount(0), Is.Zero);
        });
        Assert.That(harness.Runtime.TryGetQueryHitVisual(1, 0, out Physics3DShowcaseQueryHitVisual nearest), Is.True);
        Assert.That(harness.Runtime.TryGetQueryHitVisual(1, 1, out Physics3DShowcaseQueryHitVisual farther), Is.True);
        Assert.That(nearest.DistanceCm, Is.LessThan(farther.DistanceCm));

        // When Reset is pressed, then the authored choices return and no stale scan remains.
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.Reset));
        harness.Step();
        Physics3DShowcasePanelState reset = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(reset.ScannerQueryKind, Is.EqualTo(config.ScannerRange.InitialQueryKind));
            Assert.That(reset.ScannerHasResult, Is.False);
            Assert.That(reset.ScannerRunSequence, Is.Zero);
            Assert.That(reset.LastAction, Does.Contain("Reset Scanner Range"));
        });
    }

    [Test]
    public void Feature_ScannerRange_Scenario_PlayerSingleStepsCapsuleSweepFromStartingOverlapToOrderedCompletion()
    {
        // Given the player pauses Scanner Range and chooses the capsule sweep that starts inside target #1.
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 16);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(320, 64));
        harness.SelectScene(Physics3DShowcaseScene.ScannerRange);
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.TogglePause));
        harness.Step();
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
            Physics3DShowcaseCommandKind.SetScannerQueryKind,
            (int)Physics3DShowcaseQueryKind.CapsuleCast));
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
            Physics3DShowcaseCommandKind.SetScannerDistancePreset,
            config.ScannerRange.DistancePresetsCm.Length - 1));
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
            Physics3DShowcaseCommandKind.SetScannerLayerFilter,
            config.ScannerRange.LayerFilters.Length - 1));
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.RunScannerQuery));
        harness.Step();

        // Then the formal result is ready, but the paused playhead remains at the origin with only #1 revealed.
        Physics3DShowcasePanelState started = harness.Runtime.CapturePanelState();
        int queryIndex = (int)Physics3DShowcaseQueryKind.CapsuleCast - 1;
        Assert.That(harness.Runtime.TryGetQueryVisual(queryIndex, out Physics3DShowcaseQueryVisual query), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(started.Paused, Is.True);
            Assert.That(started.ScannerPlaybackStatus, Is.EqualTo(Physics3DScannerPlaybackStatus.Playing));
            Assert.That(started.ScannerPlaybackTick, Is.Zero);
            Assert.That(started.ScannerPlaybackDistanceCm, Is.Zero);
            Assert.That(query.HitCount, Is.EqualTo(config.ScannerRange.TargetCount));
            Assert.That(query.VisibleHitCount, Is.EqualTo(1));
        });
        Assert.That(harness.Runtime.TryGetQueryHitVisual(queryIndex, 0, out Physics3DShowcaseQueryHitVisual first), Is.True);
        Assert.That(first.StartedOverlapping, Is.True, "The red crossed #1 marker must explain a cast starting inside a target.");
        Assert.That(first.DistanceCm, Is.Zero.Within(0.001f));

        float previousDistanceCm = float.NegativeInfinity;
        for (int hitIndex = 0; hitIndex < query.HitCount; hitIndex++)
        {
            Assert.That(harness.Runtime.TryGetQueryHitVisual(queryIndex, hitIndex, out Physics3DShowcaseQueryHitVisual hit), Is.True);
            Assert.That(hit.DistanceCm, Is.GreaterThanOrEqualTo(previousDistanceCm));
            previousDistanceCm = hit.DistanceCm;
        }

        // When the player presses Single Step, exactly one 30 Hz playback frame advances and the world stays paused.
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.SingleStep));
        harness.Step();
        Physics3DShowcasePanelState oneStep = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(oneStep.Paused, Is.True);
            Assert.That(oneStep.ScannerPlaybackTick, Is.EqualTo(1));
            Assert.That(oneStep.ScannerPlaybackDistanceCm, Is.EqualTo(
                oneStep.ScannerDistanceCm / config.ScannerRange.CastPlaybackDurationTicks).Within(0.001f));
            Assert.That(oneStep.ScannerVisibleHitCount, Is.EqualTo(1));
        });

        // When the player single-steps to the end, then #1..#N appear in distance order and playback completes.
        for (int tick = 1; tick < config.ScannerRange.CastPlaybackDurationTicks; tick++)
        {
            harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.SingleStep));
            harness.Step();
        }
        Physics3DShowcasePanelState completed = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(completed.Paused, Is.True);
            Assert.That(completed.ScannerPlaybackStatus, Is.EqualTo(Physics3DScannerPlaybackStatus.Complete));
            Assert.That(completed.ScannerPlaybackTick, Is.EqualTo(config.ScannerRange.CastPlaybackDurationTicks));
            Assert.That(completed.ScannerVisibleHitCount, Is.EqualTo(query.HitCount));
            Assert.That(completed.LastAction, Does.Contain("numbered in the world"));
        });

        // When Reset Station is pressed, then the playhead, numbering, result, and authored selection reset together.
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.Reset));
        harness.Step();
        Physics3DShowcasePanelState reset = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(reset.ScannerPlaybackStatus, Is.EqualTo(Physics3DScannerPlaybackStatus.Waiting));
            Assert.That(reset.ScannerPlaybackTick, Is.Zero);
            Assert.That(reset.ScannerVisibleHitCount, Is.Zero);
            Assert.That(reset.ScannerHasResult, Is.False);
            Assert.That(reset.ScannerQueryKind, Is.EqualTo(config.ScannerRange.InitialQueryKind));
        });
    }

    [Test]
    public void Feature_ScannerRange_Scenario_OverlapPulseStaysAtItsAuthoredOrigin()
    {
        // Given the player pauses and runs a sphere overlap.
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 16);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(320, 64));
        harness.SelectScene(Physics3DShowcaseScene.ScannerRange);
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.TogglePause));
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
            Physics3DShowcaseCommandKind.SetScannerQueryKind,
            (int)Physics3DShowcaseQueryKind.SphereOverlap));
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.RunScannerQuery));
        harness.Step();
        int queryIndex = (int)Physics3DShowcaseQueryKind.SphereOverlap - 1;
        Assert.That(harness.Runtime.TryGetQueryVisual(queryIndex, out Physics3DShowcaseQueryVisual initial), Is.True);

        // When the player advances one pulse quarter-cycle, then only scale changes; origin and distance stay fixed.
        int steps = config.ScannerRange.OverlapPulseCycleTicks / 4;
        for (int i = 0; i < steps; i++)
        {
            harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.SingleStep));
            harness.Step();
        }
        Assert.That(harness.Runtime.TryGetQueryVisual(queryIndex, out Physics3DShowcaseQueryVisual pulsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.ScannerPlaybackStatus, Is.EqualTo(Physics3DScannerPlaybackStatus.Pulsing));
            Assert.That(pulsed.OriginCm, Is.EqualTo(initial.OriginCm));
            Assert.That(pulsed.PlaybackDistanceCm, Is.Zero);
            Assert.That(pulsed.PulseScale, Is.GreaterThan(1f));
            Assert.That(pulsed.VisibleHitCount, Is.EqualTo(pulsed.HitCount));
        });
    }

    [Test]
    public void ScannerRange_CastPlaybackSteadyFixedTicks_DoNotAllocateOnCallingThread()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 16);
        config.ScannerRange.CastPlaybackDurationTicks = 120;
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(320, 64));
        harness.SelectScene(Physics3DShowcaseScene.ScannerRange);
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
            Physics3DShowcaseCommandKind.SetScannerQueryKind,
            (int)Physics3DShowcaseQueryKind.CapsuleCast));
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.RunScannerQuery));
        harness.Step();
        for (int i = 0; i < 16; i++)
        {
            harness.Step();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 30; i++)
        {
            harness.Step();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(allocated, Is.Zero);
            Assert.That(harness.Runtime.ScannerPlaybackStatus, Is.EqualTo(Physics3DScannerPlaybackStatus.Playing));
        });
    }

    [Test]
    public void Feature_ScannerRange_Scenario_EachChoicePublishesOnlyItsOwnOrderedHits()
    {
        // Given the authored Scanner Range has four casts and three overlap lanes.
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 16);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(320, 64));

        // When the player enters the range and actively runs each available scan choice.
        harness.SelectScene(Physics3DShowcaseScene.ScannerRange);

        // Then every cast reports all targets in distance order, and every overlap is visibly marked.
        Physics3DShowcaseQueryKind[] expectedKinds =
        {
            Physics3DShowcaseQueryKind.Ray,
            Physics3DShowcaseQueryKind.BoxCast,
            Physics3DShowcaseQueryKind.SphereCast,
            Physics3DShowcaseQueryKind.CapsuleCast,
            Physics3DShowcaseQueryKind.BoxOverlap,
            Physics3DShowcaseQueryKind.SphereOverlap,
            Physics3DShowcaseQueryKind.CapsuleOverlap
        };
        bool observedStartedOverlap = false;
        for (int queryIndex = 0; queryIndex < expectedKinds.Length; queryIndex++)
        {
            harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
                Physics3DShowcaseCommandKind.SetScannerQueryKind,
                (int)expectedKinds[queryIndex]));
            harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
                Physics3DShowcaseCommandKind.SetScannerDistancePreset,
                config.ScannerRange.DistancePresetsCm.Length - 1));
            harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
                Physics3DShowcaseCommandKind.SetScannerLayerFilter,
                2));
            harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.RunScannerQuery));
            harness.Step();
            Assert.That(
                harness.Runtime.TryGetQueryVisual(queryIndex, out Physics3DShowcaseQueryVisual query),
                Is.True,
                $"Scanner lane {queryIndex} must have a visible path.");
            Assert.Multiple(() =>
            {
                Assert.That(query.Kind, Is.EqualTo(expectedKinds[queryIndex]));
                Assert.That(query.HitCount, Is.GreaterThan(0), $"{query.Kind} must expose at least one readable hit.");
                Assert.That(query.HasFirstHit, Is.True);
                Assert.That(harness.Runtime.ScannerQueryIndex, Is.EqualTo(queryIndex));
                AssertFinite(query.OriginCm, $"{query.Kind} origin");
                AssertFinite(query.FirstHitPositionCm, $"{query.Kind} first hit");
                if (!query.IsOverlap)
                {
                    Assert.That(query.HitCount, Is.EqualTo(config.ScannerRange.TargetCount));
                    Assert.That(float.IsFinite(query.DistanceCm), Is.True);
                    Assert.That(query.DistanceCm, Is.GreaterThan(0f));
                }
            });

            float previousDistanceCm = float.NegativeInfinity;
            for (int hitIndex = 0; hitIndex < query.HitCount; hitIndex++)
            {
                Assert.That(
                    harness.Runtime.TryGetQueryHitVisual(
                        queryIndex,
                        hitIndex,
                        out Physics3DShowcaseQueryHitVisual hit),
                    Is.True,
                    $"{query.Kind} hit {hitIndex} must be readable.");
                Assert.Multiple(() =>
                {
                    AssertFinite(hit.PositionCm, $"{query.Kind} hit {hitIndex} position");
                    AssertFinite(hit.Normal, $"{query.Kind} hit {hitIndex} normal");
                    Assert.That(float.IsFinite(hit.DistanceCm), Is.True);
                    Assert.That(hit.DistanceCm, Is.GreaterThanOrEqualTo(previousDistanceCm));
                    if (!query.IsOverlap)
                    {
                        Assert.That(hit.DistanceCm, Is.InRange(0f, query.DistanceCm));
                        if (hit.StartedOverlapping)
                        {
                            Assert.That(hit.DistanceCm, Is.Zero.Within(0.001f));
                            Assert.That(hit.Normal, Is.EqualTo(Vector3.Zero));
                        }
                        else
                        {
                            Assert.That(hit.Normal.Length(), Is.EqualTo(1f).Within(1e-4f));
                        }
                    }
                    else
                    {
                        Assert.That(hit.StartedOverlapping, Is.True);
                    }
                });
                previousDistanceCm = hit.DistanceCm;
                observedStartedOverlap |= hit.StartedOverlapping;
            }

            Assert.That(
                harness.Runtime.TryGetQueryHitVisual(queryIndex, query.HitCount, out _),
                Is.False,
                $"{query.Kind} must not silently expose stale hits beyond its reported count.");
        }

        Assert.That(observedStartedOverlap, Is.True, "Scanner Range must make a starting overlap observable.");
    }

    [Test]
    public void Feature_ScannerRange_Scenario_AllTargetsCapacityFailureIsVisibleAndNeverTruncated()
    {
        // Given All Targets can see more authored bodies than the fixed result buffer can hold.
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 16);
        config.QueryHitCapacity = 1;
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(320, 64));
        harness.SelectScene(Physics3DShowcaseScene.ScannerRange);

        // When the player runs the longest Ray against All Targets.
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
            Physics3DShowcaseCommandKind.SetScannerDistancePreset,
            config.ScannerRange.DistancePresetsCm.Length - 1));
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.SetScannerLayerFilter, 2));
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.RunScannerQuery));
        harness.Step();

        // Then the panel reports a hard capacity failure and exposes no partial result.
        Physics3DShowcasePanelState panel = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(panel.ScannerQueryFailed, Is.True);
            Assert.That(panel.ScannerHasResult, Is.False);
            Assert.That(harness.Runtime.GetQueryHitCount(0), Is.Zero);
            Assert.That(panel.LastAction, Does.Contain("Scan failed").And.Contain("capacity").And.Contain("No result was truncated"));
        });
    }

    [Test]
    public void Feature_ConstraintForge_Scenario_AllNineAdvancedConstraintKindsRunInTheFixedFrameLoop()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 16);
        int legacyConstraintCount = ((config.ChainLinkCount - 1) * 2) +
                                    Math.Max(2, config.ChainLinkCount / 2);
        const int forgeSupportPivotCount = 3;
        const int advancedConstraintKindCount = 9;
        int expectedConstraintCount = legacyConstraintCount +
                                      forgeSupportPivotCount +
                                      advancedConstraintKindCount;
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(320, 64));

        harness.SelectScene(Physics3DShowcaseScene.ConstraintForge);
        for (int step = 0; step < 120; step++)
        {
            harness.Step();
        }

        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.ConstraintCount, Is.EqualTo(expectedConstraintCount));
            Assert.That(harness.PhysicsWorld.ActiveConstraintCount, Is.EqualTo(expectedConstraintCount));
            Assert.That(harness.Runtime.ActiveScene, Is.EqualTo(Physics3DShowcaseScene.ConstraintForge));
            Assert.That(harness.Simulation.TotalPhysicsSteps, Is.EqualTo(121));
        });
    }

    [Test]
    public void Feature_ConstraintForge_Scenario_PlayerPausesReversesRestartsAndResetsTheDrives()
    {
        // Given the player enters a running Constraint Forge and can see the door, slider, and servo move.
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 16);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(320, 64));
        harness.SelectScene(Physics3DShowcaseScene.ConstraintForge);
        for (int step = 0; step < 30; step++) harness.Step();
        Physics3DShowcasePanelState running = harness.Runtime.CapturePanelState();
        Assert.That(harness.Runtime.TryGetConstraintForgePlayerState(
            out _, out _, out _), Is.True);

        // When the player pauses, reverses while held, and starts the drives again.
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.ToggleConstraintDrive));
        harness.Step();
        Physics3DShowcasePanelState paused = harness.Runtime.CapturePanelState();
        Assert.That(harness.Runtime.TryGetConstraintForgePlayerState(
            out float pausedSlider,
            out float pausedDoorSpeed,
            out float pausedServo), Is.True);
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.ReverseConstraintDrive));
        harness.Step();
        Physics3DShowcasePanelState reversedWhilePaused = harness.Runtime.CapturePanelState();
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.ToggleConstraintDrive));
        harness.Step();
        for (int step = 0; step < 24; step++) harness.Step();
        Physics3DShowcasePanelState restarted = harness.Runtime.CapturePanelState();
        Assert.That(harness.Runtime.TryGetConstraintForgePlayerState(
            out float restartedSlider,
            out float restartedDoorSpeed,
            out float restartedServo), Is.True);

        // Then the panel reflects every state and the real constrained bodies visibly change after restart.
        Assert.Multiple(() =>
        {
            Assert.That(running.ConstraintDriveEnabled, Is.True);
            Assert.That(paused.ConstraintDriveEnabled, Is.False);
            Assert.That(paused.ConstraintSummary, Does.Contain("PAUSED").And.Contain("door").And.Contain("slider").And.Contain("servo"));
            Assert.That(reversedWhilePaused.ConstraintDriveEnabled, Is.False);
            Assert.That(reversedWhilePaused.ConstraintDriveDirection, Is.EqualTo(Physics3DShowcaseDriveDirection.Reverse));
            Assert.That(restarted.ConstraintDriveEnabled, Is.True);
            Assert.That(restarted.ConstraintDriveDirection, Is.EqualTo(Physics3DShowcaseDriveDirection.Reverse));
            Assert.That(
                MathF.Abs(restartedSlider - pausedSlider) > 0.01f ||
                MathF.Abs(restartedDoorSpeed - pausedDoorSpeed) > 0.01f ||
                MathF.Abs(restartedServo - pausedServo) > 0.001f,
                Is.True,
                "Restarting reversed drives must visibly change at least one real constrained body.");
        });

        // When Reset is pressed, then authored running-forward state is restored.
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.Reset));
        harness.Step();
        Physics3DShowcasePanelState reset = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(reset.ConstraintDriveEnabled, Is.EqualTo(config.ConstraintForge.InitialDriveEnabled));
            Assert.That(reset.ConstraintDriveDirection, Is.EqualTo(config.ConstraintForge.InitialDriveDirection));
            Assert.That(reset.LastAction, Does.Contain("Reset Constraint Forge"));
            Assert.That(reset.ConstraintSummary, Does.Contain("RUNNING").And.Contain("FORWARD"));
        });
    }

    [Test]
    public void ConstraintForge_InsufficientConstraintCapacityFailsExplicitly()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 16);
        int requiredConstraintCount = ((config.ChainLinkCount - 1) * 2) +
                                      Math.Max(2, config.ChainLinkCount / 2) +
                                      3 +
                                      9;
        using var harness = new ShowcaseHarness(
            config,
            CreateWorldConfig(320, 64, constraintCapacity: requiredConstraintCount - 1));

        Assert.Throws<Physics3DCapacityExceededException>(
            () => harness.SelectScene(Physics3DShowcaseScene.ConstraintForge));
    }

    [Test]
    [Explicit("Allocation gate is run deliberately after the functional Constraint Forge suite.")]
    public void ConstraintForge_WarmedThirtyHzFixedFramesAllocateZeroBytesOnCallingThread()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 16);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(320, 64));
        harness.SelectScene(Physics3DShowcaseScene.ConstraintForge);
        for (int step = 0; step < 120; step++)
        {
            harness.Step();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int step = 0; step < 30; step++)
        {
            harness.Step();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero);
    }

    [Test]
    public void Feature_ScaleCity_Scenario_CityPulseMovesOnlyTheForegroundAndResetsAcrossPopulationChanges()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 16);
        config.ScaleCity.InteractiveBodyLimit = 32;
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(320, 64));
        harness.SelectBenchmark(64);
        harness.CompletePreparedStep();
        long stepBeforeShockWave = harness.PhysicsWorld.StepIndex;
        Assert.That(
            harness.Runtime.TryGetBodyVisual(
                1,
                out Physics3DBodyState beforeShockWave,
                out Physics3DBodyKind bodyKind,
                out _,
                out _,
                out _,
                out _),
            Is.True);
        Assert.That(bodyKind, Is.EqualTo(Physics3DBodyKind.Dynamic));
        int foregroundBodies = harness.Runtime.ScaleCityState.InteractiveBodies;
        int firstSparseVisualIndex = 1 + foregroundBodies;
        Assert.That(
            harness.Runtime.TryGetBodyVisual(
                firstSparseVisualIndex,
                out Physics3DBodyState sparseBeforePulse,
                out _,
                out _,
                out _,
                out _,
                out _),
            Is.True);

        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.Impact));
        harness.Runtime.PrepareFixedStep();
        Assert.That(
            harness.Runtime.TryGetBodyVisual(1, out Physics3DBodyState queuedShockWave, out _, out _, out _, out _, out _),
            Is.True);

        Physics3DShowcasePanelState queued = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.ActiveScene, Is.EqualTo(Physics3DShowcaseScene.ScaleCity));
            Assert.That(harness.PhysicsWorld.StepIndex, Is.EqualTo(stepBeforeShockWave));
            Assert.That(
                harness.PhysicsWorld.PendingActuationCommandCount,
                Is.EqualTo(foregroundBodies * 2),
                "City Pulse must queue one impulse and one wind command per foreground body, with no background command.");
            Assert.That(queued.ScaleCity.PulseCount, Is.EqualTo(1));
            Assert.That(queued.ScaleCity.PulsedForegroundBodiesLastPulse, Is.EqualTo(foregroundBodies));
            Assert.That(queued.LastAction, Does.StartWith("City Pulse 1").And.Contain("background paths were untouched"));
            Assert.That(queuedShockWave.PositionCm, Is.EqualTo(beforeShockWave.PositionCm));
            Assert.That(queuedShockWave.Orientation, Is.EqualTo(beforeShockWave.Orientation));
            Assert.That(queuedShockWave.LinearVelocityCmPerSecond, Is.EqualTo(beforeShockWave.LinearVelocityCmPerSecond));
            Assert.That(queuedShockWave.AngularVelocityRadiansPerSecond, Is.EqualTo(beforeShockWave.AngularVelocityRadiansPerSecond));
        });

        harness.CompletePreparedStep();
        Assert.That(
            harness.Runtime.TryGetBodyVisual(1, out Physics3DBodyState appliedShockWave, out _, out _, out _, out _, out _),
            Is.True);
        Assert.That(
            harness.Runtime.TryGetBodyVisual(
                firstSparseVisualIndex,
                out Physics3DBodyState sparseAfterPulse,
                out _,
                out _,
                out _,
                out _,
                out _),
            Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(harness.PhysicsWorld.PendingActuationCommandCount, Is.Zero);
            Assert.That(harness.PhysicsWorld.StepIndex, Is.EqualTo(stepBeforeShockWave + 1));
            Assert.That(appliedShockWave.LinearVelocityCmPerSecond, Is.Not.EqualTo(beforeShockWave.LinearVelocityCmPerSecond));
            Assert.That(
                sparseAfterPulse.LinearVelocityCmPerSecond.X,
                Is.EqualTo(sparseBeforePulse.LinearVelocityCmPerSecond.X).Within(0.001f));
            Assert.That(
                sparseAfterPulse.LinearVelocityCmPerSecond.Z,
                Is.EqualTo(sparseBeforePulse.LinearVelocityCmPerSecond.Z).Within(0.001f));
        });

        // When Reset and then another population preset are selected, the observable pulse activity returns to zero.
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.Reset));
        harness.Step();
        Assert.That(harness.Runtime.ScaleCityState.PulseCount, Is.Zero);
        harness.SelectBenchmark(96);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.DynamicBodyCount, Is.EqualTo(96));
            Assert.That(harness.Runtime.ScaleCityState.PulseCount, Is.Zero);
            Assert.That(harness.Runtime.ScaleCityState.PulsedForegroundBodiesLastPulse, Is.Zero);
        });
    }

    [Test]
    public void ScaleCity_ShockWaveActuationCapacityFailureIsExplicit()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 16);
        config.ScaleCity.InteractiveBodyLimit = 16;
        using var harness = new ShowcaseHarness(
            config,
            CreateWorldConfig(320, 64, actuationCommandCapacity: 31));
        harness.SelectBenchmark(64);
        harness.CompletePreparedStep();
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.Impact));

        Assert.Throws<Physics3DCapacityExceededException>(() => harness.Runtime.PrepareFixedStep());
    }

    [Test]
    public void PlatformStation_PlayerStartsSupportedThenMovesAndJumps()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 1_200,
            benchmarkPresets: new[] { 100, 200, 500, 1_000 },
            replaySteps: 30);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(1_300, 256));
        harness.SelectScene(Physics3DShowcaseScene.PlatformStation);
        Character3DState initial = harness.Runtime.GetPlayerCharacterStateForTests();

        for (int i = 0; i < 6; i++)
        {
            harness.Runtime.SetCharacterIntentForTests(Vector2.UnitX, jumpRequested: false, traverseRequested: false);
            harness.Step();
        }

        Character3DState moved = harness.Runtime.GetPlayerCharacterStateForTests();
        harness.Runtime.SetCharacterIntentForTests(Vector2.UnitX, jumpRequested: true, traverseRequested: false);
        harness.Step();
        Character3DState jumped = harness.Runtime.GetPlayerCharacterStateForTests();

        Assert.Multiple(() =>
        {
            Assert.That(initial.IsGrounded, Is.True, "The player must begin on top of the authored start deck.");
            Assert.That(moved.PositionCm.X, Is.GreaterThan(initial.PositionCm.X + config.BodySizeCm));
            Assert.That(jumped.LinearVelocityCmPerSecond.Y, Is.GreaterThan(0f));
        });
    }

    [Test]
    public void PlatformStation_UsesFormalConveyorAndOneWayPlatformPolicies()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 1_200,
            benchmarkPresets: new[] { 100, 200, 500, 1_000 },
            replaySteps: 30);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(1_300, 256));
        harness.SelectScene(Physics3DShowcaseScene.PlatformStation);

        var bodyIds = new Physics3DBodyId[harness.PhysicsWorld.ActiveBodyCount];
        int bodyCount = harness.PhysicsWorld.CopyActiveBodyIds(bodyIds);
        int conveyorCount = 0;
        int oneWayCount = 0;
        for (int i = 0; i < bodyCount; i++)
        {
            Physics3DBodyContactPolicy policy = harness.PhysicsWorld.GetBodyContactPolicy(bodyIds[i]);
            if (policy.Kind == Physics3DBodyContactPolicyKind.SurfaceVelocity)
            {
                conveyorCount++;
                Assert.That(
                    policy.LocalSurfaceVelocityCmPerSecond,
                    Is.EqualTo(new Vector3(config.CharacterTraversal.PlatformStationConveyorSpeedCmPerSecond, 0f, 0f)));
            }
            else if (policy.Kind == Physics3DBodyContactPolicyKind.OneWayPlatform)
            {
                oneWayCount++;
                Assert.That(policy.LocalPlatformNormal, Is.EqualTo(Vector3.UnitY));
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(conveyorCount, Is.EqualTo(1), "Platform Station must contain one real surface-velocity conveyor.");
            Assert.That(oneWayCount, Is.EqualTo(1), "Platform Station must contain one real one-way platform.");
        });
    }

    [Test]
    public void Feature_PlatformStation_Scenario_PlayerClearsFourLiveSurfacesAndCanRestartAfterFailure()
    {
        // Given a new player enters a timed route with four named live surfaces.
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 1_200,
            benchmarkPresets: new[] { 100, 200, 500, 1_000 },
            replaySteps: 30);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(1_300, 256));
        harness.SelectScene(Physics3DShowcaseScene.PlatformStation);
        Physics3DShowcasePanelState initial = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(initial.CharacterRouteStatus, Is.EqualTo(Physics3DShowcaseRouteStatus.InProgress));
            Assert.That(initial.CharacterRouteCheckpointCount, Is.EqualTo(4));
            Assert.That(initial.CharacterRouteNextAction, Does.Contain("moving lift"));
        });

        // When the player lands on the moving lift, turntable, conveyor, and one-way finish in order.
        for (int checkpoint = 0; checkpoint < 4; checkpoint++)
        {
            harness.Runtime.PlacePlayerOnPlatformCheckpointForTests(checkpoint);
            for (int settle = 0;
                 settle < 4 && harness.Runtime.CharacterRouteCheckpointIndex == checkpoint;
                 settle++)
            {
                harness.Step();
            }

            Assert.That(
                harness.Runtime.CharacterRouteCheckpointIndex,
                Is.EqualTo(checkpoint + 1),
                $"The real support body for checkpoint {checkpoint + 1} must advance the route exactly once.");
        }

        // Then completion is visible and a reset starts a fresh run rather than preserving old progress.
        Physics3DShowcasePanelState completed = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(completed.CharacterRouteStatus, Is.EqualTo(Physics3DShowcaseRouteStatus.Completed));
            Assert.That(completed.CharacterRouteSummary, Does.StartWith("COMPLETE"));
            Assert.That(completed.LastAction, Does.Contain("Route complete"));
        });
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.Reset));
        harness.Step();
        Physics3DShowcasePanelState restarted = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(restarted.CharacterRouteStatus, Is.EqualTo(Physics3DShowcaseRouteStatus.InProgress));
            Assert.That(restarted.CharacterRouteCheckpointIndex, Is.Zero);
            Assert.That(restarted.LastAction, Does.Contain("Reset Platform Station"));
        });
    }

    [Test]
    public void Feature_CharacterRoutes_Scenario_TimeoutFailureIsVisibleAndRestartClearsIt()
    {
        // Given the platform route has a short authored time limit for this boundary test.
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 1_200,
            benchmarkPresets: new[] { 100, 200, 500, 1_000 },
            replaySteps: 30);
        config.CharacterTraversal.PlatformRouteTimeLimitTicks = 2;
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(1_300, 256));
        harness.SelectScene(Physics3DShowcaseScene.PlatformStation);

        // When time expires before the first checkpoint, then the panel exposes failure and demands a restart.
        harness.Step();
        Physics3DShowcasePanelState failed = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(failed.CharacterRouteStatus, Is.EqualTo(Physics3DShowcaseRouteStatus.Failed));
            Assert.That(failed.CharacterRouteSummary, Does.StartWith("FAILED"));
            Assert.That(failed.LastAction, Does.Contain("Time expired").And.Contain("Restart Route"));
        });

        // When Restart Route is requested, then failure and progress are cleared together.
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.Reset));
        harness.Runtime.PrepareFixedStep();
        Physics3DShowcasePanelState restarted = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(restarted.CharacterRouteStatus, Is.EqualTo(Physics3DShowcaseRouteStatus.InProgress));
            Assert.That(restarted.CharacterRouteCheckpointIndex, Is.Zero);
            Assert.That(restarted.CharacterRouteTicksRemaining, Is.EqualTo(2));
        });
    }

    [Test]
    public void TraversalCourse_PlayerRunsToLadderAttachesAndClimbs()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 1_200,
            benchmarkPresets: new[] { 100, 200, 500, 1_000 },
            replaySteps: 30);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(1_300, 256));
        harness.SelectScene(Physics3DShowcaseScene.TraversalCourse);

        float attachReadyX = config.CharacterTraversal.LadderCenterXCm -
                             (config.CharacterTraversal.AttachProbeDistanceCm * 0.9f);
        int steps = 0;
        while (harness.Runtime.GetPlayerCharacterStateForTests().PositionCm.X < attachReadyX && steps < 300)
        {
            harness.Runtime.SetCharacterIntentForTests(Vector2.UnitX, jumpRequested: false, traverseRequested: false);
            harness.Step();
            steps++;
        }

        Character3DState atLadder = harness.Runtime.GetPlayerCharacterStateForTests();
        Assert.That(atLadder.PositionCm.X, Is.GreaterThanOrEqualTo(attachReadyX),
            $"The authored route must let a new player reach the ladder without teleporting. " +
            $"position={atLadder.PositionCm}, velocity={atLadder.LinearVelocityCmPerSecond}, " +
            $"grounded={atLadder.IsGrounded}, stepAssist={atLadder.StepAssistActive}, steps={steps}.");

        harness.Runtime.SetCharacterIntentForTests(Vector2.Zero, jumpRequested: false, traverseRequested: true);
        harness.Step();
        Assert.That(
            harness.Runtime.GetPlayerTraversalStatusForTests().State,
            Is.EqualTo(Traversal3DState.Attached));

        harness.Runtime.SetCharacterIntentForTests(Vector2.Zero, jumpRequested: false, traverseRequested: false);
        harness.Step();
        Assert.That(
            harness.Runtime.GetPlayerTraversalStatusForTests().State,
            Is.EqualTo(Traversal3DState.Climbing));

        harness.Runtime.SetCharacterIntentForTests(Vector2.UnitY, jumpRequested: false, traverseRequested: false);
        harness.Step();

        int ladderClimbSteps = 0;
        while (harness.Runtime.GetPlayerTraversalStatusForTests().State != Traversal3DState.LedgeHang &&
               ladderClimbSteps < 120)
        {
            harness.Runtime.SetCharacterIntentForTests(Vector2.UnitY, jumpRequested: false, traverseRequested: false);
            harness.Step();
            ladderClimbSteps++;
        }

        Assert.That(
            harness.Runtime.GetPlayerTraversalStatusForTests().State,
            Is.EqualTo(Traversal3DState.LedgeHang),
            "Climbing the ladder must reach a validated ledge hang instead of stopping below the deck.");
        harness.Runtime.SetCharacterIntentForTests(Vector2.UnitY, jumpRequested: false, traverseRequested: false);
        harness.Step();
        Assert.That(harness.Runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.Mantling));

        int ladderMantleSteps = 0;
        while (harness.Runtime.GetPlayerTraversalStatusForTests().State != Traversal3DState.NormalMovement &&
               ladderMantleSteps < 120)
        {
            harness.Runtime.SetCharacterIntentForTests(Vector2.UnitY, jumpRequested: false, traverseRequested: false);
            harness.Step();
            ladderMantleSteps++;
        }

        Character3DState afterLadder = harness.Runtime.GetPlayerCharacterStateForTests();
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.NormalMovement));
            Assert.That(afterLadder.PositionCm.Y, Is.GreaterThan(config.CharacterTraversal.LadderDeckCenterYCm));
        });

        float wallAttachReadyX = config.CharacterTraversal.WallCenterXCm -
                                 (config.CharacterTraversal.AttachProbeDistanceCm * 0.9f);
        bool gapJumped = false;
        int wallApproachSteps = 0;
        while (harness.Runtime.GetPlayerCharacterStateForTests().PositionCm.X < wallAttachReadyX &&
               wallApproachSteps < 180)
        {
            float x = harness.Runtime.GetPlayerCharacterStateForTests().PositionCm.X;
            bool jump = !gapJumped &&
                        x >= config.CharacterTraversal.LadderDeckCenterXCm +
                             (config.CharacterTraversal.LadderDeckLengthCm * 0.3f);
            gapJumped |= jump;
            harness.Runtime.SetCharacterIntentForTests(Vector2.UnitX, jump, traverseRequested: false);
            harness.Step();
            wallApproachSteps++;
        }

        Assert.That(
            harness.Runtime.GetPlayerCharacterStateForTests().PositionCm.X,
            Is.GreaterThanOrEqualTo(wallAttachReadyX),
            "The authored deck gap must be jumpable on the way from the ladder to the climbing wall.");
        harness.Runtime.SetCharacterIntentForTests(Vector2.Zero, jumpRequested: false, traverseRequested: true);
        harness.Step();
        Assert.That(harness.Runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.Attached));
        harness.Runtime.SetCharacterIntentForTests(Vector2.Zero, jumpRequested: false, traverseRequested: false);
        harness.Step();
        Assert.That(harness.Runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.Climbing));

        int wallClimbSteps = 0;
        while (harness.Runtime.GetPlayerTraversalStatusForTests().State != Traversal3DState.LedgeHang &&
               wallClimbSteps < 150)
        {
            harness.Runtime.SetCharacterIntentForTests(Vector2.UnitY, jumpRequested: false, traverseRequested: false);
            harness.Step();
            wallClimbSteps++;
        }

        Assert.That(
            harness.Runtime.GetPlayerTraversalStatusForTests().State,
            Is.EqualTo(Traversal3DState.LedgeHang),
            "Climbing the wall must finish at the authored ledge.");
        harness.Runtime.SetCharacterIntentForTests(Vector2.UnitY, jumpRequested: false, traverseRequested: false);
        harness.Step();
        int wallMantleSteps = 0;
        while (harness.Runtime.GetPlayerTraversalStatusForTests().State != Traversal3DState.NormalMovement &&
               wallMantleSteps < 120)
        {
            harness.Runtime.SetCharacterIntentForTests(Vector2.UnitY, jumpRequested: false, traverseRequested: false);
            harness.Step();
            wallMantleSteps++;
        }

        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.NormalMovement));
            Assert.That(
                harness.Runtime.GetPlayerCharacterStateForTests().PositionCm.Y,
                Is.GreaterThan(config.CharacterTraversal.WallDeckCenterYCm));
            Assert.That(
                harness.Runtime.CharacterRouteStatus,
                Is.EqualTo(Physics3DShowcaseRouteStatus.Completed),
                $"Traversal route stopped at checkpoint {harness.Runtime.CharacterRouteCheckpointIndex}/" +
                $"{harness.Runtime.CharacterRouteCheckpointCount}: {harness.Runtime.CharacterRouteNextAction}");
        });
    }

    [Test]
    public void BenchmarkScene_RemainsVisiblyInMotionInsteadOfOnlyDroppingOnce()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 100, 150, 200, 250 },
            replaySteps: 16);
        config.ScaleCity.InteractiveBodyLimit = 32;
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(320, 32));
        harness.SelectBenchmark(100);

        Vector3[] initial = CaptureDynamicPositions(harness.Runtime, Physics3DShapeKind.Box);
        int interactiveBodies = harness.Runtime.ScaleCityState.InteractiveBodies;
        bool observedRelaunch = false;
        for (int i = 0; i < config.BenchmarkCycleSteps + 30; i++)
        {
            harness.Step();
            observedRelaunch |= harness.Runtime.BenchmarkRecycledBodiesLastStep > 0;
        }

        Vector3[] afterOneSecond = CaptureDynamicPositions(harness.Runtime, Physics3DShapeKind.Box);
        int movingInteractiveBodies = 0;
        for (int i = 0; i < interactiveBodies; i++)
        {
            if (Vector3.Distance(afterOneSecond[i], initial[i]) >= config.BodySizeCm)
            {
                movingInteractiveBodies++;
            }
        }

        int movingSparseBodies = 0;
        for (int i = interactiveBodies; i < initial.Length; i++)
        {
            if (MathF.Abs(afterOneSecond[i].X - initial[i].X) >= config.BodySizeCm)
            {
                movingSparseBodies++;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                movingInteractiveBodies,
                Is.GreaterThanOrEqualTo((int)(interactiveBodies * 0.75f)),
                "The foreground city must remain visibly driven by wind and launcher waves.");
            Assert.That(
                movingSparseBodies,
                Is.GreaterThanOrEqualTo((int)((initial.Length - interactiveBodies) * 0.9f)),
                "The sparse scale stream must remain visibly active instead of dropping once and stopping.");
            Assert.That(observedRelaunch, Is.True, "The benchmark never relaunched its completed stream wave.");
            Assert.That(harness.PhysicsWorld.AwakeBodyCount, Is.EqualTo(initial.Length));
        });
    }

    [Test]
    public void ScaleCity_ConfiguredForegroundCollidesWhileSparseBodiesKeepUniqueContactFreePaths()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 64, 96, 128, 192 },
            replaySteps: 16);
        config.ScaleCity.InteractiveBodyLimit = 96;
        config.ScaleCity.InteractiveColumns = 8;
        config.ScaleCity.InteractiveRows = 4;
        config.ScaleCity.WindCycleTicks = 24;
        config.ScaleCity.LauncherWaveCount = 12;
        config.ScaleCity.LauncherIntervalTicks = 4;

        using var harness = new ShowcaseHarness(config, CreateWorldConfig(320, 32));
        harness.SelectBenchmark(128);
        harness.CompletePreparedStep();

        Physics3DScaleCityShowcaseState initialStatus = harness.Runtime.ScaleCityState;
        QueryDescription synchronizedPoseQuery = new QueryDescription()
            .WithAll<Physics3DBodyCm, Physics3DPoseCm, PreviousPhysics3DPoseCm>();
        int synchronizedPoseBodies = harness.EcsWorld.CountEntities(in synchronizedPoseQuery);
        Assert.That(
            harness.Runtime.TryGetBodyVisual(1, out Physics3DBodyState initialForeground, out _, out _, out _, out _, out _),
            Is.True);

        int peakContactPairs = 0;
        int interactiveRelaunchTotal = 0;
        int sparseRecycleTotal = 0;
        float minimumWind = float.PositiveInfinity;
        float maximumWind = float.NegativeInfinity;
        var contacts = new Physics3DContactPair[512];
        for (int step = 0; step < config.BenchmarkCycleSteps + config.ScaleCity.LauncherIntervalTicks; step++)
        {
            harness.Step();
            Physics3DScaleCityShowcaseState status = harness.Runtime.ScaleCityState;
            Assert.That(status.TotalBodies, Is.EqualTo(128));
            peakContactPairs = Math.Max(peakContactPairs, status.ContactPairs);
            interactiveRelaunchTotal += status.InteractiveRelaunchedBodiesLastStep;
            sparseRecycleTotal += status.SparseRecycledBodiesLastStep;
            minimumWind = MathF.Min(minimumWind, status.WindAccelerationXCmPerSecondSquared);
            maximumWind = MathF.Max(maximumWind, status.WindAccelerationXCmPerSecondSquared);

            int contactCount = harness.PhysicsWorld.CopyContactPairs(contacts);
            for (int contactIndex = 0; contactIndex < contactCount; contactIndex++)
            {
                Physics3DContactPair contact = contacts[contactIndex];
                Assert.That(
                    harness.Runtime.IsScaleCitySparseBody(contact.BodyA) ||
                    harness.Runtime.IsScaleCitySparseBody(contact.BodyB),
                    Is.False,
                    "A sparse scale body left its unique path and entered a contact pair.");
            }
        }

        Assert.That(
            harness.Runtime.TryGetBodyVisual(1, out Physics3DBodyState movedForeground, out _, out _, out _, out _, out _),
            Is.True);
        Physics3DScaleCityShowcaseState finalStatus = harness.Runtime.ScaleCityState;
        Physics3DShowcasePanelState finalPanel = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(initialStatus.InteractiveBodies, Is.EqualTo(config.ScaleCity.InteractiveBodyLimit));
            Assert.That(initialStatus.SparseBodies, Is.EqualTo(128 - config.ScaleCity.InteractiveBodyLimit));
            Assert.That(initialStatus.PerformanceSampleCount, Is.EqualTo(1));
            Assert.That(initialStatus.PerformanceStatus, Is.EqualTo(Physics3DScaleCityPerformanceStatus.Warming));
            Assert.That(harness.Runtime.DynamicBodyCount, Is.EqualTo(128));
            Assert.That(harness.Runtime.BodyCount, Is.EqualTo(129));
            Assert.That(
                synchronizedPoseBodies,
                Is.EqualTo(initialStatus.InteractiveBodies + 1),
                "Scale City background bodies must stay authoritative in Physics3D without duplicating every sparse pose into ECS.");
            Assert.That(peakContactPairs, Is.GreaterThan(0), "The foreground city never formed visible contacts.");
            Assert.That(interactiveRelaunchTotal, Is.GreaterThan(0), "The foreground launcher never advanced a wave.");
            Assert.That(sparseRecycleTotal, Is.GreaterThan(0), "The sparse stream never recycled a completed path.");
            Assert.That(minimumWind, Is.LessThan(0f));
            Assert.That(maximumWind, Is.GreaterThan(0f));
            Assert.That(finalStatus.LastLauncherWaveIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(movedForeground.PositionCm, Is.Not.EqualTo(initialForeground.PositionCm));
            Assert.That(finalPanel.ScaleCity, Is.EqualTo(finalStatus));
        });
    }

    [Test]
    public void ScaleCity_FixedStepWindLaunchAndSparseRecycleDoNotAllocateOnCallingThread()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 160,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 16);
        config.ScaleCity.InteractiveBodyLimit = 32;
        config.ScaleCity.InteractiveColumns = 8;
        config.ScaleCity.InteractiveRows = 4;
        config.ScaleCity.WindCycleTicks = 30;
        config.ScaleCity.LauncherWaveCount = 4;
        config.ScaleCity.LauncherIntervalTicks = 5;

        using var harness = new ShowcaseHarness(config, CreateWorldConfig(192, 32));
        harness.SelectBenchmark(64);
        harness.CompletePreparedStep();
        for (int step = 0; step < config.BenchmarkCycleSteps + config.ScaleCity.LauncherIntervalTicks; step++)
        {
            harness.Step();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int step = 0; step < 30; step++)
        {
            harness.Step();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero);
    }

    [Test]
    public void ScaleCity_PerformanceWindowComputesPercentilesAndResetsAcrossScaleAndStationChanges()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 160,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 16);
        config.ScaleCity.InteractiveBodyLimit = 16;
        config.ScaleCity.PerformanceWindowSampleCount = 5;
        config.BenchmarkRealTimeBudgetMilliseconds = 5.5f;

        using var harness = new ShowcaseHarness(config, CreateWorldConfig(192, 32));
        harness.Simulation.Enabled = false;
        harness.SelectBenchmark(64);
        for (int sample = 1; sample <= 4; sample++)
        {
            harness.Runtime.RecordScaleCityPerformanceSampleForTests(sample, sample + 2d);
        }

        Physics3DScaleCityShowcaseState warming = harness.Runtime.ScaleCityState;
        Assert.Multiple(() =>
        {
            Assert.That(warming.PerformanceSampleCount, Is.EqualTo(4));
            Assert.That(warming.FramePerformanceSampleCount, Is.EqualTo(4));
            Assert.That(warming.PerformanceWindowCapacity, Is.EqualTo(5));
            Assert.That(warming.StepP50Milliseconds, Is.EqualTo(2.5d).Within(1e-9));
            Assert.That(warming.StepP95Milliseconds, Is.EqualTo(3.85d).Within(1e-9));
            Assert.That(warming.StepP99Milliseconds, Is.EqualTo(3.97d).Within(1e-9));
            Assert.That(warming.FullFrameP50Milliseconds, Is.EqualTo(4.5d).Within(1e-9));
            Assert.That(warming.FullFrameP95Milliseconds, Is.EqualTo(5.85d).Within(1e-9));
            Assert.That(warming.FullFrameP99Milliseconds, Is.EqualTo(5.97d).Within(1e-9));
            Assert.That(warming.PerformanceStatus, Is.EqualTo(Physics3DScaleCityPerformanceStatus.Warming));
            Assert.That(
                Physics3DShowcasePanelController.ScaleCityPerformanceStatusLabel(warming.PerformanceStatus),
                Is.EqualTo("WARMING"));
        });

        harness.Runtime.RecordScaleCityPerformanceSampleForTests(5d, 7d);
        Physics3DScaleCityShowcaseState p99OverBudget = harness.Runtime.ScaleCityState;
        Assert.Multiple(() =>
        {
            Assert.That(p99OverBudget.StepP50Milliseconds, Is.EqualTo(3d).Within(1e-9));
            Assert.That(p99OverBudget.StepP95Milliseconds, Is.EqualTo(4.8d).Within(1e-9));
            Assert.That(p99OverBudget.StepP99Milliseconds, Is.EqualTo(4.96d).Within(1e-9));
            Assert.That(p99OverBudget.StepP99Milliseconds, Is.LessThan(config.BenchmarkRealTimeBudgetMilliseconds));
            Assert.That(p99OverBudget.FullFrameP50Milliseconds, Is.EqualTo(5d).Within(1e-9));
            Assert.That(p99OverBudget.FullFrameP95Milliseconds, Is.EqualTo(6.8d).Within(1e-9));
            Assert.That(p99OverBudget.FullFrameP99Milliseconds, Is.EqualTo(6.96d).Within(1e-9));
            Assert.That(p99OverBudget.FullFrameP99Milliseconds, Is.GreaterThan(config.BenchmarkRealTimeBudgetMilliseconds));
            Assert.That(p99OverBudget.PerformanceStatus, Is.EqualTo(Physics3DScaleCityPerformanceStatus.OverBudget));
            Assert.That(
                Physics3DShowcasePanelController.ScaleCityPerformanceStatusLabel(p99OverBudget.PerformanceStatus),
                Is.EqualTo("OVER BUDGET"));
        });

        harness.SelectBenchmark(96);
        Physics3DScaleCityShowcaseState afterScaleChange = harness.Runtime.ScaleCityState;
        Assert.That(afterScaleChange.PerformanceSampleCount, Is.Zero);
        Assert.That(afterScaleChange.FramePerformanceSampleCount, Is.Zero);
        for (int sample = 0; sample < config.ScaleCity.PerformanceWindowSampleCount; sample++)
        {
            harness.Runtime.RecordScaleCityPerformanceSampleForTests(4d, 4.5d);
        }

        Physics3DScaleCityShowcaseState passing = harness.Runtime.ScaleCityState;
        Assert.Multiple(() =>
        {
            Assert.That(passing.PerformanceStatus, Is.EqualTo(Physics3DScaleCityPerformanceStatus.Pass));
            Assert.That(Physics3DShowcasePanelController.ScaleCityPerformanceStatusLabel(passing.PerformanceStatus), Is.EqualTo("PASS"));
            Assert.That(Physics3DShowcasePanelController.ScaleCityWindDirectionLabel(1f), Is.EqualTo("RIGHT"));
            Assert.That(Physics3DShowcasePanelController.ScaleCityWindDirectionLabel(-1f), Is.EqualTo("LEFT"));
            Assert.That(Physics3DShowcasePanelController.ScaleCityWindDirectionLabel(0f), Is.EqualTo("CALM"));
            Assert.That(Physics3DShowcasePanelController.ScaleCityPopulationLabel(in passing),
                Does.Contain("foreground").And.Contain("background"));
            Assert.That(Physics3DShowcasePanelController.ScaleCityActivityLabel(in passing),
                Does.Contain("foreground launched").And.Contain("background recycled"));
        });

        harness.Runtime.RecordScaleCityPerformanceSampleForTests(100d, 100d);
        Physics3DScaleCityShowcaseState rolledWindow = harness.Runtime.ScaleCityState;
        Assert.Multiple(() =>
        {
            Assert.That(rolledWindow.PerformanceSampleCount, Is.EqualTo(config.ScaleCity.PerformanceWindowSampleCount));
            Assert.That(rolledWindow.FramePerformanceSampleCount, Is.EqualTo(config.ScaleCity.PerformanceWindowSampleCount));
            Assert.That(rolledWindow.StepP50Milliseconds, Is.EqualTo(4d).Within(1e-9));
            Assert.That(rolledWindow.StepP95Milliseconds, Is.EqualTo(80.8d).Within(1e-9));
            Assert.That(rolledWindow.StepP99Milliseconds, Is.EqualTo(96.16d).Within(1e-9));
            Assert.That(rolledWindow.FullFrameP50Milliseconds, Is.EqualTo(4.5d).Within(1e-9));
            Assert.That(rolledWindow.PerformanceStatus, Is.EqualTo(Physics3DScaleCityPerformanceStatus.OverBudget));
        });

        harness.SelectScene(Physics3DShowcaseScene.ScannerRange);
        Assert.That(harness.Runtime.ScaleCityState, Is.EqualTo(Physics3DScaleCityShowcaseState.Empty));
        harness.SelectBenchmark(64);
        Assert.That(harness.Runtime.ScaleCityState.PerformanceSampleCount, Is.Zero);
        Assert.That(harness.Runtime.ScaleCityState.FramePerformanceSampleCount, Is.Zero);
    }

    [Test]
    public void DeterminismScene_PanelExplainsAuthoredBodyRebuildBoundary()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 20);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(320, 32));
        harness.SelectScene(Physics3DShowcaseScene.ReplayTheater);

        Physics3DShowcasePanelState panel = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(panel.SceneTitle, Is.EqualTo("Deterministic Rebuild Lab"));
            Assert.That(panel.SceneDescription, Does.Contain("authored bodies"));
            Assert.That(panel.SceneDescription, Does.Contain("not player-input replay"));
            Assert.That(panel.SceneDescription, Does.Contain("world rollback"));
            Assert.That(panel.DeterminismComparisonSummary, Does.StartWith("1 BASELINE"));
        });
    }

    [Test]
    public void DeterminismScene_StaysInCameraAndWaitsForPlayerBeforeRebuildVerification()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 20);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(320, 32));
        harness.SelectScene(Physics3DShowcaseScene.ReplayTheater);

        Vector3[] initial = CaptureDynamicPositions(harness.Runtime, Physics3DShapeKind.Box);
        Assert.That(
            MaximumHeight(initial),
            Is.LessThanOrEqualTo(3_000f),
            "Comparison actors must begin inside the authored camera volume so the baseline phase is visible.");

        for (int i = 0; i < config.ReplaySteps; i++)
        {
            harness.Step();
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                harness.Simulation.Enabled,
                Is.False,
                "Baseline completion must pause on the rebuilt scene before verification starts.");
            Assert.That(
                harness.Runtime.ReplayStatus,
                Is.EqualTo(Physics3DShowcaseReplayStatus.ReadyToReplay),
                "Rebuild verification must start from an explicit player action rather than an invisible automatic transition.");
        });
    }

    [Test]
    public void DeterminismScene_RebuildsAndVerifiesOwnedStateWithoutGlobalStepIndex()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 40);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(320, 32));
        harness.SelectScene(Physics3DShowcaseScene.ReplayTheater);

        for (int i = 0; i < config.ReplaySteps; i++)
        {
            harness.Step();
        }

        Assert.That(harness.Runtime.ReplayStatus, Is.EqualTo(Physics3DShowcaseReplayStatus.ReadyToReplay));
        Assert.That(harness.Simulation.Enabled, Is.False);
        harness.StartReplayComparison();

        for (int i = 0; i < config.ReplaySteps + 8 &&
                        harness.Runtime.ReplayStatus is not Physics3DShowcaseReplayStatus.Passed and
                        not Physics3DShowcaseReplayStatus.Failed; i++)
        {
            harness.Step();
        }

        Assert.That(
            harness.Runtime.ReplayStatus,
            Is.EqualTo(Physics3DShowcaseReplayStatus.Passed),
            $"cursor={harness.Runtime.ReplayCursor}, expected={harness.Runtime.ReplayExpectedHash:X16}, actual={harness.Runtime.ReplayActualHash:X16}");
        Assert.That(harness.Simulation.Enabled, Is.False, "A completed rebuild verification should pause on its evidence frame.");
    }

    [Test]
    public void Feature_DeterministicRebuild_Scenario_PlayerInjectsTheConfiguredDifferenceThenResetsForACleanPass()
    {
        // Given the scripted baseline has completed and the rebuilt station waits for a player choice.
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 40);
        config.ReplayDifferenceStep = 12;
        config.ReplayDifferenceBodyIndex = 5;
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(320, 32));
        harness.SelectScene(Physics3DShowcaseScene.ReplayTheater);
        for (int step = 0; step < config.ReplaySteps; step++)
        {
            harness.Step();
        }

        Assert.That(harness.Runtime.ReplayStatus, Is.EqualTo(Physics3DShowcaseReplayStatus.ReadyToReplay));

        // When the player chooses Inject Difference, then verification stops on that first authored mismatch.
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
            Physics3DShowcaseCommandKind.StartReplayDifferenceComparison));
        harness.Runtime.PrepareFixedStep();
        for (int step = 0;
             step < config.ReplaySteps && harness.Runtime.ReplayStatus == Physics3DShowcaseReplayStatus.Replaying;
             step++)
        {
            harness.CompletePreparedStep();
            if (harness.Runtime.ReplayStatus == Physics3DShowcaseReplayStatus.Replaying)
            {
                harness.Runtime.PrepareFixedStep();
            }
        }

        Physics3DShowcasePanelState failed = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.ReplayStatus, Is.EqualTo(Physics3DShowcaseReplayStatus.Failed));
            Assert.That(harness.Runtime.ReplayCursor + 1, Is.EqualTo(config.ReplayDifferenceStep));
            Assert.That(harness.Runtime.ReplayDifferenceRequested, Is.True);
            Assert.That(harness.Runtime.ReplayDifferenceInjected, Is.True);
            Assert.That(harness.Runtime.ReplayExpectedHash, Is.Not.EqualTo(harness.Runtime.ReplayActualHash));
            Assert.That(failed.DeterminismComparisonSummary,
                Does.Contain($"FAIL at step {config.ReplayDifferenceStep}").And.Contain("expected").And.Contain("actual"));
            Assert.That(harness.Simulation.Enabled, Is.False);
        });

        // When the player resets and chooses the clean run, then every rebuilt state passes again.
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.Reset));
        harness.Step();
        for (int step = 1;
             step < config.ReplaySteps && harness.Runtime.ReplayStatus == Physics3DShowcaseReplayStatus.Recording;
             step++)
        {
            harness.Step();
        }

        Assert.That(harness.Runtime.ReplayStatus, Is.EqualTo(Physics3DShowcaseReplayStatus.ReadyToReplay));
        harness.StartReplayComparison();
        for (int step = 0;
             step < config.ReplaySteps && harness.Runtime.ReplayStatus == Physics3DShowcaseReplayStatus.Replaying;
             step++)
        {
            harness.Step();
        }

        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.ReplayStatus, Is.EqualTo(Physics3DShowcaseReplayStatus.Passed));
            Assert.That(harness.Runtime.ReplayDifferenceRequested, Is.False);
            Assert.That(harness.Runtime.ReplayDifferenceInjected, Is.False);
            Assert.That(harness.Runtime.CapturePanelState().DeterminismComparisonSummary, Does.StartWith("PASS"));
        });
    }

    [Test]
    [Category("scale")]
    public void BenchmarkPresets_CreateExactAwakeServerBodyCounts()
    {
        int[] presets = { 1_000, 10_000, 25_000, 50_000 };
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 50_001,
            benchmarkPresets: presets,
            replaySteps: 16);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(52_000, 256));
        harness.Simulation.Enabled = false;

        for (int i = 0; i < presets.Length; i++)
        {
            harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
                Physics3DShowcaseCommandKind.SetBenchmarkBodies,
                presets[i]));
            harness.Runtime.PrepareFixedStep();
            Assert.That(harness.Runtime.ActiveScene, Is.EqualTo(Physics3DShowcaseScene.ScaleCity));
            Assert.That(harness.Runtime.DynamicBodyCount, Is.EqualTo(presets[i]));
            Assert.That(harness.Runtime.BodyCount, Is.EqualTo(presets[i] + 1));
            Assert.That(harness.PhysicsWorld.ActiveMobileBodyCount, Is.EqualTo(presets[i]));
        }
    }

    [Test]
    [Explicit("Allocation gate is run deliberately after the functional suite.")]
    public void BenchmarkSteadyState_ThirtyHzFixedStepDoesNotAllocateOnCallingThread()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 2_100,
            benchmarkPresets: new[] { 500, 1_000, 1_500, 2_000 },
            replaySteps: 16);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(2_200, 32));
        harness.SelectBenchmark(2_000);
        const int fixedStepHz = 30;
        int completeTravelSteps = checked((int)MathF.Ceiling(
            (2f * config.BenchmarkTravelHalfWidthCm * fixedStepHz) /
            config.BenchmarkSpeedCmPerSecond));
        for (int i = 0; i < completeTravelSteps + fixedStepHz; i++)
        {
            harness.Step();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < fixedStepHz; i++)
        {
            harness.Step();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero);
    }

    private static Physics3DShowcaseConfig CreateShowcaseConfig(
        int maximumBodies,
        int[] benchmarkPresets,
        int replaySteps)
    {
        Physics3DShowcaseConfig config = Physics3DShowcaseConfig.Load(LoadOfficialShowcaseJson());
        config.MaximumBodies = maximumBodies;
        config.VisibleBodyLimit = Math.Min(256, maximumBodies);
        config.ChainLinkCount = 6;
        config.ReplaySteps = replaySteps;
        config.ReplayBaseHeightCm = Math.Max(
            500,
            (int)MathF.Ceiling(0.5f * 981f * MathF.Pow(replaySteps / 30f, 2f)) + config.BodySizeCm);
        config.BenchmarkDefaultBodies = benchmarkPresets[0];
        config.BenchmarkPresets = benchmarkPresets;
        config.BenchmarkLaneDecks = (benchmarkPresets[^1] + config.BenchmarkLaneColumns - 1) /
                                    config.BenchmarkLaneColumns;
        return config;
    }

    private static JsonObject LoadOfficialShowcaseJson()
    {
        string path = Path.Combine(
            FindRepoRoot(),
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardPhysics3DShowcaseMod",
            "assets",
            "CapabilityStandardPhysics3DShowcaseConfig.json");
        return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException("Physics3D showcase config is missing.");
    }

    private static Physics3DWorldConfig CreateWorldConfig(
        int mobileBodies,
        int staticBodies,
        int? constraintCapacity = null,
        int? actuationCommandCapacity = null)
    {
        return new Physics3DWorldConfig
        {
            MobileBodyCapacity = mobileBodies,
            StaticBodyCapacity = staticBodies,
            ShapeCapacity = 256,
            InactiveIslandCapacity = Math.Max(1, mobileBodies),
            ConstraintCapacity = constraintCapacity ?? Math.Max(256, mobileBodies * 2),
            ConstraintsPerTypeBatchCapacity = Math.Max(256, mobileBodies),
            ConstraintCountPerBodyEstimate = 8,
            ContactPairCapacityPerWorker = 65_536,
            ActuationCommandCapacity = actuationCommandCapacity ?? Math.Max(256, mobileBodies * 2),
            WorkerCount = 2,
            FixedStepHz = 30,
            MaximumPhysicsStepsPerSourceTick = 1,
            SolverSubstepCount = 1,
            SolverVelocityIterationCount = 8,
            GravityCmPerSecondSquared = new Vector3(0f, -981f, 0f),
            LinearDamping = 0f,
            AngularDamping = 0.03f,
            MaximumSpeculativeMarginCm = 10f,
            SleepThreshold = 0.01f,
            MinimumTimestepCountUnderSleepThreshold = 255,
            ContinuousMinimumSweepTimestep = 0.001f,
            ContinuousSweepConvergenceThreshold = 0.001f,
            MaterialCombineMode = Physics3DMaterialCombineMode.GeometricMean
        };
    }

    private static void AssertFinite(Vector3 value, string description)
    {
        Assert.That(
            float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z),
            Is.True,
            $"{description} must be finite, but was {value}.");
    }

    private static Vector3[] CaptureDynamicPositions(
        Physics3DShowcaseRuntime runtime,
        Physics3DShapeKind expectedShape)
    {
        var positions = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < runtime.BodyCount; i++)
        {
            Assert.That(runtime.TryGetBodyVisual(
                i,
                out Physics3DBodyState state,
                out Physics3DBodyKind bodyKind,
                out Physics3DShapeKind shapeKind,
                out _,
                out _,
                out _), Is.True);
            if (bodyKind == Physics3DBodyKind.Dynamic && shapeKind == expectedShape)
            {
                positions.Add(state.PositionCm);
            }
        }

        Assert.That(positions, Is.Not.Empty, $"The active station must include dynamic {expectedShape} bodies.");
        return positions.ToArray();
    }

    private static float HorizontalFootprint(Vector3[] positions)
    {
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        for (int i = 0; i < positions.Length; i++)
        {
            minX = MathF.Min(minX, positions[i].X);
            maxX = MathF.Max(maxX, positions[i].X);
            minZ = MathF.Min(minZ, positions[i].Z);
            maxZ = MathF.Max(maxZ, positions[i].Z);
        }

        return MathF.Max(maxX - minX, maxZ - minZ);
    }

    private static Vector2 HorizontalCenter(Vector3[] positions)
    {
        Vector2 total = Vector2.Zero;
        for (int i = 0; i < positions.Length; i++)
        {
            total += new Vector2(positions[i].X, positions[i].Z);
        }

        return total / positions.Length;
    }

    private static float MaximumHeight(Vector3[] positions)
    {
        float maximum = float.NegativeInfinity;
        for (int i = 0; i < positions.Length; i++)
        {
            maximum = MathF.Max(maximum, positions[i].Y);
        }

        return maximum;
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "launcher.config.json")) &&
                Directory.Exists(Path.Combine(directory.FullName, "mods")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Ludots repository root.");
    }

    private sealed class ShowcaseHarness : IDisposable
    {
        public ShowcaseHarness(Physics3DShowcaseConfig config, Physics3DWorldConfig worldConfig)
        {
            EcsWorld = World.Create();
            PhysicsWorld = new Physics3DWorld(worldConfig);
            Simulation = new Physics3DSimulationSystem(EcsWorld, PhysicsWorld, 30, 1);
            Runtime = new Physics3DShowcaseRuntime();
            Runtime.ActivateForTests(EcsWorld, PhysicsWorld, Simulation, config);
        }

        public World EcsWorld { get; }
        public Physics3DWorld PhysicsWorld { get; }
        public Physics3DSimulationSystem Simulation { get; }
        public Physics3DShowcaseRuntime Runtime { get; }

        public void SelectScene(Physics3DShowcaseScene scene)
        {
            Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
                Physics3DShowcaseCommandKind.SelectScene,
                (int)scene));
            Step();
        }

        public void SelectBenchmark(int bodies)
        {
            Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
                Physics3DShowcaseCommandKind.SetBenchmarkBodies,
                bodies));
            Runtime.PrepareFixedStep();
        }

        public void StartReplayComparison()
        {
            Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
                Physics3DShowcaseCommandKind.StartReplayComparison));
            Runtime.PrepareFixedStep();
        }

        public void Step()
        {
            Runtime.PrepareFixedStep();
            CompletePreparedStep();
        }

        public void CompletePreparedStep()
        {
            Simulation.Update(1f / 30f);
            Runtime.ObserveFixedStep();
        }

        public void Dispose()
        {
            Runtime.Dispose();
            PhysicsWorld.Dispose();
            EcsWorld.Dispose();
        }
    }
}
