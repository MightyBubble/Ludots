using System.Collections.Concurrent;
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
/// M1.c 验收:SangoWebUiMod 投影层与命令路由对真实 sango 内核端到端(headless,Fake transport,
/// 照 SangoMapStatsDataPlaneTests 范式)。内核经 SangoKernelBoot 以真实 Scenario.json 启动一次,
/// 全部断言直接读内核单例(Scenario.Cur),不只看命令回执。
/// </summary>
[TestFixture]
public sealed class SangoWebUiModTests
{
    private const string SessionId = "sango-webui-tests";
    private const int Seed = 20260902;

    private static IVirtualFileSystem NewVfs()
    {
        var vfs = new VirtualFileSystem();
        vfs.Mount("SangoContentMod", Path.Combine(RepoRoot(), "mods", "sango", "SangoContentMod"));
        return vfs;
    }

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

    [OneTimeSetUp]
    public void BootKernelOnce()
    {
        SangoKernelBoot.Boot(NewVfs(), "SangoContentMod", Seed, "Scenario/Scenario.json");
    }

    private static DataPlaneHarness CreateHarness(GasClockStepPolicy? stepPolicy = null)
    {
        var feed = new SangoWorldFeed();
        feed.AttachPlayerMessageSystem();
        var router = new WebUiCommandRouter(
            new SangoWebUiGenerationResolver(),
            new SangoWebUiPermissionValidator());
        router.Register(SangoCityCommandHandler.CommandName, new SangoCityCommandHandler());
        router.Register(
            SangoEndTurnCommandHandler.CommandName,
            new SangoEndTurnCommandHandler(stepPolicy ?? new GasClockStepPolicy(10, GasStepMode.Manual)));

        var dispatcher = new WebUiQueuedCommandDispatcher(router);
        var runtime = new WebUiDataPlaneRuntime(dispatcher);
        runtime.RegisterTopic(new SangoWorldCitiesTopic(feed));
        runtime.RegisterTopic(new SangoWorldForcesTopic(feed));
        runtime.RegisterTopic(new SangoWorldTurnTopic());
        runtime.RegisterTopic(new SangoWorldMessagesTopic(feed));
        runtime.RegisterTopic(new SangoWorldCityTopic(feed));
        return new DataPlaneHarness(runtime, dispatcher);
    }

    [Test]
    public void CitiesAndForcesSnapshots_CarryScenarioWorld()
    {
        using DataPlaneHarness plane = CreateHarness();
        var citiesProducer = new SangoWorldCitiesTopic(new SangoWorldFeed());
        var context = new WebUiTopicContext(SessionId, SangoWorldCitiesTopic.TopicName, RequestId: 7, Parameters: default);
        Assert.That(citiesProducer.TryCreateSnapshot(in context, out WebUiOutboundPacket citiesPacket), Is.True);
        using JsonDocument citiesDoc = ParsePacket(citiesPacket);
        JsonElement cities = citiesDoc.RootElement.GetProperty("cities");
        Assert.That(cities.GetArrayLength(), Is.GreaterThanOrEqualTo(80), "scenario must project >= 80 cities");
        int owned = 0;
        foreach (JsonElement city in cities.EnumerateArray())
        {
            Assert.That(city.GetProperty("name").GetString(), Is.Not.Null.And.Not.Empty,
                "every projected city must carry its scenario name");
            if (city.GetProperty("forceId").GetInt32() > 0)
            {
                owned++;
            }
        }

        Assert.That(owned, Is.GreaterThanOrEqualTo(60), "most scenario cities must report an owning force");

        var forcesProducer = new SangoWorldForcesTopic(new SangoWorldFeed());
        context = new WebUiTopicContext(SessionId, SangoWorldForcesTopic.TopicName, RequestId: 8, Parameters: default);
        Assert.That(forcesProducer.TryCreateSnapshot(in context, out WebUiOutboundPacket forcesPacket), Is.True);
        using JsonDocument forcesDoc = ParsePacket(forcesPacket);
        JsonElement forces = forcesDoc.RootElement.GetProperty("forces");
        Assert.That(forces.GetArrayLength(), Is.GreaterThanOrEqualTo(40), "scenario must project >= 40 forces");
        bool anyCityCount = false;
        int governors = 0;
        foreach (JsonElement force in forces.EnumerateArray())
        {
            Assert.That(force.GetProperty("name").GetString(), Is.Not.Null.And.Not.Empty);
            anyCityCount |= force.GetProperty("cityCount").GetInt32() > 0;
            if (!string.IsNullOrEmpty(force.GetProperty("governorName").GetString()))
            {
                governors++;
            }
        }

        Assert.That(anyCityCount, Is.True, "at least one force must report owned city bases");
        Assert.That(governors, Is.GreaterThanOrEqualTo(40), "every scenario force must have a governor");
    }

