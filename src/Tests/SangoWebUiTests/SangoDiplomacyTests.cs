using System.Text.Json;
using Ludots.Core.Modding;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;
using Sango.Core;
using Sango.Runtime;
using Sango.WebUi;

namespace Ludots.Tests.SangoWebUi;

/// <summary>
/// M3.d 任务二:外交玩法对齐验收(原版活跃面:送礼/结盟/摒弃同盟;停用面分级见汇报)。
/// 覆盖(全部 headless,真实 Scenario.json 内核):
///   1. 玩家门与原版门槛:非玩家城拒绝(not_player_city)、金不足拒绝(invalid_state)、
///      使者不在待命名单拒绝(person_not_free)、自势力目标拒绝(target_force_invalid);
///   2. 送礼端到端:派遣即扣 1000 金 + 使者离城(PersonDiplomacy 任务)→ 按城图路程
///      逐日赶赴 → 送达后对方君主城 +1000 金、RelationMap 关系上升、[外交] 消息行
///      (GameEvent.OnDiplomacySendGift → SangoDiplomacyAnnals)、话题面(sango.world.
///      diplomacy)关系行反映新值;
///   3. 结盟端到端(成功率拉满的确定性面):关系数据面预热 ≥2000 后派遣 → 送达 →
///      allianceSet 增同盟(双方 AllianceList 可见)、关系 +500、消息行;随后摒弃同盟
///      → 双方 AllianceList 摘除;中途存档回灌后 AllianceList 捕获面保真(IsAlliance
///      跨存档不丢);
///   4. 确定性:同种子双跑终态 digest 与关系值逐位一致;
///   5. journal 唯一漏斗:diplomacyCommand 入 journal,同命令流重放 AllMatch。
/// </summary>
[TestFixture]
public sealed class SangoDiplomacyTests
{
    private const int Seed = 20260903;
    private const string SessionId = "sango-diplomacy-tests";

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

    private sealed class PlayerWorld : IDisposable
    {
        public readonly Scenario Scenario;
        public readonly Force PlayerForce;
        public readonly City Capital;
        public readonly SangoDiplomacyAnnals Annals;
        private readonly List<string> _lines = new();

        public PlayerWorld(int forceId, int capitalGoldFloor = 0)
        {
            SangoKernelBoot.BootWithPlayer(NewVfs(), "SangoContentMod", Seed, forceId, "Scenario/Scenario.json");
            Scenario = Scenario.Cur!;
            PlayerForce = Scenario.forceSet.Get(SangoPlayerTurnOps.PlayerForceId(Scenario))!;
            Assert.That(SangoTurnDriver.AdvanceTurn(), Is.EqualTo(SangoTurnDriver.TurnAdvanceResult.AwaitingPlayer));

            // 城选择在首个玩家回合门之后:军团行动力在回合链里回填,boot 即刻读是 0。
            Capital = PlayerCapital(Scenario, PlayerForce);
            if (capitalGoldFloor > 0 && Capital.gold < capitalGoldFloor)
            {
                Capital.gold = capitalGoldFloor;
            }

            Annals = new SangoDiplomacyAnnals();
            Annals.LinePublished += line => _lines.Add(line);
            Annals.Attach();
        }

        public string[] Lines => _lines.ToArray();

        /// <summary>推进到使者的 PersonDiplomacy 任务清空(送达+返程都可能带任务;
        /// 只等外交任务本体消失,返程任务 PersonReturn 不阻塞结算)。</summary>
        public void AdvanceUntilDiplomacyResolved(Person diplomat, int maxTurns = 40)
        {
            for (int turn = 0; turn < maxTurns; turn++)
            {
                SangoPlayerTurnOps.EndPlayerTurn();
                SangoTurnDriver.AdvanceTurn();
                if (diplomat.missionType != (int)MissionType.PersonDiplomacy)
                {
                    return;
                }
            }

            if (diplomat.missionType == (int)MissionType.PersonDiplomacy)
            {
                Assert.Fail($"diplomat {diplomat.Id} did not reach the receiver within {maxTurns} turns");
            }
        }

        public void Dispose() => Annals.Dispose();
    }

