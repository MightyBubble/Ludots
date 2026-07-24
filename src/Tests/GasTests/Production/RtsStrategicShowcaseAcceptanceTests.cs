using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;
using RtsDemoMod.Systems;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    [Category("acceptance")]
    public sealed class RtsStrategicShowcaseAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string ArtifactFolderName = "rts-strategic-showcase";
        private const string MapId = "rts_entry";

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "EntityCommandPanelMod",
            "RtsDemoMod"
        };

        [Test]
        public void GivenMoreThanLegacyRelationScratchCapacity_WhenRuntimeUpdates_ThenItAllocatesNothingAndKeepsEveryChildAttached()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            LoadMap(engine, MapId, frameTimesMs);
            World world = engine.World;
            Entity parent = world.Create(
                WorldPositionCm.FromCm(100, 200),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(90, 190) });
            var children = new Entity[300];
            for (int i = 0; i < children.Length; i++)
            {
                children[i] = world.Create(
                    new ChildOf { Parent = parent },
                    WorldPositionCm.FromCm(i, i),
                    new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(i, i) });
            }

            using var runtime = new RtsRelationRuntimeSystem(engine, 512);
            runtime.Update(DeltaTime);
            long before = GC.GetAllocatedBytesForCurrentThread();
            runtime.Update(DeltaTime);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero, "The steady-state RTS relation pass must not allocate.");
            for (int i = 0; i < children.Length; i++)
            {
                Assert.That(world.Get<ChildOf>(children[i]).Parent, Is.EqualTo(parent));
                Assert.That(world.Get<WorldPositionCm>(children[i]).Value, Is.EqualTo(Fix64Vec2.FromInt(100, 200)));
            }
        }

        [Test]
        public void GivenRelationScratchCapacityIsExceeded_WhenRuntimeUpdates_ThenItFailsBeforeChangingTheWorld()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            LoadMap(engine, MapId, frameTimesMs);
            World world = engine.World;
            Entity first = world.Create(new CommandSourceSelectableTag());
            Entity second = world.Create(new CommandSourceSelectableTag());
            Entity overflow = world.Create(new CommandSourceSelectableTag());

            using var runtime = new RtsRelationRuntimeSystem(engine, 2);
            var error = Assert.Throws<InvalidOperationException>(() => runtime.Update(DeltaTime));

            Assert.That(error!.Message, Does.StartWith("RTS.RELATION.ERR.ScratchCapacityExceeded"));
            Assert.That(world.Has<CommandSourceSelectableState>(first), Is.False);
            Assert.That(world.Has<CommandSourceSelectableState>(second), Is.False);
            Assert.That(world.Has<CommandSourceSelectableState>(overflow), Is.False);
        }

        [Test]
        public void RtsToolbar_ResetCameraButton_WritesDefaultCameraRequests()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            LoadMap(engine, MapId, frameTimesMs);

            var toolbar = engine.GetService(CoreServiceKeys.EntityCommandPanelToolbarProvider)
                ?? throw new InvalidOperationException("EntityCommandPanelToolbarProvider service is missing.");

            var buttons = new EntityCommandPanelToolbarButtonView[12];
            int buttonCount = toolbar.CopyButtons(buttons);
            Assert.That(buttonCount, Is.GreaterThan(0));
            Assert.That(
                buttons.Take(buttonCount).Any(button => string.Equals(button.ButtonId, "camera_reset", StringComparison.Ordinal)),
                Is.True,
                "RTS toolbar should expose a camera reset button.");

            toolbar.Activate("camera_reset");

            Assert.That(engine.GlobalContext.TryGetValue(CoreServiceKeys.VirtualCameraRequest.Name, out object? virtualRequestObj), Is.True);
            Assert.That(virtualRequestObj, Is.TypeOf<Ludots.Core.Gameplay.Camera.VirtualCameraRequest>());
            var virtualRequest = (Ludots.Core.Gameplay.Camera.VirtualCameraRequest)virtualRequestObj;
            Assert.That(virtualRequest.Id, Is.EqualTo("Rts"));
            Assert.That(virtualRequest.ResetRuntimeState, Is.True);

            Assert.That(engine.GlobalContext.TryGetValue(CoreServiceKeys.CameraPoseRequest.Name, out object? poseRequestObj), Is.True);
            Assert.That(poseRequestObj, Is.TypeOf<Ludots.Core.Gameplay.Camera.CameraPoseRequest>());
            var poseRequest = (Ludots.Core.Gameplay.Camera.CameraPoseRequest)poseRequestObj;
            Assert.That(poseRequest.VirtualCameraId, Is.EqualTo("Rts"));
            Assert.That(poseRequest.TargetCm, Is.EqualTo(new Vector2(0f, 0f)));
            Assert.That(poseRequest.Yaw, Is.EqualTo(180f));
            Assert.That(poseRequest.Pitch, Is.EqualTo(55f));
            Assert.That(poseRequest.DistanceCm, Is.EqualTo(14000f));
            Assert.That(poseRequest.FovYDeg, Is.EqualTo(60f));
        }

        [Test]
        public void RtsToolbar_SelectEntity_SelectsAndFocusesTarget()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            LoadMap(engine, MapId, frameTimesMs);

            var toolbar = engine.GetService(CoreServiceKeys.EntityCommandPanelToolbarProvider)
                ?? throw new InvalidOperationException("EntityCommandPanelToolbarProvider service is missing.");
            Entity barracks = FindEntity(engine.World, "Barracks");

            toolbar.Activate("war3_train");

            Assert.That(
                Ludots.Tests.EntityCollectionTestAccess.TryGetCommandSourcePrimary(engine, out Entity selected),
                Is.True,
                "Selecting from the RTS toolbar should seed a real primary selection.");
            Assert.That(selected, Is.EqualTo(barracks));

            Assert.That(engine.GlobalContext.TryGetValue(CoreServiceKeys.VirtualCameraRequest.Name, out object? virtualRequestObj), Is.True);
            Assert.That(virtualRequestObj, Is.TypeOf<Ludots.Core.Gameplay.Camera.VirtualCameraRequest>());
            var virtualRequest = (Ludots.Core.Gameplay.Camera.VirtualCameraRequest)virtualRequestObj;
            Assert.That(virtualRequest.Id, Is.EqualTo("Rts"));

            Assert.That(engine.GlobalContext.TryGetValue(CoreServiceKeys.CameraPoseRequest.Name, out object? poseRequestObj), Is.True);
            Assert.That(poseRequestObj, Is.TypeOf<Ludots.Core.Gameplay.Camera.CameraPoseRequest>());
            var poseRequest = (Ludots.Core.Gameplay.Camera.CameraPoseRequest)poseRequestObj;
            Assert.That(poseRequest.TargetCm, Is.EqualTo(ReadWorldPosition(engine.World, barracks)));
            Assert.That(poseRequest.DistanceCm, Is.EqualTo(10080f).Within(0.01f));
        }

        [Test]
        public void RtsMap_Load_SeedsPrimarySelectionForFirstContact()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            LoadMap(engine, MapId, frameTimesMs);

            Assert.That(
                Ludots.Tests.EntityCollectionTestAccess.TryGetCommandSourcePrimary(engine, out Entity selected),
                Is.True,
                "RTS showcase should auto-select a starter sample so the first-contact UI is coherent.");
            Assert.That(engine.World.Get<Name>(selected).Value, Is.EqualTo("Peasant"));
        }

        [Test]
        public void RtsActors_AreReadable_OnMapLoad_And_AfterProductionSpawns()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            LoadMap(engine, MapId, frameTimesMs);

            World world = engine.World;
            Entity peasant = FindEntity(world, "Peasant");
            Entity barracks = FindEntity(world, "Barracks");
            Entity gateway = FindEntity(world, "Gateway");

            TickUntil(
                engine,
                frameTimesMs,
                () => world.Has<VisualTransform>(peasant) &&
                      world.Has<PreviousWorldPositionCm>(peasant) &&
                      world.Has<PresentationStableId>(peasant) &&
                      world.Has<VisualTransform>(barracks) &&
                      world.Has<VisualTransform>(gateway),
                maxFrames: 8,
                "RTS showcase actors should become visually readable immediately after load.");

            AssertReadableActor(world, peasant, "Peasant");
            AssertReadableActor(world, barracks, "Barracks");
            AssertReadableActor(world, gateway, "Gateway");

            var toolbar = engine.GetService(CoreServiceKeys.EntityCommandPanelToolbarProvider)
                ?? throw new InvalidOperationException("EntityCommandPanelToolbarProvider service is missing.");
            Assert.That(toolbar.Subtitle, Does.Contain("RMB"));
            Assert.That(toolbar.Subtitle, Does.Contain("SC2 Warp"));

            var footmanIdsBefore = SnapshotEntityIdsByName(world, "Footman");
            CastAbility(engine, barracks, barracks, slot: 2);
            TickUntil(
                engine,
                frameTimesMs,
                () => CountEntitiesByName(world, "Footman") == footmanIdsBefore.Count + 1,
                maxFrames: 600,
                "Barracks should train a Footman that also receives readable presentation state.");

            Entity newFootman = FindNewestEntityByName(world, "Footman", footmanIdsBefore);
            TickUntil(
                engine,
                frameTimesMs,
                () => world.Has<VisualTransform>(newFootman) &&
                      world.Has<PreviousWorldPositionCm>(newFootman) &&
                      world.Has<PresentationStableId>(newFootman),
                maxFrames: 8,
                "Produced Footman should receive presentation bootstrap components.");

            AssertReadableActor(world, newFootman, "Trained Footman");
        }

        [Test]
        public void RtsStrategicShowcase_WritesAcceptanceArtifacts()
        {
            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", ArtifactFolderName);
            Directory.CreateDirectory(artifactDir);
            string screensDir = Path.Combine(artifactDir, "screens");
            Directory.CreateDirectory(screensDir);

            var timeline = new List<string>();
            var trace = new List<object>();
            var frameTimesMs = new List<double>();
            var panelSnapshots = new List<RtsPanelSnapshot>();

            using var engine = CreateEngine();
            LoadMap(engine, MapId, frameTimesMs);
            var toolbar = engine.GetService(CoreServiceKeys.EntityCommandPanelToolbarProvider)
                ?? throw new InvalidOperationException("EntityCommandPanelToolbarProvider service is missing.");
            IEntityCommandPanelSource panelSource = ResolveGasPanelSource(engine);

            var world = engine.World;
            var tagOps = engine.GetService(CoreServiceKeys.TagOps)
                ?? throw new InvalidOperationException("TagOps service is missing.");

            int healthAttrId = EnsureAttribute("Health");
            int mineralsAttrId = EnsureAttribute("Minerals");
            int lumberAttrId = EnsureAttribute("Lumber");
            int creditsAttrId = EnsureAttribute("Credits");
            int gasAttrId = EnsureAttribute("Gas");

            int constructingTagId = EnsureTag("State.Rts.Constructing");
            int builderAttachedTagId = EnsureTag("State.Rts.BuilderAttached");
            int morphConsumedTagId = EnsureTag("State.Rts.MorphConsumed");
            int warpingTagId = EnsureTag("State.Rts.Warping");
            int trainingTagId = EnsureTag("Status.Rts.Training");
            int researchingTagId = EnsureTag("Status.Rts.Researching");
            int warpGateTechTagId = EnsureTag("Progression.Rts.WarpGate");

            Entity peasant = FindEntity(world, "Peasant");
            Entity barracks = FindEntity(world, "Barracks");
            Entity guardTower = FindEntity(world, "Guard Tower");
            Entity constructionYard = FindEntity(world, "Construction Yard");
            Entity warFactory = FindEntity(world, "War Factory");
            Entity battleBunker = FindEntity(world, "Battle Bunker");
            Entity rocketTrooper = FindEntity(world, "Rocket Trooper");
            Entity gateway = FindEntity(world, "Gateway");
            Entity drone = FindEntity(world, "Drone");
            Entity baselineFootman = FindEntity(world, "Footman");

            trace.Add(CaptureSnapshot(world, engine, "map_loaded", "Baseline RTS strategic sandbox ready.", peasant, barracks, guardTower, constructionYard, warFactory, battleBunker, gateway, drone));
            timeline.Add("[T+001] rts_entry loaded with Warcraft worker build, C&C placement, Protoss gateway tech, and Zerg morph actors ready.");
            RtsPanelSnapshot peasantPanel = CapturePanelSnapshot(
                engine,
                toolbar,
                panelSource,
                peasant,
                panelSnapshots,
                "001_peasant_build_palette",
                previewSlotIndex: 0,
                previewWorldCm: new Vector2(-1950f, -1150f));
            Assert.That(peasantPanel.Preview?.PerformerId, Is.EqualTo("core_input_preview_build_site"));

            float peasantMineralsBeforeLumberMill = ReadAttribute(world, peasant, mineralsAttrId);
            float peasantLumberBeforeLumberMill = ReadAttribute(world, peasant, lumberAttrId);
            var lumberMillIdsBefore = SnapshotEntityIdsByName(world, "Lumber Mill");
            CastAbilityAtWorldPoint(engine, peasant, slot: 0, new Vector2(-1950f, -1150f));
            TickUntil(engine, frameTimesMs, () => CountEntitiesByName(world, "Lumber Mill") == lumberMillIdsBefore.Count + 1, maxFrames: 20, "Peasant should place a Lumber Mill site.");
            Entity lumberMill = FindNewestEntityByName(world, "Lumber Mill", lumberMillIdsBefore);
            TickUntil(
                engine,
                frameTimesMs,
                () => world.Has<ChildOf>(peasant) && IsSelectionDisabled(world, peasant),
                maxFrames: 20,
                "Peasant should attach after the construction host spawns and become temporarily unselectable.");
            Assert.That(world.Has<ChildOf>(peasant), Is.True, "Peasant should attach to the construction host during Warcraft-style building.");
            Assert.That(world.Get<ChildOf>(peasant).Parent, Is.EqualTo(lumberMill));
            Assert.That(HasEffectiveTag(world, tagOps, peasant, builderAttachedTagId), Is.True);
            Assert.That(HasEffectiveTag(world, tagOps, lumberMill, constructingTagId), Is.True);
            Assert.That(world.Get<CommandSourceSelectableState>(peasant).Enabled, Is.False, "Attached builders should be temporarily unselectable.");
            Assert.That(ReadAttribute(world, peasant, mineralsAttrId), Is.EqualTo(peasantMineralsBeforeLumberMill - 160f).Within(0.01f));
            Assert.That(ReadAttribute(world, peasant, lumberAttrId), Is.EqualTo(peasantLumberBeforeLumberMill - 60f).Within(0.01f));
            trace.Add(CaptureSnapshot(world, engine, "war3_lumber_mill_started", "Peasant entered build relation and the Lumber Mill site is constructing.", peasant, lumberMill));
            timeline.Add("[T+002] War3 build: Peasant.Build(Lumber Mill) spends 160 minerals / 60 lumber, attaches to the site, and freezes selection while the relation is active.");

            TickUntil(
                engine,
                frameTimesMs,
                () => !HasEffectiveTag(world, tagOps, lumberMill, constructingTagId) &&
                      !world.Has<ChildOf>(peasant) &&
                      IsSelectable(world, peasant),
                maxFrames: 1000,
                "Lumber Mill construction should complete and detach the peasant.");
            Assert.That(world.Has<ChildOf>(peasant), Is.False, "Peasant should detach after construction completes.");
            Assert.That(world.Get<CommandSourceSelectableState>(peasant).Enabled, Is.True, "Peasant should become selectable again after detaching.");
            trace.Add(CaptureSnapshot(world, engine, "war3_lumber_mill_complete", "Peasant detached after construction completion.", peasant, lumberMill));
            timeline.Add("[T+003] War3 build completion: Lumber Mill clears Constructing, the worker relation is removed, and the peasant regains interaction.");

            var guardTowerIdsBefore = SnapshotEntityIdsByName(world, "Guard Tower");
            CastAbilityAtWorldPoint(engine, peasant, slot: 3, new Vector2(-230f, -1380f));
            TickUntil(engine, frameTimesMs, () => CountEntitiesByName(world, "Guard Tower") == guardTowerIdsBefore.Count + 1, maxFrames: 20, "Peasant form override should place a new Guard Tower site.");
            Entity builtGuardTower = FindNewestEntityByName(world, "Guard Tower", guardTowerIdsBefore);
            TickUntil(
                engine,
                frameTimesMs,
                () => world.Has<ChildOf>(peasant) && IsSelectionDisabled(world, peasant),
                maxFrames: 20,
                "Peasant should attach to the Guard Tower after spawn and become temporarily unselectable.");
            Assert.That(world.Has<ChildOf>(peasant), Is.True);
            Assert.That(world.Get<ChildOf>(peasant).Parent, Is.EqualTo(builtGuardTower));
            Assert.That(HasEffectiveTag(world, tagOps, builtGuardTower, constructingTagId), Is.True);
            TickUntil(
                engine,
                frameTimesMs,
                () => !HasEffectiveTag(world, tagOps, builtGuardTower, constructingTagId) &&
                      !world.Has<ChildOf>(peasant) &&
                      IsSelectable(world, peasant),
                maxFrames: 1000,
                "Guard Tower construction should complete and release the peasant.");
            Assert.That(world.Has<ChildOf>(peasant), Is.False);
            Assert.That(world.Get<CommandSourceSelectableState>(peasant).Enabled, Is.True);
            trace.Add(CaptureSnapshot(world, engine, "war3_guard_tower_form_override", "Worker form-set slot override placed and completed a Guard Tower.", peasant, builtGuardTower));
            timeline.Add("[T+004] War3 form-set route: the peasant's R-slot override builds a Guard Tower site without new runtime infrastructure.");

            var footmanIdsBeforeTrain = SnapshotEntityIdsByName(world, "Footman");
            float barracksMineralsBefore = ReadAttribute(world, barracks, mineralsAttrId);
            CastAbility(engine, barracks, barracks, slot: 2);
            TickUntil(
                engine,
                frameTimesMs,
                () => HasEffectiveTag(world, tagOps, barracks, trainingTagId),
                maxFrames: 8,
                "Barracks should enter Training.",
                () => BuildTrainingDiagnostics(world, tagOps, barracks, "Footman"));
            RtsPanelSnapshot barracksPanel = CapturePanelSnapshot(engine, toolbar, panelSource, barracks, panelSnapshots, "002_barracks_training_queue");
            Assert.That(barracksPanel.Statuses.Count, Is.GreaterThan(0), "Barracks panel should expose its active training status.");
            Assert.That(barracksPanel.QueueItems.Count, Is.GreaterThan(0), "Barracks panel should expose its order queue.");
            TickUntil(
                engine,
                frameTimesMs,
                () => CountEntitiesByName(world, "Footman") == footmanIdsBeforeTrain.Count + 1,
                maxFrames: 600,
                "Barracks should train a Footman.",
                () => BuildTrainingDiagnostics(world, tagOps, barracks, "Footman"));
            Entity trainedFootman = FindNewestEntityByName(world, "Footman", footmanIdsBeforeTrain);
            Assert.That(ReadAttribute(world, barracks, mineralsAttrId), Is.EqualTo(barracksMineralsBefore - 135f).Within(0.01f));
            trace.Add(CaptureSnapshot(world, engine, "war3_footman_trained", "Barracks trained a fresh Footman after a timed queue.", barracks, baselineFootman, trainedFootman));
            timeline.Add("[T+005] War3 training: Barracks queues Footman production with a Training tag clip, then spawns a second Footman outside the building.");

            CastAbility(engine, trainedFootman, guardTower, slot: 1);
            TickUntil(
                engine,
                frameTimesMs,
                () => world.Has<ChildOf>(trainedFootman) && IsSelectionDisabled(world, trainedFootman),
                maxFrames: 12,
                "Footman should garrison into the Guard Tower and become unselectable.");
            Assert.That(world.Get<ChildOf>(trainedFootman).Parent, Is.EqualTo(guardTower));
            Assert.That(world.Get<CommandSourceSelectableState>(trainedFootman).Enabled, Is.False);
            CastAbility(engine, guardTower, guardTower, slot: 2);
            TickUntil(
                engine,
                frameTimesMs,
                () => !world.Has<ChildOf>(trainedFootman) && IsSelectable(world, trainedFootman),
                maxFrames: 24,
                "Ungarrison should detach the Footman and restore selection.");
            Assert.That(world.Get<CommandSourceSelectableState>(trainedFootman).Enabled, Is.True);
            trace.Add(CaptureSnapshot(world, engine, "war3_garrison_cycle", "Footman entered and exited the tower via relation-based garrison logic.", trainedFootman, guardTower));
            timeline.Add("[T+006] Shared garrison: Footman enters the tower as ChildOf(Target), becomes unselectable, then exits on UngarrisonAll without custom attach stacks.");

            var powerPlantIdsBefore = SnapshotEntityIdsByName(world, "Power Plant");
            float conyardCreditsBeforePower = ReadAttribute(world, constructionYard, creditsAttrId);
            CastAbilityAtWorldPoint(engine, constructionYard, slot: 0, new Vector2(1480f, -1280f));
            TickUntil(engine, frameTimesMs, () => CountEntitiesByName(world, "Power Plant") == powerPlantIdsBefore.Count + 1, maxFrames: 20, "Construction Yard should place a Power Plant.");
            Entity powerPlant = FindNewestEntityByName(world, "Power Plant", powerPlantIdsBefore);
            TickUntil(engine, frameTimesMs, () => HasEffectiveTag(world, tagOps, powerPlant, constructingTagId), maxFrames: 8, "Power Plant should enter Constructing after spawn.");
            Assert.That(ReadAttribute(world, constructionYard, creditsAttrId), Is.EqualTo(conyardCreditsBeforePower - 500f).Within(0.01f));
            TickUntil(engine, frameTimesMs, () => !HasEffectiveTag(world, tagOps, powerPlant, constructingTagId), maxFrames: 300, "Power Plant should finish rising quickly.");
            timeline.Add("[T+007] C&C placement: Construction Yard stamps down a Power Plant instantly, then the new building exits its short Constructing state.");

            var refineryIdsBefore = SnapshotEntityIdsByName(world, "Refinery");
            float conyardCreditsBeforeRefinery = ReadAttribute(world, constructionYard, creditsAttrId);
            CastAbilityAtWorldPoint(engine, constructionYard, slot: 3, new Vector2(2650f, -1380f));
            TickUntil(engine, frameTimesMs, () => CountEntitiesByName(world, "Refinery") == refineryIdsBefore.Count + 1, maxFrames: 20, "Construction Yard form override should place a Refinery.");
            Entity refinery = FindNewestEntityByName(world, "Refinery", refineryIdsBefore);
            TickUntil(engine, frameTimesMs, () => HasEffectiveTag(world, tagOps, refinery, constructingTagId), maxFrames: 8, "Refinery should enter Constructing after spawn.");
            Assert.That(ReadAttribute(world, constructionYard, creditsAttrId), Is.EqualTo(conyardCreditsBeforeRefinery - 1400f).Within(0.01f));
            TickUntil(engine, frameTimesMs, () => !HasEffectiveTag(world, tagOps, refinery, constructingTagId), maxFrames: 300, "Refinery should finish its short construction.");
            trace.Add(CaptureSnapshot(world, engine, "cnc_buildings_online", "Construction Yard placed both Power Plant and Refinery through direct place effects and form overrides.", constructionYard, powerPlant, refinery));
            timeline.Add("[T+008] C&C form-set route: the same conyard gains a Refinery on slot override, proving building palettes can stay purely data-driven.");

            var rhinoIdsBefore = SnapshotEntityIdsByName(world, "Rhino Tank");
            float warFactoryCreditsBefore = ReadAttribute(world, warFactory, creditsAttrId);
            CastAbility(engine, warFactory, warFactory, slot: 2);
            TickUntil(engine, frameTimesMs, () => HasEffectiveTag(world, tagOps, warFactory, trainingTagId), maxFrames: 8, "War Factory should enter Training.");
            TickUntil(engine, frameTimesMs, () => CountEntitiesByName(world, "Rhino Tank") == rhinoIdsBefore.Count + 1, maxFrames: 700, "War Factory should train a Rhino Tank.");
            Entity rhino = FindNewestEntityByName(world, "Rhino Tank", rhinoIdsBefore);
            Assert.That(ReadAttribute(world, warFactory, creditsAttrId), Is.EqualTo(warFactoryCreditsBefore - 900f).Within(0.01f));
            CastAbility(engine, rocketTrooper, battleBunker, slot: 1);
            TickUntil(
                engine,
                frameTimesMs,
                () => world.Has<ChildOf>(rocketTrooper) && IsSelectionDisabled(world, rocketTrooper),
                maxFrames: 12,
                "Rocket Trooper should garrison into the bunker and become unselectable.");
            Assert.That(world.Get<ChildOf>(rocketTrooper).Parent, Is.EqualTo(battleBunker));
            CastAbility(engine, battleBunker, battleBunker, slot: 2);
            TickUntil(
                engine,
                frameTimesMs,
                () => !world.Has<ChildOf>(rocketTrooper) && IsSelectable(world, rocketTrooper),
                maxFrames: 24,
                "Bunker ungarrison should release the Rocket Trooper and restore selection.");
            trace.Add(CaptureSnapshot(world, engine, "cnc_training_and_bunker", "War Factory rolled out a Rhino and the Rocket Trooper cycled through bunker garrison.", warFactory, rhino, battleBunker, rocketTrooper));
            timeline.Add("[T+009] C&C unit flow: War Factory trains a Rhino while the bunker reuses the same shared garrison/ungarrison relation behavior as the tower.");

            var zealotIdsBeforeTrain = SnapshotEntityIdsByName(world, "Zealot");
            float gatewayMineralsBeforeTrain = ReadAttribute(world, gateway, mineralsAttrId);
            CastAbility(engine, gateway, gateway, slot: 2);
            TickUntil(engine, frameTimesMs, () => HasEffectiveTag(world, tagOps, gateway, trainingTagId), maxFrames: 8, "Gateway should enter Training.");
            TickUntil(engine, frameTimesMs, () => CountEntitiesByName(world, "Zealot") == zealotIdsBeforeTrain.Count + 1, maxFrames: 600, "Gateway should train a Zealot before Warp Gate research.");
            Entity trainedZealot = FindNewestEntityByName(world, "Zealot", zealotIdsBeforeTrain);
            Assert.That(ReadAttribute(world, gateway, mineralsAttrId), Is.EqualTo(gatewayMineralsBeforeTrain - 100f).Within(0.01f));

            float gatewayMineralsBeforeResearch = ReadAttribute(world, gateway, mineralsAttrId);
            float gatewayGasBeforeResearch = ReadAttribute(world, gateway, gasAttrId);
            CastAbility(engine, gateway, gateway, slot: 3);
            TickUntil(engine, frameTimesMs, () => HasEffectiveTag(world, tagOps, gateway, researchingTagId), maxFrames: 8, "Gateway should enter Researching.");
            RtsPanelSnapshot gatewayResearchPanel = CapturePanelSnapshot(engine, toolbar, panelSource, gateway, panelSnapshots, "003_gateway_research_status");
            Assert.That(gatewayResearchPanel.Statuses.Count, Is.GreaterThan(0), "Gateway panel should expose its active research status.");
            Assert.That(gatewayResearchPanel.QueueItems.Count, Is.GreaterThan(0), "Gateway panel should expose its research order queue.");
            TickUntil(engine, frameTimesMs, () => HasEffectiveTag(world, tagOps, gateway, warpGateTechTagId), maxFrames: 900, "Warp Gate tech should be granted after the research clip.");
            Assert.That(ReadAttribute(world, gateway, mineralsAttrId), Is.EqualTo(gatewayMineralsBeforeResearch - 50f).Within(0.01f));
            Assert.That(ReadAttribute(world, gateway, gasAttrId), Is.EqualTo(gatewayGasBeforeResearch - 50f).Within(0.01f));
            TickUntil(
                engine,
                frameTimesMs,
                () => TryGetSlotDisplayLabel(panelSource, gateway, slotIndex: 0, out string label) &&
                      string.Equals(label, "折跃狂热者", StringComparison.Ordinal),
                maxFrames: 4,
                "Gateway panel should refresh its slot 0 form override after Warp Gate research.");
            RtsPanelSnapshot warpgatePanel = CapturePanelSnapshot(
                engine,
                toolbar,
                panelSource,
                gateway,
                panelSnapshots,
                "004_warpgate_preview",
                previewSlotIndex: 0,
                previewWorldCm: new Vector2(300f, 2380f));
            Assert.That(warpgatePanel.Slots[0].DisplayLabel, Is.EqualTo("折跃狂热者"));
            Assert.That(warpgatePanel.Preview?.PerformerId, Is.EqualTo("core_input_preview_warp_site"));

            var zealotIdsBeforeWarp = SnapshotEntityIdsByName(world, "Zealot");
            CastAbilityAtWorldPoint(engine, gateway, slot: 0, new Vector2(300f, 2380f));
            TickUntil(engine, frameTimesMs, () => CountEntitiesByName(world, "Zealot") == zealotIdsBeforeWarp.Count + 1, maxFrames: 20, "Warp Gate slot override should warp a Zealot.");
            Entity warpedZealot = FindNewestEntityByName(world, "Zealot", zealotIdsBeforeWarp);
            TickUntil(
                engine,
                frameTimesMs,
                () => HasEffectiveTag(world, tagOps, warpedZealot, warpingTagId) && IsSelectionDisabled(world, warpedZealot),
                maxFrames: 20,
                "Warped Zealot should receive its warp-in state and become temporarily unselectable.");
            Assert.That(HasEffectiveTag(world, tagOps, warpedZealot, warpingTagId), Is.True, "Warped Zealot should begin inside the Warping state.");
            Assert.That(world.Get<CommandSourceSelectableState>(warpedZealot).Enabled, Is.False, "Warping units should be temporarily unselectable.");
            TickUntil(
                engine,
                frameTimesMs,
                () => !HasEffectiveTag(world, tagOps, warpedZealot, warpingTagId) && IsSelectable(world, warpedZealot),
                maxFrames: 600,
                "Warping state should expire and restore selection.");
            Assert.That(world.Get<CommandSourceSelectableState>(warpedZealot).Enabled, Is.True);
            trace.Add(CaptureSnapshot(world, engine, "sc2_gateway_tech_and_warp", "Gateway researched Warp Gate and changed its slot 0 output into an instant warp-in.", gateway, trainedZealot, warpedZealot));
            timeline.Add("[T+010] Protoss tech path: Gateway first trains a Zealot, then researches Warp Gate, gains Progression.Rts.WarpGate, and swaps slot 0 into a point-target warp-in.");

            var spawningPoolIdsBefore = SnapshotEntityIdsByName(world, "Spawning Pool");
            float droneMineralsBefore = ReadAttribute(world, drone, mineralsAttrId);
            RtsPanelSnapshot dronePanel = CapturePanelSnapshot(
                engine,
                toolbar,
                panelSource,
                drone,
                panelSnapshots,
                "005_drone_morph_preview",
                previewSlotIndex: 0,
                previewWorldCm: new Vector2(3250f, 2200f));
            Assert.That(dronePanel.Preview?.PerformerId, Is.EqualTo("core_input_preview_morph_site"));
            CastAbilityAtWorldPoint(engine, drone, slot: 0, new Vector2(3250f, 2200f));
            TickUntil(engine, frameTimesMs, () => CountEntitiesByName(world, "Spawning Pool") == spawningPoolIdsBefore.Count + 1, maxFrames: 20, "Drone morph should spawn a Spawning Pool.");
            Entity spawningPool = FindNewestEntityByName(world, "Spawning Pool", spawningPoolIdsBefore);
            TickUntil(
                engine,
                frameTimesMs,
                () => world.Has<ChildOf>(drone) && IsSelectionDisabled(world, drone),
                maxFrames: 20,
                "Morphing drone should attach after the structure shell spawns and become unselectable.");
            Assert.That(world.Has<ChildOf>(drone), Is.True, "Morphing drone should attach to the spawned structure.");
            Assert.That(world.Get<ChildOf>(drone).Parent, Is.EqualTo(spawningPool));
            Assert.That(HasEffectiveTag(world, tagOps, drone, morphConsumedTagId), Is.True);
            Assert.That(HasEffectiveTag(world, tagOps, spawningPool, constructingTagId), Is.True);
            Assert.That(world.Get<CommandSourceSelectableState>(drone).Enabled, Is.False);
            Assert.That(ReadAttribute(world, drone, mineralsAttrId), Is.EqualTo(droneMineralsBefore - 200f).Within(0.01f));
            TickUntil(engine, frameTimesMs, () => !world.IsAlive(drone), maxFrames: 1400, "Drone should be consumed when the morph completes.");
            Assert.That(HasEffectiveTag(world, tagOps, spawningPool, constructingTagId), Is.False, "Morph host should finish construction before the drone is removed.");
            trace.Add(CaptureSnapshot(world, engine, "zerg_morph_complete", "Drone morph consumed itself into a completed Spawning Pool.", spawningPool));
            timeline.Add("[T+011] Zerg morph: Drone attaches to the Spawning Pool shell, stays non-interactable during Constructing, then is destroyed when the morph completes.");

            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTraceJsonl(trace), Encoding.UTF8);
            File.WriteAllText(Path.Combine(artifactDir, "panel-trace.jsonl"), BuildTraceJsonl(panelSnapshots), Encoding.UTF8);
            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(timeline, frameTimesMs), Encoding.UTF8);
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid(), Encoding.UTF8);
            WritePanelScreens(panelSnapshots, screensDir);
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

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
            engine.GlobalContext.Remove(CoreServiceKeys.CameraPoseRequest.Name);
            engine.GlobalContext.Remove(CoreServiceKeys.VirtualCameraRequest.Name);
            Tick(engine, 5, frameTimesMs);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), "No trigger errors should occur while loading the RTS acceptance map.");
        }

        private static void CastAbility(GameEngine engine, Entity actor, Entity target, int slot)
        {
            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue) as OrderQueue
                ?? throw new InvalidOperationException("OrderQueue service is missing.");

            bool enqueued = orderQueue.TryEnqueue(new Order
            {
                OrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["castAbility"],
                PlayerId = 1,
                Actor = actor,
                Target = target,
                Args = new OrderArgs
                {
                    I0 = slot
                },
                SubmitMode = OrderSubmitMode.Immediate
            });

            Assert.That(enqueued, Is.True, "Ability order should enqueue.");
        }

        private static void CastAbilityAtWorldPoint(GameEngine engine, Entity actor, int slot, Vector2 targetWorldCm)
        {
            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue) as OrderQueue
                ?? throw new InvalidOperationException("OrderQueue service is missing.");

            Vector2 originWorldCm = ReadWorldPosition(engine.World, actor);
            var spatial = new OrderSpatial
            {
                Kind = OrderSpatialKind.WorldCm,
                Mode = OrderCollectionMode.List,
                WorldCm = new Vector3(originWorldCm.X, 0f, originWorldCm.Y)
            };
            spatial.AddInlinePointWorldCm((int)originWorldCm.X, 0, (int)originWorldCm.Y);
            spatial.AddInlinePointWorldCm((int)targetWorldCm.X, 0, (int)targetWorldCm.Y);

            bool enqueued = orderQueue.TryEnqueue(new Order
            {
                OrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["castAbility"],
                PlayerId = 1,
                Actor = actor,
                Args = new OrderArgs
                {
                    I0 = slot,
                    Spatial = spatial
                },
                SubmitMode = OrderSubmitMode.Immediate
            });

            Assert.That(enqueued, Is.True, "Point-targeted ability order should enqueue.");
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

        private static void TickUntil(
            GameEngine engine,
            List<double> frameTimesMs,
            Func<bool> condition,
            int maxFrames,
            string because,
            Func<string> onTimeout = null)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (condition())
                {
                    return;
                }

                Tick(engine, 1, frameTimesMs);
            }

            string failureMessage = because;
            if (onTimeout != null)
            {
                failureMessage += Environment.NewLine + onTimeout();
            }

            Assert.That(condition(), Is.True, failureMessage);
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

        private static bool IsSelectable(World world, Entity entity)
        {
            return world.IsAlive(entity) &&
                   world.Has<CommandSourceSelectableState>(entity) &&
                   world.Get<CommandSourceSelectableState>(entity).Enabled;
        }

        private static bool IsSelectionDisabled(World world, Entity entity)
        {
            return world.IsAlive(entity) &&
                   world.Has<CommandSourceSelectableState>(entity) &&
                   !world.Get<CommandSourceSelectableState>(entity).Enabled;
        }

        private static float ReadAttribute(World world, Entity entity, int attributeId)
        {
            return world.Get<AttributeBuffer>(entity).GetCurrent(attributeId);
        }

        private static Vector2 ReadWorldPosition(World world, Entity entity)
        {
            ref readonly var position = ref world.Get<WorldPositionCm>(entity);
            return new Vector2(position.Value.X.ToFloat(), position.Value.Y.ToFloat());
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

        private static HashSet<int> SnapshotEntityIdsByName(World world, string entityName)
        {
            var result = new HashSet<int>();
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(entity.Id);
                }
            });
            return result;
        }

        private static Entity FindNewestEntityByName(World world, string entityName, HashSet<int> baselineIds)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase) &&
                    !baselineIds.Contains(entity.Id) &&
                    (result == Entity.Null || entity.Id > result.Id))
                {
                    result = entity;
                }
            });

            if (result == Entity.Null)
            {
                throw new InvalidOperationException($"Unable to locate new entity '{entityName}'.");
            }

            return result;
        }

        private static string BuildTrainingDiagnostics(World world, TagOps tagOps, Entity producer, string producedUnitName)
        {
            int trainingTagId = EnsureTag("Status.Rts.Training");
            int mineralsAttrId = EnsureAttribute("Minerals");
            int creditsAttrId = EnsureAttribute("Credits");

            var sb = new StringBuilder();
            sb.Append("Producer=");
            sb.Append(DescribeEntity(world, tagOps, producer));
            sb.Append(" | ProducedUnits=");
            sb.Append(DescribeEntitiesByName(world, tagOps, producedUnitName));

            if (world.IsAlive(producer) && world.Has<AttributeBuffer>(producer))
            {
                sb.Append(" | ProducerResources=");
                sb.Append('{');
                sb.Append("Minerals=");
                sb.Append(ReadAttribute(world, producer, mineralsAttrId));
                sb.Append(", Credits=");
                sb.Append(ReadAttribute(world, producer, creditsAttrId));
                sb.Append(", Training=");
                sb.Append(HasEffectiveTag(world, tagOps, producer, trainingTagId));
                sb.Append('}');
            }

            return sb.ToString();
        }

        private static string DescribeEntitiesByName(World world, TagOps tagOps, string entityName)
        {
            var descriptions = new List<string>();
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    descriptions.Add(DescribeEntity(world, tagOps, entity));
                }
            });

            return descriptions.Count == 0
                ? "[]"
                : "[" + string.Join("; ", descriptions.OrderBy(text => text, StringComparer.Ordinal)) + "]";
        }

        private static string DescribeEntity(World world, TagOps tagOps, Entity entity)
        {
            if (!world.IsAlive(entity))
            {
                return "<dead>";
            }

            string name = world.Has<Name>(entity) ? world.Get<Name>(entity).Value : "<unnamed>";
            string position = world.Has<WorldPositionCm>(entity)
                ? $"({ReadWorldPosition(world, entity).X:0.##},{ReadWorldPosition(world, entity).Y:0.##})"
                : "(no-pos)";
            bool hasParent = world.Has<ChildOf>(entity);
            int parentId = hasParent ? world.Get<ChildOf>(entity).Parent.Id : 0;
            bool selectable = world.Has<CommandSourceSelectableState>(entity) && world.Get<CommandSourceSelectableState>(entity).Enabled;
            bool training = HasEffectiveTag(world, tagOps, entity, EnsureTag("Status.Rts.Training"));
            bool constructing = HasEffectiveTag(world, tagOps, entity, EnsureTag("State.Rts.Constructing"));

            return $"#{entity.Id}:{name}@{position},parent={parentId},selectable={selectable},training={training},constructing={constructing}";
        }

        private static object CaptureSnapshot(World world, GameEngine engine, string step, string note, params Entity[] focusEntities)
        {
            var tagOps = engine.GetService(CoreServiceKeys.TagOps)
                ?? throw new InvalidOperationException("TagOps service is missing.");

            int constructingTagId = EnsureTag("State.Rts.Constructing");
            int builderAttachedTagId = EnsureTag("State.Rts.BuilderAttached");
            int morphConsumedTagId = EnsureTag("State.Rts.MorphConsumed");
            int warpingTagId = EnsureTag("State.Rts.Warping");
            int trainingTagId = EnsureTag("Status.Rts.Training");
            int researchingTagId = EnsureTag("Status.Rts.Researching");
            int warpGateTechTagId = EnsureTag("Progression.Rts.WarpGate");

            var focus = focusEntities
                .Where(entity => world.IsAlive(entity) && world.Has<Name>(entity))
                .Select(entity => new
                {
                    Id = entity.Id,
                    Name = world.Get<Name>(entity).Value,
                    PositionCm = world.Has<WorldPositionCm>(entity)
                        ? new
                        {
                            X = ReadWorldPosition(world, entity).X,
                            Y = ReadWorldPosition(world, entity).Y
                        }
                        : null,
                    HasParent = world.Has<ChildOf>(entity),
                    ParentId = world.Has<ChildOf>(entity) ? world.Get<ChildOf>(entity).Parent.Id : 0,
                    Selectable = world.Has<CommandSourceSelectableState>(entity) && world.Get<CommandSourceSelectableState>(entity).Enabled,
                    Attributes = ReadTrackedAttributes(world, entity),
                    Tags = new Dictionary<string, bool>(StringComparer.Ordinal)
                    {
                        ["State.Rts.Constructing"] = HasEffectiveTag(world, tagOps, entity, constructingTagId),
                        ["State.Rts.BuilderAttached"] = HasEffectiveTag(world, tagOps, entity, builderAttachedTagId),
                        ["State.Rts.MorphConsumed"] = HasEffectiveTag(world, tagOps, entity, morphConsumedTagId),
                        ["State.Rts.Warping"] = HasEffectiveTag(world, tagOps, entity, warpingTagId),
                        ["Status.Rts.Training"] = HasEffectiveTag(world, tagOps, entity, trainingTagId),
                        ["Status.Rts.Researching"] = HasEffectiveTag(world, tagOps, entity, researchingTagId),
                        ["Progression.Rts.WarpGate"] = HasEffectiveTag(world, tagOps, entity, warpGateTechTagId)
                    }
                })
                .ToArray();

            return new
            {
                Step = step,
                Note = note,
                Counts = new
                {
                    LumberMills = CountEntitiesByName(world, "Lumber Mill"),
                    GuardTowers = CountEntitiesByName(world, "Guard Tower"),
                    Footmen = CountEntitiesByName(world, "Footman"),
                    PowerPlants = CountEntitiesByName(world, "Power Plant"),
                    Refineries = CountEntitiesByName(world, "Refinery"),
                    Rhinos = CountEntitiesByName(world, "Rhino Tank"),
                    Zealots = CountEntitiesByName(world, "Zealot"),
                    SpawningPools = CountEntitiesByName(world, "Spawning Pool")
                },
                Focus = focus
            };
        }

        private static Dictionary<string, float> ReadTrackedAttributes(World world, Entity entity)
        {
            var result = new Dictionary<string, float>(StringComparer.Ordinal);
            if (!world.Has<AttributeBuffer>(entity))
            {
                return result;
            }

            TryAddTrackedAttribute(world, entity, "Health", result);
            TryAddTrackedAttribute(world, entity, "Minerals", result);
            TryAddTrackedAttribute(world, entity, "Lumber", result);
            TryAddTrackedAttribute(world, entity, "Credits", result);
            TryAddTrackedAttribute(world, entity, "Gas", result);
            return result;
        }

        private static void TryAddTrackedAttribute(World world, Entity entity, string attributeName, IDictionary<string, float> result)
        {
            int attributeId = AttributeRegistry.GetId(attributeName);
            if (attributeId > 0)
            {
                result[attributeName] = world.Get<AttributeBuffer>(entity).GetCurrent(attributeId);
            }
        }

        private static string BuildTraceJsonl<T>(IEnumerable<T> snapshots)
        {
            return string.Join(
                Environment.NewLine,
                snapshots.Select(snapshot => JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
                {
                    WriteIndented = false
                })));
        }

        private static string BuildBattleReport(IReadOnlyList<string> timeline, IReadOnlyList<double> frameTimesMs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: rts-strategic-showcase");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: validate classic RTS strategic verbs on top of GAS tags, effects, relation parenting, and form-set slot routing.");
            sb.AppendLine("- Gameplay domain: Warcraft worker build, C&C place-and-rise, Protoss warp tech, Zerg morph, shared garrison/ungarrison.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- Seed: fixed-step deterministic simulation at 60 FPS.");
            sb.AppendLine("- Map: `rts_entry`.");
            sb.AppendLine("- Clock profile: `FixedFrame`.");
            sb.AppendLine("- Initial entities: Peasant, Barracks, Guard Tower, Footman, Construction Yard, War Factory, Battle Bunker, Rocket Trooper, Gateway, Probe, Drone.");
            sb.AppendLine();
            sb.AppendLine("## Action Script");
            sb.AppendLine("1. Warcraft peasant builds Lumber Mill and Guard Tower via data-driven slots.");
            sb.AppendLine("2. Barracks trains Footman, then the trained unit garrisons and ungarrisons.");
            sb.AppendLine("3. Construction Yard places Power Plant and Refinery; War Factory trains Rhino; bunker cycles Rocket Trooper garrison.");
            sb.AppendLine("4. Gateway trains Zealot, researches Warp Gate, then warps a second Zealot at a world point.");
            sb.AppendLine("5. Drone morphs into a Spawning Pool and gets consumed on completion.");
            sb.AppendLine();
            sb.AppendLine("## Expected Outcomes");
            sb.AppendLine("- Primary success condition: every strategic action completes using existing GAS tags, effects, relation parenting, and form-set routing.");
            sb.AppendLine("- Failure branch condition: builders fail to attach/detach, garrisoned units never release, research never grants `Progression.Rts.WarpGate`, or morphing drones survive completion.");
            sb.AppendLine("- Key metrics:");
            sb.AppendLine($"  total timeline steps: {timeline.Count}");
            sb.AppendLine($"  average frame time ms: {frameTimesMs.DefaultIfEmpty(0d).Average():F3}");
            sb.AppendLine($"  peak frame time ms: {frameTimesMs.DefaultIfEmpty(0d).Max():F3}");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            for (int i = 0; i < timeline.Count; i++)
            {
                sb.AppendLine($"- {timeline[i]}");
            }
            sb.AppendLine();
            sb.AppendLine("## Evidence Artifacts");
            sb.AppendLine("- `artifacts/acceptance/rts-strategic-showcase/trace.jsonl`");
            sb.AppendLine("- `artifacts/acceptance/rts-strategic-showcase/panel-trace.jsonl`");
            sb.AppendLine("- `artifacts/acceptance/rts-strategic-showcase/battle-report.md`");
            sb.AppendLine("- `artifacts/acceptance/rts-strategic-showcase/path.mmd`");
            sb.AppendLine("- `artifacts/acceptance/rts-strategic-showcase/screens/*.svg`");
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    Start[Map Loaded]",
                "    Start --> War3Build[War3 Worker Build]",
                "    War3Build --> War3Train[Train Footman]",
                "    War3Train --> SharedGarrison[Shared Garrison Cycle]",
                "    SharedGarrison --> CncBuild[C&C Place and Rise]",
                "    CncBuild --> CncTrain[Train Rhino]",
                "    CncTrain --> GatewayTrain[Gateway Train Zealot]",
                "    GatewayTrain --> WarpGateResearch[Research Warp Gate]",
                "    WarpGateResearch --> WarpIn[Warp Zealot at World Point]",
                "    WarpIn --> ZergMorph[Drone Morphs Spawning Pool]",
                "    ZergMorph --> Done[Acceptance Complete]",
                "    War3Build -. fail .-> AttachFailure[Builder did not attach or detach]",
                "    SharedGarrison -. fail .-> GarrisonFailure[Relation garrison stuck]",
                "    WarpGateResearch -. fail .-> ResearchFailure[Warp tech missing]",
                "    ZergMorph -. fail .-> MorphFailure[Drone survived morph]"
            });
        }

        private static void InstallDummyInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
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
            List<RtsPanelSnapshot> snapshots,
            string step,
            int previewSlotIndex = -1,
            Vector2? previewWorldCm = null)
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

            RtsPreviewSnapshot? preview = null;
            if (previewSlotIndex >= 0 && previewWorldCm.HasValue)
            {
                preview = CapturePreviewSnapshot(engine, target, previewSlotIndex, previewWorldCm.Value);
            }

            var snapshot = new RtsPanelSnapshot(
                step,
                ReadName(engine.World, target),
                toolbar.Subtitle,
                toolbarSnapshots,
                slotSnapshots,
                statusSnapshots,
                queueSnapshots,
                preview);
            snapshots.Add(snapshot);
            return snapshot;
        }

        private static bool TryGetSlotDisplayLabel(IEntityCommandPanelSource source, Entity target, int slotIndex, out string label)
        {
            label = string.Empty;
            if (slotIndex < 0)
            {
                return false;
            }

            var slots = new EntityCommandPanelSlotView[8];
            int slotCount = source.CopySlots(target, 0, slots);
            for (int i = 0; i < slotCount; i++)
            {
                if (slots[i].SlotIndex != slotIndex)
                {
                    continue;
                }

                label = slots[i].DisplayLabel ?? string.Empty;
                return true;
            }

            return false;
        }

        private static RtsPreviewSnapshot? CapturePreviewSnapshot(GameEngine engine, Entity actor, int slotIndex, Vector2 targetWorldCm)
        {
            var abilities = engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry)
                ?? throw new InvalidOperationException("AbilityDefinitionRegistry service is missing.");
            var effects = engine.GetService(CoreServiceKeys.EffectTemplateRegistry)
                ?? throw new InvalidOperationException("EffectTemplateRegistry service is missing.");
            var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore service is missing.");
            var spatialQueries = engine.GetService(CoreServiceKeys.SpatialQueryService)
                ?? throw new InvalidOperationException("SpatialQueryService service is missing.");
            var overlays = engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
                ?? throw new InvalidOperationException("GroundOverlayBuffer service is missing.");
            var performerDefinitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
                ?? throw new InvalidOperationException("PerformerDefinitionRegistry service is missing.");
            var performers = engine.GetService(CoreServiceKeys.PerformerEntityRuntime)
                ?? throw new InvalidOperationException("PerformerEntityRuntime missing.");
            var presentationEvents = engine.GetService(CoreServiceKeys.PresentationEventStream)
                ?? throw new InvalidOperationException("PresentationEventStream service is missing.");

            overlays.Clear();
            performers.Clear();
            presentationEvents.Clear();

            var runtime = new AbilityAimPresentationRuntime(
                engine.World,
                abilities,
                effects,
                collections,
                spatialQueries,
                presentationEvents,
                engine.GameSession);
            runtime.UpdateAiming(
                actor,
                new InputOrderMapping
                {
                    ActionId = $"PreviewSlot{slotIndex}",
                    TargetType = OrderTargetType.Position,
                    ArgsTemplate = new OrderArgsTemplate { I0 = slotIndex }
                },
                new AbilityAimInputState(
                    AbilityAimInputSlot.Target,
                    hasCursorWorldCm: true,
                    cursorWorldCm: new Vector3(targetWorldCm.X, 0f, targetWorldCm.Y),
                    hasOriginWorldCm: false,
                    originWorldCm: default,
                    hoveredEntity: Entity.Null));
            engine.Tick(DeltaTime);

            string overlaySummary = string.Join(", ",
                overlays.GetSpan().ToArray().GroupBy(item => item.Shape).Select(group => $"{group.Key}:{group.Count()}"));
            if (collections.TryGetView(actor, EntityCollectionKeys.AbilityAimAffected, out var affected))
            {
                overlaySummary = string.IsNullOrWhiteSpace(overlaySummary)
                    ? $"affected:{affected.Count}"
                    : $"{overlaySummary}, affected:{affected.Count}";
            }

            RtsPreviewSnapshot? preview = null;
            RtsPreviewSnapshot? genericPreview = null;
            var performerQuery = new QueryDescription().WithAll<PerformerState, PerformerWorldPosition>();
            engine.World.Query(in performerQuery, (Entity entity, ref PerformerState state, ref PerformerWorldPosition worldPos) =>
            {
                string performerId = performerDefinitions.GetName(state.DefId);
                if (!IsRtsAimPreviewPerformer(performerId) &&
                    !string.Equals(performerId, "core_input.ability_aim.preview", StringComparison.Ordinal))
                {
                    return;
                }

                var candidate = new RtsPreviewSnapshot(
                    performerId,
                    worldPos.Value.X,
                    worldPos.Value.Y,
                    worldPos.Value.Z,
                    0f,
                    0f,
                    0f,
                    overlaySummary);
                if (IsRtsAimPreviewPerformer(performerId))
                {
                    preview = candidate;
                    return;
                }

                genericPreview ??= candidate;
            });
            preview ??= genericPreview;

            if (preview != null)
            {
                runtime.Clear(actor);
                engine.Tick(DeltaTime);
                overlays.Clear();
                return preview;
            }

            runtime.Clear(actor);
            engine.Tick(DeltaTime);
            overlays.Clear();
            return null;
        }

        private static bool IsRtsAimPreviewPerformer(string performerId)
        {
            return string.Equals(performerId, "core_input_preview_build_site", StringComparison.Ordinal) ||
                   string.Equals(performerId, "core_input_preview_warp_site", StringComparison.Ordinal) ||
                   string.Equals(performerId, "core_input_preview_morph_site", StringComparison.Ordinal);
        }

        private static void SelectEntity(GameEngine engine, Entity target)
        {
            var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore service is missing.");
            Entity owner = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
            Assert.That(engine.World.IsAlive(owner), Is.True, "Local player selection owner should exist on RTS map.");
            Assert.That(engine.World.IsAlive(target), Is.True, "Selection target should exist.");

            Span<Entity> next = stackalloc Entity[1];
            next[0] = target;
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.UiAcquisition,
                EntityCollectionRoleKind.CommandSource,
                contextEntity: owner,
                primaryEntity: target,
                title: "RTS strategic command source",
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

        private static void WritePanelScreens(IReadOnlyList<RtsPanelSnapshot> snapshots, string screensDir)
        {
            for (int i = 0; i < snapshots.Count; i++)
            {
                RtsPanelSnapshot snapshot = snapshots[i];
                WritePanelSnapshotSvg(snapshot, Path.Combine(screensDir, $"{i + 1:000}_{snapshot.Step}.svg"));
            }

            WritePanelTimelineSvg(snapshots, Path.Combine(screensDir, "timeline.svg"));
        }

        private static void WritePanelSnapshotSvg(RtsPanelSnapshot snapshot, string path)
        {
            const int width = 1600;
            int toolbarHeight = 92 + snapshot.ToolbarButtons.Count * 28;
            int slotHeight = 160 + snapshot.Slots.Count * 28;
            int statusHeight = 140 + Math.Max(1, snapshot.Statuses.Count) * 28;
            int queueHeight = 140 + Math.Max(1, snapshot.QueueItems.Count) * 28;
            int previewHeight = snapshot.Preview == null ? 120 : 184;
            int height = Math.Max(920, 120 + Math.Max(toolbarHeight + slotHeight, statusHeight + queueHeight + previewHeight));

            var toolbarLines = snapshot.ToolbarButtons.Count == 0
                ? new[] { "no quick-select buttons visible" }
                : snapshot.ToolbarButtons.Select(button => $"{(button.Active ? "[x]" : "[ ]")} {button.Label} ({button.ButtonId}) {button.AccentColorHex}").ToArray();
            var slotLines = snapshot.Slots.Count == 0
                ? new[] { "no slots" }
                : snapshot.Slots.Select(slot => $"[{slot.SlotIndex}] {slot.DisplayLabel} | {slot.DetailLabel} | {slot.Flags} | action={slot.ActionId}").ToArray();
            var statusLines = snapshot.Statuses.Count == 0
                ? new[] { "no active statuses" }
                : snapshot.Statuses.Select(status => $"{status.Kind} {status.ProgressPermille / 10.0:F1}% | {status.Label} | {status.Detail}").ToArray();
            var queueLines = snapshot.QueueItems.Count == 0
                ? new[] { "queue empty" }
                : snapshot.QueueItems.Select(item => $"{item.Stage} | {item.Label} | {item.Detail}").ToArray();
            var preview = snapshot.Preview;
            var previewLines = preview == null
                ? new[] { "preview unavailable" }
                : new[]
                {
                    $"performer={preview.Value.PerformerId}",
                    $"worldPos=({preview.Value.WorldX:0.##}, {preview.Value.WorldY:0.##}, {preview.Value.WorldZ:0.##})",
                    $"scale=({preview.Value.ScaleX:0.##}, {preview.Value.ScaleY:0.##}, {preview.Value.ScaleZ:0.##})",
                    $"overlays={preview.Value.OverlaySummary}"
                };

            string svg = $$"""
<svg xmlns="http://www.w3.org/2000/svg" width="{{width}}" height="{{height}}" viewBox="0 0 {{width}} {{height}}">
  <rect width="{{width}}" height="{{height}}" fill="#0b1017" />
  <rect x="32" y="28" width="1536" height="{{height - 56}}" rx="20" fill="#122031" stroke="#4c89c7" stroke-width="2" />
  <text x="64" y="84" fill="#f7d36d" font-size="34" font-family="Consolas, monospace">RTS Command Snapshot | {{EscapeSvg(snapshot.Step)}}</text>
  <text x="64" y="126" fill="#ffffff" font-size="24" font-family="Consolas, monospace">Focus: {{EscapeSvg(snapshot.FocusEntity)}} | {{EscapeSvg(snapshot.Subtitle)}}</text>
  {{RenderPanelSectionSvg("Quick Select", toolbarLines, 64, 170, 690)}}
  {{RenderPanelSectionSvg("Command Slots", slotLines, 64, 170 + toolbarHeight, 690)}}
  {{RenderPanelSectionSvg("Statuses", statusLines, 790, 170, 746)}}
  {{RenderPanelSectionSvg("Order Queue", queueLines, 790, 170 + statusHeight, 746)}}
  {{RenderPanelSectionSvg("Preview Ghost", previewLines, 790, 170 + statusHeight + queueHeight, 746)}}
  <text x="64" y="{{height - 40}}" fill="#9db4cc" font-size="18" font-family="Consolas, monospace">Data source: gas.ability-slots + toolbar provider + AbilityAimPresentationRuntime performer preview.</text>
</svg>
""";
            File.WriteAllText(path, svg, Encoding.UTF8);
        }

        private static void WritePanelTimelineSvg(IReadOnlyList<RtsPanelSnapshot> snapshots, string path)
        {
            int y = 76;
            var lines = new List<string>(snapshots.Count * 2);
            for (int i = 0; i < snapshots.Count; i++)
            {
                RtsPanelSnapshot snapshot = snapshots[i];
                string preview = snapshot.Preview == null ? "preview=none" : $"preview={snapshot.Preview.Value.PerformerId}";
                lines.Add($"""  <text x="56" y="{y}" fill="#f7d36d" font-size="24" font-family="Consolas, monospace">{EscapeSvg($"{i + 1:000} {snapshot.Step}")}</text>""");
                lines.Add($"""  <text x="460" y="{y}" fill="#ffffff" font-size="20" font-family="Consolas, monospace">{EscapeSvg($"focus={snapshot.FocusEntity} | slots={snapshot.Slots.Count} | statuses={snapshot.Statuses.Count} | queue={snapshot.QueueItems.Count} | {preview}")}</text>""");
                y += 72;
            }

            string svg = $$"""
<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="{{Math.Max(240, y + 32)}}" viewBox="0 0 1600 {{Math.Max(240, y + 32)}}">
  <rect width="1600" height="{{Math.Max(240, y + 32)}}" fill="#081018" />
  <text x="24" y="40" fill="#ffffff" font-size="28" font-family="Consolas, monospace">RTS command panel evidence timeline</text>
{{string.Join(Environment.NewLine, lines)}}
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

        private static void AssertReadableActor(World world, Entity entity, string label)
        {
            Assert.That(world.IsAlive(entity), Is.True, $"{label} should exist.");
            Assert.That(world.Has<VisualTransform>(entity), Is.True, $"{label} should expose VisualTransform for RTS markers.");
            Assert.That(world.Has<PreviousWorldPositionCm>(entity), Is.True, $"{label} should expose PreviousWorldPositionCm for interpolation.");
            Assert.That(world.Has<PresentationStableId>(entity), Is.True, $"{label} should expose PresentationStableId for entity-scoped performers.");

            VisualTransform visual = world.Get<VisualTransform>(entity);
            Assert.That(visual.Scale, Is.Not.EqualTo(Vector3.Zero), $"{label} should have a non-zero marker scale.");
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

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;

            public bool GetButton(string devicePath) => false;

            public Vector2 GetMousePosition() => Vector2.Zero;

            public float GetMouseWheel() => 0f;

            public void EnableIME(bool enable)
            {
            }

            public void SetIMECandidatePosition(int x, int y)
            {
            }

            public string GetCharBuffer() => string.Empty;
        }

        private readonly record struct RtsPanelSnapshot(
            string Step,
            string FocusEntity,
            string Subtitle,
            IReadOnlyList<RtsToolbarButtonSnapshot> ToolbarButtons,
            IReadOnlyList<RtsPanelSlotSnapshot> Slots,
            IReadOnlyList<RtsPanelStatusSnapshot> Statuses,
            IReadOnlyList<RtsPanelQueueSnapshot> QueueItems,
            RtsPreviewSnapshot? Preview);

        private readonly record struct RtsToolbarButtonSnapshot(
            string ButtonId,
            string Label,
            bool Active,
            string AccentColorHex);

        private readonly record struct RtsPanelSlotSnapshot(
            int SlotIndex,
            string ActionId,
            string DisplayLabel,
            string DetailLabel,
            string Flags);

        private readonly record struct RtsPanelStatusSnapshot(
            string Kind,
            string Label,
            string Detail,
            int ProgressPermille,
            string AccentColorHex);

        private readonly record struct RtsPanelQueueSnapshot(
            string Stage,
            string Label,
            string Detail,
            string AccentColorHex);

        private readonly record struct RtsPreviewSnapshot(
            string PerformerId,
            float WorldX,
            float WorldY,
            float WorldZ,
            float ScaleX,
            float ScaleY,
            float ScaleZ,
            string OverlaySummary);
    }
}