    [Test]
    public async Task CityCommand_Train_EndToEnd_ChangesKernelWorldState()
    {
        // 先推进一回合,让各军团 OnForceTurnStart 发行动力、清 jobCounter(照真实游玩次序)。
        SangoTurnDriver.AdvanceTurn();
        Scenario scenario = Scenario.Cur!;

        City? target = null;
        Person[] executors = Array.Empty<Person>();
        scenario.citySet.ForEach(city =>
        {
            if (target != null || city == null || city.mBelongForce == null || city.mBelongCorps == null)
            {
                return;
            }

            int jobId = (int)CityJobType.TrainTroops;
            if (city.freePersons.Count == 0 ||
                !city.CheckJobCost(CityJobType.TrainTroops) ||
                city.morale >= city.MaxMorale ||
                city.GetJobCounter(jobId) != 0 ||
                city.mBelongCorps.ActionPoint < JobType.GetJobCostAP(jobId))
            {
                return;
            }

            target = city;
            executors = city.freePersons.Where(person => person != null).Take(1).ToArray()!;
        });

        Assert.That(target, Is.Not.Null, "scenario must offer at least one city passing the CityTrainTroops.IsValid gate");
        City city = target!;
        int moraleBefore = city.morale;
        int freeBefore = city.freePersons.Count;
        int personId = executors[0].Id;

        using DataPlaneHarness plane = CreateHarness();
        plane.Transport.ReceiveCommand(new
        {
            name = SangoCityCommandHandler.CommandName,
            clientSeq = 11,
            entityRefs = Array.Empty<object>(),
            payload = new { type = "train", cityId = city.Id, personIds = new[] { personId } }
        });
        await plane.Pump.FlushCommandsAsync(TestContext.CurrentContext.CancellationToken);

        WebUiOutboundPacket ack = await plane.Transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
        Assert.That(ack.Kind, Is.EqualTo(WebUiPacketKind.CommandAck), "train command must be acknowledged");
        Assert.That(ack.ClientSeq, Is.EqualTo(11));

        // 内核语义断言(JobTrainTroops:士气提升,执行者离队待命;训练金费为 0,不断言金变化)。
        Assert.That(city.morale, Is.GreaterThan(moraleBefore),
            $"city {city.Id} morale must rise under JobTrainTroops ({moraleBefore} -> {city.morale})");
        Assert.That(city.freePersons.Count, Is.EqualTo(freeBefore - 1),
            "the executor must leave freePersons (ActionOver) as in the kernel job flow");
        Assert.That(city.GetJobCounter((int)CityJobType.TrainTroops), Is.EqualTo(1),
            "city job counter must record the executed train order");

        // 投影随内核:重新订阅城市列表话题,快照读数必须等于内核当前值(命令后反映变化)。
        plane.Transport.Receive(WebUiDataPlaneProtocol.CreateControlPacket(
            SessionId, 12, "subscribe", SangoWorldCitiesTopic.TopicName, new { }));
        WebUiOutboundPacket citiesSnapshot = await plane.Transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
        Assert.That(citiesSnapshot.Kind, Is.EqualTo(WebUiPacketKind.Snapshot));
        using JsonDocument doc = ParsePacket(citiesSnapshot);
        JsonElement row = doc.RootElement.GetProperty("cities").EnumerateArray()
            .Single(candidate => candidate.GetProperty("id").GetInt32() == city.Id);
        Assert.That(row.GetProperty("gold").GetInt32(), Is.EqualTo(city.gold), "projection gold must equal kernel gold");
        Assert.That(row.GetProperty("personCount").GetInt32(), Is.EqualTo(city.allPersons.Count));
    }

    [Test]
    public async Task EndTurnCommand_RoutesThroughManualClockStepPolicy()
    {
        var stepPolicy = new GasClockStepPolicy(10, GasStepMode.Manual);
        using DataPlaneHarness plane = CreateHarness(stepPolicy);

        plane.Transport.ReceiveCommand(new
        {
            name = SangoEndTurnCommandHandler.CommandName,
            clientSeq = 21,
            entityRefs = Array.Empty<object>(),
            payload = new { }
        });
        await plane.Pump.FlushCommandsAsync(TestContext.CurrentContext.CancellationToken);

        WebUiOutboundPacket ack = await plane.Transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
        Assert.That(ack.Kind, Is.EqualTo(WebUiPacketKind.CommandAck));

        Assert.That(stepPolicy.ConsumeStepsForThisFixedTick(), Is.EqualTo(1),
            "sango.endTurn must have queued exactly one manual clock step (RequestStep(1))");
        Assert.That(stepPolicy.ConsumeStepsForThisFixedTick(), Is.EqualTo(0),
            "no extra steps beyond the single requested one");
    }

