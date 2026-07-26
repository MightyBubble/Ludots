using System;
using DynamicNavBakeShowcaseMod;
using DynamicNavBakeShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

/// <summary>
/// Feature: algorithm switch after quiescent bootstrap measures one full resident generation
/// Given the player has finished map/action/nav bootstrap on the formal GameEngine chain
/// When they switch bake algorithm over the committed 8x8 resident window
/// Then telemetry publishes exactly one generation covering those 64 tiles
/// And no open/failed/mixed/dropped counters fire
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("acceptance")]
public sealed class DynamicNavBakeBootstrapAlgorithmSwitchSeamTests
{
    [TestCase("NavBakeDynamicRtsShowcaseMod", DynamicNavBakeShowcaseIds.RtsMapId, TestName = "Feature_AlgorithmSwitch_AfterQuiescentBootstrap_PublishesOneResidentGeneration_Rts")]
    [TestCase("NavBakeOpenWorld64x64ShowcaseMod", DynamicNavBakeShowcaseIds.OpenWorldMapId, TestName = "Feature_AlgorithmSwitch_AfterQuiescentBootstrap_PublishesOneResidentGeneration_OpenWorld")]
    public void Feature_AlgorithmSwitch_AfterQuiescentBootstrap_PublishesOneResidentGeneration(
        string sceneModId,
        string mapId)
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(sceneModId, registerRecast: false);
        engine.LoadMap(mapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(engine, mapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        int expectedResidentTiles = checked(actions.ActiveConfig.ResidentWidthChunks * actions.ActiveConfig.ResidentHeightChunks);
        Assert.That(expectedResidentTiles, Is.EqualTo(64));

        DynamicNavBakeShowcaseAcceptanceHarness.BeginEvidenceEpoch(engine);
        Assert.That(actions.TrySwitchAlgorithm(engine, NavBakeAlgorithmKind.LayeredSpan, out string switchError), Is.True, switchError);
        actions.DrainUntilIdle(engine, maxTicks: 8192);

        RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)!;
        RuntimeNavMeshTelemetryService telemetry = DynamicNavBakeShowcaseAcceptanceHarness.RequireTelemetry(engine);
        RuntimeNavMeshTelemetrySnapshot snap = telemetry.CaptureSnapshot();

        Assert.That(queue.CurrentAlgorithm, Is.EqualTo(NavBakeAlgorithmKind.LayeredSpan));
        Assert.That(queue.HasRequestedAlgorithm, Is.False);
        Assert.That(queue.Status, Is.EqualTo(RuntimeNavMeshRebuildStatus.Idle));
        Assert.That(queue.PendingTileCount, Is.EqualTo(0));
        Assert.That(queue.SealedRemainingCount, Is.EqualTo(0));
        Assert.That(queue.CommittedResidentWindowCount, Is.EqualTo(expectedResidentTiles));
        Assert.That(telemetry.HasOpenGeneration, Is.False);
        Assert.That(snap.SampleCount, Is.EqualTo(1));
        Assert.That(snap.TotalProcessedTiles, Is.EqualTo(expectedResidentTiles));
        Assert.That(snap.LastRebuiltTileCount, Is.EqualTo(expectedResidentTiles));
        Assert.That(snap.LastCommitted, Is.True);
        Assert.That(snap.LastAborted, Is.False);
        Assert.That(snap.FailedBatchCount, Is.EqualTo(0));
        Assert.That(snap.MixedGenerationCount, Is.EqualTo(0));
        Assert.That(snap.DroppedSampleCount, Is.EqualTo(0));
        Assert.That(snap.FallbackCount, Is.EqualTo(0));
        Assert.That(snap.DroppedDirtyCommandCount, Is.EqualTo(0));
        Assert.That(snap.SteadyStateTilesPerSecond, Is.GreaterThan(0d));
    }
}
