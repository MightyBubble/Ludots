using System.Text.Json;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Modding;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;
using Sango.Core;
using Sango.Runtime;
using Sango.WebUi;

namespace Ludots.Tests.SangoWebUi;

/// <summary>
/// M2.b 部队命令与话题验收:真实内核 + 命令路由直调(WebUiInboundPacket → CommandAck/
/// CommandError 回执,照 SangoSaveCommandTests 的 handler 直调范式扩展到 router 层)。
/// 断言直接读内核单例(Scenario.Cur),不只看命令回执。
/// </summary>
[TestFixture]
public sealed class SangoTroopCommandTests
{
    private const string SessionId = "sango-troop-tests";
    private const int Seed = 20260902;

    private static string RepoRoot()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (File.Exists(Path.Combine(dir, "showcase.registry.json")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not locate Ludots repo root (showcase.registry.json).");
    }

    private static IVirtualFileSystem NewVfs()
    {
        var vfs = new VirtualFileSystem();
        vfs.Mount("SangoContentMod", Path.Combine(RepoRoot(), "mods", "sango", "SangoContentMod"));
        return vfs;
    }

    private static void EnsureKernelBooted()
    {
        if (Scenario.Cur == null)
        {
            SangoKernelBoot.Boot(NewVfs(), "SangoContentMod", Seed, "Scenario/Scenario.json");
        }
    }

    // CityExpedition.IsValid 镜像(一回合行动力发放后),与 SangoWebUiModTests 同法。
    private static City FindEligibleCity()
    {
        Scenario scenario = Scenario.Cur!;
        City? target = null;
        scenario.citySet.ForEach(city =>
        {
            if (target != null || city == null || city.mBelongForce == null || city.mBelongCorps == null)
            {
                return;
            }

            int jobId = (int)CityJobType.MakeTroop;
            if (city.troops > 0 && city.food > 0 && city.freePersons.Count > 0 &&
                city.mBelongCorps.ActionPoint >= JobType.GetJobCostAP(jobId))
            {
                target = city;
            }
        });
        return target ?? throw new InvalidOperationException("no eligible city for createTroop");
    }

    private static Cell PickEmptyRangeCell(Troop troop)
    {
        troop.MoveRange.Clear();
        Scenario.Cur!.Map.GetMoveRange(troop, troop.MoveRange);
        Cell? best = null;
        int bestDistance = -1;
        foreach (Cell cell in troop.MoveRange)
        {
            if (cell.troop != null || cell.building != null)
            {
                continue;
            }

            int distance = Math.Abs(cell.x - troop.x) + Math.Abs(cell.y - troop.y);
            if (distance > bestDistance)
            {
                best = cell;
                bestDistance = distance;
            }
        }

        return best ?? throw new InvalidOperationException("no empty cell in move range");
    }

    // 确定性找一处"必在移动范围外"的图内格:全图扫描第一个不在 MoveRange 的存活格
    //(移动力有限,必有;避免依赖地图尺寸/角落假设,CI 合成图与真图同逻辑)。
    private static Cell PickOutOfRangeCell(Troop troop)
    {
        troop.MoveRange.Clear();
        Scenario.Cur!.Map.GetMoveRange(troop, troop.MoveRange);
        Map map = Scenario.Cur.Map;
        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                Cell? cell = map.GetCell(x, y);
                if (cell != null && !troop.MoveRange.Contains(cell))
                {
                    return cell;
                }
            }
        }

