using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Tests.TestCommon;
using CoreInputMod.Systems;
using Ludots.Core.Association;
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
using Ludots.Core.Input.CommandSources;
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
[Category("ci-gate")]
public sealed class SelectionKnowledgeProjectionTests
{
    [Test]
    public void Issue197_ClickAndBoxCommandSourceGateCameraVisibleCandidatesThroughKnowledgeProjection()
    {
        using var world = World.Create();
        var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
        var local = world.Create(new PlayerIdentity { PlayerId = 1 });
        Entity ally = world.Create();
        Entity unknown = CreateSelectable(world, xCm: 1000, zCm: 1000);
        Entity lastKnown = CreateSelectable(world, xCm: 2000, zCm: 1000);
        Entity disclosedLive = CreateSelectable(world, xCm: 3000, zCm: 1000);
        Entity directLive = CreateSelectable(world, xCm: 4000, zCm: 1000);
        var globals = CreateCommandSourceGlobals(world, input, local, relationFilter: "Friendly");
        CommandSourceDomainHarness domains = InstallCommandSourceDomainServices(world, globals);
        domains.Relationships.EnsureLink(local, unknown, domains.OwnsTypeId);
        domains.Relationships.EnsureLink(local, lastKnown, domains.OwnsTypeId);
        domains.Relationships.EnsureLink(local, disclosedLive, domains.OwnsTypeId);
        domains.Relationships.EnsureLink(local, directLive, domains.OwnsTypeId);
        InstallKnowledge(
            world,
            globals,
            local,
            ally,
            disclosedLive,
            directLive,
            lastKnown);
        var collections = (EntityCollectionStore)globals[CoreServiceKeys.EntityCollectionStore.Name];
        var system = CreateCommandSourceAcquisitionSystem(world, globals, local);

        Click(system, globals, input, new Vector2(1000f, 1000f));
        AssertCommandSource(collections, local);

        Click(system, globals, input, new Vector2(2000f, 1000f));
        AssertCommandSource(collections, local);

        Click(system, globals, input, new Vector2(3000f, 1000f));
        AssertCommandSource(collections, local, disclosedLive);

        DragSelect(system, globals, input, new Vector2(500f, 500f), new Vector2(4500f, 1500f));
        AssertCommandSourceEquivalent(collections, local, disclosedLive, directLive);
        Assert.That(unknown, Is.Not.EqualTo(Entity.Null));
    }

    [Test]
    public void Issue197_RelationshipFilterStillDeniesHostileEvenWhenKnowledgeAllowsLiveInspection()
    {
        using var world = World.Create();
        var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
        var local = world.Create(new PlayerIdentity { PlayerId = 1 });
        var hostileDomain = world.Create(new PlayerIdentity { PlayerId = 2 });
        Entity hostile = CreateSelectable(world, xCm: 2000, zCm: 1000);
        var globals = CreateCommandSourceGlobals(world, input, local, relationFilter: "Friendly");
        CommandSourceDomainHarness domains = InstallCommandSourceDomainServices(world, globals);
        domains.Relationships.EnsureLink(hostileDomain, hostile, domains.OwnsTypeId);
        domains.Relationships.EnsureLink(local, hostileDomain, domains.HostileTypeId);
        var store = new KnowledgeProjectionStore();
        store.Upsert(local, hostile, CreateRecord(KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live, local));
        globals[CoreServiceKeys.KnowledgeProjectionResolver.Name] = new KnowledgeProjectionResolver(store);
        var collections = (EntityCollectionStore)globals[CoreServiceKeys.EntityCollectionStore.Name];
        var system = CreateCommandSourceAcquisitionSystem(world, globals, local);

        Click(system, globals, input, new Vector2(2000f, 1000f));

        AssertCommandSource(collections, local);
    }

