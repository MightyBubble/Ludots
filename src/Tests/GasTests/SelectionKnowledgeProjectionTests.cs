using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using CoreInputMod.Systems;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Knowledge;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
public sealed class SelectionKnowledgeProjectionTests
{
    [Test]
    public void Issue197_ClickAndBoxSelectionGateCameraVisibleCandidatesThroughKnowledgeProjection()
    {
        TeamRelationshipSnapshot relationships = TeamManager.CaptureSnapshot();
        try
        {
            TeamManager.Clear();
            using var world = World.Create();
            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create(new Team { Id = 1 });
            Entity ally = world.Create();
            Entity unknown = CreateSelectable(world, xCm: 1000, zCm: 1000, teamId: 1);
            Entity lastKnown = CreateSelectable(world, xCm: 2000, zCm: 1000, teamId: 1);
            Entity disclosedLive = CreateSelectable(world, xCm: 3000, zCm: 1000, teamId: 1);
            Entity directLive = CreateSelectable(world, xCm: 4000, zCm: 1000, teamId: 1);
            var globals = CreateSelectionGlobals(world, input, local, relationFilter: "Friendly");
            SelectionRuntime selection = (SelectionRuntime)globals[CoreServiceKeys.SelectionRuntime.Name];
            InstallKnowledge(
                world,
                globals,
                local,
                ally,
                disclosedLive,
                directLive,
                lastKnown);
            var system = new CurrentSelectionApplySystem(world, globals);

            Click(system, globals, input, new Vector2(1000f, 1000f));
            AssertSelection(selection, local);

            Click(system, globals, input, new Vector2(2000f, 1000f));
            AssertSelection(selection, local);

            Click(system, globals, input, new Vector2(3000f, 1000f));
            AssertSelection(selection, local, disclosedLive);

            DragSelect(system, globals, input, new Vector2(500f, 500f), new Vector2(4500f, 1500f));
            AssertSelectionEquivalent(selection, local, disclosedLive, directLive);
            Assert.That(unknown, Is.Not.EqualTo(Entity.Null));
        }
        finally
        {
            TeamManager.RestoreSnapshot(relationships);
        }
    }

    [Test]
    public void Issue197_RelationshipFilterStillDeniesHostileEvenWhenKnowledgeAllowsLiveInspection()
    {
        TeamRelationshipSnapshot relationships = TeamManager.CaptureSnapshot();
        try
        {
            TeamManager.Clear();
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
            using var world = World.Create();
            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create(new Team { Id = 1 });
            Entity hostile = CreateSelectable(world, xCm: 2000, zCm: 1000, teamId: 2);
            var globals = CreateSelectionGlobals(world, input, local, relationFilter: "Friendly");
            SelectionRuntime selection = (SelectionRuntime)globals[CoreServiceKeys.SelectionRuntime.Name];
            var store = new KnowledgeProjectionStore();
            store.Upsert(local, hostile, CreateRecord(KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live, local));
            globals[CoreServiceKeys.KnowledgeProjectionResolver.Name] = new KnowledgeProjectionResolver(store);
            var system = new CurrentSelectionApplySystem(world, globals);

            Click(system, globals, input, new Vector2(2000f, 1000f));

            AssertSelection(selection, local);
        }
        finally
        {
            TeamManager.RestoreSnapshot(relationships);
        }
    }

    [Test]
    public void Issue197_CommandTargetingCanRequireEntityIdentityOrLivePosition()
    {
        using var world = World.Create();
        Entity viewer = world.Create();
        Entity identityOnly = world.Create();
        Entity lastKnown = world.Create();
        Entity live = world.Create();
        var globals = new Dictionary<string, object> { [CoreServiceKeys.LocalPlayerEntity.Name] = viewer };
        var store = new KnowledgeProjectionStore();
        store.Upsert(viewer, identityOnly, CreateRecord(KnowledgePresence.Known, KnowledgePositionAccess.None, viewer));
        store.Upsert(viewer, lastKnown, CreateRecord(KnowledgePresence.Known, KnowledgePositionAccess.LastKnown, viewer));
        store.Upsert(viewer, live, CreateRecord(KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live, viewer));
        globals[CoreServiceKeys.KnowledgeProjectionResolver.Name] = new KnowledgeProjectionResolver(store);

        Assert.That(SelectionEligibility.CanTargetCommand(world, globals, viewer, identityOnly, KnowledgePositionAccess.None), Is.True);
        Assert.That(SelectionEligibility.CanTargetCommand(world, globals, viewer, identityOnly, KnowledgePositionAccess.LastKnown), Is.False);
        Assert.That(SelectionEligibility.CanTargetCommand(world, globals, viewer, lastKnown, KnowledgePositionAccess.LastKnown), Is.True);
        Assert.That(SelectionEligibility.CanTargetCommand(world, globals, viewer, lastKnown, KnowledgePositionAccess.Live), Is.False);
        Assert.That(SelectionEligibility.CanTargetCommand(world, globals, viewer, live, KnowledgePositionAccess.Live), Is.True);
    }

