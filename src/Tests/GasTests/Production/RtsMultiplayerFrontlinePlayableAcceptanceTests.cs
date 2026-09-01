using Ludots.Platform.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.ActionLoops;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Hosting;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Core.Vision;
using Ludots.Launcher.Backend;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;
using RtsDemoMod.Systems;
using RtsMultiplayerFrontlineMod.Runtime;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class RtsMultiplayerFrontlinePlayableAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "rts_duel_v1";
    private const string RuntimeKey = "rts.multiplayer.frontline.runtime";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "EntityCommandPanelMod",
        "RtsDemoMod",
        "RtsShowcaseMod",
        "RtsMultiplayerFrontlineMod",
    };

    [Test]
    [Description(
        "Feature: Symmetric opening battlefield terrain\n" +
        "  Given Frontline is a two-player competitive duel\n" +
        "  When the map presentation contract is resolved\n" +
        "  Then both players start on the same flat authored terrain instead of an asymmetric borrowed shoreline")]
    public void GivenFrontlineTerrain_WhenMapResolves_ThenBoardSurfaceAndHeightTruthAreBothDeclared()
    {
        using GameEngine engine = CreateStartedEngine();

        var mapConfig = engine.MapManager.LoadMap(MapId)
            ?? throw new InvalidOperationException("Frontline map config is missing.");

        Assert.That(
            mapConfig.ContinuousHeightmapAsset,
            Is.EqualTo("assets/terrain/rts_duel_v1_flat.vhtm"));
        Assert.That(mapConfig.TerrainPresentation, Is.Not.Null);
        Assert.That(mapConfig.TerrainPresentation!.Source, Is.EqualTo(TerrainPresentationSource.BoardTerrain));
        Assert.That(mapConfig.TerrainPresentation.BoardName, Is.EqualTo("default"));
        Assert.That(mapConfig.Boards, Has.Count.EqualTo(1));
        Assert.That(mapConfig.Boards[0].DataFile, Is.EqualTo("rts_duel_v1_flat.vtxm"));
    }

    [Test]
    [Description(
        "Feature: Room ready controls are playable from the product launcher\n" +
        "  Given a player starts the networked Frontline showcase from the official preset\n" +
        "  When the launcher resolves startup input contexts\n" +
        "  Then the F5 room-ready input context is active alongside Frontline commands without taking WASD from the camera")]
    public void GivenNetworkedFrontlinePreset_WhenLauncherResolves_ThenRoomReadyControlsAreActive()
    {
        var launcher = new LauncherService(FindRepoRoot());
        LauncherResolveResult result = launcher.Resolve(
            new[] { "preset:rts_multiplayer_frontline_networked_raylib" },
            LauncherPlatformIds.Raylib,
            LauncherBuildMode.Never);

        LauncherResolvedSetting startupInputContexts = result.Plan.Diagnostics.Settings
            .Single(setting => setting.Key == "startupInputContexts");
        JsonArray contexts = startupInputContexts.EffectiveValue?.AsArray()
            ?? throw new InvalidOperationException("startupInputContexts did not resolve to a JSON array.");
        string[] ids = contexts
            .Select(item => item?.GetValue<string>() ?? string.Empty)
            .ToArray();

        Assert.That(ids, Does.Contain("Default_Gameplay"));
        Assert.That(ids, Does.Contain("Frontline.Gameplay"));
        Assert.That(ids, Does.Contain("Frontline.RoomControls"));
        Assert.That(ids, Does.Not.Contain("Rts_Gameplay"));

        using GameEngine engine = CreateStartedEngine();
        InputConfigRoot input = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        InputContextDef frontline = input.Contexts.Single(context => context.Id == "Frontline.Gameplay");
        string[] frontlinePaths = FlattenBindingPaths(frontline.Bindings).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(input.Actions.Any(action => action.Id == "SkillQ"), Is.True);
            Assert.That(input.Actions.Any(action => action.Id == "CommandSourceAcquire"), Is.True);
            Assert.That(input.Actions.Any(action => action.Id == "Command"), Is.True);
            Assert.That(frontlinePaths, Does.Contain("<Keyboard>/q"));
            Assert.That(frontlinePaths, Does.Contain("<Mouse>/LeftButton"));
            Assert.That(frontlinePaths, Does.Contain("<Mouse>/RightButton"));
            Assert.That(frontlinePaths, Does.Not.Contain("<Keyboard>/w"));
            Assert.That(frontlinePaths, Does.Not.Contain("<Keyboard>/a"));
            Assert.That(frontlinePaths, Does.Not.Contain("<Keyboard>/s"));
            Assert.That(frontlinePaths, Does.Not.Contain("<Keyboard>/d"));
        });
    }

    [Test]
    [Description(
        "Feature: Fair match start\n" +
        "  Given two players join the Frontline duel\n" +
        "  When the battlefield finishes loading\n" +
        "  Then each player sees one command core, two harvesters, two infantry squads, and 40 crystals")]
    public void GivenTwoPlayers_WhenBattleLoads_ThenBothReceiveTheSameStartingForce()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);

        World world = engine.World;
        AssertSide(world, playerId: 1, teamId: 1, "Northern", expectedCrystals: 40f);
        AssertSide(world, playerId: 2, teamId: 2, "Southern", expectedCrystals: 40f);
        Assert.That(CountNamed(world, "Crystal Field"), Is.EqualTo(2));
        Assert.That(ReadSnapshot(engine, "Outcome"), Is.EqualTo("InProgress"));
        Assert.That(ReadSnapshot(engine, "Phase"), Is.EqualTo("WaitingForPlayers"));
        Assert.That(engine.TriggerManager.Errors, Is.Empty);
    }

    [Test]
    [Description(
        "Feature: Same-screen opponent feedback\n" +
        "  Given the Frontline duel loads as a one-screen sandbox\n" +
        "  When both opening armies are revealed by the authored starting vision\n" +
        "  Then each player has live knowledge for the other player's command core, harvesters, and infantry")]
    public void GivenOneScreenSandbox_WhenBattleLoads_ThenEachPlayerCanSeeTheEnemyArmy()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);

        FrontlineConfig config = GetFrontlineConfig(engine);
        KnowledgeProjectionStore knowledge = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
            ?? throw new InvalidOperationException("KnowledgeProjectionStore is missing.");

        for (int sideIndex = 0; sideIndex < config.Sides.Length; sideIndex++)
        {
            FrontlineSideConfig side = config.Sides[sideIndex];
            Entity viewer = engine.CurrentMapSession!.PlayerEntityLookup.Get(side.PlayerId);
            int visibleEnemyCount = 0;
            var query = new QueryDescription()
                .WithAll<FrontlineParticipant, FogOccupantCm>()
                .WithAny<FrontlineCore, FrontlineHarvester, FrontlineInfantry>();

            foreach (ref Chunk chunk in engine.World.Query(in query))
            {
                ReadOnlySpan<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
                ref Entity first = ref chunk.Entity(0);
                foreach (int index in chunk)
                {
                    if (participants[index].SideIndex == sideIndex)
                    {
                        continue;
                    }

                    Entity enemy = System.Runtime.CompilerServices.Unsafe.Add(ref first, index);
                    Assert.That(
                        knowledge.TryGet(
                            viewer,
                            enemy,
                            engine.GameSession.CurrentTick,
                            out KnowledgeDisclosureRecord record),
                        Is.True,
                        $"Player side {sideIndex} must receive the enemy opening unit in the same-screen sandbox.");
                    Assert.Multiple(() =>
                    {
                        Assert.That(record.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
                        Assert.That(record.Position, Is.EqualTo(KnowledgePositionAccess.Live));
                    });
                    visibleEnemyCount++;
                }
            }

            Assert.That(visibleEnemyCount, Is.EqualTo(5));
        }
    }

    [Test]
    [Description(
        "Feature: Readable multiplayer battlefield\n" +
        "  Given a new player enters the Frontline duel\n" +
        "  When the battlefield finishes loading\n" +
        "  Then command cores, harvesters, infantry, and crystal fields each appear with a distinct visible battlefield shape")]
    public void GivenNewPlayer_WhenBattleLoads_ThenEveryCombatEntityHasReadablePresentation()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        Tick(engine, 2);

        PresenterDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
            ?? throw new InvalidOperationException("PresenterDefinitionRegistry service is missing.");
        PresenterEntityRuntime performers = engine.GetService(CoreServiceKeys.PresenterEntityRuntime)
            ?? throw new InvalidOperationException("PresenterEntityRuntime service is missing.");
        PrimitiveDrawBuffer primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
            ?? throw new InvalidOperationException("PrimitiveDrawBuffer service is missing.");
        IContinuousHeightmap heightmap = engine.GetService(CoreServiceKeys.ContinuousHeightmap)
            ?? throw new InvalidOperationException("The Frontline battlefield must register its declared visual heightmap.");

        AssertReadablePresentation(
            engine.World,
            definitions,
            performers,
            "rts.frontline.visual.core",
            "Command Core");
        AssertReadablePresentation(
            engine.World,
            definitions,
            performers,
            "rts.frontline.visual.harvester",
            "Harvester");
        AssertReadablePresentation(
            engine.World,
            definitions,
            performers,
            "rts.frontline.visual.infantry",
            "Infantry");
        AssertReadablePresentation(
            engine.World,
            definitions,
            performers,
            "rts.frontline.visual.crystal",
            "Crystal Field");
        AssertCombatEntitiesGrounded(engine.World, heightmap);

        int visiblePrimitiveCount = 0;
        foreach (ref readonly PrimitiveDrawItem item in primitives.GetSpan())
        {
            if (item.Visibility == VisualVisibility.Visible)
            {
                visiblePrimitiveCount++;
            }
        }

        Assert.That(
            visiblePrimitiveCount,
            Is.GreaterThanOrEqualTo(12),
            "The opening battlefield must emit visible geometry for its twelve combat entities.");
    }

    [Test]
    [Description(
        "Feature: Both players ready before battle\n" +
        "  Given only one player is ready\n" +
        "  When the other player is not ready and someone tries to move a unit early\n" +
        "  Then the battle waits, rejects the early command, and enables movement only after both players complete the three-second countdown")]
    public void GivenOnlyOneReady_WhenOrdersArrive_ThenBattleWaitsAndCommandsStartAfterCountdown()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);

        World world = engine.World;
        Entity harvester = FindNamed(world, "Northern Harvester A");
        WorldPositionCm start = world.Get<WorldPositionCm>(harvester);
        SetParticipantReady(engine, sideIndex: 0, ready: true);
        Assert.That(ReadSnapshot(engine, "Phase"), Is.EqualTo("WaitingForPlayers"));

        EnqueueMove(engine, harvester, playerId: 1, x: 12000, y: 14200);
        Tick(engine, 30);
        Assert.That(DistanceCm(world.Get<WorldPositionCm>(harvester), start), Is.EqualTo(0f));
        Assert.That(world.Get<OrderBuffer>(harvester).IsEmpty, Is.True,
            "A pre-match command must be explicitly cancelled, not held for a surprise start.");

        SetParticipantReady(engine, sideIndex: 1, ready: true);
        Assert.That(ReadSnapshot(engine, "Phase"), Is.EqualTo("Countdown"));
        Assert.That(ReadSnapshot(engine, "CountdownRemainingTicks"), Is.EqualTo(90));
        EnqueueMove(engine, harvester, playerId: 1, x: 12000, y: 14200);
        TickUntilCountdown(engine, 1);
        Assert.That(DistanceCm(world.Get<WorldPositionCm>(harvester), start), Is.EqualTo(0f));
        Assert.That(world.Get<OrderBuffer>(harvester).IsEmpty, Is.True);

        TickUntil(engine, () => Equals(ReadSnapshot(engine, "Phase"), "InProgress"), 8,
            "The battle should start after the configured three-second countdown.");
        EnqueueMove(engine, harvester, playerId: 1, x: 12000, y: 14200);
        TickUntil(engine, () => DistanceCm(world.Get<WorldPositionCm>(harvester), start) > 100f, 30,
            "Movement should become available after the countdown.");
    }

    [Test]
    [Description(
        "Feature: Ready state changes\n" +
        "  Given both players entered the three-second start countdown\n" +
        "  When either player cancels ready or disconnects\n" +
        "  Then the countdown stops immediately and reconnecting does not silently mark that player ready")]
    public void GivenCountdown_WhenPlayerUnreadyOrDisconnects_ThenCountdownCancelsImmediately()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);

        SetParticipantReady(engine, sideIndex: 0, ready: true);
        SetParticipantReady(engine, sideIndex: 1, ready: true);
        TickUntilCountdown(engine, 60);
        SetParticipantReady(engine, sideIndex: 1, ready: false);
        Assert.That(ReadSnapshot(engine, "Phase"), Is.EqualTo("WaitingForPlayers"));
        Assert.That(ReadSnapshot(engine, "CountdownRemainingTicks"), Is.EqualTo(0));

        SetParticipantReady(engine, sideIndex: 1, ready: true);
        Assert.That(ReadSnapshot(engine, "Phase"), Is.EqualTo("Countdown"));
        SetParticipantConnected(engine, sideIndex: 0, connected: false);
        Assert.That(ReadSnapshot(engine, "Phase"), Is.EqualTo("WaitingForPlayers"));
        Assert.That(ReadSnapshot(engine, "SideOneReady"), Is.EqualTo(false));

        SetParticipantConnected(engine, sideIndex: 0, connected: true);
        Tick(engine, 10);
        Assert.That(ReadSnapshot(engine, "Phase"), Is.EqualTo("WaitingForPlayers"),
            "Reconnect must not silently ready a player.");
    }

    [Test]
    [Description(
        "Feature: Gather crystals\n" +
        "  Given the northern player has 40 crystals and selects a harvester\n" +
        "  When the player orders it to gather from the northern crystal field\n" +
        "  Then crystals stay unchanged while loading and 20 crystals arrive only after the harvester reaches the command core dock")]
    public void GivenHarvester_WhenPlayerOrdersGather_ThenCrystalsArriveOnlyAfterReturn()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        World world = engine.World;
        Entity core = FindNamed(world, "Northern Command Core");
        Entity harvester = FindNamed(world, "Northern Harvester A");
        Entity node = FindNamed(world, "Northern Crystal Field");
        int crystalAttributeId = RequireAttribute("Crystals");
        int gatherOrderTypeId = RequireOrderType(engine, "frontlineGather");

        EnqueueOrder(engine, gatherOrderTypeId, playerId: 1, harvester, node);
        WorldPositionCm start = world.Get<WorldPositionCm>(harvester);
        TickUntil(
            engine,
            () => DistanceCm(world.Get<WorldPositionCm>(harvester), start) > 500f,
            180,
            "The harvester should visibly leave its starting position.");

        Assert.That(ReadAttribute(world, core, crystalAttributeId), Is.EqualTo(40f));
        TickUntil(
            engine,
            () => DistanceCm(world.Get<WorldPositionCm>(harvester), world.Get<WorldPositionCm>(node)) <= 100f,
            420,
            "The harvester should reach the crystal field.");
        Assert.That(ReadAttribute(world, core, crystalAttributeId), Is.EqualTo(40f),
            "Crystals must not be credited at the mine.");

        Tick(engine, 30);
        Assert.That(ReadAttribute(world, core, crystalAttributeId), Is.EqualTo(40f),
            "Crystals must remain unchanged while loading.");

        TickUntil(
            engine,
            () => ReadAttribute(world, core, crystalAttributeId) == 60f,
            600,
            "The 20-crystal cargo should be credited only after the harvester returns.");
        ResourceSinkProfile sink = world.Get<ResourceSinkProfile>(core);
        WorldCmInt2 corePosition = world.Get<WorldPositionCm>(core).ToWorldCmInt2();
        WorldPositionCm dockPosition = WorldPositionCm.FromCm(
            corePosition.X + sink.DockOffsetXCm,
            corePosition.Y + sink.DockOffsetYCm);
        Assert.That(DistanceCm(world.Get<WorldPositionCm>(harvester), dockPosition), Is.LessThanOrEqualTo(100f));
    }

    [Test]
    [Description(
        "Feature: Train infantry\n" +
        "  Given the command core has only 40 crystals\n" +
        "  When the player tries to train an infantry squad costing 60 crystals\n" +
        "  Then the command is rejected without charging crystals or creating a squad")]
    public void GivenFortyCrystals_WhenPlayerTrainsInfantry_ThenOrderIsRejectedWithoutCharge()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        World world = engine.World;
        Entity core = FindNamed(world, "Northern Command Core");
        int crystalAttributeId = RequireAttribute("Crystals");
        int startingInfantry = CountTemplateEntities(engine, "rts_frontline_infantry");

        OrderSubmitResult rejected = SubmitTraining(engine, core, playerId: 1, slot: 0, out _);
        Assert.Multiple(() =>
        {
            Assert.That(rejected, Is.EqualTo(OrderSubmitResult.RejectedByRule));
            Assert.That(world.Get<OrderBuffer>(core).HasActive, Is.False);
            Assert.That(world.Get<OrderBuffer>(core).HasQueued, Is.False);
        });
        Tick(engine, 260);

        Assert.That(ReadAttribute(world, core, crystalAttributeId), Is.EqualTo(40f));
        Assert.That(CountNamed(world, "Infantry"), Is.EqualTo(startingInfantry));
    }

    [Test]
    [Description(
        "Feature: Reserve crystals when training commands arrive\n" +
        "  Given the command core has 60 crystals\n" +
        "  When the player queues two infantry squads at once\n" +
        "  Then the first command starts, the second is rejected for insufficient crystals, and only one squad is produced")]
    public void GivenSixtyCrystals_WhenTwoTrainingCommandsArrive_ThenOnlyFirstIsAdmitted()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        World world = engine.World;
        Entity core = FindNamed(world, "Northern Command Core");
        int crystalAttributeId = RequireAttribute("Crystals");
        TagOps tagOps = RequireTagOps(engine);
        AttributeMutationOps.SetCurrent(world, core, crystalAttributeId, 60f, tagOps);
        int startingInfantry = CountNamed(world, "Infantry");
        OrderSubmitResult firstOutcome;
        OrderSubmitResult secondOutcome;
        Order first;
        SubmitTrainingBatch(
            engine,
            core,
            playerId: 1,
            slot: 0,
            out firstOutcome,
            out first,
            out secondOutcome,
            out _);
        Assert.Multiple(() =>
        {
            Assert.That(firstOutcome, Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(secondOutcome, Is.EqualTo(OrderSubmitResult.RejectedByRule));
            Assert.That(ReadAttribute(world, core, crystalAttributeId), Is.EqualTo(60f));
            Assert.That(world.Get<OrderBuffer>(core).QueuedCount, Is.EqualTo(1));
        });

        TickUntil(
            engine,
            () => world.Get<OrderBuffer>(core).HasActive &&
                world.Get<OrderBuffer>(core).ActiveOrder.Order.OrderId == first.OrderId,
            4,
            "The first queued training order should start on the next fixed simulation step.");
        Assert.Multiple(() =>
        {
            Assert.That(world.Get<OrderBuffer>(core).ActiveOrder.Order.OrderId, Is.EqualTo(first.OrderId));
            Assert.That(ReadAttribute(world, core, crystalAttributeId), Is.EqualTo(0f));
            Assert.That(world.Get<OrderBuffer>(core).QueuedCount, Is.Zero);
        });

        AdvanceCommittedTicks(engine, 239);
        TickUntil(engine, () => CountNamed(world, "Infantry") == startingInfantry + 1, 8,
            "Exactly the admitted squad should finish after eight seconds.");
        Assert.That(CountNamed(world, "Infantry"), Is.EqualTo(startingInfantry + 1));
    }

    [Test]
    [Description(
        "Feature: Train two squads in sequence\n" +
        "  Given the command core has 120 crystals\n" +
        "  When the player queues two infantry squads\n" +
        "  Then the second command waits persistently and starts only after the first eight-second training finishes")]
    public void GivenOneHundredTwentyCrystals_WhenTwoTrainingCommandsArrive_ThenSecondStartsAfterFirst()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        World world = engine.World;
        Entity core = FindNamed(world, "Northern Command Core");
        int crystalAttributeId = RequireAttribute("Crystals");
        AttributeMutationOps.SetCurrent(world, core, crystalAttributeId, 120f, RequireTagOps(engine));
        List<Entity> startingInfantryEntities = FindTemplateEntities(engine, "rts_frontline_infantry");
        int startingInfantry = startingInfantryEntities.Count;
        OrderSubmitResult firstResult;
        OrderSubmitResult secondResult;
        Order first;
        Order second;
        SubmitTrainingBatch(
            engine,
            core,
            playerId: 1,
            slot: 0,
            out firstResult,
            out first,
            out secondResult,
            out second);
        Assert.Multiple(() =>
        {
            Assert.That(firstResult, Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(secondResult, Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(second.OrderId, Is.Not.EqualTo(first.OrderId),
                "Sequential training commands must keep distinct order identities.");
            Assert.That(ReadAttribute(world, core, crystalAttributeId), Is.EqualTo(120f));
            Assert.That(world.Get<OrderBuffer>(core).QueuedCount, Is.EqualTo(2));
        });

        TickUntil(
            engine,
            () => world.Get<OrderBuffer>(core).HasActive &&
                world.Get<OrderBuffer>(core).ActiveOrder.Order.OrderId == first.OrderId,
            4,
            "The first queued training order should start on the next fixed simulation step.");
        Assert.Multiple(() =>
        {
            Assert.That(world.Get<OrderBuffer>(core).ActiveOrder.Order.OrderId, Is.EqualTo(first.OrderId));
            Assert.That(ReadAttribute(world, core, crystalAttributeId), Is.EqualTo(60f));
            Assert.That(world.Get<OrderBuffer>(core).QueuedCount, Is.EqualTo(1));
        });

        AdvanceCommittedTicks(engine, 239);
        Assert.That(CountTemplateEntities(engine, "rts_frontline_infantry"), Is.EqualTo(startingInfantry),
            "The first squad must not finish before eight seconds.");

        TickUntil(
            engine,
            () => world.Get<OrderBuffer>(core).HasActive &&
                world.Get<OrderBuffer>(core).ActiveOrder.Order.OrderId == second.OrderId &&
                world.Get<FrontlineCoreState>(core).LastHandledTrainOrderId == second.OrderId &&
                ReadAttribute(world, core, crystalAttributeId) == 0f &&
                CountNamed(world, "Infantry") == startingInfantry + 1,
            16,
            "The second squad should start and expose the complete first-training result after the first training finishes.");

        Assert.Multiple(() =>
        {
            Assert.That(CountNamed(world, "Infantry"), Is.EqualTo(startingInfantry + 1));
            Assert.That(ReadAttribute(world, core, crystalAttributeId), Is.EqualTo(0f));
            Assert.That(world.Get<FrontlineCoreState>(core).LastHandledTrainOrderId, Is.EqualTo(second.OrderId));
            Assert.That(world.Get<OrderBuffer>(core).ActiveOrder.Order.OrderId, Is.EqualTo(second.OrderId));
        });

        AdvanceCommittedTicks(engine, 239);
        Assert.That(CountNamed(world, "Infantry"), Is.EqualTo(startingInfantry + 1));
        TickUntil(engine, () => CountNamed(world, "Infantry") == startingInfantry + 2, 8,
            "The second squad should finish after its own eight-second training time.");

        List<Entity> trainedInfantry = FindTemplateEntities(engine, "rts_frontline_infantry")
            .Where(entity => !startingInfantryEntities.Contains(entity))
            .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(trainedInfantry, Has.Count.EqualTo(2));
            Assert.That(
                world.Get<WorldPositionCm>(trainedInfantry[1]),
                Is.Not.EqualTo(world.Get<WorldPositionCm>(trainedInfantry[0])),
                "Two sequentially trained infantry must remain independently selectable at distinct positions.");
        });
    }

    [Test]
    [Description(
        "Feature: Train infantry\n" +
        "  Given the command core has 60 crystals\n" +
        "  When the player trains infantry and waits eight seconds\n" +
        "  Then 60 crystals are charged once and exactly one new squad arrives for that player")]
    public void GivenSixtyCrystals_WhenTrainingFinishes_ThenExactlyOneOwnedInfantryArrives()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        World world = engine.World;
        Entity core = FindNamed(world, "Northern Command Core");
        int crystalAttributeId = RequireAttribute("Crystals");
        AttributeMutationOps.SetCurrent(world, core, crystalAttributeId, 60f, RequireTagOps(engine));
        int startingInfantry = CountNamed(world, "Infantry");

        EnqueueCastAbility(engine, core, playerId: 1, slot: 0);
        TickUntil(
            engine,
            () => ReadAttribute(world, core, crystalAttributeId) == 0f,
            20,
            "An admitted training order should charge exactly 60 crystals.");
        AdvanceCommittedTicks(engine, 239);
        Assert.That(CountNamed(world, "Infantry"), Is.EqualTo(startingInfantry),
            "The squad must not arrive before the configured training time.");

        TickUntil(
            engine,
            () => CountTemplateEntities(engine, "rts_frontline_infantry") >= startingInfantry + 1,
            8,
            "One infantry squad should arrive when training completes.");

        Entity created = FindRuntimeTemplateEntity(engine, "rts_frontline_infantry");
        Assert.That(CountTemplateEntities(engine, "rts_frontline_infantry"), Is.EqualTo(startingInfantry + 1));
        Assert.That(world.Get<PlayerOwner>(created).PlayerId, Is.EqualTo(1));
        Assert.That(world.Get<Team>(created).Id, Is.EqualTo(1));
        Assert.That(ReadAttribute(world, core, crystalAttributeId), Is.EqualTo(0f));
    }

    [Test]
    [Description(
        "Feature: Train infantry from either starting side\n" +
        "  Given the southern command core has 60 crystals\n" +
        "  When the southern player trains infantry and waits eight seconds\n" +
        "  Then exactly one new squad arrives for player two on the southern side")]
    public void GivenSouthernCore_WhenTrainingFinishes_ThenInfantryKeepsSouthernIdentity()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        World world = engine.World;
        Entity core = FindNamed(world, "Southern Command Core");
        int crystalAttributeId = RequireAttribute("Crystals");
        AttributeMutationOps.SetCurrent(world, core, crystalAttributeId, 60f, RequireTagOps(engine));
        FrontlineConfig config = GetFrontlineConfig(engine);
        int startingInfantry = CountTemplateEntities(engine, "rts_frontline_infantry");

        EnqueueCastAbility(engine, core, playerId: 2, slot: 0);
        TickUntil(
            engine,
            () => ReadAttribute(world, core, crystalAttributeId) == 0f,
            20,
            "An admitted southern training order should charge exactly 60 crystals.");
        AdvanceCommittedTicks(engine, 239);
        Assert.That(CountTemplateEntities(engine, "rts_frontline_infantry"), Is.EqualTo(startingInfantry),
            "The southern squad must not arrive before the configured training time.");

        AdvanceCommittedTicks(engine, 8);
        Assert.That(
            CountTemplateEntities(engine, "rts_frontline_infantry"),
            Is.EqualTo(startingInfantry + 1),
            "Exactly one southern gameplay squad should arrive when training completes.");

        AdvanceCommittedTicks(engine, 1);
        Entity created = FindRuntimeTemplateEntity(engine, "rts_frontline_infantry");
        Assert.Multiple(() =>
        {
            Assert.That(CountTemplateEntities(engine, "rts_frontline_infantry"), Is.EqualTo(startingInfantry + 1));
            Assert.That(world.Get<PlayerOwner>(created).PlayerId, Is.EqualTo(2));
            Assert.That(world.Get<Team>(created).Id, Is.EqualTo(2));
            Assert.That(world.Get<VisionEmitterCm>(created).ScopeKeyId, Is.EqualTo(RtsMultiplayerFrontlineMod.Runtime.FrontlineVisionScopes.Resolve(engine, config.Sides[1].VisionScopeKey)));
            Assert.That(ReadAttribute(world, core, crystalAttributeId), Is.EqualTo(0f));
        });
    }

    [Test]
    [Description(
        "Feature: Infantry combat\n" +
        "  Given northern and southern infantry are far apart\n" +
        "  When the northern player orders an attack on the enemy\n" +
        "  Then the northern infantry pursues, deals damage in range, and removes the defeated enemy from the battlefield")]
    public void GivenHostileTarget_WhenPlayerOrdersAttack_ThenInfantryPursuesDamagesAndDefeatsIt()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        World world = engine.World;
        Entity attacker = FindNamed(world, "Northern Infantry A");
        Entity target = FindNamed(world, "Southern Infantry A");
        int healthAttributeId = RequireAttribute("Health");
        int attackOrderTypeId = RequireOrderType(engine, "attackTarget");
        WorldPositionCm startingPosition = world.Get<WorldPositionCm>(attacker);

        EnqueueOrder(engine, attackOrderTypeId, playerId: 1, attacker, target);
        TickUntil(
            engine,
            () => DistanceCm(world.Get<WorldPositionCm>(attacker), startingPosition) > 500f,
            180,
            "The infantry should pursue instead of damaging from across the map.");
        Assert.That(ReadAttribute(world, target, healthAttributeId), Is.EqualTo(100f));

        TickUntil(
            engine,
            () => world.IsAlive(target) && ReadAttribute(world, target, healthAttributeId) < 100f,
            1200,
            "The pursuing infantry should enter range and deal damage.");
        TickUntil(
            engine,
            () => !world.IsAlive(target),
            360,
            "The defeated infantry should be removed from the battlefield.");
    }

    [Test]
    [Description(
        "Feature: Change orders during battle\n" +
        "  Given a selected infantry squad is moving across the battlefield\n" +
        "  When the player orders it to attack an enemy squad\n" +
        "  Then the attack replaces movement immediately instead of waiting in a queue and expiring")]
    public void GivenMovingInfantry_WhenNetworkPlayerOrdersAttack_ThenAttackInterruptsMovementImmediately()
    {
        NetworkRuntimeConfig networkProfile = LoadNetworkProfile();
        NetworkCommandSchemaConfig moveSchema = networkProfile.CommandSchemas.Single(
            schema => schema.OrderTypeKey == "moveTo");
        NetworkCommandSchemaConfig attackSchema = networkProfile.CommandSchemas.Single(
            schema => schema.OrderTypeKey == "attackTarget");
        Assert.Multiple(() =>
        {
            Assert.That(moveSchema.AllowedSubmitModes,
                Is.EquivalentTo(new[] { OrderSubmitMode.Immediate, OrderSubmitMode.Queued }));
            Assert.That(attackSchema.AllowedSubmitModes,
                Is.EquivalentTo(new[] { OrderSubmitMode.Immediate, OrderSubmitMode.Queued }));
            Assert.That(attackSchema.TargetKind, Is.EqualTo(NetworkCommandTargetKind.WorldPositionAndEntity),
                "Attack commands must carry both the hostile entity and the player-authored engagement point.");
        });

        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        World world = engine.World;
        Entity attacker = FindNamed(world, "Northern Infantry A");
        Entity target = FindNamed(world, "Southern Infantry A");
        OrderQueue queue = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("OrderQueue service is missing.");
        OrderBufferSystem orders = engine.GetService(CoreServiceKeys.OrderBufferSystem)
            ?? throw new InvalidOperationException("OrderBufferSystem service is missing.");

        WorldCmInt2 start = world.Get<WorldPositionCm>(attacker).ToWorldCmInt2();
        var move = new Order
        {
            OrderTypeId = RequireOrderType(engine, moveSchema.OrderTypeKey),
            PlayerId = 1,
            Actor = attacker,
            Target = Entity.Null,
            Args = OrderArgs.CreateSingleWorldCm(new Vector3(start.X + 5000, 0f, start.Y)),
            SubmitMode = OrderSubmitMode.Immediate,
        };
        queue.EnsureOrderId(ref move);
        OrderAdmissionResultBuffer admission = engine.GetService(CoreServiceKeys.OrderAdmissionResultBuffer)
            ?? throw new InvalidOperationException("OrderAdmissionResultBuffer service is missing.");
        admission.BeginLogicStep();
        Assert.That(orders.SubmitOrder(attacker, in move), Is.EqualTo(OrderSubmitResult.Activated));

        var attack = new Order
        {
            OrderTypeId = RequireOrderType(engine, attackSchema.OrderTypeKey),
            PlayerId = 1,
            Actor = attacker,
            Target = target,
            SubmitMode = OrderSubmitMode.Immediate,
        };
        queue.EnsureOrderId(ref attack);
        OrderSubmitResult attackResult = orders.SubmitOrder(attacker, in attack);
        admission.EndEntityIntake();
        admission.EndLogicStep();
        OrderBuffer buffer = world.Get<OrderBuffer>(attacker);

        Assert.Multiple(() =>
        {
            Assert.That(attackResult, Is.EqualTo(OrderSubmitResult.Activated));
            Assert.That(buffer.HasActive, Is.True);
            Assert.That(buffer.ActiveOrder.Order.OrderId, Is.EqualTo(attack.OrderId));
            Assert.That(buffer.HasQueued, Is.False);
            Assert.That(buffer.HasPending, Is.False);
        });
    }

    [Test]
    [Description(
        "Feature: Train infantry through multiplayer controls\n" +
        "  Given the player has selected their command core\n" +
        "  When the player trains normally or holds the queue modifier while training\n" +
        "  Then the multiplayer session accepts both commands using the same controls as local play")]
    public void GivenSelectedCore_WhenPlayerTrainsNormallyOrQueuesTraining_ThenNetworkProfileAcceptsBothModes()
    {
        NetworkRuntimeConfig networkProfile = LoadNetworkProfile();
        NetworkCommandSchemaConfig trainingSchema = networkProfile.CommandSchemas.Single(
            schema => schema.OrderTypeKey == "castAbility");

        Assert.Multiple(() =>
        {
            Assert.That(
                trainingSchema.AllowedSubmitModes,
                Is.EquivalentTo(new[]
                {
                    OrderSubmitMode.Immediate,
                    OrderSubmitMode.Queued,
                    OrderSubmitMode.PersistentQueued,
                }));
            Assert.That(
                networkProfile.MaxPastTargetTicks,
                Is.GreaterThanOrEqualTo(
                    networkProfile.SnapshotAcknowledgementTimeoutTicks + networkProfile.MaxFutureTargetTicks),
                "The unstable profile must accept commands throughout one explicit snapshot recovery window.");
        });
    }

    [Test]
    [Description(
        "Feature: Destroy the command core\n" +
        "  Given northern infantry can reach the southern command core\n" +
        "  When the northern player orders that infantry to attack the core\n" +
        "  Then the northern player wins immediately")]
    public void GivenRunningMatch_WhenSouthernCoreFalls_ThenNorthernPlayerWins()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        World world = engine.World;
        Entity attacker = FindNamed(world, "Northern Infantry A");
        Entity targetCore = FindNamed(world, "Southern Command Core");
        EnqueueOrder(
            engine,
            RequireOrderType(engine, "attackTarget"),
            playerId: 1,
            attacker,
            targetCore);
        TickUntil(
            engine,
            () => Equals(ReadSnapshot(engine, "Outcome"), "SideOneVictory"),
            6000,
            "The infantry attack should destroy the opposing core and finish the match.");

        TickUntil(
            engine,
            () => !world.IsAlive(targetCore),
            8,
            "The defeated command core should leave the authoritative world through normal cleanup.");

        FrontlineMatchSnapshot match = GetFrontlineRuntime(engine).Snapshot;
        FrontlineMatchResolutionSnapshot resolution = GetFrontlineRuntime(engine).Resolution;

        Assert.Multiple(() =>
        {
            Assert.That(match.Outcome, Is.EqualTo(FrontlineMatchOutcome.SideOneVictory));
            Assert.That(match.WinningSideIndex, Is.EqualTo(0));
            Assert.That(resolution.CommittedTick, Is.EqualTo(match.CommittedTick));
            Assert.That(resolution.Reason, Is.EqualTo(FrontlineMatchResolutionReason.CoreDestroyed));
            Assert.That(resolution.Outcome, Is.EqualTo(match.Outcome));
            Assert.That(resolution.WinningSideIndex, Is.EqualTo(match.WinningSideIndex));
            Assert.That(resolution.SideOneCoreHealth, Is.GreaterThan(0f));
            Assert.That(resolution.SideTwoCoreHealth, Is.LessThanOrEqualTo(0f));
        });
    }

    [Test]
    [Description(
        "Feature: Resolve simultaneous destruction\n" +
        "  Given both command cores are one hit from destruction\n" +
        "  When both cores are destroyed on the same committed simulation tick\n" +
        "  Then the match ends in a draw without favoring iteration order")]
    public void GivenBothCoresFallOnSameCommittedTick_WhenOutcomeResolves_ThenMatchIsDraw()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        SetHealth(engine, FindNamed(engine.World, "Northern Command Core"), 0f);
        SetHealth(engine, FindNamed(engine.World, "Southern Command Core"), 0f);
        TickUntil(
            engine,
            () => Equals(ReadSnapshot(engine, "Outcome"), "Draw"),
            4,
            "Both cores should resolve on the next committed simulation tick.");

        Assert.That(ReadSnapshot(engine, "Outcome"), Is.EqualTo("Draw"));
        Assert.That(ReadSnapshot(engine, "WinningSideIndex"), Is.EqualTo(-1));
    }

    [Test]
    [Description(
        "Feature: Resolve the time limit\n" +
        "  Given both command cores survive for five minutes\n" +
        "  When match time expires\n" +
        "  Then the player whose core has more health wins")]
    public void GivenFiveMinuteLimit_WhenBothCoresSurvive_ThenHigherHealthSideWins()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        SetHealth(engine, FindNamed(engine.World, "Southern Command Core"), 800f);
        AdvanceFrontlineClockWithoutWorld(engine, 8999);
        Assert.That(ReadSnapshot(engine, "Outcome"), Is.EqualTo("InProgress"));

        TickUntilCommittedTick(engine, 9000);
        FrontlineMatchResolutionSnapshot resolution = GetFrontlineRuntime(engine).Resolution;
        Assert.Multiple(() =>
        {
            Assert.That(ReadSnapshot(engine, "Outcome"), Is.EqualTo("SideOneVictory"));
            Assert.That(resolution.Reason, Is.EqualTo(FrontlineMatchResolutionReason.Duration));
            Assert.That(resolution.SideOneCoreHealth, Is.EqualTo(1000f));
            Assert.That(resolution.SideTwoCoreHealth, Is.EqualTo(800f));
        });
    }

    [Test]
    [Description(
        "Feature: Resolve a disconnected player\n" +
        "  Given the southern player enters the 30-second disconnect grace period before the five-minute limit\n" +
        "  When disconnect grace and match time expire on the same tick\n" +
        "  Then the disconnect result is applied before the command-core health comparison")]
    public void GivenDisconnectGraceAndTimeLimitExpireTogether_WhenOutcomeResolves_ThenDisconnectTakesPriority()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        SetHealth(engine, FindNamed(engine.World, "Northern Command Core"), 500f);
        AdvanceFrontlineClockWithoutWorld(engine, 8100);
        SetParticipantConnected(engine, sideIndex: 1, connected: false);
        AdvanceFrontlineClockWithoutWorld(engine, 8999);
        TickUntilCommittedTick(engine, 9000);

        FrontlineMatchResolutionSnapshot resolution = GetFrontlineRuntime(engine).Resolution;
        Assert.Multiple(() =>
        {
            Assert.That(ReadSnapshot(engine, "CommittedTick"), Is.EqualTo(9000));
            Assert.That(ReadSnapshot(engine, "Outcome"), Is.EqualTo("SideOneVictory"),
                "Disconnect expiry must outrank the simultaneous higher-health time-limit result.");
            Assert.That(resolution.Reason, Is.EqualTo(FrontlineMatchResolutionReason.Disconnect));
            Assert.That(resolution.SideOneCoreHealth, Is.EqualTo(500f));
            Assert.That(resolution.SideTwoCoreHealth, Is.EqualTo(1000f));
        });
    }

    [Test]
    [Description(
        "Feature: Preserve the five-minute result during reconnect\n" +
        "  Given a player disconnects shortly before the five-minute limit\n" +
        "  When the limit arrives and that player reconnects inside the grace period\n" +
        "  Then the match resolves from both core health values recorded at five minutes")]
    public void GivenDisconnectAtTimeLimit_WhenPlayerReconnects_ThenRecordedCoreHealthDecidesOutcome()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        Entity northernCore = FindNamed(engine.World, "Northern Command Core");
        Entity southernCore = FindNamed(engine.World, "Southern Command Core");
        SetHealth(engine, northernCore, 900f);
        SetHealth(engine, southernCore, 800f);
        AdvanceFrontlineClockWithoutWorld(engine, 8990);
        SetParticipantConnected(engine, sideIndex: 1, connected: false);

        AdvanceFrontlineClockWithoutWorld(engine, 8999);
        TickUntilCommittedTick(engine, 9000);
        Assert.That(ReadSnapshot(engine, "Outcome"), Is.EqualTo("InProgress"));

        SetHealth(engine, northernCore, 100f);
        SetHealth(engine, southernCore, 950f);
        SetParticipantConnected(engine, sideIndex: 1, connected: true);
        TickUntilCommittedTick(engine, 9001);

        FrontlineMatchResolutionSnapshot resolution = GetFrontlineRuntime(engine).Resolution;
        Assert.Multiple(() =>
        {
            Assert.That(ReadSnapshot(engine, "Outcome"), Is.EqualTo("SideOneVictory"),
                "Health changes after five minutes must not replace the recorded time-limit result.");
            Assert.That(resolution.Reason, Is.EqualTo(FrontlineMatchResolutionReason.Duration));
            Assert.That(resolution.SideOneCoreHealth, Is.EqualTo(900f));
            Assert.That(resolution.SideTwoCoreHealth, Is.EqualTo(800f));
        });
    }

    [Test]
    [Description(
        "Feature: Core destruction outranks a deferred time-limit result\n" +
        "  Given the five-minute core-health result is waiting for a disconnected player\n" +
        "  When either command core is destroyed during the reconnect grace period\n" +
        "  Then the core destruction ends the match immediately instead of using recorded health")]
    public void GivenDeferredTimeLimit_WhenCommandCoreFalls_ThenDestructionEndsMatchImmediately()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        StartMatch(engine);

        Entity northernCore = FindNamed(engine.World, "Northern Command Core");
        Entity southernCore = FindNamed(engine.World, "Southern Command Core");
        SetHealth(engine, northernCore, 900f);
        SetHealth(engine, southernCore, 800f);
        AdvanceFrontlineClockWithoutWorld(engine, 8990);
        SetParticipantConnected(engine, sideIndex: 1, connected: false);

        AdvanceFrontlineClockWithoutWorld(engine, 8999);
        TickUntilCommittedTick(engine, 9000);
        Assert.That(ReadSnapshot(engine, "Outcome"), Is.EqualTo("InProgress"));

        SetHealth(engine, northernCore, 0f);
        TickUntilCommittedTick(engine, 9001);

        Assert.That(ReadSnapshot(engine, "Outcome"), Is.EqualTo("SideTwoVictory"),
            "Command-core destruction must outrank the deferred five-minute health snapshot.");
    }

    [Test]
    [Description(
        "Feature: First-time player guidance\n" +
        "  Given a player enters the Frontline duel for the first time\n" +
        "  When the battlefield HUD appears\n" +
        "  Then it explains ready state, the objective, gathering, training, and attacking without showing network telemetry")]
    public void GivenNewPlayer_WhenHudAppears_ThenItExplainsTheBattleWithoutNetworkTelemetry()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        Tick(engine, 1);

        ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
            ?? throw new InvalidOperationException("ScreenOverlayBuffer service is missing.");
        string hud = string.Join("\n", ReadOverlayText(overlay));

        Assert.That(hud, Does.Contain("DESTROY THE ENEMY COMMAND CORE"));
        Assert.That(hud, Does.Contain("ONE SCREEN DUEL"));
        Assert.That(hud, Does.Contain("Waiting for both commanders"));
        Assert.That(hud, Does.Contain("North: NOT READY"));
        Assert.That(hud, Does.Contain("Press F5 when ready"));
        Assert.That(hud, Does.Contain("1  Harvesters: right-click a crystal field"));
        Assert.That(hud, Does.Contain("2  Command core: press Q to train infantry"));
        Assert.That(hud, Does.Contain("3  Infantry: right-click an enemy or core"));
        Assert.That(hud, Does.Not.Match("(?i)packet|latency|ping|network tick|queue depth|snapshot id|ack"));

        FrontlineHudLayoutConfig layout = GetFrontlineRuntime(engine).Config.Hud.Layout;
        Assert.Multiple(() =>
        {
            Assert.That(layout.Height, Is.LessThanOrEqualTo(160), "The first-time HUD must leave the battlefield visible.");
            Assert.That(layout.InstructionColumnX, Is.GreaterThanOrEqualTo(280));
            Assert.That(layout.InstructionColumnX, Is.LessThan(layout.Width - 280));
            Assert.That(layout.InstructionColumnX, Is.GreaterThanOrEqualTo(440), "Lobby side status and instructions need separate readable columns.");
        });
    }

    [Test]
    [Description(
        "Feature: Reusable fair battlefield data\n" +
        "  Given a creator changes player ids, teams, or spawn positions\n" +
        "  When the match config and map are loaded\n" +
        "  Then mirrored forces and positions come from data files instead of hardcoded gameplay coordinates")]
    public void GivenAuthoredBattleData_WhenFilesAreInspected_ThenSidesAndPositionsHaveOneDataSource()
    {
        string repoRoot = FindRepoRoot();
        string modRoot = Path.Combine(repoRoot, "mods", "showcases", "rts_multiplayer_frontline", "RtsMultiplayerFrontlineMod");
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Path.Combine(modRoot, "assets", "RtsMultiplayerFrontlineConfig.json")));
        using JsonDocument map = JsonDocument.Parse(File.ReadAllText(Path.Combine(modRoot, "assets", "Maps", "rts_duel_v1.json")));
        using JsonDocument cameras = JsonDocument.Parse(File.ReadAllText(Path.Combine(modRoot, "assets", "Configs", "Camera", "virtual_cameras.json")));
        using JsonDocument templates = JsonDocument.Parse(File.ReadAllText(Path.Combine(modRoot, "assets", "Entities", "templates.json")));

        JsonElement sides = config.RootElement.GetProperty("sides");
        Assert.That(sides.GetArrayLength(), Is.EqualTo(2));

        JsonElement entities = map.RootElement.GetProperty("Entities");
        Assert.That(CountMapEntities(entities, "rts_frontline_harvester", sideIndex: 0), Is.EqualTo(2));
        Assert.That(CountMapEntities(entities, "rts_frontline_harvester", sideIndex: 1), Is.EqualTo(2));
        Assert.That(CountMapEntities(entities, "rts_frontline_infantry", sideIndex: 0), Is.EqualTo(2));
        Assert.That(CountMapEntities(entities, "rts_frontline_infantry", sideIndex: 1), Is.EqualTo(2));
        Assert.That(ReadTemplateBaseAttribute(templates.RootElement, "rts_frontline_core", "Crystals"), Is.EqualTo(40));
        int northCoreX = FindMapEntityX(entities, "Northern Command Core");
        int southCoreX = FindMapEntityX(entities, "Southern Command Core");
        int centerX = map.RootElement.GetProperty("DefaultCamera").GetProperty("TargetXCm").GetInt32();
        Assert.That(northCoreX + southCoreX, Is.EqualTo(centerX * 2));
        JsonElement openingCamera = map.RootElement.GetProperty("DefaultCamera");
        JsonElement frontlineCamera = cameras.RootElement.EnumerateArray()
            .Single(candidate => candidate.GetProperty("id").GetString() == openingCamera.GetProperty("VirtualCameraId").GetString());
        Assert.Multiple(() =>
        {
            Assert.That(openingCamera.GetProperty("DistanceCm").GetInt32(), Is.GreaterThan(0));
            Assert.That(openingCamera.GetProperty("FovYDeg").GetInt32(), Is.InRange(1, 179));
            Assert.That(frontlineCamera.GetProperty("panMode").GetString(), Is.EqualTo("Keyboard"));
            Assert.That(frontlineCamera.GetProperty("enableGrabDrag").GetBoolean(), Is.True);
            Assert.That(frontlineCamera.GetProperty("targetHeightMode").GetString(), Is.EqualTo("VisualHeightmap"));
            JsonElement commandUi = map.RootElement.GetProperty("Metadata").GetProperty("rts.commandSourceUi");
            Assert.That(commandUi.GetProperty("cameraFocusDistanceCm").GetInt32(), Is.EqualTo(5200));
            Assert.That(commandUi.GetProperty("cameraFocusFovYDeg").GetInt32(), Is.EqualTo(46));
            Assert.That(commandUi.GetProperty("cameraFocusTowardDefaultTargetCm").GetInt32(), Is.EqualTo(1800));
            Assert.That(commandUi.GetProperty("toolbarVisible").GetBoolean(), Is.False);
            Assert.That(commandUi.GetProperty("skillBarVisible").GetBoolean(), Is.False);
            Assert.That(commandUi.GetProperty("orderMonitor").GetProperty("visible").GetBoolean(), Is.False);
        });

        string[] sourceFiles = Directory.GetFiles(modRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
        string source = string.Join("\n", sourceFiles.Select(File.ReadAllText));
        string authoredPositionSource = string.Join(
            "\n",
            sourceFiles
                .Where(path => !string.Equals(Path.GetFileName(path), "FrontlineReplication.cs", StringComparison.Ordinal))
                .Select(File.ReadAllText));
        Assert.That(source, Does.Not.Match(@"PlayerId\s*=\s*[12]\b"));
        Assert.That(source, Does.Not.Match(@"TeamId\s*=\s*[12]\b"));
        Assert.That(authoredPositionSource, Does.Not.Match(@"WorldPositionCm\s*\.\s*FromCm"));
        Assert.That(
            Regex.Matches(
                source,
                @"\b(7000|8200|9300|11200|18800|20700|21800|23000|13000|13800|14200|14400|15600|15800|16200|17000)\b"),
            Is.Empty);
    }

    [Test]
    [Description(
        "Feature: Visible opening battlefield\n" +
        "  Given a player enters the Frontline duel for the first time\n" +
        "  When the default and seat-focused 16:9 battlefield cameras appear\n" +
        "  Then both players' starting units and crystal fields stay inside the same screen with a readable margin")]
    public void GivenOpeningBattlefield_WhenCamerasAppear_ThenBothArmiesFitInOneScreen()
    {
        string mapPath = Path.Combine(
            FindRepoRoot(),
            "mods",
            "showcases",
            "rts_multiplayer_frontline",
            "RtsMultiplayerFrontlineMod",
            "assets",
            "Maps",
            "rts_duel_v1.json");
        using JsonDocument map = JsonDocument.Parse(File.ReadAllText(mapPath));

        JsonElement entities = map.RootElement.GetProperty("Entities");
        JsonElement camera = map.RootElement.GetProperty("DefaultCamera");
        Vector2 defaultTargetCm = new(
            camera.GetProperty("TargetXCm").GetInt32(),
            camera.GetProperty("TargetYCm").GetInt32());
        Vector2[] openingPositions = ReadOpeningBattlefieldPositions(entities).ToArray();
        Assert.That(openingPositions, Has.Length.EqualTo(12));

        AssertCameraFitsPositions(
            openingPositions,
            defaultTargetCm,
            camera.GetProperty("DistanceCm").GetSingle(),
            camera.GetProperty("FovYDeg").GetSingle(),
            "The neutral opening camera must show the complete one-screen duel before seat-specific focus is available.");

        JsonElement commandUi = map.RootElement
            .GetProperty("Metadata")
            .GetProperty("rts.commandSourceUi");
        float focusTowardDefaultCm = commandUi.GetProperty("cameraFocusTowardDefaultTargetCm").GetSingle();
        float focusDistanceCm = commandUi.GetProperty("cameraFocusDistanceCm").GetSingle();
        float focusFovYDeg = commandUi.GetProperty("cameraFocusFovYDeg").GetSingle();
        foreach (string coreName in new[] { "Northern Command Core", "Southern Command Core" })
        {
            Vector2 corePosition = FindMapEntityPosition(entities, coreName);
            Vector2 focusTarget = ResolveCommandFocusTarget(corePosition, defaultTargetCm, focusTowardDefaultCm);
            AssertCameraFitsPositions(
                openingPositions,
                focusTarget,
                focusDistanceCm,
                focusFovYDeg,
                $"The {coreName} command-seat focus must still show both armies in one screen.");
        }
    }

    [Test]
    [Description(
        "Feature: Readable command-seat focus\n" +
        "  Given the opening overview shows both Frontline armies\n" +
        "  When the local command seat focuses its first controllable unit\n" +
        "  Then the camera returns to the authored close tactical framing instead of keeping the overview field of view")]
    public void GivenOpeningOverview_WhenLocalSeatFocuses_ThenAuthoredCloseFramingIsRestored()
    {
        using GameEngine engine = CreateStartedEngine();
        engine.LoadMap(MapId);

        PlayerEntityLookup players = engine.GetService(CoreServiceKeys.PlayerEntityLookup)
            ?? throw new InvalidOperationException("PlayerEntityLookup missing.");
        Entity localPlayer = players.Get(1);
        Assert.That(engine.World.IsAlive(localPlayer), Is.True);
        ClientLocalSeatAccess.RequireRegistry(engine).ReplaceAll(new[]
        {
            new ClientLocalSeat("seat.0")
            {
                PossessedPlayerId = 1,
                PossessedRep = localPlayer,
            },
        });

        using var commandPanel = new RtsCommandSourceCommandPanelSystem(engine);
        commandPanel.Update(DeltaTime);

        CameraPoseRequest cameraRequest = engine.GetService(CoreServiceKeys.CameraPoseRequest);

        Assert.Multiple(() =>
        {
            Assert.That(cameraRequest.DistanceCm, Is.EqualTo(5200f).Within(0.01f));
            Assert.That(cameraRequest.FovYDeg, Is.EqualTo(46f).Within(0.01f));
        });
    }

    [Test]
    [Description(
        "Feature: Opening forces have one authoring source\n" +
        "  Given starting crystals and unit instances are authored by the map and its templates\n" +
        "  When an obsolete duplicate opening field is added to match config\n" +
        "  Then config loading fails instead of silently ignoring the conflicting value")]
    public void GivenLegacyOpeningFields_WhenConfigLoads_ThenConflictingDuplicatesAreRejected()
    {
        string path = Path.Combine(
            FindRepoRoot(),
            "mods",
            "showcases",
            "rts_multiplayer_frontline",
            "RtsMultiplayerFrontlineMod",
            "assets",
            "RtsMultiplayerFrontlineConfig.json");

        AssertLegacyOpeningFieldRejected(path, root => root["startingCrystals"] = 99);
        AssertLegacyOpeningFieldRejected(path, root =>
            root["sides"]!.AsArray()[0]!.AsObject()["initialHarvesterCount"] = 3);
        AssertLegacyOpeningFieldRejected(path, root =>
            root["sides"]!.AsArray()[1]!.AsObject()["initialInfantryCount"] = 4);
    }

    [Test]
    [Description(
        "Feature: Fair opening is validated from the loaded battlefield\n" +
        "  Given the authored map gives one commander different starting crystals\n" +
        "  When the opening contract is checked\n" +
        "  Then the match fails before play instead of accepting an unfair start")]
    public void GivenMismatchedMapCrystals_WhenOpeningIsValidated_ThenMatchFailsExplicitly()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        FrontlineConfig config = GetFrontlineConfig(engine);
        Entity southernCore = FindNamed(engine.World, "Southern Command Core");
        int crystalsId = RequireAttribute(config.CrystalAttribute);
        ref AttributeBuffer attributes = ref engine.World.Get<AttributeBuffer>(southernCore);
        attributes.SetCurrent(crystalsId, attributes.GetCurrent(crystalsId) + 1f);

        Assert.That(
            () => FrontlineOpeningAuthoring.Validate(engine, config),
            Throws.InvalidOperationException.With.Message.Contains("must be mirrored"));
    }

    [Test]
    [Description(
        "Feature: Fair opening is validated from the loaded battlefield\n" +
        "  Given one side has fewer map-authored harvesters than the other\n" +
        "  When the opening contract is checked\n" +
        "  Then the match fails before play instead of trusting a stale config count")]
    public void GivenMismatchedMapUnitCounts_WhenOpeningIsValidated_ThenMatchFailsExplicitly()
    {
        using GameEngine engine = CreateStartedEngine();
        LoadMap(engine);
        FrontlineConfig config = GetFrontlineConfig(engine);
        Entity southernHarvester = FindNamed(engine.World, "Southern Harvester A");
        engine.World.Remove<FrontlineHarvester>(southernHarvester);

        Assert.That(
            () => FrontlineOpeningAuthoring.Validate(engine, config),
            Throws.InvalidOperationException.With.Message.Contains("must be mirrored"));
    }

    private static void AssertLegacyOpeningFieldRejected(string path, Action<JsonObject> mutate)
    {
        JsonObject root = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException("RTS Frontline config JSON is empty.");
        mutate(root);

        Assert.That(
            () => FrontlineConfig.Load(root),
            Throws.TypeOf<JsonException>(),
            "Obsolete opening fields must never be accepted and ignored.");
    }

    private static FrontlineConfig GetFrontlineConfig(GameEngine engine)
    {
        return GetFrontlineRuntime(engine).Config;
    }

    private static FrontlineRuntime GetFrontlineRuntime(GameEngine engine) =>
        engine.GlobalContext.TryGetValue(RuntimeKey, out object? value) && value is FrontlineRuntime runtime
            ? runtime
            : throw new InvalidOperationException("RTS Frontline runtime is unavailable.");

    private static int CountMapEntities(JsonElement entities, string templateId, int sideIndex)
    {
        int count = 0;
        foreach (JsonElement entity in entities.EnumerateArray())
        {
            if (entity.GetProperty("Template").GetString() != templateId ||
                !entity.TryGetProperty("Overrides", out JsonElement overrides) ||
                !overrides.TryGetProperty("FrontlineParticipant", out JsonElement participant) ||
                participant.GetProperty("SideIndex").GetInt32() != sideIndex)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private static IEnumerable<string> FlattenBindingPaths(IEnumerable<InputBindingDef>? bindings)
    {
        if (bindings == null)
        {
            yield break;
        }

        foreach (InputBindingDef binding in bindings)
        {
            if (!string.IsNullOrWhiteSpace(binding.Path))
            {
                yield return binding.Path;
            }

            foreach (string path in FlattenBindingPaths(binding.CompositeParts))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<Vector2> ReadOpeningBattlefieldPositions(JsonElement entities)
    {
        foreach (JsonElement entity in entities.EnumerateArray())
        {
            string? template = entity.GetProperty("Template").GetString();
            if (template is not "rts_frontline_core" and
                not "rts_frontline_harvester" and
                not "rts_frontline_infantry" and
                not "rts_frontline_crystal_node")
            {
                continue;
            }

            if (!entity.TryGetProperty("Overrides", out JsonElement overrides) ||
                !overrides.TryGetProperty("WorldPositionCm", out JsonElement position))
            {
                throw new InvalidOperationException($"Opening entity template '{template}' must author WorldPositionCm.");
            }

            JsonElement value = position.GetProperty("Value");
            yield return new Vector2(
                value.GetProperty("X").GetInt32(),
                value.GetProperty("Y").GetInt32());
        }
    }

    private static Vector2 ResolveCommandFocusTarget(
        Vector2 commandSourceCm,
        Vector2 defaultTargetCm,
        float focusTowardDefaultCm)
    {
        Vector2 towardDefault = defaultTargetCm - commandSourceCm;
        if (focusTowardDefaultCm <= 0f)
        {
            return commandSourceCm;
        }

        Assert.That(towardDefault.LengthSquared(), Is.GreaterThan(0f),
            "Command-source focus must have a non-zero direction toward the default opening camera target.");
        return commandSourceCm + Vector2.Normalize(towardDefault) * focusTowardDefaultCm;
    }

    private static void AssertCameraFitsPositions(
        IReadOnlyList<Vector2> positions,
        Vector2 targetCm,
        float distanceCm,
        float fovYDeg,
        string because)
    {
        float requiredHalfWidthCm = 0f;
        float requiredHalfHeightCm = 0f;
        for (int i = 0; i < positions.Count; i++)
        {
            requiredHalfWidthCm = MathF.Max(requiredHalfWidthCm, MathF.Abs(positions[i].X - targetCm.X));
            requiredHalfHeightCm = MathF.Max(requiredHalfHeightCm, MathF.Abs(positions[i].Y - targetCm.Y));
        }

        float verticalFovRadians = fovYDeg * MathF.PI / 180f;
        float verticalHalfHeightCm = distanceCm * MathF.Tan(verticalFovRadians * 0.5f);
        float horizontalHalfWidthCm = verticalHalfHeightCm * (16f / 9f);

        Assert.Multiple(() =>
        {
            Assert.That(horizontalHalfWidthCm, Is.GreaterThanOrEqualTo(requiredHalfWidthCm * 1.1f), because);
            Assert.That(verticalHalfHeightCm, Is.GreaterThanOrEqualTo(requiredHalfHeightCm * 1.1f), because);
        });
    }

    private static int ReadTemplateBaseAttribute(
        JsonElement templates,
        string templateId,
        string attribute)
    {
        foreach (JsonElement template in templates.EnumerateArray())
        {
            if (template.GetProperty("id").GetString() == templateId)
            {
                return template
                    .GetProperty("components")
                    .GetProperty("AttributeBuffer")
                    .GetProperty("base")
                    .GetProperty(attribute)
                    .GetInt32();
            }
        }

        throw new InvalidOperationException($"Template '{templateId}' was not found.");
    }

    private static GameEngine CreateStartedEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods),
            Path.Combine(repoRoot, "assets"));
        InstallDummyInput(engine);

        var uiRoot = new UIRoot(new SkiaUiRenderer());
        uiRoot.Resize(1280f, 720f);
        engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
        engine.SetService(CoreServiceKeys.ViewController, new StubViewController(1280f, 720f));
        engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)new SkiaTextMeasurer());
        engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)new SkiaImageSizeProvider());
        engine.Start();
        return engine;
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

    private static NetworkRuntimeConfig LoadNetworkProfile()
    {
        string path = Path.Combine(
            FindRepoRoot(),
            "mods",
            "showcases",
            "rts_multiplayer_frontline",
            "RtsMultiplayerFrontlineNetworkedMod",
            "assets",
            "game.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        NetworkRuntimeConfig profile = document.RootElement
            .GetProperty("networking")
            .Deserialize<NetworkRuntimeConfig>(options)
            ?? throw new InvalidOperationException("RTS Frontline network profile is empty.");
        profile.Validate();
        return profile;
    }

    private static void LoadMap(GameEngine engine)
    {
        engine.LoadMap(MapId);
        Tick(engine, 5);
        Assert.That(engine.TriggerManager.Errors, Is.Empty);
        Assert.That(engine.GlobalContext.ContainsKey(RuntimeKey), Is.True,
            "The Frontline runtime should publish its platform-neutral projection hook.");
    }

    private static void AssertSide(World world, int playerId, int teamId, string namePrefix, float expectedCrystals)
    {
        Entity core = FindNamed(world, $"{namePrefix} Command Core");
        Assert.That(world.Get<PlayerOwner>(core).PlayerId, Is.EqualTo(playerId));
        Assert.That(world.Get<Team>(core).Id, Is.EqualTo(teamId));
        Assert.That(ReadAttribute(world, core, RequireAttribute("Crystals")), Is.EqualTo(expectedCrystals));
        Assert.That(CountNamed(world, $"{namePrefix} Harvester"), Is.EqualTo(2));
        Assert.That(CountNamed(world, $"{namePrefix} Infantry"), Is.EqualTo(2));
    }

    private static void AssertReadablePresentation(
        World world,
        PresenterDefinitionRegistry definitions,
        PresenterEntityRuntime performers,
        string performerKey,
        string entityNameFragment)
    {
        int definitionId = definitions.GetId(performerKey);
        Assert.That(
            definitionId,
            Is.GreaterThan(0),
            $"Frontline must declare the role-specific performer '{performerKey}'.");

        int entityCount = 0;
        var query = new QueryDescription().WithAll<Name, VisualTransform>();
        foreach (ref Chunk chunk in world.Query(in query))
        {
            ReadOnlySpan<Name> names = chunk.GetSpan<Name>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (!names[index].Value.Contains(entityNameFragment, StringComparison.Ordinal))
                {
                    continue;
                }

                entityCount++;
                Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref first, index);
                Assert.That(
                    performers.GetActiveByOwnerDefinition(definitionId, entity),
                    Has.Count.EqualTo(1),
                    $"'{names[index].Value}' must own exactly one '{performerKey}' root performer.");
            }
        }

        Assert.That(entityCount, Is.GreaterThan(0), $"No '{entityNameFragment}' entities were loaded for presentation validation.");
    }

    private static void AssertCombatEntitiesGrounded(World world, IContinuousHeightmap heightmap)
    {
        int combatEntityCount = 0;
        var query = new QueryDescription()
            .WithAll<WorldPositionCm, VisualTransform, ContinuousHeightmapSampleState>()
            .WithAny<FrontlineCore, FrontlineHarvester, FrontlineInfantry, FrontlineCrystalNode>();
        foreach (ref Chunk chunk in world.Query(in query))
        {
            ReadOnlySpan<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
            ReadOnlySpan<VisualTransform> visuals = chunk.GetSpan<VisualTransform>();
            ReadOnlySpan<ContinuousHeightmapSampleState> samples = chunk.GetSpan<ContinuousHeightmapSampleState>();
            foreach (int index in chunk)
            {
                Vector2 worldCm = positions[index].Value.ToVector2();
                Assert.That(
                    heightmap.TrySampleHeightCm(worldCm.X, worldCm.Y, out float expectedHeightCm),
                    Is.True,
                    "Every opening combat entity must lie inside the battlefield visual heightmap.");
                byte sampled = samples[index].Sampled;
                float visualY = visuals[index].Position.Y;
                Assert.Multiple(() =>
                {
                    Assert.That(sampled, Is.EqualTo(1), "Terrain grounding must complete before the battle is shown.");
                    Assert.That(visualY, Is.EqualTo(expectedHeightCm * 0.01f).Within(0.01f));
                    Assert.That(visualY, Is.GreaterThan(0.1f), "The opening unit must stand above the shoreline surface instead of remaining at Y=0.");
                });
                combatEntityCount++;
            }
        }

        Assert.That(combatEntityCount, Is.EqualTo(12), "The opening battlefield must ground all twelve combat entities.");
    }

    private static void EnqueueCastAbility(GameEngine engine, Entity core, int playerId, int slot)
    {
        EnqueueOrder(
            engine,
            RequireOrderType(engine, "castAbility"),
            playerId,
            core,
            core,
            new OrderArgs { I0 = slot });
    }

    private static OrderSubmitResult SubmitTraining(
        GameEngine engine,
        Entity core,
        int playerId,
        int slot,
        out Order order)
    {
        OrderBufferSystem orders = engine.GetService(CoreServiceKeys.OrderBufferSystem)
            ?? throw new InvalidOperationException("OrderBufferSystem service is missing.");
        order = CreateTrainingOrder(engine, core, playerId, slot);
        return SubmitOrderInsideLogicStep(engine, orders, core, in order);
    }

    private static void SubmitTrainingBatch(
        GameEngine engine,
        Entity core,
        int playerId,
        int slot,
        out OrderSubmitResult firstResult,
        out Order first,
        out OrderSubmitResult secondResult,
        out Order second)
    {
        OrderBufferSystem orders = engine.GetService(CoreServiceKeys.OrderBufferSystem)
            ?? throw new InvalidOperationException("OrderBufferSystem service is missing.");
        OrderAdmissionResultBuffer admission = engine.GetService(CoreServiceKeys.OrderAdmissionResultBuffer)
            ?? throw new InvalidOperationException("OrderAdmissionResultBuffer service is missing.");
        first = CreateTrainingOrder(engine, core, playerId, slot);
        second = CreateTrainingOrder(engine, core, playerId, slot);
        admission.BeginLogicStep();
        try
        {
            firstResult = orders.SubmitOrder(core, in first);
            secondResult = orders.SubmitOrder(core, in second);
        }
        finally
        {
            if (admission.EntityIntakeOpen)
            {
                admission.EndEntityIntake();
            }

            if (admission.LogicStepActive)
            {
                admission.EndLogicStep();
            }
        }
    }

    private static Order CreateTrainingOrder(GameEngine engine, Entity core, int playerId, int slot)
    {
        OrderQueue queue = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("OrderQueue service is missing.");
        var order = new Order
        {
            OrderTypeId = RequireOrderType(engine, "castAbility"),
            PlayerId = playerId,
            Actor = core,
            Target = core,
            Args = new OrderArgs { I0 = slot },
            SubmitMode = OrderSubmitMode.PersistentQueued,
        };
        queue.EnsureOrderId(ref order);
        return order;
    }

    private static OrderSubmitResult SubmitOrderInsideLogicStep(
        GameEngine engine,
        OrderBufferSystem orders,
        Entity actor,
        in Order order)
    {
        OrderAdmissionResultBuffer admission = engine.GetService(CoreServiceKeys.OrderAdmissionResultBuffer)
            ?? throw new InvalidOperationException("OrderAdmissionResultBuffer service is missing.");
        admission.BeginLogicStep();
        try
        {
            return orders.SubmitOrder(actor, in order);
        }
        finally
        {
            if (admission.EntityIntakeOpen)
            {
                admission.EndEntityIntake();
            }

            if (admission.LogicStepActive)
            {
                admission.EndLogicStep();
            }
        }
    }

    private static void EnqueueMove(GameEngine engine, Entity actor, int playerId, int x, int y)
    {
        EnqueueOrder(
            engine,
            RequireOrderType(engine, "moveTo"),
            playerId,
            actor,
            Entity.Null,
            OrderArgs.CreateSingleWorldCm(new Vector3(x, 0f, y)));
    }

    private static void EnqueueOrder(
        GameEngine engine,
        int orderTypeId,
        int playerId,
        Entity actor,
        Entity target,
        OrderArgs args = default)
    {
        var queue = engine.GetService(CoreServiceKeys.OrderQueue) as OrderQueue
            ?? throw new InvalidOperationException("OrderQueue service is missing.");
        var order = new Order
        {
            OrderTypeId = orderTypeId,
            PlayerId = playerId,
            Actor = actor,
            Target = target,
            Args = args,
            SubmitMode = OrderSubmitMode.Immediate,
        };

        Assert.That(queue.TryEnqueueAssigned(ref order), Is.True, "The player's order should enter the formal order queue.");
    }

    private static int RequireOrderType(GameEngine engine, string key)
    {
        OrderTypeRegistry registry = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("OrderTypeRegistry service is missing.");
        int id = registry.GetId(key);
        Assert.That(id, Is.GreaterThan(0), $"Order type '{key}' must be configured.");
        return id;
    }

    private static int RequireAttribute(string name)
    {
        int id = AttributeRegistry.GetId(name);
        Assert.That(id, Is.GreaterThanOrEqualTo(0), $"Attribute '{name}' must be configured.");
        return id;
    }

    private static TagOps RequireTagOps(GameEngine engine) =>
        engine.GetService(CoreServiceKeys.TagOps)
        ?? throw new InvalidOperationException("TagOps service is missing.");

    private static Entity FindNamed(World world, string name)
    {
        Entity found = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity entity, ref Name component) =>
        {
            if (found == Entity.Null && string.Equals(component.Value, name, StringComparison.Ordinal))
            {
                found = entity;
            }
        });

        return found != Entity.Null
            ? found
            : throw new InvalidOperationException($"Entity '{name}' was not found.");
    }

    private static int CountTemplateEntities(GameEngine engine, string templateId)
    {
        int templateKeyId = engine.MapLoader.EntityTemplateKeys.GetId(templateId);
        Assert.That(templateKeyId, Is.GreaterThan(0), $"Template key '{templateId}' must be registered.");
        int count = 0;
        var query = new QueryDescription().WithAll<EntityTemplateKeyRef>();
        engine.World.Query(in query, (ref EntityTemplateKeyRef keyRef) =>
        {
            if (keyRef.TemplateKeyId == templateKeyId)
            {
                count++;
            }
        });
        return count;
    }

    private static List<Entity> FindTemplateEntities(GameEngine engine, string templateId)
    {
        int templateKeyId = engine.MapLoader.EntityTemplateKeys.GetId(templateId);
        Assert.That(templateKeyId, Is.GreaterThan(0), $"Template key '{templateId}' must be registered.");
        var entities = new List<Entity>();
        var query = new QueryDescription().WithAll<EntityTemplateKeyRef>();
        engine.World.Query(in query, (Entity entity, ref EntityTemplateKeyRef keyRef) =>
        {
            if (keyRef.TemplateKeyId == templateKeyId)
            {
                entities.Add(entity);
            }
        });
        return entities;
    }

    private static Entity FindRuntimeTemplateEntity(GameEngine engine, string templateId)
    {
        int templateKeyId = engine.MapLoader.EntityTemplateKeys.GetId(templateId);
        Assert.That(templateKeyId, Is.GreaterThan(0), $"Template key '{templateId}' must be registered.");
        Entity found = Entity.Null;
        var query = new QueryDescription().WithAll<EntityTemplateKeyRef, Name>();
        engine.World.Query(in query, (Entity entity, ref EntityTemplateKeyRef keyRef, ref Name name) =>
        {
            if (found == Entity.Null &&
                keyRef.TemplateKeyId == templateKeyId &&
                string.Equals(name.Value, "Frontline Infantry", StringComparison.Ordinal))
            {
                found = entity;
            }
        });

        return found != Entity.Null
            ? found
            : throw new InvalidOperationException($"Runtime-created template entity '{templateId}' was not found.");
    }

    private static int CountNamed(World world, string fragment)
    {
        int count = 0;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity _, ref Name name) =>
        {
            if (name.Value.Contains(fragment, StringComparison.Ordinal))
            {
                count++;
            }
        });
        return count;
    }

    private static float ReadAttribute(World world, Entity entity, int attributeId) =>
        world.Get<AttributeBuffer>(entity).GetCurrent(attributeId);

    private static void SetHealth(GameEngine engine, Entity entity, float health) =>
        AttributeMutationOps.SetCurrent(
            engine.World,
            entity,
            RequireAttribute("Health"),
            health,
            RequireTagOps(engine));

    private static float DistanceCm(in WorldPositionCm a, in WorldPositionCm b)
    {
        var left = a.ToWorldCmInt2();
        var right = b.ToWorldCmInt2();
        long dx = left.X - (long)right.X;
        long dy = left.Y - (long)right.Y;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    private static object ReadSnapshot(GameEngine engine, string propertyName)
    {
        object runtime = engine.GlobalContext[RuntimeKey]
            ?? throw new InvalidOperationException("Frontline runtime projection is missing.");
        object snapshot = runtime.GetType().GetProperty("Snapshot", BindingFlags.Instance | BindingFlags.Public)?.GetValue(runtime)
            ?? throw new MissingMemberException(runtime.GetType().FullName, "Snapshot");
        object value = snapshot.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(snapshot)
            ?? throw new MissingMemberException(snapshot.GetType().FullName, propertyName);
        return value is Enum ? value.ToString()! : value;
    }

    private static void SetParticipantConnected(GameEngine engine, int sideIndex, bool connected)
    {
        object runtime = engine.GlobalContext[RuntimeKey]
            ?? throw new InvalidOperationException("Frontline runtime projection is missing.");
        MethodInfo method = runtime.GetType().GetMethod("SetParticipantConnected", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(runtime.GetType().FullName, "SetParticipantConnected");
        method.Invoke(runtime, new object[] { sideIndex, connected });
    }

    private static void SetParticipantReady(GameEngine engine, int sideIndex, bool ready)
    {
        object runtime = engine.GlobalContext[RuntimeKey]
            ?? throw new InvalidOperationException("Frontline runtime projection is missing.");
        MethodInfo method = runtime.GetType().GetMethod("SetParticipantReady", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(runtime.GetType().FullName, "SetParticipantReady");
        method.Invoke(runtime, new object[] { sideIndex, ready });
    }

    private static void StartMatch(GameEngine engine)
    {
        SetParticipantReady(engine, sideIndex: 0, ready: true);
        SetParticipantReady(engine, sideIndex: 1, ready: true);
        TickUntil(engine, () => Equals(ReadSnapshot(engine, "Phase"), "InProgress"), 200,
            "Both ready players should enter battle after three seconds.");
        Assert.That(ReadSnapshot(engine, "CommittedTick"), Is.EqualTo(0));
    }

    private static IReadOnlyList<string> ReadOverlayText(ScreenOverlayBuffer overlay)
    {
        var result = new List<string>();
        foreach (ref readonly ScreenOverlayItem item in overlay.GetSpan())
        {
            if (item.Kind != ScreenOverlayItemKind.Text)
            {
                continue;
            }

            string? text = overlay.GetString(item.StringId);
            if (!string.IsNullOrEmpty(text))
            {
                result.Add(text);
            }
        }
        return result;
    }

    private static int FindMapEntityX(JsonElement entities, string name)
    {
        return (int)FindMapEntityPosition(entities, name).X;
    }

    private static Vector2 FindMapEntityPosition(JsonElement entities, string name)
    {
        foreach (JsonElement entity in entities.EnumerateArray())
        {
            if (!entity.TryGetProperty("Overrides", out JsonElement overrides) ||
                !overrides.TryGetProperty("Name", out JsonElement authoredName))
            {
                continue;
            }
            if (string.Equals(authoredName.GetProperty("Value").GetString(), name, StringComparison.Ordinal))
            {
                JsonElement position = overrides.GetProperty("WorldPositionCm").GetProperty("Value");
                return new Vector2(
                    position.GetProperty("X").GetInt32(),
                    position.GetProperty("Y").GetInt32());
            }
        }

        throw new InvalidOperationException($"Map entity '{name}' was not found.");
    }

    private static void Tick(GameEngine engine, int frames)
    {
        var stepPolicy = engine.GetService(CoreServiceKeys.GasClockStepPolicy);
        for (int i = 0; i < frames; i++)
        {
            if (stepPolicy.Mode == GasStepMode.Manual)
            {
                stepPolicy.RequestStep(1);
            }
            engine.Tick(DeltaTime);
        }
    }

    private static void TickUntil(GameEngine engine, Func<bool> condition, int maxFrames, string because)
    {
        for (int i = 0; i < maxFrames && !condition(); i++)
        {
            Tick(engine, 1);
        }
        Assert.That(condition(), Is.True, because);
    }

    private static void AdvanceCommittedTicks(GameEngine engine, int count)
    {
        int current = (int)ReadSnapshot(engine, "CommittedTick");
        TickUntilCommittedTick(engine, checked(current + count));
    }

    private static void TickUntilCommittedTick(GameEngine engine, int targetTick)
    {
        int frameBudget = checked((targetTick - (int)ReadSnapshot(engine, "CommittedTick")) * 4 + 8);
        TickUntil(
            engine,
            () => (int)ReadSnapshot(engine, "CommittedTick") >= targetTick,
            frameBudget,
            $"The deterministic simulation should commit tick {targetTick}.");
    }

    private static void AdvanceFrontlineClockWithoutWorld(GameEngine engine, int targetTick)
    {
        FrontlineRuntime runtime = GetFrontlineRuntime(engine);
        while (runtime.Snapshot.CommittedTick < targetTick)
        {
            Assert.That(
                runtime.AdvanceFixedTick(),
                Is.True,
                $"The active match should advance to committed tick {targetTick}.");
        }
    }

    private static void TickUntilCountdown(GameEngine engine, int remainingTicks)
    {
        TickUntil(
            engine,
            () => (int)ReadSnapshot(engine, "CountdownRemainingTicks") <= remainingTicks,
            200,
            $"The ready countdown should reach {remainingTicks} ticks remaining.");
    }

    private static void InstallDummyInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        engine.SetService(CoreServiceKeys.InputHandler, new PlayerInputHandler(new NullInputBackend(), inputConfig));
        engine.SetService(CoreServiceKeys.UiCaptured, false);
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