    [Test]
    public void TurnAndMessagesTopics_ReflectAdvancedTurn()
    {
        int turnBefore = Scenario.Cur!.Info.turnCount;
        string dateBefore = Scenario.Cur.GetDateStr();

        var feed = new SangoWorldFeed();
        SangoTurnDriver.AdvanceTurn();
        feed.NoteTurnAdvanced();

        var turnTopic = new SangoWorldTurnTopic();
        var context = new WebUiTopicContext(SessionId, SangoWorldTurnTopic.TopicName, RequestId: 9, Parameters: default);
        Assert.That(turnTopic.TryCreateSnapshot(in context, out WebUiOutboundPacket turnPacket), Is.True);
        using JsonDocument turnDoc = ParsePacket(turnPacket);
        JsonElement turnRoot = turnDoc.RootElement;
        Assert.That(turnRoot.GetProperty("turnCount").GetInt32(), Is.EqualTo(turnBefore + 1));
        Assert.That(turnRoot.GetProperty("dateText").GetString(), Is.Not.EqualTo(dateBefore),
            "one turn advances the in-game date by ten days");
        Assert.That(turnRoot.GetProperty("summary").GetString(), Does.Contain($"turn {turnBefore + 1,3}"),
            "the turn summary must quote the kernel turn counter (SangoTurnDriver fixed-width format)");

        var messagesTopic = new SangoWorldMessagesTopic(feed);
        context = new WebUiTopicContext(SessionId, SangoWorldMessagesTopic.TopicName, RequestId: 10, Parameters: default);
        Assert.That(messagesTopic.TryCreateSnapshot(in context, out WebUiOutboundPacket messagesPacket), Is.True);
        using JsonDocument messagesDoc = ParsePacket(messagesPacket);
        JsonElement messages = messagesDoc.RootElement.GetProperty("messages");
        Assert.That(messages.GetArrayLength(), Is.GreaterThanOrEqualTo(1),
            "the feed must carry the SangoTurnDriver summary row for the advanced turn");
        Assert.That(messages[messages.GetArrayLength() - 1].GetProperty("turnCount").GetInt32(),
            Is.EqualTo(turnBefore + 1));
    }

    [Test]
    public async Task CityDetailTopic_SubscriptionParametersSelectTheCity()
    {
        Scenario scenario = Scenario.Cur!;
        City? sample = null;
        scenario.citySet.ForEach(city =>
        {
            if (sample == null && city != null && city.mBelongForce != null && city.allPersons.Count > 0)
            {
                sample = city;
            }
        });
        Assert.That(sample, Is.Not.Null, "scenario must offer an owned city with a person roster");

        using DataPlaneHarness plane = CreateHarness();
        plane.Transport.Receive(WebUiDataPlaneProtocol.CreateControlPacket(
            SessionId, 31, "subscribe", SangoWorldCityTopic.TopicName, new { cityId = sample!.Id }));
        WebUiOutboundPacket snapshot = await plane.Transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
        Assert.That(snapshot.Kind, Is.EqualTo(WebUiPacketKind.Snapshot));
        Assert.That(snapshot.Topic, Is.EqualTo(SangoWorldCityTopic.TopicName));

        using JsonDocument doc = ParsePacket(snapshot);
        JsonElement root = doc.RootElement;
        Assert.That(root.GetProperty("id").GetInt32(), Is.EqualTo(sample.Id));
        Assert.That(root.GetProperty("persons").GetArrayLength(), Is.EqualTo(sample.allPersons.Count),
            "the detail projection must list every person in the city roster");
        Assert.That(root.GetProperty("gold").GetInt32(), Is.EqualTo(sample.gold));
    }

