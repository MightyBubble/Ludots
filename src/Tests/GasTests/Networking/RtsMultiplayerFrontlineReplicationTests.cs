using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.ParticipantVisibility;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Core.Vision;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;
using RtsMultiplayerFrontlineMod.Runtime;
using RtsMultiplayerFrontlineMod.Systems;

namespace Ludots.Tests.GAS.Networking;

[NonParallelizable]
[TestFixture]
public sealed class RtsMultiplayerFrontlineReplicationTests
{
    private const string MapId = "rts_duel_v1";
    private const string RuntimeKey = "rts.multiplayer.frontline.runtime";

    private static readonly string[] FrontlineMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "EntityCommandPanelMod",
        "RtsDemoMod",
        "RtsShowcaseMod",
        "RtsMultiplayerFrontlineMod",
    };

    [Test]
    public async Task AuthoritativeServer_InstallsCommandRuntimeWithoutUiServices()
    {
        string repoRoot = FindRepoRoot();
        using var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "EntityCommandPanelMod" }),
            Path.Combine(repoRoot, "assets"));
        engine.SetService(CoreServiceKeys.NetworkProcessRole, NetworkProcessRole.AuthoritativeServer);

        await engine.TriggerManager.FireEventAsync(GameEvents.GameStart, engine.CreateContext());

        Assert.That(engine.TriggerManager.Errors, Is.Empty);
        Assert.That(engine.GetService(CoreServiceKeys.EntityCommandPanelService), Is.Not.Null);
        Assert.That(engine.GetService(CoreServiceKeys.UiTextMeasurer), Is.Null);
    }

    [Test]
    public void Projector_UsesKnowledgeMaskWithoutLeakingHealthOrCrystals()
    {
        using World world = World.Create();
        const int healthId = 7;
        const int crystalId = 9;
        var attributes = new AttributeBuffer();
        attributes.SetCurrent(healthId, 875f);
        attributes.SetCurrent(crystalId, 65f);
        Entity core = world.Create(
            new ReplicationSchemaRef(701),
            WorldPositionCm.FromCm(1200, 3400),
            attributes,
            new Team { Id = 1 },
            new PlayerOwner { PlayerId = 1 },
            new FrontlineParticipant { SideIndex = 0 },
            new FrontlineCore());
        var spec = new FrontlineReplicationSpec(
            FrontlineReplicationKind.Core,
            SchemaId: 701,
            HasHealth: true,
            HasCrystals: true,
            HasOwner: true);
        var projector = new FrontlineCoreReplicationProjector(in spec, healthId, crystalId);

        KnowledgeIdMask256 healthOnly = KnowledgeIdMask256.Empty.WithId(healthId);
        var disclosure = new KnowledgeDisclosureRecord(
            KnowledgePresence.LiveVisible,
            KnowledgePositionAccess.Live,
            healthOnly,
            KnowledgeIdMask256.Empty,
            KnowledgeIdMask256.Empty,
            core,
            observedTick: 10,
            expiryTick: 0,
            confidencePermille: 1000,
            revision: 2);

        Assert.That(projector.TryProject(world, core, in disclosure, out ReplicationProjectedState projected), Is.True);
        Assert.That(
            FrontlineReplicationPayload.Has(projected.Values.Value3, FrontlineReplicationPayload.HealthValid),
            Is.True);
        Assert.That(
            FrontlineReplicationPayload.Has(projected.Values.Value3, FrontlineReplicationPayload.CrystalsValid),
            Is.False);
        Assert.That(FrontlineReplicationPayload.UnpackLowFloat(projected.Values.Value1), Is.EqualTo(875f));
        Assert.That(FrontlineReplicationPayload.UnpackHighInt(projected.Values.Value1), Is.Zero,
            "An undisclosed crystal value must be zero on the wire, not merely marked invalid.");

        var noAttributes = new KnowledgeDisclosureRecord(
            KnowledgePresence.LiveVisible,
            KnowledgePositionAccess.Live,
            KnowledgeIdMask256.Empty,
            KnowledgeIdMask256.Empty,
            KnowledgeIdMask256.Empty,
            core,
            observedTick: 11,
            expiryTick: 0,
            confidencePermille: 1000,
            revision: 3);
        Assert.That(projector.TryProject(world, core, in noAttributes, out projected), Is.True);
        Assert.That(projected.Values.Value1, Is.Zero,
            "Health and crystals must both be absent when Knowledge discloses neither attribute.");
    }

    [Test]
    public void ClientApplier_CreatesFormalSouthernMirrorAndAuthorsSouthernVisionScope()
    {
        using GameEngine engine = CreateStartedEngine();
        FrontlineConfig config = GetRuntime(engine).Config;
        int healthId = RequireAttribute(config.HealthAttribute);
        int crystalId = RequireAttribute(config.CrystalAttribute);
        FrontlineReplicationSpec[] specs = FrontlineReplication.CreateSpecs(config.Replication);
        var templates = new FrontlineClientTemplateFactory(
            engine.World,
            engine.MapLoader.TemplateRegistry.GetAll(),
            specs,
            config.Replication.MatchStateSchemaId);
        FrontlineReplicationSpec coreSpec = specs[(int)FrontlineReplicationKind.Core];
        var applier = new FrontlineCoreReplicationApplier(
            in coreSpec,
            templates,
            config.Sides,
            healthId,
            crystalId);
        var values = new ReplicationStateVector(
            FrontlineReplicationPayload.PackInts(23000, 15000),
            FrontlineReplicationPayload.PackFloats(800f, 55f),
            FrontlineReplicationPayload.PackInts(config.Sides[1].TeamId, config.Sides[1].PlayerId),
            coreSpec.SupportedValidBits);
        var identity = new ReplicationMirrorIdentity(new NetworkEntityHandle(3, 1));
        var state = new ReplicationMirrorState(coreSpec.SchemaId, revision: 7, in values);

        Entity mirror = applier.Create(engine.World, in identity, in state);

        Assert.That(engine.World.Has<FrontlineCore>(mirror), Is.True);
        Assert.That(engine.World.Has<ReplicationSchemaRef>(mirror), Is.True);
        Assert.That(engine.World.Has<ReplicationMirrorIdentity>(mirror), Is.True);
        Assert.That(engine.World.Get<Team>(mirror).Id, Is.EqualTo(config.Sides[1].TeamId));
        Assert.That(engine.World.Get<PlayerOwner>(mirror).PlayerId, Is.EqualTo(config.Sides[1].PlayerId));
        Assert.That(engine.World.Get<FrontlineParticipant>(mirror).SideIndex, Is.EqualTo(1));
        Assert.That(engine.World.Get<VisionEmitterCm>(mirror).ScopeKeyId, Is.EqualTo(config.Sides[1].VisionScopeKeyId));
        Assert.That(engine.World.Get<AttributeBuffer>(mirror).GetCurrent(healthId), Is.EqualTo(800f));
        Assert.That(engine.World.Get<AttributeBuffer>(mirror).GetCurrent(crystalId), Is.EqualTo(55f));
    }

    [Test]
    public void NetworkEntityBinding_AllocatesThenReleasesPendingEntityAtCleanupBoundary()
    {
        using World world = World.Create();
        Entity retained = world.Create(new ReplicationSchemaRef(701));
        Entity removed = world.Create(new ReplicationSchemaRef(702));
        var table = new NetworkEntityTable(capacity: 2);
        using var system = new FrontlineNetworkEntityBindingSystem(world, table);

        system.Update(0f);
        Assert.That(table.Count, Is.EqualTo(2));
        Assert.That(table.TryResolve(retained, out _), Is.True);
        Assert.That(table.TryResolve(removed, out NetworkEntityHandle removedHandle), Is.True);

        world.Add(removed, new PresentationDestroyPending());
        system.Update(0f);

        Assert.That(world.IsAlive(removed), Is.False);
        Assert.That(table.Count, Is.EqualTo(1));
        Assert.That(table.TryResolve(removedHandle, out _), Is.False);
        Assert.That(table.TryResolve(retained, out _), Is.True);
    }

    [Test]
    public void NetworkEntityBinding_ThrowsWhenCapacityCannotRepresentTheAuthoritativeWorld()
    {
        using World world = World.Create();
        world.Create(new ReplicationSchemaRef(701));
        world.Create(new ReplicationSchemaRef(702));
        var table = new NetworkEntityTable(capacity: 1);
        using var system = new FrontlineNetworkEntityBindingSystem(world, table);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;
        Assert.That(exception.Message, Does.Contain("capacity 1"));
    }

    [Test]
    public void ReplicatedClientMapLoaded_RemovesOnlyAuthoredGameplayAndKeepsAllRepresentativesAlive()
    {
        using GameEngine engine = CreateStartedEngine();
        engine.SetService(CoreServiceKeys.NetworkProcessRole, NetworkProcessRole.ReplicatedClient);

        engine.LoadMap(MapId);

        Assert.That(engine.TriggerManager.Errors, Is.Empty);
        QueryDescription authoredGameplay = new QueryDescription()
            .WithAll<ReplicationSchemaRef>()
            .WithNone<ReplicationMirrorIdentity>();
        Assert.That(engine.World.CountEntities(in authoredGameplay), Is.Zero);

        FrontlineConfig config = GetRuntime(engine).Config;
        FrontlineReplicatedClientMapBoundary.ValidateRepresentatives(
            engine.World,
            engine.CurrentMapSession!,
            config.Sides);
        Assert.That(engine.CurrentMapSession!.PlayerEntityLookup.Count, Is.EqualTo(2));
        Assert.That(engine.CurrentMapSession.TeamEntityLookup.Count, Is.EqualTo(2));

        for (int i = 0; i < config.Sides.Length; i++)
        {
            Assert.That(
                engine.CurrentMapSession.PlayerEntityLookup.TryGet(config.Sides[i].PlayerId, out Entity player),
                Is.True);
            Assert.That(engine.World.IsAlive(player), Is.True);
            Assert.That(engine.World.Has<ReplicationSchemaRef>(player), Is.False);
            Assert.That(
                engine.CurrentMapSession.TeamEntityLookup.TryGet(config.Sides[i].TeamId, out Entity team),
                Is.True);
            Assert.That(engine.World.IsAlive(team), Is.True);
            Assert.That(engine.World.Has<ReplicationSchemaRef>(team), Is.False);
        }
    }

    [Test]
    public void VisionScopeAuthoring_RejectsMismatchedSideInsteadOfSilentlyUsingAnotherScope()
    {
        using World world = World.Create();
        FrontlineSideConfig[] sides =
        {
            new() { Id = "north", PlayerId = 11, TeamId = 21, VisionScopeKeyId = 31 },
            new() { Id = "south", PlayerId = 12, TeamId = 22, VisionScopeKeyId = 32 },
        };
        Entity southern = world.Create(
            new FrontlineParticipant { SideIndex = 1 },
            new Team { Id = 22 },
            new PlayerOwner { PlayerId = 12 },
            new VisionEmitterCm { ScopeKeyId = 31 });
        using var system = new FrontlineVisionScopeAuthoringSystem(world, sides);

        system.Update(0f);
        Assert.That(world.Get<VisionEmitterCm>(southern).ScopeKeyId, Is.EqualTo(32));

        var wrongOwner = new PlayerOwner { PlayerId = 11 };
        world.Set(southern, in wrongOwner);
        Assert.Throws<InvalidOperationException>(() => system.Update(0f));
    }

    [Test]
    public void MatchStatePayload_AcceptsDisconnectExtensionBoundaryAndRejectsTheNextTick()
    {
        using GameEngine engine = CreateStartedEngine();
        FrontlineConfig config = GetRuntime(engine).Config;
        int maxCommittedTick = checked(config.MatchDurationTicks + config.DisconnectGraceTicks);
        var boundary = new FrontlineMatchSnapshot(
            maxCommittedTick,
            FrontlineMatchPhase.InProgress,
            CountdownRemainingTicks: 0,
            FrontlineMatchOutcome.InProgress,
            WinningSideIndex: -1,
            SideOneReady: false,
            SideTwoReady: true,
            SideOneConnected: false,
            SideTwoConnected: true);
        ReplicationStateVector values = FrontlineMatchStatePayload.Encode(in boundary);

        Assert.That(
            FrontlineMatchStatePayload.TryDecode(
                in values,
                config.ReadyCountdownTicks,
                maxCommittedTick,
                out FrontlineMatchStateProjection projection),
            Is.True);
        Assert.That(projection.CommittedTick, Is.EqualTo(maxCommittedTick));

        var beyondBoundary = boundary with { CommittedTick = checked(maxCommittedTick + 1) };
        values = FrontlineMatchStatePayload.Encode(in beyondBoundary);
        Assert.That(
            FrontlineMatchStatePayload.TryDecode(
                in values,
                config.ReadyCountdownTicks,
                maxCommittedTick,
                out _),
            Is.False);
    }

    [Test]
    public void AuthoritativeVisibility_DisclosesPublicStateAndResourcesButOnlyOwnAttributes()
    {
        using GameEngine engine = CreateStartedEngine();
        engine.LoadMap(MapId);
        Assert.That(engine.TriggerManager.Errors, Is.Empty);
        FrontlineConfig config = GetRuntime(engine).Config;
        DynamicParticipantVisibilityPublisher publisher = FrontlineAuthoritativeVisibility.Install(engine, config);
        KnowledgeProjectionStore knowledge = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
            ?? throw new InvalidOperationException("KnowledgeProjectionStore is unavailable.");
        int healthId = RequireAttribute(config.HealthAttribute);
        int crystalId = RequireAttribute(config.CrystalAttribute);
        Entity[] viewers = config.Sides
            .Select(side => engine.CurrentMapSession!.PlayerEntityLookup.Get(side.PlayerId))
            .ToArray();

        publisher.Publish(currentTick: 2);
        QueryDescription publicQuery = new QueryDescription()
            .WithAll<ReplicationSchemaRef>()
            .WithAny<FrontlineCrystalNode, FrontlineMatchStateEntity>();
        int publicCount = 0;
        engine.World.Query(in publicQuery, entity =>
        {
            publicCount++;
            for (int i = 0; i < viewers.Length; i++)
            {
                Assert.That(knowledge.TryGet(viewers[i], entity, 2, out KnowledgeDisclosureRecord record), Is.True);
                Assert.That(record.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
                Assert.That(record.AttributeMask.IsEmpty, Is.True);
            }
        });
        Assert.That(publicCount, Is.GreaterThanOrEqualTo(3));

        QueryDescription ownedQuery = new QueryDescription()
            .WithAll<ReplicationSchemaRef, PlayerOwner>();
        engine.World.Query(in ownedQuery, (Entity entity, ref PlayerOwner owner) =>
        {
            int ownerPlayerId = owner.PlayerId;
            int ownerSide = Array.FindIndex(config.Sides, side => side.PlayerId == ownerPlayerId);
            Assert.That(ownerSide, Is.GreaterThanOrEqualTo(0));
            Assert.That(knowledge.TryGet(viewers[ownerSide], entity, 2, out KnowledgeDisclosureRecord ownRecord), Is.True);
            Assert.That(ownRecord.AttributeMask.ContainsId(healthId), Is.True);
            Assert.That(ownRecord.AttributeMask.ContainsId(crystalId), Is.True);

            int enemySide = ownerSide == 0 ? 1 : 0;
            if (knowledge.TryGet(viewers[enemySide], entity, 2, out KnowledgeDisclosureRecord enemyRecord))
            {
                Assert.That(enemyRecord.AttributeMask.ContainsId(healthId), Is.False);
                Assert.That(enemyRecord.AttributeMask.ContainsId(crystalId), Is.False);
            }
        });
    }

    [Test]
    public void ReplicatedClientPresentation_UsesMatchMirrorForSnapshotAndVictoryHud()
    {
        using GameEngine engine = CreateStartedEngine();
        engine.SetService(CoreServiceKeys.NetworkProcessRole, NetworkProcessRole.ReplicatedClient);
        engine.LoadMap(MapId);
        Assert.That(engine.TriggerManager.Errors, Is.Empty);
        FrontlineRuntime runtime = GetRuntime(engine);
        FrontlineConfig config = runtime.Config;
        FrontlineReplicationSpec[] specs = FrontlineReplication.CreateSpecs(config.Replication);
        var templates = new FrontlineClientTemplateFactory(
            engine.World,
            engine.MapLoader.TemplateRegistry.GetAll(),
            specs,
            config.Replication.MatchStateSchemaId);
        var applier = new FrontlineMatchStateReplicationApplier(
            config.Replication.MatchStateSchemaId,
            config.ReadyCountdownTicks,
            config.MatchDurationTicks,
            config.DisconnectGraceTicks,
            templates);
        var authoritative = new FrontlineMatchSnapshot(
            CommittedTick: 345,
            FrontlineMatchPhase.Completed,
            CountdownRemainingTicks: 0,
            FrontlineMatchOutcome.SideTwoVictory,
            WinningSideIndex: 1,
            SideOneReady: true,
            SideTwoReady: true,
            SideOneConnected: true,
            SideTwoConnected: true);
        ReplicationStateVector values = FrontlineMatchStatePayload.Encode(in authoritative);
        var identity = new ReplicationMirrorIdentity(new NetworkEntityHandle(9, 1));
        var state = new ReplicationMirrorState(config.Replication.MatchStateSchemaId, revision: 3, in values);
        applier.Create(engine.World, in identity, in state);

        var presentation = new FrontlinePresentationSystem(engine, runtime);
        FrontlineMatchSnapshot snapshot = presentation.ResolvePresentationSnapshot();
        Assert.That(snapshot, Is.EqualTo(authoritative));

        ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
            ?? throw new InvalidOperationException("ScreenOverlayBuffer is unavailable.");
        overlay.Clear();
        presentation.Update(0f);
        Assert.That(ReadOverlayStrings(overlay), Does.Contain(config.Hud.SideTwoVictoryText));
    }

    [Test]
    public void ReplicatedClientPresentation_RejectsMissingAndDuplicateMatchMirrors()
    {
        using GameEngine engine = CreateStartedEngine();
        engine.SetService(CoreServiceKeys.NetworkProcessRole, NetworkProcessRole.ReplicatedClient);
        FrontlineRuntime runtime = GetRuntime(engine);
        FrontlineConfig config = runtime.Config;
        var presentation = new FrontlinePresentationSystem(engine, runtime);

        Assert.That(
            Assert.Throws<InvalidOperationException>(() => presentation.ResolvePresentationSnapshot())!.Message,
            Does.Contain("found 0"));

        var projection = new FrontlineMatchStateProjection { WinningSideIndex = -1 };
        engine.World.Create(
            new FrontlineMatchStateEntity(),
            projection,
            new ReplicationSchemaRef(config.Replication.MatchStateSchemaId),
            new ReplicationMirrorIdentity(new NetworkEntityHandle(1, 1)));
        engine.World.Create(
            new FrontlineMatchStateEntity(),
            projection,
            new ReplicationSchemaRef(config.Replication.MatchStateSchemaId),
            new ReplicationMirrorIdentity(new NetworkEntityHandle(2, 1)));

        Assert.That(
            Assert.Throws<InvalidOperationException>(() => presentation.ResolvePresentationSnapshot())!.Message,
            Does.Contain("found 2"));
    }

    private static string[] ReadOverlayStrings(ScreenOverlayBuffer overlay)
    {
        return overlay.GetSpan()
            .ToArray()
            .Where(item => item.Kind == ScreenOverlayItemKind.Text)
            .Select(item => overlay.GetString(item.StringId) ?? string.Empty)
            .ToArray();
    }

    private static GameEngine CreateStartedEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, FrontlineMods),
            Path.Combine(repoRoot, "assets"));
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        engine.SetService(CoreServiceKeys.InputHandler, new PlayerInputHandler(new NullInputBackend(), inputConfig));
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        var uiRoot = new UIRoot(new SkiaUiRenderer());
        uiRoot.Resize(1920f, 1080f);
        engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
        engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)new SkiaTextMeasurer());
        engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)new SkiaImageSizeProvider());
        engine.Start();
        return engine;
    }

    private static FrontlineRuntime GetRuntime(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(RuntimeKey, out object? value) && value is FrontlineRuntime runtime
            ? runtime
            : throw new InvalidOperationException("RTS Frontline runtime is unavailable.");
    }

    private static int RequireAttribute(string name)
    {
        int id = AttributeRegistry.GetId(name);
        return id != AttributeRegistry.InvalidId
            ? id
            : throw new InvalidOperationException($"Attribute '{name}' is unavailable.");
    }

    private static string FindRepoRoot()
    {
        string? directory = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "src", "Core", "Ludots.Core.csproj")))
            {
                return directory;
            }
            directory = Path.GetDirectoryName(directory);
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
}