    [Test]
    public void Issue197_CommandTargetingCanRequireEntityIdentityOrLivePosition()
    {
        using var world = World.Create();
        Entity viewer = world.Create();
        Entity identityOnly = world.Create();
        Entity lastKnown = world.Create();
        Entity live = world.Create();
        var store = new KnowledgeProjectionStore();
        store.Upsert(viewer, identityOnly, CreateRecord(KnowledgePresence.Known, KnowledgePositionAccess.None, viewer));
        store.Upsert(viewer, lastKnown, CreateRecord(KnowledgePresence.Known, KnowledgePositionAccess.LastKnown, viewer));
        store.Upsert(viewer, live, CreateRecord(KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live, viewer));
        var globals = new Dictionary<string, object>
        {
            [CoreServiceKeys.KnowledgeProjectionResolver.Name] = new KnowledgeProjectionResolver(store),
        };

        Assert.That(CommandSourceEligibility.CanTargetCommand(world, globals, viewer, identityOnly, KnowledgePositionAccess.None), Is.True);
        Assert.That(CommandSourceEligibility.CanTargetCommand(world, globals, viewer, identityOnly, KnowledgePositionAccess.LastKnown), Is.False);
        Assert.That(CommandSourceEligibility.CanTargetCommand(world, globals, viewer, lastKnown, KnowledgePositionAccess.LastKnown), Is.True);
        Assert.That(CommandSourceEligibility.CanTargetCommand(world, globals, viewer, lastKnown, KnowledgePositionAccess.Live), Is.False);
        Assert.That(CommandSourceEligibility.CanTargetCommand(world, globals, viewer, live, KnowledgePositionAccess.Live), Is.True);
    }

    [Test]
    public void ExplicitCommandSourceViewer_IsNotOverriddenByDiagnosticsViewer()
    {
        using var world = World.Create();
        Entity localViewer = world.Create();
        Entity diagnosticsViewer = world.Create();
        Entity live = world.Create(new CommandSourceSelectableTag());
        var globals = new Dictionary<string, object>
        {
        };
        ClientLocalSeatTestBindings.BindSoleSeat(globals, diagnosticsViewer, 1, "seat.0");
        var store = new KnowledgeProjectionStore();
        store.Upsert(localViewer, live, CreateRecord(KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live, localViewer));
        globals[CoreServiceKeys.KnowledgeProjectionResolver.Name] = new KnowledgeProjectionResolver(store);

        Assert.That(CommandSourceEligibility.CanInspectLive(world, globals, localViewer, live), Is.True);
    }