        throw new InvalidOperationException("the whole map is inside the move range; cannot test out_of_range");
    }

    // M3.c:越程可驻空格(委任移动目标)与越程不可驻格(占位建筑,仍拒)的选格器。
    private static Cell PickOutOfRangeStayableCell(Troop troop)
    {
        troop.MoveRange.Clear();
        Scenario.Cur!.Map.GetMoveRange(troop, troop.MoveRange);
        Map map = Scenario.Cur.Map;
        Cell? best = null;
        int bestDistance = -1;
        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                Cell? cell = map.GetCell(x, y);
                if (cell == null || troop.MoveRange.Contains(cell) || !cell.CanStay(troop) || !cell.moveAble)
                {
                    continue;
                }

                int distance = Math.Abs(cell.x - troop.x) + Math.Abs(cell.y - troop.y);
                if (distance > bestDistance)
                {
                    best = cell;
                    bestDistance = distance;
                }
            }
        }

        return best ?? throw new InvalidOperationException("no stayable out-of-range cell on the map");
    }

    private static Cell PickOutOfRangeNonStayableCell(Troop troop)
    {
        troop.MoveRange.Clear();
        Scenario.Cur!.Map.GetMoveRange(troop, troop.MoveRange);
        Map map = Scenario.Cur.Map;
        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                Cell? cell = map.GetCell(x, y);
                if (cell != null && !troop.MoveRange.Contains(cell) && !cell.CanStay(troop))
                {
                    return cell;
                }
            }
        }

        throw new InvalidOperationException("no non-stayable out-of-range cell on the map");
    }

    private sealed class CommandHarness : IDisposable
    {
        public CommandHarness(WebUiDataPlaneRuntime runtime, WebUiQueuedCommandDispatcher dispatcher)
        {
            Runtime = runtime;
            Dispatcher = dispatcher;
            Pump = new WebUiDataPlaneTickPump(runtime, dispatcher);
            Transport = new FakeTransport();
            Runtime.AttachSession(SessionId, Transport);
        }

        public WebUiDataPlaneRuntime Runtime { get; }

        public WebUiQueuedCommandDispatcher Dispatcher { get; }

        public WebUiDataPlaneTickPump Pump { get; }

        public FakeTransport Transport { get; }

        public void Dispose()
        {
            Runtime.Dispose();
            Dispatcher.Dispose();
        }
    }

    private sealed class FakeTransport : IWebUiDataTransport
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<WebUiOutboundPacket> _sent = new();
        private readonly SemaphoreSlim _signal = new(0);

        public event EventHandler<WebUiInboundPacket>? PacketReceived;

        public WebUiTransportCapabilities Capabilities { get; } = WebUiTransportCapabilities.StringBridge();

        public ValueTask SendAsync(WebUiOutboundPacket packet, CancellationToken cancellationToken = default)
        {
            _sent.Enqueue(packet);
            _signal.Release();
            return ValueTask.CompletedTask;
        }

        public void Receive(WebUiInboundPacket packet) => PacketReceived?.Invoke(this, packet);

        public async Task<WebUiOutboundPacket> WaitForSentAsync(CancellationToken cancellationToken = default)
        {
            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
            return _sent.TryDequeue(out WebUiOutboundPacket? packet)
                ? packet
                : throw new InvalidOperationException("Send signal was raised without a packet.");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static CommandHarness NewHarness()
    {
        var router = new WebUiCommandRouter(
            new SangoWebUiGenerationResolver(),
            new SangoWebUiPermissionValidator());
        router.Register(SangoCreateTroopCommandHandler.CommandName, new SangoCreateTroopCommandHandler());
        router.Register(SangoMoveTroopCommandHandler.CommandName, new SangoMoveTroopCommandHandler());
        var dispatcher = new WebUiQueuedCommandDispatcher(router);
        var runtime = new WebUiDataPlaneRuntime(dispatcher);
        return new CommandHarness(runtime, dispatcher);
    }

    // 命令回环:照 SangoWebUiModTests 的 Fake transport 全链(runtime 解包 → router →
    // CommandAck/CommandError 回执);回执是控制封包,结果在 payload 段。
    private static async Task<(bool Acked, string ErrorCode, string Message)> DispatchAsync(
        CommandHarness harness, string name, object payload, long clientSeq)
    {
        harness.Transport.Receive(WebUiDataPlaneProtocol.CreateControlPacket(
            SessionId, clientSeq, "command", "orders", new
            {
                name,
                clientSeq,
                entityRefs = Array.Empty<object>(),
                payload
            }));
        await harness.Pump.FlushCommandsAsync(TestContext.CurrentContext.CancellationToken);
        WebUiOutboundPacket outbound = await harness.Transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
        using JsonDocument doc = JsonDocument.Parse(outbound.Payload);
        JsonElement root = doc.RootElement;
        // 回执封包的 payload 段可能平铺(直调 router)或嵌套(经 runtime),两处都探测。
        JsonElement result = root.TryGetProperty("payload", out JsonElement nested) &&
            nested.ValueKind == JsonValueKind.Object &&
            (nested.TryGetProperty("code", out _) || nested.TryGetProperty("clientSeq", out _))
                ? nested
                : root;
        return (
            outbound.Kind == WebUiPacketKind.CommandAck,
            result.TryGetProperty("code", out JsonElement code) ? code.GetString() ?? string.Empty : string.Empty,
            result.TryGetProperty("message", out JsonElement message) ? message.GetString() ?? string.Empty : string.Empty);
    }

    [OneTimeSetUp]
    public void BootKernelOnce()
    {
        EnsureKernelBooted();
    }

    [SetUp]
    public void AdvanceTurnForActionPoints()
    {
        // 开局军团行动力为 0(Corps.OnForceTurnStart 发放);每测试前推一回合保证门槛可过。
        SangoTurnDriver.AdvanceTurn();
    }

    [Test]
    public async Task CreateTroopCommand_DataPlaneLoop_EnrollsTroopInKernel()
    {
        EnsureKernelBooted();
        City city = FindEligibleCity();
        int[] personIds = city.freePersons.Where(person => person != null).Take(2).Select(person => person!.Id).ToArray();
        int troopsBefore = scenarioTroopCount();
        using CommandHarness harness = NewHarness();

        (bool acked, string code, string message) = await DispatchAsync(harness,
            SangoCreateTroopCommandHandler.CommandName, new
            {
                cityId = city.Id,
                personIds,
                troops = 3000,
                gold = 500,
                food = 20000
            }, clientSeq: 11);
        Assert.That(acked, Is.True, $"createTroop rejected: {code} {message}");

        Assert.That(scenarioTroopCount(), Is.EqualTo(troopsBefore + 1), "kernel troopsSet must grow by one");
        Troop? troop = null;
        Scenario.Cur!.troopsSet.ForEach(t => { if (t != null && t.IsAlive) { troop = t; } });
        Assert.That(troop, Is.Not.Null);
        Assert.That(troop!.cell, Is.SameAs(city.CenterCell), "EnsureTroop anchors at the city center cell");
        Assert.That(troop.Leader!.Id, Is.EqualTo(personIds[0]), "first personId is the leader (UpdateJobValue order)");
        Assert.That(city.freePersons, Does.Not.Contain(troop.Leader), "leader must leave freePersons");

        // 未知武将/缺载荷的类型化失败。
        (acked, code, _) = await DispatchAsync(harness,
            SangoCreateTroopCommandHandler.CommandName, new
            {
                cityId = city.Id,
                personIds = new[] { 999999 },
                troops = 100
            }, clientSeq: 12);
        Assert.That(acked, Is.False);
        Assert.That(code, Is.EqualTo("person_not_free"));

        (acked, code, _) = await DispatchAsync(harness,
            SangoCreateTroopCommandHandler.CommandName, new { cityId = city.Id }, clientSeq: 13);
        Assert.That(acked, Is.False);
        Assert.That(code, Is.EqualTo("invalid_payload"));
    }

    [Test]
    public async Task MoveTroopCommand_DataPlaneLoop_MovesKernelTroop()
    {
        EnsureKernelBooted();
        City city = FindEligibleCity();
        int[] personIds = city.freePersons.Where(person => person != null).Take(1).Select(person => person!.Id).ToArray();
        var (opResult, troop) = SangoTroopOps.CreateTroop(Scenario.Cur!, city, personIds, troops: 2000, food: 5000);
        Assert.That(opResult.Succeeded, Is.True, opResult.Message);
        Assert.That(troop, Is.Not.Null);

        Cell dest = PickEmptyRangeCell(troop!);
        using CommandHarness harness = NewHarness();
        (bool acked, string code, string message) = await DispatchAsync(harness,
            SangoMoveTroopCommandHandler.CommandName, new
            {
                troopId = troop!.Id,
                x = dest.x,
                y = dest.y
            }, clientSeq: 21);
        Assert.That(acked, Is.True, $"moveTroop rejected: {code} {message}");
        Assert.That(troop.cell, Is.SameAs(dest), "kernel troop must land on the ordered cell");
        Assert.That(troop.ActionOver, Is.True, "the ordered move ends the troop action");

        // M3.c:越程可驻空格 = 委任移动(TroopMovetoCell 多回合任务,首回合推进+行动完结)。
        Troop second = SeedExtraTroop();
        Cell origin = second.cell;
        Cell far = PickOutOfRangeStayableCell(second);
        (acked, code, message) = await DispatchAsync(harness,
            SangoMoveTroopCommandHandler.CommandName, new
            {
                troopId = second.Id,
                x = far.x,
                y = far.y
            }, clientSeq: 22);
        Assert.That(acked, Is.True, $"commissioned move rejected: {code} {message}");
        Assert.That(second.missionType, Is.EqualTo((int)MissionType.TroopMovetoCell), "the far order must grant the march mission");
        Assert.That(second.missionParams1, Is.EqualTo(far.x));
        Assert.That(second.missionParams2, Is.EqualTo(far.y));
        Assert.That(second.cell, Is.Not.SameAs(origin), "the command turn's advance must run");
        Assert.That(second.ActionOver, Is.True, "the commissioned order ends the command turn's action");

        // 越程不可驻格(占位建筑)仍类型化拒绝(位置不变)。
        Troop third = SeedExtraTroop();
        Cell thirdOrigin = third.cell;
        Cell blocked = PickOutOfRangeNonStayableCell(third);
        (acked, code, _) = await DispatchAsync(harness,
            SangoMoveTroopCommandHandler.CommandName, new
            {
                troopId = third.Id,
                x = blocked.x,
                y = blocked.y
            }, clientSeq: 24);
        Assert.That(acked, Is.False);
        Assert.That(code, Is.EqualTo("out_of_range"));
        Assert.That(third.cell, Is.SameAs(thirdOrigin), "rejected order must not move the troop");

        (acked, code, _) = await DispatchAsync(harness,
            SangoMoveTroopCommandHandler.CommandName, new
            {
                troopId = 999999,
                x = 0,
                y = 0
            }, clientSeq: 23);
        Assert.That(acked, Is.False);
        Assert.That(code, Is.EqualTo("troop_not_found"));
    }

    [Test]
    public void TroopsTopic_SnapshotMirrorsKernelTroops()
    {
        EnsureKernelBooted();
        City city = FindEligibleCity();
        int[] personIds = city.freePersons.Where(person => person != null).Take(1).Select(person => person!.Id).ToArray();
        var (opResult, _) = SangoTroopOps.CreateTroop(Scenario.Cur!, city, personIds, troops: 2000, food: 5000);
        Assert.That(opResult.Succeeded, Is.True, opResult.Message);

        var topic = new SangoWorldTroopsTopic(new SangoWorldFeed());
        var context = new WebUiTopicContext(SessionId, SangoWorldTroopsTopic.TopicName, RequestId: 7, Parameters: default);
        Assert.That(topic.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
        using JsonDocument doc = JsonDocument.Parse(packet.Payload);
        JsonElement troops = doc.RootElement.GetProperty("troops");
        Assert.That(troops.GetArrayLength(), Is.GreaterThanOrEqualTo(1), "the seeded troop must project");

        foreach (JsonElement row in troops.EnumerateArray())
        {
            Troop kernelTroop = Scenario.Cur!.troopsSet.Get(row.GetProperty("id").GetInt32())!;
            Assert.That(row.GetProperty("x").GetInt32(), Is.EqualTo(kernelTroop.x));
            Assert.That(row.GetProperty("y").GetInt32(), Is.EqualTo(kernelTroop.y));
            Assert.That(row.GetProperty("troops").GetInt32(), Is.EqualTo(kernelTroop.troops));
            Assert.That(row.GetProperty("forceId").GetInt32(), Is.EqualTo(kernelTroop.mBelongForce!.Id));
        }
    }

    [Test]
    public void CityDetailTopic_CarriesFieldTroopsAndFormTroopTypes()
    {
        EnsureKernelBooted();
        City city = FindEligibleCity();
        int[] personIds = city.freePersons.Where(person => person != null).Take(1).Select(person => person!.Id).ToArray();
        var (opResult, troop) = SangoTroopOps.CreateTroop(Scenario.Cur!, city, personIds, troops: 2000, food: 5000);
        Assert.That(opResult.Succeeded, Is.True, opResult.Message);

        var topic = new SangoWorldCityTopic(new SangoWorldFeed());
        var context = new WebUiTopicContext(
            SessionId, SangoWorldCityTopic.TopicName, RequestId: 8,
            Parameters: JsonSerializer.SerializeToElement(new { cityId = city.Id }));
        Assert.That(topic.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
        using JsonDocument doc = JsonDocument.Parse(packet.Payload);
        JsonElement root = doc.RootElement;

        JsonElement fieldTroops = root.GetProperty("fieldTroops");
        Assert.That(fieldTroops.GetArrayLength(), Is.EqualTo(1), "the city detail must list its expedition troop");
        Assert.That(fieldTroops[0].GetProperty("id").GetInt32(), Is.EqualTo(troop!.Id));
        Assert.That(fieldTroops[0].GetProperty("x").GetInt32(), Is.EqualTo(troop.x));

        JsonElement troopTypes = root.GetProperty("troopTypes");
        Assert.That(troopTypes.GetArrayLength(), Is.GreaterThan(0), "the expedition form must expose active troop types");
        bool anyLand = false;
        bool anyWater = false;
        foreach (JsonElement type in troopTypes.EnumerateArray())
        {
            anyLand |= type.GetProperty("isLand").GetBoolean();
            anyWater |= !type.GetProperty("isLand").GetBoolean();
        }

        Assert.That(anyLand, Is.True, "CityExpedition requires a land type option");
        Assert.That(anyWater, Is.True, "CityExpedition requires a water type option");
    }

    [Test]
    public void PermissionValidator_AllowsTroopCommandsAndRejectsUnknown()
    {
        var validator = new SangoWebUiPermissionValidator();
        Assert.That(validator.CanUse(
            new WebUiCommandRequest(SangoCreateTroopCommandHandler.CommandName, 1, Array.Empty<WebUiEntityRef>(), default),
            out _), Is.True);
        Assert.That(validator.CanUse(
            new WebUiCommandRequest(SangoMoveTroopCommandHandler.CommandName, 1, Array.Empty<WebUiEntityRef>(), default),
            out _), Is.True);
        Assert.That(validator.CanUse(
            new WebUiCommandRequest("sango.troopSummon", 1, Array.Empty<WebUiEntityRef>(), default),
            out string error), Is.False);
        Assert.That(error, Does.Contain("not allowed"));
    }

    private static int scenarioTroopCount()
    {
        int count = 0;
        Scenario.Cur!.troopsSet.ForEach(troop =>
        {
            if (troop != null && troop.IsAlive)
            {
                count++;
            }
        });
        return count;
    }

    private static Troop SeedExtraTroop()
    {
        City city = FindEligibleCity();
        int[] personIds = city.freePersons.Where(person => person != null).Take(1).Select(person => person!.Id).ToArray();
        var (opResult, troop) = SangoTroopOps.CreateTroop(Scenario.Cur!, city, personIds, troops: 1000, food: 5000);
        Assert.That(opResult.Succeeded, Is.True, opResult.Message);
        return troop!;
    }
}