    static City PlayerCapital(Scenario scenario, Force force)
    {
        City? capital = force.CapitalCity;
        if (capital != null && capital.mBelongCorps != null && capital.freePersons.Count > 0 &&
            capital.mBelongCorps.ActionPoint >= 30 && capital.gold >= 1000)
        {
            return capital;
        }

        foreach (City? city in scenario.citySet)
        {
            if (city != null && city.mBelongForce == force && city.mBelongCorps != null &&
                city.freePersons.Count > 0 && city.mBelongCorps.ActionPoint >= 30 && city.gold >= 1000)
            {
                return city;
            }
        }

        throw new InvalidOperationException("the player force has no gate-passing diplomacy city at boot");
    }

    static Force NearestForeignForce(Scenario scenario, City origin, Force player)
    {
        Force? best = null;
        int bestDistance = int.MaxValue;
        foreach (Force? force in scenario.forceSet)
        {
            if (force == null || force == player || !force.IsAlive || force.mGovernor?.mBelongCity == null)
            {
                continue;
            }

            int distance = origin.Distance(force.mGovernor.mBelongCity);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = force;
            }
        }

        return best ?? throw new InvalidOperationException("no foreign force with a governor city");
    }

    static int FindPlayerForceId()
    {
        BootResult probe = SangoKernelBoot.Boot(NewVfs(), "SangoContentMod", Seed);
        foreach (Force? force in probe.Scenario.forceSet)
        {
            if (force != null && force.IsAlive && force.Id > 0 && force.CapitalCity != null)
            {
                return force.Id;
            }
        }

        throw new InvalidOperationException("the scenario has no playable alive force");
    }

    // ---- 门控面 ----

    [Test]
    public void DiplomacyGates_RejectForeignCity_BadDiplomat_SelfTarget_PoorCity()
    {
        int forceId = FindPlayerForceId();
        using var world = new PlayerWorld(forceId);
        Scenario scenario = world.Scenario;
        City? foreignCity = null;
        scenario.citySet.ForEach(city =>
        {
            if (foreignCity == null && city != null && city.mBelongForce != null && city.mBelongForce != world.PlayerForce)
            {
                foreignCity = city;
            }
        });
        Force target = NearestForeignForce(scenario, world.Capital, world.PlayerForce);
        Person diplomat = world.Capital.freePersons.First(person => person != null);

        Assert.That(SangoDiplomacyOps.Execute(scenario, foreignCity!, "sendGift", new[] { diplomat.Id }, target.Id).ErrorCode,
            Is.EqualTo("not_player_city"), "player worlds only dispatch diplomats from the player's own cities");

        Assert.That(SangoDiplomacyOps.Execute(scenario, world.Capital, "alliance", new[] { 999999 }, target.Id).ErrorCode,
            Is.EqualTo("person_not_free"), "the diplomat must sit in the dispatching city's free persons");

        Assert.That(SangoDiplomacyOps.Execute(scenario, world.Capital, "sendGift", new[] { diplomat.Id }, world.PlayerForce.Id).ErrorCode,
            Is.EqualTo("target_force_invalid"), "self diplomacy is rejected");

        int goldBackup = world.Capital.gold;
        world.Capital.gold = 500;
        Assert.That(SangoDiplomacyOps.Execute(scenario, world.Capital, "sendGift", new[] { diplomat.Id }, target.Id).ErrorCode,
            Is.EqualTo("invalid_state"), "the original IsValid gate requires city gold >= 1000");
        world.Capital.gold = goldBackup;

        Assert.That(SangoDiplomacyOps.Execute(scenario, world.Capital, "trade", new[] { diplomat.Id }, target.Id).ErrorCode,
            Is.EqualTo("invalid_payload"), "original-inactive action types are not invented here");
    }

    // ---- 送礼端到端 + 确定性 ----

    [Test]
    public void SendGift_EndToEnd_GoldRelationMessageTopic_Deterministic()
    {
        int forceId = FindPlayerForceId();

        (string Digest, int Relation, int ReceiverGold, string[] Lines, int Gift) Run(bool withMidSave)
        {
            using var world = new PlayerWorld(forceId);
            Scenario scenario = world.Scenario;
            Force player = world.PlayerForce;
            Force target = NearestForeignForce(scenario, world.Capital, player);
            Person diplomat = world.Capital.freePersons
                .Where(person => person != null)
                .OrderByDescending(person => person.Politics + person.Glamour / 2)
                .First();

            int relationBefore = scenario.GetRelation(player, target);
            int cityGoldBefore = world.Capital.gold;
            const int gift = 1000;

            // 送礼落地的精确面:OnDiplomacySendGift(发射方, 接收方, 金额, 成败)在
            // PerformWithoutCheck 内触发;世界经济的月度漂移让金币绝对值不可解析断言,
            // 事件参数是转账的确定性真源(跨 run 相等由 digest 兜底)。
            var giftEvents = new List<(int SenderId, int ReceiverId, int Value, bool Success)>();
            GameEvent.EventDelegate<Force, Force, int, bool>? giftHandler = (sender, receiver, value, success) =>
            {
                int receiverTotalGold = 0;
                receiver.ForEachCity(city => receiverTotalGold += city.gold);
                TestContext.Progress.WriteLine(
                    $"[m3d-diplo] gift event: value={value} relation={Scenario.Cur!.GetRelation(sender, receiver)} receiverTotalGold={receiverTotalGold} senderFP={sender.FightPower} receiverFP={receiver.FightPower} turn={Scenario.Cur.Info.turnCount}");
                giftEvents.Add((sender.Id, receiver.Id, value, success));
            };
            GameEvent.OnDiplomacySendGift += giftHandler;
            try
            {
                var result = SangoDiplomacyOps.Execute(scenario, world.Capital, "sendGift", new[] { diplomat.Id }, target.Id);
                Assert.That(result.Succeeded, Is.True, $"sendGift dispatch must pass the original gates: {result.Message}");
                Assert.That(diplomat.missionType, Is.EqualTo((int)MissionType.PersonDiplomacy),
                    "the diplomat departs with the original PersonDiplomacy mission");
                Assert.That(world.Capital.freePersons, Does.Not.Contain(diplomat), "dispatch removes the diplomat from the city free list");
                Assert.That(world.Capital.gold, Is.EqualTo(cityGoldBefore - gift), "OnDispatch deducts the gift gold immediately");
                Assert.That(scenario.GetRelation(player, target), Is.EqualTo(relationBefore),
                    "the relation only moves after the envoy arrives (original resolution timing)");

                if (withMidSave)
                {
                    // 捕获点 = 派遣后的首个玩家门(尚无回合内 AI 决策面残态,M3.a 已证明的
                    // 干净边界);使者 PersonDiplomacy 任务面全序列化,回灌后续跑与直跑同序。
                    // 注:玩家势力回合中途(多回合推进后的门)的存档确定性存在既有缺口
                    //(jobCounter/AIPrepared/AICommandList 等回合内决策面状态不入档),
                    // 已按 tech-debt 立案移交,不在外交测试里追。
                    var participant = new SangoSaveParticipant(NewVfs(), "SangoContentMod");
                    participant.RestoreState(participant.CaptureState());
                    scenario = Scenario.Cur!;
                    player = scenario.forceSet.Get(player.Id)!;
                    target = scenario.forceSet.Get(target.Id)!;
                    diplomat = scenario.personSet.Get(diplomat.Id)!;
                    // 回灌按 curForceId 静默 drain 到当前势力,玩家阻塞位补一次步进重立。
                    SangoTurnDriver.AdvanceTurn();
                }

                world.AdvanceUntilDiplomacyResolved(diplomat);
            }
            finally
            {
                GameEvent.OnDiplomacySendGift -= giftHandler;
            }

            int relationAfter = scenario.GetRelation(player, target);
            Assert.That(relationAfter, Is.GreaterThan(relationBefore),
                "a delivered gift must raise the relation (send gift always succeeds)");
            Assert.That(giftEvents, Has.Count.EqualTo(1), "exactly one send-gift outcome event must fire");
            Assert.That(giftEvents[0].SenderId, Is.EqualTo(player.Id));
            Assert.That(giftEvents[0].ReceiverId, Is.EqualTo(target.Id));
            Assert.That(giftEvents[0].Value, Is.EqualTo(gift), "the original gift amount (JobTypes 22 cost = 1000) transfers");
            Assert.That(giftEvents[0].Success, Is.True, "a dispatched gift always lands");
            Assert.That(world.Lines.Any(line => line.Contains("赠送了") && line.Contains(target.Name)),
                "the diplomacy annals must publish the gift line");

            // 话题面:关系行携带新值(玩家中心的邦交面板真源)。
            var topic = new SangoWorldDiplomacyTopic(new SangoWorldFeed());
            var context = new WebUiTopicContext(SessionId, SangoWorldDiplomacyTopic.TopicName, RequestId: 11, Parameters: default);
            Assert.That(topic.TryCreateSnapshot(in context, out var packet), Is.True);
            using JsonDocument doc = JsonDocument.Parse(packet.Payload);
            JsonElement relations = doc.RootElement.GetProperty("relations");
            Assert.That(relations.GetArrayLength(), Is.GreaterThan(10), "the relation face covers the living force field");
            bool found = false;
            foreach (JsonElement row in relations.EnumerateArray())
            {
                if (row.GetProperty("forceId").GetInt32() == target.Id)
                {
                    Assert.That(row.GetProperty("relation").GetInt32(), Is.EqualTo(relationAfter));
                    found = true;
                }
            }

            Assert.That(found, Is.True, "the target force must appear in the projected relation rows");
            string topicJson = doc.RootElement.GetRawText();
            TestContext.Progress.WriteLine(
                $"[m3d-diplo] topic sample (tail): ...{topicJson[Math.Max(0, topicJson.Length - 500)..]}");
            TestContext.Progress.WriteLine(
                $"[m3d-diplo] sendGift: {world.PlayerForce.Name} -> {target.Name} relation {relationBefore}->{relationAfter}, lines={string.Join(" | ", world.Lines.Take(3))}");
            return (SangoTurnDriver.WorldDigest(), relationAfter, target.CapitalCity.gold, world.Lines, gift);
        }

        (string digestA, int relationA, int goldA, string[] linesA, _) = Run(withMidSave: false);
        (string digestB, int relationB, int goldB, string[] linesB, _) = Run(withMidSave: false);
        (string digestMid, int relationMid, int goldMid, _, _) = Run(withMidSave: true);

        Assert.That(relationB, Is.EqualTo(relationA), "same-seed gift runs must raise the relation identically");
        Assert.That(digestB, Is.EqualTo(digestA), "same-seed worlds must replay bit for bit");
        Assert.That(linesB, Is.EqualTo(linesA), "the annals lines must be identical across runs");
        TestContext.Progress.WriteLine($"[m3d-diplo] digestA={digestA} digestMid={digestMid} same={digestA == digestMid}");
        Assert.That(relationMid, Is.EqualTo(relationA), "a mid-travel save/load must not change the gift outcome");
        Assert.That(goldMid, Is.EqualTo(goldA), "receiver gold must match across the mid-save variant");
        TestContext.Progress.WriteLine($"[m3d-diplo] digest={digestA}");
    }

    // ---- 结盟端到端 + 摒弃 + AllianceList 捕获面 ----

    [Test]
    public void Alliance_EndToEnd_DiscardAlliance_SaveCaptureFace()
    {
        int forceId = FindPlayerForceId();
        using var world = new PlayerWorld(forceId, capitalGoldFloor: 8000);
        Scenario scenario = world.Scenario;
        Force target = NearestForeignForce(scenario, world.Capital, world.PlayerForce);
        Person diplomat = world.Capital.freePersons
            .Where(person => person != null)
            .OrderByDescending(person => person.Politics + person.Glamour / 2)
            .First();

        // 数据面预热:结盟门槛关系 ≥2000(原版 CanPerform 读 RelationMap);
        // 资金 3000 + 高政使者把成功率推到 100(基础 50 + 关系 20 + 金额 30,封顶)。
        int relationNow = scenario.GetRelation(world.PlayerForce, target);
        if (relationNow < 3000)
        {
            scenario.AddRelation(world.PlayerForce, target, 3000 - relationNow);
        }

        var result = SangoDiplomacyOps.Execute(scenario, world.Capital, "alliance", new[] { diplomat.Id }, target.Id, 3000);
        Assert.That(result.Succeeded, Is.True, $"alliance dispatch must pass the original gates: {result.Message}");

        world.AdvanceUntilDiplomacyResolved(diplomat);

        Alliance? alliance = null;
        scenario.allianceSet.ForEach(candidate =>
        {
            if (candidate != null && candidate.IsAlive && candidate.Contains(world.PlayerForce) && candidate.Contains(target))
            {
                alliance = candidate;
            }
        });
        Assert.That(alliance, Is.Not.Null, "the successful alliance must enter the scenario allianceSet");
        Assert.That(world.PlayerForce.IsAlliance(target), Is.True, "both sides see the alliance via AllianceList");
        Assert.That(target.IsAlliance(world.PlayerForce), Is.True);
        Assert.That(world.Lines.Any(line => line.Contains("缔结了同盟")), "the alliance line must reach the message stream");

        // 存档回灌:AllianceList 是"不入档但入决策面"状态,捕获面必须保真。
        var participant = new SangoSaveParticipant(NewVfs(), "SangoContentMod");
        participant.RestoreState(participant.CaptureState());
        Scenario restored = Scenario.Cur!;
        Force restoredPlayer = restored.forceSet.Get(world.PlayerForce.Id)!;
        Force restoredTarget = restored.forceSet.Get(target.Id)!;
        Assert.That(restoredPlayer.IsAlliance(restoredTarget), Is.True,
            "Force.AllianceList must survive the save boundary via the capture face (IsAlliance drives war/aggression decisions)");

        // 摒弃同盟(原版 CityDiplomacyDiscardAlliance.DoJob:双方名单直接摘除)。
        City? discardCity = null;
        restored.citySet.ForEach(city =>
        {
            if (discardCity == null && city != null && city.mBelongForce == restoredPlayer &&
                city.mBelongCorps != null && city.freePersons.Count > 0 &&
                city.mBelongCorps.ActionPoint >= 30 && city.gold >= 1000)
            {
                discardCity = city;
            }
        });
        Assert.That(discardCity, Is.Not.Null, "the player force must still hold a gate-passing city after the alliance campaign");
        Person discardWitness = discardCity!.freePersons.First(person => person != null);
        var discard = SangoDiplomacyOps.Execute(restored, discardCity, "discardAlliance", new[] { discardWitness.Id }, restoredTarget.Id);
        Assert.That(discard.Succeeded, Is.True, $"discardAlliance must pass the original gates: {discard.Message}");
        Assert.That(restoredPlayer.IsAlliance(restoredTarget), Is.False, "the discard removes the alliance from both member lists");
        Assert.That(restoredTarget.IsAlliance(restoredPlayer), Is.False);

        // 摒弃一个不存在的同盟 → 类型化拒绝。
        var discardAgain = SangoDiplomacyOps.Execute(restored, discardCity, "discardAlliance", new[] { discardWitness.Id }, restoredTarget.Id);
        Assert.That(discardAgain.ErrorCode, Is.EqualTo("no_active_alliance"));

        TestContext.Progress.WriteLine(
            $"[m3d-diplo] alliance: {restoredPlayer.Name} + {restoredTarget.Name} id={alliance!.Id} leftCount={alliance.leftCount}, then discarded");
    }

    // ---- journal 唯一漏斗 ----

    [Test]
    public void DiplomacyCommand_EntersJournal_AndReplaysBitIdentical()
    {
        int forceId = FindPlayerForceId();
        SangoCommandJournal.Clear();
        SangoPlayerTurnOps.SelectPlayerForce(NewVfs(), "SangoContentMod", Seed, forceId, "Scenario/Scenario.json");
        Assert.That(SangoTurnDriver.AdvanceTurn(), Is.EqualTo(SangoTurnDriver.TurnAdvanceResult.AwaitingPlayer));
        Scenario scenario = Scenario.Cur!;
        Force player = scenario.forceSet.Get(forceId)!;
        City capital = PlayerCapital(scenario, player);
        if (capital.gold < 5000)
        {
            capital.gold = 5000;
        }

        Force target = NearestForeignForce(scenario, capital, player);
        Person diplomat = capital.freePersons.Where(person => person != null).First();

        var result = SangoDiplomacyOps.Execute(scenario, capital, "sendGift", new[] { diplomat.Id }, target.Id);
        Assert.That(result.Succeeded, Is.True);

        for (int turn = 0; turn < 8; turn++)
        {
            SangoPlayerTurnOps.EndPlayerTurn();
            SangoTurnDriver.AdvanceTurn();
        }

        SangoJournalCommand[] commands = SangoCommandJournal.Snapshot();
        Assert.That(commands.Any(command => command.Kind == SangoReplayJournal.DiplomacyCommandKind), Is.True,
            "the diplomacy command must enter the command journal (single funnel)");

        var report = SangoReplayJournal.Replay(NewVfs(), "SangoContentMod", Seed, commands);
        Assert.That(report.AllMatch, Is.True,
            $"the diplomacy command stream must replay bit for bit (mismatches: {report.Mismatches.Count})");
        TestContext.Progress.WriteLine($"[m3d-diplo] replay steps={report.StepsChecked} all-match");
    }
}