    /// <summary>
    /// M3.a 运行时取证(任务⑤):开局选曹操 → 玩家回合下内政令(探索) → 结束回合
    /// (进行 + 时钟步)→ 消息流携带 PlayerMessage 真源行。世界在局内重装两次
    /// (开局选择 + 测试尾恢复全托管),与 web 命令 sango.selectPlayerForce 的数据面同链
    /// (SangoPlayerTurnOps.SelectPlayerForce → BootWithPlayer → CheckPlayer)。
    /// </summary>
    [Test]
    public async Task PlayerFlow_SelectCaoCao_SearchOrder_EndTurn_MessagesCarryPlayerMessageSource()
    {
        // 开局态重装(选势力是开局动作;固定种子下 force "曹操" 可寻)。
        SangoKernelBoot.Boot(NewVfs(), "SangoContentMod", Seed, "Scenario/Scenario.json");
        Force? caoCao = null;
        Scenario.Cur!.forceSet.ForEach(force =>
        {
            if (force != null && force.Name == "曹操")
            {
                caoCao = force;
            }
        });
        Assert.That(caoCao, Is.Not.Null, "the scenario must carry the 曹操 force for the forensic flow");
        int forceId = caoCao!.Id;

        SangoCommandJournal.Clear();
        SangoPlayerTurnOps.SelectPlayerForce(NewVfs(), "SangoContentMod", Seed, forceId);
        Scenario scenario = Scenario.Cur!;
        Assert.That(scenario.forceSet.Get(forceId)!.IsPlayer, Is.True, "CheckPlayer must mark 曹操 as the player force");

        string firstStep = SangoTurnDriver.AdvanceTurn().ToString()!;
        Assert.That(firstStep, Is.EqualTo("AwaitingPlayer"), "the world must stop at the player turn gate before any order");

        using DataPlaneHarness plane = CreateHarness();
        City? playerCity = null;
        City? foreignCity = null;
        scenario.citySet.ForEach(city =>
        {
            if (city == null || city.mBelongForce == null)
            {
                return;
            }

            if (playerCity == null && city.mBelongForce.Id == forceId && city.freePersons.Count > 0)
            {
                playerCity = city;
            }

            if (foreignCity == null && city.mBelongForce.Id != forceId)
            {
                foreignCity = city;
            }
        });
        Assert.That(playerCity, Is.Not.Null, "曹操 must own a staffed city at boot");

        // 非玩家城命令:玩家局城门拒绝(not_player_city),取证玩家门真实生效。
        plane.Transport.ReceiveCommand(new
        {
            name = SangoCityCommandHandler.CommandName,
            clientSeq = 41,
            entityRefs = Array.Empty<object>(),
            payload = new { type = "train", cityId = foreignCity!.Id, personIds = new[] { playerCity!.freePersons[0]!.Id } }
        });
        await plane.Pump.FlushCommandsAsync(TestContext.CurrentContext.CancellationToken);
        WebUiOutboundPacket foreignAck = await plane.Transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
        Assert.That(foreignAck.Kind, Is.EqualTo(WebUiPacketKind.CommandError), "a foreign city order must be rejected in a player world");
        using (JsonDocument foreignDoc = ParsePacket(foreignAck))
        {
            Assert.That(foreignDoc.RootElement.GetProperty("payload").GetProperty("code").GetString(),
                Is.EqualTo("not_player_city"));
        }

        // 玩家城探索令:过门 + AP 真实扣减;搜索演出事件在本回合内的 RenderEvent 泵结算。
        int apBefore = playerCity.mBelongCorps!.ActionPoint;
        plane.Transport.ReceiveCommand(new
        {
            name = SangoCityCommandHandler.CommandName,
            clientSeq = 42,
            entityRefs = Array.Empty<object>(),
            payload = new { type = "search", cityId = playerCity.Id, personIds = new[] { playerCity.freePersons[0]!.Id } }
        });
        await plane.Pump.FlushCommandsAsync(TestContext.CurrentContext.CancellationToken);
        WebUiOutboundPacket searchAck = await plane.Transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
        Assert.That(searchAck.Kind, Is.EqualTo(WebUiPacketKind.CommandAck), "the player-city search order must pass the gates");

        // 结束回合 = 原版「进行」(放行君主军团)+ Manual 时钟步;命令只路由,不直接推内核。
        var stepPolicy = new GasClockStepPolicy(10, GasStepMode.Manual);
        using DataPlaneHarness turnPlane = CreateHarness(stepPolicy);
        turnPlane.Transport.ReceiveCommand(new
        {
            name = SangoEndTurnCommandHandler.CommandName,
            clientSeq = 43,
            entityRefs = Array.Empty<object>(),
            payload = new { }
        });
        await turnPlane.Pump.FlushCommandsAsync(TestContext.CurrentContext.CancellationToken);
        WebUiOutboundPacket endAck = await turnPlane.Transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
        Assert.That(endAck.Kind, Is.EqualTo(WebUiPacketKind.CommandAck), "endTurn must acknowledge the 进行 + clock-step route");
        Assert.That(stepPolicy.ConsumeStepsForThisFixedTick(), Is.EqualTo(1), "exactly one manual clock step must be requested");
        Assert.That(SangoPlayerTurnOps.AwaitingPlayer(scenario), Is.False, "the 进行 release must have unblocked the governor corps");

        // 时钟步在生产链由引擎 tick 消费;headless 直接推 Sango 回合至下一玩家门,搜索事件在
        // 本回合的演出泵内结算,PlayerMessage 行(onTextMessageAdd 真源)随之入流。
        var feed = new SangoWorldFeed();
        feed.AttachPlayerMessageSystem();
        string secondStep = SangoTurnDriver.AdvanceTurn().ToString()!;
        Assert.That(secondStep, Is.EqualTo("AwaitingPlayer"), "the next step must stop at the next player turn gate");
        // 探索令的结算证据由下方 PlayerMessage 真源行承载(DoJobSearching 的 IsPlayer 分支);
        // freePersons/AP 都是回合内暂态(新回合 OnForceTurnStart 重建),不可跨回合比对。

        var messagesTopic = new SangoWorldMessagesTopic(feed);
        var context = new WebUiTopicContext(SessionId, SangoWorldMessagesTopic.TopicName, RequestId: 44, Parameters: default);
        Assert.That(messagesTopic.TryCreateSnapshot(in context, out WebUiOutboundPacket messagesPacket), Is.True);
        using JsonDocument messagesDoc = ParsePacket(messagesPacket);
        JsonElement messages = messagesDoc.RootElement.GetProperty("messages");
        bool carriesPlayerMessageSource = false;
        foreach (JsonElement message in messages.EnumerateArray())
        {
            string text = message.GetProperty("text").GetString() ?? string.Empty;
            if (text.Contains("发现") || text.Contains("什么也没发现"))
            {
                carriesPlayerMessageSource = true;
                TestContext.Progress.WriteLine(
                    $"[m3a-forensics] PlayerMessage 真源行 turn={message.GetProperty("turnCount").GetInt32()}: {text}");
            }
        }

        Assert.That(carriesPlayerMessageSource, Is.True,
            "the message stream must carry the search outcome from the kernel PlayerMessage source (IsPlayer-gated lines)");

        // 恢复夹具的全托管环境(后续断言假定无玩家世界)。
        SangoKernelBoot.Boot(NewVfs(), "SangoContentMod", Seed, "Scenario/Scenario.json");
    }

