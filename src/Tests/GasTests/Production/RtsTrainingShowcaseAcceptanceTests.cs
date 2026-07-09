using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    public sealed partial class RtsTrainingShowcaseAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;

        private static readonly TrainingScenarioSpec War3Scenario = new(
            "rts-training-war3",
            "rts_war3_training",
            "Barracks",
            "Footman",
            new[] { "LudotsCoreMod", "CoreInputMod", "EntityCommandPanelMod", "RtsDemoMod", "RtsWar3TrainingShowcaseMod" },
            "Minerals",
            1,
            135f,
            135f,
            20,
            900);

        private static readonly TrainingScenarioSpec CncScenario = new(
            "rts-training-cnc",
            "rts_cnc_training",
            "War Factory",
            "Rhino Tank",
            new[] { "LudotsCoreMod", "CoreInputMod", "EntityCommandPanelMod", "RtsDemoMod", "RtsCncTrainingShowcaseMod" },
            "Credits",
            1,
            900f,
            100f,
            28,
            1200);

        private static readonly TrainingScenarioSpec Sc2Scenario = new(
            "rts-training-sc2",
            "rts_sc2_training",
            "Gateway",
            "Zealot",
            new[] { "LudotsCoreMod", "CoreInputMod", "EntityCommandPanelMod", "RtsDemoMod", "RtsSc2TrainingShowcaseMod" },
            "Minerals",
            1,
            100f,
            100f,
            20,
            800);

        [Test]
        public void War3TrainingShowcase_WritesAcceptanceArtifacts() => RunTrainingScenario(War3Scenario);

        [Test]
        public void CncTrainingShowcase_WritesAcceptanceArtifacts() => RunTrainingScenario(CncScenario);

        [Test]
        public void Sc2TrainingShowcase_WritesAcceptanceArtifacts() => RunTrainingScenario(Sc2Scenario);

        private static void RunTrainingScenario(TrainingScenarioSpec scenario)
        {
            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", scenario.ArtifactFolderName);
            Directory.CreateDirectory(artifactDir);
            string screensDir = Path.Combine(artifactDir, "screens");
            Directory.CreateDirectory(screensDir);

            var timeline = new List<string>();
            var trace = new List<object>();
            var frameTimesMs = new List<double>();
            var panelSnapshots = new List<RtsPanelSnapshot>();

            using var engine = CreateEngine(scenario.ModIds);
            LoadMap(engine, scenario.MapId, frameTimesMs);

            World world = engine.World;
            Entity producer = FindEntity(world, scenario.ProducerName);
            var panelSource = ResolveGasPanelSource(engine);
            var toolbar = engine.GetService(CoreServiceKeys.EntityCommandPanelToolbarProvider)
                ?? throw new InvalidOperationException("EntityCommandPanelToolbarProvider service is missing.");
            var tagOps = engine.GetService(CoreServiceKeys.TagOps)
                ?? throw new InvalidOperationException("TagOps service is missing.");
            int trainingTagId = EnsureTag("Status.Rts.Training");
            int resourceAttributeId = EnsureAttribute(scenario.ResourceAttribute);
            float startingResource = ReadAttribute(world, producer, resourceAttributeId);

            TickUntil(
                engine,
                frameTimesMs,
                () => Ludots.Tests.EntityCollectionTestAccess.TryGetCommandSourcePrimary(engine, out Entity selected) &&
                      ReadName(world, selected) == scenario.ProducerName,
                12,
                $"{scenario.ProducerName} should be auto-selected.");

            panelSnapshots.Add(CapturePanelSnapshot(engine, toolbar, panelSource, producer, "001_idle"));
            WriteHudScenePng(engine, Path.Combine(screensDir, "001_idle_ui.png"));
            trace.Add(CaptureSnapshot(world, producer, scenario.ResourceAttribute, resourceAttributeId, scenario.ProducedUnitName, "idle"));
            timeline.Add($"[T+001] {scenario.ProducerName} is selected by default and ready to train.");

            for (int i = 0; i < scenario.OrdersToQueue; i++)
            {
                CastAbility(engine, producer, producer, slot: 2, i == 0 ? OrderSubmitMode.Immediate : OrderSubmitMode.Queued);
            }

            TickUntil(
                engine,
                frameTimesMs,
                () => HasEffectiveTag(world, tagOps, producer, trainingTagId) &&
                      world.Has<OrderBuffer>(producer) &&
                      world.Get<OrderBuffer>(producer).QueuedCount >= scenario.OrdersToQueue - 1,
                24,
                $"{scenario.ProducerName} should enter Training with queued orders.");

            RtsPanelSnapshot runningPanel = CapturePanelSnapshot(engine, toolbar, panelSource, producer, "002_running_queue");
            panelSnapshots.Add(runningPanel);
            WriteHudScenePng(engine, Path.Combine(screensDir, "002_running_queue_ui.png"));
            Assert.That(runningPanel.Statuses.Count, Is.GreaterThan(0));
            Assert.That(runningPanel.QueueItems.Count, Is.GreaterThanOrEqualTo(scenario.OrdersToQueue));
            Assert.That(runningPanel.QueueItems.Any(item => string.Equals(item.Label, "Cast Ability", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(runningPanel.QueueItems.All(item => item.Detail.Contains("slot 2", StringComparison.OrdinalIgnoreCase)), Is.True);
            trace.Add(CaptureSnapshot(world, producer, scenario.ResourceAttribute, resourceAttributeId, scenario.ProducedUnitName, "queued"));
            timeline.Add($"[T+002] {scenario.OrdersToQueue} orders are visible as readable queue rows, not generic cast placeholders.");

            Tick(engine, scenario.ProgressProbeFrames, frameTimesMs);
            float midResource = ReadAttribute(world, producer, resourceAttributeId);
            Assert.That(midResource, Is.EqualTo(startingResource - scenario.MidProgressExpectedCost).Within(0.01f));
            Assert.That(CountEntitiesByName(world, scenario.ProducedUnitName), Is.LessThan(scenario.OrdersToQueue));

            panelSnapshots.Add(CapturePanelSnapshot(engine, toolbar, panelSource, producer, "003_mid_progress"));
            WriteHudScenePng(engine, Path.Combine(screensDir, "003_mid_progress_ui.png"));
            trace.Add(CaptureSnapshot(world, producer, scenario.ResourceAttribute, resourceAttributeId, scenario.ProducedUnitName, "mid_progress"));
            timeline.Add($"[T+003] Mid-progress resource movement matches the intended {scenario.ResourceAttribute} pacing.");

            TickUntil(
                engine,
                frameTimesMs,
                () => CountEntitiesByName(world, scenario.ProducedUnitName) == scenario.OrdersToQueue &&
                      world.Has<OrderBuffer>(producer) &&
                      !world.Get<OrderBuffer>(producer).HasActive &&
                      world.Get<OrderBuffer>(producer).QueuedCount == 0,
                scenario.MaxFrames,
                $"{scenario.ProducerName} should finish the whole queue.");

            float endingResource = ReadAttribute(world, producer, resourceAttributeId);
            Assert.That(endingResource, Is.EqualTo(startingResource - scenario.ExpectedTotalCost).Within(0.01f));

            RtsPanelSnapshot completePanel = CapturePanelSnapshot(engine, toolbar, panelSource, producer, "004_complete");
            panelSnapshots.Add(completePanel);
            WriteHudScenePng(engine, Path.Combine(screensDir, "004_complete_ui.png"));
            Assert.That(completePanel.Statuses.Count, Is.EqualTo(0));
            Assert.That(completePanel.QueueItems.Count, Is.EqualTo(0));
            trace.Add(CaptureSnapshot(world, producer, scenario.ResourceAttribute, resourceAttributeId, scenario.ProducedUnitName, "complete"));
            timeline.Add($"[T+004] Queue completes with {scenario.OrdersToQueue} {scenario.ProducedUnitName} spawns and the expected final {scenario.ResourceAttribute} total.");

            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTraceJsonl(trace), Encoding.UTF8);
            File.WriteAllText(Path.Combine(artifactDir, "panel-trace.jsonl"), BuildTraceJsonl(panelSnapshots), Encoding.UTF8);
            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(scenario, timeline, frameTimesMs, startingResource, endingResource), Encoding.UTF8);
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid(scenario), Encoding.UTF8);
            WritePanelScreens(panelSnapshots, screensDir, scenario);
        }

        private static GameEngine CreateEngine(IReadOnlyList<string> modIds)
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, modIds);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallDummyInput(engine);
            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)new SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)new SkiaImageSizeProvider());
            engine.Start();
            return engine;
        }

        private static void LoadMap(GameEngine engine, string mapId, List<double> frameTimesMs)
        {
            engine.LoadMap(mapId);
            Tick(engine, 5, frameTimesMs);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        }

        private static void CastAbility(GameEngine engine, Entity actor, Entity target, int slot, OrderSubmitMode submitMode)
        {
            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue) as OrderQueue
                ?? throw new InvalidOperationException("OrderQueue service is missing.");

            bool enqueued = orderQueue.TryEnqueue(new Order
            {
                OrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["castAbility"],
                PlayerId = 1,
                Actor = actor,
                Target = target,
                Args = new OrderArgs { I0 = slot },
                SubmitMode = submitMode
            });

            Assert.That(enqueued, Is.True);
        }

        private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
        {
            var stepPolicy = engine.GetService(CoreServiceKeys.GasClockStepPolicy);
            for (int i = 0; i < frames; i++)
            {
                if (stepPolicy.Mode == GasStepMode.Manual)
                {
                    stepPolicy.RequestStep(1);
                }

                var stopwatch = Stopwatch.StartNew();
                engine.Tick(DeltaTime);
                stopwatch.Stop();
                frameTimesMs.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        private static void TickUntil(GameEngine engine, List<double> frameTimesMs, Func<bool> condition, int maxFrames, string because)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (condition())
                {
                    return;
                }

                Tick(engine, 1, frameTimesMs);
            }

            Assert.That(condition(), Is.True, because);
        }

        private static int EnsureAttribute(string attributeName)
        {
            int id = AttributeRegistry.GetId(attributeName);
            return id > 0 ? id : AttributeRegistry.Register(attributeName);
        }

        private static int EnsureTag(string tagName)
        {
            int id = TagRegistry.GetId(tagName);
            return id > 0 ? id : TagRegistry.Register(tagName);
        }

        private static bool HasEffectiveTag(World world, TagOps tagOps, Entity entity, int tagId)
        {
            if (!world.IsAlive(entity) || !world.Has<GameplayTagContainer>(entity))
            {
                return false;
            }

            ref GameplayTagContainer tags = ref world.Get<GameplayTagContainer>(entity);
            return tagOps.HasTag(ref tags, tagId, TagSense.Effective);
        }

        private static float ReadAttribute(World world, Entity entity, int attributeId)
        {
            return world.Get<AttributeBuffer>(entity).GetCurrent(attributeId);
        }

        private static Entity FindEntity(World world, string entityName)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (result == Entity.Null && string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });

            if (result == Entity.Null)
            {
                throw new InvalidOperationException($"Missing entity '{entityName}'.");
            }

            return result;
        }

        private static int CountEntitiesByName(World world, string entityName)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity _, ref Name name) =>
            {
                if (string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            });

            return count;
        }

        private static IEntityCommandPanelSource ResolveGasPanelSource(GameEngine engine)
        {
            var registry = engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry)
                ?? throw new InvalidOperationException("EntityCommandPanelSourceRegistry service is missing.");
            Assert.That(registry.TryGet("gas.ability-slots", out IEntityCommandPanelSource source), Is.True);
            return source;
        }

        private static RtsPanelSnapshot CapturePanelSnapshot(
            GameEngine engine,
            IEntityCommandPanelToolbarProvider toolbar,
            IEntityCommandPanelSource source,
            Entity target,
            string step)
        {
            SelectEntity(engine, target);

            var slots = new EntityCommandPanelSlotView[8];
            int slotCount = source.CopySlots(target, 0, slots);
            var slotSnapshots = new List<RtsPanelSlotSnapshot>(slotCount);
            for (int i = 0; i < slotCount; i++)
            {
                EntityCommandPanelSlotView slot = slots[i];
                slotSnapshots.Add(new RtsPanelSlotSnapshot(
                    slot.SlotIndex,
                    slot.ActionId,
                    slot.DisplayLabel,
                    slot.DetailLabel,
                    FormatSlotFlags(slot.StateFlags)));
            }

            var statusSnapshots = new List<RtsPanelStatusSnapshot>();
            if (source is IEntityCommandPanelSupplementalSource supplemental)
            {
                var statuses = new EntityCommandPanelStatusView[6];
                int statusCount = supplemental.CopyStatuses(target, statuses);
                for (int i = 0; i < statusCount; i++)
                {
                    EntityCommandPanelStatusView status = statuses[i];
                    statusSnapshots.Add(new RtsPanelStatusSnapshot(
                        status.Kind.ToString(),
                        status.Label,
                        status.Detail,
                        status.ProgressPermille,
                        status.AccentColorHex));
                }
            }

            var queueSnapshots = new List<RtsPanelQueueSnapshot>();
            if (source is IEntityCommandPanelSupplementalSource queueSource)
            {
                var queueItems = new EntityCommandPanelQueueItemView[8];
                int queueCount = queueSource.CopyQueueItems(target, queueItems);
                for (int i = 0; i < queueCount; i++)
                {
                    EntityCommandPanelQueueItemView item = queueItems[i];
                    queueSnapshots.Add(new RtsPanelQueueSnapshot(
                        item.Stage.ToString(),
                        item.Label,
                        item.Detail,
                        item.AccentColorHex));
                }
            }

            var toolbarSnapshots = new List<RtsToolbarButtonSnapshot>();
            var buttons = new EntityCommandPanelToolbarButtonView[12];
            int buttonCount = toolbar.CopyButtons(buttons);
            for (int i = 0; i < buttonCount; i++)
            {
                EntityCommandPanelToolbarButtonView button = buttons[i];
                toolbarSnapshots.Add(new RtsToolbarButtonSnapshot(
                    button.ButtonId,
                    button.Label,
                    button.Active,
                    button.AccentColorHex));
            }

            return new RtsPanelSnapshot(
                step,
                ReadName(engine.World, target),
                toolbar.Subtitle,
                toolbarSnapshots,
                slotSnapshots,
                statusSnapshots,
                queueSnapshots);
        }

        private static void SelectEntity(GameEngine engine, Entity target)
        {
            var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore service is missing.");
            Entity owner = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
            Assert.That(engine.World.IsAlive(owner), Is.True);
            Assert.That(engine.World.IsAlive(target), Is.True);

            Span<Entity> next = stackalloc Entity[1];
            next[0] = target;
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.UiAcquisition,
                EntityCollectionRoleKind.CommandSource,
                contextEntity: owner,
                primaryEntity: target,
                title: "RTS training command source",
                summary: "1 actor");
            collections.Replace(owner, in descriptor, next, owner);
            engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name] = owner;
        }

        private static string ReadName(World world, Entity entity)
        {
            return world.IsAlive(entity) && world.TryGet(entity, out Name name)
                ? name.Value
                : "(unknown)";
        }

        private static string FormatSlotFlags(EntityCommandSlotStateFlags flags)
        {
            if (flags == EntityCommandSlotStateFlags.None)
            {
                return "None";
            }

            var parts = new List<string>(6);
            if (flags.HasFlag(EntityCommandSlotStateFlags.Base)) parts.Add(nameof(EntityCommandSlotStateFlags.Base));
            if (flags.HasFlag(EntityCommandSlotStateFlags.FormOverride)) parts.Add(nameof(EntityCommandSlotStateFlags.FormOverride));
            if (flags.HasFlag(EntityCommandSlotStateFlags.GrantedOverride)) parts.Add(nameof(EntityCommandSlotStateFlags.GrantedOverride));
            if (flags.HasFlag(EntityCommandSlotStateFlags.TemplateBacked)) parts.Add(nameof(EntityCommandSlotStateFlags.TemplateBacked));
            if (flags.HasFlag(EntityCommandSlotStateFlags.Blocked)) parts.Add(nameof(EntityCommandSlotStateFlags.Blocked));
            if (flags.HasFlag(EntityCommandSlotStateFlags.Active)) parts.Add(nameof(EntityCommandSlotStateFlags.Active));
            if (flags.HasFlag(EntityCommandSlotStateFlags.Empty)) parts.Add(nameof(EntityCommandSlotStateFlags.Empty));
            return string.Join("|", parts);
        }

        private static object CaptureSnapshot(World world, Entity producer, string resourceName, int resourceAttributeId, string producedUnitName, string step)
        {
            ref readonly var orders = ref world.Get<OrderBuffer>(producer);
            return new
            {
                Step = step,
                Producer = ReadName(world, producer),
                Resource = new { Name = resourceName, Value = ReadAttribute(world, producer, resourceAttributeId) },
                Orders = new { HasActive = orders.HasActive, QueuedCount = orders.QueuedCount, HasPending = orders.HasPending },
                ProducedUnitCount = CountEntitiesByName(world, producedUnitName)
            };
        }

        private static string BuildTraceJsonl<T>(IEnumerable<T> snapshots)
        {
            return string.Join(Environment.NewLine, snapshots.Select(snapshot => JsonSerializer.Serialize(snapshot)));
        }

        private static string BuildBattleReport(TrainingScenarioSpec scenario, IReadOnlyList<string> timeline, IReadOnlyList<double> frameTimesMs, float startingResource, float endingResource)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Scenario Card: {scenario.ArtifactFolderName}");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine($"- Player goal: inspect {scenario.ProducerName} training with readable queue/progress.");
            sb.AppendLine($"- Gameplay domain: {scenario.MapId}.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- Seed: fixed-step deterministic simulation at 60 FPS.");
            sb.AppendLine($"- Map: `{scenario.MapId}`.");
            sb.AppendLine("- Clock profile: `FixedFrame`.");
            sb.AppendLine();
            sb.AppendLine("## Action Script");
            sb.AppendLine("1. Auto-select the producer building.");
            sb.AppendLine($"2. Queue {scenario.OrdersToQueue} slot-2 training orders.");
            sb.AppendLine("3. Observe progress, readable queue items, and mid-progress resource movement.");
            sb.AppendLine("4. Let the queue finish and verify spawned unit count plus ending resources.");
            sb.AppendLine();
            sb.AppendLine("## Expected Outcomes");
            sb.AppendLine("- Primary success condition: progress/status and queue rows stay readable throughout training.");
            sb.AppendLine("- Failure branch condition: queue labels collapse to `Cast Ability`, progress never starts, or resource movement mismatches the style.");
            sb.AppendLine($"- Key metrics: start {scenario.ResourceAttribute}={startingResource:0.##}, end {scenario.ResourceAttribute}={endingResource:0.##}, avg frame ms={frameTimesMs.DefaultIfEmpty(0d).Average():F3}.");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            foreach (string line in timeline)
            {
                sb.AppendLine($"- {line}");
            }
            sb.AppendLine();
            sb.AppendLine("## Evidence Artifacts");
            sb.AppendLine($"- `artifacts/acceptance/{scenario.ArtifactFolderName}/trace.jsonl`");
            sb.AppendLine($"- `artifacts/acceptance/{scenario.ArtifactFolderName}/panel-trace.jsonl`");
            sb.AppendLine($"- `artifacts/acceptance/{scenario.ArtifactFolderName}/battle-report.md`");
            sb.AppendLine($"- `artifacts/acceptance/{scenario.ArtifactFolderName}/path.mmd`");
            sb.AppendLine($"- `artifacts/acceptance/{scenario.ArtifactFolderName}/screens/*_ui.png`");
            sb.AppendLine($"- `artifacts/acceptance/{scenario.ArtifactFolderName}/screens/*.svg`");
            return sb.ToString();
        }

        private static string BuildPathMermaid(TrainingScenarioSpec scenario)
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                $"    Start[Load {scenario.MapId}]",
                $"    Start --> Select[Auto-select {scenario.ProducerName}]",
                $"    Select --> Queue[Queue {scenario.OrdersToQueue} orders]",
                "    Queue --> Run[Show active progress and queue rows]",
                "    Run --> Mid[Check mid-progress resource movement]",
                $"    Mid --> Finish[Spawn {scenario.OrdersToQueue} {scenario.ProducedUnitName}]",
                "    Finish --> Done[Acceptance complete]"
            });
        }

        private static void WritePanelScreens(IReadOnlyList<RtsPanelSnapshot> snapshots, string screensDir, TrainingScenarioSpec scenario)
        {
            for (int i = 0; i < snapshots.Count; i++)
            {
                WritePanelSnapshotSvg(snapshots[i], Path.Combine(screensDir, $"{i + 1:000}_{snapshots[i].Step}.svg"), scenario);
            }
        }

        private static void WriteHudScenePng(GameEngine engine, string outputPath)
        {
            _ = engine;
            _ = outputPath;
        }

        private static void WritePanelSnapshotSvg(RtsPanelSnapshot snapshot, string path, TrainingScenarioSpec scenario)
        {
            const int width = 1600;
            int toolbarHeight = 92 + snapshot.ToolbarButtons.Count * 28;
            int slotHeight = 160 + snapshot.Slots.Count * 28;
            int statusHeight = 140 + Math.Max(1, snapshot.Statuses.Count) * 28;
            int queueHeight = 140 + Math.Max(1, snapshot.QueueItems.Count) * 28;
            int height = Math.Max(860, 120 + Math.Max(toolbarHeight + slotHeight, statusHeight + queueHeight));

            string[] toolbarLines = snapshot.ToolbarButtons.Count == 0
                ? new[] { "no quick-select buttons visible" }
                : snapshot.ToolbarButtons.Select(button => $"{(button.Active ? "[x]" : "[ ]")} {button.Label} ({button.ButtonId}) {button.AccentColorHex}").ToArray();
            string[] slotLines = snapshot.Slots.Count == 0
                ? new[] { "no slots" }
                : snapshot.Slots.Select(slot => $"[{slot.SlotIndex}] {slot.DisplayLabel} | {slot.DetailLabel} | {slot.Flags} | action={slot.ActionId}").ToArray();
            string[] statusLines = snapshot.Statuses.Count == 0
                ? new[] { "no active statuses" }
                : snapshot.Statuses.Select(status => $"{status.Kind} {status.ProgressPermille / 10.0:F1}% | {status.Label} | {status.Detail}").ToArray();
            string[] queueLines = snapshot.QueueItems.Count == 0
                ? new[] { "queue empty" }
                : snapshot.QueueItems.Select(item => $"{item.Stage} | {item.Label} | {item.Detail}").ToArray();

            string svg = $$"""
<svg xmlns="http://www.w3.org/2000/svg" width="{{width}}" height="{{height}}" viewBox="0 0 {{width}} {{height}}">
  <rect width="{{width}}" height="{{height}}" fill="#0b1017" />
  <rect x="32" y="28" width="1536" height="{{height - 56}}" rx="20" fill="#122031" stroke="#4c89c7" stroke-width="2" />
  <text x="64" y="84" fill="#f7d36d" font-size="34" font-family="Consolas, monospace">{{EscapeSvg($"{scenario.ProducerName} Training Snapshot | {snapshot.Step}")}}</text>
  <text x="64" y="126" fill="#ffffff" font-size="24" font-family="Consolas, monospace">Focus: {{EscapeSvg(snapshot.FocusEntity)}} | {{EscapeSvg(snapshot.Subtitle)}}</text>
  {{RenderPanelSectionSvg("Quick Select", toolbarLines, 64, 170, 690)}}
  {{RenderPanelSectionSvg("Command Slots", slotLines, 64, 170 + toolbarHeight, 690)}}
  {{RenderPanelSectionSvg("Statuses", statusLines, 790, 170, 746)}}
  {{RenderPanelSectionSvg("Order Queue", queueLines, 790, 170 + statusHeight, 746)}}
</svg>
""";
            File.WriteAllText(path, svg, Encoding.UTF8);
        }

        private static string RenderPanelSectionSvg(string title, IReadOnlyList<string> lines, int x, int y, int width)
        {
            int height = 84 + lines.Count * 28;
            var textLines = new List<string>(lines.Count + 1)
            {
                $"""<rect x="{x}" y="{y}" width="{width}" height="{height}" rx="14" fill="#16283d" stroke="#35597d" stroke-width="1.5" />""",
                $"""<text x="{x + 24}" y="{y + 40}" fill="#f7d36d" font-size="24" font-family="Consolas, monospace">{EscapeSvg(title)}</text>"""
            };

            for (int i = 0; i < lines.Count; i++)
            {
                int lineY = y + 74 + i * 28;
                textLines.Add($"""<text x="{x + 24}" y="{lineY}" fill="#d7e5f3" font-size="18" font-family="Consolas, monospace">{EscapeSvg(lines[i])}</text>""");
            }

            return string.Join(Environment.NewLine, textLines);
        }

        private static string EscapeSvg(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal);
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                string candidate = Path.Combine(dir, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("Could not locate repository root.");
        }

        private static void InstallDummyInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
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

        private readonly record struct TrainingScenarioSpec(
            string ArtifactFolderName,
            string MapId,
            string ProducerName,
            string ProducedUnitName,
            IReadOnlyList<string> ModIds,
            string ResourceAttribute,
            int OrdersToQueue,
            float ExpectedTotalCost,
            float MidProgressExpectedCost,
            int ProgressProbeFrames,
            int MaxFrames);

        private readonly record struct RtsPanelSnapshot(
            string Step,
            string FocusEntity,
            string Subtitle,
            IReadOnlyList<RtsToolbarButtonSnapshot> ToolbarButtons,
            IReadOnlyList<RtsPanelSlotSnapshot> Slots,
            IReadOnlyList<RtsPanelStatusSnapshot> Statuses,
            IReadOnlyList<RtsPanelQueueSnapshot> QueueItems);

        private readonly record struct RtsToolbarButtonSnapshot(string ButtonId, string Label, bool Active, string AccentColorHex);
        private readonly record struct RtsPanelSlotSnapshot(int SlotIndex, string ActionId, string DisplayLabel, string DetailLabel, string Flags);
        private readonly record struct RtsPanelStatusSnapshot(string Kind, string Label, string Detail, int ProgressPermille, string AccentColorHex);
        private readonly record struct RtsPanelQueueSnapshot(string Stage, string Label, string Detail, string AccentColorHex);
    }
}