    [Test]
    public void ExplicitSelectionViewer_IsNotOverriddenBySelectionViewDiagnosticsViewer()
    {
        using var world = World.Create();
        Entity localViewer = world.Create();
        Entity diagnosticsViewer = world.Create();
        Entity live = world.Create(new SelectionSelectableTag());
        var globals = new Dictionary<string, object>
        {
            [CoreServiceKeys.LocalPlayerEntity.Name] = localViewer,
            [CoreServiceKeys.EntityViewViewerEntity.Name] = diagnosticsViewer,
        };
        var store = new KnowledgeProjectionStore();
        store.Upsert(localViewer, live, CreateRecord(KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live, localViewer));
        globals[CoreServiceKeys.KnowledgeProjectionResolver.Name] = new KnowledgeProjectionResolver(store);

        Assert.That(SelectionEligibility.CanInspectLive(world, globals, localViewer, live), Is.True);
    }

    [Test]
    public void Issue197_GasSelectionResponseFiltersUnknownAndLastKnownCandidatesThroughProjection()
    {
        using var world = World.Create();
        var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
        Entity origin = world.Create(new Team { Id = 1 });
        Entity unknown = world.Create(WorldPositionCm.FromCm(100, 0), new Team { Id = 2 }, new SelectionSelectableTag());
        Entity lastKnown = world.Create(WorldPositionCm.FromCm(120, 0), new Team { Id = 2 }, new SelectionSelectableTag());
        Entity live = world.Create(WorldPositionCm.FromCm(140, 0), new Team { Id = 2 }, new SelectionSelectableTag());
        var globals = new Dictionary<string, object>
        {
            [CoreServiceKeys.AuthoritativeInput.Name] = input,
            [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
            [CoreServiceKeys.SelectionRequestQueue.Name] = new SelectionRequestQueue(),
            [CoreServiceKeys.SelectionResponseBuffer.Name] = new SelectionResponseBuffer(),
            [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings { ConfirmActionId = "Select" },
            [CoreServiceKeys.LocalPlayerEntity.Name] = origin,
        };
        var store = new KnowledgeProjectionStore();
        store.Upsert(origin, lastKnown, CreateRecord(KnowledgePresence.Known, KnowledgePositionAccess.LastKnown, origin));
        store.Upsert(origin, live, CreateRecord(KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live, origin));
        globals[CoreServiceKeys.KnowledgeProjectionResolver.Name] = new KnowledgeProjectionResolver(store);
        var rules = new SelectionRuleRegistry();
        rules.Register(77, new SelectionRule
        {
            Mode = SelectionRuleMode.Radius,
            RelationshipFilter = RelationshipFilter.All,
            RadiusCm = 300,
            MaxCount = 8,
        });
        var system = new GasSelectionResponseSystem(world, globals, new StubSpatialQueryService(unknown, lastKnown, live), rules);
        var requests = (SelectionRequestQueue)globals[CoreServiceKeys.SelectionRequestQueue.Name];
        var responses = (SelectionResponseBuffer)globals[CoreServiceKeys.SelectionResponseBuffer.Name];
        requests.TryEnqueue(new SelectionRequest { RequestId = 42, RequestTagId = 77, Origin = origin });

        SetActionSnapshot(globals, "Select", new Vector2(0f, 0f), pressedThisFrame: true, isDown: true);
        SetAuthoritativeGroundPoint(input, new WorldCmInt2(1, 1));
        input.Update();
        system.Update(0f);

        Assert.That(responses.TryConsume(42, out SelectionResponse response), Is.True);
        Assert.That(response.Count, Is.EqualTo(1));
        Assert.That(response.GetEntity(0), Is.EqualTo(live));
    }

    [Test]
    public void Issue197_TabTargetCycleFiltersUnknownAndLastKnownCandidatesThroughProjection()
    {
        using var world = World.Create();
        var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
        Entity local = world.Create(
            new Team { Id = 1 },
            new VisualTransform { Position = Vector3.Zero });
        Entity unknown = world.Create(
            new Team { Id = 2 },
            new VisualTransform { Position = new Vector3(5f, 0f, 0f) },
            new SelectionSelectableTag());
        Entity lastKnown = world.Create(
            new Team { Id = 2 },
            new VisualTransform { Position = new Vector3(10f, 0f, 0f) },
            new SelectionSelectableTag());
        Entity live = world.Create(
            new Team { Id = 2 },
            new VisualTransform { Position = new Vector3(15f, 0f, 0f) },
            new SelectionSelectableTag());
        var globals = new Dictionary<string, object>
        {
            [CoreServiceKeys.AuthoritativeInput.Name] = input,
            [CoreServiceKeys.LocalPlayerEntity.Name] = local,
        };
        var store = new KnowledgeProjectionStore();
        store.Upsert(local, lastKnown, CreateRecord(KnowledgePresence.Known, KnowledgePositionAccess.LastKnown, local));
        store.Upsert(local, live, CreateRecord(KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live, local));
        globals[CoreServiceKeys.KnowledgeProjectionResolver.Name] = new KnowledgeProjectionResolver(store);
        var system = new TabTargetCycleSystem(world, globals, searchRadiusCm: 3000);

        input.InjectButtonPress(TabTargetCycleSystem.TabTargetActionId);
        input.Update();
        system.Update(0f);

        Assert.That(globals.TryGetValue(CoreServiceKeys.TabTargetEntity.Name, out object? targetObj), Is.True);
        Assert.That(targetObj, Is.EqualTo(live));
        Assert.That(unknown, Is.Not.EqualTo(Entity.Null));
    }

    private static Dictionary<string, object> CreateSelectionGlobals(
        World world,
        PlayerInputHandler input,
        Entity local,
        string relationFilter)
    {
        var globals = new Dictionary<string, object>
        {
            [CoreServiceKeys.AuthoritativeInput.Name] = input,
            [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
            [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
            [CoreServiceKeys.LocalPlayerEntity.Name] = local,
            [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings { ConfirmActionId = "Select" },
        };
        var config = new SelectionRuntimeConfig
        {
            TargetFilter = new SelectionTargetFilterConfig { RelationFilter = relationFilter },
            Acquisition = new SelectionAcquisitionConfig(),
        };
        var selectionKeys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
        var selection = new SelectionRuntime(world, config, selectionKeys);
        var collectionKeys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
        globals[CoreServiceKeys.SelectionRuntime.Name] = selection;
        globals[CoreServiceKeys.SelectionSetKeyRegistry.Name] = selectionKeys;
        globals[CoreServiceKeys.EntityCollectionStore.Name] = new EntityCollectionStore(collectionKeys);
        globals[CoreServiceKeys.EntityCollectionKeyRegistry.Name] = collectionKeys;
        return globals;
    }

    private static Entity CreateSelectable(World world, int xCm, int zCm, int teamId)
    {
        return world.Create(
            WorldPositionCm.FromCm(xCm, zCm),
            new VisualTransform { Position = new Vector3(xCm / 100f, 0f, zCm / 100f) },
            new CullState { IsVisible = true },
            new SelectionSelectableTag(),
            new Team { Id = teamId });
    }

    private static void InstallKnowledge(
        World world,
        Dictionary<string, object> globals,
        Entity viewer,
        Entity ally,
        Entity disclosedLive,
        Entity directLive,
        Entity lastKnown)
    {
        var relationshipTypes = new RelationshipTypeRegistry();
        int intelTypeId = relationshipTypes.Register("Intel");
        var relationships = new RelationshipRuntime(
            world,
            relationshipTypes,
            new RelationshipMetricRegistry(),
            new RelationshipFlagRegistry(),
            new RelationshipBandRegistry(),
            new RelationshipChangeBuffer(capacity: 4));
        var collections = (EntityCollectionStore)globals[CoreServiceKeys.EntityCollectionStore.Name];
        relationships.EnsureLink(viewer, ally, intelTypeId);
        collections.Replace(
            ally,
            EntityCollectionDescriptor.Create("test.disclosed.live", EntityCollectionSourceKind.RelationDerived, EntityCollectionRoleKind.Display),
            stackalloc Entity[1] { disclosedLive });
        var store = new KnowledgeProjectionStore();
        store.Upsert(viewer, directLive, CreateRecord(KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live, viewer));
        store.Upsert(viewer, lastKnown, CreateRecord(KnowledgePresence.Known, KnowledgePositionAccess.LastKnown, viewer));
        var catalog = RelationshipCatalogRuntime.Compile(
            new RelationshipCatalogConfig
            {
                KnowledgeGrants =
                {
                    new RelationshipKnowledgeGrantConfig
                    {
                        Id = "test.intel.disclosed.live",
                        TypeId = "Intel",
                        CollectionKey = "test.disclosed.live",
                        Presence = KnowledgePresence.LiveVisible,
                        Position = KnowledgePositionAccess.Live,
                        AttributeIds = { 1 },
                        RelationshipTypeIds = { 2 },
                        ObservedTick = 1,
                        ConfidencePermille = 900
                    }
                }
            },
            relationshipTypes,
            new RelationshipMetricRegistry(),
            collections);
        var projector = new KnowledgeRelationCollectionProjector(relationships, collections, catalog, store);
        globals[CoreServiceKeys.KnowledgeProjectionResolver.Name] = new KnowledgeProjectionResolver(store, projector);
    }

    private static KnowledgeDisclosureRecord CreateRecord(
        KnowledgePresence presence,
        KnowledgePositionAccess position,
        Entity source)
    {
        return new KnowledgeDisclosureRecord(
            presence,
            position,
            KnowledgeIdMask256.Empty.WithId(1),
            KnowledgeIdMask256.Empty.WithId(2),
            KnowledgeIdMask256.Empty,
            source,
            observedTick: 1,
            expiryTick: 0,
            confidencePermille: 900,
            revision: 0);
    }

    private static void Click(
        CurrentSelectionApplySystem system,
        Dictionary<string, object> globals,
        PlayerInputHandler input,
        Vector2 pointer)
    {
        SetActionSnapshot(globals, "Select", pointer, pressedThisFrame: true, isDown: true);
        SetAuthoritativeGroundPoint(input, new WorldCmInt2((int)pointer.X, (int)pointer.Y));
        input.InjectAction("PointerPos", new Vector3(pointer.X, pointer.Y, 0f));
        input.Update();
        system.Update(0f);

        SetActionSnapshot(globals, "Select", pointer, pressedThisFrame: false, isDown: false, releasedThisFrame: true);
        SetAuthoritativeGroundPoint(input, new WorldCmInt2((int)pointer.X, (int)pointer.Y));
        input.InjectAction("PointerPos", new Vector3(pointer.X, pointer.Y, 0f));
        input.Update();
        system.Update(0f);
    }

    private static void DragSelect(
        CurrentSelectionApplySystem system,
        Dictionary<string, object> globals,
        PlayerInputHandler input,
        Vector2 from,
        Vector2 to)
    {
        SetActionSnapshot(globals, "Select", from, pressedThisFrame: true, isDown: true);
        input.InjectAction("PointerPos", new Vector3(from.X, from.Y, 0f));
        input.Update();
        system.Update(0f);

        SetActionSnapshot(globals, "Select", to, pressedThisFrame: false, isDown: true);
        input.InjectAction("PointerPos", new Vector3(to.X, to.Y, 0f));
        input.Update();
        system.Update(0f);

        SetActionSnapshot(globals, "Select", to, pressedThisFrame: false, isDown: false, releasedThisFrame: true);
        input.InjectAction("PointerPos", new Vector3(to.X, to.Y, 0f));
        input.Update();
        system.Update(0f);
    }

    private static void SetActionSnapshot(
        Dictionary<string, object> globals,
        string actionId,
        Vector2 pointer,
        bool pressedThisFrame,
        bool isDown,
        bool releasedThisFrame = false)
    {
        var pointerButtons = (AuthoritativePointerButtonSnapshot)globals[CoreServiceKeys.AuthoritativePointerButtons.Name];
        pointerButtons.SetState(
            actionId,
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

    private static void SetAuthoritativeGroundPoint(PlayerInputHandler input, in WorldCmInt2 worldCm)
    {
        input.InjectAction(AuthoritativeGroundPointerHelper.ActionId, new Vector3(worldCm.X, 0f, worldCm.Y));
    }

    private static void AssertSelection(SelectionRuntime selection, Entity owner, params Entity[] expected)
    {
        Assert.That(selection.GetSelectionCount(owner, SelectionSetKeys.LivePrimary), Is.EqualTo(expected.Length));
        Entity[] actual = new Entity[expected.Length];
        int written = selection.CopySelection(owner, SelectionSetKeys.LivePrimary, actual);
        Assert.That(written, Is.EqualTo(expected.Length));
        Assert.That(actual, Is.EqualTo(expected));
    }

    private static void AssertSelectionEquivalent(SelectionRuntime selection, Entity owner, params Entity[] expected)
    {
        int count = selection.GetSelectionCount(owner, SelectionSetKeys.LivePrimary);
        Entity[] actual = new Entity[count];
        int written = selection.CopySelection(owner, SelectionSetKeys.LivePrimary, actual);
        Assert.That(written, Is.EqualTo(count));
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    private static InputConfigRoot CreateInputConfig()
    {
        return new InputConfigRoot
        {
            Actions = new List<InputActionDef>
            {
                new() { Id = "Select", Name = "Select", Type = InputActionType.Button },
                new() { Id = "Command", Name = "Command", Type = InputActionType.Button },
                new() { Id = "Cancel", Name = "Cancel", Type = InputActionType.Button },
                new() { Id = "PointerPos", Name = "PointerPos", Type = InputActionType.Axis2D },
                new() { Id = AuthoritativeGroundPointerHelper.ActionId, Name = AuthoritativeGroundPointerHelper.ActionId, Type = InputActionType.Axis3D },
                new() { Id = TabTargetCycleSystem.TabTargetActionId, Name = TabTargetCycleSystem.TabTargetActionId, Type = InputActionType.Button },
                new() { Id = TabTargetCycleSystem.TabTargetReverseActionId, Name = TabTargetCycleSystem.TabTargetReverseActionId, Type = InputActionType.Button },
            },
            Contexts = new List<InputContextDef> { new() { Id = "Test", Name = "Test", Priority = 1 } },
        };
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

    private sealed class WorldMappedScreenProjector : IScreenProjector
    {
        public Vector2 WorldToScreen(Vector3 worldPosition)
        {
            return new Vector2(worldPosition.X * 100f, worldPosition.Z * 100f);
        }
    }

    private sealed class StubSpatialQueryService : ISpatialQueryService
    {
        private readonly Entity[] _results;

        public StubSpatialQueryService(params Entity[] results)
        {
            _results = results ?? Array.Empty<Entity>();
        }

        public SpatialQueryResult QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer) => Write(buffer);
        public SpatialQueryResult QueryRadius(WorldCmInt2 center, int radiusCm, Span<Entity> buffer) => Write(buffer);
        public SpatialQueryResult QueryCone(WorldCmInt2 origin, int directionDeg, int halfAngleDeg, int rangeCm, Span<Entity> buffer) => Write(buffer);
        public SpatialQueryResult QueryRectangle(WorldCmInt2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer) => Write(buffer);
        public SpatialQueryResult QueryLine(WorldCmInt2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer) => Write(buffer);
        public SpatialQueryResult QueryHexRange(HexCoordinates center, int hexRadius, Span<Entity> buffer) => Write(buffer);
        public SpatialQueryResult QueryHexRing(HexCoordinates center, int hexRadius, Span<Entity> buffer) => Write(buffer);

        private SpatialQueryResult Write(Span<Entity> buffer)
        {
            int count = Math.Min(buffer.Length, _results.Length);
            for (int i = 0; i < count; i++)
            {
                buffer[i] = _results[i];
            }

            return new SpatialQueryResult(count, Math.Max(0, _results.Length - count));
        }
    }
}
