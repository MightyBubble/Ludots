using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CapabilityStandardPhysics3DShowcaseMod.Runtime;
using Ludots.Core.Character3D;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Physics3D;
using Ludots.Core.Scripting;
using Ludots.Core.Traversal3D;
using Ludots.Core.Vehicle3D;
using Ludots.Launcher.Backend;
using Ludots.Platform.Abstractions;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Events;
using Ludots.UI.Surface;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[NonParallelizable]
[Category("acceptance")]
public sealed class CapabilityStandardPhysics3DShowcaseAcceptanceTests
{
    private const string BindingName = "capability_standard_physics3d_showcase";
    private const string PresetId = "capability_standard_physics3d_showcase_raylib";
    private const string ShowcaseModId = "CapabilityStandardPhysics3DShowcaseMod";
    private const string PhysicsModId = "Physics3DMod";
    private const string MapId = "capability_standard_physics3d_showcase";
    private const string CameraId = "Camera.Profile.Physics3DLab";
    private const string CharacterRouteCameraId = "Camera.Profile.Physics3DCharacterRoute";
    private const string WheelLabCameraId = "Camera.Profile.Physics3DWheelLab";
    private const string PanelElementId = "capability-standard-physics3d-panel";
    private const int FixedHz = 30;
    private const double FixedStepBudgetMilliseconds = 1000d / FixedHz;
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // Player-facing station SSOT. Keep this order identical to Physics3DShowcaseScene and the panel buttons.
    private static readonly Physics3DShowcaseScene[] PlaygroundStations =
    {
        Physics3DShowcaseScene.ScannerRange,
        Physics3DShowcaseScene.MaterialHill,
        Physics3DShowcaseScene.PlatformStation,
        Physics3DShowcaseScene.WindTunnel,
        Physics3DShowcaseScene.TraversalCourse,
        Physics3DShowcaseScene.WheelLab,
        Physics3DShowcaseScene.RagdollLab,
        Physics3DShowcaseScene.ConstraintForge,
        Physics3DShowcaseScene.ReplayTheater,
        Physics3DShowcaseScene.ScaleCity
    };

