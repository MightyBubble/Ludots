using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Arch.Core;
using BrowserRtsProductionShowcaseMod;
using CoreInputMod.Systems;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.UI;
using Ludots.UI.Skia;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[NonParallelizable]
public sealed class RtsProductionBrowserRedAlertContractTests
{
    private const string MapId = "rts_red_alert_like";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "EntityCommandPanelMod",
        "BrowserRtsProductionShowcaseMod",
        "RtsRedAlertLikeShowcaseMod"
    };

    [Test]
    public void RedAlertLikeShowcase_PresentationUsesCorePrimitiveMeshesWithoutBillboards()
    {
        using var engine = CreateEngine();

        var meshRegistry = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry)
            ?? throw new InvalidOperationException("PresentationMeshAssetRegistry is missing.");

        AssertPrimitiveMesh(meshRegistry, "cube", PrimitiveMeshKind.Cube);
        AssertPrimitiveMesh(meshRegistry, "sphere", PrimitiveMeshKind.Sphere);
        Assert.That(meshRegistry.GetId("rts.ra.billboard.city"), Is.EqualTo(0));
        Assert.That(meshRegistry.GetId("rts.ra.billboard.tower"), Is.EqualTo(0));
        Assert.That(meshRegistry.GetId("rts.ra.billboard.mine"), Is.EqualTo(0));
        Assert.That(meshRegistry.GetId("rts.ra.billboard.workshop"), Is.EqualTo(0));
        Assert.That(meshRegistry.GetId("rts.ra.billboard.ram"), Is.EqualTo(0));
        Assert.That(meshRegistry.GetId("rts.ra.billboard.stable"), Is.EqualTo(0));
        Assert.That(meshRegistry.GetId("rts.ra.billboard.catapult"), Is.EqualTo(0));
    }

    [Test]
    public void RedAlertLikeShowcase_MapEntitiesEmitPrimitiveChildPerformers()
    {
        using var engine = CreateEngine();
        var frameTimesMs = new List<double>();
        LoadMap(engine, frameTimesMs);

        AssertEntityUsesPrimitiveCompositeVisual(engine, "Allied Construction Yard", minimumParts: 5, requiresSphere: true);
        AssertEntityUsesPrimitiveCompositeVisual(engine, "Allied Power Plant", minimumParts: 5, requiresSphere: true);
        AssertEntityUsesPrimitiveCompositeVisual(engine, "Allied Ore Refinery", minimumParts: 5, requiresSphere: false);
        AssertEntityUsesPrimitiveCompositeVisual(engine, "Allied War Factory", minimumParts: 5, requiresSphere: false);
        AssertEntityUsesPrimitiveCompositeVisual(engine, "Allied MCV", minimumParts: 4, requiresSphere: true);
        AssertEntityUsesPrimitiveCompositeVisual(engine, "Allied Ore Harvester", minimumParts: 4, requiresSphere: false);
        AssertEntityUsesPrimitiveCompositeVisual(engine, "Allied Rhino Tank", minimumParts: 5, requiresSphere: true);
    }

    [Test]
    public void RedAlertLikeShowcase_MovablePrimitiveChildrenFollowOwnerTransformSync()
    {
        using var engine = CreateEngine();
        var frameTimesMs = new List<double>();
        LoadMap(engine, frameTimesMs);

        World world = engine.World;
        Entity rhino = FindEntity(world, "Allied Rhino Tank");
        Assert.That(world.TryGet(rhino, out PresentationOwnerHasPerformerPayload payload), Is.True);
        Assert.That(payload.Count, Is.GreaterThan(1), "Rhino primitive visual should be a root performer with attached child parts.");
        Assert.That(payload.RootCount, Is.EqualTo(1));
        Assert.That(payload.SingleRootTransformSync, Is.EqualTo(1), "Movable owner visuals must opt into owner-payload transform sync.");

        Entity root = payload.SingleRootPerformer;
        Assert.That(world.IsAlive(root), Is.True);
        Assert.That(world.Has<PerfOwnerPayloadTransformSync>(root), Is.True);
        Assert.That(world.TryGet(root, out PerformerChildren children), Is.True);
        Assert.That(children.Count, Is.GreaterThan(0));

        Entity child = children.Get(0);
        Assert.That(world.IsAlive(child), Is.True);
        Assert.That(
            world.Has<PerfOwnerPayloadAttachedTransformSync>(child) || world.Has<PerfHasAttachmentTick>(child),
            Is.True,
            "Primitive child performers must carry formal parent-attachment sync, not just an AssetBinding localOffset.");

        Vector3 rootBefore = world.Get<PerformerWorldPosition>(root).Value;
        Vector3 childBefore = world.Get<PerformerWorldPosition>(child).Value;
        Vector3 delta = new(8f, 0f, 11f);

        ref VisualTransform visual = ref world.Get<VisualTransform>(rhino);
        visual.Position += delta;
        world.Set(rhino, WorldPositionCm.FromCm(
            (int)MathF.Round(visual.Position.X * 100f),
            (int)MathF.Round(visual.Position.Z * 100f)));

        var performerRuntime = engine.GetService(CoreServiceKeys.PerformerEntityRuntime)
            ?? throw new InvalidOperationException("PerformerEntityRuntime missing.");
        var definitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
            ?? throw new InvalidOperationException("PerformerDefinitionRegistry missing.");
        using var transformSync = new PerformerEntityTransformSyncSystem(world, performerRuntime, definitions);
        transformSync.Update(0.016f);

        AssertVectorNear(world.Get<PerformerWorldPosition>(root).Value, rootBefore + delta, "Rhino root performer should follow owner VisualTransform.");
        AssertVectorNear(world.Get<PerformerWorldPosition>(child).Value, childBefore + delta, "Rhino child performer should follow the synced root transform.");
    }

    [Test]
    public void RedAlertLikeShowcase_ClickingSceneEntitySelectsItAndEmitsSelectionRing()
    {
        using var engine = CreateEngine();
        var frameTimesMs = new List<double>();
        LoadMap(engine, frameTimesMs);

        Entity warFactory = FindEntity(engine.World, "Allied War Factory");
        ClickEntityThroughCurrentSelection(engine, warFactory);

        AssertSelectedPrimary(engine, warFactory, "Clicking a scene entity through CoreInput should update the formal selection.");

        var overlays = engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
            ?? throw new InvalidOperationException("GroundOverlayBuffer service is missing.");
        overlays.Clear();
        ClearAuthoritativeInput(engine);
        Tick(engine, 1, frameTimesMs);

        Assert.That(
            CountSelectionRingsNear(engine, warFactory),
            Is.GreaterThan(0),
            "Selected Red Alert entities should emit a visible ground ring from the showcase presentation system.");
    }

    [Test]
    public void RedAlertLikeShowcase_BrowserRuntimeSuppressesTerrainButKeepsRaylibGroundGrid()
    {
        using var engine = CreateEngine();
        var frameTimesMs = new List<double>();
        Tick(engine, 1, frameTimesMs);

        RenderDebugState renderDebug = engine.GetService(CoreServiceKeys.RenderDebugState)
            ?? throw new InvalidOperationException("RenderDebugState service is missing.");
        Assert.That(renderDebug.DrawTerrain, Is.False);
        Assert.That(renderDebug.DrawDebugDraw, Is.True);
        Assert.That(renderDebug.DrawPrimitives, Is.True);
        Assert.That(renderDebug.DrawSkiaUi, Is.True);
        Assert.That(
            engine.GlobalContext.TryGetValue(SkillBarOverlaySystem.SkillBarEnabledKey, out object? skillBarEnabledObj),
            Is.True,
            "Browser RTS showcase should explicitly own the browser command card and disable the native CoreInput skill bar.");
        Assert.That(skillBarEnabledObj, Is.EqualTo(false));
    }

    [Test]
    public void RedAlertLikeShowcase_WarFactorySlot2_TrainsRhinoThroughSharedCommandPanel()
    {
        using var engine = CreateEngine();
        var frameTimesMs = new List<double>();
        LoadMap(engine, frameTimesMs);

        World world = engine.World;
        Entity warFactory = FindEntity(world, "Allied War Factory");
        SelectEntity(engine, warFactory);
        Tick(engine, 2, frameTimesMs);

        var registry = engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry)
            ?? throw new InvalidOperationException("EntityCommandPanelSourceRegistry is missing.");
        Assert.That(registry.TryGet("gas.ability-slots", out IEntityCommandPanelSource? source), Is.True);

        var context = new EntityCommandPanelSourceContext(warFactory, "gas.ability-slots", "browser-rts-production-test");
        var slots = new EntityCommandPanelSlotView[AbilityStateBuffer.CAPACITY];
        int copied = EntityCommandPanelSourceDispatch.CopySlots(source!, in context, 0, slots);
        Assert.That(copied, Is.GreaterThanOrEqualTo(3), "War Factory should expose its live build card.");

        EntityCommandPanelSlotView trainRhinoSlot = slots.Take(copied)
            .First(slot => slot.SlotIndex == 2);
        Assert.That(trainRhinoSlot.DisplayLabel, Is.EqualTo("Train Rhino"));
        Assert.That(trainRhinoSlot.ActionId, Is.EqualTo("SkillE"));
        Assert.That(trainRhinoSlot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Blocked), Is.False);

        int rhinosBefore = CountEntitiesByName(world, "Rhino Tank");

        bool activated = EntityCommandPanelSourceDispatch.ActivateSlot(source!, in context, 0, 2);
        Assert.That(activated, Is.True, "Browser command source must route slot 2 through the shared mapped-action bridge.");

        var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("OrderQueue service is missing.");
        Assert.That(orderQueue.Count, Is.GreaterThan(0), "Shared command panel activation should enqueue a formal order.");

        Tick(engine, 1, frameTimesMs);
        Assert.That(orderQueue.Count, Is.EqualTo(0), "The next fixed simulation step should consume the formal order queue.");
        Assert.That(world.TryGet(warFactory, out OrderBuffer orders), Is.True, "War Factory should keep formal orders in OrderBuffer.");
        Assert.That(orders.HasActive, Is.True, "Queued command-panel activation should become the War Factory active order.");
        Assert.That(orders.ActiveOrder.Order.Actor, Is.EqualTo(warFactory), "The active order must target the selected War Factory as actor.");
        Assert.That(orders.ActiveOrder.Order.Args.I0, Is.EqualTo(2), "The active order should carry the Rhino ability slot.");
        Assert.That(world.TryGet(warFactory, out BlackboardIntBuffer ints), Is.True, "War Factory should author blackboard ints for GAS activation.");
        Assert.That(ints.TryGet(OrderBlackboardKeys.Cast_SlotIndex, out int blackboardSlot), Is.True, "Cast slot should be written to the order blackboard.");
        Assert.That(blackboardSlot, Is.EqualTo(2));

        Tick(engine, 1, frameTimesMs);
        Assert.That(world.Has<AbilityExecInstance>(warFactory), Is.True, "War Factory should start a GAS AbilityExecInstance.");

        TickUntil(
            engine,
            frameTimesMs,
            () => CountEntitiesByName(world, "Rhino Tank") == rhinosBefore + 1,
            240,
            "War Factory slot 2 should train one Rhino Tank through the formal GAS queue.");
    }

    [Test]
    public void RedAlertLikeShowcase_WebDataPlaneSlot2_TrainsRhinoThroughBrowserCommand()
    {
        using var engine = CreateEngine();
        var frameTimesMs = new List<double>();
        LoadMap(engine, frameTimesMs);

        World world = engine.World;
        Entity warFactory = FindEntity(world, "Allied War Factory");
        SelectEntity(engine, warFactory);
        Tick(engine, 2, frameTimesMs);

        int rhinosBefore = CountEntitiesByName(world, "Rhino Tank");
        var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("OrderQueue service is missing.");
        Assert.That(orderQueue.Count, Is.EqualTo(0));

        WebUiCommandResult result = ApplyBrowserRtsCommand(
            engine,
            "activateAbilitySlot",
            new
            {
                entityKey = EntityKey(warFactory),
                groupIndex = 0,
                slotIndex = 2
            });
        Assert.That(result.Success, Is.True, $"{result.ErrorCode}: {result.Message}");
        AssertSelectedPrimary(engine, warFactory, "Clicking Train Rhino in the Web HUD must keep the selected War Factory.");
        Assert.That(orderQueue.Count, Is.GreaterThan(0), "Web command should enqueue a formal order.");

        Tick(engine, 1, frameTimesMs);
        Assert.That(orderQueue.Count, Is.EqualTo(0), "The next fixed simulation step should consume the Web command order.");
        Assert.That(world.Has<AbilityExecInstance>(warFactory), Is.True, "War Factory should start GAS execution from the Web command.");

        TickUntil(
            engine,
            frameTimesMs,
            () => TryGetActiveStatus(engine, warFactory, out EntityCommandPanelStatusView status) &&
                  status.Label == "Train Rhino" &&
                  status.ProgressPermille > 0,
            12,
            "The shared command panel status should expose real Train Rhino progress for the browser queue panel.");

        TickUntil(
            engine,
            frameTimesMs,
            () => CountEntitiesByName(world, "Rhino Tank") == rhinosBefore + 1,
            240,
            "Browser DataPlane activateAbilitySlot should train one Rhino Tank through the formal GAS queue.");
    }

    [Test]
    public void RedAlertLikeShowcase_WebDataPlaneSlot2_QueuesTwoRhinosThroughWarFactoryOrderBuffer()
    {
        using var engine = CreateEngine();
        var frameTimesMs = new List<double>();
        LoadMap(engine, frameTimesMs);

        World world = engine.World;
        Entity warFactory = FindEntity(world, "Allied War Factory");
        SelectEntity(engine, warFactory);
        Tick(engine, 2, frameTimesMs);

        int rhinosBefore = CountEntitiesByName(world, "Rhino Tank");
        AssertActivateAbilitySlot(engine, warFactory, groupIndex: 0, slotIndex: 2);
        Tick(engine, 1, frameTimesMs);
        Assert.That(world.TryGet(warFactory, out OrderBuffer firstOrders), Is.True);
        Assert.That(firstOrders.HasActive, Is.True);
        Assert.That(firstOrders.ActiveOrder.Order.Args.I0, Is.EqualTo(2));
        Assert.That(firstOrders.ActiveOrder.Order.Args.I1, Is.GreaterThan(0), "Queued skill orders must lock the clicked ability id.");

        AssertActivateAbilitySlot(engine, warFactory, groupIndex: 0, slotIndex: 2);
        Tick(engine, 1, frameTimesMs);

        ref readonly OrderBuffer queuedOrders = ref world.Get<OrderBuffer>(warFactory);
        Assert.That(queuedOrders.HasActive, Is.True);
        Assert.That(queuedOrders.QueuedCount, Is.EqualTo(1), "Second Train Rhino should remain in War Factory OrderBuffer instead of being rejected.");
        Assert.That(queuedOrders.GetQueued(0).Order.Args.I0, Is.EqualTo(2));
        Assert.That(queuedOrders.GetQueued(0).Order.Args.I1, Is.EqualTo(queuedOrders.ActiveOrder.Order.Args.I1));

        var queueItems = CopyQueueItems(engine, warFactory);
        Assert.That(queueItems, Has.Some.Matches<EntityCommandPanelQueueItemView>(item =>
            item.Stage == EntityCommandPanelQueueStage.Active && item.Label == "Train Rhino"));
        Assert.That(queueItems, Has.Some.Matches<EntityCommandPanelQueueItemView>(item =>
            item.Stage == EntityCommandPanelQueueStage.Queued && item.Label == "Train Rhino"));

        TickUntil(
            engine,
            frameTimesMs,
            () => CountEntitiesByName(world, "Rhino Tank") == rhinosBefore + 2,
            300,
            "Two browser Train Rhino clicks should execute as two formal queued War Factory orders.");
    }

    [Test]
    public void RedAlertLikeShowcase_WebBuildSlot_QueuesConstructionThenReadySlotPlacesAtConfirmedGroundPoint()
    {
        using var engine = CreateEngine();
        var frameTimesMs = new List<double>();
        LoadMap(engine, frameTimesMs);

        World world = engine.World;
        Entity constructionYard = FindEntity(world, "Allied Construction Yard");
        SelectEntity(engine, constructionYard);
        Tick(engine, 2, frameTimesMs);

        var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("OrderQueue service is missing.");
        var mapping = engine.GetService(CoreServiceKeys.ActiveInputOrderMapping)
            ?? throw new InvalidOperationException("ActiveInputOrderMapping is missing.");
        Assert.That(orderQueue.Count, Is.EqualTo(0));

        int powerPlantsBefore = CountEntitiesByName(world, "Power Plant");
        WebUiCommandResult result = ApplyBrowserRtsCommand(
            engine,
            "activateAbilitySlot",
            new
            {
                entityKey = EntityKey(constructionYard),
                groupIndex = 0,
                slotIndex = 0
            });

        Assert.That(result.Success, Is.True, $"{result.ErrorCode}: {result.Message}");
        AssertSelectedPrimary(engine, constructionYard, "Clicking Build Power Plant in the Web HUD must keep the Construction Yard selected.");
        Assert.That(orderQueue.Count, Is.GreaterThan(0), "C&C-style build commands should first enqueue formal production on the Construction Yard.");
        Assert.That(mapping.IsAiming, Is.False, "Build Power Plant should not enter placement aiming until the production item is ready.");
        Assert.That(CountEntitiesByName(world, "Power Plant"), Is.EqualTo(powerPlantsBefore));

        Tick(engine, 1, frameTimesMs);
        Assert.That(orderQueue.Count, Is.EqualTo(0), "The next fixed simulation step should consume the production order.");
        Assert.That(world.Has<AbilityExecInstance>(constructionYard), Is.True, "Construction Yard should start a GAS build AbilityExecInstance.");

        TickUntil(
            engine,
            frameTimesMs,
            () => TryGetActiveStatus(engine, constructionYard, out EntityCommandPanelStatusView status) &&
                  status.Label == "Build Power Plant" &&
                  status.ProgressPermille > 0,
            12,
            "The browser queue panel should be backed by the real GAS active build progress.");

        TickUntil(
            engine,
            frameTimesMs,
            () => HasTag(world, constructionYard, "State.Rts.RedAlert.Ready.PowerPlant") &&
                  !world.Has<AbilityExecInstance>(constructionYard),
            180,
            "Build Power Plant should complete production and mark a ready-to-place structure on the Construction Yard.");

        TickUntil(
            engine,
            frameTimesMs,
            () => GetCommandSlot(engine, constructionYard, groupIndex: 0, slotIndex: 0).DisplayLabel == "Place Power Plant",
            8,
            "Ability form routing should turn the completed build item into a ready placement slot.");

        EntityCommandPanelSlotView readySlot = GetCommandSlot(engine, constructionYard, groupIndex: 0, slotIndex: 0);
        Assert.That(readySlot.DisplayLabel, Is.EqualTo("Place Power Plant"));
        Assert.That(readySlot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Blocked), Is.False);

        WebUiCommandResult placeResult = ApplyBrowserRtsCommand(
            engine,
            "activateAbilitySlot",
            new
            {
                entityKey = EntityKey(constructionYard),
                groupIndex = 0,
                slotIndex = 0
            });

        Assert.That(placeResult.Success, Is.True, $"{placeResult.ErrorCode}: {placeResult.Message}");
        AssertSelectedPrimary(engine, constructionYard, "Clicking the ready Place Power Plant slot must keep the Construction Yard selected while aiming.");
        Assert.That(mapping.IsAiming, Is.True, "Ready C&C build slots should enter the shared CoreInput placement state.");
        Assert.That(mapping.AimingActionId, Is.EqualTo("SkillQ"));
        Assert.That(orderQueue.Count, Is.EqualTo(0), "Placement should wait for an explicit ground confirmation.");
        var authoritativeInput = engine.GetService(CoreServiceKeys.AuthoritativeInput) as FrozenInputActionReader
            ?? throw new InvalidOperationException("AuthoritativeInput service must be a FrozenInputActionReader.");
        const int targetX = 13200;
        const int targetY = 16000;
        TickWithGroundPointer(engine, 3, frameTimesMs, targetX, targetY);
        AssertPlacementPreviewVisible(engine, targetX, targetY);

        using (JsonDocument aimingSnapshot = CreateBrowserRtsSnapshot(engine))
        {
            string[] messages = aimingSnapshot.RootElement
                .GetProperty("diagnostics")
                .GetProperty("messages")
                .EnumerateArray()
                .Select(static element => element.GetString() ?? string.Empty)
                .ToArray();
            Assert.That(
                messages,
                Has.Some.Contains("Placement armed"),
                "Browser diagnostics should expose that the ready structure is armed for terrain confirmation.");
        }

        authoritativeInput.SetActionState(
            AuthoritativeGroundPointerHelper.ActionId,
            new Vector3(targetX, 0f, targetY),
            isDown: true,
            pressedThisFrame: false,
            releasedThisFrame: false);
        authoritativeInput.SetActionState(
            "Select",
            Vector3.One,
            isDown: true,
            pressedThisFrame: true,
            releasedThisFrame: false);
        mapping.Update(Time.FixedDeltaTime);

        Assert.That(mapping.IsAiming, Is.False, "Confirming placement should leave the shared CoreInput aiming state.");
        Assert.That(orderQueue.Count, Is.GreaterThan(0), "Confirmed placement should enqueue one formal castAbility order.");

        TickUntil(
            engine,
            frameTimesMs,
            () => CountEntitiesByName(world, "Power Plant") == powerPlantsBefore + 1,
            16,
            "Confirmed Web placement should create one power plant through the formal GAS CreateUnit path.");

        Entity spawned = FindEntity(world, "Power Plant");
        AssertWorldPosition(world, spawned, targetX, targetY);
        Tick(engine, 3, frameTimesMs);
        AssertEntityUsesPrimitiveCompositeVisual(engine, "Power Plant", minimumParts: 5, requiresSphere: true);
        Assert.That(HasTag(world, constructionYard, "State.Rts.RedAlert.Ready.PowerPlant"), Is.False);
    }

    [Test]
    public void RedAlertLikeShowcase_WebBuildSlot_QueuesSecondConstructionWithoutTurningItIntoPlacement()
    {
        using var engine = CreateEngine();
        var frameTimesMs = new List<double>();
        LoadMap(engine, frameTimesMs);

        World world = engine.World;
        Entity constructionYard = FindEntity(world, "Allied Construction Yard");
        SelectEntity(engine, constructionYard);
        Tick(engine, 2, frameTimesMs);

        AssertActivateAbilitySlot(engine, constructionYard, groupIndex: 0, slotIndex: 0);
        Tick(engine, 1, frameTimesMs);
        Assert.That(world.TryGet(constructionYard, out OrderBuffer firstOrders), Is.True);
        Assert.That(firstOrders.HasActive, Is.True);
        int buildAbilityId = firstOrders.ActiveOrder.Order.Args.I1;
        Assert.That(buildAbilityId, Is.GreaterThan(0), "Build Power Plant order should lock its ability id at submit time.");

        AssertActivateAbilitySlot(engine, constructionYard, groupIndex: 0, slotIndex: 0);
        var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("OrderQueue service is missing.");
        Assert.That(orderQueue.Count, Is.GreaterThan(0), "Second Build Power Plant activation should enqueue a formal queued order.");
        for (int probe = 0; probe < 8; probe++)
        {
            Tick(engine, 1, frameTimesMs);
            if (orderQueue.Count == 0)
            {
                break;
            }
        }

        ref readonly OrderBuffer queuedOrders = ref world.Get<OrderBuffer>(constructionYard);
        Assert.That(queuedOrders.QueuedCount, Is.EqualTo(1), "Second Build Power Plant should stay queued on the Construction Yard.");
        Assert.That(queuedOrders.GetQueued(0).Order.Args.I0, Is.EqualTo(0));
        Assert.That(queuedOrders.GetQueued(0).Order.Args.I1, Is.EqualTo(buildAbilityId));

        var initialQueueItems = CopyQueueItems(engine, constructionYard);
        Assert.That(initialQueueItems, Has.Some.Matches<EntityCommandPanelQueueItemView>(item =>
            item.Stage == EntityCommandPanelQueueStage.Active && item.Label == "Build Power Plant"));
        Assert.That(initialQueueItems, Has.Some.Matches<EntityCommandPanelQueueItemView>(item =>
            item.Stage == EntityCommandPanelQueueStage.Queued && item.Label == "Build Power Plant"));

        TickUntil(
            engine,
            frameTimesMs,
            () => HasTag(world, constructionYard, "State.Rts.RedAlert.Ready.PowerPlant"),
            180,
            "First completed C&C build should store a ready Power Plant without consuming the queued second build.");

        TickUntil(
            engine,
            frameTimesMs,
            () => GetCommandSlot(engine, constructionYard, groupIndex: 0, slotIndex: 0).DisplayLabel == "Place Power Plant",
            8,
            "Completed C&C production should route the build slot to its stored placement form.");

        Assert.That(GetCommandSlot(engine, constructionYard, groupIndex: 0, slotIndex: 0).DisplayLabel, Is.EqualTo("Place Power Plant"));
        Assert.That(world.TryGet(constructionYard, out OrderBuffer promotedOrders), Is.True);
        Assert.That(promotedOrders.HasActive, Is.True, "The queued second Build Power Plant should promote to the active OrderBuffer slot after the first build completes.");
        Assert.That(promotedOrders.ActiveOrder.Order.Args.I1, Is.EqualTo(buildAbilityId));
        Assert.That(promotedOrders.QueuedCount, Is.EqualTo(0));
        var routedQueueItems = CopyQueueItems(engine, constructionYard);
        Assert.That(routedQueueItems, Has.Some.Matches<EntityCommandPanelQueueItemView>(item =>
            item.Stage == EntityCommandPanelQueueStage.Active &&
            item.Label == "Build Power Plant"),
            "The promoted second order must remain Build Power Plant even after form routing changes slot 0 to Place Power Plant.");

        TickUntil(
            engine,
            frameTimesMs,
            () => GetReadyTagCount(world, constructionYard, "State.Rts.RedAlert.Ready.PowerPlant") >= 2 &&
                  !world.Has<AbilityExecInstance>(constructionYard),
            180,
            "Second queued Build Power Plant should complete as another stored ready item, not disappear as a failed Place command.");

        Assert.That(CountEntitiesByName(world, "Power Plant"), Is.EqualTo(0), "C&C production completion should store a placeable item without spawning until placement confirm.");
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
        InstallHeadlessPresentationHost(engine);
        engine.Start();
        return engine;
    }

    private static void LoadMap(GameEngine engine, List<double> frameTimesMs)
    {
        engine.LoadMap(MapId);
        Tick(engine, 8, frameTimesMs);
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
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

            var startedAt = DateTime.UtcNow;
            engine.Tick(Time.FixedDeltaTime);
            UpdateHeadlessCamera(engine);
            frameTimesMs.Add((DateTime.UtcNow - startedAt).TotalMilliseconds);
        }
    }

    private static void TickWithGroundPointer(GameEngine engine, int frames, List<double> frameTimesMs, int worldXCm, int worldYCm)
    {
        for (int i = 0; i < frames; i++)
        {
            SetGroundPointerOverride(engine, worldXCm, worldYCm);
            Tick(engine, 1, frameTimesMs);
        }
    }

    private static void SetGroundPointerOverride(GameEngine engine, int worldXCm, int worldYCm)
    {
        var pointerOverride = engine.GetService(CoreServiceKeys.AuthoritativeGroundPointerOverride)
            ?? throw new InvalidOperationException("AuthoritativeGroundPointerOverride service is missing.");
        InteractionActionBindings bindings = engine.GetService(CoreServiceKeys.InteractionActionBindings)
            ?? throw new InvalidOperationException("InteractionActionBindings service is missing.");

        pointerOverride.Set(bindings.CommandActionId, new Vector2(worldXCm, worldYCm));
    }

    private static void TickUntil(
        GameEngine engine,
        List<double> frameTimesMs,
        Func<bool> condition,
        int maxFrames,
        string because)
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

    private static void SelectEntity(GameEngine engine, Entity target)
    {
        var selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
            ?? throw new InvalidOperationException("SelectionRuntime service is missing.");
        Entity owner = engine.GetService(CoreServiceKeys.LocalPlayerEntity);

        Span<Entity> next = stackalloc Entity[1];
        next[0] = target;
        Assert.That(selection.ReplaceSelection(owner, SelectionSetKeys.LivePrimary, next), Is.True);
        selection.TryBindView(owner, SelectionViewKeys.Primary, owner, SelectionSetKeys.LivePrimary);
        engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = owner;
        engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
    }

    private static void AssertSelectedPrimary(GameEngine engine, Entity expected, string because)
    {
        Assert.That(
            SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity selected),
            Is.True,
            because);
        Assert.That(selected, Is.EqualTo(expected), because);
    }

    private static EntityCommandPanelSlotView GetCommandSlot(GameEngine engine, Entity target, int groupIndex, int slotIndex)
    {
        IEntityCommandPanelSource source = GetGasCommandPanelSource(engine);
        var context = new EntityCommandPanelSourceContext(target, "gas.ability-slots", "browser-rts-production-test");
        var slots = new EntityCommandPanelSlotView[AbilityStateBuffer.CAPACITY];
        int copied = EntityCommandPanelSourceDispatch.CopySlots(source, in context, groupIndex, slots);
        for (int i = 0; i < copied; i++)
        {
            if (slots[i].SlotIndex == slotIndex)
            {
                return slots[i];
            }
        }

        throw new InvalidOperationException($"Command slot {slotIndex} was not copied from group {groupIndex}.");
    }

    private static bool TryGetActiveStatus(GameEngine engine, Entity target, out EntityCommandPanelStatusView status)
    {
        IEntityCommandPanelSource source = GetGasCommandPanelSource(engine);
        var context = new EntityCommandPanelSourceContext(target, "gas.ability-slots", "browser-rts-production-test");
        var statuses = new EntityCommandPanelStatusView[8];
        int copied = EntityCommandPanelSourceDispatch.CopyStatuses(source, in context, statuses);
        for (int i = 0; i < copied; i++)
        {
            if (statuses[i].Kind == EntityCommandPanelStatusKind.ActiveAbility)
            {
                status = statuses[i];
                return true;
            }
        }

        status = default;
        return false;
    }

    private static IEntityCommandPanelSource GetGasCommandPanelSource(GameEngine engine)
    {
        var registry = engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry)
            ?? throw new InvalidOperationException("EntityCommandPanelSourceRegistry is missing.");
        return registry.TryGet("gas.ability-slots", out IEntityCommandPanelSource? source)
            ? source
            : throw new InvalidOperationException("EntityCommandPanel source 'gas.ability-slots' is missing.");
    }

    private static EntityCommandPanelQueueItemView[] CopyQueueItems(GameEngine engine, Entity target)
    {
        IEntityCommandPanelSource source = GetGasCommandPanelSource(engine);
        var context = new EntityCommandPanelSourceContext(target, "gas.ability-slots", "browser-rts-production-test");
        var queueItems = new EntityCommandPanelQueueItemView[8];
        int copied = EntityCommandPanelSourceDispatch.CopyQueueItems(source, in context, queueItems);
        return queueItems.Take(copied).ToArray();
    }

    private static void AssertActivateAbilitySlot(GameEngine engine, Entity target, int groupIndex, int slotIndex)
    {
        WebUiCommandResult result = ApplyBrowserRtsCommand(
            engine,
            "activateAbilitySlot",
            new
            {
                entityKey = EntityKey(target),
                groupIndex,
                slotIndex
            });
        Assert.That(result.Success, Is.True, $"{result.ErrorCode}: {result.Message}");
        AssertSelectedPrimary(engine, target, "Browser command activation should not clear the selected producer.");
    }

    private static bool HasTag(World world, Entity entity, string tagName)
    {
        int tagId = TagRegistry.GetId(tagName);
        return tagId > 0 &&
               world.TryGet(entity, out GameplayTagContainer tags) &&
               tags.HasTag(tagId);
    }

    private static int GetReadyTagCount(World world, Entity entity, string tagName)
    {
        int tagId = TagRegistry.GetId(tagName);
        if (tagId <= 0 || !world.TryGet(entity, out TagCountContainer counts))
        {
            return 0;
        }

        return counts.GetCount(tagId);
    }

    private static Entity FindEntity(World world, string entityName)
    {
        Entity result = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity entity, ref Name name) =>
        {
            if (result == Entity.Null && string.Equals(name.Value, entityName, StringComparison.Ordinal))
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
            if (string.Equals(name.Value, entityName, StringComparison.Ordinal))
            {
                count++;
            }
        });

        return count;
    }

    private static void AssertWorldPosition(World world, Entity entity, float expectedX, float expectedY)
    {
        Assert.That(world.TryGet(entity, out WorldPositionCm position), Is.True, "Spawned entity should have a world position.");
        Assert.That(position.Value.X.ToFloat(), Is.EqualTo(expectedX).Within(1f));
        Assert.That(position.Value.Y.ToFloat(), Is.EqualTo(expectedY).Within(1f));
    }

    private static void AssertPlacementPreviewVisible(GameEngine engine, int expectedXCm, int expectedYCm)
    {
        var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
            ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer missing.");
        var snapshot = engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer)
            ?? throw new InvalidOperationException("PresentationVisualSnapshotBuffer missing.");
        var meshRegistry = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry) as MeshAssetRegistry
            ?? throw new InvalidOperationException("PresentationMeshAssetRegistry missing.");

        int cubeId = meshRegistry.GetId("cube");
        Vector3 expectedPosition = new(expectedXCm * 0.01f, 0f, expectedYCm * 0.01f);
        int visiblePreviewParts =
            CountVisiblePrimitiveParts(primitives.GetSpan(), cubeId, expectedPosition, minScaleXZ: 4f) +
            CountVisiblePrimitiveParts(snapshot.GetSpan(), cubeId, expectedPosition, minScaleXZ: 4f);

        Assert.That(
            visiblePreviewParts,
            Is.GreaterThan(0),
            $"Placement ghost should emit a visible primitive cube near ({expectedXCm},{expectedYCm}). " +
            $"primitiveCube=[{DescribeMeshMatches(primitives.GetSpan(), cubeId)}] snapshotCube=[{DescribeMeshMatches(snapshot.GetSpan(), cubeId)}]");
    }

    private static void ClickEntityThroughCurrentSelection(GameEngine engine, Entity target)
    {
        if (!SpatialBoundsUtility.TryProjectScreenBounds(
                engine.World,
                target,
                engine.GetService(CoreServiceKeys.ScreenProjector),
                out ScreenRect screenBounds))
        {
            throw new InvalidOperationException("Target entity could not be projected to screen for selection.");
        }

        Vector2 pointer = new(
            (screenBounds.MinX + screenBounds.MaxX) * 0.5f,
            (screenBounds.MinY + screenBounds.MaxY) * 0.5f);
        WorldCmInt2 groundPoint = ResolveWorldPositionCm(engine.World, target);
        var selectionSystem = new CurrentSelectionApplySystem(engine.World, engine.GlobalContext);

        SetCoreSelectionPointer(engine, pointer, groundPoint, pressedThisFrame: true, isDown: true, releasedThisFrame: false);
        selectionSystem.Update(0f);

        SetCoreSelectionPointer(engine, pointer, groundPoint, pressedThisFrame: false, isDown: false, releasedThisFrame: true);
        selectionSystem.Update(0f);
    }

    private static void SetCoreSelectionPointer(
        GameEngine engine,
        Vector2 pointer,
        WorldCmInt2 groundPoint,
        bool pressedThisFrame,
        bool isDown,
        bool releasedThisFrame)
    {
        var input = engine.GetService(CoreServiceKeys.AuthoritativeInput) as FrozenInputActionReader
            ?? throw new InvalidOperationException("AuthoritativeInput service must be a FrozenInputActionReader.");
        var pointerButtons = engine.GetService(CoreServiceKeys.AuthoritativePointerButtons)
            ?? throw new InvalidOperationException("AuthoritativePointerButtons service is missing.");
        InteractionActionBindings bindings = engine.GetService(CoreServiceKeys.InteractionActionBindings)
            ?? throw new InvalidOperationException("InteractionActionBindings service is missing.");

        input.SetActionState(
            bindings.PointerPositionActionId,
            new Vector3(pointer.X, pointer.Y, 0f),
            isDown,
            pressedThisFrame,
            releasedThisFrame);
        input.SetActionState(
            AuthoritativeGroundPointerHelper.ActionId,
            new Vector3(groundPoint.X, 0f, groundPoint.Y),
            isDown,
            pressedThisFrame,
            releasedThisFrame);
        pointerButtons.SetState(
            bindings.ConfirmActionId,
            new PointerButtonState(
                pointer,
                pointer,
                pointer,
                pointer,
                isDown,
                pressedThisFrame,
                releasedThisFrame,
                hasPressPointer: pressedThisFrame,
                hasReleasePointer: releasedThisFrame,
                hasLastDownPointer: isDown || releasedThisFrame));
    }

    private static void ClearAuthoritativeInput(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.AuthoritativeInput) is FrozenInputActionReader input)
        {
            input.Clear();
        }

        engine.GetService(CoreServiceKeys.AuthoritativePointerButtons)?.Clear();
    }

    private static WorldCmInt2 ResolveWorldPositionCm(World world, Entity target)
    {
        if (world.TryGet(target, out WorldPositionCm position))
        {
            return new WorldCmInt2((int)MathF.Round(position.Value.X.ToFloat()), (int)MathF.Round(position.Value.Y.ToFloat()));
        }

        if (world.TryGet(target, out VisualTransform visual))
        {
            return new WorldCmInt2((int)MathF.Round(visual.Position.X * 100f), (int)MathF.Round(visual.Position.Z * 100f));
        }

        throw new InvalidOperationException("Target entity has no world position.");
    }

    private static int CountSelectionRingsNear(GameEngine engine, Entity target)
    {
        var overlays = engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
            ?? throw new InvalidOperationException("GroundOverlayBuffer service is missing.");
        Vector3 targetPosition = engine.World.TryGet(target, out VisualTransform transform)
            ? transform.Position
            : WorldUnits.WorldCmToVisualMeters(engine.World.Get<WorldPositionCm>(target).Value, yMeters: 0f);
        Vector2 targetXz = new(targetPosition.X, targetPosition.Z);
        int count = 0;
        ReadOnlySpan<GroundOverlayItem> items = overlays.GetSpan();
        for (int i = 0; i < items.Length; i++)
        {
            ref readonly GroundOverlayItem item = ref items[i];
            if (item.Shape != GroundOverlayShape.Ring)
            {
                continue;
            }

            Vector2 itemXz = new(item.Center.X, item.Center.Z);
            if (Vector2.DistanceSquared(itemXz, targetXz) <= 9f)
            {
                count++;
            }
        }

        return count;
    }

    private static WebUiCommandResult ApplyBrowserRtsCommand(GameEngine engine, string name, object payload)
    {
        object topic = CreateBrowserRtsTopic(engine, out Type topicType);
        MethodInfo apply = topicType.GetMethod("ApplyCommand", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Browser RTS production topic producer does not expose ApplyCommand.");

        var request = new WebUiCommandRequest(
            name,
            ClientSeq: 1,
            EntityRefs: Array.Empty<WebUiEntityRef>(),
            Payload: JsonSerializer.SerializeToElement(payload));

        return (WebUiCommandResult)(apply.Invoke(topic, new object[] { request })
            ?? throw new InvalidOperationException("ApplyCommand returned null."));
    }

    private static JsonDocument CreateBrowserRtsSnapshot(GameEngine engine)
    {
        object topic = CreateBrowserRtsTopic(engine, out _);
        var producer = topic as IWebUiTopicProducer
            ?? throw new InvalidOperationException("Browser RTS production topic producer does not implement IWebUiTopicProducer.");
        var context = new WebUiTopicContext(
            "test-session",
            "ludots.showcase.rtsProduction.world",
            RequestId: 1,
            Parameters: default);
        if (!producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet))
        {
            throw new InvalidOperationException("Browser RTS production topic producer did not create a snapshot.");
        }

        return JsonDocument.Parse(packet.Payload);
    }

    private static object CreateBrowserRtsTopic(GameEngine engine, out Type topicType)
    {
        topicType = typeof(BrowserRtsProductionShowcaseModEntry).Assembly.GetType(
                "BrowserRtsProductionShowcaseMod.BrowserRtsProductionShowcaseTopicProducer",
                throwOnError: true)
            ?? throw new InvalidOperationException("Browser RTS production topic producer type is missing.");

        return Activator.CreateInstance(topicType, engine)
            ?? throw new InvalidOperationException("Browser RTS production topic producer could not be constructed.");
    }

    private static string EntityKey(Entity entity)
    {
        return $"{entity.Id}:{entity.WorldId}:{entity.Version}";
    }

    private static void AssertPrimitiveMesh(MeshAssetRegistry meshRegistry, string assetId, PrimitiveMeshKind expectedKind)
    {
        int id = meshRegistry.GetId(assetId);
        Assert.That(id, Is.GreaterThan(0), $"Mesh asset '{assetId}' should be registered.");
        Assert.That(meshRegistry.TryGetPrimitiveKind(id, out PrimitiveMeshKind kind), Is.True);
        Assert.That(kind, Is.EqualTo(expectedKind));
    }

    private static void AssertEntityUsesPrimitiveCompositeVisual(GameEngine engine, string entityName, int minimumParts, bool requiresSphere)
    {
        Entity entity = FindEntity(engine.World, entityName);
        Vector3 expectedPosition = engine.World.TryGet(entity, out VisualTransform transform)
            ? transform.Position
            : WorldUnits.WorldCmToVisualMeters(engine.World.Get<WorldPositionCm>(entity).Value, yMeters: 0f);

        var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
            ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer missing.");
        var snapshot = engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer)
            ?? throw new InvalidOperationException("PresentationVisualSnapshotBuffer missing.");
        var meshRegistry = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry) as MeshAssetRegistry
            ?? throw new InvalidOperationException("PresentationMeshAssetRegistry missing.");
        int cubeId = meshRegistry.GetId("cube");
        int sphereId = meshRegistry.GetId("sphere");

        int cubeParts = CountVisiblePrimitiveParts(primitives.GetSpan(), cubeId, expectedPosition) +
                        CountVisiblePrimitiveParts(snapshot.GetSpan(), cubeId, expectedPosition);
        int sphereParts = CountVisiblePrimitiveParts(primitives.GetSpan(), sphereId, expectedPosition) +
                          CountVisiblePrimitiveParts(snapshot.GetSpan(), sphereId, expectedPosition);
        int totalParts = cubeParts + sphereParts;

        Assert.That(
            totalParts,
            Is.GreaterThanOrEqualTo(minimumParts),
            $"{entityName} should be built from several primitive child performers. {BuildPrimitiveDiagnostic(engine, entity, primitives, snapshot, cubeId, sphereId)}");
        Assert.That(cubeParts, Is.GreaterThan(0), $"{entityName} should include cube body parts.");
        if (requiresSphere)
        {
            Assert.That(sphereParts, Is.GreaterThan(0), $"{entityName} should include at least one sphere detail.");
        }
    }

    private static int CountVisiblePrimitiveParts(ReadOnlySpan<PrimitiveDrawItem> items, int meshAssetId, Vector3 expectedPosition)
    {
        return CountVisiblePrimitiveParts(items, meshAssetId, expectedPosition, minScaleXZ: 0f);
    }

    private static int CountVisiblePrimitiveParts(ReadOnlySpan<PrimitiveDrawItem> items, int meshAssetId, Vector3 expectedPosition, float minScaleXZ)
    {
        int count = 0;
        Vector2 entityPosition = new(expectedPosition.X, expectedPosition.Z);
        for (int i = 0; i < items.Length; i++)
        {
            ref readonly PrimitiveDrawItem item = ref items[i];
            if (item.Visibility != VisualVisibility.Visible ||
                item.MeshAssetId != meshAssetId ||
                MathF.Max(MathF.Abs(item.Scale.X), MathF.Abs(item.Scale.Z)) < minScaleXZ)
            {
                continue;
            }

            Vector2 primitivePosition = new(item.Position.X, item.Position.Z);
            if (Vector2.DistanceSquared(primitivePosition, entityPosition) <= 25f)
            {
                count++;
            }
        }

        return count;
    }

    private static string BuildPrimitiveDiagnostic(
        GameEngine engine,
        Entity entity,
        PrimitiveDrawBuffer primitives,
        PrimitiveDrawBuffer snapshot,
        int cubeId,
        int sphereId)
    {
        string entityName = engine.World.TryGet(entity, out Name name) ? name.Value : $"#{entity.Id}";
        bool hasStableId = engine.World.Has<PresentationStableId>(entity);
        bool hasLifecycle = engine.World.Has<PresentationLifecycleState>(entity);
        bool hasBootstrapHandled = engine.World.Has<PerformerRootBootstrapHandled>(entity);
        bool hasPayload = engine.World.Has<PresentationOwnerHasPerformerPayload>(entity);
        string payload = hasPayload
            ? FormatPayload(engine.World.Get<PresentationOwnerHasPerformerPayload>(entity))
            : "<none>";
        string activePerformers = engine.GetService(CoreServiceKeys.PerformerEntityRuntime)?.BuildActiveDefinitionSummary(16) ?? "<none>";

        return
            $"entity={entityName} stableId={hasStableId} lifecycle={hasLifecycle} bootstrapHandled={hasBootstrapHandled} payload={payload} " +
            $"primitiveCube=[{DescribeMeshMatches(primitives.GetSpan(), cubeId)}] snapshotCube=[{DescribeMeshMatches(snapshot.GetSpan(), cubeId)}] " +
            $"primitiveSphere=[{DescribeMeshMatches(primitives.GetSpan(), sphereId)}] snapshotSphere=[{DescribeMeshMatches(snapshot.GetSpan(), sphereId)}] performers=[{activePerformers}]";
    }

    private static string FormatPayload(PresentationOwnerHasPerformerPayload payload)
    {
        return $"count={payload.Count},roots={payload.RootCount},single={payload.SingleRootPerformer.Id}:{payload.SingleRootPerformer.Version},sync={payload.SingleRootTransformSync}";
    }

    private static void AssertVectorNear(Vector3 actual, Vector3 expected, string message)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.001f), message);
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.001f), message);
        Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(0.001f), message);
    }

    private static string DescribeMeshMatches(ReadOnlySpan<PrimitiveDrawItem> items, int expectedMeshAssetId)
    {
        var matches = new List<string>(4);
        for (int i = 0; i < items.Length && matches.Count < 8; i++)
        {
            ref readonly PrimitiveDrawItem item = ref items[i];
            if (item.MeshAssetId != expectedMeshAssetId)
            {
                continue;
            }

            matches.Add($"{item.Visibility}@({item.Position.X:F1},{item.Position.Z:F1}) scale=({item.Scale.X:F1},{item.Scale.Y:F1},{item.Scale.Z:F1})");
        }

        return matches.Count == 0 ? "<none>" : string.Join("; ", matches);
    }

    private static void InstallDummyInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    private static void InstallHeadlessPresentationHost(GameEngine engine)
    {
        var view = new StubViewController(1920f, 1080f);
        engine.SetService(CoreServiceKeys.ViewController, view);

        var timingDiagnostics = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
        var cameraAdapter = new StubCameraAdapter();
        var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter, timingDiagnostics);
        var screenProjector = new CoreScreenProjector(engine.GameSession.Camera, view);
        var screenRayProvider = new CoreScreenRayProvider(engine.GameSession.Camera, view);
        var presentationFrameSetup = engine.GetService(CoreServiceKeys.PresentationFrameSetup);
        screenProjector.BindPresenter(cameraPresenter);
        screenRayProvider.BindPresenter(cameraPresenter);
        screenProjector.BindPresentationAlphaProvider(() => presentationFrameSetup?.GetInterpolationAlpha() ?? 1f);
        screenRayProvider.BindPresentationAlphaProvider(() => presentationFrameSetup?.GetInterpolationAlpha() ?? 1f);
        engine.SetService(CoreServiceKeys.ScreenProjector, screenProjector);
        engine.SetService(CoreServiceKeys.ScreenRayProvider, screenRayProvider);

        var culling = new CameraCullingSystem(
            engine.World,
            engine.GameSession.Camera,
            engine.SpatialQueries,
            view,
            cullingConfig: engine.MergedConfig.Presentation.CameraCulling,
            loadedChunks: null,
            performers: engine.GetService(CoreServiceKeys.PerformerEntityRuntime),
            timingDiagnostics: timingDiagnostics);
        engine.InsertPresentationSystemBefore<PresentationEntityLifecycleSystem>(culling);
        engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);
        engine.GlobalContext[HeadlessCameraKey] = new HeadlessCameraRuntime(cameraPresenter, presentationFrameSetup);
    }

    private static void UpdateHeadlessCamera(GameEngine engine)
    {
        if (!engine.GlobalContext.TryGetValue(HeadlessCameraKey, out object? runtimeObj) ||
            runtimeObj is not HeadlessCameraRuntime runtime)
        {
            return;
        }

        float alpha = runtime.PresentationFrameSetup?.GetInterpolationAlpha() ?? 1f;
        runtime.CameraPresenter.Update(engine.GameSession.Camera, alpha);
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
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }

    private const string HeadlessCameraKey = "Tests.RtsProductionBrowser.HeadlessCamera";

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

    private sealed class StubCameraAdapter : ICameraAdapter
    {
        public CameraRenderState3D LastState { get; private set; }

        public void UpdateCamera(in CameraRenderState3D state)
        {
            LastState = state;
        }
    }

    private sealed class HeadlessCameraRuntime
    {
        public HeadlessCameraRuntime(CameraPresenter cameraPresenter, PresentationFrameSetupSystem? presentationFrameSetup)
        {
            CameraPresenter = cameraPresenter;
            PresentationFrameSetup = presentationFrameSetup;
        }

        public CameraPresenter CameraPresenter { get; }
        public PresentationFrameSetupSystem? PresentationFrameSetup { get; }
    }
}
