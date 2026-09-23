using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Orders;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.UI;
using Ludots.UI.Skia;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;
using RtsCncFullShowcaseMod;
using RtsCncFullShowcaseMod.Systems;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
public sealed class RtsCncFullShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "rts_cnc_full";
    private const string ProducerName = "Atlantic Directorate Barracks";
    private const string TrainedUnitName = "Atlantic Directorate Rifle Section";

    [Test]
    public async Task CncFullShowcase_LoadsFiveFactionsOneHundredUnitsAndDataPlane()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "rts-cnc-full-showcase");
        Directory.CreateDirectory(artifactDir);

        var frameTimesMs = new List<double>();
        using GameEngine engine = CreateEngine(repoRoot);
        LoadMap(engine, frameTimesMs);

        AssertContentCounts(engine);
        AssertItemAndGraphRegistries(engine);

        Entity producer = FindEntity(engine.World, ProducerName);
        IEntityCommandPanelSource panelSource = ResolveGasPanelSource(engine);
        var slots = new EntityCommandPanelSlotView[AbilityStateBuffer.CAPACITY];
        int slotCount = panelSource.CopySlots(producer, 0, slots);
        Assert.That(slotCount, Is.EqualTo(4), "The Atlantic barracks should expose four trainable infantry abilities.");
        Assert.That(slots.Take(slotCount).All(static slot => !string.IsNullOrWhiteSpace(slot.DisplayLabel)), Is.True);
        AssertTrainAbilityIsSmartCast(engine, slots[0].AbilityId);

        int creditsId = AttributeRegistry.GetId("Credits");
        float startingCredits = engine.World.Get<AttributeBuffer>(producer).GetCurrent(creditsId);
        int startingRifleCount = CountEntitiesByName(engine.World, TrainedUnitName);
        Entity offTarget = FindEntity(engine.World, "Volkov Union Rifle Section");
        CastAbility(engine, producer, offTarget, slot: 0, OrderSubmitMode.Immediate);
        TickUntil(
            engine,
            frameTimesMs,
            () => CountEntitiesByName(engine.World, TrainedUnitName) == startingRifleCount + 1,
            maxFrames: 180,
            "The barracks should train a fresh rifle section through GAS CreateUnit.");

        float endingCredits = engine.World.Get<AttributeBuffer>(producer).GetCurrent(creditsId);
        Assert.That(endingCredits, Is.EqualTo(startingCredits - 50f).Within(0.01f));

        RtsCncFullDataPlaneSnapshot dataPlane = await VerifyDataPlaneAsync(engine);
        Assert.That(dataPlane.Summary.Factions, Is.EqualTo(5));
        Assert.That(dataPlane.Summary.UnitTypes, Is.EqualTo(100));
        Assert.That(dataPlane.Summary.Producers, Is.EqualTo(25));
        Assert.That(dataPlane.Summary.SupplyItemTypes, Is.EqualTo(5));
        Assert.That(dataPlane.Summary.Graphs, Is.EqualTo(1));
        Assert.That(dataPlane.ActiveFactionId, Is.EqualTo("nile"));
        Assert.That(dataPlane.Units.Select(static unit => unit.Name), Does.Contain(TrainedUnitName));

        File.WriteAllText(
            Path.Combine(artifactDir, "battle-report.md"),
            BuildBattleReport(dataPlane, frameTimesMs, startingCredits, endingCredits),
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(artifactDir, "dataplane-snapshot.json"),
            JsonSerializer.Serialize(dataPlane, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
    }

    [Test]
    public async Task CncFullShowcase_RecordableMatchLoop_ReachesVictoryThroughDataPlaneCommands()
    {
        string repoRoot = FindRepoRoot();
        var frameTimesMs = new List<double>();
        using GameEngine engine = CreateEngine(repoRoot);
        TestContext.Progress.WriteLine("loop: engine created");
        LoadMap(engine, frameTimesMs);
        TestContext.Progress.WriteLine("loop: map loaded");

        using var transport = await AttachDataPlaneSessionAsync(engine);
        TestContext.Progress.WriteLine("loop: dataplane attached");
        await SendDataPlaneCommandAsync(engine, transport, "startHarvest", 11, frameTimesMs);
        TestContext.Progress.WriteLine("loop: startHarvest ack");
        RtsCncFullDataPlaneSnapshot mined = await TickUntilSnapshotAsync(
            engine,
            transport,
            frameTimesMs,
            snapshot => string.Equals(snapshot.Match?.Phase, "ReadyToTrain", StringComparison.Ordinal),
            maxFrames: 420,
            "Mining should bank ore and unlock training.");
        TestContext.Progress.WriteLine($"loop: mined phase={mined.Match!.Phase} ore={mined.Match.Ore}");
        Assert.That(mined.Match!.Ore, Is.GreaterThanOrEqualTo(1100f));

        await SendDataPlaneCommandAsync(engine, transport, "trainArmy", 12, frameTimesMs);
        TestContext.Progress.WriteLine("loop: trainArmy ack");
        RtsCncFullDataPlaneSnapshot trained = await TickUntilSnapshotAsync(
            engine,
            transport,
            frameTimesMs,
            snapshot => string.Equals(snapshot.Match?.Phase, "ReadyToAttack", StringComparison.Ordinal) &&
                        snapshot.Match.UnitsTrained >= 1,
            maxFrames: 420,
            "GAS production should create at least one fresh rifle section.");
        TestContext.Progress.WriteLine($"loop: trained phase={trained.Match!.Phase} units={trained.Match.UnitsTrained}");
        Assert.That(trained.Match!.UnitsTrained, Is.GreaterThanOrEqualTo(1));

        int enemyCountBeforeAttack = CountTeamCombatEntities(engine, teamId: 2);
        Assert.That(enemyCountBeforeAttack, Is.GreaterThan(0));

        await SendDataPlaneCommandAsync(engine, transport, "attackEnemy", 13, frameTimesMs);
        TestContext.Progress.WriteLine("loop: attackEnemy ack");
        RtsCncFullDataPlaneSnapshot victory = await TickUntilSnapshotAsync(
            engine,
            transport,
            frameTimesMs,
            snapshot => snapshot.Match?.Victory == true,
            maxFrames: 900,
            "Combat should destroy every Volkov non-anchor entity and publish victory.");
        TestContext.Progress.WriteLine($"loop: victory phase={victory.Match!.Phase} destroyed={victory.Match.EnemyDestroyed}");

        Tick(engine, 6, frameTimesMs);
        Assert.That(victory.Match!.Victory, Is.True);
        Assert.That(victory.Match.EnemyDestroyed, Is.GreaterThanOrEqualTo(enemyCountBeforeAttack));
        Assert.That(CountTeamCombatEntities(engine, teamId: 2), Is.EqualTo(0));
    }

    private static GameEngine CreateEngine(string repoRoot)
    {
        string assetsRoot = Path.Combine(repoRoot, "assets");
        var modPaths = RepoModPaths.ResolveExplicit(repoRoot, new[]
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "EntityCommandPanelMod",
            "RtsDemoMod",
            "RtsCncFullShowcaseMod",
        });

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

    private static void LoadMap(GameEngine engine, List<double> frameTimesMs)
    {
        engine.LoadMap(MapId);
        Tick(engine, 6, frameTimesMs);
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
    }

    private static void AssertContentCounts(GameEngine engine)
    {
        EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("EntityTemplateKeyRegistry missing.");
        var unitTemplateIds = new HashSet<string>(StringComparer.Ordinal);
        var producerTemplateIds = new HashSet<string>(StringComparer.Ordinal);
        var teamIds = new HashSet<int>();

        var query = new QueryDescription().WithAll<EntityTemplateKeyRef>();
        engine.World.Query(in query, (Entity entity, ref EntityTemplateKeyRef templateKey) =>
        {
            string templateId = templateKeys.GetName(templateKey.TemplateKeyId);
            if (!templateId.StartsWith("rts_cnc_full_", StringComparison.Ordinal))
            {
                return;
            }

            if (engine.World.TryGet(entity, out Team team))
            {
                teamIds.Add(team.Id);
            }

            if (templateId.EndsWith("_producer", StringComparison.Ordinal))
            {
                producerTemplateIds.Add(templateId);
            }
            else if (!templateId.EndsWith("_anchor", StringComparison.Ordinal))
            {
                unitTemplateIds.Add(templateId);
            }
        });

        Assert.That(teamIds.Count, Is.EqualTo(5));
        Assert.That(unitTemplateIds.Count, Is.EqualTo(100));
        Assert.That(producerTemplateIds.Count, Is.EqualTo(25));
    }

    private static void AssertItemAndGraphRegistries(GameEngine engine)
    {
        ItemDefinitionRegistry items = engine.GetService(CoreServiceKeys.ItemDefinitionRegistry)
            ?? throw new InvalidOperationException("ItemDefinitionRegistry missing.");
        Assert.That(items.GetId("rts_cnc_full_ore_canister"), Is.GreaterThan(0));
        Assert.That(items.GetId("rts_cnc_full_power_core"), Is.GreaterThan(0));
        Assert.That(items.GetId("rts_cnc_full_vehicle_kit"), Is.GreaterThan(0));
        Assert.That(items.GetId("rts_cnc_full_airframe_crate"), Is.GreaterThan(0));
        Assert.That(items.GetId("rts_cnc_full_naval_parts"), Is.GreaterThan(0));
        Assert.That(GraphIdRegistry.GetId("Graph.RtsCncFull.RosterTotals"), Is.GreaterThan(0));
    }

    private static async Task<RtsCncFullDataPlaneSnapshot> VerifyDataPlaneAsync(GameEngine engine)
    {
        using var transport = await AttachDataPlaneSessionAsync(engine);

        transport.Receive(new WebUiInboundPacket(
            "rts-cnc-full-test",
            RtsCncFullShowcaseTopicProducer.TopicName,
            WebUiPacketKind.Command,
            WebUiDeliverySemantics.ReliableOrdered,
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                name = "selectFaction",
                clientSeq = 7,
                entityRefs = Array.Empty<object>(),
                payload = new { factionId = "nile" },
            }),
            "application/json",
            RequestId: 3,
            ClientSeq: 7));
        Tick(engine, 2, new List<double>());
        await WaitForKindAsync(transport, WebUiPacketKind.CommandAck);

        WebUiDataPlaneRuntime runtime = engine.GetService(RtsCncFullShowcaseModEntry.DataPlaneRuntimeKey)
            ?? throw new InvalidOperationException("C&C full DataPlane runtime missing.");
        await runtime.PublishTopicAsync(RtsCncFullShowcaseTopicProducer.TopicName);
        await WaitForSnapshotCountAsync(transport, 2);

        WebUiOutboundPacket snapshotPacket = transport.Sent
            .Last(packet => packet.Kind is WebUiPacketKind.Snapshot or WebUiPacketKind.Delta);
        string json = WebUiDataPlaneProtocol.PayloadToString(snapshotPacket.Payload);
        RtsCncFullDataPlaneSnapshot? snapshot = JsonSerializer.Deserialize<RtsCncFullDataPlaneSnapshot>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return snapshot ?? throw new InvalidOperationException("DataPlane snapshot did not deserialize.");
    }

    private static async Task<TestDataTransport> AttachDataPlaneSessionAsync(GameEngine engine)
    {
        WebUiDataPlaneRuntime runtime = engine.GetService(RtsCncFullShowcaseModEntry.DataPlaneRuntimeKey)
            ?? throw new InvalidOperationException("C&C full DataPlane runtime missing.");
        var transport = new TestDataTransport();
        runtime.AttachSession("rts-cnc-full-test", transport);

        transport.Receive(WebUiDataPlaneProtocol.CreateControlPacket(
            "rts-cnc-full-test",
            1,
            "handshake",
            "system",
            new { requiredCapabilities = Array.Empty<string>() }));
        await WaitForPacketsAsync(transport, 1);

        transport.Receive(WebUiDataPlaneProtocol.CreateControlPacket(
            "rts-cnc-full-test",
            2,
            "subscribe",
            RtsCncFullShowcaseTopicProducer.TopicName,
            new { }));
        await WaitForPacketsAsync(transport, 2);
        return transport;
    }

    private static async Task SendDataPlaneCommandAsync(
        GameEngine engine,
        TestDataTransport transport,
        string name,
        int clientSeq,
        List<double> frameTimesMs)
    {
        transport.Receive(new WebUiInboundPacket(
            "rts-cnc-full-test",
            RtsCncFullShowcaseTopicProducer.TopicName,
            WebUiPacketKind.Command,
            WebUiDeliverySemantics.ReliableOrdered,
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                name,
                clientSeq,
                entityRefs = Array.Empty<object>(),
                payload = new { },
            }),
            "application/json",
            RequestId: clientSeq + 100,
            ClientSeq: clientSeq));
        Tick(engine, 2, frameTimesMs);
        await WaitForKindAsync(transport, WebUiPacketKind.CommandAck);
    }

    private static async Task<RtsCncFullDataPlaneSnapshot> TickUntilSnapshotAsync(
        GameEngine engine,
        TestDataTransport transport,
        List<double> frameTimesMs,
        Func<RtsCncFullDataPlaneSnapshot, bool> predicate,
        int maxFrames,
        string because)
    {
        RtsCncFullDataPlaneSnapshot? latest = null;
        for (int i = 0; i < maxFrames; i++)
        {
            if (i % 60 == 0)
            {
                TestContext.Progress.WriteLine($"loop: tickUntil frame={i} latest={latest?.Match?.Phase ?? "<none>"}");
            }

            Tick(engine, 1, frameTimesMs);
            if (TryReadLatestSnapshot(transport, out RtsCncFullDataPlaneSnapshot snapshot))
            {
                latest = snapshot;
                if (predicate(snapshot))
                {
                    return snapshot;
                }
            }

            if (i % 30 == 0)
            {
                await Task.Delay(1);
            }
        }

        Assert.Fail($"{because} Last phase: {latest?.Match?.Phase ?? "<none>"}.");
        throw new InvalidOperationException(because);
    }

    private static bool TryReadLatestSnapshot(TestDataTransport transport, out RtsCncFullDataPlaneSnapshot snapshot)
    {
        WebUiOutboundPacket[] packets = transport.Sent
            .Where(packet => packet.Kind is WebUiPacketKind.Snapshot or WebUiPacketKind.Delta)
            .ToArray();
        if (packets.Length == 0)
        {
            snapshot = null!;
            return false;
        }

        WebUiOutboundPacket packet = packets[^1];
        string json = WebUiDataPlaneProtocol.PayloadToString(packet.Payload);
        snapshot = JsonSerializer.Deserialize<RtsCncFullDataPlaneSnapshot>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return snapshot != null;
    }

    private static void CastAbility(GameEngine engine, Entity actor, Entity target, int slot, OrderSubmitMode submitMode)
    {
        OrderQueue orderQueue = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("OrderQueue service is missing.");
        bool enqueued = orderQueue.TryEnqueue(new Order
        {
            OrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["castAbility"],
            PlayerId = 1,
            Actor = actor,
            Target = target,
            Args = new OrderArgs { I0 = slot },
            SubmitMode = submitMode,
        });

        Assert.That(enqueued, Is.True);
    }

    private static void AssertTrainAbilityIsSmartCast(GameEngine engine, int abilityId)
    {
        AbilityDefinitionRegistry abilities = engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry)
            ?? throw new InvalidOperationException("AbilityDefinitionRegistry service is missing.");
        Assert.That(abilities.TryGet(abilityId, out AbilityDefinition ability), Is.True);
        Assert.That(ability.HasInputBindingOverride, Is.True);
        Assert.That(ability.InputBindingOverride.HasCastModeOverride, Is.True);
        Assert.That(ability.InputBindingOverride.CastModeOverride, Is.EqualTo(InteractionModeType.SmartCast));
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

    private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
    {
        var stepPolicy = engine.GetService(CoreServiceKeys.GasClockStepPolicy);
        for (int i = 0; i < frames; i++)
        {
            if (stepPolicy.Mode == GasStepMode.Manual)
            {
                stepPolicy.RequestStep(1);
            }

            long start = Environment.TickCount64;
            engine.Tick(DeltaTime);
            frameTimesMs.Add(Environment.TickCount64 - start);
        }
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

        return result == Entity.Null
            ? throw new InvalidOperationException($"Missing entity '{entityName}'.")
            : result;
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

    private static int CountTeamCombatEntities(GameEngine engine, int teamId)
    {
        EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("EntityTemplateKeyRegistry missing.");
        int count = 0;
        var query = new QueryDescription().WithAll<Team, EntityTemplateKeyRef>();
        engine.World.Query(in query, (Entity entity, ref Team team, ref EntityTemplateKeyRef templateKey) =>
        {
            if (team.Id != teamId || engine.World.Has<PresentationDestroyPending>(entity))
            {
                return;
            }

            string templateId = templateKeys.GetName(templateKey.TemplateKeyId);
            if (templateId.StartsWith("rts_cnc_full_", StringComparison.Ordinal) &&
                !templateId.EndsWith("_anchor", StringComparison.Ordinal))
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

    private static string BuildBattleReport(
        RtsCncFullDataPlaneSnapshot snapshot,
        IReadOnlyList<double> frameTimesMs,
        float startingCredits,
        float endingCredits)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: rts-cnc-full-showcase");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Player goal: validate a playable C&C-like full mod with five countries, 100 trainable unit types, items, graph content, and WebUI DataPlane projection.");
        sb.AppendLine("- Runtime path: real ConfigPipeline, real GAS abilities/effects, real RTS order queue, real Items registry, real Graph registry, and real WebUI DataPlane runtime.");
        sb.AppendLine();
        sb.AppendLine("## Outcomes");
        sb.AppendLine($"- Factions: {snapshot.Summary.Factions}");
        sb.AppendLine($"- Unit types: {snapshot.Summary.UnitTypes}");
        sb.AppendLine($"- Producer buildings: {snapshot.Summary.Producers}");
        sb.AppendLine($"- Supply item types: {snapshot.Summary.SupplyItemTypes}");
        sb.AppendLine($"- Graphs: {snapshot.Summary.Graphs}");
        sb.AppendLine($"- Training check: {ProducerName} trained {TrainedUnitName}; credits {startingCredits:0.##} -> {endingCredits:0.##}.");
        sb.AppendLine($"- DataPlane command check: active faction switched to `{snapshot.ActiveFactionId}`.");
        sb.AppendLine($"- Avg frame ms during smoke: {frameTimesMs.DefaultIfEmpty(0d).Average():F3}.");
        return sb.ToString();
    }

    private static async Task WaitForPacketsAsync(TestDataTransport transport, int count)
    {
        for (int i = 0; i < 50 && transport.Sent.Count < count; i++)
        {
            await Task.Delay(10);
        }

        Assert.That(transport.Sent.Count, Is.GreaterThanOrEqualTo(count));
    }

    private static async Task WaitForKindAsync(TestDataTransport transport, WebUiPacketKind kind)
    {
        for (int i = 0; i < 50 && transport.Sent.All(packet => packet.Kind != kind); i++)
        {
            await Task.Delay(10);
        }

        Assert.That(transport.Sent.Any(packet => packet.Kind == kind), Is.True);
    }

    private static async Task WaitForSnapshotCountAsync(TestDataTransport transport, int count)
    {
        for (int i = 0; i < 50 && transport.Sent.Count(packet => packet.Kind is WebUiPacketKind.Snapshot or WebUiPacketKind.Delta) < count; i++)
        {
            await Task.Delay(10);
        }

        Assert.That(
            transport.Sent.Count(packet => packet.Kind is WebUiPacketKind.Snapshot or WebUiPacketKind.Delta),
            Is.GreaterThanOrEqualTo(count));
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

    private sealed class TestDataTransport : IWebUiDataTransport, IDisposable
    {
        public WebUiTransportCapabilities Capabilities { get; } = WebUiTransportCapabilities.MessageBridge();
        public List<WebUiOutboundPacket> Sent { get; } = new();
        public event EventHandler<WebUiInboundPacket>? PacketReceived;

        public ValueTask SendAsync(WebUiOutboundPacket packet, System.Threading.CancellationToken cancellationToken = default)
        {
            Sent.Add(packet);
            return ValueTask.CompletedTask;
        }

        public void Receive(WebUiInboundPacket packet)
        {
            PacketReceived?.Invoke(this, packet);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed record RtsCncFullDataPlaneSnapshot(
        int Tick,
        string MapId,
        string ActiveFactionId,
        RtsCncFullSummary Summary,
        RtsCncFullFaction[] Factions,
        RtsCncFullUnit[] Units,
        RtsCncFullProducer[] Producers,
        RtsCncFullSupplyItem[] SupplyItems,
        RtsCncFullGraph[] Graphs,
        RtsCncFullMatchSnapshot? Match);

    private sealed record RtsCncFullSummary(int Factions, int UnitTypes, int Producers, int ProducerCategories, int SupplyItemTypes, int Graphs);
    private sealed record RtsCncFullFaction(string Id, string Name, int TeamId, string Color, bool Active, int UnitTypes, int Producers);
    private sealed record RtsCncFullUnit(string Name, string FactionId, int TeamId, string Category, string TemplateId, float Health, float Damage, float Range);
    private sealed record RtsCncFullProducer(string Name, string FactionId, int TeamId, string Category, string TemplateId, int AbilitySlots);
    private sealed record RtsCncFullSupplyItem(string Id, string DisplayName, int MaxStack);
    private sealed record RtsCncFullGraph(string Id, bool Registered);
    private sealed record RtsCncFullMatchSnapshot(
        string Phase,
        string PhaseLabel,
        float PhaseProgress,
        float Credits,
        float Ore,
        float HarvestRate,
        int HarvestLoads,
        int UnitsTrained,
        int PlayerArmyAlive,
        int EnemyUnitsAlive,
        int EnemyStructuresAlive,
        int EnemyDestroyed,
        bool Victory,
        string Instruction,
        string LastInput,
        string LastEvent,
        string[] Log);
}