    [Test]
    public void RootMod_ResolvesFormalLauncherDependenciesAndOneToOneThirtyHzClock()
    {
        string repoRoot = FindRepoRoot();
        LauncherLaunchPlan plan = ResolveLaunchPlan(repoRoot);

        Assert.That(plan.AdapterId, Is.EqualTo(LauncherPlatformIds.Raylib));
        Assert.That(plan.Selectors, Is.EqualTo(new[] { $"preset:{PresetId}" }));
        Assert.That(plan.RootModIds, Is.EqualTo(new[] { ShowcaseModId }));
        Assert.That(plan.OrderedModIds, Does.Contain("LudotsCoreMod"));
        Assert.That(plan.OrderedModIds, Does.Contain("CoreInputMod"));
        Assert.That(plan.OrderedModIds, Does.Contain("CameraProfilesMod"));
        Assert.That(plan.OrderedModIds, Does.Contain(PhysicsModId));
        Assert.That(plan.OrderedModIds, Does.Contain(ShowcaseModId));
        var orderedModIds = plan.OrderedModIds.ToList();
        Assert.That(
            orderedModIds.IndexOf(PhysicsModId),
            Is.LessThan(orderedModIds.IndexOf(ShowcaseModId)),
            "The authoritative Physics3D capability must load before the player-facing lab.");

        AssertModDependencies(repoRoot);
        AssertPhysicsModRuntimeDependencies(repoRoot);
        AssertEntryAssets(repoRoot);
        AssertClockConfig(Path.Combine(repoRoot, "assets", "Configs", "Engine", "clock.json"), includeStepCap: false);
        AssertClockConfig(
            Path.Combine(repoRoot, "mods", "capabilities", "physics3d", "Physics3DMod", "assets", "Configs", "Physics3D", "world.json"),
            includeStepCap: true);
        AssertClockConfig(
            Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "capability_standard",
                ShowcaseModId,
                "assets",
                "Configs",
                "Physics3D",
                "world.json"),
            includeStepCap: true);
    }

    [Test]
    public void NewPlayer_MaterialHillWaitsForPushAndRequiresResetBeforeSecondLaunch()
    {
        string repoRoot = FindRepoRoot();
        LauncherLaunchPlan plan = ResolveLaunchPlan(repoRoot);
        using GameEngine engine = CreateEngine(repoRoot, plan, out _);
        engine.LoadEntryMap(MapId);
        Tick(engine);

        var runtime = engine.GetService(CoreServiceKeys.BenchmarkSceneController) as Physics3DShowcaseRuntime
            ?? throw new InvalidOperationException("Physics3D showcase runtime was not installed by the resolved launch plan.");
        IUiSurfaceHost surfaceHost = engine.GetService(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("Acceptance UI surface host is missing.");

        Click(surfaceHost, SceneButtonId(Physics3DShowcaseScene.MaterialHill));
        TickUntil(
            engine,
            () => runtime.ActiveScene == Physics3DShowcaseScene.MaterialHill,
            maximumFrames: 128);

        AssertMaterialHillPlayerLoop(engine, runtime, surfaceHost);
    }

    [Test]
    public void Feature_ScannerRange_Scenario_PlayerPausesSingleStepsCapsuleSweepAndResetsIt()
    {
        // Given a new player sees Scanner Range waiting for an explicit scan and pauses the 30 Hz world.
        string repoRoot = FindRepoRoot();
        LauncherLaunchPlan plan = ResolveLaunchPlan(repoRoot);
        using GameEngine engine = CreateEngine(repoRoot, plan, out _);
        engine.LoadEntryMap(MapId);
        Tick(engine);
        var runtime = engine.GetService(CoreServiceKeys.BenchmarkSceneController) as Physics3DShowcaseRuntime
            ?? throw new InvalidOperationException("Physics3D showcase runtime is missing.");
        IUiSurfaceHost surfaceHost = engine.GetService(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("Acceptance UI surface host is missing.");
        Assert.That(runtime.ScannerHasResult, Is.False);
        ClickWhenPresent(engine, surfaceHost, "physics3d-action-pause");
        TickUntil(engine, () => runtime.CapturePanelState().Paused, maximumFrames: 128);

        // When the player picks Capsule Cast, maximum distance, All targets, and presses Play Scan.
        ClickWhenPresent(engine, surfaceHost, "physics3d-scanner-kind-capsulecast");
        ClickWhenPresent(engine, surfaceHost, "physics3d-scanner-distance-2");
        ClickWhenPresent(engine, surfaceHost, "physics3d-scanner-layer-2");
        ClickWhenPresent(engine, surfaceHost, "physics3d-scanner-run");
        TickUntil(engine, () => runtime.ScannerHasResult, maximumFrames: 128);
        Physics3DShowcasePanelState scanned = runtime.CapturePanelState();
        int queryIndex = (int)Physics3DShowcaseQueryKind.CapsuleCast - 1;

        // Then the paused sweep remains at its origin and exposes the red crossed #1 starting-overlap marker.
        Assert.Multiple(() =>
        {
            Assert.That(scanned.ScannerQueryKind, Is.EqualTo(Physics3DShowcaseQueryKind.CapsuleCast));
            Assert.That(scanned.ScannerLayerFilterName, Is.EqualTo("All targets"));
            Assert.That(scanned.ScannerQueries.CapsuleCastHits, Is.EqualTo(runtime.ActiveConfig.ScannerRange.TargetCount));
            Assert.That(scanned.ScannerQueryFailed, Is.False);
            Assert.That(scanned.ScannerPlaybackStatus, Is.EqualTo(Physics3DScannerPlaybackStatus.Playing));
            Assert.That(scanned.ScannerPlaybackTick, Is.Zero);
            Assert.That(scanned.ScannerVisibleHitCount, Is.EqualTo(1));
        });
        Assert.That(runtime.TryGetQueryHitVisual(queryIndex, 0, out Physics3DShowcaseQueryHitVisual first), Is.True);
        Assert.That(first.StartedOverlapping, Is.True);
        Assert.That(first.DistanceCm, Is.Zero.Within(0.001f));

        // When Single Step is pressed, then exactly one fixed playback frame advances while the world remains paused.
        ClickWhenPresent(engine, surfaceHost, "physics3d-action-single-step");
        TickUntil(engine, () => runtime.ScannerPlaybackTick == 1, maximumFrames: 128);
        Physics3DShowcasePanelState oneStep = runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(oneStep.Paused, Is.True);
            Assert.That(oneStep.ScannerPlaybackTick, Is.EqualTo(1));
            Assert.That(oneStep.ScannerPlaybackDistanceCm, Is.GreaterThan(0f));
        });

        // When playback resumes, then all #1..#N markers become visible nearest first.
        ClickWhenPresent(engine, surfaceHost, "physics3d-action-pause");
        TickUntil(
            engine,
            () => runtime.ScannerPlaybackStatus == Physics3DScannerPlaybackStatus.Complete,
            maximumFrames: runtime.ActiveConfig.ScannerRange.CastPlaybackDurationTicks * 16);
        Physics3DShowcasePanelState completed = runtime.CapturePanelState();
        Assert.That(completed.ScannerVisibleHitCount, Is.EqualTo(completed.ScannerQueries.CapsuleCastHits));
        float previousDistanceCm = float.NegativeInfinity;
        for (int hitIndex = 0; hitIndex < completed.ScannerVisibleHitCount; hitIndex++)
        {
            Assert.That(runtime.TryGetQueryHitVisual(queryIndex, hitIndex, out Physics3DShowcaseQueryHitVisual hit), Is.True);
            Assert.That(hit.DistanceCm, Is.GreaterThanOrEqualTo(previousDistanceCm));
            previousDistanceCm = hit.DistanceCm;
        }

        // When Reset Station is pressed, then the playhead, hit numbers, and result return to the authored waiting state.
        long revision = runtime.SceneRevision;
        ClickWhenPresent(engine, surfaceHost, "physics3d-action-reset");
        TickUntil(engine, () => runtime.SceneRevision > revision, maximumFrames: 128);
        Physics3DShowcasePanelState reset = runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(reset.ScannerHasResult, Is.False);
            Assert.That(reset.ScannerRunSequence, Is.Zero);
            Assert.That(reset.ScannerPlaybackStatus, Is.EqualTo(Physics3DScannerPlaybackStatus.Waiting));
            Assert.That(reset.ScannerPlaybackTick, Is.Zero);
            Assert.That(reset.ScannerVisibleHitCount, Is.Zero);
            Assert.That(reset.LastAction, Does.Contain("Reset Scanner Range"));
        });
    }

    [Test]
    public void Feature_WindTunnel_Scenario_PlayerSelectsReversesRelaunchesAndResetsThePair()
    {
        // Given the player enters Wind Tunnel with the Steady zone selected and forward fields.
        string repoRoot = FindRepoRoot();
        LauncherLaunchPlan plan = ResolveLaunchPlan(repoRoot);
        using GameEngine engine = CreateEngine(repoRoot, plan, out _);
        engine.LoadEntryMap(MapId);
        Tick(engine);
        var runtime = engine.GetService(CoreServiceKeys.BenchmarkSceneController) as Physics3DShowcaseRuntime
            ?? throw new InvalidOperationException("Physics3D showcase runtime is missing.");
        IUiSurfaceHost surfaceHost = engine.GetService(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("Acceptance UI surface host is missing.");
        ClickWhenPresent(engine, surfaceHost, SceneButtonId(Physics3DShowcaseScene.WindTunnel));
        TickUntil(engine, () => runtime.ActiveScene == Physics3DShowcaseScene.WindTunnel, maximumFrames: 128);

        // When the player selects Vortex, reverses the formal fields, and relaunches its pair.
        ClickWhenPresent(engine, surfaceHost, "physics3d-wind-zone-vortex");
        TickUntil(engine, () => runtime.WindTunnelZone == Physics3DShowcaseWindZone.Vortex, maximumFrames: 128);
        ClickWhenPresent(engine, surfaceHost, "physics3d-wind-reverse");
        TickUntil(engine, () => runtime.WindTunnelDirection == Physics3DShowcaseDriveDirection.Reverse, maximumFrames: 128);
        ClickWhenPresent(engine, surfaceHost, "physics3d-wind-relaunch");
        TickUntil(engine, () => runtime.CapturePanelState().LastAction.Contains("Relaunched", StringComparison.Ordinal), maximumFrames: 128);
        AdvanceObservedSteps(engine, runtime, minimumSteps: 45);
        Physics3DShowcasePanelState moved = runtime.CapturePanelState();

        // Then the chosen direction and the different light/heavy displacement are visible.
        Assert.Multiple(() =>
        {
            Assert.That(moved.WindZone, Is.EqualTo(Physics3DShowcaseWindZone.Vortex));
            Assert.That(moved.WindDirection, Is.EqualTo(Physics3DShowcaseDriveDirection.Reverse));
            Assert.That(moved.WindLightTravelCm, Is.GreaterThan(moved.WindHeavyTravelCm));
            Assert.That(moved.WindSummary, Does.Contain("Vortex").And.Contain("REVERSE"));
        });

        // When Reset Station is pressed, then authored Steady/Forward selection returns.
        long revision = runtime.SceneRevision;
        ClickWhenPresent(engine, surfaceHost, "physics3d-action-reset");
        TickUntil(engine, () => runtime.SceneRevision > revision, maximumFrames: 128);
        Physics3DShowcasePanelState reset = runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(reset.WindZone, Is.EqualTo(Physics3DShowcaseWindZone.Steady));
            Assert.That(reset.WindDirection, Is.EqualTo(Physics3DShowcaseDriveDirection.Forward));
            Assert.That(reset.LastAction, Does.Contain("Reset Wind Tunnel"));
        });
    }

    [Test]
    public void Feature_ConstraintForge_Scenario_PlayerPausesReversesRestartsAndResetsTheDrives()
    {
        // Given the player enters Constraint Forge with running forward drives.
        string repoRoot = FindRepoRoot();
        LauncherLaunchPlan plan = ResolveLaunchPlan(repoRoot);
        using GameEngine engine = CreateEngine(repoRoot, plan, out _);
        engine.LoadEntryMap(MapId);
        Tick(engine);
        var runtime = engine.GetService(CoreServiceKeys.BenchmarkSceneController) as Physics3DShowcaseRuntime
            ?? throw new InvalidOperationException("Physics3D showcase runtime is missing.");
        IUiSurfaceHost surfaceHost = engine.GetService(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("Acceptance UI surface host is missing.");
        ClickWhenPresent(engine, surfaceHost, SceneButtonId(Physics3DShowcaseScene.ConstraintForge));
        TickUntil(engine, () => runtime.ActiveScene == Physics3DShowcaseScene.ConstraintForge, maximumFrames: 128);
        AdvanceObservedSteps(engine, runtime, minimumSteps: 20);

        // When the player pauses, reverses, and restarts the authored drive targets.
        ClickWhenPresent(engine, surfaceHost, "physics3d-constraint-toggle");
        TickUntil(engine, () => !runtime.CapturePanelState().ConstraintDriveEnabled, maximumFrames: 128);
        ClickWhenPresent(engine, surfaceHost, "physics3d-constraint-reverse");
        TickUntil(
            engine,
            () => runtime.CapturePanelState().ConstraintDriveDirection == Physics3DShowcaseDriveDirection.Reverse,
            maximumFrames: 128);
        ClickWhenPresent(engine, surfaceHost, "physics3d-constraint-toggle");
        TickUntil(engine, () => runtime.CapturePanelState().ConstraintDriveEnabled, maximumFrames: 128);
        AdvanceObservedSteps(engine, runtime, minimumSteps: 20);
        Physics3DShowcasePanelState restarted = runtime.CapturePanelState();

        // Then the door, slider, servo, running state, and reverse direction are visible.
        Assert.Multiple(() =>
        {
            Assert.That(restarted.ConstraintDriveEnabled, Is.True);
            Assert.That(restarted.ConstraintDriveDirection, Is.EqualTo(Physics3DShowcaseDriveDirection.Reverse));
            Assert.That(restarted.ConstraintSummary, Does.Contain("RUNNING").And.Contain("door").And.Contain("slider").And.Contain("servo"));
            Assert.That(runtime.TryGetConstraintForgePlayerState(out _, out _, out _), Is.True);
        });

        // When Reset Station is pressed, then running-forward authored state returns.
        long revision = runtime.SceneRevision;
        ClickWhenPresent(engine, surfaceHost, "physics3d-action-reset");
        TickUntil(engine, () => runtime.SceneRevision > revision, maximumFrames: 128);
        Physics3DShowcasePanelState reset = runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(reset.ConstraintDriveEnabled, Is.True);
            Assert.That(reset.ConstraintDriveDirection, Is.EqualTo(Physics3DShowcaseDriveDirection.Forward));
            Assert.That(reset.LastAction, Does.Contain("Reset Constraint Forge"));
        });
    }

    [Test]
    public void Feature_CharacterRoutes_Scenario_PlayerSeesProgressCameraFollowAndExplicitRestart()
    {
        // Given a new player enters Platform Station from the overview camera.
        string repoRoot = FindRepoRoot();
        LauncherLaunchPlan plan = ResolveLaunchPlan(repoRoot);
        using GameEngine engine = CreateEngine(repoRoot, plan, out AcceptanceInputBackend inputBackend);
        engine.LoadEntryMap(MapId);
        Tick(engine);
        var runtime = engine.GetService(CoreServiceKeys.BenchmarkSceneController) as Physics3DShowcaseRuntime
            ?? throw new InvalidOperationException("Physics3D showcase runtime is missing.");
        IUiSurfaceHost surfaceHost = engine.GetService(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("Acceptance UI surface host is missing.");
        Assert.That(engine.GameSession.Camera.IsVirtualCameraActive(CameraId), Is.True);

        // When the player selects Platform Station and moves, the formal route camera follows the real character.
        ClickWhenPresent(engine, surfaceHost, SceneButtonId(Physics3DShowcaseScene.PlatformStation));
        TickUntil(
            engine,
            () => runtime.ActiveScene == Physics3DShowcaseScene.PlatformStation &&
                  engine.GameSession.Camera.IsVirtualCameraActive(CharacterRouteCameraId),
            maximumFrames: 128);
        inputBackend.SetButton("<Keyboard>/w", pressed: true);
        AdvanceObservedSteps(engine, runtime, minimumSteps: 8);
        inputBackend.SetButton("<Keyboard>/w", pressed: false);
        Character3DState player = runtime.GetPlayerCharacterStateForTests();
        Physics3DShowcasePanelState route = runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(route.CharacterRouteStatus, Is.EqualTo(Physics3DShowcaseRouteStatus.InProgress));
            Assert.That(route.CharacterRouteCheckpointCount, Is.EqualTo(4));
            Assert.That(route.CharacterRouteNextAction, Is.Not.Empty);
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm, Is.Not.Null);
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm!.Value.X, Is.EqualTo(player.PositionCm.X).Within(1f));
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm!.Value.Y, Is.EqualTo(player.PositionCm.Z).Within(1f));
        });

        // When Restart Route is pressed, progress resets and the character camera remains authoritative.
        long revision = runtime.SceneRevision;
        ClickWhenPresent(engine, surfaceHost, "physics3d-route-restart");
        TickUntil(engine, () => runtime.SceneRevision > revision, maximumFrames: 128);
        Assert.Multiple(() =>
        {
            Assert.That(runtime.CharacterRouteCheckpointIndex, Is.Zero);
            Assert.That(runtime.CharacterRouteStatus, Is.EqualTo(Physics3DShowcaseRouteStatus.InProgress));
            Assert.That(engine.GameSession.Camera.IsVirtualCameraActive(CharacterRouteCameraId), Is.True);
        });

        // When the player leaves the character station, the map overview camera is restored explicitly.
        ClickWhenPresent(engine, surfaceHost, SceneButtonId(Physics3DShowcaseScene.ScannerRange));
        TickUntil(
            engine,
            () => runtime.ActiveScene == Physics3DShowcaseScene.ScannerRange &&
                  engine.GameSession.Camera.IsVirtualCameraActive(CameraId),
            maximumFrames: 128);
        Assert.That(engine.GameSession.Camera.FollowTargetPositionCm, Is.Null);
    }

    [Test]
    public void Feature_PlatformStation_Scenario_PlayerCompletesFourLiveSurfacesWithKeyboard()
    {
        // Given a new player enters Platform Station through the formal launcher and sees the route camera.
        string repoRoot = FindRepoRoot();
        LauncherLaunchPlan plan = ResolveLaunchPlan(repoRoot);
        using GameEngine engine = CreateEngine(repoRoot, plan, out AcceptanceInputBackend inputBackend);
        engine.LoadEntryMap(MapId);
        Tick(engine);
        var runtime = engine.GetService(CoreServiceKeys.BenchmarkSceneController) as Physics3DShowcaseRuntime
            ?? throw new InvalidOperationException("Physics3D showcase runtime is missing.");
        IUiSurfaceHost surfaceHost = engine.GetService(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("Acceptance UI surface host is missing.");
        IPhysics3DWorld world = engine.GetService(Physics3DServiceKeys.World)
            ?? throw new InvalidOperationException("Physics3D world is missing.");
        ClickWhenPresent(engine, surfaceHost, SceneButtonId(Physics3DShowcaseScene.PlatformStation));
        TickUntil(
            engine,
            () => runtime.ActiveScene == Physics3DShowcaseScene.PlatformStation &&
                  engine.GameSession.Camera.IsVirtualCameraActive(CharacterRouteCameraId),
            maximumFrames: 128);

        // When the player boards, rides, jumps, releases movement on the conveyor, and approaches the one-way platform from below.
        Physics3DCharacterTraversalShowcaseConfig route = runtime.ActiveConfig.CharacterTraversal;
        int movingSupportTicks = 0;
        int rotatingSupportTicks = 0;
        bool inheritedMovingVelocity = false;
        bool conveyorCarriedWithoutInput = false;
        bool enteredOneWayFromBelow = false;
        bool clearedOneWayTop = false;
        bool reachedOneWaySideLane = false;
        bool reachedOneWayUnderside = false;
        bool oneWayJumpStarted = false;
        bool conveyorTracking = false;
        float conveyorStartX = 0f;
        Physics3DBodyId movingSupport = default;
        Physics3DBodyId rotatingSupport = default;
        Physics3DBodyId previousSupport = default;
        Vector3 previousSupportVelocity = Vector3.Zero;
        float oneWayBottom = route.PlatformStationOneWayCenterYCm - (route.DeckThicknessCm * 0.5f);
        float oneWayTop = route.PlatformStationOneWayCenterYCm + (route.DeckThicknessCm * 0.5f);
        float oneWayHalfDepth = route.PlatformStationOneWaySizeZCm * 0.5f;
        float oneWaySideLaneZ = oneWayHalfDepth + route.CharacterRadiusCm + route.PlatformOneWayPassThroughClearanceCm;

        for (int step = 0;
             step < route.PlatformRouteTimeLimitTicks &&
             runtime.CharacterRouteStatus == Physics3DShowcaseRouteStatus.InProgress;
             step++)
        {
            Character3DState character = runtime.GetPlayerCharacterStateForTests();
            int checkpoint = runtime.CharacterRouteCheckpointIndex;
            if (checkpoint <= 1 &&
                character.IsGrounded &&
                character.SupportVelocityCmPerSecond.Length() >= route.PlatformMinimumSupportSpeedCmPerSecond)
            {
                if (!movingSupport.IsValid)
                {
                    movingSupport = character.SupportBody;
                }

                if (character.SupportBody == movingSupport)
                {
                    movingSupportTicks++;
                }
            }

            if (movingSupport.IsValid &&
                previousSupport == movingSupport &&
                !character.IsGrounded &&
                previousSupportVelocity.Length() >= route.PlatformMinimumSupportSpeedCmPerSecond)
            {
                float supportSpeed = previousSupportVelocity.Length();
                float retained = Vector3.Dot(
                    character.LinearVelocityCmPerSecond,
                    previousSupportVelocity / supportSpeed);
                inheritedMovingVelocity |= retained >= supportSpeed * route.PlatformInheritedVelocityRatio;
            }

            bool withinRotatingPlatform =
                MathF.Abs(character.PositionCm.X - route.RotatingPlatformCenterXCm) <= route.RotatingPlatformRadiusCm &&
                MathF.Abs(character.PositionCm.Z) <= route.RotatingPlatformRadiusCm;
            if (checkpoint == 1 &&
                character.IsGrounded &&
                character.SupportBody != movingSupport &&
                withinRotatingPlatform)
            {
                if (!rotatingSupport.IsValid)
                {
                    rotatingSupport = character.SupportBody;
                }

                rotatingSupportTicks++;
            }

            bool onConveyor =
                checkpoint == 2 &&
                character.IsGrounded &&
                MathF.Abs(character.SupportVelocityCmPerSecond.X - route.PlatformStationConveyorSpeedCmPerSecond) <= 1f;
            if (onConveyor)
            {
                if (!conveyorTracking)
                {
                    conveyorTracking = true;
                    conveyorStartX = character.PositionCm.X;
                }
                else
                {
                    conveyorCarriedWithoutInput |=
                        character.PositionCm.X - conveyorStartX >= route.PlatformConveyorCarryDistanceCm;
                }
            }

            float oneWayHalfWidth = route.PlatformStationOneWaySizeXCm * 0.5f;
            bool withinOneWay =
                MathF.Abs(character.PositionCm.X - route.PlatformStationOneWayCenterXCm) <= oneWayHalfWidth &&
                MathF.Abs(character.PositionCm.Z) <= oneWayHalfDepth;
            enteredOneWayFromBelow |= withinOneWay &&
                                      character.PositionCm.Y <= oneWayBottom + route.PlatformOneWayPassThroughClearanceCm;
            clearedOneWayTop |= enteredOneWayFromBelow &&
                                withinOneWay &&
                                character.PositionCm.Y >= oneWayTop + route.PlatformOneWayPassThroughClearanceCm;
            reachedOneWaySideLane |= checkpoint == 3 && character.PositionCm.Z >= oneWaySideLaneZ;
            reachedOneWayUnderside |= reachedOneWaySideLane &&
                                       withinOneWay &&
                                       character.IsGrounded &&
                                       character.PositionCm.Y <= oneWayBottom + route.PlatformOneWayPassThroughClearanceCm;

            Vector2 move = Vector2.Zero;
            bool jump = false;
            switch (checkpoint)
            {
                case 0:
                    if (character.IsGrounded &&
                        character.SupportVelocityCmPerSecond.Length() >= route.PlatformMinimumSupportSpeedCmPerSecond)
                    {
                        Physics3DBodyState platform = world.GetBodyState(character.SupportBody);
                        move = MoveTowardX(character.PositionCm.X, platform.PositionCm.X);
                    }
                    else
                    {
                        move = MoveTowardX(character.PositionCm.X, route.MovingPlatformCenterXCm);
                        jump = character.IsGrounded &&
                               (character.PositionCm.X >= route.PlatformStationStartXCm +
                                                          (route.PlatformStationStartDeckSizeXCm * 0.35f) ||
                                MathF.Abs(character.PositionCm.X - route.MovingPlatformCenterXCm) <=
                                route.PlatformSizeXCm * 0.6f);
                    }
                    break;
                case 1:
                    float rotatingRideX = route.RotatingPlatformCenterXCm -
                                           (route.RotatingPlatformRadiusCm * 0.55f);
                    if (character.IsGrounded && character.SupportBody == movingSupport)
                    {
                        Physics3DBodyState platform = world.GetBodyState(movingSupport);
                        float movingPlatformDismountX = route.MovingPlatformCenterXCm +
                                                        route.MovingPlatformTravelCm -
                                                        (route.PlatformSizeXCm * 0.05f);
                        move = MoveTowardX(character.PositionCm.X, platform.PositionCm.X);
                        jump = MathF.Abs(character.PositionCm.X - platform.PositionCm.X) <= 20f &&
                               platform.PositionCm.X >= movingPlatformDismountX &&
                               character.SupportVelocityCmPerSecond.X >= route.PlatformMinimumSupportSpeedCmPerSecond;
                    }
                    else
                    {
                        move = MoveTowardX(character.PositionCm.X, rotatingRideX);
                    }

                    if (character.IsGrounded && character.SupportBody != movingSupport)
                    {
                        jump = false;
                    }

                    if (rotatingSupport.IsValid && character.SupportBody == rotatingSupport)
                    {
                        move = Vector2.Zero;
                        jump = false;
                    }
                    break;
                case 2:
                    if (!onConveyor)
                    {
                        float conveyorCenterX = route.RotatingPlatformCenterXCm + route.PlatformStationConveyorOffsetXCm;
                        move = MoveTowardX(character.PositionCm.X, conveyorCenterX);
                        jump = character.IsGrounded && character.SupportBody == rotatingSupport;
                    }
                    break;
                case 3:
                    if (!reachedOneWaySideLane)
                    {
                        move = Vector2.UnitY;
                    }
                    else if (!reachedOneWayUnderside &&
                             MathF.Abs(character.PositionCm.X - route.PlatformStationOneWayCenterXCm) > 10f)
                    {
                        move = MoveTowardX(character.PositionCm.X, route.PlatformStationOneWayCenterXCm);
                    }
                    else if (!reachedOneWayUnderside)
                    {
                        move = -Vector2.UnitY;
                    }
                    else if (!oneWayJumpStarted && character.IsGrounded)
                    {
                        jump = true;
                        oneWayJumpStarted = true;
                    }
                    else if (oneWayJumpStarted && character.IsGrounded && !clearedOneWayTop)
                    {
                        oneWayJumpStarted = false;
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected Platform Station checkpoint {checkpoint}.");
            }

            previousSupport = character.IsGrounded ? character.SupportBody : default;
            previousSupportVelocity = character.SupportVelocityCmPerSecond;
            SetCharacterKeyboardIntent(inputBackend, move, jump, traverse: false);
            Tick(engine);
        }

        ClearCharacterKeyboardIntent(inputBackend);

        // Then the route reports completion only after all four visible physical behaviors occurred.
        Physics3DShowcasePanelState completed = runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(completed.CharacterRouteStatus, Is.EqualTo(Physics3DShowcaseRouteStatus.Completed));
            Assert.That(completed.CharacterRouteCheckpointIndex, Is.EqualTo(4));
            Assert.That(completed.CharacterRouteSummary, Does.StartWith("COMPLETE"));
            Assert.That(movingSupportTicks, Is.GreaterThanOrEqualTo(route.PlatformStableSupportTicks));
            Assert.That(rotatingSupportTicks, Is.GreaterThanOrEqualTo(route.PlatformStableSupportTicks));
            Assert.That(inheritedMovingVelocity, Is.True, "Jumping from the moving lift did not retain lift velocity.");
            Assert.That(conveyorCarriedWithoutInput, Is.True, "Releasing movement on the conveyor did not carry the player.");
            Assert.That(enteredOneWayFromBelow, Is.True, "The player never entered the one-way platform from below.");
            Assert.That(clearedOneWayTop, Is.True, "The player never passed through the one-way platform to its top side.");
            Assert.That(engine.GameSession.Camera.IsVirtualCameraActive(CharacterRouteCameraId), Is.True);
        });
    }

    [Test]
    public void Feature_TraversalCourse_Scenario_PlayerCompletesBothMantlesWithKeyboard()
    {
        // Given a new player enters Traversal Course through the formal launcher.
        string repoRoot = FindRepoRoot();
        LauncherLaunchPlan plan = ResolveLaunchPlan(repoRoot);
        using GameEngine engine = CreateEngine(repoRoot, plan, out AcceptanceInputBackend inputBackend);
        engine.LoadEntryMap(MapId);
        Tick(engine);
        var runtime = engine.GetService(CoreServiceKeys.BenchmarkSceneController) as Physics3DShowcaseRuntime
            ?? throw new InvalidOperationException("Physics3D showcase runtime is missing.");
        IUiSurfaceHost surfaceHost = engine.GetService(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("Acceptance UI surface host is missing.");
        ClickWhenPresent(engine, surfaceHost, SceneButtonId(Physics3DShowcaseScene.TraversalCourse));
        TickUntil(
            engine,
            () => runtime.ActiveScene == Physics3DShowcaseScene.TraversalCourse &&
                  engine.GameSession.Camera.IsVirtualCameraActive(CharacterRouteCameraId),
            maximumFrames: 128);

        Physics3DCharacterTraversalShowcaseConfig route = runtime.ActiveConfig.CharacterTraversal;
        DriveTraversalCourseToLadder(engine, inputBackend, runtime, route);

        // When the player presses E at the ladder, climbs with D, and mantles its deck.
        SetCharacterKeyboardIntent(inputBackend, Vector2.Zero, jump: false, traverse: true);
        Tick(engine);
        SetCharacterKeyboardIntent(inputBackend, Vector2.Zero, jump: false, traverse: false);
        Assert.That(runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.Attached));
        Tick(engine);
        Assert.That(runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.Climbing));
        SetCharacterKeyboardIntent(inputBackend, Vector2.UnitY, jump: false, traverse: false);
        int ladderClimbSteps = 0;
        while (runtime.GetPlayerTraversalStatusForTests().State != Traversal3DState.LedgeHang &&
               ladderClimbSteps < 120)
        {
            Tick(engine);
            ladderClimbSteps++;
        }

        Assert.That(runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.LedgeHang));
        Tick(engine);
        Assert.That(runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.Mantling));
        int ladderMantleSteps = 0;
        while (runtime.GetPlayerTraversalStatusForTests().State != Traversal3DState.NormalMovement &&
               ladderMantleSteps < 120)
        {
            Tick(engine);
            ladderMantleSteps++;
        }

        ClearCharacterKeyboardIntent(inputBackend);
        Assert.That(runtime.CharacterRouteCheckpointIndex, Is.GreaterThanOrEqualTo(4));

        // When the player runs and jumps across the gap, presses E at the high wall, then completes the second mantle.
        float wallAttachReadyX = route.WallCenterXCm - (route.AttachProbeDistanceCm * 0.9f);
        bool gapJumped = false;
        int wallApproachSteps = 0;
        while (runtime.GetPlayerCharacterStateForTests().PositionCm.X < wallAttachReadyX &&
               wallApproachSteps < 240)
        {
            Character3DState character = runtime.GetPlayerCharacterStateForTests();
            bool jump = !gapJumped &&
                        character.PositionCm.X >= route.LadderDeckCenterXCm + (route.LadderDeckLengthCm * 0.3f);
            gapJumped |= jump;
            SetCharacterKeyboardIntent(inputBackend, Vector2.UnitX, jump, traverse: false);
            Tick(engine);
            wallApproachSteps++;
        }

        ClearCharacterKeyboardIntent(inputBackend);
        Character3DState atWall = runtime.GetPlayerCharacterStateForTests();
        Assert.Multiple(() =>
        {
            Assert.That(atWall.PositionCm.X, Is.GreaterThanOrEqualTo(wallAttachReadyX));
            Assert.That(atWall.PositionCm.X, Is.LessThan(route.WallCenterXCm - (route.WallThicknessCm * 0.5f)));
            Assert.That(gapJumped, Is.True);
        });

        SetCharacterKeyboardIntent(inputBackend, Vector2.Zero, jump: false, traverse: true);
        Tick(engine);
        SetCharacterKeyboardIntent(inputBackend, Vector2.Zero, jump: false, traverse: false);
        Assert.That(runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.Attached));
        Tick(engine);
        Assert.That(runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.Climbing));
        SetCharacterKeyboardIntent(inputBackend, Vector2.UnitY, jump: false, traverse: false);
        int wallClimbSteps = 0;
        while (runtime.GetPlayerTraversalStatusForTests().State != Traversal3DState.LedgeHang &&
               wallClimbSteps < 150)
        {
            Tick(engine);
            wallClimbSteps++;
        }

        Assert.That(runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.LedgeHang));
        Tick(engine);
        int wallMantleSteps = 0;
        while (runtime.GetPlayerTraversalStatusForTests().State != Traversal3DState.NormalMovement &&
               wallMantleSteps < 120)
        {
            Tick(engine);
            wallMantleSteps++;
        }

        ClearCharacterKeyboardIntent(inputBackend);

        // Then the visible route completes at 6/6 on the upper deck, with the route camera still following the player.
        Physics3DShowcasePanelState completed = runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(completed.CharacterRouteStatus, Is.EqualTo(Physics3DShowcaseRouteStatus.Completed));
            Assert.That(completed.CharacterRouteCheckpointIndex, Is.EqualTo(6));
            Assert.That(completed.CharacterRouteSummary, Does.StartWith("COMPLETE"));
            Assert.That(runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.NormalMovement));
            Assert.That(runtime.GetPlayerCharacterStateForTests().PositionCm.Y, Is.GreaterThan(route.WallDeckCenterYCm));
            Assert.That(engine.GameSession.Camera.IsVirtualCameraActive(CharacterRouteCameraId), Is.True);
        });
    }

    [Test]
    public void Feature_WheelLab_Scenario_PlayerKeepsEveryWheelTypeInViewAcrossTheCompleteCourse()
    {
        // Given the player enters Wheel Lab through the formal launcher path.
        string repoRoot = FindRepoRoot();
        LauncherLaunchPlan plan = ResolveLaunchPlan(repoRoot);
        using GameEngine engine = CreateEngine(repoRoot, plan, out AcceptanceInputBackend inputBackend);
        engine.LoadEntryMap(MapId);
        Tick(engine);
        var runtime = engine.GetService(CoreServiceKeys.BenchmarkSceneController) as Physics3DShowcaseRuntime
            ?? throw new InvalidOperationException("Physics3D showcase runtime is missing.");
        IUiSurfaceHost surfaceHost = engine.GetService(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("Acceptance UI surface host is missing.");
        ClickWhenPresent(engine, surfaceHost, SceneButtonId(Physics3DShowcaseScene.WheelLab));
        TickUntil(
            engine,
            () => runtime.ActiveScene == Physics3DShowcaseScene.WheelLab &&
                  engine.GameSession.Camera.IsVirtualCameraActive(WheelLabCameraId),
            maximumFrames: 128);

        Physics3DWheelLabShowcaseConfig config = runtime.ActiveConfig.WheelLab;
        Vehicle3DWheelKind[] wheelKinds =
        {
            Vehicle3DWheelKind.Physical,
            Vehicle3DWheelKind.Box,
            Vehicle3DWheelKind.Scanning
        };

        // When the player uses the same W/A/D and Space route for every wheel type.
        for (int run = 0; run < wheelKinds.Length; run++)
        {
            Assert.That(runtime.WheelLabMode, Is.EqualTo(wheelKinds[run]));
            bool[] visitedSections = new bool[Enum.GetValues<WheelLabCourseSection>().Length];
            float maximumCameraErrorCm = 0f;
            int minimumGroundedWheelCount = 4;
            bool sawGroundContact = false;
            visitedSections[(int)runtime.WheelLabSection] = true;

            inputBackend.SetButton("<Keyboard>/d", pressed: true);
            Tick(engine);
            inputBackend.SetButton("<Keyboard>/d", pressed: false);
            inputBackend.SetButton("<Keyboard>/a", pressed: true);
            Tick(engine);
            inputBackend.SetButton("<Keyboard>/a", pressed: false);
            for (int settleTick = 0; settleTick < 4; settleTick++)
            {
                Tick(engine);
            }

            CaptureWheelLabPlayerEvidence(
                engine,
                runtime,
                visitedSections,
                ref maximumCameraErrorCm,
                ref minimumGroundedWheelCount,
                ref sawGroundContact);
            Assert.That(runtime.WheelLabTrialStatus, Is.EqualTo(Physics3DWheelLabTrialStatus.Ready),
                "Steering at the start must not begin or invalidate the shared trial.");

            inputBackend.SetButton("<Keyboard>/w", pressed: true);
            for (int tick = 0; tick < config.TrialRecommendedThrottleTicks; tick++)
            {
                Tick(engine);
                CaptureWheelLabPlayerEvidence(
                    engine,
                    runtime,
                    visitedSections,
                    ref maximumCameraErrorCm,
                    ref minimumGroundedWheelCount,
                    ref sawGroundContact);
                if (runtime.WheelLabSection == WheelLabCourseSection.Braking)
                {
                    break;
                }
            }

            inputBackend.SetButton("<Keyboard>/w", pressed: false);
            inputBackend.SetButton("<Keyboard>/a", pressed: false);
            inputBackend.SetButton("<Keyboard>/d", pressed: false);
            Assert.That(
                runtime.WheelLabSection,
                Is.EqualTo(WheelLabCourseSection.Braking),
                $"{wheelKinds[run]} did not reach the visible green braking zone within the authored drive window.");
            inputBackend.SetButton("<Keyboard>/space", pressed: true);
            for (int tick = 0; tick < config.TrialRecommendedBrakeTicks; tick++)
            {
                Tick(engine);
                CaptureWheelLabPlayerEvidence(
                    engine,
                    runtime,
                    visitedSections,
                    ref maximumCameraErrorCm,
                    ref minimumGroundedWheelCount,
                    ref sawGroundContact);
            }

            inputBackend.SetButton("<Keyboard>/space", pressed: false);
            Tick(engine);
            CaptureWheelLabPlayerEvidence(
                engine,
                runtime,
                visitedSections,
                ref maximumCameraErrorCm,
                ref minimumGroundedWheelCount,
                ref sawGroundContact);

            Assert.That(
                runtime.TryGetWheelLabTrialResult(wheelKinds[run], out Physics3DWheelLabTrialResult result),
                Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(Physics3DWheelLabTrialStatus.Succeeded),
                    $"{wheelKinds[run]} did not complete the public Wheel Lab route: {result.Reason}.");
                Assert.That(result.MaximumSuspensionCompressionCm, Is.GreaterThan(0f));
                Assert.That(result.MaximumSlipCmPerSecond, Is.GreaterThan(0f));
                Assert.That(result.GroundedRatio, Is.GreaterThan(0f));
                Assert.That(result.BrakeMeasured, Is.True);
                Assert.That(result.BrakingDistanceCm, Is.GreaterThan(0f));
                Assert.That(sawGroundContact, Is.True);
                Assert.That(minimumGroundedWheelCount, Is.LessThan(4),
                    "The jump must visibly unload at least one wheel contact marker.");
                Assert.That(maximumCameraErrorCm, Is.LessThanOrEqualTo(1f),
                    "The Wheel Lab camera must stay attached to the chassis while WASD drives the vehicle.");
            });
            for (int section = (int)WheelLabCourseSection.Start;
                 section <= (int)WheelLabCourseSection.Finish;
                 section++)
            {
                Assert.That(
                    visitedSections[section],
                    Is.True,
                    $"{wheelKinds[run]} did not expose Wheel Lab section {(WheelLabCourseSection)section} to the player.");
            }

            if (run + 1 < wheelKinds.Length)
            {
                inputBackend.SetButton("<Keyboard>/q", pressed: true);
                Tick(engine);
                inputBackend.SetButton("<Keyboard>/q", pressed: false);
                TickUntil(
                    engine,
                    () => runtime.WheelLabMode == wheelKinds[run + 1],
                    maximumFrames: 128);
                Assert.That(engine.GameSession.Camera.IsVirtualCameraActive(WheelLabCameraId), Is.True);
            }
        }

        // Then leaving the station restores the authored overview camera with no stale follow target.
        ClickWhenPresent(engine, surfaceHost, SceneButtonId(Physics3DShowcaseScene.ScannerRange));
        TickUntil(
            engine,
            () => runtime.ActiveScene == Physics3DShowcaseScene.ScannerRange &&
                  engine.GameSession.Camera.IsVirtualCameraActive(CameraId),
            maximumFrames: 128);
        Assert.That(engine.GameSession.Camera.FollowTargetPositionCm, Is.Null);
    }

    [Test]
    public void Feature_RagdollLab_Scenario_PlayerKnocksDownAndRecoversTheMannequin()
    {
        // Given a new player enters Ragdoll Lab through the formal launcher and sees an active-pose mannequin.
        string repoRoot = FindRepoRoot();
        LauncherLaunchPlan plan = ResolveLaunchPlan(repoRoot);
        using GameEngine engine = CreateEngine(repoRoot, plan, out _);
        engine.LoadEntryMap(MapId);
        Tick(engine);
        var runtime = engine.GetService(CoreServiceKeys.BenchmarkSceneController) as Physics3DShowcaseRuntime
            ?? throw new InvalidOperationException("Physics3D showcase runtime is missing.");
        IUiSurfaceHost surfaceHost = engine.GetService(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("Acceptance UI surface host is missing.");
        Physics3DSimulationSystem simulation = engine.GetService(Physics3DServiceKeys.SimulationSystem)
            ?? throw new InvalidOperationException("Physics3D simulation system is missing.");
        ClickWhenPresent(engine, surfaceHost, SceneButtonId(Physics3DShowcaseScene.RagdollLab));
        TickUntil(engine, () => runtime.ActiveScene == Physics3DShowcaseScene.RagdollLab, maximumFrames: 128);

        // When the player swings the pendulum, releases active pose, tries recovery too early, waits for rest, and retries.
        RunRagdollLabPlayerRoute(engine, runtime, simulation, surfaceHost);

        // Then the visible mannequin has been handed back from dynamic ragdoll bodies to its recovered pose.
        Physics3DShowcasePanelState completed = runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(runtime.RagdollLabState.IsRecovered, Is.True);
            Assert.That(completed.RagdollSummary, Does.Contain("RECOVERED").And.Contain("clearance passed"));
            Assert.That(completed.LastAction, Does.Contain("Clearance passed"));
        });
    }

    [Test]
    [Category("scale")]
    public void NewPlayer_CanTourTenPlaygroundStationsControlTimeAndRunFiftyThousandBodiesInScaleCity()
    {
        string repoRoot = FindRepoRoot();
        LauncherLaunchPlan plan = ResolveLaunchPlan(repoRoot);
        using GameEngine engine = CreateEngine(repoRoot, plan, out AcceptanceInputBackend inputBackend);
        var trace = new List<string>();

        Assert.That(Ludots.Core.Engine.Time.FixedDeltaTime, Is.EqualTo(1f / FixedHz).Within(1e-6f));
        engine.LoadEntryMap(MapId);
        Tick(engine);

        var runtime = engine.GetService(CoreServiceKeys.BenchmarkSceneController) as Physics3DShowcaseRuntime
            ?? throw new InvalidOperationException("Physics3D showcase runtime was not installed by the resolved launch plan.");
        IPhysics3DWorld world = engine.GetService(Physics3DServiceKeys.World)
            ?? throw new InvalidOperationException("Physics3D world was not installed for the entry map.");
        Physics3DSimulationSystem simulation = engine.GetService(Physics3DServiceKeys.SimulationSystem)
            ?? throw new InvalidOperationException("Physics3D simulation system was not installed for the entry map.");
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
        {
            throw new InvalidOperationException("Acceptance UI surface host is missing.");
        }

        Assert.That(PlaygroundStations, Is.EqualTo(Enum.GetValues<Physics3DShowcaseScene>()));
        Assert.That(runtime.IsActive, Is.True);
        Assert.That(runtime.SuppressHostDiagnosticUi, Is.False, "The player-facing playground panel must remain visible in Raylib.");
        Assert.That(runtime.ActiveScene, Is.EqualTo(Physics3DShowcaseScene.ScannerRange));
        Assert.That(world.FixedDeltaSeconds, Is.EqualTo(1f / FixedHz).Within(1e-6f));
        Assert.That(surfaceHost.Scene, Is.Not.Null);
        Assert.That(surfaceHost.Scene!.FindByElementId(PanelElementId), Is.Not.Null);
        Assert.That(
            surfaceHost.Scene.HitTest(1500f, 800f),
            Is.Null,
            "The playground's blank screen area must remain available to world interaction.");
        for (int i = 0; i < PlaygroundStations.Length; i++)
        {
            Assert.That(
                surfaceHost.Scene.FindByElementId(SceneButtonId(PlaygroundStations[i])),
                Is.Not.Null,
                $"Station button for {PlaygroundStations[i]} must be present on first entry.");
        }

        trace.Add(JsonSerializer.Serialize(new
        {
            step = "entry",
            map = MapId,
            scene = runtime.ActiveScene.ToString(),
            stations = PlaygroundStations.Select(static station => station.ToString()).ToArray(),
            fixedHz = FixedHz,
            panel = "visible",
            worldInputOutsidePanel = "pass-through"
        }));

        foreach (Physics3DShowcaseScene scene in PlaygroundStations)
        {
            Click(surfaceHost, SceneButtonId(scene));
            TickUntil(engine, () => runtime.ActiveScene == scene, maximumFrames: 128);
            Assert.That(runtime.ActiveScene, Is.EqualTo(scene));
            Assert.That(runtime.BodyCount, Is.GreaterThan(0), $"{scene} must show physical content to the player.");

            if (scene == Physics3DShowcaseScene.ReplayTheater)
            {
                RunUntilDeterministicRebuildCompletes(engine, runtime, surfaceHost);
                Assert.That(
                    runtime.ReplayStatus,
                    Is.EqualTo(Physics3DShowcaseReplayStatus.Passed),
                    $"cursor={runtime.ReplayCursor}, expected={runtime.ReplayExpectedHash:X16}, actual={runtime.ReplayActualHash:X16}");
            }
            else
            {
                ResumeIfPaused(engine, surfaceHost, simulation);
                WaitForObservedPhysicsStep(engine, runtime, simulation, scene);
            }

            CollectStationEvidence(engine, runtime, simulation, surfaceHost, inputBackend, scene);

            Physics3DShowcasePanelState panel = runtime.CapturePanelState();
            trace.Add(JsonSerializer.Serialize(new
            {
                step = "station",
                scene = scene.ToString(),
                bodies = runtime.BodyCount,
                constraints = runtime.ConstraintCount,
                sceneStep = runtime.SceneStep,
                deterministicRebuildStatus = panel.DeterminismComparisonStatus.ToString(),
                scanner = panel.ScannerQueries,
                material = panel.MaterialSummary,
                wind = panel.WindSummary,
                wheel = panel.WheelSummary,
                ragdoll = panel.RagdollSummary
            }));
        }

        ResumeIfPaused(engine, surfaceHost, simulation);
        Click(surfaceHost, "physics3d-action-pause");
        TickUntil(engine, () => !simulation.Enabled, maximumFrames: 128);
        Assert.That(simulation.Enabled, Is.False);
        long stepsAfterPause = simulation.TotalPhysicsSteps;
        for (int i = 0; i < 8; i++)
        {
            Tick(engine);
        }
        Assert.That(simulation.TotalPhysicsSteps, Is.EqualTo(stepsAfterPause));

        Click(surfaceHost, "physics3d-action-single-step");
        TickUntil(engine, () => simulation.TotalPhysicsSteps == stepsAfterPause + 1, maximumFrames: 128);
        Assert.That(simulation.Enabled, Is.False);
        Assert.That(simulation.TotalPhysicsSteps, Is.EqualTo(stepsAfterPause + 1));
        trace.Add(JsonSerializer.Serialize(new
        {
            step = "pause-single-step",
            pausedSteps = stepsAfterPause,
            afterSingleStep = simulation.TotalPhysicsSteps,
            delta = simulation.TotalPhysicsSteps - stepsAfterPause
        }));

        Click(surfaceHost, "physics3d-action-pause");
        TickUntil(engine, () => simulation.Enabled, maximumFrames: 128);
        Assert.That(simulation.Enabled, Is.True);

        int[] scalePresets = { 1_000, 10_000, 25_000, 50_000 };
        foreach (int preset in scalePresets)
        {
            Click(surfaceHost, $"physics3d-benchmark-{preset}");
            TickUntil(
                engine,
                () => runtime.ActiveScene == Physics3DShowcaseScene.ScaleCity && runtime.DynamicBodyCount == preset,
                maximumFrames: 128);
            Assert.That(runtime.ActiveScene, Is.EqualTo(Physics3DShowcaseScene.ScaleCity));
            Assert.That(runtime.DynamicBodyCount, Is.EqualTo(preset));
            Assert.That(runtime.BodyCount, Is.EqualTo(preset + 1));
            Assert.That(world.ActiveMobileBodyCount, Is.EqualTo(preset));
            Physics3DScaleCityShowcaseState scaleCity = runtime.ScaleCityState;
            int expectedInteractiveBodies = Math.Min(preset, runtime.ActiveConfig.ScaleCity.InteractiveBodyLimit);
            Assert.That(scaleCity.InteractiveBodies, Is.EqualTo(expectedInteractiveBodies));
            Assert.That(scaleCity.SparseBodies, Is.EqualTo(preset - expectedInteractiveBodies));
            Assert.That(scaleCity.TotalBodies, Is.EqualTo(preset));
            Assert.That(scaleCity.PulseCount, Is.Zero);
            Assert.That(scaleCity.PulsedForegroundBodiesLastPulse, Is.Zero);
            int expectedPathCount = checked(
                runtime.ActiveConfig.BenchmarkLaneColumns * runtime.ActiveConfig.BenchmarkLaneDecks);
            Assert.That(runtime.BenchmarkPathCount, Is.EqualTo(expectedPathCount));
            Assert.That(runtime.BenchmarkWaveCount, Is.EqualTo(runtime.ActiveConfig.BenchmarkWaveCount));
        }

        Assert.That(world.AwakeBodyCount, Is.EqualTo(50_000));
        int visibleBodyLimit = runtime.ActiveConfig.VisibleBodyLimit;
        TickUntil(engine, () => runtime.VisibleBodyCount == visibleBodyLimit, maximumFrames: 128);
        Assert.That(runtime.VisibleBodyCount, Is.EqualTo(visibleBodyLimit));
        for (int i = 0; i < 10; i++)
        {
            TickUntilNextPhysicsStep(engine, simulation);
        }

        int motionProbeCount = Math.Min(
            runtime.ScaleCityState.InteractiveBodies,
            runtime.ActiveConfig.ScaleCity.LauncherWaveCount);
        var motionProbeStarts = new Vector3[motionProbeCount];
        for (int probeIndex = 0; probeIndex < motionProbeStarts.Length; probeIndex++)
        {
            Assert.That(
                runtime.TryGetBodyVisual(
                    probeIndex + 1,
                    out Physics3DBodyState probe,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _),
                Is.True);
            motionProbeStarts[probeIndex] = probe.PositionCm;
        }

        float maximumVisibleProbeMovementCm = 0f;
        bool observedRelaunch = false;
        var fiftyThousandSamples = new double[runtime.ActiveConfig.ScaleCity.PerformanceWindowSampleCount];
        int fiftyThousandPeakContactPairs = 0;
        for (int i = 0; i < fiftyThousandSamples.Length; i++)
        {
            TickUntilNextPhysicsStep(engine, simulation);
            fiftyThousandSamples[i] = simulation.PhysicsUpdateMillisecondsLastUpdate;
            fiftyThousandPeakContactPairs = Math.Max(fiftyThousandPeakContactPairs, world.ContactPairCount);
            observedRelaunch |= runtime.BenchmarkRecycledBodiesLastStep > 0;
            for (int probeIndex = 0; probeIndex < motionProbeStarts.Length; probeIndex++)
            {
                Assert.That(
                    runtime.TryGetBodyVisual(
                        probeIndex + 1,
                        out Physics3DBodyState probe,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _),
                    Is.True);
                maximumVisibleProbeMovementCm = MathF.Max(
                    maximumVisibleProbeMovementCm,
                    Vector3.Distance(probe.PositionCm, motionProbeStarts[probeIndex]));
            }
        }
        double fiftyThousandP95 = Percentile(fiftyThousandSamples, 0.95d);
        Physics3DScaleCityShowcaseState fiftyThousandState =
            AssertScaleCityContactEvidence(runtime, world, expectedBodies: 50_000);
        int fiftyThousandSteadyContactPairs = fiftyThousandState.ContactPairs;
        Assert.That(
            fiftyThousandP95,
            Is.LessThanOrEqualTo(runtime.ActiveConfig.BenchmarkRealTimeBudgetMilliseconds),
            $"50K Scale City Physics3D P95 {fiftyThousandP95:0.###}ms exceeds the configured 30Hz budget.");
        Assert.Multiple(() =>
        {
            Assert.That(fiftyThousandState.PerformanceStatus, Is.EqualTo(Physics3DScaleCityPerformanceStatus.Pass));
            Assert.That(fiftyThousandState.FullFrameP95Milliseconds,
                Is.LessThanOrEqualTo(runtime.ActiveConfig.BenchmarkRealTimeBudgetMilliseconds));
            Assert.That(fiftyThousandState.FullFrameP99Milliseconds,
                Is.LessThanOrEqualTo(runtime.ActiveConfig.BenchmarkRealTimeBudgetMilliseconds));
        });
        Assert.That(fiftyThousandPeakContactPairs, Is.GreaterThan(0), "Scale City foreground never produced real contacts.");
        Assert.That(observedRelaunch, Is.True, "Scale City never relaunched an authored wave.");
        Assert.That(
            maximumVisibleProbeMovementCm,
            Is.GreaterThan(runtime.ActiveConfig.BodySizeCm),
            "The 50K world was counted but its visible rigid bodies did not keep moving.");
        trace.Add(JsonSerializer.Serialize(new
        {
            step = "scale-city-50k-functional",
            authoritativeBodies = runtime.DynamicBodyCount,
            drawnBodies = runtime.VisibleBodyCount,
            paths = runtime.BenchmarkPathCount,
            waves = runtime.BenchmarkWaveCount,
            interactiveBodies = fiftyThousandState.InteractiveBodies,
            sparseBodies = fiftyThousandState.SparseBodies,
            windAccelerationXCmPerSecondSquared = fiftyThousandState.WindAccelerationXCmPerSecondSquared,
            lastLauncherWaveIndex = fiftyThousandState.LastLauncherWaveIndex,
            continuousRelaunch = observedRelaunch,
            maximumVisibleProbeMovementCm,
            physicsP95Ms = fiftyThousandP95,
            completeFrameP95Ms = fiftyThousandState.FullFrameP95Milliseconds,
            completeFrameP99Ms = fiftyThousandState.FullFrameP99Milliseconds,
            peakContactPairs = fiftyThousandPeakContactPairs,
            steadyContactPairs = fiftyThousandSteadyContactPairs,
            budget = fiftyThousandState.PerformanceStatus == Physics3DScaleCityPerformanceStatus.Pass
                ? "realtime"
                : "over-30hz-budget"
        }));

        Click(surfaceHost, "physics3d-benchmark-25000");
        TickUntil(
            engine,
            () => runtime.ActiveScene == Physics3DShowcaseScene.ScaleCity && runtime.DynamicBodyCount == 25_000,
            maximumFrames: 128);
        for (int i = 0; i < 10; i++)
        {
            TickUntilNextPhysicsStep(engine, simulation);
        }

        var twentyFiveThousandSamples = new double[runtime.ActiveConfig.ScaleCity.PerformanceWindowSampleCount];
        int twentyFiveThousandPeakContactPairs = 0;
        for (int i = 0; i < twentyFiveThousandSamples.Length; i++)
        {
            TickUntilNextPhysicsStep(engine, simulation);
            twentyFiveThousandSamples[i] = simulation.PhysicsUpdateMillisecondsLastUpdate;
            twentyFiveThousandPeakContactPairs = Math.Max(
                twentyFiveThousandPeakContactPairs,
                world.ContactPairCount);
        }

        double twentyFiveThousandP95 = Percentile(twentyFiveThousandSamples, 0.95d);
        Physics3DScaleCityShowcaseState twentyFiveThousandState =
            AssertScaleCityContactEvidence(runtime, world, expectedBodies: 25_000);
        int twentyFiveThousandSteadyContactPairs = twentyFiveThousandState.ContactPairs;
        Assert.That(
            twentyFiveThousandP95,
            Is.LessThanOrEqualTo(runtime.ActiveConfig.BenchmarkRealTimeBudgetMilliseconds),
            $"25K Scale City Physics3D P95 {twentyFiveThousandP95:0.###}ms exceeds the configured 30Hz budget.");
        Assert.Multiple(() =>
        {
            Assert.That(twentyFiveThousandState.PerformanceStatus, Is.EqualTo(Physics3DScaleCityPerformanceStatus.Pass));
            Assert.That(twentyFiveThousandState.FullFrameP95Milliseconds,
                Is.LessThanOrEqualTo(runtime.ActiveConfig.BenchmarkRealTimeBudgetMilliseconds));
            Assert.That(twentyFiveThousandState.FullFrameP99Milliseconds,
                Is.LessThanOrEqualTo(runtime.ActiveConfig.BenchmarkRealTimeBudgetMilliseconds));
        });
        Assert.That(twentyFiveThousandPeakContactPairs, Is.GreaterThan(0), "Scale City foreground never produced real contacts.");
        trace.Add(JsonSerializer.Serialize(new
        {
            step = "scale-city-25k-budget",
            authoritativeBodies = runtime.DynamicBodyCount,
            interactiveBodies = twentyFiveThousandState.InteractiveBodies,
            sparseBodies = twentyFiveThousandState.SparseBodies,
            physicsP95Ms = twentyFiveThousandP95,
            completeFrameP95Ms = twentyFiveThousandState.FullFrameP95Milliseconds,
            completeFrameP99Ms = twentyFiveThousandState.FullFrameP99Milliseconds,
            peakContactPairs = twentyFiveThousandPeakContactPairs,
            steadyContactPairs = twentyFiveThousandSteadyContactPairs,
            budgetMs = runtime.ActiveConfig.BenchmarkRealTimeBudgetMilliseconds,
            budget = twentyFiveThousandState.PerformanceStatus == Physics3DScaleCityPerformanceStatus.Pass
                ? "realtime"
                : "over-30hz-budget"
        }));

        Click(surfaceHost, "physics3d-benchmark-10000");
        TickUntil(
            engine,
            () => runtime.ActiveScene == Physics3DShowcaseScene.ScaleCity && runtime.DynamicBodyCount == 10_000,
            maximumFrames: 128);
        for (int i = 0; i < 10; i++)
        {
            TickUntilNextPhysicsStep(engine, simulation);
        }

        var samples = new double[runtime.ActiveConfig.ScaleCity.PerformanceWindowSampleCount];
        var endToEnd = new double[runtime.ActiveConfig.ScaleCity.PerformanceWindowSampleCount];
        int tenThousandPeakContactPairs = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            endToEnd[i] = TickUntilNextPhysicsStep(engine, simulation);
            samples[i] = simulation.PhysicsUpdateMillisecondsLastUpdate;
            tenThousandPeakContactPairs = Math.Max(tenThousandPeakContactPairs, world.ContactPairCount);
        }

        double physicsP50 = Percentile(samples, 0.50d);
        double physicsP95 = Percentile(samples, 0.95d);
        double physicsP99 = Percentile(samples, 0.99d);
        double endToEndP95 = Percentile(endToEnd, 0.95d);
        Physics3DScaleCityShowcaseState tenThousandState =
            AssertScaleCityContactEvidence(runtime, world, expectedBodies: 10_000);
        int tenThousandSteadyContactPairs = tenThousandState.ContactPairs;
        Assert.That(
            physicsP95,
            Is.LessThanOrEqualTo(FixedStepBudgetMilliseconds),
            $"10K authoritative Physics3D P95 {physicsP95:0.###}ms exceeds the 30Hz step budget {FixedStepBudgetMilliseconds:0.###}ms.");
        Assert.Multiple(() =>
        {
            Assert.That(tenThousandState.PerformanceStatus, Is.EqualTo(Physics3DScaleCityPerformanceStatus.Pass));
            Assert.That(tenThousandState.FullFrameP95Milliseconds,
                Is.LessThanOrEqualTo(runtime.ActiveConfig.BenchmarkRealTimeBudgetMilliseconds));
            Assert.That(tenThousandState.FullFrameP99Milliseconds,
                Is.LessThanOrEqualTo(runtime.ActiveConfig.BenchmarkRealTimeBudgetMilliseconds));
        });
        Assert.That(tenThousandPeakContactPairs, Is.GreaterThan(0), "Scale City foreground never produced real contacts.");
        trace.Add(JsonSerializer.Serialize(new
        {
            step = "scale-city-10k-budget",
            authoritativeBodies = runtime.DynamicBodyCount,
            drawnBodies = runtime.VisibleBodyCount,
            paths = runtime.BenchmarkPathCount,
            waves = runtime.BenchmarkWaveCount,
            interactiveBodies = tenThousandState.InteractiveBodies,
            sparseBodies = tenThousandState.SparseBodies,
            continuousRelaunch = observedRelaunch,
            physicsP50Ms = physicsP50,
            physicsP95Ms = physicsP95,
            physicsP99Ms = physicsP99,
            completeFrameP95Ms = tenThousandState.FullFrameP95Milliseconds,
            completeFrameP99Ms = tenThousandState.FullFrameP99Milliseconds,
            endToEndP95Ms = endToEndP95,
            peakContactPairs = tenThousandPeakContactPairs,
            steadyContactPairs = tenThousandSteadyContactPairs,
            budgetMs = runtime.ActiveConfig.BenchmarkRealTimeBudgetMilliseconds,
            budget = tenThousandState.PerformanceStatus == Physics3DScaleCityPerformanceStatus.Pass
                ? "realtime"
                : "over-30hz-budget"
        }));

        engine.UnloadMap(MapId);
        Assert.That(runtime.IsActive, Is.False);
        TickUntil(engine, () => surfaceHost.Scene?.FindByElementId(PanelElementId) == null, maximumFrames: 128);
        Assert.That(surfaceHost.Scene?.FindByElementId(PanelElementId), Is.Null);
        trace.Add(JsonSerializer.Serialize(new
        {
            step = "leave-map",
            map = MapId,
            panel = "released"
        }));

        WriteAcceptanceArtifacts(
            repoRoot,
            trace,
            physicsP50,
            physicsP95,
            physicsP99,
            endToEndP95,
            twentyFiveThousandP95,
            fiftyThousandP95,
            tenThousandPeakContactPairs,
            tenThousandSteadyContactPairs,
            twentyFiveThousandPeakContactPairs,
            twentyFiveThousandSteadyContactPairs,
            fiftyThousandPeakContactPairs,
            fiftyThousandSteadyContactPairs,
            visibleBodyLimit);
    }

    private static LauncherLaunchPlan ResolveLaunchPlan(string repoRoot)
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"ludots-physics3d-showcase-launcher-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            string preferencesPath = Path.Combine(tempDirectory, "preferences.json");
            string userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
            File.WriteAllText(preferencesPath, "{}", Utf8NoBom);
            File.WriteAllText(userConfigPath, "{}", Utf8NoBom);
            var launcher = new LauncherService(
                repoRoot,
                Path.Combine(repoRoot, "launcher.config.json"),
                Path.Combine(repoRoot, "launcher.presets.json"),
                preferencesPath,
                userConfigPath);
            return launcher.Resolve(
                new[] { $"preset:{PresetId}" },
                LauncherPlatformIds.Raylib,
                LauncherBuildMode.Never).Plan;
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static GameEngine CreateEngine(
        string repoRoot,
        LauncherLaunchPlan plan,
        out AcceptanceInputBackend inputBackend)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            plan.Mods.Select(static mod => mod.RootPath).ToList(),
            Path.Combine(repoRoot, "assets"));
        inputBackend = InstallInput(engine);
        AcceptanceUiHostInstaller.Install(engine, 1600f, 900f);
        engine.Start();
        return engine;
    }

    private static AcceptanceInputBackend InstallInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputBackend = new AcceptanceInputBackend();
        var inputHandler = new PlayerInputHandler(inputBackend, inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.AuthoritativeInput, inputHandler);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        return inputBackend;
    }

    private static void AssertModDependencies(string repoRoot)
    {
        string modJson = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            ShowcaseModId,
            "mod.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(modJson, Encoding.UTF8));
        string[] dependencies = document.RootElement
            .GetProperty("dependencies")
            .EnumerateObject()
            .Select(static property => property.Name)
            .ToArray();
        Assert.That(
            dependencies,
            Is.EquivalentTo(new[] { "LudotsCoreMod", "CoreInputMod", "CameraProfilesMod", PhysicsModId }));
    }

    private static void AssertPhysicsModRuntimeDependencies(string repoRoot)
    {
        string modRoot = Path.Combine(repoRoot, "mods", "capabilities", "physics3d", PhysicsModId);
        string projectPath = Path.Combine(modRoot, $"{PhysicsModId}.csproj");
        XDocument project = XDocument.Load(projectPath);
        string? copyLocalLockFileAssemblies = project
            .Descendants()
            .FirstOrDefault(static element => element.Name.LocalName == "CopyLocalLockFileAssemblies")
            ?.Value;
        Assert.That(
            copyLocalLockFileAssemblies,
            Is.EqualTo("true").IgnoreCase,
            "Physics3DMod must publish package runtime dependencies into its formal bin/net8.0 Mod output.");

        string outputDirectory = Path.Combine(modRoot, "bin", "net8.0");
        Assert.That(File.Exists(Path.Combine(outputDirectory, "BepuPhysics.dll")), Is.True);
        Assert.That(File.Exists(Path.Combine(outputDirectory, "BepuUtilities.dll")), Is.True);

        string dependencyManifest = File.ReadAllText(
            Path.Combine(outputDirectory, $"{PhysicsModId}.deps.json"),
            Encoding.UTF8);
        Assert.That(dependencyManifest, Does.Contain("BepuPhysics/2.4.0"));
        Assert.That(dependencyManifest, Does.Contain("BepuUtilities/2.4.0"));
    }

    private static void AssertEntryAssets(string repoRoot)
    {
        string assetRoot = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            ShowcaseModId,
            "assets");
        using (JsonDocument game = JsonDocument.Parse(File.ReadAllText(Path.Combine(assetRoot, "game.json"), Encoding.UTF8)))
        {
            Assert.That(game.RootElement.GetProperty("startupMapId").GetString(), Is.EqualTo(MapId));
            Assert.That(game.RootElement.GetProperty("targetFps").GetInt32(), Is.Zero);
        }

        using (JsonDocument map = JsonDocument.Parse(File.ReadAllText(Path.Combine(assetRoot, "Maps", $"{MapId}.json"), Encoding.UTF8)))
        {
            Assert.That(map.RootElement.GetProperty("Id").GetString(), Is.EqualTo(MapId));
            Assert.That(
                map.RootElement.GetProperty("DefaultCamera").GetProperty("VirtualCameraId").GetString(),
                Is.EqualTo(CameraId));
            Assert.That(
                map.RootElement.GetProperty("DefaultCamera").GetProperty("DistanceCm").GetInt32(),
                Is.EqualTo(7_000));
        }

        using (JsonDocument cameras = JsonDocument.Parse(File.ReadAllText(
                   Path.Combine(assetRoot, "Configs", "Camera", "virtual_cameras.json"),
                   Encoding.UTF8)))
        {
            JsonElement camera = cameras.RootElement
                .EnumerateArray()
                .Single(candidate => candidate.GetProperty("id").GetString() == CameraId);
            Assert.That(camera.GetProperty("id").GetString(), Is.EqualTo(CameraId));
            Assert.That(camera.GetProperty("distanceCm").GetInt32(), Is.EqualTo(7_000));
            Assert.That(camera.GetProperty("panMode").GetString(), Is.EqualTo("Keyboard"));
            Assert.That(camera.GetProperty("enableGrabDrag").GetBoolean(), Is.True);
            JsonElement wheelCamera = cameras.RootElement
                .EnumerateArray()
                .Single(candidate => candidate.GetProperty("id").GetString() == WheelLabCameraId);
            Assert.Multiple(() =>
            {
                Assert.That(wheelCamera.GetProperty("targetSource").GetString(), Is.EqualTo("FollowTarget"));
                Assert.That(wheelCamera.GetProperty("followMode").GetString(), Is.EqualTo("AlwaysFollow"));
                Assert.That(wheelCamera.GetProperty("panMode").GetString(), Is.EqualTo("None"));
                Assert.That(wheelCamera.GetProperty("enableGrabDrag").GetBoolean(), Is.False);
            });
        }

        using (JsonDocument config = JsonDocument.Parse(File.ReadAllText(
                   Path.Combine(assetRoot, "CapabilityStandardPhysics3DShowcaseConfig.json"),
                   Encoding.UTF8)))
        {
            JsonElement root = config.RootElement;
            Assert.That(root.GetProperty("initialScene").GetString(), Is.EqualTo("ScannerRange"));
            Assert.That(root.GetProperty("maximumBodies").GetInt32(), Is.EqualTo(50_001));
            Assert.That(root.GetProperty("chainLinkCount").GetInt32(), Is.EqualTo(14));
            Assert.That(root.GetProperty("queryHitCapacity").GetInt32(), Is.EqualTo(128));
            Assert.That(root.GetProperty("replaySteps").GetInt32(), Is.EqualTo(54));
            Assert.That(
                root.GetProperty("wheelLab").GetProperty("cameraId").GetString(),
                Is.EqualTo(WheelLabCameraId));
            Assert.That(root.GetProperty("replayGridSize").GetInt32(), Is.EqualTo(6));
            Assert.That(root.GetProperty("replayBodySpacingCm").GetInt32(), Is.EqualTo(180));
            Assert.That(root.GetProperty("replayCenterXCm").GetInt32(), Is.EqualTo(1_000));
            Assert.That(root.GetProperty("replayBaseHeightCm").GetInt32(), Is.EqualTo(1_800));
            Assert.That(root.GetProperty("replayLaneOffsetCm").GetInt32(), Is.EqualTo(1_100));
            Assert.That(root.GetProperty("benchmarkDefaultBodies").GetInt32(), Is.EqualTo(10_000));
            Assert.That(root.GetProperty("benchmarkLaneColumns").GetInt32(), Is.EqualTo(40));
            Assert.That(root.GetProperty("benchmarkLaneDecks").GetInt32(), Is.EqualTo(1_250));
            Assert.That(root.GetProperty("benchmarkLaneSpacingCm").GetInt32(), Is.GreaterThan(root.GetProperty("bodySizeCm").GetInt32()));
            Assert.That(root.GetProperty("benchmarkDeckSpacingCm").GetInt32(), Is.GreaterThan(root.GetProperty("bodySizeCm").GetInt32()));
            Assert.That(root.GetProperty("benchmarkCycleSteps").GetInt32(), Is.EqualTo(120));
            Assert.That(root.GetProperty("benchmarkWaveCount").GetInt32(), Is.EqualTo(120));
            Assert.That(root.GetProperty("benchmarkBaseHeightCm").GetInt32(), Is.EqualTo(200));
            Assert.That(root.GetProperty("benchmarkArcHeightCm").GetInt32(), Is.EqualTo(1_962));
            Assert.That(root.GetProperty("benchmarkTravelHalfWidthCm").GetInt32(), Is.EqualTo(1_800));
            Assert.That(root.GetProperty("benchmarkSpeedCmPerSecond").GetInt32(), Is.EqualTo(900));
            Assert.That(root.GetProperty("benchmarkSpinRadiansPerSecond").GetSingle(), Is.EqualTo(1.4f));
            Assert.That(
                root.GetProperty("benchmarkRealTimeBudgetMilliseconds").GetSingle(),
                Is.EqualTo((float)FixedStepBudgetMilliseconds).Within(0.001f));
            Assert.That(
                root.GetProperty("benchmarkPresets").EnumerateArray().Select(static value => value.GetInt32()).ToArray(),
                Is.EqualTo(new[] { 1_000, 10_000, 25_000, 50_000 }));
        }

        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(assetRoot, "Configs", "config_catalog.json"),
            Encoding.UTF8));
        string[] catalogPaths = catalog.RootElement
            .EnumerateArray()
            .Select(static entry => entry.GetProperty("Path").GetString() ?? string.Empty)
            .ToArray();
        Assert.That(
            catalogPaths,
            Is.EqualTo(new[]
            {
                "CapabilityStandardPhysics3DShowcaseConfig.json",
                "Physics3D/world.json",
                "Camera/virtual_cameras.json"
            }));
    }

    private static void AssertClockConfig(string path, bool includeStepCap)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        JsonElement root = document.RootElement;
        string fixedHzProperty = root.TryGetProperty("FixedHz", out _)
            ? "FixedHz"
            : "FixedStepHz";
        Assert.That(root.GetProperty(fixedHzProperty).GetInt32(), Is.EqualTo(FixedHz), path);
        if (includeStepCap)
        {
            Assert.That(root.GetProperty("MaximumPhysicsStepsPerSourceTick").GetInt32(), Is.EqualTo(1), path);
            Assert.That(root.GetProperty("ActuationCommandCapacity").GetInt32(), Is.EqualTo(100_000), path);
        }
    }

    private static void WaitForObservedPhysicsStep(
        GameEngine engine,
        Physics3DShowcaseRuntime runtime,
        Physics3DSimulationSystem simulation,
        Physics3DShowcaseScene scene)
    {
        for (int frame = 0; frame < 128 && runtime.SceneStep == 0; frame++)
        {
            Tick(engine);
        }

        Assert.That(
            runtime.SceneStep,
            Is.GreaterThan(0),
            $"Station {scene} did not complete Prepare -> simulation step -> Observe. " +
            $"simulation.Enabled={simulation.Enabled}, lastSteps={simulation.PhysicsStepsLastUpdate}, " +
            $"totalSteps={simulation.TotalPhysicsSteps}, engineTick={engine.GameSession.CurrentTick}.");
    }

    private static void AdvanceObservedSteps(
        GameEngine engine,
        Physics3DShowcaseRuntime runtime,
        int minimumSteps)
    {
        if (minimumSteps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumSteps));
        }

        long target = runtime.SceneStep + minimumSteps;
        TickUntil(
            engine,
            () => runtime.SceneStep >= target,
            maximumFrames: Math.Max(256, minimumSteps * 8));
    }

    private static Physics3DScaleCityShowcaseState AssertScaleCityContactEvidence(
        Physics3DShowcaseRuntime runtime,
        IPhysics3DWorld world,
        int expectedBodies)
    {
        Physics3DScaleCityShowcaseState state = runtime.ScaleCityState;
        int expectedInteractiveBodies = Math.Min(
            expectedBodies,
            runtime.ActiveConfig.ScaleCity.InteractiveBodyLimit);
        Assert.Multiple(() =>
        {
            Assert.That(state.TotalBodies, Is.EqualTo(expectedBodies));
            Assert.That(state.InteractiveBodies, Is.EqualTo(expectedInteractiveBodies));
            Assert.That(state.SparseBodies, Is.EqualTo(expectedBodies - expectedInteractiveBodies));
            Assert.That(state.ContactPairs, Is.EqualTo(world.ContactPairCount).And.GreaterThan(0));
            Assert.That(float.IsFinite(state.WindAccelerationXCmPerSecondSquared), Is.True);
            Assert.That(state.LastLauncherWaveIndex, Is.GreaterThanOrEqualTo(0));
        });

        var pairs = new Physics3DContactPair[state.ContactPairs];
        int copiedPairCount = world.CopyContactPairs(pairs);
        Assert.That(copiedPairCount, Is.EqualTo(state.ContactPairs));
        for (int pairIndex = 0; pairIndex < copiedPairCount; pairIndex++)
        {
            Physics3DContactPair pair = pairs[pairIndex];
            if (runtime.IsScaleCitySparseBody(pair.BodyA) || runtime.IsScaleCitySparseBody(pair.BodyB))
            {
                Assert.Fail(
                    $"Scale City sparse body entered contact pair {pairIndex}: {pair.BodyA} / {pair.BodyB}.");
            }
        }

        return state;
    }

    private static void AssertMaterialHillPlayerLoop(
        GameEngine engine,
        Physics3DShowcaseRuntime runtime,
        IUiSurfaceHost surfaceHost)
    {
        var startPositions = new Vector3[3];
        for (int laneIndex = 0; laneIndex < startPositions.Length; laneIndex++)
        {
            Assert.That(
                runtime.TryGetMaterialHillLaneState(
                    laneIndex,
                    out Physics3DBodyState state,
                    out _,
                    out _),
                Is.True);
            startPositions[laneIndex] = state.PositionCm;
        }

        AdvanceObservedSteps(engine, runtime, minimumSteps: 8);
        Assert.That(runtime.MaterialHillImpulseSubmissionCount, Is.Zero);
        for (int laneIndex = 0; laneIndex < startPositions.Length; laneIndex++)
        {
            Assert.That(
                runtime.TryGetMaterialHillLaneState(
                    laneIndex,
                    out Physics3DBodyState waitingState,
                    out _,
                    out float waitingTravelCm),
                Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(waitingState.Awake, Is.False);
                Assert.That(waitingState.PositionCm, Is.EqualTo(startPositions[laneIndex]));
                Assert.That(waitingTravelCm, Is.EqualTo(0f).Within(0.001f));
            });
        }

        Click(surfaceHost, "physics3d-action-impact");
        TickUntil(
            engine,
            () => runtime.MaterialHillImpulseSubmissionCount == 3,
            maximumFrames: 128);
        Assert.That(
            runtime.MaterialHillImpulseSubmissionCount,
            Is.EqualTo(3),
            "Push Crates must submit one authored push impulse per lane at the fixed-step boundary.");

        Click(surfaceHost, "physics3d-action-impact");
        TickUntil(
            engine,
            () => runtime.CapturePanelState().LastAction.Contains("Reset", StringComparison.Ordinal),
            maximumFrames: 128);
        Assert.Multiple(() =>
        {
            Assert.That(runtime.MaterialHillImpulseSubmissionCount, Is.EqualTo(3));
            Assert.That(runtime.CapturePanelState().LastAction, Does.Contain("Reset"));
        });

        Click(surfaceHost, "physics3d-action-reset");
        TickUntil(
            engine,
            () => runtime.MaterialHillImpulseSubmissionCount == 0,
            maximumFrames: 128);
        for (int laneIndex = 0; laneIndex < startPositions.Length; laneIndex++)
        {
            Assert.That(
                runtime.TryGetMaterialHillLaneState(
                    laneIndex,
                    out Physics3DBodyState resetState,
                    out _,
                    out float resetTravelCm),
                Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(resetState.Awake, Is.False);
                Assert.That(resetState.PositionCm, Is.EqualTo(startPositions[laneIndex]));
                Assert.That(resetTravelCm, Is.EqualTo(0f).Within(0.001f));
            });
        }

        Click(surfaceHost, "physics3d-action-impact");
        TickUntil(
            engine,
            () => runtime.MaterialHillImpulseSubmissionCount == 3,
            maximumFrames: 128);
        TickUntil(
            engine,
            () => runtime.MaterialHillState.Status is
                Physics3DShowcaseChallengeStatus.Complete or Physics3DShowcaseChallengeStatus.Failed,
            maximumFrames: runtime.ActiveConfig.MaterialHill.CompletionTimeLimitTicks * 8);
        Physics3DShowcasePanelState panel = runtime.CapturePanelState();
        Assert.That(panel.MaterialHill.Status, Is.EqualTo(Physics3DShowcaseChallengeStatus.Complete));
        Assert.That(panel.MaterialSummary,
            Does.StartWith("COMPLETE").And.Contain("Ice").And.Contain("Wood").And.Contain("Rubber").And.Contain("slid").And.Contain("(-"));
        Assert.That(
            runtime.TryGetMaterialHillLaneState(0, out _, out float iceFriction, out float iceTravel),
            Is.True);
        Assert.That(
            runtime.TryGetMaterialHillLaneState(2, out _, out float rubberFriction, out float rubberTravel),
            Is.True);
        Assert.That(iceFriction, Is.LessThan(rubberFriction));
        Assert.That(iceTravel, Is.GreaterThan(rubberTravel));
    }

    private static void CollectStationEvidence(
        GameEngine engine,
        Physics3DShowcaseRuntime runtime,
        Physics3DSimulationSystem simulation,
        IUiSurfaceHost surfaceHost,
        AcceptanceInputBackend inputBackend,
        Physics3DShowcaseScene scene)
    {
        switch (scene)
        {
            case Physics3DShowcaseScene.ScannerRange:
            {
                ClickWhenPresent(engine, surfaceHost, "physics3d-scanner-run");
                TickUntil(engine, () => runtime.ScannerHasResult, maximumFrames: 128);
                Physics3DShowcaseScannerQueryEvidence queries =
                    runtime.CapturePanelState().ScannerQueries;
                Assert.Multiple(() =>
                {
                    Assert.That(queries.RayHits, Is.EqualTo(runtime.GetQueryHitCount(0)).And.GreaterThan(0));
                    Assert.That(queries.BoxCastHits, Is.Zero);
                    Assert.That(queries.SphereCastHits, Is.Zero);
                    Assert.That(queries.CapsuleCastHits, Is.Zero);
                    Assert.That(queries.BoxOverlapHits, Is.Zero);
                    Assert.That(queries.SphereOverlapHits, Is.Zero);
                    Assert.That(queries.CapsuleOverlapHits, Is.Zero);
                    Assert.That(queries.RayFirstDistanceCm, Is.GreaterThan(0f));
                    Assert.That(float.IsFinite(queries.RayFirstDistanceCm), Is.True);
                });

                int allHitCount = queries.RayHits;
                int runSequence = runtime.ScannerRunSequence;
                ClickWhenPresent(engine, surfaceHost, "physics3d-scanner-result-closest");
                TickUntil(
                    engine,
                    () => runtime.ScannerResultMode == Physics3DShowcaseQueryResultMode.Closest,
                    maximumFrames: 128);
                ClickWhenPresent(engine, surfaceHost, "physics3d-scanner-run");
                TickUntil(engine, () => runtime.ScannerRunSequence > runSequence, maximumFrames: 128);
                Assert.That(runtime.GetQueryHitCount(0), Is.EqualTo(1));

                runSequence = runtime.ScannerRunSequence;
                ClickWhenPresent(engine, surfaceHost, "physics3d-scanner-result-any");
                TickUntil(
                    engine,
                    () => runtime.ScannerResultMode == Physics3DShowcaseQueryResultMode.Any,
                    maximumFrames: 128);
                ClickWhenPresent(engine, surfaceHost, "physics3d-scanner-run");
                TickUntil(engine, () => runtime.ScannerRunSequence > runSequence, maximumFrames: 128);
                Assert.Multiple(() =>
                {
                    Assert.That(runtime.ScannerAnyHit, Is.True);
                    Assert.That(runtime.GetQueryHitCount(0), Is.Zero, "Any must not invent a hit marker.");
                });

                ClickWhenPresent(engine, surfaceHost, "physics3d-scanner-result-all");
                TickUntil(
                    engine,
                    () => runtime.ScannerResultMode == Physics3DShowcaseQueryResultMode.All,
                    maximumFrames: 128);
                ClickWhenPresent(engine, surfaceHost, "physics3d-scanner-sensors");
                TickUntil(engine, () => !runtime.ScannerIncludeSensors, maximumFrames: 128);
                runSequence = runtime.ScannerRunSequence;
                ClickWhenPresent(engine, surfaceHost, "physics3d-scanner-run");
                TickUntil(engine, () => runtime.ScannerRunSequence > runSequence, maximumFrames: 128);
                Assert.That(runtime.GetQueryHitCount(0), Is.EqualTo(allHitCount - 1));

                ClickWhenPresent(engine, surfaceHost, "physics3d-scanner-sensors");
                TickUntil(engine, () => runtime.ScannerIncludeSensors, maximumFrames: 128);
                ClickWhenPresent(engine, surfaceHost, "physics3d-scanner-ignore-assembly");
                TickUntil(engine, () => !runtime.ScannerIgnoreAssembly, maximumFrames: 128);
                runSequence = runtime.ScannerRunSequence;
                ClickWhenPresent(engine, surfaceHost, "physics3d-scanner-run");
                TickUntil(engine, () => runtime.ScannerRunSequence > runSequence, maximumFrames: 128);
                Assert.That(runtime.GetQueryHitCount(0), Is.EqualTo(allHitCount + 1));
                break;
            }
            case Physics3DShowcaseScene.MaterialHill:
            {
                AssertMaterialHillPlayerLoop(engine, runtime, surfaceHost);
                break;
            }
            case Physics3DShowcaseScene.PlatformStation:
            {
                Character3DState initial = runtime.GetPlayerCharacterStateForTests();
                inputBackend.SetButton("<Keyboard>/w", pressed: true);
                for (int step = 0; step < 6; step++)
                {
                    Tick(engine);
                }

                inputBackend.SetButton("<Keyboard>/w", pressed: false);
                Character3DState moved = runtime.GetPlayerCharacterStateForTests();
                inputBackend.SetButton("<Keyboard>/space", pressed: true);
                Tick(engine);
                inputBackend.SetButton("<Keyboard>/space", pressed: false);
                Character3DState jumped = runtime.GetPlayerCharacterStateForTests();
                Assert.Multiple(() =>
                {
                    Assert.That(initial.IsGrounded, Is.True, "Platform Station must seat the player on the start deck.");
                    Assert.That(moved.PositionCm.X, Is.GreaterThan(initial.PositionCm.X + runtime.ActiveConfig.BodySizeCm));
                    Assert.That(jumped.LinearVelocityCmPerSecond.Y, Is.GreaterThan(0f));
                    Assert.That(runtime.KinematicBodyCount, Is.GreaterThan(0));
                });
                break;
            }
            case Physics3DShowcaseScene.WindTunnel:
            {
                Assert.That(runtime.WindTunnelFieldCount, Is.EqualTo(3));
                AdvanceObservedSteps(engine, runtime, minimumSteps: 90);
                Physics3DShowcasePanelState panel = runtime.CapturePanelState();
                Assert.That(panel.WindSummary, Does.Contain("Steady").And.Contain("FORWARD"));
                Assert.That(panel.WindSummary, Does.Contain("light").And.Contain("heavy"));
                Assert.That(
                    runtime.TryGetWindTunnelPairState(0, out _, out _, out float lightTravel, out float heavyTravel),
                    Is.True);
                Assert.That(lightTravel, Is.GreaterThan(heavyTravel));
                break;
            }
            case Physics3DShowcaseScene.TraversalCourse:
            {
                Character3DState initial = runtime.GetPlayerCharacterStateForTests();
                Physics3DCharacterTraversalShowcaseConfig config = runtime.ActiveConfig.CharacterTraversal;
                DriveTraversalCourseToLadder(engine, inputBackend, runtime, config);
                Character3DState atLadder = runtime.GetPlayerCharacterStateForTests();

                inputBackend.SetButton("<Keyboard>/e", pressed: true);
                Tick(engine);
                inputBackend.SetButton("<Keyboard>/e", pressed: false);
                Assert.That(runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.Attached));
                Tick(engine);
                Assert.That(runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.Climbing));

                inputBackend.SetButton("<Keyboard>/d", pressed: true);
                int climbSteps = 0;
                while (runtime.GetPlayerTraversalStatusForTests().State != Traversal3DState.LedgeHang &&
                       climbSteps < 120)
                {
                    Tick(engine);
                    climbSteps++;
                }

                inputBackend.SetButton("<Keyboard>/d", pressed: false);
                Assert.Multiple(() =>
                {
                    Assert.That(initial.IsGrounded, Is.True, "Traversal Course must begin with the player standing on the route.");
                    Assert.That(runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.LedgeHang));
                    Assert.That(runtime.GetPlayerCharacterStateForTests().PositionCm.Y, Is.GreaterThan(atLadder.PositionCm.Y));
                    Assert.That(runtime.BodyCount, Is.GreaterThan(1));
                });
                break;
            }
            case Physics3DShowcaseScene.WheelLab:
            {
                AssertWheelLabModeEvidence(runtime, Vehicle3DWheelKind.Physical, expectedAction: null);
                ClickWhenPresent(engine, surfaceHost, "physics3d-wheel-box");
                TickUntil(engine, () => runtime.WheelLabMode == Vehicle3DWheelKind.Box, maximumFrames: 128);
                WaitForObservedPhysicsStep(engine, runtime, simulation, scene);
                AssertWheelLabModeEvidence(runtime, Vehicle3DWheelKind.Box, "Box Wheels is ready at the shared start");
                ClickWhenPresent(engine, surfaceHost, "physics3d-wheel-physical");
                TickUntil(engine, () => runtime.WheelLabMode == Vehicle3DWheelKind.Physical, maximumFrames: 128);
                WaitForObservedPhysicsStep(engine, runtime, simulation, scene);
                AssertWheelLabModeEvidence(runtime, Vehicle3DWheelKind.Physical, "Physical Wheels is ready at the shared start");
                break;
            }
            case Physics3DShowcaseScene.RagdollLab:
            {
                RunRagdollLabPlayerRoute(engine, runtime, simulation, surfaceHost);
                break;
            }
            case Physics3DShowcaseScene.ConstraintForge:
                Assert.That(runtime.ConstraintCount, Is.GreaterThan(0));
                Assert.That(runtime.CapturePanelState().Constraints, Is.EqualTo(runtime.ConstraintCount));
                break;
            case Physics3DShowcaseScene.ReplayTheater:
                Assert.That(runtime.CapturePanelState().DeterminismComparisonSummary, Does.Contain("PASS"));
                break;
            case Physics3DShowcaseScene.ScaleCity:
            {
                Assert.That(runtime.DynamicBodyCount, Is.GreaterThan(0));
                Assert.That(runtime.BenchmarkPathCount, Is.GreaterThan(0));
                Assert.That(runtime.CapturePanelState().BenchmarkBodies, Is.EqualTo(runtime.DynamicBodyCount));
                Assert.That(
                    runtime.TryGetScaleCityInteractiveBodyState(0, out Physics3DBodyState beforePulse),
                    Is.True);
                ClickWhenPresent(engine, surfaceHost, "physics3d-action-impact");
                TickUntil(engine, () => runtime.ScaleCityState.PulseCount == 1, maximumFrames: 128);
                Assert.That(
                    runtime.TryGetScaleCityInteractiveBodyState(0, out Physics3DBodyState afterPulse),
                    Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(runtime.ScaleCityState.PulsedForegroundBodiesLastPulse,
                        Is.EqualTo(runtime.ScaleCityState.InteractiveBodies));
                    Assert.That(afterPulse.LinearVelocityCmPerSecond,
                        Is.Not.EqualTo(beforePulse.LinearVelocityCmPerSecond));
                    Assert.That(runtime.CapturePanelState().LastAction,
                        Does.Contain("City Pulse 1").And.Contain("background paths were untouched"));
                });

                ClickWhenPresent(engine, surfaceHost, "physics3d-action-reset");
                TickUntil(engine, () => runtime.ScaleCityState.PulseCount == 0, maximumFrames: 128);
                Assert.That(runtime.ScaleCityState.PulsedForegroundBodiesLastPulse, Is.Zero);
                break;
            }
            default:
                throw new InvalidOperationException($"Unhandled playground station '{scene}'.");
        }
    }

    private static Vector2 MoveTowardX(float currentX, float targetX)
    {
        float delta = targetX - currentX;
        if (MathF.Abs(delta) <= 10f)
        {
            return Vector2.Zero;
        }

        return delta > 0f ? Vector2.UnitX : -Vector2.UnitX;
    }

    private static void DriveTraversalCourseToLadder(
        GameEngine engine,
        AcceptanceInputBackend inputBackend,
        Physics3DShowcaseRuntime runtime,
        Physics3DCharacterTraversalShowcaseConfig config)
    {
        float attachReadyX = config.LadderCenterXCm - (config.AttachProbeDistanceCm * 0.9f);
        int approachSteps = 0;
        while (runtime.GetPlayerCharacterStateForTests().PositionCm.X < attachReadyX &&
               approachSteps < 300)
        {
            Character3DState character = runtime.GetPlayerCharacterStateForTests();
            bool jump = runtime.CharacterRouteCheckpointIndex == 2 && character.IsGrounded;
            SetCharacterKeyboardIntent(inputBackend, Vector2.UnitX, jump, traverse: false);
            Tick(engine);
            approachSteps++;
        }

        ClearCharacterKeyboardIntent(inputBackend);
        Assert.That(
            runtime.GetPlayerCharacterStateForTests().PositionCm.X,
            Is.GreaterThanOrEqualTo(attachReadyX),
            $"Public keyboard input did not reach the ladder within {approachSteps} fixed steps.");
    }

    private static void SetCharacterKeyboardIntent(
        AcceptanceInputBackend inputBackend,
        Vector2 move,
        bool jump,
        bool traverse)
    {
        inputBackend.SetButton("<Keyboard>/w", move.X > 0.5f);
        inputBackend.SetButton("<Keyboard>/s", move.X < -0.5f);
        inputBackend.SetButton("<Keyboard>/d", move.Y > 0.5f);
        inputBackend.SetButton("<Keyboard>/a", move.Y < -0.5f);
        inputBackend.SetButton("<Keyboard>/space", jump);
        inputBackend.SetButton("<Keyboard>/e", traverse);
    }

    private static void ClearCharacterKeyboardIntent(AcceptanceInputBackend inputBackend)
        => SetCharacterKeyboardIntent(inputBackend, Vector2.Zero, jump: false, traverse: false);

    private static void AssertWheelLabModeEvidence(
        Physics3DShowcaseRuntime runtime,
        Vehicle3DWheelKind expectedMode,
        string? expectedAction)
    {
        Physics3DShowcasePanelState panel = runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(runtime.WheelLabMode, Is.EqualTo(expectedMode));
            Assert.That(runtime.WheelLabVehicleCount, Is.EqualTo(1));
            Assert.That(runtime.WheelLabWheelCountValue, Is.EqualTo(4));
            Assert.That(runtime.WheelLabModeBodyCount, Is.EqualTo(4));
            Assert.That(panel.WheelSummary, Does.StartWith(
                expectedMode == Vehicle3DWheelKind.Physical ? "Physical Wheels" : "Box Wheels"));
            if (expectedAction != null)
            {
                Assert.That(panel.LastAction, Does.Contain(expectedAction));
            }
        });

        for (int wheelIndex = 0; wheelIndex < 4; wheelIndex++)
        {
            Assert.That(
                runtime.TryGetWheelLabDebugVisual(wheelIndex, out Physics3DWheelLabDebugVisual visual),
                Is.True);
            Assert.That(visual.Mode, Is.EqualTo(expectedMode));
            Assert.That(float.IsFinite(visual.CompressionCm), Is.True);
        }
    }

    private static void RunRagdollLabPlayerRoute(
        GameEngine engine,
        Physics3DShowcaseRuntime runtime,
        Physics3DSimulationSystem simulation,
        IUiSurfaceHost surfaceHost)
    {
        // Entry must finish Prepare -> simulation step -> Observe before any release or mode switch.
        Assert.That(runtime.SceneStep, Is.GreaterThan(0));
        Physics3DShowcasePanelState before = runtime.CapturePanelState();
        Assert.That(before.RagdollSummary, Does.Contain("ACTIVE POSE").And.Contain("bones"));

        int pendulumBodyIndex = FindRagdollPendulumBodyIndex(runtime);
        Assert.That(
            runtime.TryGetBodyVisual(
                pendulumBodyIndex,
                out Physics3DBodyState pendulumBefore,
                out _,
                out _,
                out _,
                out _,
                out _),
            Is.True);

        long stepBeforePendulum = runtime.SceneStep;
        ClickWhenPresent(engine, surfaceHost, "physics3d-ragdoll-pendulum");
        TickUntil(
            engine,
            () => runtime.CapturePanelState().LastAction.Contains("pendulum is swinging", StringComparison.Ordinal),
            maximumFrames: 128);
        WaitForObservedPhysicsStep(engine, runtime, simulation, Physics3DShowcaseScene.RagdollLab);
        Physics3DShowcasePanelState afterPendulum = runtime.CapturePanelState();
        Assert.That(
            runtime.TryGetBodyVisual(
                pendulumBodyIndex,
                out Physics3DBodyState pendulumAfter,
                out _,
                out _,
                out _,
                out _,
                out _),
            Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(runtime.SceneStep, Is.GreaterThan(stepBeforePendulum));
            Assert.That(afterPendulum.LastAction, Does.Contain("pendulum is swinging"));
            Assert.That(
                pendulumAfter.LinearVelocityCmPerSecond.X,
                Is.GreaterThan(pendulumBefore.LinearVelocityCmPerSecond.X + 1f),
                "Swing Pendulum must change the real pendulum body's velocity toward the mannequin.");
        });

        TickUntil(
            engine,
            () => runtime.RagdollLabState.Phase == Physics3DRagdollLabPhase.ImpactConfirmed,
            maximumFrames: 512);
        Assert.That(
            runtime.CapturePanelState().RagdollSummary,
            Does.Contain("IMPACT CONFIRMED"),
            "The public route must observe a real pendulum-to-bone contact before the mannequin tumbles.");

        long stepBeforePose = runtime.SceneStep;
        ClickWhenPresent(engine, surfaceHost, "physics3d-ragdoll-active-pose");
        TickUntil(
            engine,
            () => runtime.CapturePanelState().LastAction.Contains("Active pose released", StringComparison.Ordinal),
            maximumFrames: 128);
        WaitForObservedPhysicsStep(engine, runtime, simulation, Physics3DShowcaseScene.RagdollLab);
        Physics3DShowcasePanelState afterPose = runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(runtime.SceneStep, Is.GreaterThan(stepBeforePose));
            Assert.That(afterPose.RagdollSummary, Does.Contain("PASSIVE"));
            Assert.That(afterPose.LastAction, Does.Contain("Active pose released"));
        });

        int ragdollBoneCount = runtime.ActiveConfig.RagdollLab.Bones.Length;
        int dynamicBodiesBeforeRecovery = runtime.DynamicBodyCount;
        int kinematicBodiesBeforeRecovery = runtime.KinematicBodyCount;
        ClickWhenPresent(engine, surfaceHost, "physics3d-ragdoll-recover");
        Tick(engine);
        Physics3DShowcasePanelState rejectedRecovery = runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(runtime.RagdollLabState.IsRecovered, Is.False);
            Assert.That(runtime.DynamicBodyCount, Is.EqualTo(dynamicBodiesBeforeRecovery));
            Assert.That(runtime.KinematicBodyCount, Is.EqualTo(kinematicBodiesBeforeRecovery));
            Assert.That(
                rejectedRecovery.LastAction,
                Does.Contain("Recovery is not ready"),
                $"Try Recovery produced phase {runtime.RagdollLabState.Phase} with action '{rejectedRecovery.LastAction}'.");
        });

        TickUntil(
            engine,
            () => runtime.RagdollLabState.Phase == Physics3DRagdollLabPhase.Recoverable,
            maximumFrames: 2_048);
        Physics3DRagdollLabShowcaseState ready = runtime.RagdollLabState;
        Assert.Multiple(() =>
        {
            Assert.That(
                ready.StairStepsDescended,
                Is.GreaterThanOrEqualTo(runtime.ActiveConfig.RagdollLab.MinimumStairStepsDescended));
            Assert.That(ready.SettledTicks, Is.EqualTo(runtime.ActiveConfig.RagdollLab.RequiredSettledTicks));
            Assert.That(runtime.CapturePanelState().RagdollSummary, Does.Contain("RECOVERY READY"));
        });

        ClickWhenPresent(engine, surfaceHost, "physics3d-ragdoll-recover");
        TickUntil(
            engine,
            () => runtime.CapturePanelState().RagdollSummary.Contains("RECOVERED", StringComparison.Ordinal),
            maximumFrames: 128);
        Physics3DShowcasePanelState afterRecovery = runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(afterRecovery.RagdollSummary, Does.Contain("RECOVERED").And.Contain("clearance passed"));
            Assert.That(afterRecovery.LastAction, Does.Contain("Clearance passed"));
            Assert.That(runtime.DynamicBodyCount, Is.EqualTo(dynamicBodiesBeforeRecovery - ragdollBoneCount));
            Assert.That(runtime.KinematicBodyCount, Is.EqualTo(kinematicBodiesBeforeRecovery + ragdollBoneCount));
        });

        // Leave only after Observe has completed for the latest Ragdoll action.
        Assert.That(runtime.SceneStep, Is.GreaterThan(0));
        Assert.That(runtime.ActiveScene, Is.EqualTo(Physics3DShowcaseScene.RagdollLab));
    }

    private static void CaptureWheelLabPlayerEvidence(
        GameEngine engine,
        Physics3DShowcaseRuntime runtime,
        bool[] visitedSections,
        ref float maximumCameraErrorCm,
        ref int minimumGroundedWheelCount,
        ref bool sawGroundContact)
    {
        visitedSections[(int)runtime.WheelLabSection] = true;
        Physics3DBodyState chassis = runtime.GetWheelLabChassisState();
        Vector2? followTarget = engine.GameSession.Camera.FollowTargetPositionCm;
        if (!followTarget.HasValue)
        {
            throw new InvalidOperationException("Wheel Lab camera lost its chassis follow target during the public route.");
        }

        maximumCameraErrorCm = MathF.Max(
            maximumCameraErrorCm,
            Vector2.Distance(followTarget.Value, new Vector2(chassis.PositionCm.X, chassis.PositionCm.Z)));
        minimumGroundedWheelCount = Math.Min(minimumGroundedWheelCount, runtime.WheelLabGroundedWheelCount);
        for (int wheelIndex = 0; wheelIndex < 4; wheelIndex++)
        {
            if (!runtime.TryGetWheelLabDebugVisual(wheelIndex, out Physics3DWheelLabDebugVisual visual))
            {
                throw new InvalidOperationException($"Wheel Lab lost debug feedback for wheel {wheelIndex}.");
            }

            sawGroundContact |= visual.Grounded;
            if (visual.Grounded &&
                (!float.IsFinite(visual.ContactPointCm.X) ||
                 !float.IsFinite(visual.ContactPointCm.Y) ||
                 !float.IsFinite(visual.ContactPointCm.Z)))
            {
                throw new InvalidOperationException($"Wheel Lab wheel {wheelIndex} published a non-finite contact marker.");
            }
        }
    }

    private static int FindRagdollPendulumBodyIndex(Physics3DShowcaseRuntime runtime)
    {
        Physics3DRagdollLabShowcaseConfig config = runtime.ActiveConfig.RagdollLab;
        Vector3 authoredStart = new(
            config.PendulumAnchorXCm,
            config.PendulumAnchorYCm - config.PendulumRopeLengthCm,
            0f);
        int nearestIndex = -1;
        float nearestDistanceSquared = float.PositiveInfinity;
        for (int bodyIndex = 0; bodyIndex < runtime.BodyCount; bodyIndex++)
        {
            if (!runtime.TryGetBodyVisual(
                    bodyIndex,
                    out Physics3DBodyState state,
                    out Physics3DBodyKind bodyKind,
                    out Physics3DShapeKind shapeKind,
                    out _,
                    out _,
                    out _) ||
                bodyKind != Physics3DBodyKind.Dynamic ||
                shapeKind != Physics3DShapeKind.Sphere)
            {
                continue;
            }

            float distanceSquared = Vector3.DistanceSquared(state.PositionCm, authoredStart);
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestIndex = bodyIndex;
                nearestDistanceSquared = distanceSquared;
            }
        }

        Assert.That(nearestIndex, Is.GreaterThanOrEqualTo(0), "Ragdoll Lab pendulum body is missing.");
        Assert.That(
            nearestDistanceSquared,
            Is.LessThan(runtime.ActiveConfig.BodySizeCm * runtime.ActiveConfig.BodySizeCm),
            "Ragdoll Lab has no dynamic sphere at the authored pendulum start.");
        return nearestIndex;
    }

    private static void RunUntilDeterministicRebuildCompletes(
        GameEngine engine,
        Physics3DShowcaseRuntime runtime,
        IUiSurfaceHost surfaceHost)
    {
        int phaseFrames = (runtime.ActiveConfig.ReplaySteps + 8) * 16;

        // Given the rebuilt station is ready, when the player asks for an authored difference.
        TickUntil(
            engine,
            () => runtime.ReplayStatus == Physics3DShowcaseReplayStatus.ReadyToReplay,
            maximumFrames: phaseFrames);
        ClickWhenPresent(engine, surfaceHost, "physics3d-replay-inject-difference");
        TickUntil(
            engine,
            () => runtime.ReplayStatus == Physics3DShowcaseReplayStatus.Failed,
            maximumFrames: phaseFrames);

        // Then it stops on the configured first mismatch and exposes expected and actual hashes.
        Physics3DShowcasePanelState failed = runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(runtime.ReplayCursor + 1, Is.EqualTo(runtime.ActiveConfig.ReplayDifferenceStep));
            Assert.That(runtime.ReplayDifferenceInjected, Is.True);
            Assert.That(runtime.ReplayExpectedHash, Is.Not.EqualTo(runtime.ReplayActualHash));
            Assert.That(failed.DeterminismComparisonSummary,
                Does.Contain($"FAIL at step {runtime.ActiveConfig.ReplayDifferenceStep}").And.Contain("expected").And.Contain("actual"));
        });

        // When the player resets and chooses the clean path, then the rebuilt run passes.
        ClickWhenPresent(engine, surfaceHost, "physics3d-action-reset");
        TickUntil(
            engine,
            () => runtime.ReplayStatus == Physics3DShowcaseReplayStatus.ReadyToReplay,
            maximumFrames: phaseFrames);
        ClickWhenPresent(engine, surfaceHost, "physics3d-replay-start");
        TickUntil(
            engine,
            () => runtime.ReplayStatus == Physics3DShowcaseReplayStatus.Passed,
            maximumFrames: phaseFrames);
        Assert.That(runtime.ReplayDifferenceInjected, Is.False);
    }

    private static void ResumeIfPaused(
        GameEngine engine,
        IUiSurfaceHost surfaceHost,
        Physics3DSimulationSystem simulation)
    {
        if (simulation.Enabled)
        {
            return;
        }

        Click(surfaceHost, "physics3d-action-pause");
        TickUntil(engine, () => simulation.Enabled, maximumFrames: 128);
        Assert.That(simulation.Enabled, Is.True);
    }

    private static void ClickWhenPresent(GameEngine engine, IUiSurfaceHost surfaceHost, string elementId)
    {
        TickUntil(
            engine,
            () => surfaceHost.Scene?.FindByElementId(elementId) != null,
            maximumFrames: 128);
        Click(surfaceHost, elementId);
    }

    private static void Click(IUiSurfaceHost surfaceHost, string elementId)
    {
        UiScene scene = surfaceHost.Scene
            ?? throw new InvalidOperationException("Physics3D showcase UI scene is not mounted.");
        UiNode node = scene.FindByElementId(elementId)
            ?? throw new InvalidOperationException($"Physics3D showcase UI element '{elementId}' is missing.");
        UiEventResult result = scene.Dispatch(new UiPointerEvent(
            UiPointerEventType.Click,
            PointerId: 1,
            X: node.LayoutRect.X + (node.LayoutRect.Width * 0.5f),
            Y: node.LayoutRect.Y + (node.LayoutRect.Height * 0.5f),
            TargetNodeId: node.Id));
        Assert.That(result.Handled, Is.True, $"UI element '{elementId}' did not handle its click.");
    }

    private static void Tick(GameEngine engine)
    {
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        engine.Tick(1f / FixedHz);
    }

    private static void TickUntil(
        GameEngine engine,
        Func<bool> condition,
        int maximumFrames)
    {
        for (int frame = 0; frame < maximumFrames; frame++)
        {
            if (condition())
            {
                return;
            }

            Tick(engine);
        }

        Assert.That(condition(), Is.True, $"Condition did not become true within {maximumFrames} rendered frames.");
    }

    private static double TickUntilNextPhysicsStep(GameEngine engine, Physics3DSimulationSystem simulation)
    {
        long physicsStepsBefore = simulation.TotalPhysicsSteps;
        long timestamp = Stopwatch.GetTimestamp();
        for (int frame = 0; frame < 128; frame++)
        {
            Tick(engine);
            if (simulation.TotalPhysicsSteps <= physicsStepsBefore)
            {
                continue;
            }

            Assert.That(simulation.TotalPhysicsSteps, Is.EqualTo(physicsStepsBefore + 1));
            Assert.That(simulation.PhysicsStepsLastUpdate, Is.EqualTo(1));
            return Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
        }

        Assert.Fail("Physics3D did not complete the next authoritative step within 128 rendered frames.");
        return double.NaN;
    }

    private static string SceneButtonId(Physics3DShowcaseScene scene) =>
        $"physics3d-scene-{scene.ToString().ToLowerInvariant()}";

    private static double Percentile(double[] samples, double percentile)
    {
        if (samples.Length == 0)
        {
            throw new ArgumentException("At least one sample is required.", nameof(samples));
        }

        if (!(percentile > 0d && percentile <= 1d))
        {
            throw new ArgumentOutOfRangeException(nameof(percentile));
        }

        double[] sorted = (double[])samples.Clone();
        Array.Sort(sorted);
        int index = Math.Clamp((int)Math.Ceiling(sorted.Length * percentile) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }

    private static void WriteAcceptanceArtifacts(
        string repoRoot,
        IReadOnlyList<string> trace,
        double physicsP50,
        double physicsP95,
        double physicsP99,
        double endToEndP95,
        double twentyFiveThousandP95,
        double fiftyThousandP95,
        int tenThousandPeakContactPairs,
        int tenThousandSteadyContactPairs,
        int twentyFiveThousandPeakContactPairs,
        int twentyFiveThousandSteadyContactPairs,
        int fiftyThousandPeakContactPairs,
        int fiftyThousandSteadyContactPairs,
        int visibleBodyLimit)
    {
        string artifactDirectory = Path.Combine(repoRoot, "artifacts", "acceptance", "physics3d-showcase");
        Directory.CreateDirectory(artifactDirectory);
        File.WriteAllText(
            Path.Combine(artifactDirectory, "trace.jsonl"),
            string.Join(Environment.NewLine, trace) + Environment.NewLine,
            Utf8NoBom);
        File.WriteAllText(
            Path.Combine(artifactDirectory, "path.mmd"),
            BuildPathDiagram(),
            Utf8NoBom);
        File.WriteAllText(
            Path.Combine(artifactDirectory, "battle-report.md"),
            BuildBattleReport(
                physicsP50,
                physicsP95,
                physicsP99,
                endToEndP95,
                twentyFiveThousandP95,
                fiftyThousandP95,
                tenThousandPeakContactPairs,
                tenThousandSteadyContactPairs,
                twentyFiveThousandPeakContactPairs,
                twentyFiveThousandSteadyContactPairs,
                fiftyThousandPeakContactPairs,
                fiftyThousandSteadyContactPairs,
                visibleBodyLimit),
            Utf8NoBom);
    }

    private static string BuildBattleReport(
        double physicsP50,
        double physicsP95,
        double physicsP99,
        double endToEndP95,
        double twentyFiveThousandP95,
        double fiftyThousandP95,
        int tenThousandPeakContactPairs,
        int tenThousandSteadyContactPairs,
        int twentyFiveThousandPeakContactPairs,
        int twentyFiveThousandSteadyContactPairs,
        int fiftyThousandPeakContactPairs,
        int fiftyThousandSteadyContactPairs,
        int visibleBodyLimit)
    {
        var report = new StringBuilder();
        report.AppendLine("# Physics3D Playground 验收战报");
        report.AppendLine();
        report.AppendLine("## 1. 概述");
        report.AppendLine();
        report.AppendLine("玩家从正式 Raylib 启动预设进入 Physics3D Playground，默认落在 Scanner Range，并看到十个站点按钮和完整控制面板。主循环与物理世界均为 30Hz，每次主循环只推进一次权威物理步。十个站点、暂停、单步和 Scale City 的 1K 至 50K 档位均通过真实界面按钮操作。");
        report.AppendLine();
        report.AppendLine("## 2. 结构");
        report.AppendLine();
        report.AppendLine("- 启动入口：`capability_standard_physics3d_showcase_raylib`");
        report.AppendLine("- 默认地图：`capability_standard_physics3d_showcase`");
        report.AppendLine("- 初始站点：Scanner Range");
        report.AppendLine("- 站点顺序：Scanner Range → Material Hill → Platform Station → Wind Tunnel → Traversal Course → Wheel Lab → Ragdoll Lab → Constraint Forge → Deterministic Rebuild Lab → Scale City");
        report.AppendLine("- 站点数量：10");
        report.AppendLine("- Scale City 压力档位：1K / 10K / 25K / 50K");
        report.AppendLine($"- 50K 时权威刚体：50,000；表现层采样上限：{visibleBodyLimit:N0}");
        report.AppendLine();
        report.AppendLine("## 3. 详情");
        report.AppendLine();
        report.AppendLine($"- 10K 物理步 P50：{physicsP50:0.###} ms");
        report.AppendLine($"- 10K 物理步 P95：{physicsP95:0.###} ms");
        report.AppendLine($"- 10K 物理步 P99：{physicsP99:0.###} ms");
        report.AppendLine($"- 10K 完整引擎 Tick P95：{endToEndP95:0.###} ms");
        report.AppendLine($"- 25K 物理步 P95：{twentyFiveThousandP95:0.###} ms");
        report.AppendLine($"- 50K 物理步 P95：{fiftyThousandP95:0.###} ms");
        report.AppendLine($"- 10K 接触对：峰值 {tenThousandPeakContactPairs:N0}；稳态 {tenThousandSteadyContactPairs:N0}");
        report.AppendLine($"- 25K 接触对：峰值 {twentyFiveThousandPeakContactPairs:N0}；稳态 {twentyFiveThousandSteadyContactPairs:N0}");
        report.AppendLine($"- 50K 接触对：峰值 {fiftyThousandPeakContactPairs:N0}；稳态 {fiftyThousandSteadyContactPairs:N0}");
        report.AppendLine($"- 30Hz 单步预算：{FixedStepBudgetMilliseconds:0.###} ms");
        report.AppendLine($"- 10K 判定：{(physicsP95 <= FixedStepBudgetMilliseconds ? "实时" : "超出30帧预算")}");
        report.AppendLine($"- 25K 判定：{(twentyFiveThousandP95 <= FixedStepBudgetMilliseconds ? "实时" : "超出30帧预算")}");
        report.AppendLine($"- 50K 判定：{(fiftyThousandP95 <= FixedStepBudgetMilliseconds ? "实时" : "超出30帧预算")}");
        report.AppendLine();
        report.AppendLine("## 4. 场景");
        report.AppendLine();
        report.AppendLine("- Scanner Range：玩家播放射线和三种体积扫描，暂停后可按 30Hz 单步观察；命中按 1..N 出现在世界中，红色交叉编号表示扫描从目标内部开始；三种重叠查询留在原点脉冲。");
        report.AppendLine("- Material Hill：玩家点击推箱，比较冰/木/橡胶坡上的滑动距离。");
        report.AppendLine("- Platform Station：玩家依次通过移动台、转台、传送带和单向终点台；面板持续显示下一目标、剩余时间和完成或失败结果。");
        report.AppendLine("- Wind Tunnel：玩家比较稳风、阵风、涡旋下轻重物体的位移。");
        report.AppendLine("- Traversal Course：玩家沿斜坡、台阶和移动台抵达梯子，再完成梯子与墙面的两次翻越并站上顶层。");
        report.AppendLine("- Wheel Lab：玩家切换实体轮与 Box Wheel，四个轮位和真实轮组刚体随选择重建。");
        report.AppendLine("- Ragdoll Lab：玩家启动摆锤、释放主动姿态并请求恢复；落脚空间通过后，人偶交回站立动画。");
        report.AppendLine("- Constraint Forge：玩家看到链条、门、滑轨等约束在运动。");
        report.AppendLine("- Deterministic Rebuild Lab：玩家启动核对后，脚本基线与重建运行的刚体状态逐步一致并通过；它不回放玩家输入，也不回滚运行中的世界。");
        report.AppendLine("- Scale City：玩家切换 1K/10K/25K/50K；前景刚体真实堆叠、碰撞并受风，背景稀疏流沿独立轨道循环。");
        report.AppendLine();
        report.AppendLine("## 5. 边界");
        report.AppendLine();
        report.AppendLine("- 本次 50K 结果验证的是单服务器权威 3D 刚体世界，不等价于已经验证 150 名玩家的网络收发、兴趣管理或状态同步成本。");
        report.AppendLine("- 表现层有固定采样上限；压力数字来自权威物理世界，不以画面中绘制数量冒充模拟数量。");
        report.AppendLine("- 固定步只支持当前明确配置的 30Hz 一对一推进；20Hz 主循环不在支持范围内。");
        report.AppendLine("- Ragdoll Lab 离开前必须完成一次完整的 Prepare → 物理步 → Observe；绕过生命周期守卫会直接失败。");
        report.AppendLine("- 站点按钮、冲量提交和重发证据均来自真实运行时暴露结果；缺失即失败，不做静默降级。");
        report.AppendLine();
        report.AppendLine("## 6. UAT");
        report.AppendLine();
        report.AppendLine("```gherkin");
        report.AppendLine("# language: zh-CN");
        report.AppendLine("功能: 新玩家在 Physics3D Playground 里逛完十个站点并看懂结果");
        report.AppendLine();
        report.AppendLine("  场景: 第一次进入就能从 Scanner Range 浏览全部十个站点");
        report.AppendLine("    假如 玩家从 Physics3D Playground 的 Raylib 正式入口启动游戏");
        report.AppendLine("    当 玩家进入默认地图");
        report.AppendLine("    那么 玩家首先看到 Scanner Range、30 Hz 状态和十个站点按钮");
        report.AppendLine("    并且 玩家依次点击 Scanner Range、Material Hill、Platform Station、Wind Tunnel、Traversal Course、Wheel Lab、Ragdoll Lab、Constraint Forge、Deterministic Rebuild Lab、Scale City");
        report.AppendLine("    那么 每次点击都切换到有可见物理内容的新站点");
        report.AppendLine("    并且 若某站点按钮缺失或切换失败，验收直接报错而不是跳过");
        report.AppendLine();
        report.AppendLine("  场景: 玩家暂停并单步观察胶囊扫描的命中顺序");
        report.AppendLine("    假如 玩家在 Scanner Range 暂停世界，并选择 Capsule Cast、最远距离和 All targets");
        report.AppendLine("    当 玩家点击 Play Scan");
        report.AppendLine("    那么 扫描头停在起点，红色交叉的 #1 明确表示胶囊从目标内部开始");
        report.AppendLine("    当 玩家点击一次 Single Step");
        report.AppendLine("    那么 扫描只前进一个 30Hz 固定帧，并且世界继续保持暂停");
        report.AppendLine("    当 玩家恢复播放直到扫描结束");
        report.AppendLine("    那么 全部命中按距离以 #1 到 #N 依次出现在世界和面板中");
        report.AppendLine("    当 玩家点击 Reset Station");
        report.AppendLine("    那么 播放游标、命中编号、结果和扫描选择一起回到初始状态");
        report.AppendLine();
        report.AppendLine("  场景: 玩家在 Material Hill 比较三种地面");
        report.AppendLine("    假如 玩家进入 Material Hill 并看到三道坡和三只相同箱子");
        report.AppendLine("    当 玩家点击 Push Crates");
        report.AppendLine("    那么 面板显示 Ice、Wood、Rubber 各自滑过的距离");
        report.AppendLine("    并且 冰面箱子比橡胶面箱子滑得更远");
        report.AppendLine();
        report.AppendLine("  场景: 玩家在 Platform Station 完成四段平台路线");
        report.AppendLine("    假如 玩家进入 Platform Station 并站在起始平台上");
        report.AppendLine("    当 玩家依次落到移动台、转台、传送带和单向终点台");
        report.AppendLine("    那么 面板逐段更新已完成数量和下一目标");
        report.AppendLine("    并且 到达终点后明确显示路线完成");
        report.AppendLine("    当 玩家偏离路线或耗尽时间后点击 Restart Route");
        report.AppendLine("    那么 失败原因可见，并且角色、计时和进度一起回到起点");
        report.AppendLine();
        report.AppendLine("  场景: 玩家从 Traversal Course 起点翻越梯子和高墙");
        report.AppendLine("    假如 玩家进入 Traversal Course 并站在路线起点");
        report.AppendLine("    当 玩家越过斜坡、台阶和移动台，在梯子前按 E 后向上攀爬");
        report.AppendLine("    那么 面板记录梯子翻越并提示前往高墙");
        report.AppendLine("    当 玩家跳到高墙、再次抓附并翻上顶层");
        report.AppendLine("    那么 角色在顶层站稳，面板明确显示全部六段完成");
        report.AppendLine("    并且 两个角色站内相机始终跟随角色，离站后恢复全局视角");
        report.AppendLine();
        report.AppendLine("  场景: 玩家比较方盒轮和实体轮");
        report.AppendLine("    假如 玩家进入 Wheel Lab 并看到装有四个实体轮位的车辆");
        report.AppendLine("    当 玩家点击 Box Wheels");
        report.AppendLine("    那么 同一辆车保留四个轮位，并用八个方盒轮组刚体继续模拟");
        report.AppendLine("    当 玩家点击 Physical Wheels");
        report.AppendLine("    那么 同一辆车再次保留四个轮位，并用八个实体轮组刚体继续模拟");
        report.AppendLine();
        report.AppendLine("  场景: 玩家在 Ragdoll Lab 操作人偶后再离开");
        report.AppendLine("    假如 玩家进入 Ragdoll Lab 并看到楼梯上的人偶");
        report.AppendLine("    当 玩家点击 Swing Pendulum");
        report.AppendLine("    那么 摆锤真实刚体获得朝向人偶的速度");
        report.AppendLine("    并且 玩家点击 Toggle Active Pose");
        report.AppendLine("    那么 面板从主动姿态变为被动姿态");
        report.AppendLine("    当 玩家点击 Recover");
        report.AppendLine("    那么 落脚空间检查通过，人偶骨骼从动态模拟交回站立动画");
        report.AppendLine("    并且 世界至少完成一次物理步观察后，玩家才能切换到下一站点");
        report.AppendLine();
        report.AppendLine("  场景: 玩家暂停世界并只前进一步");
        report.AppendLine("    假如 物理世界正在以 30 Hz 运行");
        report.AppendLine("    当 玩家点击 Pause 后再点击 Single Step");
        report.AppendLine("    那么 暂停期间世界不前进");
        report.AppendLine("    并且 Single Step 恰好推进一个权威物理步且仍保持暂停");
        report.AppendLine();
        report.AppendLine("  场景: 玩家查看持续运动的 Scale City");
        report.AppendLine("    假如 玩家选择 Scale City 站点");
        report.AppendLine("    当 玩家依次点击 1K、10K、25K 和 50K");
        report.AppendLine("    那么 权威物理世界在每个档位都保有准确的动态刚体数量");
        report.AppendLine("    并且 前景刚体持续堆叠、碰撞、受风并分波重发，背景稀疏流沿独立轨道循环");
        report.AppendLine($"    并且 画面只抽样最多 {visibleBodyLimit} 个刚体但不会减少权威模拟数量");
        report.AppendLine("    当 玩家分别观察 10K、25K 和 50K 的连续帧");
        report.AppendLine($"    那么 每个档位连续测量的物理步 P95 都不超过 {FixedStepBudgetMilliseconds:0.###} 毫秒");
        report.AppendLine("    并且 前景产生真实接触，而每个接触对都不包含背景稀疏流刚体");
        report.AppendLine();
        report.AppendLine("  场景: 玩家离开游乐园后界面不残留");
        report.AppendLine("    假如 Physics3D Playground 面板正在显示");
        report.AppendLine("    当 玩家离开当前地图");
        report.AppendLine("    那么 面板租约被释放且游乐园面板从界面树移除");
        report.AppendLine("```");
        return report.ToString();
    }

    private static string BuildPathDiagram() =>
        "flowchart LR\n" +
        "    A[\"从 Raylib 正式预设启动\"] --> B[\"进入默认 Scanner Range\"]\n" +
        "    B --> C[\"依次点击十个站点按钮\"]\n" +
        "    C --> D[\"暂停并单步观察\"]\n" +
        "    D --> E[\"确定性重建核对通过\"]\n" +
        "    E --> F[\"Scale City 依次选择 1K / 10K / 25K / 50K\"]\n" +
        "    F --> G[\"采集 30Hz 物理预算\"]\n" +
        "    G --> H[\"离开地图并释放面板\"]\n";

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

    private sealed class AcceptanceInputBackend : IInputBackend
    {
        private readonly HashSet<string> _pressedButtons = new(StringComparer.Ordinal);

        public void SetButton(string devicePath, bool pressed)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
            if (pressed)
            {
                _pressedButtons.Add(devicePath);
            }
            else
            {
                _pressedButtons.Remove(devicePath);
            }
        }

        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => _pressedButtons.Contains(devicePath);
        public Vector2 GetMousePosition() => Vector2.Zero;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }
}
