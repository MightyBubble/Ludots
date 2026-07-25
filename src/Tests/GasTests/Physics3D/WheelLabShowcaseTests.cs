using System;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using CapabilityStandardPhysics3DShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Physics3D;
using Ludots.Core.Scripting;
using Ludots.Core.Vehicle3D;
using Ludots.Platform.Abstractions;
using Ludots.Tests.GAS.Production;
using Ludots.UI.Surface;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class WheelLabShowcaseTests
{
    [Test]
    public void Feature_WheelLab_Scenario_ProductionPipelinePreparesAndObservesEveryAuthoritativeStep()
    {
        string repoRoot = FindRepoRoot();
        using var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(
                repoRoot,
                new[]
                {
                    "LudotsCoreMod",
                    "CoreInputMod",
                    "CameraProfilesMod",
                    "Physics3DMod",
                    "CapabilityStandardPhysics3DShowcaseMod"
                }),
            Path.Combine(repoRoot, "assets"));
        InstallInput(engine);
        AcceptanceUiHostInstaller.Install(engine, 1600f, 900f);
        engine.Start();
        engine.LoadEntryMap("capability_standard_physics3d_showcase");

        var runtime = engine.GetService(CoreServiceKeys.BenchmarkSceneController) as Physics3DShowcaseRuntime
            ?? throw new InvalidOperationException("Production Physics3D showcase runtime is missing.");
        IPhysics3DWorld physics = engine.GetService(Physics3DServiceKeys.World)
            ?? throw new InvalidOperationException("Production Physics3D world is missing.");
        Physics3DSimulationSystem simulation = engine.GetService(Physics3DServiceKeys.SimulationSystem)
            ?? throw new InvalidOperationException("Production Physics3D simulation system is missing.");
        runtime.EnqueueCommand(new Physics3DShowcaseCommand(
            Physics3DShowcaseCommandKind.SelectScene,
            (int)Physics3DShowcaseScene.WheelLab));
        long firstStep = physics.StepIndex;

        Assert.DoesNotThrow(() =>
        {
            for (int i = 0; i < 4; i++)
            {
                engine.Tick(1f / 30f);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(runtime.ActiveScene, Is.EqualTo(Physics3DShowcaseScene.WheelLab));
            Assert.That(physics.StepIndex, Is.EqualTo(firstStep + 4));
            Assert.That(runtime.WheelLabVehicleCount, Is.EqualTo(1));
            Assert.That(runtime.WheelLabWheelCountValue, Is.EqualTo(4));
        });

        MapId showcaseMap = engine.CurrentMapSession.MapId;
        ScriptContext resumeContext = engine.CreateContext();
        resumeContext.Set(CoreServiceKeys.MapId, showcaseMap);
        engine.TriggerManager.FireMapEventAsync(showcaseMap, GameEvents.MapResumed, resumeContext)
            .GetAwaiter().GetResult();
        engine.TriggerManager.FireMapEventAsync(showcaseMap, GameEvents.MapResumed, resumeContext)
            .GetAwaiter().GetResult();

        var backgroundMap = new MapId("physics3d_lifecycle_background");
        ScriptContext backgroundUnloadContext = engine.CreateContext();
        backgroundUnloadContext.Set(CoreServiceKeys.MapId, backgroundMap);
        engine.TriggerManager.FireMapEventAsync(backgroundMap, GameEvents.MapUnloaded, backgroundUnloadContext)
            .GetAwaiter().GetResult();

        long stepBeforeLifecycleTick = physics.StepIndex;
        long sceneStepBeforeLifecycleTick = runtime.SceneStep;
        engine.Tick(1f / 30f);
        Assert.Multiple(() =>
        {
            Assert.That(engine.GetService(CoreServiceKeys.BenchmarkSceneController), Is.SameAs(runtime));
            Assert.That(engine.GetService(Physics3DServiceKeys.World), Is.SameAs(physics));
            Assert.That(engine.GetService(Physics3DServiceKeys.SimulationSystem), Is.SameAs(simulation));
            Assert.That(runtime.IsActive, Is.True);
            Assert.That(runtime.ActiveScene, Is.EqualTo(Physics3DShowcaseScene.WheelLab));
            Assert.That(physics.StepIndex, Is.EqualTo(stepBeforeLifecycleTick + 1));
            Assert.That(runtime.SceneStep, Is.EqualTo(sceneStepBeforeLifecycleTick + 1));
        });
    }

    [Test]
    public void Feature_WheelLab_Scenario_PlayerSeesACompleteStrictlyConfiguredDrivingCourse()
    {
        // Given the authored Wheel Lab configuration is loaded through the strict production parser.
        JsonObject json = LoadOfficialConfigJson();

        // When an unknown Wheel Lab field is introduced.
        Physics3DShowcaseConfig config = Physics3DShowcaseConfig.Load(json);
        JsonObject unknown = (JsonObject)json.DeepClone();
        unknown["wheelLab"]!.AsObject()["silentWheelFallback"] = true;
        JsonObject insufficientComparisonCapacity = (JsonObject)json.DeepClone();
        insufficientComparisonCapacity["wheelLab"]!.AsObject()["comparisonResultCapacity"] = 2;

        // Then the real course is accepted and the invented fallback is rejected.
        Assert.Multiple(() =>
        {
            Assert.That(config.WheelLab.InitialWheelKind, Is.EqualTo(Vehicle3DWheelKind.Physical));
            Assert.That(config.WheelLab.ScanningQueryKind, Is.EqualTo(Vehicle3DWheelQueryKind.SphereCast));
            Assert.That(config.WheelLab.BumpCount, Is.GreaterThanOrEqualTo(3));
            Assert.That(config.WheelLab.PotholeDepthCm, Is.GreaterThan(0f));
            Assert.That(config.WheelLab.PotholeTransitionLengthCm, Is.GreaterThan(0f));
            Assert.That(config.WheelLab.BankAngleDegrees, Is.GreaterThan(0f));
            Assert.That(config.WheelLab.RampAngleDegrees, Is.GreaterThan(0f));
            Assert.That(config.WheelLab.BrakeEndZCm, Is.GreaterThan(config.WheelLab.BrakeStartZCm));
            Assert.That(config.WheelLab.ComparisonResultCapacity, Is.GreaterThanOrEqualTo(3));
            Assert.That(config.WheelLab.TrialTimeLimitTicks, Is.GreaterThan(0));
            Assert.That(
                config.WheelLab.TrialCompletionMinimumZCm,
                Is.InRange(config.WheelLab.BrakeStartZCm, config.WheelLab.BrakeEndZCm));
            Assert.Throws<System.Text.Json.JsonException>(() => Physics3DShowcaseConfig.Load(unknown));
            Assert.Throws<InvalidOperationException>(
                () => Physics3DShowcaseConfig.Load(insufficientComparisonCapacity));
        });
    }

    [Test]
    public void Feature_WheelLab_Scenario_ChangingWheelTypeVoidsTheCurrentRunAndRestartsFromTheAuthoredState()
    {
        // Given the player has started a physical-wheel run and moved away from the authored start.
        using var harness = new WheelLabHarness();
        harness.Runtime.SetWheelLabInputForTests(new Vehicle3DInput(throttle: 1f, brake: 0f, steering: 0f));
        for (int i = 0; i < 24; i++)
        {
            harness.Step();
        }

        Physics3DBodyId physicalChassis = harness.Runtime.WheelLabChassisBody;
        Physics3DBodyState authoredStart = harness.Runtime.WheelLabTrialStartState;
        Physics3DBodyState beforeBox = harness.Runtime.GetWheelLabChassisState();
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.WheelLabMode, Is.EqualTo(Vehicle3DWheelKind.Physical));
            Assert.That(harness.Runtime.WheelLabTrialStatus, Is.EqualTo(Physics3DWheelLabTrialStatus.Running));
            Assert.That(beforeBox.PositionCm, Is.Not.EqualTo(authoredStart.PositionCm));
            Assert.That(harness.Runtime.WheelLabVehicleCount, Is.EqualTo(1));
            Assert.That(harness.Runtime.WheelLabWheelCountValue, Is.EqualTo(4));
            Assert.That(harness.Runtime.WheelLabModeBodyCount, Is.EqualTo(4));
            Assert.That(harness.Physics.ActiveConstraintCount, Is.EqualTo(20));
        });

        // When the player selects Box Wheels at a fixed-step boundary.
        harness.Runtime.SetWheelLabInputForTests(default);
        harness.PrepareModeSwitch(Vehicle3DWheelKind.Box);
        Physics3DBodyId boxChassis = harness.Runtime.WheelLabChassisBody;
        Physics3DBodyState afterBoxPrepare = harness.Runtime.GetWheelLabChassisState();

        // Then the old run is visibly void, while a clean chassis and wheel assembly restart from one authored state.
        Assert.That(
            harness.Runtime.TryGetWheelLabTrialResult(
                Vehicle3DWheelKind.Physical,
                out Physics3DWheelLabTrialResult invalidated),
            Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(boxChassis, Is.Not.EqualTo(physicalChassis));
            AssertBodyState(afterBoxPrepare, authoredStart);
            Assert.That(harness.Runtime.WheelLabMode, Is.EqualTo(Vehicle3DWheelKind.Box));
            Assert.That(harness.Runtime.WheelLabTrialStatus, Is.EqualTo(Physics3DWheelLabTrialStatus.Ready));
            Assert.That(harness.Runtime.WheelLabModeBodyCount, Is.EqualTo(4));
            Assert.That(harness.Physics.ActiveConstraintCount, Is.EqualTo(20));
            Assert.That(invalidated.Status, Is.EqualTo(Physics3DWheelLabTrialStatus.Invalidated));
            Assert.That(invalidated.CompletionTick, Is.GreaterThan(0));
            Assert.That(invalidated.Reason, Is.EqualTo(Physics3DWheelLabTrialReason.WheelTypeChanged));
        });
        harness.CompletePreparedStep();

        // When the player selects Scanning Wheels before starting the Box run.
        harness.PrepareModeSwitch(Vehicle3DWheelKind.Scanning);
        Physics3DBodyId scanningChassis = harness.Runtime.WheelLabChassisBody;
        Physics3DBodyState afterScanningPrepare = harness.Runtime.GetWheelLabChassisState();

        // Then the same authored state is used again, with four batched sphere casts and no private wheel bodies.
        Assert.Multiple(() =>
        {
            Assert.That(scanningChassis, Is.Not.EqualTo(boxChassis));
            AssertBodyState(afterScanningPrepare, authoredStart);
            Assert.That(harness.Runtime.WheelLabMode, Is.EqualTo(Vehicle3DWheelKind.Scanning));
            Assert.That(harness.Runtime.WheelLabModeBodyCount, Is.Zero);
            Assert.That(harness.Physics.ActiveConstraintCount, Is.Zero);
            Assert.That(harness.Runtime.WheelLabVehicleCount, Is.EqualTo(1));
            Assert.That(harness.Runtime.WheelLabWheelCountValue, Is.EqualTo(4));
        });
        harness.CompletePreparedStep();

        // When the player cycles once more, Then physical wheels also receive the exact authored state.
        harness.PrepareModeSwitch(Vehicle3DWheelKind.Physical);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.WheelLabChassisBody, Is.Not.EqualTo(scanningChassis));
            AssertBodyState(harness.Runtime.GetWheelLabChassisState(), authoredStart);
            Assert.That(harness.Runtime.WheelLabMode, Is.EqualTo(Vehicle3DWheelKind.Physical));
            Assert.That(harness.Runtime.WheelLabModeBodyCount, Is.EqualTo(4));
            Assert.That(harness.Physics.ActiveConstraintCount, Is.EqualTo(20));
        });
        harness.CompletePreparedStep();
    }

    [Test]
    public void Feature_WheelLab_Scenario_OnlyPositiveForwardThrottleStartsTheSharedTrial()
    {
        // Given the selected wheel type is ready at the authored start.
        using var harness = new WheelLabHarness();

        // When the player steers, brakes, or selects reverse without moving forward.
        harness.Runtime.SetWheelLabInputForTests(new Vehicle3DInput(throttle: 0f, brake: 0f, steering: 1f));
        harness.Step();
        Assert.That(harness.Runtime.WheelLabTrialStatus, Is.EqualTo(Physics3DWheelLabTrialStatus.Ready));
        harness.Runtime.SetWheelLabInputForTests(new Vehicle3DInput(throttle: 0f, brake: 1f, steering: 0f));
        harness.Step();
        Assert.That(harness.Runtime.WheelLabTrialStatus, Is.EqualTo(Physics3DWheelLabTrialStatus.Ready));
        harness.Runtime.SetWheelLabInputForTests(new Vehicle3DInput(throttle: -1f, brake: 0f, steering: 0f));
        harness.Step();

        // Then the trial remains ready until positive forward throttle is submitted.
        Assert.That(harness.Runtime.WheelLabTrialStatus, Is.EqualTo(Physics3DWheelLabTrialStatus.Ready));
        harness.Runtime.SetWheelLabInputForTests(new Vehicle3DInput(throttle: 1f, brake: 0f, steering: 0f));
        harness.Step();
        Assert.That(harness.Runtime.WheelLabTrialStatus, Is.EqualTo(Physics3DWheelLabTrialStatus.Running));
    }

    [Test]
    public void Feature_WheelLab_Scenario_PhysicalWheelsUseVisibleCylinderBodies()
    {
        // Given the player opens Wheel Lab on its default physical-wheel comparison.
        using var harness = new WheelLabHarness();
        Physics3DWheelLabShowcaseConfig config = harness.Runtime.ActiveConfig.WheelLab;
        int firstWheelBodyIndex = harness.Runtime.BodyCount - harness.Runtime.WheelLabModeBodyCount;

        // Then every real wheel is both simulated and presented as the same narrow cylinder.
        for (int wheelIndex = 0; wheelIndex < harness.Runtime.WheelLabModeBodyCount; wheelIndex++)
        {
            bool found = harness.Runtime.TryGetBodyVisual(
                firstWheelBodyIndex + wheelIndex,
                out _,
                out _,
                out Physics3DShapeKind shapeKind,
                out Vector3 visualSizeCm,
                out _,
                out _);
            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(shapeKind, Is.EqualTo(Physics3DShapeKind.Cylinder));
                Assert.That(
                    visualSizeCm,
                    Is.EqualTo(new Vector3(config.WheelRadiusCm * 2f, config.WheelWidthCm, config.WheelRadiusCm * 2f)));
            });
        }
    }

    [Test]
    public void Feature_WheelLab_Scenario_MovingPlatformPublishesTheSameNonzeroMotionToPhysicsAndEcs()
    {
        // Given the player starts the shared trial from its authored platform phase.
        using var harness = new WheelLabHarness();
        harness.Runtime.SetWheelLabInputForTests(new Vehicle3DInput(throttle: 1f, brake: 0f, steering: 0f));

        // When the first fixed step is prepared.
        harness.Runtime.PrepareFixedStep();
        harness.Runtime.GetWheelLabMovingPlatformMotion(
            out Physics3DBodyState bodyState,
            out Physics3DPoseCm ecsPose);

        // Then gameplay and physics see the same real platform motion, rather than a teleported visual.
        Assert.Multiple(() =>
        {
            Assert.That(bodyState.LinearVelocityCmPerSecond.LengthSquared(), Is.GreaterThan(0f));
            Assert.That(bodyState.AngularVelocityRadiansPerSecond.LengthSquared(), Is.GreaterThan(0f));
            Assert.That(ecsPose.LinearVelocity, Is.EqualTo(bodyState.LinearVelocityCmPerSecond));
            Assert.That(ecsPose.AngularVelocity, Is.EqualTo(bodyState.AngularVelocityRadiansPerSecond));
        });
        harness.CompletePreparedStep();
    }

    [Test]
    public void Feature_WheelLab_Scenario_PlayerUsesOnePublicKeyboardRouteForAllThreeWheelTypes()
    {
        // Given the production map is open at Wheel Lab and the player can only use its public keyboard bindings.
        string repoRoot = FindRepoRoot();
        using var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(
                repoRoot,
                new[]
                {
                    "LudotsCoreMod",
                    "CoreInputMod",
                    "CameraProfilesMod",
                    "Physics3DMod",
                    "CapabilityStandardPhysics3DShowcaseMod"
                }),
            Path.Combine(repoRoot, "assets"));
        TestInputBackend keyboard = InstallTestInput(engine);
        AcceptanceUiHostInstaller.Install(engine, 1600f, 900f);
        engine.Start();
        engine.LoadEntryMap("capability_standard_physics3d_showcase");

        var runtime = engine.GetService(CoreServiceKeys.BenchmarkSceneController) as Physics3DShowcaseRuntime
            ?? throw new InvalidOperationException("Production Physics3D showcase runtime is missing.");
        runtime.EnqueueCommand(new Physics3DShowcaseCommand(
            Physics3DShowcaseCommandKind.SelectScene,
            (int)Physics3DShowcaseScene.WheelLab));
        TickEngine(engine, 2);

        Physics3DBodyState[] starts = new Physics3DBodyState[3];
        Physics3DBodyState[] platformStarts = new Physics3DBodyState[3];
        Physics3DWheelLabTrialResult[] results = new Physics3DWheelLabTrialResult[3];
        PublicKeyboardRouteObservation[] observations = new PublicKeyboardRouteObservation[3];
        WheelModePresentationEvidence[] presentations = new WheelModePresentationEvidence[3];
        Vehicle3DWheelKind[] kinds =
        {
            Vehicle3DWheelKind.Physical,
            Vehicle3DWheelKind.Box,
            Vehicle3DWheelKind.Scanning
        };

        // When the player repeats the same A/D steering, W drive, Space brake, and idle timing, pressing Q only between attempts.
        for (int run = 0; run < kinds.Length; run++)
        {
            Assert.That(runtime.WheelLabMode, Is.EqualTo(kinds[run]));
            presentations[run] = CaptureWheelModePresentationEvidence(runtime);
            observations[run] = DrivePublicKeyboardRoute(engine, keyboard, runtime);
            starts[run] = runtime.WheelLabTrialStartState;
            platformStarts[run] = runtime.WheelLabTrialPlatformStartState;
            Assert.That(runtime.TryGetWheelLabTrialResult(kinds[run], out Physics3DWheelLabTrialResult diagnostic), Is.True);
            results[run] = diagnostic;
            Physics3DBodyState finalState = runtime.GetWheelLabChassisState();
            TestContext.WriteLine(
                $"{kinds[run]}: {diagnostic.Status}/{diagnostic.Reason}, tick={diagnostic.CompletionTick}, " +
                $"position={finalState.PositionCm}, speed={runtime.WheelLabSpeedKph:F3} kph, " +
                $"compression={diagnostic.MaximumSuspensionCompressionCm:F3} cm, " +
                $"slip={diagnostic.MaximumSlipCmPerSecond:F3} cm/s, " +
                $"grounded={diagnostic.GroundedRatio:P2}, brake={diagnostic.BrakingDistanceCm:F3} cm, " +
                $"steerYaw={observations[run].SteeringYawDeltaRadians:F4} rad, " +
                $"airborneTicks={observations[run].MaximumConsecutiveAirborneTicks}, " +
                $"peakCompression={observations[run].PeakCompressionCm:F3} cm");

            if (run + 1 < kinds.Length)
            {
                PressPublicKey(engine, keyboard, "<Keyboard>/q");
                Assert.That(runtime.WheelLabMode, Is.EqualTo(kinds[run + 1]));
            }
        }

        // Then all three attempts have terminal, side-by-side metrics and began from the same authored state.
        AssertBodyState(starts[1], starts[0]);
        AssertBodyState(starts[2], starts[0]);
        AssertBodyState(platformStarts[1], platformStarts[0]);
        AssertBodyState(platformStarts[2], platformStarts[0]);
        for (int i = 0; i < kinds.Length; i++)
        {
            Physics3DWheelLabTrialResult result = results[i];
            PublicKeyboardRouteObservation observation = observations[i];
            Assert.Multiple(() =>
            {
                Assert.That(result.WheelKind, Is.EqualTo(kinds[i]));
                Assert.That(
                    result.Status,
                    Is.EqualTo(Physics3DWheelLabTrialStatus.Succeeded),
                    $"{kinds[i]} did not finish the shared public-key route: {result.Reason} at tick {result.CompletionTick}.");
                Assert.That(result.CompletionTick, Is.GreaterThan(0));
                Assert.That(result.MaximumSuspensionCompressionCm, Is.GreaterThanOrEqualTo(0f));
                Assert.That(result.MaximumSlipCmPerSecond, Is.GreaterThanOrEqualTo(0f));
                Assert.That(result.GroundedRatio, Is.InRange(0f, 1f));
                Assert.That(result.BrakingDistanceCm, Is.GreaterThan(0f));
                Assert.That(result.BrakeMeasured, Is.True);
                Assert.That(observation.UsedSteerLeft, Is.True, $"{kinds[i]} public route never pressed A.");
                Assert.That(observation.UsedSteerRight, Is.True, $"{kinds[i]} public route never pressed D.");
                Assert.That(observation.UsedDrive, Is.True, $"{kinds[i]} public route never pressed W.");
                Assert.That(observation.UsedBrake, Is.True, $"{kinds[i]} public route never pressed Space.");
                Assert.That(
                    observation.SteeringYawDeltaRadians,
                    Is.GreaterThan(0.02f),
                    $"{kinds[i]} A/D steering produced no perceptible turn on the shared course.");
                Assert.That(
                    observation.SawSuspensionCompress,
                    Is.True,
                    $"{kinds[i]} never showed suspension compression on the authored bumps or pit.");
                Assert.That(
                    observation.SawSuspensionRebound,
                    Is.True,
                    $"{kinds[i]} never showed suspension rebound after peak travel.");
                if (kinds[i] == Vehicle3DWheelKind.Box)
                {
                    Assert.That(
                        observation.MaximumConsecutiveAirborneTicks,
                        Is.LessThan(observations[0].MaximumConsecutiveAirborneTicks),
                        "Box Wheels should stay flatter than Physical Wheels across the shared jump.");
                }
                else
                {
                    Assert.That(
                        observation.MaximumConsecutiveAirborneTicks,
                        Is.GreaterThanOrEqualTo(2),
                        $"{kinds[i]} never left the ground for a real airborne interval on the authored route.");
                    Assert.That(
                        observation.SawLandingAfterAirborne,
                        Is.True,
                        $"{kinds[i]} never landed after leaving the ground.");
                }
                Assert.That(result.MaximumSuspensionCompressionCm, Is.GreaterThan(0f));
            });
        }

        AssertWheelModesAreMateriallyDistinct(kinds, presentations, results);

        Physics3DShowcasePanelState panel = runtime.CapturePanelState();
        var surfaceHost = engine.GetService(CoreServiceKeys.UiSurfaceHost) as UiSurfaceHost
            ?? throw new InvalidOperationException("Production Physics3D showcase UI surface is missing.");
        Assert.Multiple(() =>
        {
            Assert.That(panel.WheelPhysicalResult, Does.StartWith("PASS"));
            Assert.That(panel.WheelBoxResult, Does.StartWith("PASS"));
            Assert.That(panel.WheelScanningResult, Does.StartWith("PASS"));
            Assert.That(surfaceHost.Scene?.FindByElementId("physics3d-wheel-result-physical"), Is.Not.Null);
            Assert.That(surfaceHost.Scene?.FindByElementId("physics3d-wheel-result-box"), Is.Not.Null);
            Assert.That(surfaceHost.Scene?.FindByElementId("physics3d-wheel-result-scanning"), Is.Not.Null);
            Assert.That(runtime.CreateWheelLabRouteGuide(), Does.Contain("A/D"));
        });
    }

    [Test]
    public void Feature_WheelLab_Scenario_PlayerSeesSuspensionCompressReboundAndLeavesTheRampAirborne()
    {
        // Given the player starts a physical-wheel run on the authored suspension course.
        using var harness = new WheelLabHarness();
        Physics3DWheelLabShowcaseConfig config = harness.Runtime.ActiveConfig.WheelLab;
        var observation = PublicKeyboardRouteObservation.CreateEmpty();
        float spawnXCm = config.SpawnXCm;

        // When the player holds forward throttle through the yellow bumps, brown pit, purple platform, and red ramp.
        harness.Runtime.SetWheelLabInputForTests(new Vehicle3DInput(throttle: 1f, brake: 0f, steering: 0f));
        int minGrounded = 4;
        float maxCompression = 0f;
        var sectionHits = new System.Collections.Generic.HashSet<WheelLabCourseSection>();
        for (int i = 0; i < config.TrialRecommendedThrottleTicks; i++)
        {
            harness.Step();
            if (harness.Runtime.WheelLabTrialStatus is Physics3DWheelLabTrialStatus.Succeeded or
                Physics3DWheelLabTrialStatus.Failed)
            {
                break;
            }

            sectionHits.Add(harness.Runtime.WheelLabSection);
            minGrounded = Math.Min(minGrounded, harness.Runtime.WheelLabGroundedWheelCount);
            maxCompression = MathF.Max(maxCompression, harness.Runtime.WheelLabAverageCompressionCm);
            ObserveWheelLabGameplay(harness.Runtime, ref observation, spawnXCm);
            if (observation.SawLandingAfterAirborne &&
                harness.Runtime.GetWheelLabChassisState().PositionCm.Z >= config.RampEndZCm)
            {
                break;
            }
        }

        TestContext.WriteLine(
            $"sections={string.Join(',', sectionHits)}, minGrounded={minGrounded}, " +
            $"maxCompression={maxCompression:F3}, peakObs={observation.PeakCompressionCm:F3}, " +
            $"compress={observation.SawSuspensionCompress}, rebound={observation.SawSuspensionRebound}, " +
            $"airborne={observation.MaximumConsecutiveAirborneTicks}, land={observation.SawLandingAfterAirborne}, " +
            $"status={harness.Runtime.WheelLabTrialStatus}, z={harness.Runtime.GetWheelLabChassisState().PositionCm.Z:F1}");

        // Then suspension visibly compresses and rebounds, and the car has a real airborne stretch before landing.
        Assert.Multiple(() =>
        {
            Assert.That(observation.SawSuspensionCompress, Is.True);
            Assert.That(observation.SawSuspensionRebound, Is.True);
            Assert.That(observation.PeakCompressionCm, Is.GreaterThan(1f));
            Assert.That(observation.MaximumConsecutiveAirborneTicks, Is.GreaterThanOrEqualTo(2));
            Assert.That(observation.SawLandingAfterAirborne, Is.True);
            Assert.That(minGrounded, Is.EqualTo(0));
        });
    }

    [Test]
    public void Feature_WheelLab_Scenario_ThreeWheelTypesLookAndBehaveMateriallyDifferent()
    {
        // Given the player can swap the three real wheel assemblies at the shared authored start.
        using var harness = new WheelLabHarness();
        Vehicle3DWheelKind[] kinds =
        {
            Vehicle3DWheelKind.Physical,
            Vehicle3DWheelKind.Box,
            Vehicle3DWheelKind.Scanning
        };
        var presentations = new WheelModePresentationEvidence[kinds.Length];
        var results = new Physics3DWheelLabTrialResult[kinds.Length];

        // When each wheel type drives the same forward-then-brake reference route.
        for (int i = 0; i < kinds.Length; i++)
        {
            if (i > 0)
            {
                harness.Runtime.SetWheelLabInputForTests(default);
                harness.SwitchMode(kinds[i]);
            }

            presentations[i] = CaptureWheelModePresentationEvidence(harness.Runtime);
            RunRecommendedTrial(harness);
            Assert.That(
                harness.Runtime.TryGetWheelLabTrialResult(kinds[i], out Physics3DWheelLabTrialResult result),
                Is.True);
            Assert.That(
                result.Status,
                Is.EqualTo(Physics3DWheelLabTrialStatus.Succeeded),
                $"{kinds[i]} reference route failed: {result.Reason} at tick {result.CompletionTick}, " +
                $"z={harness.Runtime.GetWheelLabChassisState().PositionCm.Z:F1}, " +
                $"speed={harness.Runtime.WheelLabSpeedKph:F2}");
            results[i] = result;
        }

        // Then the player can tell them apart by what they see and by at least one measured driving outcome.
        AssertWheelModesAreMateriallyDistinct(kinds, presentations, results);
    }

    [Test]
    public void Feature_WheelLab_Scenario_PlayerDrivesBrakesAndReadsWheelContactEvidence()
    {
        // Given the player starts on physical wheels, whose ray contacts expose stable surface normals.
        using var harness = new WheelLabHarness();
        Vector3 start = harness.Runtime.GetWheelLabChassisState().PositionCm;

        // When the player holds throttle through the first part of the course.
        harness.Runtime.SetWheelLabInputForTests(new Vehicle3DInput(throttle: 1f, brake: 0f, steering: 0f));
        for (int i = 0; i < 120; i++)
        {
            harness.Step();
        }

        Physics3DBodyState driven = harness.Runtime.GetWheelLabChassisState();
        float speedBeforeBrake = harness.Runtime.WheelLabSpeedKph;

        // And the player releases throttle and holds the brake.
        harness.Runtime.SetWheelLabInputForTests(new Vehicle3DInput(throttle: 0f, brake: 1f, steering: 0f));
        for (int i = 0; i < 45; i++)
        {
            harness.Step();
        }

        // Then the chassis moved forward, braking reduced speed, and all debug evidence is physics-derived.
        Assert.Multiple(() =>
        {
            Assert.That(driven.PositionCm.Z, Is.GreaterThan(start.Z + 100f));
            Assert.That(speedBeforeBrake, Is.GreaterThan(1f));
            Assert.That(harness.Runtime.WheelLabSpeedKph, Is.LessThan(speedBeforeBrake));
            Assert.That(harness.Runtime.WheelLabGroundedWheelCount, Is.GreaterThan(0));
            Assert.That(harness.Runtime.CreateWheelLabSummary(), Does.Contain("Physical Wheels"));
        });

        int groundedVisuals = 0;
        int groundedNormals = 0;
        for (int i = 0; i < 4; i++)
        {
            Assert.That(harness.Runtime.TryGetWheelLabDebugVisual(i, out Physics3DWheelLabDebugVisual visual), Is.True);
            if (visual.Grounded)
            {
                groundedVisuals++;
                Assert.That(float.IsFinite(visual.CompressionCm), Is.True);
                float normalLengthSquared = visual.ContactNormal.LengthSquared();
                if (normalLengthSquared > 0f)
                {
                    groundedNormals++;
                    Assert.That(normalLengthSquared, Is.GreaterThan(0.9f));
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(groundedVisuals, Is.GreaterThan(0));
            Assert.That(groundedNormals, Is.GreaterThan(0));
        });
    }

    [Test]
    public void Feature_WheelLab_Scenario_ScanningWheelsBrakeWithoutLiftingTheChassisOffTheRoad()
    {
        // Given the player reaches the braking zone with Scanning Wheels on the shared 30 Hz route.
        using var harness = new WheelLabHarness();
        harness.SwitchMode(Vehicle3DWheelKind.Scanning);
        Physics3DWheelLabShowcaseConfig config = harness.Runtime.ActiveConfig.WheelLab;
        harness.Runtime.SetWheelLabInputForTests(new Vehicle3DInput(throttle: 1f, brake: 0f, steering: 0f));
        for (int i = 0; i < config.TrialRecommendedThrottleTicks; i++)
        {
            harness.Step();
            if (harness.Runtime.WheelLabTrialStatus != Physics3DWheelLabTrialStatus.Running)
            {
                break;
            }

            if (harness.Runtime.GetWheelLabChassisState().PositionCm.Z >= config.TrialCompletionMinimumZCm)
            {
                break;
            }
        }

        Physics3DBodyState brakingStart = harness.Runtime.GetWheelLabChassisState();
        Assert.That(
            brakingStart.PositionCm.Z,
            Is.GreaterThanOrEqualTo(config.TrialCompletionMinimumZCm),
            "Scanning wheels must reach the green braking zone before the brake check begins.");
        float maximumVerticalExcursionCm = 0f;
        int minimumGroundedWheels = 4;
        bool everyStateFinite = true;

        // When the player releases the throttle and holds the brake until a result appears.
        harness.Runtime.SetWheelLabInputForTests(new Vehicle3DInput(throttle: 0f, brake: 1f, steering: 0f));
        for (int i = 0;
             i < config.TrialRecommendedBrakeTicks &&
             harness.Runtime.WheelLabTrialStatus == Physics3DWheelLabTrialStatus.Running;
             i++)
        {
            harness.Step();
            Physics3DBodyState state = harness.Runtime.GetWheelLabChassisState();
            maximumVerticalExcursionCm = MathF.Max(
                maximumVerticalExcursionCm,
                MathF.Abs(state.PositionCm.Y - brakingStart.PositionCm.Y));
            minimumGroundedWheels = Math.Min(minimumGroundedWheels, harness.Runtime.WheelLabGroundedWheelCount);
            everyStateFinite &=
                float.IsFinite(state.PositionCm.X) &&
                float.IsFinite(state.PositionCm.Y) &&
                float.IsFinite(state.PositionCm.Z) &&
                float.IsFinite(state.Orientation.X) &&
                float.IsFinite(state.Orientation.Y) &&
                float.IsFinite(state.Orientation.Z) &&
                float.IsFinite(state.Orientation.W) &&
                float.IsFinite(state.LinearVelocityCmPerSecond.X) &&
                float.IsFinite(state.LinearVelocityCmPerSecond.Y) &&
                float.IsFinite(state.LinearVelocityCmPerSecond.Z) &&
                float.IsFinite(state.AngularVelocityRadiansPerSecond.X) &&
                float.IsFinite(state.AngularVelocityRadiansPerSecond.Y) &&
                float.IsFinite(state.AngularVelocityRadiansPerSecond.Z);
        }

        // Then braking ends in PASS while the chassis stays supported instead of being kicked upward.
        Assert.That(
            harness.Runtime.TryGetWheelLabTrialResult(
                Vehicle3DWheelKind.Scanning,
                out Physics3DWheelLabTrialResult result),
            Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(Physics3DWheelLabTrialStatus.Succeeded));
            Assert.That(result.BrakeMeasured, Is.True);
            Assert.That(harness.Runtime.WheelLabSpeedKph, Is.LessThanOrEqualTo(config.TrialStopSpeedKph));
            Assert.That(minimumGroundedWheels, Is.GreaterThan(0));
            Assert.That(maximumVerticalExcursionCm, Is.LessThan(80f));
            Assert.That(everyStateFinite, Is.True);
        });
    }

    [Test]
    public void Feature_WheelLab_Scenario_ChangingOnlyBrakeStrengthCannotLeakIntoTheNextBoxRun()
    {
        // Given two sessions finish a Physical run with different authored brake-force limits.
        Physics3DBodyState[] sixtyKilonewtonTrace = CaptureBoxThrottleTraceAfterPhysicalRun(60_000f);
        Physics3DBodyState[] seventyKilonewtonTrace = CaptureBoxThrottleTraceAfterPhysicalRun(70_000f);

        // When each player switches to Box Wheels and repeats the same pure-throttle route.
        Assert.That(seventyKilonewtonTrace.Length, Is.EqualTo(sixtyKilonewtonTrace.Length));

        // Then every Box frame starts clean and is identical because brake is zero throughout this run.
        for (int i = 0; i < sixtyKilonewtonTrace.Length; i++)
        {
            AssertBodyState(seventyKilonewtonTrace[i], sixtyKilonewtonTrace[i]);
        }
    }

    [Test]
    public void Feature_WheelLab_Scenario_CompletedTrialIgnoresFurtherVehicleControls()
    {
        // Given two players complete the same Physical Wheel trial.
        Physics3DBodyState[] idleTrace = CapturePostTrialTrace(default);
        Physics3DBodyState[] heldControlsTrace = CapturePostTrialTrace(
            new Vehicle3DInput(throttle: 1f, brake: 0f, steering: 1f));

        // When one player keeps holding throttle and steering after PASS while the other releases all controls.
        Assert.That(heldControlsTrace.Length, Is.EqualTo(idleTrace.Length));

        // Then both settled vehicle trajectories remain identical and the completed result cannot be driven again.
        for (int i = 0; i < idleTrace.Length; i++)
        {
            AssertBodyState(heldControlsTrace[i], idleTrace[i]);
        }
    }

    [Test]
    public void Feature_WheelLab_Scenario_WarmedFourWheelFixedStepAllocatesZeroBytes()
    {
        // Given a running scanning-wheel trial has warmed the exact 30Hz showcase path.
        using var harness = new WheelLabHarness();
        harness.SwitchMode(Vehicle3DWheelKind.Scanning);
        harness.Runtime.SetWheelLabInputForTests(new Vehicle3DInput(throttle: 1f, brake: 0f, steering: 0f));
        for (int i = 0; i < 64; i++)
        {
            harness.Step();
        }

        Assert.That(harness.Runtime.WheelLabTrialStatus, Is.EqualTo(Physics3DWheelLabTrialStatus.Running));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();

        // When 64 more authoritative fixed steps run.
        for (int i = 0; i < 64; i++)
        {
            harness.Step();
        }

        // Then the calling thread allocates no managed memory.
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Warmed Wheel Lab fixed-step path allocated {allocated} bytes.");
    }

    [TestCase(Vehicle3DWheelKind.Physical)]
    [TestCase(Vehicle3DWheelKind.Box)]
    [TestCase(Vehicle3DWheelKind.Scanning)]
    public void Feature_WheelLab_Scenario_SameFixedInputProducesTheSamePerFrameWorldTrace(
        Vehicle3DWheelKind wheelKind)
    {
        // Given two four-worker 30 Hz sessions start the selected wheel type from the same authored state.
        ulong[] firstTrace = CaptureFixedInputWorldTrace(wheelKind, workerCount: 4);
        ulong[] secondTrace = CaptureFixedInputWorldTrace(wheelKind, workerCount: 4);

        // When both players repeat the same forward, steering, reverse, and brake sequence.
        Assert.That(secondTrace.Length, Is.EqualTo(firstTrace.Length));

        // Then every authoritative frame has the same complete observable body-state hash.
        for (int frame = 0; frame < firstTrace.Length; frame++)
        {
            Assert.That(
                secondTrace[frame],
                Is.EqualTo(firstTrace[frame]),
                $"{wheelKind} diverged at fixed frame {frame}.");
        }
    }

    private static ulong[] CaptureFixedInputWorldTrace(Vehicle3DWheelKind wheelKind, int workerCount)
    {
        using var harness = new WheelLabHarness(workerCount: workerCount);
        harness.SwitchMode(wheelKind);
        const int forwardTicks = 90;
        const int steeringTicks = 45;
        const int reverseTicks = 45;
        const int brakeTicks = 30;
        const int totalTicks = forwardTicks + steeringTicks + reverseTicks + brakeTicks;
        var trace = new ulong[totalTicks + 1];
        trace[0] = harness.Physics.ComputeObservableBodyStateHash();
        for (int tick = 0; tick < totalTicks; tick++)
        {
            Vehicle3DInput input = tick switch
            {
                < forwardTicks => new Vehicle3DInput(throttle: 1f, brake: 0f, steering: 0f),
                < forwardTicks + steeringTicks => new Vehicle3DInput(throttle: 1f, brake: 0f, steering: 0.5f),
                < forwardTicks + steeringTicks + reverseTicks => new Vehicle3DInput(throttle: -1f, brake: 0f, steering: -0.5f),
                _ => new Vehicle3DInput(throttle: 0f, brake: 1f, steering: 0f)
            };
            harness.Runtime.SetWheelLabInputForTests(input);
            harness.Step();
            trace[tick + 1] = harness.Physics.ComputeObservableBodyStateHash();
        }

        return trace;
    }

    private static Physics3DBodyState[] CaptureBoxThrottleTraceAfterPhysicalRun(float maximumBrakeForce)
    {
        using var harness = new WheelLabHarness(maximumBrakeForce);

        // Drive a short identical throttle-only segment so brake-force limits are registered but never applied.
        harness.Runtime.SetWheelLabInputForTests(new Vehicle3DInput(throttle: 1f, brake: 0f, steering: 0f));
        for (int i = 0; i < 45; i++)
        {
            harness.Step();
        }

        harness.Runtime.SetWheelLabInputForTests(default);
        float canonicalBrakeForce = Physics3DShowcaseConfig.Load(LoadOfficialConfigJson()).WheelLab.MaximumBrakeForce;
        harness.Runtime.ActiveConfig.WheelLab.MaximumBrakeForce = canonicalBrakeForce;
        harness.SwitchMode(Vehicle3DWheelKind.Box);

        const int throttleTicks = 90;
        var trace = new Physics3DBodyState[throttleTicks + 1];
        trace[0] = harness.Runtime.GetWheelLabChassisState();
        harness.Runtime.SetWheelLabInputForTests(new Vehicle3DInput(throttle: 1f, brake: 0f, steering: 0f));
        for (int i = 0; i < throttleTicks; i++)
        {
            harness.Step();
            trace[i + 1] = harness.Runtime.GetWheelLabChassisState();
        }

        return trace;
    }

    private static Physics3DBodyState[] CapturePostTrialTrace(in Vehicle3DInput terminalInput)
    {
        using var harness = new WheelLabHarness();
        RunRecommendedTrial(harness);
        harness.Runtime.SetWheelLabInputForTests(terminalInput);

        const int observationTicks = 60;
        var trace = new Physics3DBodyState[observationTicks + 1];
        trace[0] = harness.Runtime.GetWheelLabChassisState();
        for (int i = 0; i < observationTicks; i++)
        {
            harness.Step();
            trace[i + 1] = harness.Runtime.GetWheelLabChassisState();
        }

        Assert.That(harness.Runtime.WheelLabTrialStatus, Is.EqualTo(Physics3DWheelLabTrialStatus.Succeeded));
        return trace;
    }

    private static void RunRecommendedTrial(WheelLabHarness harness)
    {
        Physics3DWheelLabShowcaseConfig config = harness.Runtime.ActiveConfig.WheelLab;
        harness.Runtime.SetWheelLabInputForTests(new Vehicle3DInput(throttle: 1f, brake: 0f, steering: 0f));
        for (int i = 0; i < config.TrialRecommendedThrottleTicks; i++)
        {
            harness.Step();
            if (harness.Runtime.WheelLabTrialStatus != Physics3DWheelLabTrialStatus.Running)
            {
                break;
            }

            // Player-facing route: release throttle once the green braking zone is reached.
            if (harness.Runtime.GetWheelLabChassisState().PositionCm.Z >= config.TrialCompletionMinimumZCm)
            {
                break;
            }
        }

        harness.Runtime.SetWheelLabInputForTests(new Vehicle3DInput(throttle: 0f, brake: 1f, steering: 0f));
        for (int i = 0; i < config.TrialRecommendedBrakeTicks; i++)
        {
            harness.Step();
            if (harness.Runtime.WheelLabTrialStatus is Physics3DWheelLabTrialStatus.Succeeded or
                Physics3DWheelLabTrialStatus.Failed)
            {
                break;
            }
        }

        Assert.That(
            harness.Runtime.WheelLabTrialStatus,
            Is.EqualTo(Physics3DWheelLabTrialStatus.Succeeded),
            () =>
            {
                harness.Runtime.TryGetWheelLabTrialResult(
                    harness.Runtime.WheelLabMode,
                    out Physics3DWheelLabTrialResult result);
                Physics3DBodyState chassis = harness.Runtime.GetWheelLabChassisState();
                return $"{harness.Runtime.WheelLabMode} recommended route ended {harness.Runtime.WheelLabTrialStatus}/" +
                       $"{result.Reason} tick={result.CompletionTick} z={chassis.PositionCm.Z:F1} " +
                       $"y={chassis.PositionCm.Y:F1} speed={harness.Runtime.WheelLabSpeedKph:F2} " +
                       $"brakeMeasured={result.BrakeMeasured} brakeCm={result.BrakingDistanceCm:F1}";
            });
    }

    private static void AssertBodyState(in Physics3DBodyState actual, in Physics3DBodyState expected)
    {
        Physics3DBodyState actualValue = actual;
        Physics3DBodyState expectedValue = expected;
        Assert.Multiple(() =>
        {
            Assert.That(actualValue.PositionCm, Is.EqualTo(expectedValue.PositionCm));
            Assert.That(actualValue.Orientation, Is.EqualTo(expectedValue.Orientation));
            Assert.That(actualValue.LinearVelocityCmPerSecond, Is.EqualTo(expectedValue.LinearVelocityCmPerSecond));
            Assert.That(actualValue.AngularVelocityRadiansPerSecond, Is.EqualTo(expectedValue.AngularVelocityRadiansPerSecond));
            Assert.That(actualValue.Awake, Is.EqualTo(expectedValue.Awake));
        });
    }

    private static JsonObject LoadOfficialConfigJson()
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

    private static void InstallInput(GameEngine engine)
    {
        InstallInput(engine, new NullInputBackend());
    }

    private static TestInputBackend InstallTestInput(GameEngine engine)
    {
        var backend = new TestInputBackend();
        InstallInput(engine, backend);
        return backend;
    }

    private static void InstallInput(GameEngine engine, IInputBackend backend)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputHandler = new PlayerInputHandler(backend, inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.AuthoritativeInput, inputHandler);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    private static PublicKeyboardRouteObservation DrivePublicKeyboardRoute(
        GameEngine engine,
        TestInputBackend keyboard,
        Physics3DShowcaseRuntime runtime)
    {
        Physics3DWheelLabShowcaseConfig config = runtime.ActiveConfig.WheelLab;
        int throttleTicks = config.TrialRecommendedThrottleTicks;
        int brakeTicks = config.TrialRecommendedBrakeTicks;
        var observation = PublicKeyboardRouteObservation.CreateEmpty();
        float spawnXCm = config.SpawnXCm;

        // Steering alone must not start the shared trial.
        keyboard.SetButton("<Keyboard>/d", true);
        observation.UsedSteerRight = true;
        TickEngine(engine, 1);
        ObserveWheelLabGameplay(runtime, ref observation, spawnXCm);
        keyboard.SetButton("<Keyboard>/d", false);
        keyboard.SetButton("<Keyboard>/a", true);
        observation.UsedSteerLeft = true;
        TickEngine(engine, 1);
        ObserveWheelLabGameplay(runtime, ref observation, spawnXCm);
        keyboard.SetButton("<Keyboard>/a", false);
        TickEngine(engine, 2);
        ObserveWheelLabGameplay(runtime, ref observation, spawnXCm);
        Assert.That(
            runtime.WheelLabTrialStatus,
            Is.EqualTo(Physics3DWheelLabTrialStatus.Ready),
            "A/D at the start must not begin the shared trial.");

        keyboard.SetButton("<Keyboard>/w", true);
        observation.UsedDrive = true;

        bool bankSteerArmed = false;
        bool bankSteerActive = false;
        bool bankCounterActive = false;
        int bankSteerTicksRemaining = 0;
        int bankCounterTicksRemaining = 0;
        float yawBeforeBankSteer = 0f;
        const int bankSteerHoldTicks = 18;
        const int bankCounterHoldTicks = 24;

        for (int tick = 0; tick < throttleTicks; tick++)
        {
            if (!bankSteerArmed &&
                runtime.WheelLabSection == WheelLabCourseSection.SideSlope &&
                runtime.WheelLabTrialStatus == Physics3DWheelLabTrialStatus.Running)
            {
                bankSteerArmed = true;
                bankSteerActive = true;
                bankSteerTicksRemaining = bankSteerHoldTicks;
                yawBeforeBankSteer = ExtractYawRadians(runtime.GetWheelLabChassisState().Orientation);
                keyboard.SetButton("<Keyboard>/d", true);
                observation.UsedSteerRight = true;
            }

            if (bankSteerActive)
            {
                bankSteerTicksRemaining--;
                if (bankSteerTicksRemaining <= 0)
                {
                    bankSteerActive = false;
                    keyboard.SetButton("<Keyboard>/d", false);
                    float yawAfterBankSteer = ExtractYawRadians(runtime.GetWheelLabChassisState().Orientation);
                    observation.SteeringYawDeltaRadians = MathF.Max(
                        observation.SteeringYawDeltaRadians,
                        MathF.Abs(NormalizeRadians(yawAfterBankSteer - yawBeforeBankSteer)));
                    bankCounterActive = true;
                    bankCounterTicksRemaining = bankCounterHoldTicks;
                    keyboard.SetButton("<Keyboard>/a", true);
                    observation.UsedSteerLeft = true;
                }
            }
            else if (bankCounterActive)
            {
                bankCounterTicksRemaining--;
                if (bankCounterTicksRemaining <= 0)
                {
                    bankCounterActive = false;
                    keyboard.SetButton("<Keyboard>/a", false);
                }
            }

            if (runtime.WheelLabSection is WheelLabCourseSection.MovingPlatform
                    or WheelLabCourseSection.Jump
                    or WheelLabCourseSection.Braking)
            {
                bankSteerActive = false;
                bankCounterActive = false;
                keyboard.SetButton("<Keyboard>/a", false);
                keyboard.SetButton("<Keyboard>/d", false);
            }

            TickEngine(engine, 1);
            ObserveWheelLabGameplay(runtime, ref observation, spawnXCm);
            if (runtime.WheelLabTrialStatus != Physics3DWheelLabTrialStatus.Running)
            {
                break;
            }

            if (runtime.GetWheelLabChassisState().PositionCm.Z >= config.TrialCompletionMinimumZCm)
            {
                break;
            }
        }

        keyboard.SetButton("<Keyboard>/w", false);
        keyboard.SetButton("<Keyboard>/a", false);
        keyboard.SetButton("<Keyboard>/d", false);
        Physics3DBodyState brakingStart = runtime.GetWheelLabChassisState();
        TestContext.WriteLine(
            $"{runtime.WheelLabMode} brake input: position={brakingStart.PositionCm}, " +
            $"speed={runtime.WheelLabSpeedKph:F3} kph");
        Assert.That(
            brakingStart.PositionCm.Z,
            Is.GreaterThanOrEqualTo(config.TrialCompletionMinimumZCm),
            $"{runtime.WheelLabMode} must cross the ordered bumps, pothole, bank, moving platform, and ramp before braking.");
        Assert.That(
            bankSteerArmed,
            Is.True,
            $"{runtime.WheelLabMode} never reached the blue bank, so A/D steering was never exercised mid-route.");

        keyboard.SetButton("<Keyboard>/space", true);
        observation.UsedBrake = true;
        for (int tick = 0; tick < brakeTicks; tick++)
        {
            TickEngine(engine, 1);
            ObserveWheelLabGameplay(runtime, ref observation, spawnXCm);
            if (runtime.WheelLabTrialStatus is Physics3DWheelLabTrialStatus.Succeeded or
                Physics3DWheelLabTrialStatus.Failed)
            {
                break;
            }
        }

        keyboard.SetButton("<Keyboard>/space", false);
        TickEngine(engine, 1);
        ObserveWheelLabGameplay(runtime, ref observation, spawnXCm);

        Assert.That(
            runtime.WheelLabTrialStatus,
            Is.AnyOf(
                Physics3DWheelLabTrialStatus.Succeeded,
                Physics3DWheelLabTrialStatus.Failed),
            "The public keyboard route must always end with a visible pass or fail result.");
        return observation;
    }

    private static void ObserveWheelLabGameplay(
        Physics3DShowcaseRuntime runtime,
        ref PublicKeyboardRouteObservation observation,
        float spawnXCm)
    {
        Physics3DWheelLabShowcaseConfig config = runtime.ActiveConfig.WheelLab;
        Physics3DBodyState chassis = runtime.GetWheelLabChassisState();
        float compressionCm = runtime.WheelLabAverageCompressionCm;
        int grounded = runtime.WheelLabGroundedWheelCount;
        WheelLabCourseSection section = runtime.WheelLabSection;
        observation.MaximumAbsLateralOffsetCm = MathF.Max(
            observation.MaximumAbsLateralOffsetCm,
            MathF.Abs(chassis.PositionCm.X - spawnXCm));

        if (grounded > 0)
        {
            if (section is WheelLabCourseSection.Bumps or WheelLabCourseSection.Pothole)
            {
                if (compressionCm > observation.PeakCompressionCm)
                {
                    observation.PeakCompressionCm = compressionCm;
                }

                if (compressionCm > 5f)
                {
                    observation.SawSuspensionCompress = true;
                }
            }

            bool reboundWindow = section is WheelLabCourseSection.SideSlope
                or WheelLabCourseSection.MovingPlatform
                or WheelLabCourseSection.Jump
                or WheelLabCourseSection.Braking;
            if (observation.SawSuspensionCompress &&
                observation.PeakCompressionCm > 5f &&
                reboundWindow &&
                compressionCm <= observation.PeakCompressionCm - 3f)
            {
                observation.SawSuspensionRebound = true;
            }

            if (observation.CurrentlyAirborne)
            {
                observation.SawLandingAfterAirborne = true;
                observation.CurrentlyAirborne = false;
                observation.CurrentAirborneTicks = 0;
            }
        }
        else if (runtime.WheelLabTrialStatus == Physics3DWheelLabTrialStatus.Running &&
                 chassis.PositionCm.Z >= config.PlatformGapStartZCm)
        {
            // Platform gap and ramp crest are the authored unload windows.
            observation.CurrentlyAirborne = true;
            observation.CurrentAirborneTicks++;
            observation.MaximumConsecutiveAirborneTicks = Math.Max(
                observation.MaximumConsecutiveAirborneTicks,
                observation.CurrentAirborneTicks);
        }
        else
        {
            observation.CurrentAirborneTicks = 0;
            observation.CurrentlyAirborne = false;
        }
    }

    private static WheelModePresentationEvidence CaptureWheelModePresentationEvidence(Physics3DShowcaseRuntime runtime)
    {
        Physics3DWheelLabShowcaseConfig config = runtime.ActiveConfig.WheelLab;
        int modeBodyCount = runtime.WheelLabModeBodyCount;
        var shapeKinds = new Physics3DShapeKind[modeBodyCount];
        var visualSizesCm = new Vector3[modeBodyCount];
        if (modeBodyCount > 0)
        {
            int firstWheelBodyIndex = runtime.BodyCount - modeBodyCount;
            for (int wheelIndex = 0; wheelIndex < modeBodyCount; wheelIndex++)
            {
                bool found = runtime.TryGetBodyVisual(
                    firstWheelBodyIndex + wheelIndex,
                    out _,
                    out _,
                    out Physics3DShapeKind shapeKind,
                    out Vector3 visualSizeCm,
                    out _,
                    out _);
                Assert.That(found, Is.True, $"Wheel mode body visual {wheelIndex} is missing.");
                shapeKinds[wheelIndex] = shapeKind;
                visualSizesCm[wheelIndex] = visualSizeCm;
            }
        }

        return new WheelModePresentationEvidence(
            runtime.WheelLabMode,
            modeBodyCount,
            runtime.WheelLabVehicleCount,
            runtime.WheelLabWheelCountValue,
            shapeKinds,
            visualSizesCm,
            config.WheelRadiusCm,
            config.WheelWidthCm);
    }

    private static void AssertWheelModesAreMateriallyDistinct(
        Vehicle3DWheelKind[] kinds,
        WheelModePresentationEvidence[] presentations,
        Physics3DWheelLabTrialResult[] results)
    {
        Assert.That(kinds.Length, Is.EqualTo(3));
        Assert.That(presentations.Length, Is.EqualTo(3));
        Assert.That(results.Length, Is.EqualTo(3));

        WheelModePresentationEvidence physical = FindPresentation(presentations, Vehicle3DWheelKind.Physical);
        WheelModePresentationEvidence box = FindPresentation(presentations, Vehicle3DWheelKind.Box);
        WheelModePresentationEvidence scanning = FindPresentation(presentations, Vehicle3DWheelKind.Scanning);

        Assert.Multiple(() =>
        {
            Assert.That(physical.ModeBodyCount, Is.EqualTo(4));
            Assert.That(box.ModeBodyCount, Is.EqualTo(4));
            Assert.That(scanning.ModeBodyCount, Is.Zero);
            Assert.That(physical.VehicleCount, Is.EqualTo(1));
            Assert.That(box.VehicleCount, Is.EqualTo(1));
            Assert.That(scanning.VehicleCount, Is.EqualTo(1));
            Assert.That(physical.WheelCount, Is.EqualTo(4));
            Assert.That(box.WheelCount, Is.EqualTo(4));
            Assert.That(scanning.WheelCount, Is.EqualTo(4));
        });

        for (int i = 0; i < 4; i++)
        {
            Assert.Multiple(() =>
            {
                Assert.That(physical.ShapeKinds[i], Is.EqualTo(Physics3DShapeKind.Cylinder));
                Assert.That(
                    physical.VisualSizesCm[i],
                    Is.EqualTo(new Vector3(physical.WheelRadiusCm * 2f, physical.WheelWidthCm, physical.WheelRadiusCm * 2f)));
                Assert.That(box.ShapeKinds[i], Is.EqualTo(Physics3DShapeKind.Box));
                Assert.That(box.VisualSizesCm[i].X, Is.EqualTo(box.WheelWidthCm));
                Assert.That(box.VisualSizesCm[i].Y, Is.EqualTo(box.VisualSizesCm[i].Z));
                Assert.That(box.VisualSizesCm[i].Y, Is.GreaterThan(box.WheelWidthCm));
            });
        }

        Physics3DWheelLabTrialResult physicalResult = FindResult(results, Vehicle3DWheelKind.Physical);
        Physics3DWheelLabTrialResult boxResult = FindResult(results, Vehicle3DWheelKind.Box);
        Physics3DWheelLabTrialResult scanningResult = FindResult(results, Vehicle3DWheelKind.Scanning);

        bool physicalDiffersFromBox = TrialResultsDifferMaterially(physicalResult, boxResult);
        bool physicalDiffersFromScanning = TrialResultsDifferMaterially(physicalResult, scanningResult);
        bool boxDiffersFromScanning = TrialResultsDifferMaterially(boxResult, scanningResult);
        Assert.Multiple(() =>
        {
            Assert.That(
                physicalDiffersFromBox,
                Is.True,
                "Physical and Box wheel runs published no material measured driving difference.");
            Assert.That(
                physicalDiffersFromScanning,
                Is.True,
                "Physical and Scanning wheel runs published no material measured driving difference.");
            Assert.That(
                boxDiffersFromScanning,
                Is.True,
                "Box and Scanning wheel runs published no material measured driving difference.");
            Assert.That(physical.ShapeKinds[0], Is.Not.EqualTo(box.ShapeKinds[0]));
            Assert.That(scanning.ModeBodyCount, Is.Not.EqualTo(physical.ModeBodyCount));
            Assert.That(scanning.ModeBodyCount, Is.Not.EqualTo(box.ModeBodyCount));
        });
    }

    private static bool TrialResultsDifferMaterially(
        in Physics3DWheelLabTrialResult left,
        in Physics3DWheelLabTrialResult right)
    {
        const float compressionEpsilonCm = 0.5f;
        const float slipEpsilonCmPerSecond = 25f;
        const float groundedEpsilon = 0.02f;
        const float brakingEpsilonCm = 25f;
        return MathF.Abs(left.MaximumSuspensionCompressionCm - right.MaximumSuspensionCompressionCm) > compressionEpsilonCm ||
               MathF.Abs(left.MaximumSlipCmPerSecond - right.MaximumSlipCmPerSecond) > slipEpsilonCmPerSecond ||
               MathF.Abs(left.GroundedRatio - right.GroundedRatio) > groundedEpsilon ||
               MathF.Abs(left.BrakingDistanceCm - right.BrakingDistanceCm) > brakingEpsilonCm;
    }

    private static WheelModePresentationEvidence FindPresentation(
        WheelModePresentationEvidence[] presentations,
        Vehicle3DWheelKind kind)
    {
        for (int i = 0; i < presentations.Length; i++)
        {
            if (presentations[i].WheelKind == kind)
            {
                return presentations[i];
            }
        }

        throw new InvalidOperationException($"Missing presentation evidence for '{kind}'.");
    }

    private static Physics3DWheelLabTrialResult FindResult(
        Physics3DWheelLabTrialResult[] results,
        Vehicle3DWheelKind kind)
    {
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i].WheelKind == kind)
            {
                return results[i];
            }
        }

        throw new InvalidOperationException($"Missing trial result for '{kind}'.");
    }

    private static float ExtractYawRadians(Quaternion orientation)
    {
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, orientation);
        return MathF.Atan2(forward.X, forward.Z);
    }

    private static float NormalizeRadians(float radians)
    {
        while (radians > MathF.PI)
        {
            radians -= MathF.PI * 2f;
        }

        while (radians < -MathF.PI)
        {
            radians += MathF.PI * 2f;
        }

        return radians;
    }

    private static void PressPublicKey(GameEngine engine, TestInputBackend keyboard, string devicePath)
    {
        keyboard.SetButton(devicePath, true);
        TickEngine(engine, 1);
        keyboard.SetButton(devicePath, false);
        TickEngine(engine, 1);
    }

    private static void TickEngine(GameEngine engine, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(1f / 30f);
        }
    }

    private struct PublicKeyboardRouteObservation
    {
        public bool UsedSteerLeft;
        public bool UsedSteerRight;
        public bool UsedDrive;
        public bool UsedBrake;
        public float SteeringYawDeltaRadians;
        public float PeakCompressionCm;
        public bool SawSuspensionCompress;
        public bool SawSuspensionRebound;
        public int CurrentAirborneTicks;
        public int MaximumConsecutiveAirborneTicks;
        public bool CurrentlyAirborne;
        public bool SawLandingAfterAirborne;
        public float MaximumAbsLateralOffsetCm;

        public static PublicKeyboardRouteObservation CreateEmpty() => new();
    }

    private readonly struct WheelModePresentationEvidence
    {
        public WheelModePresentationEvidence(
            Vehicle3DWheelKind wheelKind,
            int modeBodyCount,
            int vehicleCount,
            int wheelCount,
            Physics3DShapeKind[] shapeKinds,
            Vector3[] visualSizesCm,
            float wheelRadiusCm,
            float wheelWidthCm)
        {
            WheelKind = wheelKind;
            ModeBodyCount = modeBodyCount;
            VehicleCount = vehicleCount;
            WheelCount = wheelCount;
            ShapeKinds = shapeKinds;
            VisualSizesCm = visualSizesCm;
            WheelRadiusCm = wheelRadiusCm;
            WheelWidthCm = wheelWidthCm;
        }

        public Vehicle3DWheelKind WheelKind { get; }
        public int ModeBodyCount { get; }
        public int VehicleCount { get; }
        public int WheelCount { get; }
        public Physics3DShapeKind[] ShapeKinds { get; }
        public Vector3[] VisualSizesCm { get; }
        public float WheelRadiusCm { get; }
        public float WheelWidthCm { get; }
    }

    private sealed class WheelLabHarness : IDisposable
    {
        public WheelLabHarness(float? maximumBrakeForce = null, int workerCount = 1)
        {
            JsonObject configJson = LoadOfficialConfigJson();
            if (maximumBrakeForce.HasValue)
            {
                configJson["wheelLab"]!.AsObject()["maximumBrakeForce"] = maximumBrakeForce.Value;
            }

            Physics3DShowcaseConfig config = Physics3DShowcaseConfig.Load(configJson);
            config.InitialScene = Physics3DShowcaseScene.WheelLab;
            config.MaximumBodies = 256;
            config.VisibleBodyLimit = 256;
            Ecs = World.Create();
            Physics = new Physics3DWorld(CreateWorldConfig(workerCount));
            Simulation = new Physics3DSimulationSystem(Ecs, Physics, sourceFixedStepHz: 30, maximumPhysicsStepsPerSourceTick: 1);
            Runtime = new Physics3DShowcaseRuntime();
            Runtime.ActivateForTests(Ecs, Physics, Simulation, config);
        }

        public World Ecs { get; }
        public Physics3DWorld Physics { get; }
        public Physics3DSimulationSystem Simulation { get; }
        public Physics3DShowcaseRuntime Runtime { get; }

        public void SwitchMode(Vehicle3DWheelKind mode)
        {
            PrepareModeSwitch(mode);
            CompletePreparedStep();
        }

        public void PrepareModeSwitch(Vehicle3DWheelKind mode)
        {
            Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
                Physics3DShowcaseCommandKind.SetWheelMode,
                (int)mode));
            Runtime.PrepareFixedStep();
        }

        public void CompletePreparedStep()
        {
            Simulation.Update(1f / 30f);
            Runtime.ObserveFixedStep();
        }

        public void Step()
        {
            Runtime.PrepareFixedStep();
            Simulation.Update(1f / 30f);
            Runtime.ObserveFixedStep();
        }

        public void Dispose()
        {
            Runtime.Dispose();
            Physics.Dispose();
            Ecs.Dispose();
        }

        private static Physics3DWorldConfig CreateWorldConfig(int workerCount)
        {
            return new Physics3DWorldConfig
            {
                MobileBodyCapacity = 128,
                StaticBodyCapacity = 128,
                ShapeCapacity = 256,
                InactiveIslandCapacity = 128,
                ConstraintCapacity = 512,
                ConstraintsPerTypeBatchCapacity = 512,
                ConstraintCountPerBodyEstimate = 16,
                ContactPairCapacityPerWorker = 4_096,
                ActuationCommandCapacity = 256,
                WorkerCount = workerCount,
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
    }

    private sealed class NullInputBackend : IInputBackend
    {
        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => false;
        public Vector2 GetMousePosition() => Vector2.Zero;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }

    private sealed class TestInputBackend : IInputBackend
    {
        private readonly System.Collections.Generic.HashSet<string> _pressed = new(StringComparer.Ordinal);

        public void SetButton(string devicePath, bool pressed)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
            if (pressed)
            {
                _pressed.Add(devicePath);
            }
            else
            {
                _pressed.Remove(devicePath);
            }
        }

        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => _pressed.Contains(devicePath);
        public Vector2 GetMousePosition() => Vector2.Zero;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }
}
