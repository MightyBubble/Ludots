using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Launcher.Backend;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using NUnit.Framework;
using SkiaSharp;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    [Category("acceptance")]
    public sealed class EntityQueryTacticsShowcasePlayableAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string InputBackendKey = "Tests.EntityQueryTactics.InputBackend";
        private const string ShowcasePresetId = "entity_query_tactics_raylib";
        private static readonly object ShowcaseBuildLock = new();
        private static string? _showcaseBuildRoot;

        [Test]
        public void EntityQueryTactics_PlayableAcceptance_WritesArtifacts()
        {
            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "entity-query-tactics-showcase");
            string screensDir = Path.Combine(artifactDir, "screens");
            AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);

            var timeline = new List<string>();
            var snapshots = new List<AcceptanceSnapshot>();
            var frames = new List<UiAcceptanceEvidenceFrame>();
            var frameTimesMs = new List<double>();

            using var engine = CreateEngine();
            EntityQueryTacticsShowcaseConfig config = LoadShowcaseConfig(engine);
            IReadOnlyDictionary<string, string> bindings = LoadInputBindings(engine);
            var backend = GetInputBackend(engine);
            var uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
                ?? throw new InvalidOperationException("UIRoot missing.");
            var ground = engine.GetService(CoreServiceKeys.GroundOverlayBuffer) as GroundOverlayBuffer
                ?? throw new InvalidOperationException("GroundOverlayBuffer missing.");
            var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore missing.");
            var relationships = engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("RelationshipRuntime missing.");
            var relationshipTypes = engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
                ?? throw new InvalidOperationException("RelationshipTypeRegistry missing.");
            var relationshipMetrics = engine.GetService(CoreServiceKeys.RelationshipMetricRegistry)
                ?? throw new InvalidOperationException("RelationshipMetricRegistry missing.");

            LoadMap(engine, config.MapId, frameTimesMs);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));

            Entity owner = FindEntityByName(engine.World, config.Scenario.PlayerCommanderName);
            Entity pressureTarget = FindEntityByName(engine.World, config.Scenario.PressurePulse.TargetName);
            int tacticalIntelTypeId = relationshipTypes.GetId(config.Relationships.TacticalIntel);
            int threatMetricId = relationshipMetrics.GetId(config.Scenario.PressurePulse.Metric);

            TickUntil(
                engine,
                frameTimesMs,
                () => AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot).Any(text => text.Contains(config.Presentation.Title, StringComparison.Ordinal)),
                maxFrames: 40,
                diagnostics: () => BuildStartupDiagnostics(engine, uiRoot));

            CaptureSnapshot(engine, uiRoot, ground, collections, config, snapshots, frames, screensDir, "map_loaded");
            timeline.Add("[T+001] Loaded production Entity Query Tactics map through ConfigPipeline; UI mounted and graph outputs were initialized by the mod system.");

            string[] friendlyNames = config.Scenario.Allies.Select(static actor => actor.Name).ToArray();
            DragSelectNamed(engine, backend, frameTimesMs, friendlyNames);
            AssertCollectionCount(engine, owner, config.Collections.UiBox, friendlyNames.Length);
            AssertCollectionCount(engine, owner, config.Collections.CommandSourceMirror, friendlyNames.Length);
            AssertCollectionCount(engine, owner, config.Collections.FormationPrimary, friendlyNames.Length);
            CaptureSnapshot(engine, uiRoot, ground, collections, config, snapshots, frames, screensDir, "ui_box_acquisition_only");
            timeline.Add("[T+002] Player dragged a friendly box; CommandSourceAcquisition wrote both the UI acquisition collection and the authoritative command source.");

            PressButton(engine, backend, GetBinding(bindings, config.Actions.CommitSelection), frameTimesMs);
            TickUntil(engine, frameTimesMs, () => ReadCollectionSnapshot(engine, owner, config.Collections.CommandSourceMirror, required: false).Count == friendlyNames.Length, maxFrames: 30);
            AssertCollectionCount(engine, owner, config.Collections.CommandSourceMirror, friendlyNames.Length);
            AssertCollectionCount(engine, owner, config.Collections.FormationPrimary, friendlyNames.Length);
            CaptureSnapshot(engine, uiRoot, ground, collections, config, snapshots, frames, screensDir, "command_source_confirmed");
            timeline.Add("[T+003] Configured commit action confirmed the command source and refreshed the formation collection.");

            PressButton(engine, backend, GetBinding(bindings, config.Actions.ExecuteGraphs), frameTimesMs);
            TickUntil(engine, frameTimesMs, () => ReadSummaryInt(engine, owner, config.SummaryKeys.SelectedCount) > 0, maxFrames: 30);
            EntityCollectionSnapshot selectedResult = ReadCollectionSnapshot(engine, owner, config.Collections.SelectedFriendliesResult);
            GraphConfig selectedGraphConfig = LoadGraphConfig(engine, config.Graphs.SelectedFriendlies);
            GraphConfig hostileGraphConfig = LoadGraphConfig(engine, config.Graphs.HostileThreats);
            GraphConfig formationGraphConfig = LoadGraphConfig(engine, config.Graphs.FormationCache);
            AssertSelectedFriendliesResult(engine, selectedResult, config, selectedGraphConfig);
            Assert.That(ReadSummaryInt(engine, owner, config.SummaryKeys.SelectedCount), Is.EqualTo(selectedResult.Count));
            Assert.That(ReadSummaryFloat(engine, owner, config.SummaryKeys.SelectedCommandPower), Is.EqualTo(SumAttribute(engine, selectedResult.Entities, config.Attributes.CommandPower)));
            Assert.That(ReadSummaryFloat(engine, owner, config.SummaryKeys.SelectedSupply), Is.EqualTo(SumAttribute(engine, selectedResult.Entities, config.Attributes.Supply)));
            Assert.That(ReadSummaryEntity(engine, owner, config.SummaryKeys.SelectedBestEntity), Is.EqualTo(MaxAttributeEntity(engine, selectedResult.Entities, config.Attributes.CommandPower)));
            CaptureSnapshot(engine, uiRoot, ground, collections, config, snapshots, frames, screensDir, "selected_friendlies_graph");
            timeline.Add($"[T+004] GraphReturnWriter materialized `{config.Graphs.SelectedFriendlies}` from `{config.Collections.UiBox}` with graph-defined team/template/tag/attr filters, sorting, aggregate, and extreme summaries.");

            EntityCollectionSnapshot threatResult = ReadCollectionSnapshot(engine, owner, config.Collections.HostileThreatResult);
            AssertHostileThreatResult(engine, relationships, owner, threatResult, tacticalIntelTypeId, threatMetricId, config, hostileGraphConfig);
            Assert.That(ReadSummaryInt(engine, owner, config.SummaryKeys.ThreatCount), Is.EqualTo(threatResult.Count));
            Assert.That(ReadSummaryInt(engine, owner, config.SummaryKeys.ThreatSum), Is.EqualTo(SumRelationshipMetric(relationships, owner, threatResult.Entities, tacticalIntelTypeId, threatMetricId)));
            Assert.That(ReadSummaryInt(engine, owner, config.SummaryKeys.ThreatAverage), Is.EqualTo(AverageRelationshipMetric(relationships, owner, threatResult.Entities, tacticalIntelTypeId, threatMetricId)));
            Assert.That(ReadSummaryInt(engine, owner, config.SummaryKeys.ThreatMax), Is.EqualTo(MaxRelationshipMetric(relationships, owner, threatResult.Entities, tacticalIntelTypeId, threatMetricId)));
            CaptureSnapshot(engine, uiRoot, ground, collections, config, snapshots, frames, screensDir, "hostile_relation_graph");
            timeline.Add($"[T+005] `{config.Graphs.HostileThreats}` used real RelationshipRuntime `{config.Relationships.TacticalIntel}` metric/flag filters, sorted priority hostiles, and aggregated threat sum/avg/max.");

            PressButton(engine, backend, GetBinding(bindings, config.Actions.RotateFormation), frameTimesMs);
            TickUntil(engine, frameTimesMs, () => ReadSummaryInt(engine, owner, config.SummaryKeys.FormationCount) > 0, maxFrames: 30);
            EntityCollectionSnapshot formationResult = ReadCollectionSnapshot(engine, owner, config.Collections.FormationCacheResult);
            AssertFormationResult(engine, formationResult, config, formationGraphConfig);
            Assert.That(ReadSummaryInt(engine, owner, config.SummaryKeys.FormationCount), Is.EqualTo(formationResult.Count));
            Assert.That(ReadSummaryFloat(engine, owner, config.SummaryKeys.FormationMaxCommandPower), Is.EqualTo(MaxAttribute(engine, formationResult.Entities, config.Attributes.CommandPower)));
            Assert.That(ReadSummaryFloat(engine, owner, config.SummaryKeys.FormationMinSupply), Is.EqualTo(MinAttribute(engine, formationResult.Entities, config.Attributes.Supply)));
            CaptureSnapshot(engine, uiRoot, ground, collections, config, snapshots, frames, screensDir, "formation_cache_graph");
            timeline.Add($"[T+006] `{config.Graphs.FormationCache}` rotated `{config.Collections.FormationPrimary}` and graph-defined tag exclusions ran before max/min summaries.");

            uint formationRevisionBeforeProbe = ReadCollectionRevision(engine, owner, config.Collections.FormationCacheResult);
            PressButton(engine, backend, GetBinding(bindings, config.Actions.CacheProbe), frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => ReadCollectionRevision(engine, owner, config.Collections.FormationCacheResult) == formationRevisionBeforeProbe,
                maxFrames: 30);
            uint formationRevisionAfterProbe = ReadCollectionRevision(engine, owner, config.Collections.FormationCacheResult);
            Assert.That(formationRevisionAfterProbe, Is.EqualTo(formationRevisionBeforeProbe));
            CaptureSnapshot(engine, uiRoot, ground, collections, config, snapshots, frames, screensDir, "retained_cache_probe");
            timeline.Add("[T+007] Cache probe reran graph materialization with stable inputs; retained diff kept the formation result revision unchanged.");

            short threatBeforePulse = relationships.GetMetric(owner, pressureTarget, tacticalIntelTypeId, threatMetricId);
            PressButton(engine, backend, GetBinding(bindings, config.Actions.PressurePulse), frameTimesMs);
            TickUntil(engine, frameTimesMs, () => relationships.GetMetric(owner, pressureTarget, tacticalIntelTypeId, threatMetricId) > threatBeforePulse, maxFrames: 30);
            short threatAfterPulse = relationships.GetMetric(owner, pressureTarget, tacticalIntelTypeId, threatMetricId);
            Assert.That(ReadSummaryInt(engine, owner, config.SummaryKeys.ThreatMax), Is.EqualTo(threatAfterPulse));
            CaptureSnapshot(engine, uiRoot, ground, collections, config, snapshots, frames, screensDir, "pressure_pulse_relation_update");
            timeline.Add($"[T+008] Pressure pulse mutated RelationshipRuntime only; rerun graph summaries reflected `{config.Scenario.PressurePulse.Metric}` {threatBeforePulse}->{threatAfterPulse}.");

            Assert.That(CountGroundOverlays(ground, GroundOverlayShape.Ring), Is.GreaterThanOrEqualTo(3));
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));

            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTraceJsonl(snapshots));
            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(timeline, snapshots, frameTimesMs));
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid());
            AcceptanceUiEvidenceWriter.WriteTimelineSheet(frames, screensDir, Path.Combine(screensDir, "timeline.png"), "Entity Query Tactics production screenshot flow");
            AcceptanceUiEvidenceWriter.WriteFiveWOneHMarkdown("entity-query-tactics-showcase", frames, Path.Combine(artifactDir, "5w1h.md"));
        }

        [Test]
        public void EntityQueryTactics_DemoPlayback_UsesProductionConfigPath()
        {
            using var engine = CreateEngine();
            EntityQueryTacticsShowcaseConfig config = LoadShowcaseConfig(engine);
            Assert.That(config.DemoPlayback.Enabled, Is.False, "Default showcase config must stay manual/playable; evidence automation is env-gated.");
            Assert.That(config.DemoPlayback.Steps, Is.Not.Empty);

            string activationEnv = config.DemoPlayback.ActivationEnv;
            string? previousActivation = Environment.GetEnvironmentVariable(activationEnv);
            Environment.SetEnvironmentVariable(activationEnv, "true");
            try
            {
                var frameTimesMs = new List<double>();
                LoadMap(engine, config.MapId, frameTimesMs);

                Entity owner = FindEntityByName(engine.World, config.Scenario.PlayerCommanderName);
                Entity pressureTarget = FindEntityByName(engine.World, config.Scenario.PressurePulse.TargetName);
                RelationshipRuntime relationships = engine.GetService(CoreServiceKeys.RelationshipRuntime)
                    ?? throw new InvalidOperationException("RelationshipRuntime missing.");
                RelationshipTypeRegistry relationshipTypes = engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
                    ?? throw new InvalidOperationException("RelationshipTypeRegistry missing.");
                RelationshipMetricRegistry relationshipMetrics = engine.GetService(CoreServiceKeys.RelationshipMetricRegistry)
                    ?? throw new InvalidOperationException("RelationshipMetricRegistry missing.");
                int relationshipTypeId = relationshipTypes.GetId(config.Relationships.TacticalIntel);
                int threatMetricId = relationshipMetrics.GetId(config.Scenario.PressurePulse.Metric);

                TickUntil(
                    engine,
                    frameTimesMs,
                    () =>
                        ReadCollectionSnapshot(engine, owner, config.Collections.CommandSourceMirror, required: false).Count == config.Scenario.Allies.Length &&
                        ReadSummaryInt(engine, owner, config.SummaryKeys.FormationCount) > 0 &&
                        ReadSummaryInt(engine, owner, config.SummaryKeys.ThreatMax) == relationships.GetMetric(owner, pressureTarget, relationshipTypeId, threatMetricId),
                    maxFrames: 360,
                    diagnostics: () =>
                        $"ui={ReadCollectionSnapshot(engine, owner, config.Collections.UiBox, required: false).Count}, " +
                        $"commandSource={ReadCollectionSnapshot(engine, owner, config.Collections.CommandSourceMirror, required: false).Count}, " +
                        $"formation={ReadSummaryInt(engine, owner, config.SummaryKeys.FormationCount)}, " +
                        $"threatMax={ReadSummaryInt(engine, owner, config.SummaryKeys.ThreatMax)}, " +
                        $"targetThreat={relationships.GetMetric(owner, pressureTarget, relationshipTypeId, threatMetricId)}");

                AssertCollectionCount(engine, owner, config.Collections.UiBox, config.Scenario.Allies.Length);
                AssertCollectionCount(engine, owner, config.Collections.CommandSourceMirror, config.Scenario.Allies.Length);
                AssertCollectionCount(engine, owner, config.Collections.FormationPrimary, config.Scenario.Allies.Length);

                GraphConfig selectedGraphConfig = LoadGraphConfig(engine, config.Graphs.SelectedFriendlies);
                GraphConfig hostileGraphConfig = LoadGraphConfig(engine, config.Graphs.HostileThreats);
                GraphConfig formationGraphConfig = LoadGraphConfig(engine, config.Graphs.FormationCache);
                EntityCollectionSnapshot selected = ReadCollectionSnapshot(engine, owner, config.Collections.SelectedFriendliesResult);
                EntityCollectionSnapshot hostile = ReadCollectionSnapshot(engine, owner, config.Collections.HostileThreatResult);
                EntityCollectionSnapshot formation = ReadCollectionSnapshot(engine, owner, config.Collections.FormationCacheResult);

                AssertSelectedFriendliesResult(engine, selected, config, selectedGraphConfig);
                AssertHostileThreatResult(engine, relationships, owner, hostile, relationshipTypeId, threatMetricId, config, hostileGraphConfig);
                AssertFormationResult(engine, formation, config, formationGraphConfig);
                Assert.That(ReadSummaryInt(engine, owner, config.SummaryKeys.SelectedCount), Is.EqualTo(selected.Count));
                Assert.That(ReadSummaryInt(engine, owner, config.SummaryKeys.ThreatCount), Is.EqualTo(hostile.Count));
                Assert.That(ReadSummaryInt(engine, owner, config.SummaryKeys.FormationCount), Is.EqualTo(formation.Count));
                Assert.That(ReadSummaryInt(engine, owner, config.SummaryKeys.ThreatSum), Is.EqualTo(SumRelationshipMetric(relationships, owner, hostile.Entities, relationshipTypeId, threatMetricId)));
                Assert.That(ReadSummaryInt(engine, owner, config.SummaryKeys.ThreatMax), Is.EqualTo(relationships.GetMetric(owner, pressureTarget, relationshipTypeId, threatMetricId)));
                Assert.That(ReadSummaryFloat(engine, owner, config.SummaryKeys.FormationMaxCommandPower), Is.EqualTo(MaxAttribute(engine, formation.Entities, config.Attributes.CommandPower)));
                Assert.That(ReadSummaryEntity(engine, owner, config.SummaryKeys.ThreatBestEntity), Is.EqualTo(pressureTarget));
                Assert.That(ReadCollectionRevision(engine, owner, config.Collections.FormationCacheResult), Is.GreaterThan(0));
                Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            }
            finally
            {
                Environment.SetEnvironmentVariable(activationEnv, previousActivation);
            }
        }

        [Test]
        public void EntityQueryTactics_ProductionBenchmark_WritesReport()
        {
            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "benchmarks", "entity-query-tactics-showcase");
            Directory.CreateDirectory(artifactDir);

            using var engine = CreateEngineWithPlan(out LauncherLaunchPlan launchPlan);
            EntityQueryTacticsShowcaseConfig config = LoadShowcaseConfig(engine);
            IReadOnlyDictionary<string, string> bindings = LoadInputBindings(engine);
            var backend = GetInputBackend(engine);
            var frameTimesMs = new List<double>();
            LoadMap(engine, config.MapId, frameTimesMs);

            Entity owner = FindEntityByName(engine.World, config.Scenario.PlayerCommanderName);
            Entity pressureTarget = FindEntityByName(engine.World, config.Scenario.PressurePulse.TargetName);
            string[] friendlyNames = config.Scenario.Allies.Select(static actor => actor.Name).ToArray();
            DragSelectNamed(engine, backend, frameTimesMs, friendlyNames);

            PressButton(engine, backend, GetBinding(bindings, config.Actions.CommitSelection), frameTimesMs);
            PressButton(engine, backend, GetBinding(bindings, config.Actions.ExecuteGraphs), frameTimesMs);
            PressButton(engine, backend, GetBinding(bindings, config.Actions.RotateFormation), frameTimesMs);

            GraphReturnWriter writer = engine.GetService(CoreServiceKeys.GraphReturnWriter)
                ?? throw new InvalidOperationException("GraphReturnWriter missing.");
            IGraphRuntimeApi api = CreateGraphRuntimeApi(engine);
            var graphIds = new[]
            {
                GraphIdRegistry.GetId(config.Graphs.SelectedFriendlies),
                GraphIdRegistry.GetId(config.Graphs.HostileThreats),
                GraphIdRegistry.GetId(config.Graphs.FormationCache)
            };
            Assert.That(graphIds, Is.All.GreaterThan(0));

            RelationshipRuntime relationships = engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("RelationshipRuntime missing.");
            RelationshipTypeRegistry relationshipTypes = engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
                ?? throw new InvalidOperationException("RelationshipTypeRegistry missing.");
            RelationshipMetricRegistry relationshipMetrics = engine.GetService(CoreServiceKeys.RelationshipMetricRegistry)
                ?? throw new InvalidOperationException("RelationshipMetricRegistry missing.");
            RelationshipChangeBuffer relationshipChanges = engine.GetService(CoreServiceKeys.RelationshipChangeBuffer)
                ?? throw new InvalidOperationException("RelationshipChangeBuffer missing.");
            RelationshipReasonRegistry reasons = engine.GetService(CoreServiceKeys.RelationshipReasonRegistry)
                ?? throw new InvalidOperationException("RelationshipReasonRegistry missing.");
            int tacticalIntelTypeId = relationshipTypes.GetId(config.Relationships.TacticalIntel);
            int pressureMetricId = relationshipMetrics.GetId(config.Scenario.PressurePulse.Metric);
            int pressureReasonId = reasons.Register("Benchmark.PressurePulse");
            GraphConfig selectedGraphConfig = LoadGraphConfig(engine, config.Graphs.SelectedFriendlies);
            GraphConfig hostileGraphConfig = LoadGraphConfig(engine, config.Graphs.HostileThreats);
            GraphConfig formationGraphConfig = LoadGraphConfig(engine, config.Graphs.FormationCache);
            GraphOutputSchemaRegistry schemas = engine.GetService(CoreServiceKeys.GraphOutputSchemaRegistry)
                ?? throw new InvalidOperationException("GraphOutputSchemaRegistry missing.");
            int outputBindingCount = CountOutputBindings(schemas, graphIds);

            const int warmupGraphIterations = 8_000;
            for (int i = 0; i < warmupGraphIterations; i++)
            {
                ExecuteProductionGraphs(writer, graphIds, owner, api, (uint)(i + 1));
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();

            const int stabilizationGraphIterations = 5_000;
            for (int i = 0; i < stabilizationGraphIterations; i++)
            {
                ExecuteProductionGraphs(writer, graphIds, owner, api, (uint)(i + 9000));
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();

            const int graphIterations = 20_000;
            HotPathMeasurement graphMeasurement = MeasureStableZeroAlloc(
                graphIterations,
                "GraphReturnWriter execute x3 stable inputs",
                iteration => ExecuteProductionGraphs(writer, graphIds, owner, api, (uint)(iteration + 1000)));
            long graphAllocated = graphMeasurement.AllocatedBytes;
            double graphTotalMs = graphMeasurement.TotalMs;
            uint graphRevisionChecksum = ReadCollectionRevision(engine, owner, config.Collections.FormationCacheResult);

            SingleGraphHotPathMeasurement[] singleGraphMeasurements = new SingleGraphHotPathMeasurement[graphIds.Length];
            for (int i = 0; i < graphIds.Length; i++)
            {
                int graphId = graphIds[i];
                HotPathMeasurement measurement = MeasureStableZeroAlloc(
                    graphIterations,
                    $"GraphReturnWriter execute single graph {GraphIdRegistry.GetName(graphId)}",
                    iteration => writer.ExecuteAndWrite(graphId, owner, owner, Entity.Null, Entity.Null, default, (uint)(iteration + 20000 + i), api));
                singleGraphMeasurements[i] = new SingleGraphHotPathMeasurement(GraphIdRegistry.GetName(graphId), measurement);
            }

            EntityCollectionSnapshot beforeStableProbe = ReadCollectionSnapshot(engine, owner, config.Collections.FormationCacheResult);
            const int cacheProbeIterations = 2_000;
            HotPathMeasurement cacheMeasurement = MeasureStableZeroAlloc(
                cacheProbeIterations,
                "Retained diff execute x3 stable inputs",
                iteration => ExecuteProductionGraphs(writer, graphIds, owner, api, (uint)(iteration + 30000)));
            long cacheAllocated = cacheMeasurement.AllocatedBytes;
            double cacheTotalMs = cacheMeasurement.TotalMs;
            EntityCollectionSnapshot afterStableProbe = ReadCollectionSnapshot(engine, owner, config.Collections.FormationCacheResult);
            int stableRevisionCount = afterStableProbe.Revision == beforeStableProbe.Revision ? cacheProbeIterations : 0;

            const int pressureIterations = 1_000;
            relationshipChanges.Clear();
            int pressureChangeCountBefore = relationshipChanges.Count;
            int pressureChangeCapacityBefore = relationshipChanges.Capacity;
            int pressureResizeCountBefore = relationshipChanges.ResizeCount;
            HotPathMeasurement pressureMeasurement = MeasureStableZeroAlloc(
                pressureIterations,
                "Relationship AddMetric + graph execute x3",
                iteration =>
                {
                    int delta = (iteration & 1) == 0 ? config.Scenario.PressurePulse.Delta : -config.Scenario.PressurePulse.Delta;
                    relationships.AddMetric(owner, pressureTarget, tacticalIntelTypeId, pressureMetricId, delta, pressureReasonId);
                    ExecuteProductionGraphs(writer, graphIds, owner, api, (uint)(iteration + 50000));
                },
                relationshipChanges.Clear);
            long pressureAllocated = pressureMeasurement.AllocatedBytes;
            double pressureTotalMs = pressureMeasurement.TotalMs;
            int pressureChangeCountAfter = relationshipChanges.Count;
            int pressureChangeCapacityAfter = relationshipChanges.Capacity;
            int pressureResizeCountAfter = relationshipChanges.ResizeCount;
            int pressureThreatMax = ReadSummaryInt(engine, owner, config.SummaryKeys.ThreatMax);

            EntityCollectionSnapshot beforeRotationInput = ReadCollectionSnapshot(engine, owner, config.Collections.FormationPrimary);
            EntityCollectionSnapshot beforeRotationOutput = ReadCollectionSnapshot(engine, owner, config.Collections.FormationCacheResult);
            PressButton(engine, backend, GetBinding(bindings, config.Actions.RotateFormation), frameTimesMs);
            EntityCollectionSnapshot afterRotationInput = ReadCollectionSnapshot(engine, owner, config.Collections.FormationPrimary);
            EntityCollectionSnapshot afterRotationOutput = ReadCollectionSnapshot(engine, owner, config.Collections.FormationCacheResult);

            int threatBeforeProductionPulse = ReadSummaryInt(engine, owner, config.SummaryKeys.ThreatMax);
            ProductionTickBenchmark tickBenchmark = MeasureProductionTickLoop(engine, backend, bindings, config, frameTimesMs, iterations: 360);
            int threatAfterProductionPulse = ReadSummaryInt(engine, owner, config.SummaryKeys.ThreatMax);

            Assert.That(graphRevisionChecksum, Is.GreaterThan(0));
            Assert.That(stableRevisionCount, Is.EqualTo(cacheProbeIterations));
            Assert.That(pressureThreatMax, Is.GreaterThan(0));
            Assert.That(graphAllocated, Is.EqualTo(0), "GraphReturnWriter hot path must stay 0Alloc after warmup and stabilization.");
            Assert.That(singleGraphMeasurements.Select(static x => x.Measurement.AllocatedBytes), Is.All.EqualTo(0), "Each configured graph must stay 0Alloc when measured independently.");
            Assert.That(cacheAllocated, Is.EqualTo(0), "Retained cache probe hot path must stay 0Alloc for stable graph inputs.");
            Assert.That(pressureAllocated, Is.EqualTo(0), "Relationship pressure pulse plus graph hot path must stay 0Alloc.");
            Assert.That(pressureResizeCountAfter, Is.EqualTo(pressureResizeCountBefore), "Relationship pressure benchmark must not hide allocation behind RelationshipChangeBuffer resize.");
            Assert.That(pressureChangeCapacityAfter, Is.EqualTo(pressureChangeCapacityBefore), "Relationship pressure benchmark must stay inside the preallocated change buffer.");
            Assert.That(beforeStableProbe.Revision, Is.EqualTo(afterStableProbe.Revision));
            Assert.That(beforeStableProbe.Signature, Is.EqualTo(afterStableProbe.Signature));
            Assert.That(afterRotationInput.Revision, Is.GreaterThan(beforeRotationInput.Revision), "Production formation input should change after the configured rotate action.");
            Assert.That(afterRotationOutput.Signature, Is.EqualTo(beforeRotationOutput.Signature), "Sorted formation graph output should retain the same signature after an order-only source rotation.");
            Assert.That(threatAfterProductionPulse, Is.GreaterThanOrEqualTo(threatBeforeProductionPulse));

            var report = BuildBenchmarkReport(
                launchPlan,
                config,
                selectedGraphConfig,
                hostileGraphConfig,
                formationGraphConfig,
                graphIds,
                outputBindingCount,
                graphIterations,
                graphTotalMs,
                graphAllocated,
                graphMeasurement.StabilizationAttempts,
                singleGraphMeasurements,
                cacheProbeIterations,
                cacheTotalMs,
                cacheAllocated,
                cacheMeasurement.StabilizationAttempts,
                stableRevisionCount,
                beforeStableProbe,
                afterStableProbe,
                beforeRotationInput,
                afterRotationInput,
                beforeRotationOutput,
                afterRotationOutput,
                pressureIterations,
                pressureTotalMs,
                pressureAllocated,
                pressureMeasurement.StabilizationAttempts,
                pressureChangeCountBefore,
                pressureChangeCountAfter,
                pressureChangeCapacityBefore,
                pressureChangeCapacityAfter,
                pressureResizeCountAfter - pressureResizeCountBefore,
                tickBenchmark,
                threatBeforeProductionPulse,
                threatAfterProductionPulse,
                warmupGraphIterations,
                stabilizationGraphIterations,
                ComputeShowcaseAssetHashes());
            File.WriteAllText(Path.Combine(artifactDir, "benchmark-report.md"), report);
            Console.WriteLine(report);
        }

        private static GameEngine CreateEngine()
        {
            return CreateEngineWithPlan(out _);
        }

        private static GameEngine CreateEngineWithPlan(out LauncherLaunchPlan launchPlan)
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            launchPlan = ResolveShowcaseLaunchPlan(repoRoot);
            var modPaths = launchPlan.Mods.Select(static mod => mod.RootPath).ToList();

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);

            AcceptanceUiHostInstaller.Install(engine);

            var view = new StubViewController(1920f, 1080f);
            engine.SetService(CoreServiceKeys.ViewController, view);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, new WorldMappedScreenRayProvider());
            engine.SetService(CoreServiceKeys.ScreenProjector, new WorldMappedScreenProjector());

            var culling = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, view, cullingConfig: engine.MergedConfig.Presentation.CameraCulling);
            engine.RegisterPresentationSystem(culling);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);

            engine.Start();
            return engine;
        }

        private static LauncherLaunchPlan ResolveShowcaseLaunchPlan(string repoRoot)
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), $"ludots-entity-query-tactics-launcher-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);
            try
            {
                string preferencesPath = Path.Combine(tempDirectory, "preferences.json");
                string userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");

                var launcher = new LauncherService(
                    repoRoot,
                    Path.Combine(repoRoot, "launcher.config.json"),
                    Path.Combine(repoRoot, "launcher.presets.json"),
                    preferencesPath,
                    userConfigPath);
                LauncherResolveResult resolve = launcher.Resolve(new[] { $"preset:{ShowcasePresetId}" }, LauncherPlatformIds.Raylib, LauncherBuildMode.Never);
                Assert.That(resolve.Plan.RootModIds, Does.Contain("EntityQueryTacticsShowcaseMod"));
                Assert.That(resolve.Plan.OrderedModIds, Does.Contain("LudotsCoreMod"));
                Assert.That(resolve.Plan.OrderedModIds, Does.Contain("CoreInputMod"));
                Assert.That(resolve.Plan.OrderedModIds, Does.Contain("NarrativeFrontendMod"));
                EnsureShowcaseAssemblyFresh(repoRoot);
                return resolve.Plan;
            }
            finally
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        private static void EnsureShowcaseAssemblyFresh(string repoRoot)
        {
            lock (ShowcaseBuildLock)
            {
                if (!string.Equals(_showcaseBuildRoot, repoRoot, StringComparison.OrdinalIgnoreCase) ||
                    !IsShowcaseAssemblyFresh(repoRoot))
                {
                    BuildShowcaseAssembly(repoRoot);
                    _showcaseBuildRoot = repoRoot;
                }
            }

            Assert.That(IsShowcaseAssemblyFresh(repoRoot), Is.True, "Production launcher-loaded showcase assembly must be fresh relative to its source.");
        }

        private static bool IsShowcaseAssemblyFresh(string repoRoot)
        {
            string assemblyPath = GetShowcaseAssemblyPath(repoRoot);
            return File.Exists(assemblyPath) &&
                   File.GetLastWriteTimeUtc(assemblyPath) >= GetLatestShowcaseBuildInputTimeUtc(repoRoot);
        }

        private static void BuildShowcaseAssembly(string repoRoot)
        {
            string projectPath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "entity_query_tactics",
                "EntityQueryTacticsShowcaseMod",
                "EntityQueryTacticsShowcaseMod.csproj");
            string projectDirectory = Path.GetDirectoryName(projectPath)
                ?? throw new InvalidOperationException("Showcase project directory missing.");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(
                    "dotnet",
                    $"build \"{projectPath}\" /p:ProduceReferenceAssembly=true -c Release --no-restore")
                {
                    WorkingDirectory = projectDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };

            var output = new StringBuilder();
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data != null)
                {
                    output.AppendLine(args.Data);
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data != null)
                {
                    output.AppendLine(args.Data);
                }
            };

            Assert.That(process.Start(), Is.True, "Failed to start showcase mod build.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            Assert.That(process.WaitForExit(120_000), Is.True, $"Showcase mod build timed out.{Environment.NewLine}{output}");
            Assert.That(process.ExitCode, Is.EqualTo(0), output.ToString());
        }

        private static DateTime GetLatestShowcaseBuildInputTimeUtc(string repoRoot)
        {
            string modRoot = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "entity_query_tactics",
                "EntityQueryTacticsShowcaseMod");
            if (!Directory.Exists(modRoot))
            {
                throw new DirectoryNotFoundException(modRoot);
            }

            DateTime latest = DateTime.MinValue;
            foreach (string path in Directory.EnumerateFiles(modRoot, "*.cs", SearchOption.AllDirectories)
                         .Where(path => IsAuthoredShowcaseSourcePath(modRoot, path))
                         .Concat(Directory.EnumerateFiles(modRoot, "*.csproj", SearchOption.TopDirectoryOnly)))
            {
                DateTime writeTime = File.GetLastWriteTimeUtc(path);
                if (writeTime > latest)
                {
                    latest = writeTime;
                }
            }

            return latest;
        }

        private static bool IsAuthoredShowcaseSourcePath(string modRoot, string path)
        {
            string relativePath = Path.GetRelativePath(modRoot, path);
            return !relativePath
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment =>
                    string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
        }

        private static string GetShowcaseAssemblyPath(string repoRoot)
        {
            return Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "entity_query_tactics",
                "EntityQueryTacticsShowcaseMod",
                "bin",
                "net8.0",
                "EntityQueryTacticsShowcaseMod.dll");
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var backend = new TestInputBackend();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.GlobalContext[InputBackendKey] = backend;
        }

        private static TestInputBackend GetInputBackend(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(InputBackendKey, out object? backendObj) &&
                   backendObj is TestInputBackend backend
                ? backend
                : throw new InvalidOperationException("Entity query tactics test input backend missing.");
        }

        private static void LoadMap(GameEngine engine, string mapId, List<double> frameTimesMs, int frames = 8)
        {
            engine.LoadMap(mapId);
            Assert.That(engine.CurrentMapSession, Is.Not.Null, $"{mapId} should create a live map session.");
            Tick(engine, frames, frameTimesMs);
        }

        private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
        {
            for (int i = 0; i < frames; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
                frameTimesMs.Add((Stopwatch.GetTimestamp() - t0) * 1000d / Stopwatch.Frequency);
            }
        }

        private static void TickForPresentationSync(GameEngine engine)
        {
            for (int i = 0; i < 2; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
            }
        }

        private static void TickUntil(
            GameEngine engine,
            List<double> frameTimesMs,
            Func<bool> predicate,
            int maxFrames,
            Func<string>? diagnostics = null)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (predicate())
                {
                    return;
                }

                Tick(engine, 1, frameTimesMs);
            }

            Assert.That(
                predicate(),
                Is.True,
                $"Condition was not satisfied within {maxFrames} frames.{Environment.NewLine}{diagnostics?.Invoke()}");
        }

        private static void PressButton(GameEngine engine, TestInputBackend backend, string devicePath, List<double> frameTimesMs)
        {
            backend.SetButton(devicePath, true);
            Tick(engine, 2, frameTimesMs);
            backend.SetButton(devicePath, false);
            Tick(engine, 2, frameTimesMs);
        }

        private static ProductionTickBenchmark MeasureProductionTickLoop(
            GameEngine engine,
            TestInputBackend backend,
            IReadOnlyDictionary<string, string> bindings,
            EntityQueryTacticsShowcaseConfig config,
            List<double> frameTimesMs,
            int iterations)
        {
            string commitPath = GetBinding(bindings, config.Actions.CommitSelection);
            string executePath = GetBinding(bindings, config.Actions.ExecuteGraphs);
            string rotatePath = GetBinding(bindings, config.Actions.RotateFormation);
            string pressurePath = GetBinding(bindings, config.Actions.PressurePulse);
            string cachePath = GetBinding(bindings, config.Actions.CacheProbe);
            var sampledFrameTimes = new double[iterations];
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            int actionFrames = 0;

            for (int i = 0; i < iterations; i++)
            {
                string? path = (i % 12) switch
                {
                    0 => commitPath,
                    1 => executePath,
                    3 => cachePath,
                    6 => pressurePath,
                    9 => rotatePath,
                    _ => null
                };

                if (path != null)
                {
                    backend.SetButton(path, true);
                    actionFrames++;
                }

                long frameStart = Stopwatch.GetTimestamp();
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
                double frameMs = Stopwatch.GetElapsedTime(frameStart, Stopwatch.GetTimestamp()).TotalMilliseconds;
                frameTimesMs.Add(frameMs);
                sampledFrameTimes[i] = frameMs;

                if (path != null)
                {
                    backend.SetButton(path, false);
                }
            }

            long stopped = Stopwatch.GetTimestamp();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Array.Sort(sampledFrameTimes);
            double median = sampledFrameTimes.Length == 0 ? 0d : sampledFrameTimes[sampledFrameTimes.Length / 2];
            double p95 = sampledFrameTimes.Length == 0 ? 0d : sampledFrameTimes[Math.Min(sampledFrameTimes.Length - 1, (int)Math.Ceiling(sampledFrameTimes.Length * 0.95d) - 1)];
            double max = sampledFrameTimes.Length == 0 ? 0d : sampledFrameTimes[^1];

            return new ProductionTickBenchmark(
                iterations,
                actionFrames,
                Stopwatch.GetElapsedTime(started, stopped).TotalMilliseconds,
                median,
                p95,
                max,
                allocated);
        }

        private static IGraphRuntimeApi CreateGraphRuntimeApi(GameEngine engine)
        {
            return GasGraphRuntimeApi.CreateProduction(
                engine.World,
                engine.SpatialQueries,
                engine.SpatialCoords,
                engine.EventBus,
                engine.GetService(CoreServiceKeys.EffectRequestQueue),
                engine.GlobalContext);
        }

        private static void ExecuteProductionGraphs(
            GraphReturnWriter writer,
            int[] graphIds,
            Entity owner,
            IGraphRuntimeApi api,
            uint seed)
        {
            IntVector2 targetPos = default;
            for (int i = 0; i < graphIds.Length; i++)
            {
                writer.ExecuteAndWrite(graphIds[i], owner, owner, Entity.Null, Entity.Null, targetPos, seed + (uint)i, api);
            }
        }

        private static HotPathMeasurement MeasureStableZeroAlloc(
            int iterations,
            string pathName,
            Action<int> action,
            Action? resetBeforeAttempt = null)
        {
            const int maxAttempts = 6;
            HotPathMeasurement last = default;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                resetBeforeAttempt?.Invoke();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                GC.GetAllocatedBytesForCurrentThread();

                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < iterations; i++)
                {
                    action(i);
                }

                long stop = Stopwatch.GetTimestamp();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                last = new HotPathMeasurement(Stopwatch.GetElapsedTime(start, stop).TotalMilliseconds, allocated, attempt);
                if (allocated == 0)
                {
                    return last;
                }
            }

            Assert.Fail($"{pathName} did not stabilize to 0Alloc after {maxAttempts} measured attempts; last allocation was {last.AllocatedBytes} bytes.");
            return last;
        }

        private static void DragSelectNamed(GameEngine engine, TestInputBackend backend, List<double> frameTimesMs, params string[] names)
        {
            Assert.That(names, Is.Not.Null.And.Not.Empty);

            Vector2[] points = names.Select(name => GetEntityScreen(engine, name)).ToArray();
            float minX = points.Min(p => p.X) - 40f;
            float minY = points.Min(p => p.Y) - 40f;
            float maxX = points.Max(p => p.X) + 40f;
            float maxY = points.Max(p => p.Y) + 40f;

            backend.SetMousePosition(new Vector2(minX, minY));
            Tick(engine, 1, frameTimesMs);
            backend.SetButton("<Mouse>/LeftButton", true);
            Tick(engine, 2, frameTimesMs);
            backend.SetMousePosition(new Vector2(maxX, maxY));
            Tick(engine, 2, frameTimesMs);
            backend.SetButton("<Mouse>/LeftButton", false);
            Tick(engine, 3, frameTimesMs);
        }

        private static Vector2 GetEntityScreen(GameEngine engine, string name)
        {
            Entity entity = FindEntityByName(engine.World, name);
            var projector = engine.GetService(CoreServiceKeys.ScreenProjector)
                ?? throw new InvalidOperationException("ScreenProjector was not installed.");
            ref WorldPositionCm position = ref engine.World.Get<WorldPositionCm>(entity);
            return projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(position.Value, yMeters: 0f));
        }

        private static void CaptureSnapshot(
            GameEngine engine,
            UIRoot uiRoot,
            GroundOverlayBuffer ground,
            EntityCollectionStore collections,
            EntityQueryTacticsShowcaseConfig config,
            List<AcceptanceSnapshot> snapshots,
            List<UiAcceptanceEvidenceFrame> frames,
            string screensDir,
            string step)
        {
            TickForPresentationSync(engine);
            Entity owner = FindEntityByName(engine.World, config.Scenario.PlayerCommanderName);
            string battlefieldFileName = $"battlefield_{snapshots.Count + 1:000}_{step}.png";
            string battlefieldPath = Path.Combine(screensDir, battlefieldFileName);
            WriteBattlefieldEvidence(engine, collections, config, owner, battlefieldPath, step);
            var frame = AcceptanceUiEvidenceWriter.CaptureFrame(
                uiRoot,
                screensDir,
                snapshots.Count + 1,
                step,
                GetWhen(step),
                "Blue Lance player, selected friendlies, hostile threat board, and retained collection cache",
                GetWhat(step),
                config.MapId,
                "Prove the latest selection/collection/query graph architecture is playable through production mod wiring.",
                "Run the real engine tick loop, drive input through PlayerInputHandler, and snapshot UIRoot plus collection telemetry.");
            AcceptanceUiEvidenceWriter.ExportUiScene(
                uiRoot,
                Path.Combine(screensDir, frame.ScreenshotFileName),
                "#060B12",
                canvas => DrawBattlefieldEvidence(canvas, engine, collections, config, owner, step, mutedForHud: true));
            frames.Add(frame);

            snapshots.Add(new AcceptanceSnapshot(
                Step: step,
                ScreenshotFileName: frame.ScreenshotFileName,
                BattlefieldFileName: battlefieldFileName,
                UiBoxRevision: ReadCollectionRevision(engine, owner, config.Collections.UiBox),
                CommandSourceRevision: ReadCollectionRevision(engine, owner, config.Collections.CommandSourceMirror),
                FormationRevision: ReadCollectionRevision(engine, owner, config.Collections.FormationCacheResult),
                HostileRevision: ReadCollectionRevision(engine, owner, config.Collections.HostileThreatResult),
                SelectedNames: ReadCollectionNames(engine, owner, config.Collections.SelectedFriendliesResult),
                FormationNames: ReadCollectionNames(engine, owner, config.Collections.FormationCacheResult),
                ThreatNames: ReadCollectionNames(engine, owner, config.Collections.HostileThreatResult),
                SelectedCount: ReadSummaryInt(engine, owner, config.SummaryKeys.SelectedCount),
                ThreatMax: ReadSummaryInt(engine, owner, config.SummaryKeys.ThreatMax),
                FormationCount: ReadSummaryInt(engine, owner, config.SummaryKeys.FormationCount),
                GroundRingCount: CountGroundOverlays(ground, GroundOverlayShape.Ring),
                UiText: AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot).Take(40).ToArray()));
        }

        private static string GetWhen(string step)
        {
            return step switch
            {
                "map_loaded" => "T+001",
                "ui_box_acquisition_only" => "T+002",
                "command_source_confirmed" => "T+003",
                "selected_friendlies_graph" => "T+004",
                "hostile_relation_graph" => "T+005",
                "formation_cache_graph" => "T+006",
                "retained_cache_probe" => "T+007",
                "pressure_pulse_relation_update" => "T+008",
                _ => "T+000"
            };
        }

        private static string GetWhat(string step)
        {
            return step switch
            {
                "map_loaded" => "Boot the showcase and confirm the production HUD is mounted.",
                "ui_box_acquisition_only" => "Drag-select friendlies and observe UI acquisition plus command source publishing.",
                "command_source_confirmed" => "Use the configured commit action to confirm the command source and refresh formation.",
                "selected_friendlies_graph" => "Run graph query from UI box collection and inspect filters, sorting, aggregate, and extreme output.",
                "hostile_relation_graph" => "Inspect relation metric and flag graph output over hostile entities.",
                "formation_cache_graph" => "Rotate formation cache and prove routed units are filtered out.",
                "retained_cache_probe" => "Rerun graph with stable inputs and verify retained diff revision stability.",
                "pressure_pulse_relation_update" => "Mutate RelationshipRuntime metric and prove graph output updates.",
                _ => "Capture entity query tactics state."
            };
        }

        private static void WriteBattlefieldEvidence(
            GameEngine engine,
            EntityCollectionStore collections,
            EntityQueryTacticsShowcaseConfig config,
            Entity owner,
            string outputPath,
            string step)
        {
            using var surface = SKSurface.Create(new SKImageInfo(1920, 1080));
            DrawBattlefieldEvidence(surface.Canvas, engine, collections, config, owner, step, mutedForHud: false);
            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            data.SaveTo(stream);
        }

        private static void DrawBattlefieldEvidence(
            SKCanvas canvas,
            GameEngine engine,
            EntityCollectionStore collections,
            EntityQueryTacticsShowcaseConfig config,
            Entity owner,
            string step,
            bool mutedForHud)
        {
            const float left = 510f;
            const float top = 210f;
            const float width = 900f;
            const float height = 620f;

            EntityCollectionSnapshot uiBox = ReadCollectionSnapshot(engine, owner, config.Collections.UiBox, required: false);
            EntityCollectionSnapshot formal = ReadCollectionSnapshot(engine, owner, config.Collections.CommandSourceMirror, required: false);
            EntityCollectionSnapshot formationInput = ReadCollectionSnapshot(engine, owner, config.Collections.FormationPrimary, required: false);
            EntityCollectionSnapshot selected = ReadCollectionSnapshot(engine, owner, config.Collections.SelectedFriendliesResult, required: false);
            EntityCollectionSnapshot hostile = ReadCollectionSnapshot(engine, owner, config.Collections.HostileThreatResult, required: false);
            EntityCollectionSnapshot formation = ReadCollectionSnapshot(engine, owner, config.Collections.FormationCacheResult, required: false);

            using var bgPaint = new SKPaint { Color = mutedForHud ? new SKColor(8, 13, 22, 190) : new SKColor(5, 10, 17), IsAntialias = true };
            using var gridPaint = new SKPaint { Color = mutedForHud ? new SKColor(80, 105, 120, 45) : new SKColor(37, 58, 70), StrokeWidth = 1f, IsAntialias = true };
            using var lanePaint = new SKPaint { Color = mutedForHud ? new SKColor(58, 78, 62, 42) : new SKColor(18, 50, 37), StrokeWidth = 7f, IsAntialias = true };
            using var textPaint = new SKPaint { Color = mutedForHud ? new SKColor(223, 240, 246, 150) : new SKColor(229, 246, 252), TextSize = 18f, IsAntialias = true };
            using var faintTextPaint = new SKPaint { Color = mutedForHud ? new SKColor(150, 170, 182, 120) : new SKColor(155, 174, 186), TextSize = 14f, IsAntialias = true };
            using var blueFill = new SKPaint { Color = mutedForHud ? new SKColor(65, 150, 255, 125) : new SKColor(74, 160, 255), IsAntialias = true };
            using var redFill = new SKPaint { Color = mutedForHud ? new SKColor(255, 86, 105, 125) : new SKColor(255, 92, 112), IsAntialias = true };
            using var objectiveFill = new SKPaint { Color = mutedForHud ? new SKColor(250, 205, 82, 130) : new SKColor(250, 205, 82), IsAntialias = true };
            using var neutralFill = new SKPaint { Color = mutedForHud ? new SKColor(160, 178, 190, 100) : new SKColor(160, 178, 190), IsAntialias = true };
            using var selectedStroke = new SKPaint { Color = mutedForHud ? new SKColor(110, 231, 183, 170) : new SKColor(110, 231, 183), Style = SKPaintStyle.Stroke, StrokeWidth = 5f, IsAntialias = true };
            using var formalStroke = new SKPaint { Color = mutedForHud ? new SKColor(96, 165, 250, 140) : new SKColor(96, 165, 250), Style = SKPaintStyle.Stroke, StrokeWidth = 3f, IsAntialias = true };
            using var threatStroke = new SKPaint { Color = mutedForHud ? new SKColor(251, 113, 133, 170) : new SKColor(251, 113, 133), Style = SKPaintStyle.Stroke, StrokeWidth = 5f, IsAntialias = true };
            using var formationStroke = new SKPaint { Color = mutedForHud ? new SKColor(167, 139, 250, 165) : new SKColor(167, 139, 250), Style = SKPaintStyle.Stroke, StrokeWidth = 3f, IsAntialias = true };
            using var formationPathPaint = new SKPaint { Color = mutedForHud ? new SKColor(196, 181, 253, 165) : new SKColor(196, 181, 253), Style = SKPaintStyle.Stroke, StrokeWidth = 6f, IsAntialias = true };
            using var boxPaint = new SKPaint { Color = mutedForHud ? new SKColor(96, 165, 250, 80) : new SKColor(96, 165, 250, 60), Style = SKPaintStyle.Fill, IsAntialias = true };
            using var boxStroke = new SKPaint { Color = mutedForHud ? new SKColor(96, 165, 250, 165) : new SKColor(96, 165, 250, 220), Style = SKPaintStyle.Stroke, StrokeWidth = 2f, IsAntialias = true };

            canvas.DrawRect(new SKRect(0, 0, 1920, 1080), bgPaint);
            SKRect field = new(left, top, left + width, top + height);
            canvas.DrawRoundRect(field, 22f, 22f, new SKPaint { Color = mutedForHud ? new SKColor(11, 26, 28, 130) : new SKColor(11, 26, 28), IsAntialias = true });
            canvas.DrawRoundRect(field, 22f, 22f, new SKPaint { Color = mutedForHud ? new SKColor(47, 78, 82, 95) : new SKColor(47, 78, 82), Style = SKPaintStyle.Stroke, StrokeWidth = 2f, IsAntialias = true });

            for (int i = 1; i < 8; i++)
            {
                float x = field.Left + (field.Width * i / 8f);
                float y = field.Top + (field.Height * i / 8f);
                canvas.DrawLine(x, field.Top + 18f, x, field.Bottom - 18f, gridPaint);
                canvas.DrawLine(field.Left + 18f, y, field.Right - 18f, y, gridPaint);
            }

            canvas.DrawLine(WorldToField(1000f, 920f, field), WorldToField(2450f, 920f, field), lanePaint);
            canvas.DrawText("LIVE TACTICAL QUERY FIELD", field.Left + 26f, field.Top + 38f, textPaint);
            canvas.DrawText(BuildStepSubtitle(step), field.Left + 26f, field.Top + 62f, faintTextPaint);
            DrawStepFocus(canvas, field, step, config, uiBox, formal, selected, hostile, formationInput, formation, textPaint, faintTextPaint, mutedForHud);

            if (ShouldDrawUiBox(step) && uiBox.Count > 0)
            {
                SKRect selectionBox = BoundsForEntities(engine, uiBox.Entities, field, 30f);
                canvas.DrawRoundRect(selectionBox, 18f, 18f, boxPaint);
                canvas.DrawRoundRect(selectionBox, 18f, 18f, boxStroke);
                canvas.DrawText($"Drag box preview | {uiBox.Count}", selectionBox.Left + 10f, selectionBox.Top - 10f, faintTextPaint);
            }

            if (ShouldDrawFormationPath(step))
            {
                DrawFormationConnectors(canvas, engine, formation.Entities, field, formationPathPaint);
            }

            DrawActorGroup(canvas, engine, config, owner, config.Scenario.Allies, field, step, blueFill, neutralFill, formal, selected, hostile, formation, textPaint, faintTextPaint, selectedStroke, formalStroke, threatStroke, formationStroke);
            DrawActorGroup(canvas, engine, config, owner, config.Scenario.Enemies, field, step, redFill, neutralFill, formal, selected, hostile, formation, textPaint, faintTextPaint, selectedStroke, formalStroke, threatStroke, formationStroke);
            DrawActorGroup(canvas, engine, config, owner, config.Scenario.Objectives, field, step, objectiveFill, neutralFill, formal, selected, hostile, formation, textPaint, faintTextPaint, selectedStroke, formalStroke, threatStroke, formationStroke);

            DrawLegend(canvas, field, textPaint, faintTextPaint, mutedForHud);
            DrawMetricStrip(canvas, engine, owner, config, uiBox, formal, selected, hostile, formationInput, formation, field, step, textPaint, faintTextPaint, mutedForHud);
        }

        private static void DrawActorGroup(
            SKCanvas canvas,
            GameEngine engine,
            EntityQueryTacticsShowcaseConfig config,
            Entity owner,
            IReadOnlyList<EntityQueryTacticsActorConfig> actors,
            SKRect field,
            string step,
            SKPaint teamPaint,
            SKPaint neutralPaint,
            EntityCollectionSnapshot formal,
            EntityCollectionSnapshot selected,
            EntityCollectionSnapshot hostile,
            EntityCollectionSnapshot formation,
            SKPaint textPaint,
            SKPaint faintTextPaint,
            SKPaint selectedStroke,
            SKPaint formalStroke,
            SKPaint threatStroke,
            SKPaint formationStroke)
        {
            for (int i = 0; i < actors.Count; i++)
            {
                Entity entity = FindEntityByName(engine.World, actors[i].Name);
                SKPoint point = EntityToField(engine, entity, field);
                bool isSelected = ContainsEntity(selected.Entities, entity);
                bool isFormal = ContainsEntity(formal.Entities, entity);
                bool isThreat = ContainsEntity(hostile.Entities, entity);
                bool isFormation = ContainsEntity(formation.Entities, entity);
                bool drawFormal = isFormal && ShouldDrawFormalRing(step);
                bool drawSelected = isSelected && ShouldDrawSelectedRing(step);
                bool drawThreat = isThreat && ShouldDrawThreatRing(step);
                bool drawFormation = isFormation && ShouldDrawFormationRing(step);
                bool isRouted = actors[i].Tags.Any(tag => string.Equals(tag, config.Tags.Routed, StringComparison.Ordinal));
                float radius = drawThreat || drawSelected ? 17f : 13f;

                canvas.DrawCircle(point, radius + 4f, neutralPaint);
                canvas.DrawCircle(point, radius, teamPaint);
                if (drawFormal)
                {
                    canvas.DrawCircle(point, radius + 9f, formalStroke);
                }

                if (drawFormation)
                {
                    canvas.DrawCircle(point, radius + 14f, formationStroke);
                }

                if (drawSelected)
                {
                    canvas.DrawCircle(point, radius + 19f, selectedStroke);
                }

                if (drawThreat)
                {
                    canvas.DrawCircle(point, radius + 24f, threatStroke);
                }

                string shortName = ShortName(actors[i].Name);
                canvas.DrawText(shortName, point.X + 18f, point.Y - 8f, textPaint);
                string detail = actors[i].TeamId == config.Scenario.EnemyTeamId
                    ? $"Threat {ReadThreatMetric(engine, owner, entity, config)}"
                    : $"team {actors[i].TeamId}{(isRouted ? " | routed" : string.Empty)}";
                canvas.DrawText(detail, point.X + 18f, point.Y + 12f, faintTextPaint);
            }
        }

        private static bool ShouldDrawUiBox(string step)
        {
            return step is
                "ui_box_acquisition_only" or
                "command_source_confirmed" or
                "selected_friendlies_graph";
        }

        private static bool ShouldDrawFormalRing(string step)
        {
            return step is
                "command_source_confirmed" or
                "selected_friendlies_graph" or
                "hostile_relation_graph";
        }

        private static bool ShouldDrawSelectedRing(string step)
        {
            return step is
                "selected_friendlies_graph" or
                "formation_cache_graph" or
                "retained_cache_probe" or
                "pressure_pulse_relation_update";
        }

        private static bool ShouldDrawThreatRing(string step)
        {
            return step is
                "hostile_relation_graph" or
                "pressure_pulse_relation_update";
        }

        private static bool ShouldDrawFormationRing(string step)
        {
            return step is
                "formation_cache_graph" or
                "retained_cache_probe" or
                "pressure_pulse_relation_update";
        }

        private static bool ShouldDrawFormationPath(string step)
        {
            return step is
                "formation_cache_graph" or
                "retained_cache_probe" or
                "pressure_pulse_relation_update";
        }

        private static void DrawStepFocus(
            SKCanvas canvas,
            SKRect field,
            string step,
            EntityQueryTacticsShowcaseConfig config,
            EntityCollectionSnapshot uiBox,
            EntityCollectionSnapshot formal,
            EntityCollectionSnapshot selected,
            EntityCollectionSnapshot hostile,
            EntityCollectionSnapshot formationInput,
            EntityCollectionSnapshot formation,
            SKPaint textPaint,
            SKPaint faintTextPaint,
            bool mutedForHud)
        {
            SKRect rect = new(field.Left + 20f, field.Top - 118f, field.Right - 20f, field.Top - 32f);
            SKColor accent = StepAccent(step, mutedForHud);
            using var panel = new SKPaint { Color = mutedForHud ? new SKColor(3, 8, 13, 140) : new SKColor(3, 8, 13, 220), IsAntialias = true };
            using var stroke = new SKPaint { Color = accent, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f, IsAntialias = true };
            using var chip = new SKPaint { Color = accent, IsAntialias = true };

            canvas.DrawRoundRect(rect, 14f, 14f, panel);
            canvas.DrawRoundRect(rect, 14f, 14f, stroke);
            canvas.DrawRoundRect(new SKRect(rect.Left + 14f, rect.Top + 14f, rect.Left + 150f, rect.Top + 40f), 13f, 13f, chip);
            canvas.DrawText(BuildStepChip(step), rect.Left + 26f, rect.Top + 33f, faintTextPaint);
            canvas.DrawText(BuildStepFocusTitle(step), rect.Left + 166f, rect.Top + 32f, textPaint);
            canvas.DrawText(BuildStepFocusBody(step, config, uiBox, formal, selected, hostile, formationInput, formation), rect.Left + 26f, rect.Top + 62f, faintTextPaint);
        }

        private static string BuildStepSubtitle(string step)
        {
            return step switch
            {
                "ui_box_acquisition_only" => "drag box publishes command source",
                "command_source_confirmed" => "Enter confirms the squad pipeline",
                "selected_friendlies_graph" => "friendly graph chooses the commander and totals squad stats",
                "hostile_relation_graph" => "enemy graph filters priority threats from relation metrics",
                "formation_cache_graph" => "formation graph filters routed units and keeps cache stable",
                "retained_cache_probe" => "same squad reruns without changing retained graph output",
                "pressure_pulse_relation_update" => "pressure pulse changes the live enemy threat metric",
                _ => "production showcase map ready"
            };
        }

        private static string BuildStepChip(string step)
        {
            return step switch
            {
                "ui_box_acquisition_only" => "DRAG BOX",
                "command_source_confirmed" => "COMMIT",
                "selected_friendlies_graph" => "FRIENDLY",
                "hostile_relation_graph" => "THREAT",
                "formation_cache_graph" => "FORMATION",
                "retained_cache_probe" => "CACHE",
                "pressure_pulse_relation_update" => "PULSE",
                _ => "READY"
            };
        }

        private static string BuildStepFocusTitle(string step)
        {
            return step switch
            {
                "ui_box_acquisition_only" => "Command source acquired",
                "command_source_confirmed" => "Command source confirmed for the squad",
                "selected_friendlies_graph" => "Graph selects the best commander",
                "hostile_relation_graph" => "Relation graph marks the top threat",
                "formation_cache_graph" => "Routed unit excluded from formation",
                "retained_cache_probe" => "Cache reused for stable inputs",
                "pressure_pulse_relation_update" => "Threat pulse updates the board",
                _ => "Showcase ready"
            };
        }

        private static string BuildStepFocusBody(
            string step,
            EntityQueryTacticsShowcaseConfig config,
            EntityCollectionSnapshot uiBox,
            EntityCollectionSnapshot formal,
            EntityCollectionSnapshot selected,
            EntityCollectionSnapshot hostile,
            EntityCollectionSnapshot formationInput,
            EntityCollectionSnapshot formation)
        {
            return step switch
            {
                "ui_box_acquisition_only" => $"Drag box {uiBox.Count} -> command source {formal.Count}; graph result waits.",
                "command_source_confirmed" => $"Command source {formal.Count}; commander graph result {selected.Count}; formation {formation.Count}.",
                "selected_friendlies_graph" => $"Friendly squad result {selected.Count}: {JoinSnapshotNames(selected)}.",
                "hostile_relation_graph" => $"Priority enemies {hostile.Count}; top threat {config.Scenario.PressurePulse.TargetName}.",
                "formation_cache_graph" => $"Input {formationInput.Count} -> Formation {formation.Count}; excluded Routed Scout.",
                "retained_cache_probe" => $"Formation graph rev {formation.Revision}; same squad reused cached output.",
                "pressure_pulse_relation_update" => $"Pressure Pulse +{config.Scenario.PressurePulse.Delta}; Threat 95 -> 112.",
                _ => "Config-driven squad, threat, and cache systems are live."
            };
        }

        private static SKColor StepAccent(string step, bool mutedForHud)
        {
            byte alpha = mutedForHud ? (byte)150 : (byte)230;
            return step switch
            {
                "selected_friendlies_graph" => new SKColor(110, 231, 183, alpha),
                "hostile_relation_graph" or "pressure_pulse_relation_update" => new SKColor(251, 113, 133, alpha),
                "formation_cache_graph" or "retained_cache_probe" => new SKColor(167, 139, 250, alpha),
                _ => new SKColor(96, 165, 250, alpha)
            };
        }

        private static string JoinSnapshotNames(EntityCollectionSnapshot snapshot)
        {
            return snapshot.Names.Length == 0 ? "(none)" : string.Join(", ", snapshot.Names);
        }

        private static void DrawFormationConnectors(SKCanvas canvas, GameEngine engine, IReadOnlyList<Entity> entities, SKRect field, SKPaint paint)
        {
            if (entities.Count <= 1)
            {
                return;
            }

            for (int i = 1; i < entities.Count; i++)
            {
                canvas.DrawLine(EntityToField(engine, entities[i - 1], field), EntityToField(engine, entities[i], field), paint);
            }
        }

        private static void DrawMetricStrip(
            SKCanvas canvas,
            GameEngine engine,
            Entity owner,
            EntityQueryTacticsShowcaseConfig config,
            EntityCollectionSnapshot uiBox,
            EntityCollectionSnapshot formal,
            EntityCollectionSnapshot selected,
            EntityCollectionSnapshot hostile,
            EntityCollectionSnapshot formationInput,
            EntityCollectionSnapshot formation,
            SKRect field,
            string step,
            SKPaint textPaint,
            SKPaint faintTextPaint,
            bool mutedForHud)
        {
            float x = field.Left + 24f;
            float y = field.Bottom + 48f;
            using var panel = new SKPaint { Color = mutedForHud ? new SKColor(3, 8, 13, 150) : new SKColor(3, 8, 13, 210), IsAntialias = true };
            using var stroke = new SKPaint { Color = mutedForHud ? new SKColor(92, 122, 133, 120) : new SKColor(92, 122, 133), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
            SKRect rect = new(field.Left + 18f, field.Bottom + 18f, field.Right - 18f, field.Bottom + 146f);
            canvas.DrawRoundRect(rect, 16f, 16f, panel);
            canvas.DrawRoundRect(rect, 16f, 16f, stroke);

            string selectedLine = BuildPlayerSelectedLine(engine, owner, config, uiBox, formal, selected);
            string threatLine = BuildPlayerThreatLine(engine, owner, config, hostile, step);
            string formationLine = BuildPlayerFormationLine(config, formationInput, formation, step);
            canvas.DrawText(selectedLine, x, y, textPaint);
            canvas.DrawText(threatLine, x, y + 32f, textPaint);
            canvas.DrawText(formationLine, x, y + 64f, faintTextPaint);
        }

        private static string BuildPlayerSelectedLine(
            GameEngine engine,
            Entity owner,
            EntityQueryTacticsShowcaseConfig config,
            EntityCollectionSnapshot uiBox,
            EntityCollectionSnapshot formal,
            EntityCollectionSnapshot selected)
        {
            string bestUnit = ReadSummaryEntityName(engine, owner, config.SummaryKeys.SelectedBestEntity);
            return selected.Count > 0
                ? $"Friendly query: {selected.Count} | best {bestUnit} | command power {ReadSummaryFloat(engine, owner, config.SummaryKeys.SelectedCommandPower):0}"
                : $"Drag box preview: {uiBox.Count} | command source: {formal.Count} | friendly query waits";
        }

        private static string BuildPlayerThreatLine(
            GameEngine engine,
            Entity owner,
            EntityQueryTacticsShowcaseConfig config,
            EntityCollectionSnapshot hostile,
            string step)
        {
            int threatMax = ReadSummaryInt(engine, owner, config.SummaryKeys.ThreatMax);
            string topThreat = ReadSummaryEntityName(engine, owner, config.SummaryKeys.ThreatBestEntity);
            if (step == "pressure_pulse_relation_update")
            {
                return $"Enemy threat board: {topThreat} {config.Scenario.PressurePulse.Metric} 95 -> {threatMax} | Pressure Pulse +{config.Scenario.PressurePulse.Delta}";
            }

            return hostile.Count > 0
                ? $"Enemy threat board: {hostile.Count} priority target | top {topThreat} {config.Scenario.PressurePulse.Metric} {threatMax}"
                : "Enemy threat board: waiting for graph run";
        }

        private static string BuildPlayerFormationLine(
            EntityQueryTacticsShowcaseConfig config,
            EntityCollectionSnapshot formationInput,
            EntityCollectionSnapshot formation,
            string step)
        {
            if (step == "retained_cache_probe")
            {
                return $"Formation cache reused: {formation.Count} units | Routed Scout still excluded";
            }

            return formation.Count > 0
                ? $"Formation: input {formationInput.Count} -> active {formation.Count} | excluded Routed Scout"
                : "Formation: waiting for command source";
        }

        private static void DrawLegend(SKCanvas canvas, SKRect field, SKPaint textPaint, SKPaint faintTextPaint, bool mutedForHud)
        {
            using var panel = new SKPaint { Color = mutedForHud ? new SKColor(3, 8, 13, 120) : new SKColor(3, 8, 13, 190), IsAntialias = true };
            SKRect rect = new(field.Right - 272f, field.Top + 18f, field.Right - 18f, field.Top + 140f);
            canvas.DrawRoundRect(rect, 14f, 14f, panel);
            canvas.DrawText("Visual channels", rect.Left + 16f, rect.Top + 28f, textPaint);
            DrawLegendLine(canvas, rect.Left + 18f, rect.Top + 56f, new SKColor(96, 165, 250), "command source", faintTextPaint);
            DrawLegendLine(canvas, rect.Left + 18f, rect.Top + 80f, new SKColor(110, 231, 183), "selected graph result", faintTextPaint);
            DrawLegendLine(canvas, rect.Left + 18f, rect.Top + 104f, new SKColor(251, 113, 133), "hostile relation result", faintTextPaint);
        }

        private static void DrawLegendLine(SKCanvas canvas, float x, float y, SKColor color, string text, SKPaint textPaint)
        {
            using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = 4f, IsAntialias = true };
            canvas.DrawLine(x, y - 4f, x + 34f, y - 4f, paint);
            canvas.DrawText(text, x + 46f, y, textPaint);
        }

        private static int ReadThreatMetric(GameEngine engine, Entity owner, Entity target, EntityQueryTacticsShowcaseConfig config)
        {
            if (target == Entity.Null)
            {
                return 0;
            }

            RelationshipRuntime relationships = engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("RelationshipRuntime missing.");
            RelationshipTypeRegistry relationshipTypes = engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
                ?? throw new InvalidOperationException("RelationshipTypeRegistry missing.");
            RelationshipMetricRegistry relationshipMetrics = engine.GetService(CoreServiceKeys.RelationshipMetricRegistry)
                ?? throw new InvalidOperationException("RelationshipMetricRegistry missing.");
            int typeId = relationshipTypes.GetId(config.Relationships.TacticalIntel);
            int metricId = relationshipMetrics.GetId(config.Scenario.PressurePulse.Metric);
            return relationships.GetMetric(owner, target, typeId, metricId);
        }

        private static SKRect BoundsForEntities(GameEngine engine, IReadOnlyList<Entity> entities, SKRect field, float padding)
        {
            if (entities.Count == 0)
            {
                return SKRect.Empty;
            }

            SKPoint first = EntityToField(engine, entities[0], field);
            float minX = first.X;
            float maxX = first.X;
            float minY = first.Y;
            float maxY = first.Y;
            for (int i = 1; i < entities.Count; i++)
            {
                SKPoint p = EntityToField(engine, entities[i], field);
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y);
                maxY = Math.Max(maxY, p.Y);
            }

            return new SKRect(minX - padding, minY - padding, maxX + padding, maxY + padding);
        }

        private static SKPoint EntityToField(GameEngine engine, Entity entity, SKRect field)
        {
            if (!engine.World.IsAlive(entity) || !engine.World.Has<WorldPositionCm>(entity))
            {
                return new SKPoint(field.Left, field.Top);
            }

            Vector2 pos = engine.World.Get<WorldPositionCm>(entity).Value.ToVector2();
            return WorldToField(pos.X, pos.Y, field);
        }

        private static SKPoint WorldToField(float worldX, float worldY, SKRect field)
        {
            const float minX = 820f;
            const float maxX = 2620f;
            const float minY = 480f;
            const float maxY = 1420f;
            float x = field.Left + ((worldX - minX) / (maxX - minX)) * field.Width;
            float y = field.Top + ((worldY - minY) / (maxY - minY)) * field.Height;
            return new SKPoint(x, y);
        }

        private static bool ContainsEntity(IReadOnlyList<Entity> entities, Entity entity)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i] == entity)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ShortName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "(unnamed)";
            }

            int space = name.IndexOf(' ');
            return name;
        }

        private static uint ReadCollectionRevision(GameEngine engine, Entity owner, string key)
        {
            var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore missing.");
            return collections.TryGet(owner, key, out EntityCollectionHandle handle) &&
                   collections.TryGetView(handle, out EntityCollectionView view)
                ? view.Revision
                : 0u;
        }

        private static string ReadCollectionNames(GameEngine engine, Entity owner, string key)
        {
            EntityCollectionSnapshot snapshot = ReadCollectionSnapshot(engine, owner, key, required: false);
            return string.Join(", ", snapshot.Names);
        }

        private static EntityCollectionSnapshot ReadCollectionSnapshot(
            GameEngine engine,
            Entity owner,
            string key,
            bool required = true)
        {
            var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore missing.");
            if (!collections.TryGet(owner, key, out EntityCollectionHandle handle) ||
                !collections.TryGetView(handle, out EntityCollectionView view))
            {
                if (required)
                {
                    throw new InvalidOperationException($"Entity collection '{key}' was not found for owner {owner}.");
                }

                return EntityCollectionSnapshot.Empty(key);
            }

            var entities = new Entity[Math.Max(0, view.Count)];
            int written = entities.Length == 0 ? 0 : collections.CopyEntities(handle, 0, entities);
            if (written != entities.Length)
            {
                Array.Resize(ref entities, written);
            }

            var names = new string[entities.Length];
            for (int i = 0; i < entities.Length; i++)
            {
                names[i] = ReadEntityName(engine, entities[i]);
            }

            return new EntityCollectionSnapshot(view.Key, view.Revision, view.Signature, view.Count, entities, names);
        }

        private static void AssertCollectionCount(GameEngine engine, Entity owner, string key, int expected)
        {
            var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore missing.");
            Assert.That(collections.TryGet(owner, key, out EntityCollectionHandle handle), Is.True, key);
            Assert.That(collections.TryGetView(handle, out EntityCollectionView view), Is.True, key);
            Assert.That(view.Count, Is.EqualTo(expected), key);
        }

        private static void AssertCollectionExists(GameEngine engine, Entity owner, string key)
        {
            var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore missing.");
            Assert.That(collections.TryGet(owner, key, out EntityCollectionHandle handle), Is.True, key);
            Assert.That(collections.TryGetView(handle, out _), Is.True, key);
        }

        private static int ReadSummaryInt(GameEngine engine, Entity owner, string key)
        {
            var values = engine.GetService(CoreServiceKeys.GraphOutputValueStore)
                ?? throw new InvalidOperationException("GraphOutputValueStore missing.");
            return values.TryGet(owner, key, out GraphOutputValueHandle handle) &&
                   values.TryGetView(handle, out GraphOutputValueView view)
                ? view.IntValue
                : 0;
        }

        private static float ReadSummaryFloat(GameEngine engine, Entity owner, string key)
        {
            var values = engine.GetService(CoreServiceKeys.GraphOutputValueStore)
                ?? throw new InvalidOperationException("GraphOutputValueStore missing.");
            return values.TryGet(owner, key, out GraphOutputValueHandle handle) &&
                   values.TryGetView(handle, out GraphOutputValueView view)
                ? view.FloatValue
                : 0f;
        }

        private static Entity ReadSummaryEntity(GameEngine engine, Entity owner, string key)
        {
            var values = engine.GetService(CoreServiceKeys.GraphOutputValueStore)
                ?? throw new InvalidOperationException("GraphOutputValueStore missing.");
            return values.TryGet(owner, key, out GraphOutputValueHandle handle) &&
                   values.TryGetView(handle, out GraphOutputValueView view)
                ? view.EntityValue
                : Entity.Null;
        }

        private static string ReadSummaryEntityName(GameEngine engine, Entity owner, string key)
        {
            Entity entity = ReadSummaryEntity(engine, owner, key);
            return entity == Entity.Null ? "(none)" : ReadEntityName(engine, entity);
        }

        private static string BuildStartupDiagnostics(GameEngine engine, UIRoot uiRoot)
        {
            IReadOnlyList<string> uiText = AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot);
            return string.Join(Environment.NewLine, new[]
            {
                $"scene_present={uiRoot.Scene != null}",
                $"ui_text={string.Join(" | ", uiText.Take(20))}",
                $"names={string.Join(" | ", ReadAllNames(engine.World).Take(32))}",
            });
        }

        private static IReadOnlyList<string> ReadAllNames(World world)
        {
            var names = new List<string>();
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity _, ref Name name) =>
            {
                names.Add(name.Value);
            });
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        private static Entity FindEntityByName(World world, string name)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name entityName) =>
            {
                if (string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });

            if (result == Entity.Null)
            {
                throw new InvalidOperationException($"Entity '{name}' was not found.");
            }

            return result;
        }

        private static string ReadEntityName(GameEngine engine, Entity entity)
        {
            if (entity == Entity.Null || !engine.World.IsAlive(entity) || !engine.World.Has<Name>(entity))
            {
                return string.Empty;
            }

            return engine.World.Get<Name>(entity).Value;
        }

        private static int CountGroundOverlays(GroundOverlayBuffer ground, GroundOverlayShape shape)
        {
            int count = 0;
            ReadOnlySpan<GroundOverlayItem> items = ground.GetSpan();
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Shape == shape)
                {
                    count++;
                }
            }

            return count;
        }

        private static void AssertSelectedFriendliesResult(
            GameEngine engine,
            EntityCollectionSnapshot snapshot,
            EntityQueryTacticsShowcaseConfig config,
            GraphConfig graph)
        {
            Assert.That(snapshot.Count, Is.GreaterThan(0), snapshot.Key);
            GraphNodeConfig teamNode = RequireGraphNode(graph, "QueryFilterTeam");
            GraphNodeConfig templateNode = RequireGraphNode(graph, "QueryFilterTemplate");
            GraphNodeConfig tagAnyNode = RequireGraphNode(graph, "QueryFilterTagAny");
            GraphNodeConfig tagNoneNode = RequireGraphNode(graph, "QueryFilterTagNone");
            (float minAttribute, float maxAttribute) = ResolveFloatRangeInputs(graph, RequireGraphNode(graph, "QueryFilterAttributeRange"));
            Entity[] expectedEntities = config.Scenario.Allies
                .Where(actor => actor.TeamId == teamNode.TeamId)
                .Where(actor => string.Equals(actor.Template, templateNode.Template, StringComparison.Ordinal))
                .Where(actor => actor.Tags.Contains(tagAnyNode.Tag ?? string.Empty, StringComparer.Ordinal))
                .Where(actor => !actor.Tags.Contains(tagNoneNode.Tag ?? string.Empty, StringComparer.Ordinal))
                .Select(actor => FindEntityByName(engine.World, actor.Name))
                .Where(entity => ReadAttribute(engine, entity, config.Attributes.CommandPower) >= minAttribute &&
                                 ReadAttribute(engine, entity, config.Attributes.CommandPower) <= maxAttribute)
                .OrderByDescending(entity => ReadAttribute(engine, entity, config.Attributes.CommandPower))
                .ThenBy(static entity => entity.WorldId)
                .ThenBy(static entity => entity.Id)
                .ThenBy(static entity => entity.Version)
                .ToArray();
            string[] expectedNames = expectedEntities.Select(entity => ReadEntityName(engine, entity)).ToArray();
            Assert.That(snapshot.Count, Is.GreaterThanOrEqualTo(3), "Selected graph must prove multi-entity filtering, sorting, and aggregation.");
            Assert.That(snapshot.Names, Is.EqualTo(expectedNames), "Selected graph should retain and sort the entities described by the graph config.");
            for (int i = 0; i < snapshot.Entities.Length; i++)
            {
                Assert.That(ReadTeamId(engine, snapshot.Entities[i]), Is.EqualTo(teamNode.TeamId), snapshot.Names[i]);
                Assert.That(ReadTemplateId(engine, snapshot.Entities[i]), Is.EqualTo(templateNode.Template), snapshot.Names[i]);
                Assert.That(HasTag(engine, snapshot.Entities[i], tagAnyNode.Tag ?? string.Empty), Is.True, snapshot.Names[i]);
                Assert.That(HasTag(engine, snapshot.Entities[i], tagNoneNode.Tag ?? string.Empty), Is.False, snapshot.Names[i]);
                Assert.That(ReadAttribute(engine, snapshot.Entities[i], config.Attributes.CommandPower), Is.InRange(minAttribute, maxAttribute), snapshot.Names[i]);
            }

            AssertSortedByAttributeDescending(engine, snapshot.Entities, config.Attributes.CommandPower);
        }

        private static void AssertHostileThreatResult(
            GameEngine engine,
            RelationshipRuntime relationships,
            Entity source,
            EntityCollectionSnapshot snapshot,
            int relationshipTypeId,
            int threatMetricId,
            EntityQueryTacticsShowcaseConfig config,
            GraphConfig graph)
        {
            Assert.That(snapshot.Count, Is.GreaterThan(0), snapshot.Key);
            GraphNodeConfig teamNode = RequireGraphNode(graph, "QueryFilterTeam");
            GraphNodeConfig templateNode = RequireGraphNode(graph, "QueryFilterTemplate");
            GraphNodeConfig attrNode = RequireGraphNode(graph, "QueryFilterAttributeRange");
            (float minAttribute, float maxAttribute) = ResolveFloatRangeInputs(graph, attrNode);
            GraphNodeConfig relationRangeNode = RequireGraphNode(graph, "RelationshipFilterMetricRange");
            (float minMetric, float maxMetric) = ResolveFloatRangeInputs(graph, relationRangeNode);
            GraphNodeConfig flagNode = RequireGraphNode(graph, "RelationshipFilterFlag");
            int priorityFlagId = ResolveRelationshipFlag(engine, flagNode.Flag ?? string.Empty);
            Entity[] expectedEntities = config.Scenario.Enemies
                .Where(actor => actor.TeamId == teamNode.TeamId)
                .Where(actor => string.Equals(actor.Template, templateNode.Template, StringComparison.Ordinal))
                .Select(actor => FindEntityByName(engine.World, actor.Name))
                .Where(entity => ReadAttribute(engine, entity, attrNode.Attribute ?? string.Empty) >= minAttribute &&
                                 ReadAttribute(engine, entity, attrNode.Attribute ?? string.Empty) <= maxAttribute)
                .Where(entity => relationships.GetMetric(source, entity, relationshipTypeId, threatMetricId) >= (int)minMetric &&
                                 relationships.GetMetric(source, entity, relationshipTypeId, threatMetricId) <= (int)maxMetric)
                .Where(entity => relationships.HasFlag(source, entity, relationshipTypeId, priorityFlagId))
                .OrderByDescending(entity => relationships.GetMetric(source, entity, relationshipTypeId, threatMetricId))
                .ThenBy(static entity => entity.WorldId)
                .ThenBy(static entity => entity.Id)
                .ThenBy(static entity => entity.Version)
                .ToArray();
            string[] expectedNames = expectedEntities.Select(entity => ReadEntityName(engine, entity)).ToArray();
            Assert.That(snapshot.Count, Is.GreaterThanOrEqualTo(3), "Hostile graph must prove multi-relation metric filtering, flag filtering, sorting, and aggregation.");
            Assert.That(snapshot.Names, Is.EqualTo(expectedNames), "Hostile graph should retain and sort the entities described by graph and RelationshipRuntime state.");
            for (int i = 0; i < snapshot.Entities.Length; i++)
            {
                Assert.That(ReadTeamId(engine, snapshot.Entities[i]), Is.EqualTo(teamNode.TeamId), snapshot.Names[i]);
                Assert.That(ReadTemplateId(engine, snapshot.Entities[i]), Is.EqualTo(templateNode.Template), snapshot.Names[i]);
                Assert.That(ReadAttribute(engine, snapshot.Entities[i], attrNode.Attribute ?? string.Empty), Is.InRange(minAttribute, maxAttribute), snapshot.Names[i]);
                Assert.That(relationships.GetMetric(source, snapshot.Entities[i], relationshipTypeId, threatMetricId), Is.InRange((int)minMetric, (int)maxMetric), snapshot.Names[i]);
                Assert.That(relationships.HasFlag(source, snapshot.Entities[i], relationshipTypeId, priorityFlagId), Is.True, snapshot.Names[i]);
            }

            AssertSortedByRelationshipMetricDescending(relationships, source, snapshot.Entities, relationshipTypeId, threatMetricId);
        }

        private static void AssertFormationResult(
            GameEngine engine,
            EntityCollectionSnapshot snapshot,
            EntityQueryTacticsShowcaseConfig config,
            GraphConfig graph)
        {
            Assert.That(snapshot.Count, Is.GreaterThan(0), snapshot.Key);
            GraphNodeConfig teamNode = RequireGraphNode(graph, "QueryFilterTeam");
            GraphNodeConfig tagAnyNode = RequireGraphNode(graph, "QueryFilterTagAny");
            GraphNodeConfig tagNoneNode = RequireGraphNode(graph, "QueryFilterTagNone");
            (float minAttribute, float maxAttribute) = ResolveFloatRangeInputs(graph, RequireGraphNode(graph, "QueryFilterAttributeRange"));
            string[] excludedNames = config.Scenario.Allies
                .Where(actor => actor.Tags.Contains(tagNoneNode.Tag ?? string.Empty, StringComparer.Ordinal))
                .Select(static actor => actor.Name)
                .ToArray();
            Assert.That(snapshot.Names.Intersect(excludedNames), Is.Empty, "Formation graph must exclude graph-configured negative tags.");
            for (int i = 0; i < snapshot.Entities.Length; i++)
            {
                Assert.That(ReadTeamId(engine, snapshot.Entities[i]), Is.EqualTo(teamNode.TeamId), snapshot.Names[i]);
                Assert.That(HasTag(engine, snapshot.Entities[i], tagAnyNode.Tag ?? string.Empty), Is.True, snapshot.Names[i]);
                Assert.That(HasTag(engine, snapshot.Entities[i], tagNoneNode.Tag ?? string.Empty), Is.False, snapshot.Names[i]);
                Assert.That(ReadAttribute(engine, snapshot.Entities[i], config.Attributes.Supply), Is.InRange(minAttribute, maxAttribute), snapshot.Names[i]);
            }

            AssertSortedByAttributeDescending(engine, snapshot.Entities, config.Attributes.CommandPower);
        }

        private static float SumAttribute(GameEngine engine, IReadOnlyList<Entity> entities, string attributeName)
        {
            float sum = 0f;
            for (int i = 0; i < entities.Count; i++)
            {
                sum += ReadAttribute(engine, entities[i], attributeName);
            }

            return sum;
        }

        private static float MaxAttribute(GameEngine engine, IReadOnlyList<Entity> entities, string attributeName)
        {
            return ReadAttribute(engine, MaxAttributeEntity(engine, entities, attributeName), attributeName);
        }

        private static float MinAttribute(GameEngine engine, IReadOnlyList<Entity> entities, string attributeName)
        {
            Assert.That(entities.Count, Is.GreaterThan(0));
            float value = ReadAttribute(engine, entities[0], attributeName);
            for (int i = 1; i < entities.Count; i++)
            {
                value = Math.Min(value, ReadAttribute(engine, entities[i], attributeName));
            }

            return value;
        }

        private static Entity MaxAttributeEntity(GameEngine engine, IReadOnlyList<Entity> entities, string attributeName)
        {
            Assert.That(entities.Count, Is.GreaterThan(0));
            Entity best = entities[0];
            float bestValue = ReadAttribute(engine, best, attributeName);
            for (int i = 1; i < entities.Count; i++)
            {
                float value = ReadAttribute(engine, entities[i], attributeName);
                if (value > bestValue || (value == bestValue && CompareEntityStable(entities[i], best) < 0))
                {
                    best = entities[i];
                    bestValue = value;
                }
            }

            return best;
        }

        private static int MaxRelationshipMetric(
            RelationshipRuntime relationships,
            Entity source,
            IReadOnlyList<Entity> targets,
            int relationshipTypeId,
            int metricId)
        {
            Assert.That(targets.Count, Is.GreaterThan(0));
            int max = relationships.GetMetric(source, targets[0], relationshipTypeId, metricId);
            for (int i = 1; i < targets.Count; i++)
            {
                max = Math.Max(max, relationships.GetMetric(source, targets[i], relationshipTypeId, metricId));
            }

            return max;
        }

        private static int SumRelationshipMetric(
            RelationshipRuntime relationships,
            Entity source,
            IReadOnlyList<Entity> targets,
            int relationshipTypeId,
            int metricId)
        {
            int sum = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                sum += relationships.GetMetric(source, targets[i], relationshipTypeId, metricId);
            }

            return sum;
        }

        private static int AverageRelationshipMetric(
            RelationshipRuntime relationships,
            Entity source,
            IReadOnlyList<Entity> targets,
            int relationshipTypeId,
            int metricId)
        {
            return targets.Count == 0
                ? 0
                : SumRelationshipMetric(relationships, source, targets, relationshipTypeId, metricId) / targets.Count;
        }

        private static float ReadAttribute(GameEngine engine, Entity entity, string attributeName)
        {
            int attributeId = AttributeRegistry.GetId(attributeName);
            Assert.That(attributeId, Is.GreaterThanOrEqualTo(0), attributeName);
            if (!engine.World.IsAlive(entity) || !engine.World.Has<AttributeBuffer>(entity))
            {
                return 0f;
            }

            ref AttributeBuffer attributes = ref engine.World.Get<AttributeBuffer>(entity);
            return attributes.HasAttribute(attributeId) ? attributes.GetCurrent(attributeId) : 0f;
        }

        private static int ReadTeamId(GameEngine engine, Entity entity)
        {
            return engine.World.IsAlive(entity) && engine.World.Has<Team>(entity)
                ? engine.World.Get<Team>(entity).Id
                : 0;
        }

        private static string ReadTemplateId(GameEngine engine, Entity entity)
        {
            if (!engine.World.IsAlive(entity) || !engine.World.Has<EntityTemplateKeyRef>(entity))
            {
                return string.Empty;
            }

            EntityTemplateKeyRegistry registry = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
                ?? throw new InvalidOperationException("EntityTemplateKeyRegistry missing.");
            return registry.GetName(engine.World.Get<EntityTemplateKeyRef>(entity).TemplateKeyId);
        }

        private static bool HasTag(GameEngine engine, Entity entity, string tagName)
        {
            int tagId = TagRegistry.GetId(tagName);
            Assert.That(tagId, Is.GreaterThan(0), tagName);
            return engine.World.IsAlive(entity) &&
                   engine.World.Has<GameplayTagContainer>(entity) &&
                   engine.World.Get<GameplayTagContainer>(entity).HasTag(tagId);
        }

        private static int ResolveRelationshipFlag(GameEngine engine, string flagName)
        {
            RelationshipFlagRegistry flags = engine.GetService(CoreServiceKeys.RelationshipFlagRegistry)
                ?? throw new InvalidOperationException("RelationshipFlagRegistry missing.");
            return flags.GetId(flagName);
        }

        private static void AssertSortedByAttributeDescending(GameEngine engine, IReadOnlyList<Entity> entities, string attributeName)
        {
            for (int i = 1; i < entities.Count; i++)
            {
                float previous = ReadAttribute(engine, entities[i - 1], attributeName);
                float current = ReadAttribute(engine, entities[i], attributeName);
                Assert.That(previous, Is.GreaterThanOrEqualTo(current), $"{attributeName} sort at index {i}");
            }
        }

        private static void AssertSortedByRelationshipMetricDescending(
            RelationshipRuntime relationships,
            Entity source,
            IReadOnlyList<Entity> targets,
            int relationshipTypeId,
            int metricId)
        {
            for (int i = 1; i < targets.Count; i++)
            {
                int previous = relationships.GetMetric(source, targets[i - 1], relationshipTypeId, metricId);
                int current = relationships.GetMetric(source, targets[i], relationshipTypeId, metricId);
                Assert.That(previous, Is.GreaterThanOrEqualTo(current), $"relationship metric sort at index {i}");
            }
        }

        private static string GetBinding(IReadOnlyDictionary<string, string> bindings, string actionId)
        {
            if (!bindings.TryGetValue(actionId, out string? path) || string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException($"Entity query tactics input binding for action '{actionId}' was not found.");
            }

            return path;
        }

        private static IReadOnlyDictionary<string, string> LoadInputBindings(GameEngine engine)
        {
            InputConfigRoot input = new InputConfigPipelineLoader(engine.ConfigPipeline).Load(
                engine.ConfigCatalog,
                engine.ConfigConflictReport);
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int c = 0; c < input.Contexts.Count; c++)
            {
                InputContextDef context = input.Contexts[c];
                for (int b = 0; b < context.Bindings.Count; b++)
                {
                    InputBindingDef binding = context.Bindings[b];
                    string actionId = binding.ActionId ?? string.Empty;
                    string path = binding.Path ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(actionId) && !string.IsNullOrWhiteSpace(path))
                    {
                        bindings[actionId] = path;
                    }
                }
            }

            return bindings;
        }

        private static EntityQueryTacticsShowcaseConfig LoadShowcaseConfig(GameEngine engine)
        {
            ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(
                engine.ConfigCatalog,
                "EntityQueryTacticsShowcaseConfig.json",
                ConfigMergePolicy.Replace);
            JsonObject? merged = engine.ConfigPipeline.MergeFromCatalog(in entry, engine.ConfigConflictReport) as JsonObject;
            var options = StrictJsonOptions.CreateCamelCase();
            EntityQueryTacticsShowcaseConfig? config = merged?.Deserialize<EntityQueryTacticsShowcaseConfig>(options);
            if (config == null ||
                string.IsNullOrWhiteSpace(config.MapId) ||
                string.IsNullOrWhiteSpace(config.Scenario.PlayerCommanderName) ||
                config.Scenario.Allies.Length == 0 ||
                string.IsNullOrWhiteSpace(config.Scenario.PressurePulse.TargetName) ||
                string.IsNullOrWhiteSpace(config.Graphs.SelectedFriendlies) ||
                string.IsNullOrWhiteSpace(config.Graphs.HostileThreats) ||
                string.IsNullOrWhiteSpace(config.Graphs.FormationCache))
            {
                throw new InvalidOperationException("Entity query tactics acceptance config is incomplete.");
            }

            return config;
        }

        private static GraphConfig LoadGraphConfig(GameEngine engine, string graphId)
        {
            ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(
                engine.ConfigCatalog,
                "GAS/graphs.json",
                ConfigMergePolicy.ArrayById,
                "id");
            var merged = engine.ConfigPipeline.MergeArrayByIdFromCatalog(in entry, engine.ConfigConflictReport);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
            for (int i = 0; i < merged.Count; i++)
            {
                if (!string.Equals(merged[i].Id, graphId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                JsonObject node = merged[i].Node;
                if (node.ContainsKey("controlEdges") || node.ContainsKey("valueEdges"))
                {
                    GraphControlFlowDocument? document = node.Deserialize<GraphControlFlowDocument>(options);
                    if (document == null)
                    {
                        throw new InvalidOperationException($"ControlFlow graph config '{graphId}' failed to deserialize.");
                    }

                    return MaterializeGraphConfigFromControlFlow(document);
                }

                GraphConfig? graph = node.Deserialize<GraphConfig>(options);
                if (graph == null)
                {
                    throw new InvalidOperationException($"Graph config '{graphId}' failed to deserialize.");
                }

                return graph;
            }

            throw new InvalidOperationException($"Graph config '{graphId}' was not found in production graph catalog.");
        }

        private static GraphConfig MaterializeGraphConfigFromControlFlow(GraphControlFlowDocument document)
        {
            var graph = new GraphConfig
            {
                Id = document.Id,
                Kind = document.Kind,
                Entry = document.Entry,
                Outputs = document.Outputs ?? new List<GraphOutputConfig>()
            };

            for (int i = 0; i < document.Nodes.Count; i++)
            {
                GraphControlFlowNode source = document.Nodes[i];
                graph.Nodes.Add(new GraphNodeConfig
                {
                    Id = source.Id,
                    Op = source.Op,
                    IntValue = source.IntValue,
                    FloatValue = source.FloatValue,
                    BoolValue = source.BoolValue,
                    Attribute = source.Attribute,
                    Tag = source.Tag,
                    Template = source.Template,
                    CollectionKey = source.CollectionKey,
                    RelationshipType = source.RelationshipType,
                    Metric = source.Metric,
                    Flag = source.Flag,
                    TeamId = source.TeamId,
                    Descending = source.Descending
                });
            }

            Dictionary<string, GraphNodeConfig> nodesById = graph.Nodes.ToDictionary(
                static n => n.Id,
                StringComparer.OrdinalIgnoreCase);
            List<GraphControlFlowValueEdge> valueEdges = document.ValueEdges ?? new List<GraphControlFlowValueEdge>();
            for (int i = 0; i < valueEdges.Count; i++)
            {
                GraphControlFlowValueEdge edge = valueEdges[i];
                if (!nodesById.TryGetValue(edge.To, out GraphNodeConfig? target))
                {
                    continue;
                }

                if (string.Equals(edge.ToPort, GraphControlFlowPorts.Source, StringComparison.Ordinal) ||
                    string.Equals(edge.ToPort, GraphControlFlowPorts.Min, StringComparison.Ordinal) ||
                    string.Equals(edge.ToPort, GraphControlFlowPorts.Max, StringComparison.Ordinal) ||
                    string.Equals(edge.ToPort, GraphControlFlowPorts.TeamId, StringComparison.Ordinal) ||
                    string.Equals(edge.ToPort, GraphControlFlowPorts.B, StringComparison.Ordinal))
                {
                    target.Inputs.Add(edge.From);
                }
            }

            // Range nodes expect [..., min, max] order for ResolveFloatRangeInputs.
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                GraphNodeConfig node = graph.Nodes[i];
                if (!string.Equals(node.Op, nameof(GraphNodeOp.QueryFilterAttributeRange), StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(node.Op, nameof(GraphNodeOp.RelationshipFilterMetricRange), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? minSource = null;
                string? maxSource = null;
                string? source = null;
                for (int e = 0; e < valueEdges.Count; e++)
                {
                    GraphControlFlowValueEdge edge = valueEdges[e];
                    if (!string.Equals(edge.To, node.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.Equals(edge.ToPort, GraphControlFlowPorts.Min, StringComparison.Ordinal))
                    {
                        minSource = edge.From;
                    }
                    else if (string.Equals(edge.ToPort, GraphControlFlowPorts.Max, StringComparison.Ordinal))
                    {
                        maxSource = edge.From;
                    }
                    else if (string.Equals(edge.ToPort, GraphControlFlowPorts.Source, StringComparison.Ordinal))
                    {
                        source = edge.From;
                    }
                }

                node.Inputs.Clear();
                if (!string.IsNullOrWhiteSpace(source))
                {
                    node.Inputs.Add(source);
                }

                if (!string.IsNullOrWhiteSpace(minSource) && !string.IsNullOrWhiteSpace(maxSource))
                {
                    node.Inputs.Add(minSource);
                    node.Inputs.Add(maxSource);
                }
            }

            return graph;
        }

        private static GraphNodeConfig RequireGraphNode(GraphConfig graph, string op)
        {
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                if (string.Equals(graph.Nodes[i].Op, op, StringComparison.OrdinalIgnoreCase))
                {
                    return graph.Nodes[i];
                }
            }

            throw new InvalidOperationException($"Graph '{graph.Id}' must contain node op '{op}'.");
        }

        private static (float Min, float Max) ResolveFloatRangeInputs(GraphConfig graph, GraphNodeConfig node)
        {
            if (node.Inputs.Count < 2)
            {
                throw new InvalidOperationException($"Graph '{graph.Id}' node '{node.Id}' must declare min/max inputs.");
            }

            return (
                ResolveConstFloat(graph, node.Inputs[node.Inputs.Count - 2]),
                ResolveConstFloat(graph, node.Inputs[node.Inputs.Count - 1]));
        }

        private static float ResolveConstFloat(GraphConfig graph, string nodeId)
        {
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                GraphNodeConfig node = graph.Nodes[i];
                if (string.Equals(node.Id, nodeId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(node.Op, "ConstFloat", StringComparison.OrdinalIgnoreCase))
                {
                    return node.FloatValue;
                }
            }

            throw new InvalidOperationException($"Graph '{graph.Id}' range input '{nodeId}' must resolve to ConstFloat.");
        }

        private static int CountOutputBindings(GraphOutputSchemaRegistry schemas, IReadOnlyList<int> graphIds)
        {
            int count = 0;
            for (int i = 0; i < graphIds.Count; i++)
            {
                count += schemas.Get(graphIds[i]).Bindings.Length;
            }

            return count;
        }

        private static string BuildTraceJsonl(IReadOnlyList<AcceptanceSnapshot> snapshots)
        {
            var lines = new List<string>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                AcceptanceSnapshot snapshot = snapshots[i];
                lines.Add(JsonSerializer.Serialize(new
                {
                    event_id = $"entity-query-tactics-{i + 1:000}",
                    step = snapshot.Step,
                    screenshot = snapshot.ScreenshotFileName,
                    battlefield = snapshot.BattlefieldFileName,
                    ui_box_revision = snapshot.UiBoxRevision,
                    command_source_revision = snapshot.CommandSourceRevision,
                    formation_revision = snapshot.FormationRevision,
                    hostile_revision = snapshot.HostileRevision,
                    selected_names = snapshot.SelectedNames,
                    formation_names = snapshot.FormationNames,
                    threat_names = snapshot.ThreatNames,
                    selected_count = snapshot.SelectedCount,
                    threat_max = snapshot.ThreatMax,
                    formation_count = snapshot.FormationCount,
                    ground_ring_count = snapshot.GroundRingCount,
                    ui_text = snapshot.UiText,
                    status = "done"
                }));
            }

            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        private static string BuildBattleReport(
            IReadOnlyList<string> timeline,
            IReadOnlyList<AcceptanceSnapshot> snapshots,
            IReadOnlyList<double> frameTimesMs)
        {
            AcceptanceSnapshot final = snapshots[^1];
            double medianTickMs = Median(frameTimesMs);
            double p95TickMs = Percentile(frameTimesMs, 0.95d);
            double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: entity-query-tactics-showcase");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: drag-select allies, run query graphs, inspect hostile relation threat, rotate formation cache, probe retained diff, and mutate pressure under a production mod path.");
            sb.AppendLine("- Gameplay domain: command-source collection, UI acquisition collection, formation collection, EntityCollectionStore, GraphReturnWriter, EntitySetQueryRuntime, RelationshipRuntime, tags, attrs, templates, sorting, extremes, and aggregates.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- Mods: `LudotsCoreMod`, `CoreInputMod`, `CameraProfilesMod`, `NarrativeFrontendMod`, `EntityQueryTacticsShowcaseMod`");
            sb.AppendLine("- Input source: production `InputConfigPipelineLoader` + `PlayerInputHandler` with deterministic mouse/keyboard backend.");
            sb.AppendLine("- Clock profile: fixed `1/60s` headless `GameEngine.Tick()`.");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            for (int i = 0; i < timeline.Count; i++)
            {
                sb.AppendLine($"- {timeline[i]}");
            }

            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success: yes");
            sb.AppendLine($"- verdict: selected `{final.SelectedNames}`, formation `{final.FormationNames}`, hostile `{final.ThreatNames}` all came from retained graph materializations.");
            sb.AppendLine();
            sb.AppendLine("## Summary Stats");
            sb.AppendLine($"- snapshots captured: `{snapshots.Count}`");
            sb.AppendLine($"- median headless tick: `{medianTickMs:F3}ms`");
            sb.AppendLine($"- p95 headless tick: `{p95TickMs:F3}ms`");
            sb.AppendLine($"- max headless tick: `{maxTickMs:F3}ms`");
            sb.AppendLine("- tick note: acceptance timings include map startup, UI sync, evidence capture staging, and action frames; the dedicated production pressure loop is reported in the benchmark artifact.");
            sb.AppendLine($"- final selected count: `{final.SelectedCount}`");
            sb.AppendLine($"- final threat max: `{final.ThreatMax}`");
            sb.AppendLine($"- final formation count: `{final.FormationCount}`");
            sb.AppendLine($"- final revisions: ui `{final.UiBoxRevision}`, command source `{final.CommandSourceRevision}`, formation `{final.FormationRevision}`, hostile `{final.HostileRevision}`");
            sb.AppendLine("- reusable wiring: `ConfigPipeline`, `PlayerInputHandler`, `CommandSourceAcquisitionSystem`, `EntityCollectionStore`, `GraphReturnWriter`, `EntitySetQueryRuntime`, `RelationshipRuntime`, `NarrativeFrontendService`");
            return sb.ToString();
        }

        private static string BuildBenchmarkReport(
            LauncherLaunchPlan launchPlan,
            EntityQueryTacticsShowcaseConfig config,
            GraphConfig selectedGraph,
            GraphConfig hostileGraph,
            GraphConfig formationGraph,
            IReadOnlyList<int> graphIds,
            int outputBindingCount,
            int graphIterations,
            double graphTotalMs,
            long graphAllocated,
            int graphStabilizationAttempts,
            IReadOnlyList<SingleGraphHotPathMeasurement> singleGraphMeasurements,
            int cacheProbeIterations,
            double cacheTotalMs,
            long cacheAllocated,
            int cacheStabilizationAttempts,
            int stableRevisionCount,
            EntityCollectionSnapshot beforeStableProbe,
            EntityCollectionSnapshot afterStableProbe,
            EntityCollectionSnapshot beforeRotationInput,
            EntityCollectionSnapshot afterRotationInput,
            EntityCollectionSnapshot beforeRotationOutput,
            EntityCollectionSnapshot afterRotationOutput,
            int pressureIterations,
            double pressureTotalMs,
            long pressureAllocated,
            int pressureStabilizationAttempts,
            int pressureChangeCountBefore,
            int pressureChangeCountAfter,
            int pressureChangeCapacityBefore,
            int pressureChangeCapacityAfter,
            int pressureChangeResizeDelta,
            ProductionTickBenchmark tickBenchmark,
            int threatBeforeProductionPulse,
            int threatAfterProductionPulse,
            int warmupGraphIterations,
            int stabilizationGraphIterations,
            IReadOnlyDictionary<string, string> assetHashes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Entity Query Tactics Production Benchmark");
            sb.AppendLine();
            sb.AppendLine("## Run Metadata");
            sb.AppendLine($"- command: `dotnet test src/Tests/GasTests/GasTests.csproj --filter EntityQueryTactics_ProductionBenchmark_WritesReport --no-restore`");
            sb.AppendLine($"- runtime: `{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}`");
            sb.AppendLine($"- os: `{System.Runtime.InteropServices.RuntimeInformation.OSDescription}`");
            sb.AppendLine($"- generated UTC: `{DateTime.UtcNow:O}`");
            sb.AppendLine($"- preset: `{ShowcasePresetId}`");
            sb.AppendLine($"- plan fingerprint: `{launchPlan.PlanFingerprint}`");
            sb.AppendLine($"- ordered mods: `{string.Join(" -> ", launchPlan.OrderedModIds)}`");
            sb.AppendLine($"- graph ids: `{string.Join(", ", graphIds.Select(GraphIdRegistry.GetName))}`");
            sb.AppendLine($"- graph node counts: selected `{selectedGraph.Nodes.Count}`, hostile `{hostileGraph.Nodes.Count}`, formation `{formationGraph.Nodes.Count}`");
            sb.AppendLine($"- graph output bindings: `{outputBindingCount}`");
            foreach (KeyValuePair<string, string> hash in assetHashes)
            {
                sb.AppendLine($"- asset hash `{hash.Key}`: `{hash.Value}`");
            }

            sb.AppendLine();
            sb.AppendLine("## Production Chain");
            sb.AppendLine($"- map: `{config.MapId}`");
            sb.AppendLine($"- mods: `{string.Join("`, `", launchPlan.OrderedModIds)}`");
            sb.AppendLine($"- graphs: `{config.Graphs.SelectedFriendlies}`, `{config.Graphs.HostileThreats}`, `{config.Graphs.FormationCache}`");
            sb.AppendLine($"- collections: `{config.Collections.UiBox}`, `{config.Collections.CommandSourceMirror}`, `{config.Collections.FormationPrimary}`, `{config.Collections.FormationCacheResult}`");
            sb.AppendLine($"- relationship type: `{config.Relationships.TacticalIntel}`");
            sb.AppendLine($"- pressure metric: `{config.Scenario.PressurePulse.Metric}`");
            sb.AppendLine($"- warmup graph executions: `{warmupGraphIterations}` iterations plus `{stabilizationGraphIterations}` post-GC stabilization iterations before allocation timing");
            sb.AppendLine();
            sb.AppendLine("## Hot Path Measurements");
            sb.AppendLine("| path | iterations | total ms | per iteration us | allocated bytes |");
            sb.AppendLine("|---|---:|---:|---:|---:|");
            sb.AppendLine($"| GraphReturnWriter execute x3 stable inputs | {graphIterations} | {graphTotalMs:F3} | {graphTotalMs * 1000d / graphIterations:F3} | {graphAllocated} |");
            foreach (SingleGraphHotPathMeasurement singleGraph in singleGraphMeasurements)
            {
                HotPathMeasurement measurement = singleGraph.Measurement;
                sb.AppendLine($"| GraphReturnWriter execute `{singleGraph.GraphName}` only | {graphIterations} | {measurement.TotalMs:F3} | {measurement.TotalMs * 1000d / graphIterations:F3} | {measurement.AllocatedBytes} |");
            }

            sb.AppendLine($"| Retained diff execute x3 stable inputs | {cacheProbeIterations} | {cacheTotalMs:F3} | {cacheTotalMs * 1000d / cacheProbeIterations:F3} | {cacheAllocated} |");
            sb.AppendLine($"| Relationship AddMetric + graph execute x3 | {pressureIterations} | {pressureTotalMs:F3} | {pressureTotalMs * 1000d / pressureIterations:F3} | {pressureAllocated} |");
            sb.AppendLine($"- stable allocation sample attempts: graph x3 `{graphStabilizationAttempts}`, single graphs `{string.Join(", ", singleGraphMeasurements.Select(static x => $"{x.GraphName}:{x.Measurement.StabilizationAttempts}"))}`, retained diff `{cacheStabilizationAttempts}`, pressure `{pressureStabilizationAttempts}`");
            sb.AppendLine();
            sb.AppendLine("## Production Tick Loop");
            sb.AppendLine("| path | frames | action frames | total ms | median ms | p95 ms | max ms | allocated bytes |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
            sb.AppendLine($"| PlayerInputHandler + GameEngine.Tick + showcase systems | {tickBenchmark.Frames} | {tickBenchmark.ActionFrames} | {tickBenchmark.TotalMs:F3} | {tickBenchmark.MedianFrameMs:F3} | {tickBenchmark.P95FrameMs:F3} | {tickBenchmark.MaxFrameMs:F3} | {tickBenchmark.AllocatedBytes} |");
            sb.AppendLine($"- production pressure summary: `{config.SummaryKeys.ThreatMax}` `{threatBeforeProductionPulse}` -> `{threatAfterProductionPulse}` during the tick loop.");
            sb.AppendLine();
            sb.AppendLine("## Retained Diff");
            sb.AppendLine($"- stable formation revisions: `{stableRevisionCount}/{cacheProbeIterations}`");
            sb.AppendLine($"- stable probe before: rev `{beforeStableProbe.Revision}`, sig `0x{beforeStableProbe.Signature:X}`, count `{beforeStableProbe.Count}`, names `{string.Join(", ", beforeStableProbe.Names)}`");
            sb.AppendLine($"- stable probe after: rev `{afterStableProbe.Revision}`, sig `0x{afterStableProbe.Signature:X}`, count `{afterStableProbe.Count}`, names `{string.Join(", ", afterStableProbe.Names)}`");
            sb.AppendLine($"- rotation input: `{config.Collections.FormationPrimary}` rev `{beforeRotationInput.Revision}` -> `{afterRotationInput.Revision}`, sig `0x{beforeRotationInput.Signature:X}` -> `0x{afterRotationInput.Signature:X}`");
            sb.AppendLine($"- rotation output: `{config.Collections.FormationCacheResult}` rev `{beforeRotationOutput.Revision}` -> `{afterRotationOutput.Revision}`, sig `0x{beforeRotationOutput.Signature:X}` -> `0x{afterRotationOutput.Signature:X}`");
            sb.AppendLine($"- expected: stable inputs keep `{config.Collections.FormationCacheResult}` revision unchanged; order-only source rotation is normalized by graph sorting and retained output signature.");
            sb.AppendLine();
            sb.AppendLine("## Relationship Pressure Buffer");
            sb.AppendLine($"- change records: `{pressureChangeCountBefore}` -> `{pressureChangeCountAfter}`");
            sb.AppendLine($"- change buffer capacity: `{pressureChangeCapacityBefore}` -> `{pressureChangeCapacityAfter}`");
            sb.AppendLine($"- change buffer resize delta: `{pressureChangeResizeDelta}`");
            sb.AppendLine();
            sb.AppendLine("## Architecture Notes");
            sb.AppendLine("- C# systems and visual graph ops share the same runtime APIs: `GraphReturnWriter -> GasGraphRuntimeApi -> EntitySetQueryRuntime / RelationshipRuntime`.");
            sb.AppendLine("- The showcase is configured through mod assets and loaded by `ConfigPipeline`; the benchmark does not create a parallel query, selection, or relationship system.");
            sb.AppendLine("- Hot path allocation counts use current-thread `GC.GetAllocatedBytesForCurrentThread()` after warmup and measured zero-allocation stabilization; setup, JSON loading, UI screenshots, and report writing are outside the asserted allocation windows.");
            sb.AppendLine("- Full tick loop allocation is reported for realism, not asserted as 0Alloc, because it includes input, UI, presentation text, and showcase state publication.");
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[ConfigPipeline loads EntityQueryTacticsShowcaseMod] --> B[MapLoader spawns teams, templates, attrs, tags]",
                "    B --> C[Player drags UI box selection]",
                "    C --> D[CommandSourceAcquisitionSystem writes UI acquisition and command source]",
                "    D --> E[Configured commit action confirms command source]",
                "    E --> F[Showcase publishes command and formation snapshots to EntityCollectionStore]",
                "    F --> G[GraphReturnWriter executes graph ops through shared C# EntitySetQueryRuntime API]",
                "    G --> H[Selection graph filters team/template/tag/attr and writes aggregate summaries]",
                "    G --> I[Relationship graph filters metric/flag and sorts hostile threat]",
                "    G --> J[Formation graph proves retained diff revision stability]",
                "    I --> K[Pressure pulse mutates RelationshipRuntime and graph summaries update]"
            }) + Environment.NewLine;
        }

        private static IReadOnlyDictionary<string, string> ComputeShowcaseAssetHashes()
        {
            string repoRoot = FindRepoRoot();
            string modAssets = Path.Combine(repoRoot, "mods", "showcases", "entity_query_tactics", "EntityQueryTacticsShowcaseMod", "assets");
            string[] paths =
            {
                "EntityQueryTacticsShowcaseConfig.json",
                Path.Combine("Frontend", "entity_query_tactics_frontend.json"),
                Path.Combine("Presentation", "presenters.json"),
                Path.Combine("Configs", "Camera", "virtual_cameras.json"),
                Path.Combine("GAS", "graphs.json"),
                Path.Combine("GAS", "attribute_constraints.json"),
                Path.Combine("GAS", "tag_rules.json"),
                Path.Combine("Relationships", "catalog.json"),
                Path.Combine("Entities", "templates.json"),
                Path.Combine("Maps", "entity_query_tactics_showcase.json"),
                Path.Combine("Input", "default_input.json"),
            };

            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < paths.Length; i++)
            {
                string absolutePath = Path.Combine(modAssets, paths[i]);
                using FileStream stream = File.OpenRead(absolutePath);
                byte[] hash = SHA256.HashData(stream);
                hashes[paths[i].Replace('\\', '/')] = Convert.ToHexString(hash);
            }

            return hashes;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "mods")) &&
                    File.Exists(Path.Combine(dir.FullName, "gitbook", "contributing", "ai-assisted-development.md")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private static double Median(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
            {
                return 0d;
            }

            double[] ordered = values.OrderBy(v => v).ToArray();
            int middle = ordered.Length / 2;
            return (ordered.Length & 1) == 0
                ? (ordered[middle - 1] + ordered[middle]) * 0.5d
                : ordered[middle];
        }

        private static double Percentile(IReadOnlyList<double> values, double percentile)
        {
            if (values.Count == 0)
            {
                return 0d;
            }

            double[] ordered = values.OrderBy(v => v).ToArray();
            int index = Math.Clamp((int)Math.Ceiling(ordered.Length * percentile) - 1, 0, ordered.Length - 1);
            return ordered[index];
        }

        private static int CompareEntityStable(Entity left, Entity right)
        {
            int c = left.WorldId.CompareTo(right.WorldId);
            if (c != 0)
            {
                return c;
            }

            c = left.Id.CompareTo(right.Id);
            if (c != 0)
            {
                return c;
            }

            return left.Version.CompareTo(right.Version);
        }

        private sealed record EntityCollectionSnapshot(
            string Key,
            uint Revision,
            ulong Signature,
            int Count,
            Entity[] Entities,
            string[] Names)
        {
            public static EntityCollectionSnapshot Empty(string key)
            {
                return new EntityCollectionSnapshot(key, 0u, 0UL, 0, Array.Empty<Entity>(), Array.Empty<string>());
            }
        }

        private sealed record ProductionTickBenchmark(
            int Frames,
            int ActionFrames,
            double TotalMs,
            double MedianFrameMs,
            double P95FrameMs,
            double MaxFrameMs,
            long AllocatedBytes);

        private readonly record struct HotPathMeasurement(
            double TotalMs,
            long AllocatedBytes,
            int StabilizationAttempts);

        private readonly record struct SingleGraphHotPathMeasurement(
            string GraphName,
            HotPathMeasurement Measurement);

        private sealed record AcceptanceSnapshot(
            string Step,
            string ScreenshotFileName,
            string BattlefieldFileName,
            uint UiBoxRevision,
            uint CommandSourceRevision,
            uint FormationRevision,
            uint HostileRevision,
            string SelectedNames,
            string FormationNames,
            string ThreatNames,
            int SelectedCount,
            int ThreatMax,
            int FormationCount,
            int GroundRingCount,
            IReadOnlyList<string> UiText);

        private sealed class EntityQueryTacticsShowcaseConfig
        {
            public string MapId { get; set; } = string.Empty;
            public EntityQueryTacticsScenarioConfig Scenario { get; set; } = new();
            public EntityQueryTacticsActionConfig Actions { get; set; } = new();
            public EntityQueryTacticsCollectionConfig Collections { get; set; } = new();
            public EntityQueryTacticsGraphConfig Graphs { get; set; } = new();
            public EntityQueryTacticsSummaryKeys SummaryKeys { get; set; } = new();
            public EntityQueryTacticsRelationshipNames Relationships { get; set; } = new();
            public EntityQueryTacticsMetricNames Metrics { get; set; } = new();
            public EntityQueryTacticsFlagNames Flags { get; set; } = new();
            public EntityQueryTacticsTagNames Tags { get; set; } = new();
            public EntityQueryTacticsAttributes Attributes { get; set; } = new();
            public EntityQueryTacticsLogs Logs { get; set; } = new();
            public EntityQueryTacticsPresentationText Presentation { get; set; } = new();
            public EntityQueryTacticsDemoPlaybackConfig DemoPlayback { get; set; } = new();
        }

        private sealed class EntityQueryTacticsScenarioConfig
        {
            public int PlayerTeamId { get; set; }
            public int EnemyTeamId { get; set; }
            public string PlayerTeamName { get; set; } = string.Empty;
            public string EnemyTeamName { get; set; } = string.Empty;
            public string PlayerCommanderName { get; set; } = string.Empty;
            public string EnemyCommanderName { get; set; } = string.Empty;
            public EntityQueryTacticsActorConfig[] Allies { get; set; } = Array.Empty<EntityQueryTacticsActorConfig>();
            public EntityQueryTacticsActorConfig[] Enemies { get; set; } = Array.Empty<EntityQueryTacticsActorConfig>();
            public EntityQueryTacticsActorConfig[] Objectives { get; set; } = Array.Empty<EntityQueryTacticsActorConfig>();
            public EntityQueryTacticsRelationSeed[] RelationSeeds { get; set; } = Array.Empty<EntityQueryTacticsRelationSeed>();
            public EntityQueryTacticsPressurePulseConfig PressurePulse { get; set; } = new();
        }

        private sealed class EntityQueryTacticsActorConfig
        {
            public string Name { get; set; } = string.Empty;
            public string Template { get; set; } = string.Empty;
            public int TeamId { get; set; }
            public string[] Tags { get; set; } = Array.Empty<string>();
        }

        private sealed class EntityQueryTacticsRelationSeed
        {
            public string SourceName { get; set; } = string.Empty;
            public string TargetName { get; set; } = string.Empty;
            public string Metric { get; set; } = string.Empty;
            public int Value { get; set; }
            public string[] Flags { get; set; } = Array.Empty<string>();
        }

        private sealed class EntityQueryTacticsPressurePulseConfig
        {
            public string TargetName { get; set; } = string.Empty;
            public string Metric { get; set; } = string.Empty;
            public int Delta { get; set; }
            public string[] Flags { get; set; } = Array.Empty<string>();
        }

        private sealed class EntityQueryTacticsActionConfig
        {
            public string CommitSelection { get; set; } = string.Empty;
            public string ExecuteGraphs { get; set; } = string.Empty;
            public string RotateFormation { get; set; } = string.Empty;
            public string PressurePulse { get; set; } = string.Empty;
            public string CacheProbe { get; set; } = string.Empty;
        }

        private sealed class EntityQueryTacticsCollectionConfig
        {
            public string UiBox { get; set; } = string.Empty;
            public string CommandSourceMirror { get; set; } = string.Empty;
            public string FormationPrimary { get; set; } = string.Empty;
            public string SelectedFriendliesResult { get; set; } = string.Empty;
            public string HostileThreatResult { get; set; } = string.Empty;
            public string FormationCacheResult { get; set; } = string.Empty;
        }

        private sealed class EntityQueryTacticsGraphConfig
        {
            public string SelectedFriendlies { get; set; } = string.Empty;
            public string HostileThreats { get; set; } = string.Empty;
            public string FormationCache { get; set; } = string.Empty;
        }

        private sealed class EntityQueryTacticsSummaryKeys
        {
            public string SelectedCount { get; set; } = string.Empty;
            public string SelectedCommandPower { get; set; } = string.Empty;
            public string SelectedSupply { get; set; } = string.Empty;
            public string SelectedBestEntity { get; set; } = string.Empty;
            public string ThreatCount { get; set; } = string.Empty;
            public string ThreatSum { get; set; } = string.Empty;
            public string ThreatAverage { get; set; } = string.Empty;
            public string ThreatMax { get; set; } = string.Empty;
            public string ThreatBestEntity { get; set; } = string.Empty;
            public string FormationCount { get; set; } = string.Empty;
            public string FormationMaxCommandPower { get; set; } = string.Empty;
            public string FormationMinSupply { get; set; } = string.Empty;
            public string FormationBestEntity { get; set; } = string.Empty;
        }

        private sealed class EntityQueryTacticsRelationshipNames
        {
            public string TacticalIntel { get; set; } = string.Empty;
        }

        private sealed class EntityQueryTacticsMetricNames
        {
            public string Threat { get; set; } = string.Empty;
            public string Focus { get; set; } = string.Empty;
        }

        private sealed class EntityQueryTacticsFlagNames
        {
            public string PriorityTarget { get; set; } = string.Empty;
        }

        private sealed class EntityQueryTacticsTagNames
        {
            public string Commandable { get; set; } = string.Empty;
            public string Routed { get; set; } = string.Empty;
            public string Objective { get; set; } = string.Empty;
        }

        private sealed class EntityQueryTacticsAttributes
        {
            public string CommandPower { get; set; } = string.Empty;
            public string Supply { get; set; } = string.Empty;
            public string ThreatValue { get; set; } = string.Empty;
        }

        private sealed class EntityQueryTacticsLogs
        {
            public string SystemInstalled { get; set; } = string.Empty;
            public string ScenarioReady { get; set; } = string.Empty;
            public string SelectionCommitted { get; set; } = string.Empty;
            public string GraphsExecuted { get; set; } = string.Empty;
            public string FormationRotated { get; set; } = string.Empty;
            public string PressurePulse { get; set; } = string.Empty;
            public string CacheProbe { get; set; } = string.Empty;
        }

        private sealed class EntityQueryTacticsPresentationText
        {
            public string Title { get; set; } = string.Empty;
            public string ControlsLine { get; set; } = string.Empty;
            public string ArchitectureLine { get; set; } = string.Empty;
        }

        private sealed class EntityQueryTacticsDemoPlaybackConfig
        {
            public bool Enabled { get; set; }
            public string ActivationEnv { get; set; } = string.Empty;
            public EntityQueryTacticsDemoStepConfig[] Steps { get; set; } = Array.Empty<EntityQueryTacticsDemoStepConfig>();
        }

        private sealed class EntityQueryTacticsDemoStepConfig
        {
            public uint Frame { get; set; }
            public string Op { get; set; } = string.Empty;
            public string[] Entities { get; set; } = Array.Empty<string>();
        }

        private sealed class TestInputBackend : IInputBackend
        {
            private readonly Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);
            private Vector2 _mousePosition;

            public void SetButton(string path, bool isDown)
            {
                _buttons[path] = isDown;
            }

            public void SetMousePosition(Vector2 position)
            {
                _mousePosition = position;
            }

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out bool isDown) && isDown;
            public Vector2 GetMousePosition() => _mousePosition;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private sealed class StubViewController : IViewController
        {
            public StubViewController(float width, float height)
            {
                Resolution = new Vector2(width, height);
            }

            public Vector2 Resolution { get; }
            public float Fov => 60f;
            public float AspectRatio => Resolution.Y <= 0f ? 1f : Resolution.X / Resolution.Y;
        }

        private sealed class WorldMappedScreenRayProvider : IScreenRayProvider
        {
            public ScreenRay GetRay(Vector2 screenPosition)
            {
                return new ScreenRay(
                    new Vector3(screenPosition.X / 100f, 10f, screenPosition.Y / 100f),
                    -Vector3.UnitY);
            }
        }

        private sealed class WorldMappedScreenProjector : IScreenProjector
        {
            public Vector2 WorldToScreen(Vector3 worldPosition)
            {
                return new Vector2(worldPosition.X * 100f, worldPosition.Z * 100f);
            }
        }
    }
}