    [Test]
    public void Issue197_TabTargetCycleFiltersUnknownAndLastKnownCandidatesThroughProjection()
    {
        using var world = World.Create();
        var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
        Entity local = world.Create(
            new Team { Id = 1 },
            WorldPositionCm.FromCm(0, 0));
        Entity unknown = world.Create(
            new Team { Id = 2 },
            WorldPositionCm.FromCm(500, 0),
            new CommandSourceSelectableTag());
        Entity lastKnown = world.Create(
            new Team { Id = 2 },
            WorldPositionCm.FromCm(1000, 0),
            new CommandSourceSelectableTag());
        Entity live = world.Create(
            new Team { Id = 2 },
            WorldPositionCm.FromCm(1500, 0),
            new CommandSourceSelectableTag());
        var globals = new Dictionary<string, object>
        {
            [CoreServiceKeys.AuthoritativeInput.Name] = input,
        };
        ClientLocalSeatTestBindings.BindSoleSeat(globals, local, 1, "seat.0");
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

    private static Dictionary<string, object> CreateCommandSourceGlobals(
        World world,
        PlayerInputHandler input,
        Entity local,
        string relationFilter)
    {
        var collectionKeys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
        var commandSourceConfig = new CommandSourceAcquisitionConfig
        {
            TargetFilter = new CommandSourceTargetFilterConfig { RelationFilter = relationFilter },
            Acquisition = new CommandSourceAcquisitionCollectionConfig
            {
                CollectionKey = EntityCollectionKeys.UiCommandAcquisition,
                Title = "Command acquisition",
            },
        };
        var globals = new Dictionary<string, object>
        {
            [CoreServiceKeys.AuthoritativeInput.Name] = input,
            [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
            [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
            [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings { ConfirmActionId = "Select" },
            [CoreServiceKeys.CommandSourceAcquisitionConfig.Name] = commandSourceConfig,
            [CoreServiceKeys.EntityCollectionStore.Name] = new EntityCollectionStore(collectionKeys),
            [CoreServiceKeys.EntityCollectionKeyRegistry.Name] = collectionKeys,
        };
        ClientLocalSeatTestBindings.BindSoleSeat(globals, local, 1, "seat.0");
        return globals;
    }

    private static Entity CreateSelectable(World world, int xCm, int zCm)
    {
        return world.Create(
            WorldPositionCm.FromCm(xCm, zCm),
            new VisualTransform { Position = new Vector3(xCm / 100f, 0f, zCm / 100f) },
            new CullState { IsVisible = true },
            new CommandSourceSelectableTag());
    }

    private static CommandSourceDomainHarness InstallCommandSourceDomainServices(
        World world,
        Dictionary<string, object> globals)
    {
        var types = new RelationshipTypeRegistry();
        int ownsTypeId = types.Register("Owns");
        int controlsTypeId = types.Register("Controls");
        int memberOfTypeId = types.Register("MemberOf");
        int hostileTypeId = types.Register("Hostile", isSymmetric: true);
        types.Register("Friendly", isSymmetric: true);
        types.Register("Neutral", isSymmetric: true);
        var relationships = new RelationshipRuntime(
            world,
            types,
            new RelationshipMetricRegistry(),
            new RelationshipFlagRegistry(),
            new RelationshipBandRegistry(),
            new RelationshipChangeBuffer(capacity: 8),
            new RelationshipReverseIndex(world));
        var ownership = new OwnershipResolver(relationships, ownsTypeId);
        var controlDomains = new ControlDomainQuery(world, relationships, ownership, ownsTypeId, controlsTypeId);
        var stances = DomainStanceQuery.Create(relationships, memberOfTypeId, new DomainStanceConfig
        {
            StanceTypes = new List<string> { "Hostile", "Friendly", "Neutral" },
            SameDomainStance = "Friendly",
            SameTeamStance = "Friendly",
            DefaultStance = "Neutral",
        });
        globals[CoreServiceKeys.ControlDomainQuery.Name] = controlDomains;
        globals[CoreServiceKeys.DomainStanceQuery.Name] = stances;
        return new CommandSourceDomainHarness(relationships, ownsTypeId, hostileTypeId);
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
            new RelationshipChangeBuffer(capacity: 4),
            new RelationshipReverseIndex(world));
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

    private readonly record struct CommandSourceDomainHarness(
        RelationshipRuntime Relationships,
        int OwnsTypeId,
        int HostileTypeId);

    private static void Click(
        CommandSourceAcquisitionSystem system,
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

    private static CommandSourceAcquisitionSystem CreateCommandSourceAcquisitionSystem(
        World world,
        Dictionary<string, object> globals,
        Entity owner)
    {
        return new CommandSourceAcquisitionSystem(
            world,
            globals,
            (out Entity resolvedOwner) =>
            {
                resolvedOwner = owner;
                return owner != Entity.Null && world.IsAlive(owner);
            });
    }

    private static void DragSelect(
        CommandSourceAcquisitionSystem system,
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

    private static void AssertCommandSource(EntityCollectionStore collections, Entity owner, params Entity[] expected)
    {
        bool hasView = collections.TryGetView(owner, EntityCollectionKeys.CommandSource, out EntityCollectionView view);
        if (expected.Length == 0)
        {
            Assert.That(!hasView || view.Count == 0, Is.True);
            return;
        }

        Assert.That(hasView, Is.True);
        Assert.That(view.Count, Is.EqualTo(expected.Length));
        Entity[] actual = new Entity[expected.Length];
        int written = collections.CopyEntities(owner, EntityCollectionKeys.CommandSource, actual);
        Assert.That(written, Is.EqualTo(expected.Length));
        Assert.That(actual, Is.EqualTo(expected));
    }

    private static void AssertCommandSourceEquivalent(EntityCollectionStore collections, Entity owner, params Entity[] expected)
    {
        Assert.That(collections.TryGetView(owner, EntityCollectionKeys.CommandSource, out EntityCollectionView view), Is.True);
        int count = view.Count;
        Entity[] actual = new Entity[count];
        int written = collections.CopyEntities(owner, EntityCollectionKeys.CommandSource, actual);
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