    private static JsonDocument ParsePacket(WebUiOutboundPacket packet)
    {
        return JsonDocument.Parse(packet.Payload);
    }

    private sealed class DataPlaneHarness : IDisposable
    {
        public DataPlaneHarness(WebUiDataPlaneRuntime runtime, WebUiQueuedCommandDispatcher dispatcher)
        {
            Runtime = runtime;
            Dispatcher = dispatcher;
            Pump = new WebUiDataPlaneTickPump(runtime, dispatcher);
            Transport = new FakeWebUiDataTransport();
            Runtime.AttachSession(SessionId, Transport);
        }

        public WebUiDataPlaneRuntime Runtime { get; }

        public WebUiQueuedCommandDispatcher Dispatcher { get; }

        public WebUiDataPlaneTickPump Pump { get; }

        public FakeWebUiDataTransport Transport { get; }

        public void Dispose()
        {
            Runtime.Dispose();
            Dispatcher.Dispose();
        }
    }

    private sealed class FakeWebUiDataTransport : IWebUiDataTransport
    {
        private readonly ConcurrentQueue<WebUiOutboundPacket> _sent = new();
        private readonly SemaphoreSlim _sentSignal = new(0);

        public WebUiTransportCapabilities Capabilities { get; } = WebUiTransportCapabilities.StringBridge();

        public event EventHandler<WebUiInboundPacket>? PacketReceived;

        public ValueTask SendAsync(WebUiOutboundPacket packet, CancellationToken cancellationToken = default)
        {
            _sent.Enqueue(packet);
            _sentSignal.Release();
            return ValueTask.CompletedTask;
        }

        public void Receive(WebUiInboundPacket packet)
        {
            PacketReceived?.Invoke(this, packet);
        }

        public void ReceiveCommand(object commandEnvelope)
        {
            Receive(WebUiDataPlaneProtocol.CreateControlPacket(
                SessionId, 1, "command", "orders", commandEnvelope));
        }

        public async Task<WebUiOutboundPacket> WaitForSentAsync(CancellationToken cancellationToken = default)
        {
            await _sentSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (_sent.TryDequeue(out WebUiOutboundPacket? packet))
            {
                return packet;
            }

            throw new InvalidOperationException("Send signal was raised without a packet.");
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
