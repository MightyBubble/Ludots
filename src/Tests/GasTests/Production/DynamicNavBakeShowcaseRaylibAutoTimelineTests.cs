using System;
using System.Numerics;
using Arch.Core;
using DynamicNavBakeShowcaseMod;
using DynamicNavBakeShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.MassNavigation;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Scripting;
using Ludots.UI;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[NonParallelizable]
[Category("acceptance")]
public sealed class DynamicNavBakeShowcaseRaylibAutoTimelineTests
{
    private string? _previousAutoTimelineEnv;

    [SetUp]
    public void SetUp()
    {
        _previousAutoTimelineEnv = Environment.GetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey);
        Environment.SetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey, null);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey, _previousAutoTimelineEnv);
    }

    // Feature: a manual player can operate the showcase as soon as the scene becomes ready
    // Given auto play is disabled and the RTS scene is still draining its authored spawn queue
    // When the first ordinary host frames make the scene playable
    // Then the visible command deck appears without requiring a scripted action or hidden shortcut
    [Test]
    public void Feature_ManualPlayer_FirstPlayableFrames_MountVisibleCommandDeckWithoutAutoTimeline()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeDynamicRtsShowcaseMod",
            registerRecast: false,
            installUi: true);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        _ = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.RtsMapId);
        UIRoot uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("Manual Dynamic NavBake acceptance requires UIRoot.");

        for (int frame = 0;
             frame < 120 && uiRoot.Scene?.FindByElementId(DynamicNavBakeShowcaseIds.PanelElementId) == null;
             frame++)
        {
            engine.Tick(DynamicNavBakeShowcaseAcceptanceHarness.DeltaTime);
        }

        Assert.That(
            uiRoot.Scene?.FindByElementId(DynamicNavBakeShowcaseIds.PanelElementId),
            Is.Not.Null,
            "A manual player must see the command deck after the authored spawn queue drains; auto timeline actions are not a UI bootstrap mechanism.");
    }

    // Feature: Raylib auto player refuses invalid algorithm names
    // Given a Dynamic NavBake scene with the auto timeline environment variable set to garbage
    // When the next presentation host frame runs
    // Then the player sees an immediate hard failure instead of a silent skip
    [Test]
    public void Feature_AutoTimeline_InvalidAlgorithmEnv_ThrowsImmediately()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeDynamicRtsShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        Environment.SetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey, "not-an-algorithm");
        engine.SetService(CoreServiceKeys.HostFrameIndex, 0);

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => engine.Tick(DynamicNavBakeShowcaseAcceptanceHarness.DeltaTime));
        Assert.That(ex!.Message, Does.Contain(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey));
        Assert.That(ex.Message, Does.Contain("recast").IgnoreCase);
    }

    // Feature: Raylib auto player requires the host frame clock
    // Given auto timeline is enabled with a valid algorithm
    // When HostFrameIndex was never published by the host
    // Then the run fails instead of inventing a default frame
    [Test]
    public void Feature_AutoTimeline_MissingHostFrameIndex_ThrowsImmediately()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeDynamicRtsShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        Environment.SetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey, NavBakeNames.AlgorithmCdt);
        engine.RemoveService(CoreServiceKeys.HostFrameIndex);

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => engine.Tick(DynamicNavBakeShowcaseAcceptanceHarness.DeltaTime));
        Assert.That(ex!.Message, Does.Contain("HostFrameIndex"));
        Assert.That(ex.Message, Does.Contain(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey));
    }

    // Feature: Non-blocking deploy does not freeze the host frame
    // Given a player deploys the squad through the public non-blocking action
    // When the path needs a resident window rebuild
    // Then the action returns on the same frame without DrainUntilIdle / recursive Tick
    [Test]
    public void Feature_TryDeploySquadNonBlocking_ReturnsWithoutRecursiveDrain()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeOpenWorld64x64ShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)!;

        double fixedTimeBeforeDeploy = Time.FixedTotalTime;
        Assert.That(actions.TryDeploySquadNonBlocking(engine, out string error), Is.True, error);
        Assert.That(
            Time.FixedTotalTime,
            Is.EqualTo(fixedTimeBeforeDeploy),
            "Non-blocking deploy must not recursively advance engine.FixedStep through DrainUntilIdle.");
        Assert.That(actions.SquadDeployed, Is.True);

        // Sync deploy would drain nested FixedSteps here. Non-blocking must leave work for later frames
        // whenever a resident transition was scheduled.
        bool stillRebuilding =
            actions.PathOrchestrationState == DynamicNavBakePathOrchestrationState.WindowRebuilding
            || queue.Status != RuntimeNavMeshRebuildStatus.Idle
            || queue.HasResidentWindowTransition
            || queue.PendingTileCount > 0;
        Assert.That(
            stillRebuilding || actions.PathOrchestrationState == DynamicNavBakePathOrchestrationState.LocalSegmentReady,
            Is.True,
            "Non-blocking deploy must either schedule a resident rebuild or already have a ready local segment.");

        if (stillRebuilding)
        {
            Assert.That(
                queue.Status == RuntimeNavMeshRebuildStatus.Idle
                && !queue.HasResidentWindowTransition
                && queue.PendingTileCount == 0,
                Is.False,
                "Non-blocking deploy must not DrainUntilIdle to a fully idle queue on the same call.");
        }

        Assert.That(actions.TryDeploySquad(engine, out string syncError), Is.False, "Sync redeploy must still reject an already-deployed squad.");
        Assert.That(syncError, Does.Contain("already deployed").IgnoreCase);
    }

    // Feature: RTS auto player closes then opens the fort on the authored frame clock
    // Given a Raylib host frame clock and cdt selected by env
    // When the player watches through initial/dynamic/final screenshot frames
    // Then the squad is moving on the selected bake, the sealed gate reshapes the route, and demolition leads to real goal-slot arrival
    [Test]
    public void Feature_RtsAutoTimeline_PlayerSeesAlgorithmDeployGateAndRestoredRoute()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeDynamicRtsShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        Environment.SetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey, NavBakeNames.AlgorithmCdt);
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline = actions.ActiveConfig.RaylibAutoTimeline;
        Assert.That(
            timeline.ResolvedFinalCaptureCompletionMode,
            Is.EqualTo(DynamicNavBakeShowcaseFinalCaptureCompletionMode.Arrival));
        RunHostFramesThrough(engine, actions, timeline.FinalScreenshotFrame);

        RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)!;
        Assert.That(queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.Cdt));
        Assert.That(queue.HasRequestedAlgorithm, Is.False);
        Assert.That(actions.SquadDeployed, Is.True);
        Assert.That(actions.WallDeployedCount, Is.EqualTo(0), "Final RTS beat demolishes the gate.");
        Assert.That(actions.FormalMoveCommandSubmitCount, Is.EqualTo(3));
        AssertSquadArrivedAtAuthoredGoalSlots(actions, engine);
        Assert.That(CountActiveSquadMoveOrders(engine), Is.EqualTo(0), "Arrival-mode final beat must clear active move orders.");
        AssertNavStable(actions.ActiveConfig, engine);
    }

    // Feature: Open-world auto player seals then opens the visible battle
    // Given a Raylib host frame clock and layered-span selected by env
    // When the player watches through the three screenshot beats
    // Then the initial corridor moves, the sealed gate reshapes the formal route, and demolition restores a ready marching path
    // And the global corridor plus local 64-tile resident window remain visible evidence
    // And final completion stays route-ready (continuous 64x64-chunk corridor march)
    [Test]
    public void Feature_OpenWorldAutoTimeline_PlayerSeesCorridorGateAndRestoredRoute()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeOpenWorld64x64ShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        Environment.SetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey, NavBakeNames.AlgorithmLayeredSpan);
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline = actions.ActiveConfig.RaylibAutoTimeline;
        Assert.That(
            timeline.ResolvedFinalCaptureCompletionMode,
            Is.EqualTo(DynamicNavBakeShowcaseFinalCaptureCompletionMode.RouteReady));
        RunHostFramesThrough(engine, actions, timeline.FinalScreenshotFrame);

        RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)!;
        Assert.That(queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.LayeredSpan));
        Assert.That(queue.HasRequestedAlgorithm, Is.False);
        Assert.That(actions.SquadDeployed, Is.True);
        Assert.That(actions.WallDeployedCount, Is.EqualTo(0), "Final open-world beat demolishes the gate.");
        Assert.That(actions.PathOrchestrationState, Is.EqualTo(DynamicNavBakePathOrchestrationState.LocalSegmentReady));
        Assert.That(actions.LastPathStatus, Is.EqualTo(NavPathStatus.Ok));
        Assert.That(actions.LastPathPointCount, Is.GreaterThan(1));
        Assert.That(actions.LastCoarseCorridorNodeCount, Is.GreaterThan(2));
        Assert.That(queue.CommittedResidentWindowCount, Is.EqualTo(64));
        Assert.That(queue.ResidentWindowCount, Is.EqualTo(64));
        Assert.That(queue.HasResidentWindowTransition, Is.False);
        AssertNavStable(actions.ActiveConfig, engine);
    }

    // Feature: generation fence blocks stale move after a wall on host frames without FixedStep
    // Given the auto player seals the RTS gate on the authored dynamic action frame
    // When many presentation host frames run without advancing FixedStep
    // Then no new formal move is submitted against the old idle generation
    // And only after delayed FixedSteps commit a newer generation does the side-route move appear
    [Test]
    public void Feature_AutoTimeline_GenerationFence_BlocksMoveUntilNewerCommittedGeneration()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeDynamicRtsShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        Environment.SetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey, NavBakeNames.AlgorithmCdt);
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline = actions.ActiveConfig.RaylibAutoTimeline;
        Assert.That(timeline.RequiredQuiescentFixedTicks, Is.GreaterThanOrEqualTo(2));

        RunHostFramesThrough(engine, actions, timeline.DynamicActionFrame);
        Assert.That(actions.WallDeployedCount, Is.EqualTo(actions.ActiveConfig.Gate.SegmentCount));
        ulong generationAtWall = actions.CaptureEvidence(engine).LastGeneration;
        int moveOrdersAtWall = CountActiveSquadMoveOrders(engine);

        double fixedBeforeHostOnly = Time.FixedTotalTime;
        for (int i = 1; i <= 24; i++)
        {
            engine.SetService(CoreServiceKeys.HostFrameIndex, timeline.DynamicActionFrame + i);
            // Tiny dt must not accumulate a FixedStep; presentation/auto timeline still runs.
            engine.Tick(1e-9f);
        }

        Assert.That(Time.FixedTotalTime, Is.EqualTo(fixedBeforeHostOnly), "Host-only frames must not advance FixedTotalTime.");
        Assert.That(
            CountActiveSquadMoveOrders(engine),
            Is.EqualTo(moveOrdersAtWall),
            "Generation fence must not issue a new move while FixedStep has not committed a newer generation.");
        Assert.That(
            actions.CaptureEvidence(engine).LastGeneration,
            Is.EqualTo(generationAtWall),
            "Without FixedStep dirty capture/commit, committed generation must stay at the pre-rebuild baseline.");

        float fixedDt = Time.FixedDeltaTime;
        Assert.That(fixedDt, Is.GreaterThan(0f));
        bool sawNewerGeneration = false;
        bool sawPostFenceMove = false;
        int frame = timeline.DynamicActionFrame + 25;
        for (int i = 0; i < 512; i++, frame++)
        {
            engine.SetService(CoreServiceKeys.HostFrameIndex, frame);
            engine.Tick(fixedDt);
            DynamicNavBakeShowcaseEvidence evidence = actions.CaptureEvidence(engine);
            if (evidence.LastGeneration > generationAtWall)
            {
                sawNewerGeneration = true;
            }

            if (sawNewerGeneration &&
                evidence.FormalRouteAgentCount == actions.ActiveConfig.Squad.Count &&
                evidence.FormalRouteDomain == PathDomain.NavMesh &&
                evidence.FormalRouteMinWaypointCount > 0 &&
                actions.LastPathStatus == NavPathStatus.Ok &&
                actions.LastPathPointCount > 1)
            {
                sawPostFenceMove = true;
                break;
            }

            if (frame >= timeline.DynamicScreenshotFrame)
            {
                break;
            }
        }

        Assert.That(sawNewerGeneration, Is.True, "Delayed FixedSteps must commit a generation newer than the wall baseline.");
        Assert.That(sawPostFenceMove, Is.True, "New formal side-route move may appear only after the generation fence.");
        Assert.That(actions.LastPathStatus, Is.EqualTo(NavPathStatus.Ok));
        AssertNavStable(actions.ActiveConfig, engine);
    }

    // Feature: Auto capture keeps the war camera locked while framing the fighting squad
    // Given a Raylib host frame clock and auto timeline enabled for the RTS fort
    // When the player reaches the initial screenshot beat
    // Then the locked auto-capture camera stays on the deterministic player framing
    // And enough authored squad members stay inside the authored coverage
    [Test]
    public void Feature_RtsAutoTimeline_PlayerFramingKeepsSquadOnScreen()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeDynamicRtsShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        Environment.SetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey, NavBakeNames.AlgorithmCdt);
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline = actions.ActiveConfig.RaylibAutoTimeline;
        RunHostFramesThrough(engine, actions, timeline.InitialScreenshotFrame);

        DynamicNavBakeShowcasePlayerFramingPose expected = actions.ResolvePlayerFramingPose(engine);
        Vector2 actual = engine.GameSession.Camera.State.TargetCm;
        float dx = actual.X - expected.TargetCm.X;
        float dy = actual.Y - expected.TargetCm.Y;
        float distance = MathF.Sqrt((dx * dx) + (dy * dy));
        Assert.That(
            distance,
            Is.LessThanOrEqualTo(timeline.CameraTargetToleranceCm),
            $"Auto capture camera must stay on the deterministic RTS player framing. actual=({actual.X},{actual.Y}) expected=({expected.TargetCm.X},{expected.TargetCm.Y}).");
        Assert.That(
            MathF.Abs(engine.GameSession.Camera.State.DistanceCm - expected.DistanceCm),
            Is.LessThanOrEqualTo(timeline.PlayerFraming.DistanceToleranceCm));
        Assert.That(
            engine.GameSession.Camera.VirtualCameraBrain!.ActiveCameraId,
            Is.EqualTo(DynamicNavBakeShowcaseIds.AutoCaptureCameraId));
        Assert.That(
            actions.CountSquadMembersInsidePlayerFraming(engine),
            Is.GreaterThanOrEqualTo(timeline.PlayerFraming.MinSquadMembersOnScreen));
        DynamicNavBakeShowcasePlayerFramingVisibility visibility =
            actions.CaptureSquadPlayerFramingVisibility(engine);
        float projectedSpan = MathF.Max(
            visibility.MaxScreenX - visibility.MinScreenX,
            visibility.MaxScreenY - visibility.MinScreenY);
        Assert.That(
            projectedSpan,
            Is.GreaterThanOrEqualTo(timeline.PlayerFraming.MinProjectedSquadSpanPx),
            "RTS auto capture must keep the squad large enough on screen to read as a fighting unit.");

        VirtualCameraRegistry registry = engine.GetService(CoreServiceKeys.VirtualCameraRegistry)!;
        Assert.That(registry.TryGet(DynamicNavBakeShowcaseIds.AutoCaptureCameraId, out VirtualCameraDefinition? definition), Is.True);
        Assert.That(definition!.PanMode, Is.EqualTo(CameraPanMode.None));
        Assert.That(definition.AllowUserInput, Is.False);
    }

    // Feature: Auto capture reframes when the sealed gate changes the battle
    // Given the RTS auto player reaches the initial then dynamic screenshot beats
    // When the wall seals the fort and the squad takes the side route
    // Then the deterministic framing target/distance changes with the stage
    // And enough authored squad members remain inside the coverage at both beats
    [Test]
    public void Feature_RtsAutoTimeline_PlayerFramingChangesAcrossStages()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeDynamicRtsShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        Environment.SetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey, NavBakeNames.AlgorithmCdt);
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline = actions.ActiveConfig.RaylibAutoTimeline;

        RunHostFramesThrough(engine, actions, timeline.InitialScreenshotFrame);
        DynamicNavBakeShowcasePlayerFramingPose initial = actions.ResolvePlayerFramingPose(engine);
        Assert.That(
            actions.CountSquadMembersInsidePlayerFraming(engine),
            Is.GreaterThanOrEqualTo(timeline.PlayerFraming.MinSquadMembersOnScreen));

        RunHostFramesFromThrough(engine, actions, timeline.InitialScreenshotFrame + 1, timeline.DynamicScreenshotFrame);
        DynamicNavBakeShowcasePlayerFramingPose dynamic = actions.ResolvePlayerFramingPose(engine);
        Assert.That(
            actions.CountSquadMembersInsidePlayerFraming(engine),
            Is.GreaterThanOrEqualTo(timeline.PlayerFraming.MinSquadMembersOnScreen));

        float targetDelta = Vector2.Distance(initial.TargetCm, dynamic.TargetCm);
        float distanceDelta = MathF.Abs(initial.DistanceCm - dynamic.DistanceCm);
        Assert.That(
            targetDelta + distanceDelta,
            Is.GreaterThan(1f),
            "Player framing must change between the open approach and the sealed-gate stage.");
    }

    // Feature: Open-world auto capture keeps the marching squad in frame
    // Given the open-world auto player on the authored host frame clock
    // When each of the three screenshot beats arrives
    // Then enough authored squad members stay inside the deterministic player framing
    [Test]
    public void Feature_OpenWorldAutoTimeline_PlayerFramingKeepsSquadOnScreenAtEachGate()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeOpenWorld64x64ShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        Environment.SetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey, NavBakeNames.AlgorithmCdt);
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline = actions.ActiveConfig.RaylibAutoTimeline;
        int[] gates =
        {
            timeline.InitialScreenshotFrame,
            timeline.DynamicScreenshotFrame,
            timeline.FinalScreenshotFrame
        };

        int previous = -1;
        for (int i = 0; i < gates.Length; i++)
        {
            RunHostFramesFromThrough(engine, actions, previous + 1, gates[i]);
            Assert.That(
                actions.CountSquadMembersInsidePlayerFraming(engine),
                Is.GreaterThanOrEqualTo(timeline.PlayerFraming.MinSquadMembersOnScreen),
                $"Open-world screenshot gate frame {gates[i]} must keep authored squad members in frame.");
            DynamicNavBakeShowcasePlayerFramingVisibility visibility =
                actions.CaptureSquadPlayerFramingVisibility(engine);
            float projectedSpan = MathF.Max(
                visibility.MaxScreenX - visibility.MinScreenX,
                visibility.MaxScreenY - visibility.MinScreenY);
            Assert.That(
                projectedSpan,
                Is.GreaterThanOrEqualTo(timeline.PlayerFraming.MinProjectedSquadSpanPx),
                $"Open-world screenshot gate frame {gates[i]} must keep the squad readable on screen.");
            Assert.That(
                engine.GameSession.Camera.VirtualCameraBrain!.ActiveCameraId,
                Is.EqualTo(DynamicNavBakeShowcaseIds.AutoCaptureCameraId));

            MinimapRuntime minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
                ?? throw new InvalidOperationException("Open-world auto capture requires MinimapRuntime.");
            DynamicNavBakeShowcaseMinimapRectConfig rect = actions.ActiveConfig.OpenWorld!.AutoCaptureMinimapRect;
            Assert.That(minimap.Visible, Is.True);
            Assert.That(minimap.FieldX + minimap.FieldSize, Is.LessThanOrEqualTo(rect.X + rect.Width + 1));
            Assert.That(minimap.FieldY + minimap.FieldSize, Is.LessThanOrEqualTo(rect.Y + rect.Height + 1));
            Assert.That(minimap.NativeChromeVisible, Is.True,
                "300x420 auto-capture minimap must fit native chrome so Markers 10/10 stay readable.");
            previous = gates[i];
        }
    }

    // Feature: Player framing ignores bake algorithm identity
    // Given the same world anchors and camera pitch/fov
    // When framing is computed as if for CDT, Recast, or Layered Span
    // Then the target and distance stay bitwise identical
    [Test]
    public void Feature_PlayerFraming_IsDeterministicAcrossAlgorithmLabels()
    {
        var framing = new DynamicNavBakeShowcasePlayerFramingConfig
        {
            CaptureWidthPx = 1280,
            CaptureHeightPx = 720,
            SafeInsetLeftPx = 432,
            SafeInsetTopPx = 32,
            SafeInsetRightPx = 32,
            SafeInsetBottomPx = 32,
            MarginCm = 600f,
            MinDistanceCm = 9000f,
            MaxDistanceCm = 18000f,
            BaseDistanceCm = 9000f,
            MinSquadMembersOnScreen = 8,
            MinProjectedSquadSpanPx = 48f,
            PathLookaheadCm = 5000f,
            CoverageBuffer = 1.1f,
            DistanceToleranceCm = 100f
        };
        framing.Validate();

        Span<Vector2> anchors = stackalloc Vector2[4];
        anchors[0] = new Vector2(0f, -3600f);
        anchors[1] = new Vector2(200f, -3400f);
        anchors[2] = new Vector2(0f, 0f);
        anchors[3] = new Vector2(0f, 3600f);

        DynamicNavBakeShowcasePlayerFramingPose a = DynamicNavBakeShowcasePlayerFraming.Compute(
            anchors,
            framing,
            pitchDeg: 58f,
            fovYDeg: 50f,
            yawDeg: 180f);
        DynamicNavBakeShowcasePlayerFramingPose b = DynamicNavBakeShowcasePlayerFraming.Compute(
            anchors,
            framing,
            pitchDeg: 58f,
            fovYDeg: 50f,
            yawDeg: 180f);
        DynamicNavBakeShowcasePlayerFramingPose c = DynamicNavBakeShowcasePlayerFraming.Compute(
            anchors,
            framing,
            pitchDeg: 58f,
            fovYDeg: 50f,
            yawDeg: 180f);

        Assert.That(b.TargetCm, Is.EqualTo(a.TargetCm));
        Assert.That(c.TargetCm, Is.EqualTo(a.TargetCm));
        Assert.That(b.DistanceCm, Is.EqualTo(a.DistanceCm));
        Assert.That(c.DistanceCm, Is.EqualTo(a.DistanceCm));
        Assert.That(a.DistanceCm, Is.GreaterThan(0f));
    }

    // Feature: Interactive play keeps the tactical camera when auto mode is off
    // Given the RTS Dynamic NavBake scene without the auto timeline environment variable
    // When the map finishes focusing
    // Then the player still gets the normal tactical camera for keyboard/edge play
    [Test]
    public void Feature_RtsInteractive_KeepsTacticalCameraWhenAutoTimelineDisabled()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeDynamicRtsShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        Assert.That(DynamicNavBakeShowcaseIds.IsAutoTimelineEnabled(), Is.False);
        Assert.That(
            engine.GameSession.Camera.VirtualCameraBrain!.ActiveCameraId,
            Is.EqualTo("Camera.Profile.Tactical"));
        Vector2 expected = actions.ResolveAuthoredCameraTargetCm();
        Vector2 actual = engine.GameSession.Camera.State.TargetCm;
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(1f));
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(1f));
    }

    // Feature: Auto player fails closed when the bake never commits before the deadline
    // Given auto timeline is enabled and the algorithm switch can never finish
    // When the host frame passes the algorithm commit deadline
    // Then the player sees a deadline failure with queue diagnostics (no continue-on-error)
    [Test]
    public void Feature_AutoTimeline_AlgorithmDeadline_ThrowsWithDiagnostics()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeDynamicRtsShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        // Recast is not registered in this host composition, so the switch request cannot become ready.
        Environment.SetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey, NavBakeNames.AlgorithmRecast);
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline = actions.ActiveConfig.RaylibAutoTimeline;

        InvalidOperationException? ex = null;
        for (int frame = 0; frame <= timeline.AlgorithmCommitDeadlineFrame; frame++)
        {
            engine.SetService(CoreServiceKeys.HostFrameIndex, frame);
            try
            {
                engine.Tick(DynamicNavBakeShowcaseAcceptanceHarness.DeltaTime);
            }
            catch (InvalidOperationException caught)
            {
                ex = caught;
                break;
            }
        }

        Assert.That(ex, Is.Not.Null, "Missing Recast adapter must fail the auto timeline algorithm deadline.");
        Assert.That(ex!.Message, Does.Contain("algorithm-ready").Or.Contain("TrySwitchAlgorithm").Or.Contain("recast"));
        Assert.That(ex.Message, Does.Contain("queue").IgnoreCase.Or.Contain("HostFrame").IgnoreCase.Or.Contain("algorithm"));
    }

    // Feature: Formal route readiness polling never allocates on the host-frame hot path
    // Given the RTS auto player has submitted its initial move and is waiting for FixedStep route resolution
    // When the same presentation host frame polls the full auto timeline repeatedly after warm-up
    // Then managed allocation stays exactly zero and no extra move is submitted
    [Test]
    public void Feature_FormalPlayerRouteSnapshot_RepeatedHostFramePoll_AllocatesZeroAfterWarmup()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeDynamicRtsShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)
            ?? throw new InvalidOperationException("Runtime nav queue is required for the auto timeline allocation contract.");
        Environment.SetEnvironmentVariable(
            DynamicNavBakeShowcaseIds.AutoTimelineEnvKey,
            NavBakeNames.FormatAlgorithm(queue.CurrentAlgorithm));
        engine.SetService(
            CoreServiceKeys.HostFrameIndex,
            actions.ActiveConfig.RaylibAutoTimeline.AlgorithmRequestEarliestFrame);

        var autoTimeline = new DynamicNavBakeShowcaseRaylibAutoTimeline();
        autoTimeline.Update(engine, actions);
        Assert.That(actions.FormalMoveCommandSubmitCount, Is.EqualTo(1));
        Assert.That(
            actions.CaptureFormalPlayerRouteSnapshot(engine).FormalReadyAgentCount,
            Is.EqualTo(0),
            "Without a FixedStep, the formal route must remain unresolved so repeated calls exercise readiness polling.");

        // Warm the complete timeline observation path outside the measured window.
        for (int i = 0; i < 64; i++)
        {
            autoTimeline.Update(engine, actions);
        }

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        long framingBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            _ = actions.ResolvePlayerFramingPose(engine);
        }

        long framingAllocated = GC.GetAllocatedBytesForCurrentThread() - framingBefore;
        Assert.That(framingAllocated, Is.EqualTo(0L), $"Player framing pose computation allocated {framingAllocated} managed bytes after warm-up.");

        long applyBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            actions.ApplyAutoCapturePlayerFraming(engine);
        }

        long applyAllocated = GC.GetAllocatedBytesForCurrentThread() - applyBefore;
        Assert.That(applyAllocated, Is.EqualTo(0L), $"Player framing camera application allocated {applyAllocated} managed bytes after warm-up.");

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        for (int i = 0; i < 8; i++)
        {
            autoTimeline.Update(engine, actions);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            autoTimeline.Update(engine, actions);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.EqualTo(0L), $"Auto timeline readiness poll allocated {allocated} managed bytes after warm-up.");
        Assert.That(actions.FormalMoveCommandSubmitCount, Is.EqualTo(1));
    }

    // Feature: Open-world auto framing never re-reads the timeline env on the host-frame hot path
    // Given the open-world auto player has already sticky-enabled the timeline after map load
    // When ApplyAutoCapturePlayerFraming runs repeatedly (including EnableOpenWorldMinimap)
    // Then managed allocation stays exactly zero — no per-frame Environment.GetEnvironmentVariable
    [Test]
    public void Feature_OpenWorld_ApplyAutoCapturePlayerFraming_AllocatesZeroAfterEnvSticky()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeOpenWorld64x64ShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        // Env may flip on after map load; sticky-true must latch on first successful read.
        Environment.SetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey, NavBakeNames.AlgorithmCdt);
        engine.SetService(CoreServiceKeys.HostFrameIndex, 0);
        actions.EnsureAutoCaptureCameraActive(engine);

        for (int i = 0; i < 64; i++)
        {
            actions.ApplyAutoCapturePlayerFraming(engine);
        }

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            actions.ApplyAutoCapturePlayerFraming(engine);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(
            allocated,
            Is.EqualTo(0L),
            $"Open-world ApplyAutoCapturePlayerFraming allocated {allocated} managed bytes after sticky env latch " +
            "(must not re-read Environment.GetEnvironmentVariable each frame).");

        MinimapRuntime minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
            ?? throw new InvalidOperationException("Open-world auto capture requires MinimapRuntime.");
        Assert.That(minimap.NativeChromeVisible, Is.True);
        Assert.That(minimap.VisibleMarkerCount, Is.EqualTo(10));
    }

    // Feature: Initial, dynamic, and final each submit exactly one formal move
    // Given the RTS auto player running on the authored host frame clock
    // When the player reaches each screenshot beat
    // Then FormalMoveCommandSubmitCount advances by exactly one per explicit move phase
    // And keepalive / screenshot observation frames never submit an extra command
    [Test]
    public void Feature_AutoTimeline_ExplicitMovePhases_SubmitExactlyOnce_NoReissueFromObservation()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeDynamicRtsShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        Environment.SetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey, NavBakeNames.AlgorithmCdt);
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline = actions.ActiveConfig.RaylibAutoTimeline;
        Assert.That(timeline.DynamicActionFrame, Is.GreaterThan(timeline.InitialScreenshotFrame));
        Assert.That(timeline.FinalActionFrame, Is.GreaterThan(timeline.DynamicScreenshotFrame));

        RunHostFramesThrough(engine, actions, timeline.InitialScreenshotFrame);
        Assert.That(actions.FormalMoveCommandSubmitCount, Is.EqualTo(1), "Initial move phase must submit exactly once.");
        Assert.That(actions.SquadDeployed, Is.True);
        AssertFormalNavMeshRouteReady(actions, engine);

        int submitAfterInitial = actions.FormalMoveCommandSubmitCount;
        int keepaliveEnd = timeline.DynamicActionFrame - 1;
        Assert.That(keepaliveEnd, Is.GreaterThanOrEqualTo(timeline.InitialScreenshotFrame));
        RunHostFramesFromThrough(engine, actions, timeline.InitialScreenshotFrame + 1, keepaliveEnd);
        Assert.That(
            actions.FormalMoveCommandSubmitCount,
            Is.EqualTo(submitAfterInitial),
            "Initial keepalive / screenshot observation must remain read-only and must not reissue TryCommandMoveToGoal.");

        RunHostFramesFromThrough(engine, actions, timeline.DynamicActionFrame, timeline.DynamicScreenshotFrame);
        Assert.That(actions.FormalMoveCommandSubmitCount, Is.EqualTo(2), "Dynamic move phase must submit exactly once after the sealed gate.");
        Assert.That(actions.WallDeployedCount, Is.EqualTo(actions.ActiveConfig.Gate.SegmentCount));
        AssertFormalNavMeshRouteReady(actions, engine);

        int submitAfterDynamic = actions.FormalMoveCommandSubmitCount;
        int dynamicKeepaliveEnd = timeline.FinalActionFrame - 1;
        Assert.That(dynamicKeepaliveEnd, Is.GreaterThanOrEqualTo(timeline.DynamicScreenshotFrame));
        RunHostFramesFromThrough(engine, actions, timeline.DynamicScreenshotFrame + 1, dynamicKeepaliveEnd);
        Assert.That(
            actions.FormalMoveCommandSubmitCount,
            Is.EqualTo(submitAfterDynamic),
            "Dynamic keepalive / screenshot observation must remain read-only.");

        RunHostFramesFromThrough(engine, actions, timeline.FinalActionFrame, timeline.FinalScreenshotFrame);
        Assert.That(actions.FormalMoveCommandSubmitCount, Is.EqualTo(3), "Final move phase must submit exactly once after demolish.");
        Assert.That(actions.WallDeployedCount, Is.EqualTo(0));
        AssertSquadArrivedAtAuthoredGoalSlots(actions, engine);
        Assert.That(CountActiveSquadMoveOrders(engine), Is.EqualTo(0));
        AssertNavStable(actions.ActiveConfig, engine);
    }

    // Feature: RTS formal move keeps formation slots around one shared goal
    // Given the RTS auto player has issued the initial formal massNavigationMove batch
    // When the player inspects each active OrderBuffer through OrderWorldSpatialResolver
    // Then all eight members keep distinct authored grid destinations around the same shared goal
    // And destinations come from squad columns/rows/spacing, not hardcoded per-member coordinates
    [Test]
    public void Feature_RtsAutoTimeline_InitialFormalMove_UsesDistinctAuthoredSlotDestinations()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeDynamicRtsShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        Environment.SetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey, NavBakeNames.AlgorithmCdt);
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline = actions.ActiveConfig.RaylibAutoTimeline;
        RunHostFramesThrough(engine, actions, timeline.InitialScreenshotFrame);

        DynamicNavBakeShowcaseSquadConfig squad = actions.ActiveConfig.Squad;
        Assert.That(actions.FormalMoveCommandSubmitCount, Is.EqualTo(1));
        Assert.That(actions.SquadEntities.Length, Is.EqualTo(squad.Count));
        Assert.That(squad.Count, Is.EqualTo(8));
        Assert.That(CountActiveSquadMoveOrders(engine), Is.EqualTo(squad.Count));

        OrderTypeRegistry registry = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("OrderTypeRegistry is required to observe formal move destinations.");
        if (!registry.TryGetId(MassNavigationOrderKeys.Move, out int moveOrderTypeId))
        {
            throw new InvalidOperationException(
                $"Formal move destinations require order type '{MassNavigationOrderKeys.Move}'.");
        }

        int sharedGoalXCm = actions.ActiveConfig.Goal.XCm;
        int sharedGoalZCm = actions.ActiveConfig.Goal.YCm;
        var seenDestinations = new System.Collections.Generic.HashSet<(int X, int Z)>();
        ReadOnlySpan<Entity> squadEntities = actions.SquadEntities;
        for (int slotIndex = 0; slotIndex < squadEntities.Length; slotIndex++)
        {
            Entity entity = squadEntities[slotIndex];
            Assert.That(engine.World.IsAlive(entity), Is.True);
            Assert.That(engine.World.Has<OrderBuffer>(entity), Is.True);

            ref readonly OrderBuffer buffer = ref engine.World.Get<OrderBuffer>(entity);
            Assert.That(buffer.HasActive, Is.True, $"Squad slot {slotIndex} must hold an active formal move.");
            Assert.That(buffer.ActiveOrder.Order.OrderTypeId, Is.EqualTo(moveOrderTypeId));
            Assert.That(buffer.ActiveOrder.Order.Actor, Is.EqualTo(entity));

            bool resolvedDestination = OrderWorldSpatialResolver.TryResolveMoveDestination(
                engine.World,
                in buffer.ActiveOrder.Order,
                out Vector3 destination);
            Assert.That(
                resolvedDestination,
                Is.True,
                $"Squad slot {slotIndex} active order must resolve through OrderWorldSpatialResolver.");

            DynamicNavBakeShowcaseWallPool.ComputeSquadSlotOffsetCm(
                squad,
                slotIndex,
                out int offsetXCm,
                out int offsetZCm);
            int expectedXCm = checked(sharedGoalXCm + offsetXCm);
            int expectedZCm = checked(sharedGoalZCm + offsetZCm);
            Assert.That(
                (int)destination.X,
                Is.EqualTo(expectedXCm),
                $"Slot {slotIndex} destination X must be shared goal + authored grid offset.");
            Assert.That(
                (int)destination.Z,
                Is.EqualTo(expectedZCm),
                $"Slot {slotIndex} destination Z must be shared goal + authored grid offset.");
            Assert.That(
                seenDestinations.Add(((int)destination.X, (int)destination.Z)),
                Is.True,
                $"Slot {slotIndex} destination must be distinct from other members.");
        }

        Assert.That(seenDestinations.Count, Is.EqualTo(squad.Count));
    }

    // Feature: RTS final capture refuses to finish on route-ready alone
    // Given the RTS auto player has rebuilt the open path after demolition and the restored formal route is ready
    // When the squad is still marching toward the authored goal slots
    // Then jumping to the final screenshot frame fails closed instead of claiming completion
    [Test]
    public void Feature_RtsAutoTimeline_FinalCapture_DoesNotCompleteOnRouteReadyAlone()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeDynamicRtsShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        Environment.SetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey, NavBakeNames.AlgorithmCdt);
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline = actions.ActiveConfig.RaylibAutoTimeline;
        Assert.That(
            timeline.ResolvedFinalCaptureCompletionMode,
            Is.EqualTo(DynamicNavBakeShowcaseFinalCaptureCompletionMode.Arrival));

        int targetFps = engine.MergedConfig.TargetFps;
        Assert.That(targetFps, Is.GreaterThan(0));
        float hostDeltaTime = 1f / targetFps;
        DynamicNavBakeShowcaseAcceptanceHarness.EnsureCaptureViewController(engine, actions);

        bool sawFinalRouteReadyWhileMoving = false;
        InvalidOperationException? earlyFinalEx = null;
        for (int frame = 0; frame <= timeline.FinalScreenshotFrame; frame++)
        {
            engine.SetService(CoreServiceKeys.HostFrameIndex, frame);
            engine.Tick(hostDeltaTime);

            if (actions.FormalMoveCommandSubmitCount == 3 &&
                actions.WallDeployedCount == 0 &&
                CountActiveSquadMoveOrders(engine) > 0)
            {
                DynamicNavBakeShowcaseFormalPlayerRouteSnapshot route = actions.CaptureFormalPlayerRouteSnapshot(engine);
                if (route.FormalReadyAgentCount == actions.ActiveConfig.Squad.Count &&
                    route.AgreedPathDomain == PathDomain.NavMesh &&
                    route.MinWaypointCount > 0)
                {
                    sawFinalRouteReadyWhileMoving = true;
                    engine.SetService(CoreServiceKeys.HostFrameIndex, timeline.FinalScreenshotFrame);
                    earlyFinalEx = Assert.Throws<InvalidOperationException>(() => engine.Tick(hostDeltaTime));
                    break;
                }
            }
        }

        Assert.That(
            sawFinalRouteReadyWhileMoving,
            Is.True,
            "Expected a window where the restored final formal route is ready while members still hold active move orders.");
        Assert.That(earlyFinalEx, Is.Not.Null);
        Assert.That(
            earlyFinalEx!.Message,
            Does.Contain("final-screenshot").Or.Contain("Completed").Or.Contain("final-arrival"));
        Assert.That(actions.FormalMoveCommandSubmitCount, Is.EqualTo(3));
        Assert.That(CountActiveSquadMoveOrders(engine), Is.GreaterThan(0));
    }

    // Feature: RTS final screenshot shows the squad idle on authored goal slots
    // Given the RTS auto player runs through the authored arrival-mode final beat
    // When the final screenshot frame arrives
    // Then every authored member is idle, inside the data-driven slot tolerance, and no phantom wall remains
    [Test]
    public void Feature_RtsAutoTimeline_FinalScreenshot_RequiresIdleInToleranceArrival()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeDynamicRtsShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        Environment.SetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey, NavBakeNames.AlgorithmCdt);
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline = actions.ActiveConfig.RaylibAutoTimeline;
        RunHostFramesThrough(engine, actions, timeline.FinalScreenshotFrame);

        Assert.That(actions.WallDeployedCount, Is.EqualTo(0));
        Assert.That(actions.FormalMoveCommandSubmitCount, Is.EqualTo(3));
        AssertSquadArrivedAtAuthoredGoalSlots(actions, engine);
        Assert.That(CountActiveSquadMoveOrders(engine), Is.EqualTo(0));
    }

    // Feature: Final player framing drops the demolished wall
    // Given the RTS auto player demolishes the gate and the squad finishes at the goal
    // When the final framing pose is resolved
    // Then the camera target stays with the fighting squad near the goal instead of a phantom wall at the old gate center
    [Test]
    public void Feature_RtsAutoTimeline_FinalFraming_HasNoPhantomWallAfterDemolition()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeDynamicRtsShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        Environment.SetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey, NavBakeNames.AlgorithmCdt);
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline = actions.ActiveConfig.RaylibAutoTimeline;
        RunHostFramesThrough(engine, actions, timeline.FinalScreenshotFrame);

        Assert.That(actions.WallDeployedCount, Is.EqualTo(0));
        DynamicNavBakeShowcasePlayerFramingPose pose = actions.ResolvePlayerFramingPose(engine);
        int gateYCm = actions.ActiveConfig.Gate.CenterYCm;
        int goalYCm = actions.ActiveConfig.Goal.YCm;
        float midpointY = (gateYCm + goalYCm) * 0.5f;
        Assert.That(
            pose.TargetCm.Y,
            Is.GreaterThan(midpointY),
            $"Final framing after demolition must not keep a phantom wall anchor at the gate. " +
            $"targetY={pose.TargetCm.Y:F1}, gateY={gateYCm}, goalY={goalYCm}, midpointY={midpointY:F1}.");
        Assert.That(
            MathF.Abs(pose.TargetCm.Y - goalYCm),
            Is.LessThan(MathF.Abs(pose.TargetCm.Y - gateYCm)),
            "Final framing target must sit closer to the goal/squad than to the demolished gate center.");
    }

    // Feature: Open-world final beat stays a continuous corridor march
    // Given the open-world auto player authored as route_ready final completion
    // When the final screenshot frame arrives
    // Then the restored formal NavMesh route remains ready for the marching squad
    [Test]
    public void Feature_OpenWorldAutoTimeline_FinalCapture_StaysRouteReady()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeOpenWorld64x64ShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        Environment.SetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey, NavBakeNames.AlgorithmCdt);
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline = actions.ActiveConfig.RaylibAutoTimeline;
        Assert.That(
            timeline.ResolvedFinalCaptureCompletionMode,
            Is.EqualTo(DynamicNavBakeShowcaseFinalCaptureCompletionMode.RouteReady));
        RunHostFramesThrough(engine, actions, timeline.FinalScreenshotFrame);

        Assert.That(actions.WallDeployedCount, Is.EqualTo(0));
        AssertFormalNavMeshRouteReady(actions, engine);
        Assert.That(actions.LastCoarseCorridorNodeCount, Is.GreaterThan(2));
        Assert.That(actions.FormalMoveCommandSubmitCount, Is.EqualTo(3));
    }

    // Feature: Arrival observation polling never allocates on the host-frame hot path
    // Given the RTS auto player has submitted the final restored move and is waiting for arrival
    // When the same presentation host frame polls arrival observation repeatedly after warm-up
    // Then managed allocation stays exactly zero and FormalMoveCommandSubmitCount remains 3
    [Test]
    public void Feature_SquadArrivalSnapshot_RepeatedHostFramePoll_AllocatesZeroAfterWarmup()
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            "NavBakeDynamicRtsShowcaseMod",
            registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(
            engine,
            DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        Environment.SetEnvironmentVariable(DynamicNavBakeShowcaseIds.AutoTimelineEnvKey, NavBakeNames.AlgorithmCdt);
        DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline = actions.ActiveConfig.RaylibAutoTimeline;
        int targetFps = engine.MergedConfig.TargetFps;
        Assert.That(targetFps, Is.GreaterThan(0));
        float hostDeltaTime = 1f / targetFps;
        DynamicNavBakeShowcaseAcceptanceHarness.EnsureCaptureViewController(engine, actions);

        bool enteredFinalArrivalPoll = false;
        for (int frame = 0; frame <= timeline.FinalScreenshotFrame; frame++)
        {
            engine.SetService(CoreServiceKeys.HostFrameIndex, frame);
            engine.Tick(hostDeltaTime);
            if (actions.FormalMoveCommandSubmitCount == 3 &&
                actions.WallDeployedCount == 0 &&
                CountActiveSquadMoveOrders(engine) > 0)
            {
                DynamicNavBakeShowcaseFormalPlayerRouteSnapshot route = actions.CaptureFormalPlayerRouteSnapshot(engine);
                if (route.FormalReadyAgentCount == actions.ActiveConfig.Squad.Count &&
                    route.AgreedPathDomain == PathDomain.NavMesh &&
                    route.MinWaypointCount > 0)
                {
                    enteredFinalArrivalPoll = true;
                    break;
                }
            }
        }

        Assert.That(enteredFinalArrivalPoll, Is.True, "Expected to reach final restored-route while members are still moving.");
        Assert.That(actions.FormalMoveCommandSubmitCount, Is.EqualTo(3));

        for (int i = 0; i < 64; i++)
        {
            _ = actions.CaptureSquadArrivalSnapshot(engine);
        }

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            _ = actions.CaptureSquadArrivalSnapshot(engine);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.EqualTo(0L), $"Squad arrival observation allocated {allocated} managed bytes after warm-up.");
        Assert.That(actions.FormalMoveCommandSubmitCount, Is.EqualTo(3));
    }

    private static void RunHostFramesThrough(GameEngine engine, DynamicNavBakeShowcaseActions actions, int lastInclusiveFrame)
    {
        RunHostFramesFromThrough(engine, actions, firstInclusiveFrame: 0, lastInclusiveFrame);
    }

    private static void RunHostFramesFromThrough(
        GameEngine engine,
        DynamicNavBakeShowcaseActions actions,
        int firstInclusiveFrame,
        int lastInclusiveFrame)
    {
        DynamicNavBakeShowcaseAcceptanceHarness.EnsureCaptureViewController(engine, actions);
        if (lastInclusiveFrame < firstInclusiveFrame)
        {
            return;
        }

        int targetFps = engine.MergedConfig.TargetFps;
        Assert.That(
            targetFps,
            Is.GreaterThan(0),
            "Dynamic NavBake auto timeline tests require a positive merged GameConfig.TargetFps.");
        float hostDeltaTime = 1f / targetFps;
        // Mirror the authored host clock; the pacemaker accumulates 20 Hz FixedSteps itself.
        for (int frame = firstInclusiveFrame; frame <= lastInclusiveFrame; frame++)
        {
            engine.SetService(CoreServiceKeys.HostFrameIndex, frame);
            engine.Tick(hostDeltaTime);
        }
    }

    private static void AssertFormalNavMeshRouteReady(DynamicNavBakeShowcaseActions actions, GameEngine engine)
    {
        DynamicNavBakeShowcaseFormalPlayerRouteSnapshot route = actions.CaptureFormalPlayerRouteSnapshot(engine);
        Assert.That(route.FormalReadyAgentCount, Is.EqualTo(actions.ActiveConfig.Squad.Count));
        Assert.That(route.AgreedPathDomain, Is.EqualTo(PathDomain.NavMesh));
        Assert.That(route.MinWaypointCount, Is.GreaterThan(0));
        Assert.That(actions.LastPathStatus, Is.EqualTo(NavPathStatus.Ok));
        Assert.That(actions.LastPathPointCount, Is.GreaterThan(1));
        Assert.That(actions.PathOrchestrationState, Is.EqualTo(DynamicNavBakePathOrchestrationState.LocalSegmentReady));
    }

    private static void AssertSquadArrivedAtAuthoredGoalSlots(DynamicNavBakeShowcaseActions actions, GameEngine engine)
    {
        DynamicNavBakeShowcaseSquadArrivalSnapshot arrival = actions.CaptureSquadArrivalSnapshot(engine);
        Assert.That(arrival.OutsideToleranceWithoutMoveCount, Is.EqualTo(0), FormatArrivalDiagnostics(arrival, actions));
        Assert.That(arrival.AllIdleInTolerance, Is.True, FormatArrivalDiagnostics(arrival, actions));
        Assert.That(arrival.IdleInToleranceCount, Is.EqualTo(actions.ActiveConfig.Squad.Count));
        Assert.That(arrival.ActiveMoveOrderCount, Is.EqualTo(0));
    }

    private static string FormatArrivalDiagnostics(
        DynamicNavBakeShowcaseSquadArrivalSnapshot arrival,
        DynamicNavBakeShowcaseActions actions)
    {
        return
            $"idleInTolerance={arrival.IdleInToleranceCount}/{arrival.SquadCount}, " +
            $"activeMoveOrders={arrival.ActiveMoveOrderCount}, " +
            $"outsideWithoutMove={arrival.OutsideToleranceWithoutMoveCount}, " +
            $"toleranceCm={actions.ActiveConfig.RaylibAutoTimeline.FinalArrivalMemberToleranceCm}, " +
            $"firstOutsideSlot={arrival.FirstOutsideSlotIndex}, " +
            $"actual=({arrival.FirstOutsideXCm},{arrival.FirstOutsideZCm}), " +
            $"expected=({arrival.FirstExpectedXCm},{arrival.FirstExpectedZCm}).";
    }

    private static int CountActiveSquadMoveOrders(GameEngine engine)
    {
        OrderTypeRegistry registry = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("OrderTypeRegistry is required to count formal move orders.");
        if (!registry.TryGetId(MassNavigationOrderKeys.Move, out int moveOrderTypeId))
        {
            throw new InvalidOperationException(
                $"Formal move count requires order type '{MassNavigationOrderKeys.Move}'.");
        }

        int count = 0;
        var query = new QueryDescription().WithAll<OrderBuffer, CommandSourceSelectableState>();
        engine.World.Query(in query, (Entity entity, ref OrderBuffer buffer, ref CommandSourceSelectableState selectable) =>
        {
            _ = entity;
            if (selectable.IsEnabled == 0 || !buffer.HasActive)
            {
                return;
            }

            if (buffer.ActiveOrder.Order.OrderTypeId == moveOrderTypeId)
            {
                count++;
            }
        });
        return count;
    }

    private static void AssertNavStable(DynamicNavBakeShowcaseConfig config, GameEngine engine)
    {
        RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)!;
        RuntimeNavMeshTelemetryService telemetry = engine.GetService(CoreServiceKeys.RuntimeNavMeshTelemetry)!;
        int residentTarget = checked(config.ResidentWidthChunks * config.ResidentHeightChunks);
        Assert.That(queue.Status, Is.EqualTo(RuntimeNavMeshRebuildStatus.Idle));
        Assert.That(queue.PendingTileCount, Is.EqualTo(0));
        Assert.That(queue.SealedRemainingCount, Is.EqualTo(0));
        Assert.That(queue.HasResidentWindowTransition, Is.False);
        Assert.That(queue.ResidentWindowCount, Is.EqualTo(residentTarget));
        Assert.That(queue.CommittedResidentWindowCount, Is.EqualTo(residentTarget));
        Assert.That(telemetry.HasOpenGeneration, Is.False);
        Assert.That(telemetry.FailedBatchCount, Is.EqualTo(0));
    }
}
