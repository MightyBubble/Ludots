using System.Numerics;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.ParticipantVisibility;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
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

    private static readonly string[] FrontlineNetworkedMods = FrontlineMods
        .Append("RtsMultiplayerFrontlineNetworkedMod")
        .ToArray();

    private static readonly JsonSerializerOptions TemplateJsonOptions = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = false,
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
    [Description(
        "Feature: Dedicated multiplayer host stays presentation-free\n" +
        "  Given the authoritative server has loaded the Frontline battlefield without a local viewport\n" +
        "  When the first authoritative simulation frame advances\n" +
        "  Then the server keeps running without activating or sampling a player camera")]
    public void AuthoritativeServer_TicksFrontlineMapWithoutLocalCameraServices()
    {
        using GameEngine engine = CreateStartedEngine(
            NetworkProcessRole.AuthoritativeServer,
            new TestServerRuntimePort(),
            installPresentationServices: false);

        Assert.DoesNotThrow(engine.LoadStartupMap);
        Assert.That(engine.GlobalContext.ContainsKey(CoreServiceKeys.VirtualCameraRequest.Name), Is.False);
        Assert.That(engine.GlobalContext.ContainsKey(CoreServiceKeys.CameraPoseRequest.Name), Is.False);
        Assert.DoesNotThrow(() =>
        {
            for (int frame = 0; frame < 120; frame++)
            {
                engine.Tick(1f / 60f);
            }
        });
        Assert.Multiple(() =>
        {
            Assert.That(engine.TriggerManager.Errors, Is.Empty);
            Assert.That(engine.CurrentMapSession?.MapId.Value, Is.EqualTo(MapId));
            Assert.That(engine.GetService(CoreServiceKeys.ViewController), Is.Null);
            Assert.That(engine.GetService(CoreServiceKeys.LocalPlayerId), Is.Zero);
            Assert.That(engine.TryGetService(CoreServiceKeys.LocalPlayerEntity, out Entity _), Is.False);
            Assert.That(engine.GameSession.Camera.IsRuntimeConfigured, Is.False);
            Assert.That(engine.GlobalContext.ContainsKey(CoreServiceKeys.VirtualCameraRequest.Name), Is.False);
            Assert.That(engine.GlobalContext.ContainsKey(CoreServiceKeys.CameraPoseRequest.Name), Is.False);
            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.HasActiveCamera, Is.False);
        });
    }

    [Test]
    [Description(
        "Feature: Each replicated commander owns a local battlefield camera\n" +
        "  Given a replicated client has loaded the Frontline battlefield and its viewport\n" +
        "  When local input requests the Frontline command camera\n" +
        "  Then the client applies that camera before presenting the next frame")]
    public void ReplicatedClient_ConsumesLocalCameraRequestBeforePresentation()
    {
        using GameEngine engine = CreateStartedReplicatedClientEngine();
        engine.LoadStartupMap();
        Assert.That(engine.GetService(CoreServiceKeys.LocalPlayerId), Is.Zero);
        Assert.That(engine.TryGetService(CoreServiceKeys.LocalPlayerEntity, out Entity _), Is.False);
        engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
        {
            Id = "Rts.Frontline",
            BlendDurationSeconds = 0f,
            ReplaceActiveStack = true,
            ResetRuntimeState = true,
        });

        Assert.DoesNotThrow(() => engine.Tick(1f / 60f));
        Assert.Multiple(() =>
        {
            Assert.That(engine.GlobalContext.ContainsKey(CoreServiceKeys.VirtualCameraRequest.Name), Is.False);
            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId, Is.EqualTo("Rts.Frontline"));
        });
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

        var hidden = new KnowledgeDisclosureRecord(
            KnowledgePresence.HiddenWithSource,
            KnowledgePositionAccess.None,
            KnowledgeIdMask256.Empty,
            KnowledgeIdMask256.Empty,
            KnowledgeIdMask256.Empty,
            core,
            observedTick: 12,
            expiryTick: 0,
            confidencePermille: 1000,
            revision: 4);
        Assert.That(projector.TryProject(world, core, in hidden, out _), Is.False,
            "A hidden enemy must not emit position or attribute replication state.");
    }

    [Test]
    public void GameStart_ConfiguresFogKnowledgeToDiscloseVisibleEnemyHealthOnly()
    {
        using GameEngine engine = CreateStartedEngine();
        FrontlineConfig config = GetRuntime(engine).Config;
        int healthId = RequireAttribute(config.HealthAttribute);
        int crystalId = RequireAttribute(config.CrystalAttribute);
        FogKnowledgeProjector projector = engine.GetService(CoreServiceKeys.FogKnowledgeProjector)
            ?? throw new InvalidOperationException("FogKnowledgeProjector is unavailable.");

        Assert.That(projector.IsProjectionPolicyConfigured, Is.True);
        Assert.That(projector.ProjectionPolicy.Disclosure.AttributeMask.ContainsId(healthId), Is.True);
        Assert.That(projector.ProjectionPolicy.Disclosure.AttributeMask.ContainsId(crystalId), Is.False);
    }

    [Test]
    public void ClientApplier_CreatesFormalSouthernMirrorAndAuthorsSouthernVisionScope()
    {
        using GameEngine engine = CreateStartedEngine();
        engine.LoadMap(MapId);
        FrontlineRuntime runtime = GetRuntime(engine);
        FrontlineConfig config = runtime.Config;
        int healthId = RequireAttribute(config.HealthAttribute);
        int crystalId = RequireAttribute(config.CrystalAttribute);
        FrontlineReplicationSpec[] specs = FrontlineReplication.CreateSpecs(config.Replication);
        var templates = new FrontlineClientTemplateFactory(
            engine.World,
            engine.MapLoader.TemplateRegistry.GetAll(),
            specs,
            config.Replication.MatchStateSchemaId,
            engine.MapLoader.EntityTemplateKeys,
            RequireStableIds(engine));
        FrontlineReplicationSpec coreSpec = specs[(int)FrontlineReplicationKind.Core];
        var applier = new FrontlineCoreReplicationApplier(
            in coreSpec,
            templates,
            config.Sides,
            healthId,
            crystalId,
            runtime.TagBinder,
            RequireOwnership(engine),
            RequirePlayers(engine));
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
        Assert.That(
            engine.World.Get<EntityTemplateKeyRef>(mirror).TemplateKeyId,
            Is.EqualTo(engine.MapLoader.EntityTemplateKeys.GetId("rts_frontline_core")));
        Assert.That(engine.World.Get<Team>(mirror).Id, Is.EqualTo(config.Sides[1].TeamId));
        Assert.That(engine.World.Get<PlayerOwner>(mirror).PlayerId, Is.EqualTo(config.Sides[1].PlayerId));
        Assert.That(engine.World.Get<FrontlineParticipant>(mirror).SideIndex, Is.EqualTo(1));
        Assert.That(engine.World.Get<VisionEmitterCm>(mirror).ScopeKeyId, Is.EqualTo(config.Sides[1].VisionScopeKeyId));
        Assert.That(engine.World.Get<AttributeBuffer>(mirror).GetCurrent(healthId), Is.EqualTo(800f));
        Assert.That(engine.World.Get<AttributeBuffer>(mirror).GetCurrent(crystalId), Is.EqualTo(55f));
        Assert.That(engine.World.Has<AbilityStateBuffer>(mirror), Is.True);
        ref AbilityStateBuffer abilities = ref engine.World.Get<AbilityStateBuffer>(mirror);
        Assert.That(abilities.Count, Is.EqualTo(1));
        Assert.That(
            abilities.Get(0).AbilityId,
            Is.EqualTo(AbilityIdRegistry.GetId("Ability.Rts.Frontline.TrainInfantry")));
        Assert.That(engine.World.Get<FrontlineTagBindingState>(mirror).IsBound, Is.EqualTo(1));
        Assert.That(engine.World.Get<PreviousWorldPositionCm>(mirror).Value,
            Is.EqualTo(engine.World.Get<WorldPositionCm>(mirror).Value));
        VisualTransform visual = engine.World.Get<VisualTransform>(mirror);
        Assert.That(visual.Position, Is.EqualTo(new Vector3(230f, 0f, 150f)));
        Assert.That(visual.Rotation, Is.EqualTo(Quaternion.Identity));
        Assert.That(visual.Scale, Is.EqualTo(RequireTemplateVisualScale(engine, "rts_frontline_core")));

        int stableId = engine.World.Get<PresentationStableId>(mirror).Value;
        var updatedValues = new ReplicationStateVector(
            FrontlineReplicationPayload.PackInts(24000, 15500),
            FrontlineReplicationPayload.PackFloats(760f, 60f),
            values.Value2,
            coreSpec.SupportedValidBits);
        var update = new ReplicatedEntityState(identity.Handle, coreSpec.SchemaId, revision: 8, in updatedValues);
        applier.Apply(engine.World, mirror, in update);

        Assert.That(engine.World.Get<PreviousWorldPositionCm>(mirror).Value,
            Is.EqualTo(WorldPositionCm.FromCm(23000, 15000).Value));
        Assert.That(engine.World.Get<WorldPositionCm>(mirror).Value,
            Is.EqualTo(WorldPositionCm.FromCm(24000, 15500).Value));
        Assert.That(engine.World.Get<VisualTransform>(mirror).Position, Is.EqualTo(new Vector3(240f, 0f, 155f)));
        Assert.That(engine.World.Get<PresentationStableId>(mirror).Value, Is.EqualTo(stableId));
        Assert.That(engine.World.Get<AbilityStateBuffer>(mirror).Get(0).AbilityId,
            Is.EqualTo(AbilityIdRegistry.GetId("Ability.Rts.Frontline.TrainInfantry")));
        Assert.That(engine.World.Get<FrontlineTagBindingState>(mirror).IsBound, Is.EqualTo(1));

        applier.Conceal(engine.World, mirror);
        Assert.That(engine.World.Get<CullState>(mirror).IsVisible, Is.False);
        Assert.That(engine.World.Get<CommandSourceSelectableState>(mirror).Enabled, Is.False);

        applier.Apply(engine.World, mirror, in update);
        Assert.That(engine.World.Get<CullState>(mirror).IsVisible, Is.True);
        Assert.That(engine.World.Get<CommandSourceSelectableState>(mirror).Enabled, Is.True);
        Assert.That(engine.World.Get<AbilityStateBuffer>(mirror).Get(0).AbilityId,
            Is.EqualTo(AbilityIdRegistry.GetId("Ability.Rts.Frontline.TrainInfantry")));
    }

    [Test]
    [Description(
        "Feature: Replicated battlefield units remain visible\n" +
        "  Given a client receives command core, harvester, infantry, and crystal mirrors\n" +
        "  When the client presents the next battlefield frame\n" +
        "  Then every mirror keeps its formal template identity, stands on the terrain, and owns its role-specific shape")]
    public void GivenFourReplicatedRoles_WhenClientPresentsFrame_ThenEachMirrorIsGroundedAndReadable()
    {
        using GameEngine engine = CreateStartedEngine();
        engine.LoadMap(MapId);
        FrontlineRuntime runtime = GetRuntime(engine);
        FrontlineConfig config = runtime.Config;
        FrontlineReplicationSpec[] specs = FrontlineReplication.CreateSpecs(config.Replication);
        var templates = new FrontlineClientTemplateFactory(
            engine.World,
            engine.MapLoader.TemplateRegistry.GetAll(),
            specs,
            config.Replication.MatchStateSchemaId,
            engine.MapLoader.EntityTemplateKeys,
            RequireStableIds(engine));
        int healthId = RequireAttribute(config.HealthAttribute);
        int crystalId = RequireAttribute(config.CrystalAttribute);
        OwnershipResolver ownership = RequireOwnership(engine);
        PlayerEntityLookup players = RequirePlayers(engine);
        FrontlineReplicationApplier[] appliers =
        {
            new FrontlineCoreReplicationApplier(in specs[0], templates, config.Sides, healthId, crystalId, runtime.TagBinder, ownership, players),
            new FrontlineHarvesterReplicationApplier(in specs[1], templates, config.Sides, healthId, crystalId, runtime.TagBinder, ownership, players),
            new FrontlineInfantryReplicationApplier(in specs[2], templates, config.Sides, healthId, crystalId, runtime.TagBinder, ownership, players),
            new FrontlineCrystalNodeReplicationApplier(in specs[3], templates, config.Sides, healthId, crystalId, runtime.TagBinder, ownership, players),
        };
        string[] templateIds =
        {
            "rts_frontline_core",
            "rts_frontline_harvester",
            "rts_frontline_infantry",
            "rts_frontline_crystal_node",
        };
        string[] performerIds =
        {
            "rts.frontline.visual.core",
            "rts.frontline.visual.harvester",
            "rts.frontline.visual.infantry",
            "rts.frontline.visual.crystal",
        };
        Entity[] mirrors = new Entity[appliers.Length];
        for (int i = 0; i < appliers.Length; i++)
        {
            FrontlineReplicationSpec spec = specs[i];
            var values = new ReplicationStateVector(
                FrontlineReplicationPayload.PackInts(9000 + (i * 1200), 15000),
                FrontlineReplicationPayload.PackFloats(spec.HasHealth ? 100f : 0f, spec.HasCrystals ? 40f : 0f),
                spec.HasOwner
                    ? FrontlineReplicationPayload.PackInts(config.Sides[0].TeamId, config.Sides[0].PlayerId)
                    : 0L,
                spec.SupportedValidBits);
            var identity = new ReplicationMirrorIdentity(new NetworkEntityHandle(20 + i, 1));
            var state = new ReplicationMirrorState(spec.SchemaId, revision: 1, in values);
            mirrors[i] = appliers[i].Create(engine.World, in identity, in state);
        }

        engine.Tick(1f / 60f);
        engine.Tick(1f / 60f);

        PerformerDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
            ?? throw new InvalidOperationException("PerformerDefinitionRegistry is unavailable.");
        PerformerEntityRuntime performers = engine.GetService(CoreServiceKeys.PerformerEntityRuntime)
            ?? throw new InvalidOperationException("PerformerEntityRuntime is unavailable.");
        for (int i = 0; i < mirrors.Length; i++)
        {
            Entity mirror = mirrors[i];
            int definitionId = definitions.GetId(performerIds[i]);
            Assert.Multiple(() =>
            {
                Assert.That(
                    engine.World.Get<EntityTemplateKeyRef>(mirror).TemplateKeyId,
                    Is.EqualTo(engine.MapLoader.EntityTemplateKeys.GetId(templateIds[i])));
                Assert.That(engine.World.Get<VisualHeightmapSampleState>(mirror).Sampled, Is.EqualTo(1));
                Assert.That(engine.World.Get<VisualTransform>(mirror).Position.Y, Is.GreaterThan(0.1f));
                Assert.That(performers.GetActiveByOwnerDefinition(definitionId, mirror), Has.Count.EqualTo(1));
            });
        }
    }

    [Test]
    public void ClientHarvesterApplier_BindsRoleTagExactlyOnceAcrossRepeatedSnapshots()
    {
        using GameEngine engine = CreateStartedEngine();
        engine.LoadMap(MapId);
        FrontlineRuntime runtime = GetRuntime(engine);
        FrontlineConfig config = runtime.Config;
        FrontlineReplicationSpec[] specs = FrontlineReplication.CreateSpecs(config.Replication);
        FrontlineReplicationSpec spec = specs[(int)FrontlineReplicationKind.Harvester];
        var templates = new FrontlineClientTemplateFactory(
            engine.World,
            engine.MapLoader.TemplateRegistry.GetAll(),
            specs,
            config.Replication.MatchStateSchemaId,
            engine.MapLoader.EntityTemplateKeys,
            RequireStableIds(engine));
        var applier = new FrontlineHarvesterReplicationApplier(
            in spec,
            templates,
            config.Sides,
            RequireAttribute(config.HealthAttribute),
            RequireAttribute(config.CrystalAttribute),
            runtime.TagBinder,
            RequireOwnership(engine),
            RequirePlayers(engine));
        var values = new ReplicationStateVector(
            FrontlineReplicationPayload.PackInts(7000, 9000),
            FrontlineReplicationPayload.PackFloats(120f, 0f),
            FrontlineReplicationPayload.PackInts(config.Sides[0].TeamId, config.Sides[0].PlayerId),
            spec.SupportedValidBits);
        var handle = new NetworkEntityHandle(4, 1);
        var identity = new ReplicationMirrorIdentity(handle);
        var state = new ReplicationMirrorState(spec.SchemaId, revision: 1, in values);

        Entity mirror = applier.Create(engine.World, in identity, in state);
        int tagId = TagRegistry.GetId(config.HarvesterTag);
        Assert.That(tagId, Is.GreaterThan(0));
        Assert.That(engine.World.Get<GameplayTagContainer>(mirror).HasTag(tagId), Is.True);
        Assert.That(engine.World.Get<TagCountContainer>(mirror).GetCount(tagId), Is.EqualTo(1));

        var update = new ReplicatedEntityState(handle, spec.SchemaId, revision: 2, in values);
        applier.Apply(engine.World, mirror, in update);

        Assert.That(engine.World.Get<GameplayTagContainer>(mirror).HasTag(tagId), Is.True);
        Assert.That(engine.World.Get<TagCountContainer>(mirror).GetCount(tagId), Is.EqualTo(1));
        Assert.That(engine.World.Get<FrontlineTagBindingState>(mirror).IsBound, Is.EqualTo(1));
    }

    [Test]
    [Description(
        "Feature: A replicated harvester keeps the player's command intent\n" +
        "  Given the local player's harvester and a visible crystal node arrived in a battlefield snapshot\n" +
        "  When the player commands that harvester on the crystal node\n" +
        "  Then the client issues a gather command against that entity instead of a ground move")]
    public void GivenOwnedHarvesterAndVisibleCrystalMirrors_WhenRoutingCommand_ThenClientIssuesEntityGather()
    {
        using GameEngine engine = CreateStartedReplicatedClientEngine();
        engine.LoadMap(MapId);
        Assert.That(engine.TriggerManager.Errors, Is.Empty);

        FrontlineRuntime runtime = GetRuntime(engine);
        FrontlineConfig config = runtime.Config;
        FrontlineReplicationSpec[] specs = FrontlineReplication.CreateSpecs(config.Replication);
        var templates = new FrontlineClientTemplateFactory(
            engine.World,
            engine.MapLoader.TemplateRegistry.GetAll(),
            specs,
            config.Replication.MatchStateSchemaId,
            engine.MapLoader.EntityTemplateKeys,
            RequireStableIds(engine));
        int healthId = RequireAttribute(config.HealthAttribute);
        int crystalId = RequireAttribute(config.CrystalAttribute);
        var harvesterApplier = new FrontlineHarvesterReplicationApplier(
            in specs[(int)FrontlineReplicationKind.Harvester],
            templates,
            config.Sides,
            healthId,
            crystalId,
            runtime.TagBinder,
            RequireOwnership(engine),
            RequirePlayers(engine));
        var crystalApplier = new FrontlineCrystalNodeReplicationApplier(
            in specs[(int)FrontlineReplicationKind.CrystalNode],
            templates,
            config.Sides,
            healthId,
            crystalId,
            runtime.TagBinder,
            RequireOwnership(engine),
            RequirePlayers(engine));

        FrontlineSideConfig localSide = config.Sides[0];
        var harvesterValues = new ReplicationStateVector(
            FrontlineReplicationPayload.PackInts(7000, 9000),
            FrontlineReplicationPayload.PackFloats(120f, 0f),
            FrontlineReplicationPayload.PackInts(localSide.TeamId, localSide.PlayerId),
            specs[(int)FrontlineReplicationKind.Harvester].SupportedValidBits);
        var harvesterIdentity = new ReplicationMirrorIdentity(new NetworkEntityHandle(4, 1));
        var harvesterState = new ReplicationMirrorState(
            specs[(int)FrontlineReplicationKind.Harvester].SchemaId,
            revision: 1,
            in harvesterValues);
        Entity harvester = harvesterApplier.Create(engine.World, in harvesterIdentity, in harvesterState);

        var crystalValues = new ReplicationStateVector(
            FrontlineReplicationPayload.PackInts(11000, 9000),
            0,
            0,
            specs[(int)FrontlineReplicationKind.CrystalNode].SupportedValidBits);
        var crystalIdentity = new ReplicationMirrorIdentity(new NetworkEntityHandle(7, 1));
        var crystalState = new ReplicationMirrorState(
            specs[(int)FrontlineReplicationKind.CrystalNode].SchemaId,
            revision: 1,
            in crystalValues);
        Entity crystal = crystalApplier.Create(engine.World, in crystalIdentity, in crystalState);

        Entity localPlayer = engine.CurrentMapSession!.PlayerEntityLookup.Get(localSide.PlayerId);
        ControlDomainQuery controlDomains = engine.GetService(CoreServiceKeys.ControlDomainQuery)
            ?? throw new InvalidOperationException("ControlDomainQuery is unavailable.");
        Assert.That(controlDomains.TryResolveControlDomain(harvester, out Entity domain), Is.True);
        Assert.That(domain, Is.EqualTo(localPlayer));

        KnowledgeProjectionStore knowledge = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
            ?? throw new InvalidOperationException("KnowledgeProjectionStore is unavailable.");
        knowledge.Upsert(localPlayer, crystal, new KnowledgeDisclosureRecord(
            KnowledgePresence.LiveVisible,
            KnowledgePositionAccess.Live,
            KnowledgeIdMask256.Empty,
            KnowledgeIdMask256.Empty,
            KnowledgeIdMask256.Empty,
            crystal,
            observedTick: 0,
            expiryTick: 0,
            confidencePermille: 1000,
            revision: 1));

        CommandIntentProfileRegistry intents = engine.GetService(CoreServiceKeys.CommandIntentProfileRegistry)
            ?? throw new InvalidOperationException("CommandIntentProfileRegistry is unavailable.");
        int profileId = intents.ProfileIdRegistry.GetId("intent.command.default");
        Span<Entity> actors = stackalloc Entity[1] { harvester };
        Span<CommandIntentRoute> routes = stackalloc CommandIntentRoute[1];
        var facts = new CommandIntentTargetFacts(crystal, HasEntity: true);

        Assert.That(intents.RouteGroup(profileId, actors, localPlayer, in facts, routes), Is.EqualTo(1));
        OrderTypeRegistry orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("OrderTypeRegistry is unavailable.");
        Assert.That(routes[0].OrderTypeId, Is.EqualTo(orderTypes.GetId("frontlineGather")));
        Assert.That(routes[0].TargetShape, Is.EqualTo(CommandIntentTargetShape.Entity));
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
        using GameEngine engine = CreateStartedReplicatedClientEngine();

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
    public void AuthoritativeVision_WhenOpposingInfantryMeet_DisclosesEachEnemyToTheFormalPlayerViewer()
    {
        using GameEngine engine = CreateStartedEngine();
        engine.LoadMap(MapId);
        Assert.That(engine.TriggerManager.Errors, Is.Empty);
        FrontlineConfig config = GetRuntime(engine).Config;

        Entity north = Entity.Null;
        Entity south = Entity.Null;
        QueryDescription infantryQuery = new QueryDescription()
            .WithAll<FrontlineInfantry, FrontlineParticipant, WorldPositionCm, VisionEmitterCm, FogOccupantCm>();
        engine.World.Query(in infantryQuery, (Entity entity, ref FrontlineParticipant participant) =>
        {
            if (participant.SideIndex == 0 && north == Entity.Null)
            {
                north = entity;
            }
            else if (participant.SideIndex == 1 && south == Entity.Null)
            {
                south = entity;
            }
        });
        Assert.That(north, Is.Not.EqualTo(Entity.Null));
        Assert.That(south, Is.Not.EqualTo(Entity.Null));

        WorldPositionCm meeting = WorldPositionCm.FromCm(15_000, 15_000);
        engine.World.Set(north, in meeting);
        engine.World.Set(south, in meeting);
        for (int i = 0; i < 3; i++)
        {
            engine.Tick(1f / config.SimulationTickRateHz);
        }

        FogLayerRegistry layers = engine.GetService(CoreServiceKeys.VisionFogLayerRegistry)
            ?? throw new InvalidOperationException("FogLayerRegistry is unavailable.");
        FogFieldStore fields = engine.GetService(CoreServiceKeys.VisionFogFieldStore)
            ?? throw new InvalidOperationException("FogFieldStore is unavailable.");
        FogLayerId groundLayer = layers.GetId("ground");
        KnowledgeProjectionStore knowledge = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
            ?? throw new InvalidOperationException("KnowledgeProjectionStore is unavailable.");
        for (int sideIndex = 0; sideIndex < config.Sides.Length; sideIndex++)
        {
            Entity ownInfantry = sideIndex == 0 ? north : south;
            VisionEmitterCm emitter = engine.World.Get<VisionEmitterCm>(ownInfantry);
            Assert.That(emitter.ScopeKeyId, Is.EqualTo(config.Sides[sideIndex].VisionScopeKeyId));
            Assert.That(emitter.LayerMask, Is.EqualTo(layers.ToMask(groundLayer)));
            Assert.That(emitter.Aperture.RangeCm, Is.EqualTo(3_200));
            Assert.That(fields.TryGet(emitter.ScopeKeyId, groundLayer, out FogField field), Is.True);
            Assert.That(
                field.GetVisibility(field.WorldToCell(meeting.ToWorldCmInt2())),
                Is.EqualTo(CellVisibility.Visible),
                $"Player side {sideIndex} vision field did not reveal the shared infantry position.");

            Entity viewer = engine.CurrentMapSession!.PlayerEntityLookup.Get(config.Sides[sideIndex].PlayerId);
            Entity enemy = sideIndex == 0 ? south : north;
            Assert.That(
                knowledge.TryGet(viewer, enemy, engine.GameSession.CurrentTick, out KnowledgeDisclosureRecord record),
                Is.True,
                $"Player side {sideIndex} did not receive knowledge for an enemy infantry at the same position.");
            Assert.That(record.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
            Assert.That(record.Position, Is.EqualTo(KnowledgePositionAccess.Live));
        }
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
    public void AuthoritativeVisibility_DisclosesPublicStateAndResourcesWithoutBypassingFogForEnemies()
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
        var status = new TestClientRuntimePort();
        using GameEngine engine = CreateStartedReplicatedClientEngine(status);
        engine.LoadMap(MapId);
        Assert.That(engine.TriggerManager.Errors, Is.Empty);
        FrontlineRuntime runtime = GetRuntime(engine);
        FrontlineConfig config = runtime.Config;
        InstallClientFeedbackServices(
            engine,
            config,
            status,
            new TestClientCommandPort(),
            CreateObserver(config));
        FrontlineReplicationSpec[] specs = FrontlineReplication.CreateSpecs(config.Replication);
        var templates = new FrontlineClientTemplateFactory(
            engine.World,
            engine.MapLoader.TemplateRegistry.GetAll(),
            specs,
            config.Replication.MatchStateSchemaId,
            engine.MapLoader.EntityTemplateKeys,
            RequireStableIds(engine));
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
    [Description(
        "Feature: Player command feedback follows the authoritative result\n" +
        "  Given a player has issued one command from a replicated client\n" +
        "  When the command moves through sending, server acceptance, queueing, waiting, execution, or rejection\n" +
        "  Then the HUD shows the current player-facing stage and never calls a queued command started")]
    public void GivenReplicatedCommand_WhenAdmissionChanges_ThenHudShowsTheAuthoritativeStage()
    {
        var status = new TestClientRuntimePort
        {
            ConnectionState = ReplicatedClientConnectionState.Connected,
            HasEstablishedSession = true,
            RoundTripTimeMilliseconds = 24,
        };
        using GameEngine engine = CreateStartedReplicatedClientEngine(status);
        engine.LoadMap(MapId);
        FrontlineRuntime runtime = GetRuntime(engine);
        FrontlineConfig config = runtime.Config;
        var commands = new TestClientCommandPort
        {
            SubmissionRevision = 1,
            LastSubmittedBatchSequence = 42,
            LastSubmitResult = ReplicatedClientCommandSubmitResult.Submitted,
        };
        NetworkRuntimeStateObserver observer = CreateObserver(config);
        InstallClientFeedbackServices(engine, config, status, commands, observer);
        AddMatchMirror(engine, runtime, RunningSnapshot(sideTwoConnected: true));

        var presentation = new FrontlinePresentationSystem(engine, runtime);
        ScreenOverlayBuffer overlay = RequireOverlay(engine);
        AssertHudContains(presentation, overlay, config.Hud.CommandSendingText);

        PublishAdmission(observer, commands.LastSubmittedBatchSequence, OrderAdmissionStage.NetworkIntake, OrderSubmitResult.NetworkScheduled);
        AssertHudContains(presentation, overlay, config.Hud.CommandSendingText);

        PublishAdmission(observer, commands.LastSubmittedBatchSequence, OrderAdmissionStage.GlobalIntake, OrderSubmitResult.Queued);
        AssertHudContains(presentation, overlay, config.Hud.CommandAcceptedText);

        PublishAdmission(observer, commands.LastSubmittedBatchSequence, OrderAdmissionStage.EntityIntake, OrderSubmitResult.Queued);
        AssertHudContains(presentation, overlay, config.Hud.CommandQueuedText);
        Assert.That(ReadOverlayStrings(overlay), Does.Not.Contain(config.Hud.CommandStartedText));

        PublishAdmission(observer, commands.LastSubmittedBatchSequence, OrderAdmissionStage.EntityIntake, OrderSubmitResult.Pending);
        AssertHudContains(presentation, overlay, config.Hud.CommandPendingText);
        Assert.That(ReadOverlayStrings(overlay), Does.Not.Contain(config.Hud.CommandStartedText));

        PublishAdmission(observer, commands.LastSubmittedBatchSequence, OrderAdmissionStage.EntityIntake, OrderSubmitResult.Activated);
        AssertHudContains(presentation, overlay, config.Hud.CommandStartedText);

        PublishAdmission(observer, commands.LastSubmittedBatchSequence, OrderAdmissionStage.NetworkIntake, OrderSubmitResult.NetworkActorNotControlled);
        AssertHudContains(
            presentation,
            overlay,
            config.Hud.ResolveAdmissionRejection(OrderSubmitResult.NetworkActorNotControlled));
    }

    [Test]
    [Description(
        "Feature: Players understand the live connection state\n" +
        "  Given a replicated Frontline match is visible\n" +
        "  When the client connects, reconnects, observes an offline opponent, loses the service, or receives a measured RTT\n" +
        "  Then the HUD shows the matching player-facing state and the remaining seat time")]
    public void GivenRunningMatch_WhenConnectionChanges_ThenHudShowsObservedPlayerState()
    {
        var status = new TestClientRuntimePort
        {
            ConnectionState = ReplicatedClientConnectionState.Handshaking,
            HasEstablishedSession = false,
        };
        using GameEngine engine = CreateStartedReplicatedClientEngine(status);
        engine.LoadMap(MapId);
        FrontlineRuntime runtime = GetRuntime(engine);
        FrontlineConfig config = runtime.Config;
        var commands = new TestClientCommandPort();
        NetworkRuntimeStateObserver observer = CreateObserver(config);
        InstallClientFeedbackServices(engine, config, status, commands, observer);
        AddMatchMirror(engine, runtime, RunningSnapshot(sideTwoConnected: false));

        var presentation = new FrontlinePresentationSystem(engine, runtime);
        ScreenOverlayBuffer overlay = RequireOverlay(engine);
        AssertHudContains(presentation, overlay, config.Hud.ConnectingText);

        status.HasEstablishedSession = true;
        status.ReconnectWindowRemainingSeconds = 12.1f;
        AssertHudContains(presentation, overlay, $"{config.Hud.ReconnectingText} 13s");

        status.ConnectionState = ReplicatedClientConnectionState.Connected;
        status.RoundTripTimeMilliseconds = config.Hud.DelayedRoundTripThresholdMilliseconds - 1;
        AssertHudContains(presentation, overlay, config.Hud.SmoothConnectionText);
        AssertHudContains(presentation, overlay, config.Hud.OpponentOfflineText);

        status.RoundTripTimeMilliseconds = config.Hud.DelayedRoundTripThresholdMilliseconds;
        AssertHudContains(presentation, overlay, config.Hud.DelayedConnectionText);

        status.IsFaulted = true;
        AssertHudContains(presentation, overlay, config.Hud.ServiceInterruptedText);
    }

    [Test]
    [Description(
        "Feature: A player sees a stable transition into battle\n" +
        "  Given the room start message arrives before the first battlefield snapshot\n" +
        "  When the client draws the HUD during that normal network gap\n" +
        "  Then the player sees that the battlefield is synchronizing and the client does not fail")]
    public void GivenStartedRoomBeforeFirstBattlefieldSnapshot_WhenHudUpdates_ThenPlayerSeesSynchronization()
    {
        var status = new TestClientRuntimePort
        {
            ConnectionState = ReplicatedClientConnectionState.Connected,
            HasEstablishedSession = true,
            IsAwaitingFullSnapshot = true,
        };
        using GameEngine engine = CreateStartedReplicatedClientEngine(status);
        engine.LoadMap(MapId);
        FrontlineRuntime runtime = GetRuntime(engine);
        FrontlineConfig config = runtime.Config;
        NetworkRuntimeStateObserver observer = CreateObserver(config);
        InstallClientFeedbackServices(engine, config, status, new TestClientCommandPort(), observer);
        var room = new NetworkRoomSnapshotHeader(
            new SessionEpoch(7),
            revision: 1,
            committedTick: 300,
            countdownRemainingTicks: 0,
            seatCount: 2,
            connectedSeatCount: 2,
            readySeatCount: 2,
            NetworkRoomPhase.Started);
        NetworkRoomSeatSnapshot[] seats =
        {
            new(0, NetworkRoomSeatConnectionState.Connected, NetworkRoomReadyState.Ready, 1, new PlayerId(1)),
            new(1, NetworkRoomSeatConnectionState.Connected, NetworkRoomReadyState.Ready, 1, new PlayerId(2)),
        };
        observer.OnClientRoomSnapshot(in room, seats);

        var presentation = new FrontlinePresentationSystem(engine, runtime);
        ScreenOverlayBuffer overlay = RequireOverlay(engine);
        AssertHudContains(presentation, overlay, config.Hud.SynchronizingBattlefieldText);

        status.IsAwaitingFullSnapshot = false;
        AddMatchMirror(engine, runtime, RunningSnapshot(sideTwoConnected: true));
        AssertHudContains(presentation, overlay, config.Hud.BattleStartedText);
        Assert.That(ReadOverlayStrings(overlay), Does.Not.Contain(config.Hud.SynchronizingBattlefieldText));
    }

    [Test]
    [Description(
        "Feature: A joining player receives immediate connection feedback\n" +
        "  Given the client has not received its first room snapshot\n" +
        "  When the HUD first appears\n" +
        "  Then the player sees that the battle service is connecting")]
    public void GivenNoRoomOrBattlefieldSnapshot_WhenHudUpdates_ThenPlayerSeesConnectingState()
    {
        var status = new TestClientRuntimePort
        {
            ConnectionState = ReplicatedClientConnectionState.Handshaking,
            HasEstablishedSession = false,
        };
        using GameEngine engine = CreateStartedReplicatedClientEngine(status);
        engine.LoadMap(MapId);
        FrontlineRuntime runtime = GetRuntime(engine);
        FrontlineConfig config = runtime.Config;
        InstallClientFeedbackServices(
            engine,
            config,
            status,
            new TestClientCommandPort(),
            CreateObserver(config));

        var presentation = new FrontlinePresentationSystem(engine, runtime);
        AssertHudContains(presentation, RequireOverlay(engine), config.Hud.ConnectingText);
    }

    [Test]
    public void ReplicatedClientPresentation_RejectsMissingAndDuplicateMatchMirrors()
    {
        using GameEngine engine = CreateStartedReplicatedClientEngine();
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

    private static void InstallClientFeedbackServices(
        GameEngine engine,
        FrontlineConfig config,
        TestClientRuntimePort runtimePort,
        TestClientCommandPort commandPort,
        NetworkRuntimeStateObserver observer)
    {
        engine.SetService(CoreServiceKeys.NetworkRuntimePort, runtimePort);
        engine.SetService(CoreServiceKeys.ReplicatedClientCommandPort, commandPort);
        engine.SetService(CoreServiceKeys.NetworkRuntimeStateObserver, observer);
        engine.SetService(CoreServiceKeys.LocalPlayerId, config.Sides[0].PlayerId);
    }

    private static void AddMatchMirror(
        GameEngine engine,
        FrontlineRuntime runtime,
        in FrontlineMatchSnapshot snapshot)
    {
        FrontlineConfig config = runtime.Config;
        FrontlineReplicationSpec[] specs = FrontlineReplication.CreateSpecs(config.Replication);
        var templates = new FrontlineClientTemplateFactory(
            engine.World,
            engine.MapLoader.TemplateRegistry.GetAll(),
            specs,
            config.Replication.MatchStateSchemaId,
            engine.MapLoader.EntityTemplateKeys,
            RequireStableIds(engine));
        var applier = new FrontlineMatchStateReplicationApplier(
            config.Replication.MatchStateSchemaId,
            config.ReadyCountdownTicks,
            config.MatchDurationTicks,
            config.DisconnectGraceTicks,
            templates);
        ReplicationStateVector values = FrontlineMatchStatePayload.Encode(in snapshot);
        var identity = new ReplicationMirrorIdentity(new NetworkEntityHandle(9, 1));
        var state = new ReplicationMirrorState(config.Replication.MatchStateSchemaId, revision: 1, in values);
        applier.Create(engine.World, in identity, in state);
    }

    private static FrontlineMatchSnapshot RunningSnapshot(bool sideTwoConnected) => new(
        CommittedTick: 300,
        FrontlineMatchPhase.InProgress,
        CountdownRemainingTicks: 0,
        FrontlineMatchOutcome.InProgress,
        WinningSideIndex: -1,
        SideOneReady: true,
        SideTwoReady: sideTwoConnected,
        SideOneConnected: true,
        SideTwoConnected: sideTwoConnected);

    private static NetworkRuntimeStateObserver CreateObserver(FrontlineConfig config) =>
        new(
            config.Sides.Length,
            clientAdmissionHistoryCapacity: 8,
            maxActorsPerCommandBatch: 8);

    private static PresentationStableIdAllocator RequireStableIds(GameEngine engine) =>
        engine.GetService(CoreServiceKeys.PresentationStableIdAllocator)
        ?? throw new InvalidOperationException("PresentationStableIdAllocator is unavailable.");

    private static OwnershipResolver RequireOwnership(GameEngine engine) =>
        engine.GetService(CoreServiceKeys.OwnershipResolver)
        ?? throw new InvalidOperationException("OwnershipResolver is unavailable.");

    private static PlayerEntityLookup RequirePlayers(GameEngine engine) =>
        engine.GetService(CoreServiceKeys.PlayerEntityLookup)
        ?? throw new InvalidOperationException("PlayerEntityLookup is unavailable.");

    private static void PublishAdmission(
        NetworkRuntimeStateObserver observer,
        ulong clientBatchSequence,
        OrderAdmissionStage stage,
        OrderSubmitResult result)
    {
        var seat = new NetworkCommandSeat(slot: 0, generation: 1, playerId: 1);
        var outcome = new NetworkCommandAdmissionOutcome(
            in seat,
            clientBatchSequence,
            targetTick: 301,
            actorCount: 1,
            orderId: 7,
            admissionBatchId: 11,
            admissionBatchIndex: 0,
            stage,
            result,
            isReplay: false,
            committedTick: 300);
        observer.OnClientAdmission(in outcome);
    }

    private static ScreenOverlayBuffer RequireOverlay(GameEngine engine) =>
        engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
        ?? throw new InvalidOperationException("ScreenOverlayBuffer is unavailable.");

    private static void AssertHudContains(
        FrontlinePresentationSystem presentation,
        ScreenOverlayBuffer overlay,
        string expected)
    {
        overlay.Clear();
        presentation.Update(0f);
        Assert.That(ReadOverlayStrings(overlay), Does.Contain(expected));
    }

    private static GameEngine CreateStartedReplicatedClientEngine(TestClientRuntimePort? runtimePort = null)
    {
        GameEngine engine = CreateStartedEngine(
            NetworkProcessRole.ReplicatedClient,
            runtimePort ?? new TestClientRuntimePort(),
            installPresentationServices: true);
        engine.SetService(CoreServiceKeys.ReplicatedClientCommandPort, new TestClientCommandPort());
        return engine;
    }

    private static GameEngine CreateStartedEngine(
        NetworkProcessRole role = NetworkProcessRole.Standalone,
        INetworkRuntimePort? runtimePort = null,
        bool installPresentationServices = true)
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(
                repoRoot,
                role == NetworkProcessRole.Standalone ? FrontlineMods : FrontlineNetworkedMods),
            Path.Combine(repoRoot, "assets"));
        if (installPresentationServices)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            engine.SetService(CoreServiceKeys.InputHandler, new PlayerInputHandler(new NullInputBackend(), inputConfig));
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.ViewController, new StubViewController(1920f, 1080f));
            engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)new SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)new SkiaImageSizeProvider());
        }

        if (role != NetworkProcessRole.Standalone)
        {
            NetworkRuntimeConfig network = engine.MergedConfig?.Networking
                ?? throw new InvalidOperationException("Network test engine is missing its network profile.");
            engine.SetService(
                CoreServiceKeys.ReplicationSchemaProjectors,
                new ReplicationSchemaProjectorRegistry(network.ReplicationSchemaCapacity));
            engine.SetService(
                CoreServiceKeys.ClientReplicationSchemaAppliers,
                new ClientReplicationSchemaApplierRegistry(network.ReplicationSchemaCapacity));
            engine.SetService(
                CoreServiceKeys.NetworkRuntimeStateObserver,
                new NetworkRuntimeStateObserver(
                    network.PlayerCapacity,
                    network.CommandSequenceHistoryCapacity,
                    network.MaxActorsPerCommandBatch));
            engine.ConfigureNetworkRuntime(
                role,
                runtimePort ?? throw new ArgumentNullException(nameof(runtimePort)));
        }

        engine.Start();
        return engine;
    }

    private static Vector3 RequireTemplateVisualScale(GameEngine engine, string templateId)
    {
        EntityTemplate template = engine.MapLoader.TemplateRegistry.Get(templateId)
            ?? throw new InvalidOperationException($"Entity template '{templateId}' is unavailable.");
        if (!template.Components.TryGetValue(nameof(VisualTransform), out var component))
        {
            throw new InvalidOperationException(
                $"Entity template '{templateId}' has no {nameof(VisualTransform)} component.");
        }

        return component.Deserialize<VisualTransform>(TemplateJsonOptions).Scale;
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

    private sealed class TestClientRuntimePort :
        INetworkRuntimePort,
        IReplicatedClientRuntimeStatus,
        IPresentationInterpolationSource
    {
        public NetworkProcessRole Role => NetworkProcessRole.ReplicatedClient;
        public ReplicatedClientConnectionState ConnectionState { get; set; } = ReplicatedClientConnectionState.Disconnected;
        public bool HasEstablishedSession { get; set; }
        public bool IsAwaitingFullSnapshot { get; set; }
        public bool IsFaulted { get; set; }
        public uint LastCommittedTick { get; set; }
        public float ReconnectWindowRemainingSeconds { get; set; } = 30f;
        public int RoundTripTimeMilliseconds { get; set; }
        public float InterpolationAlpha => 0f;

        public void Activate() { }
        public void PumpTransport() { }
        public void BeforeAuthoritativeTick(uint executingTick) { }
        public void AfterAuthoritativeCommit(uint committedTick) { }
        public void PumpReplicatedClient(float frameDeltaTime) { }
        public void Dispose() { }
    }

    private sealed class TestServerRuntimePort : INetworkRuntimePort
    {
        public NetworkProcessRole Role => NetworkProcessRole.AuthoritativeServer;
        public void Activate() { }
        public void PumpTransport() { }
        public void BeforeAuthoritativeTick(uint executingTick) { }
        public void AfterAuthoritativeCommit(uint committedTick) { }
        public void PumpReplicatedClient(float frameDeltaTime) { }
        public void Dispose() { }
    }

    private sealed class TestClientCommandPort : IReplicatedClientCommandPort
    {
        public ulong SubmissionRevision { get; set; }
        public ulong LastSubmittedBatchSequence { get; set; }
        public ReplicatedClientCommandSubmitResult LastSubmitResult { get; set; }

        public ReplicatedClientCommandSubmitResult Submit(in Order order) => LastSubmitResult;
        public ReplicatedClientCommandSubmitResult Submit(ReadOnlySpan<Order> orders) => LastSubmitResult;
    }
}
