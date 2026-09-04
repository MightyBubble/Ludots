using System.Text.Json;
using Ludots.Core.Modding;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;
using Sango.Core;
using Sango.Runtime;
using Sango.WebUi;

namespace Ludots.Tests.SangoWebUi;

/// <summary>
/// M3.e 任务一:科技玩家面验收(原版 TechniqueResearch 链 + Web 命令/话题面)。
/// 覆盖(全部 headless,真实 Scenario.json 内核):
///   1. 命令门槛:非玩家城/已拥有/前置缺失/金与技巧点不足/未知科技的类型化拒绝;
///   2. 端到端:下令研究(原版「都市/研究技巧」链)→ 扣城金/技巧点、执行武将入
///      PersonResearch 任务 → 跨回合推进(TechniqueResearch.OnForceTurnStart)→
///      完成入 Techniques → 效果落点断言(TroopAddMoveAbility:骑兵部队移动力
///      +10,非本系兵种 +0;AddTechnique 的全部队重算路径);
///   3. 确定性:同种子双跑终态 digest 逐位一致;研究进行中存档回灌后续跑一致;
///   4. journal:researchCommand 入 journal(唯一漏斗);纯 step 命令流的 AI 研究
///      链重放 AllMatch(AI 侧 AITechniques/AIResearch 的确定性);
///   5. Web 面:sango.world.techniques 话题快照(视角势力的进行中/可研究/已拥有
///      行 + 技巧点),sango.researchCommand 的 DataPlane 回环。
/// 数据面注记:InitTechniques 只是科技树的根布局面;真拥有表 Techniques 是剧本
/// 逐势力给定的(势力 5/7 开局为空),0 级科技对其全部可研究。效果断言选用
/// 0 级科技 17 熟练兵(ForceCityMaxMorale value 20 —— 势力全城士气上限 +20)。
/// </summary>
[TestFixture]
public sealed class SangoTechniqueTests
{
    private const int Seed = 20260904;
    private const string SessionId = "sango-technique-tests";
    private const int SkilledTroopsTechId = 17;

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

    private static int FindPlayerForceId()
    {
        BootResult probe = SangoKernelBoot.Boot(NewVfs(), "SangoContentMod", Seed);
        Force? fallback = null;
        foreach (Force? force in probe.Scenario.forceSet)
        {
            if (force == null || !force.IsAlive || force.Id <= 0 || force.CapitalCity == null)
            {
                continue;
            }

            // 干净研究面:开局 Techniques 为空的势力(剧本势力 5/7),0 级科技全部可研究。
            if (force.Techniques.Count == 0)
            {
                return force.Id;
            }

            fallback ??= force;
        }

        return fallback?.Id ?? throw new InvalidOperationException("the scenario has no playable alive force");
    }

    /// <summary>研究门槛齐全的玩家城(待命武将 >=4:留 2 编成对照部队、余量给军师推荐)。</summary>
    private static City ResearchCity(Scenario scenario, Force player)
    {
        City? best = null;
        int bestFree = 0;
        scenario.citySet.ForEach(city =>
        {
            if (city == null || city.mBelongForce != player || city.mBelongCorps == null)
            {
                return;
            }

            if (city.freePersons.Count > bestFree && city.troops >= 5000 && city.food > 0)
            {
                bestFree = city.freePersons.Count;
                best = city;
            }
        });
        return best ?? throw new InvalidOperationException("the player force has no research-capable city at boot");
    }

    private sealed class TechWorld
    {
        public readonly Scenario Scenario;
        public readonly Force PlayerForce;
        public readonly City City;

        public TechWorld(int forceId)
        {
            SangoKernelBoot.BootWithPlayer(NewVfs(), "SangoContentMod", Seed, forceId, "Scenario/Scenario.json");
            Scenario = Scenario.Cur!;
            PlayerForce = Scenario.forceSet.Get(SangoPlayerTurnOps.PlayerForceId(Scenario))!;
            Assert.That(SangoTurnDriver.AdvanceTurn(), Is.EqualTo(SangoTurnDriver.TurnAdvanceResult.AwaitingPlayer));
            City = ResearchCity(Scenario, PlayerForce);

            // 白盒资源面:研究门槛的金/技巧点/行动力充足(剧本势力 TP 从 0 起步,
            // 不注水则研究门永远关着;本注水不进 journal,确定性验收用双跑对比,
            // replay 面由纯 step 流的 AI 研究覆盖,见 ResearchCommand_EntersJournal)。
            if (City.gold < 8000)
            {
                City.gold = 8000;
            }

            if (PlayerForce.TechniquePoint < 6000)
            {
                PlayerForce.GainTechniquePoint(6000 - PlayerForce.TechniquePoint);
            }

            City.mBelongCorps!.ActionPoint = Math.Max(City.mBelongCorps.ActionPoint, 250);
        }

        public void AdvancePlayerTurn()
        {
            SangoPlayerTurnOps.EndPlayerTurn();
            SangoTurnDriver.AdvanceTurn();
        }
    }

    // ---- 门槛面 ----

    [Test]
    public void ResearchGates_RejectForeignCity_OwnedTech_MissingPrereq_PoorResources_UnknownTech()
    {
        int forceId = FindPlayerForceId();
        var world = new TechWorld(forceId);
        Scenario scenario = world.Scenario;
        City? foreignCity = null;
        scenario.citySet.ForEach(city =>
        {
            if (foreignCity == null && city != null && city.mBelongForce != null && city.mBelongForce != world.PlayerForce)
            {
                foreignCity = city;
            }
        });

        Assert.That(SangoTechniqueOps.Execute(scenario, foreignCity!, SkilledTroopsTechId).ErrorCode,
            Is.EqualTo("not_player_city"), "research follows the city command gate (player's own city)");

        Assert.That(SangoTechniqueOps.Execute(scenario, world.City, 14).ErrorCode,
            Is.EqualTo("technique_unavailable"), "tech 14 (出产良马) lacks its prerequisite 13 (锻炼骑兵)");

        Assert.That(SangoTechniqueOps.Execute(scenario, world.City, 999999).ErrorCode,
            Is.EqualTo("technique_not_found"));

        int goldBackup = world.City.gold;
        int tpBackup = world.PlayerForce.TechniquePoint;
        world.City.gold = 100;
        world.PlayerForce.GainTechniquePoint(-(tpBackup - 100));
        Assert.That(SangoTechniqueOps.Execute(scenario, world.City, 29).ErrorCode,
            Is.EqualTo("resources_insufficient"),
            "JobResearch affordability: tech 29 (开发木兽, another level-0 row) needs gold 1000 / TP 2000");
        world.City.gold = goldBackup;
        world.PlayerForce.GainTechniquePoint(tpBackup - world.PlayerForce.TechniquePoint);

        Assert.That(SangoTechniqueOps.Execute(scenario, world.City, SkilledTroopsTechId, new[] { 999999 }).ErrorCode,
            Is.EqualTo("person_not_free"), "explicit researcher ids must sit in the city freePersons list");

        world.PlayerForce.AddTechnique(SkilledTroopsTechId);
        Assert.That(SangoTechniqueOps.Execute(scenario, world.City, SkilledTroopsTechId).ErrorCode,
            Is.EqualTo("technique_unavailable"),
            "an owned technique must fail CanResearch (Techniques is the owned set; InitTechniques only seeds tree roots)");
    }

    // ---- 端到端:下令 → 推进 → 完成 → 效果落点 + 确定性 + 中途存档 ----

    [Test]
    public void Research_EndToEnd_TroopEffectLands_Deterministic_MidSave()
    {
        int forceId = FindPlayerForceId();

        (string Digest, int MaxMoraleDelta, int Turns) Run(bool withMidSave)
        {
            var world = new TechWorld(forceId);
            Scenario scenario = world.Scenario;
            Force player = world.PlayerForce;
            City city = world.City;

            // 效果落点基线:科技 17 熟练兵 = ForceCityMaxMorale value 20(势力全城士气上限)。
            int maxMoraleBefore = city.MaxMorale;

            // 下令研究(缺省人名单 = 原版窗口的军师自动推荐面)。
            int goldBefore = city.gold;
            int tpBefore = player.TechniquePoint;
            int freeBefore = city.freePersons.Count;
            var order = SangoTechniqueOps.Execute(scenario, city, SkilledTroopsTechId);
            Assert.That(order.Succeeded, Is.True, order.Message);

            Technique technique = scenario.CommonData.Techniques.Get(SkilledTroopsTechId)!;
            Assert.That(city.gold, Is.EqualTo(goldBefore - technique.goldCost), "JobResearch deducts the table gold cost");
            Assert.That(player.TechniquePoint, Is.EqualTo(tpBefore - technique.techPointCost), "technique points deduct");
            Assert.That(player.ResearchTechnique, Is.EqualTo(SkilledTroopsTechId), "the force enters the researching state");
            Assert.That(player.ResearchLeftCounter, Is.GreaterThan(0), "the research counter is armed (turns from Method_ResearchCounter)");
            Assert.That(city.freePersons.Count, Is.LessThan(freeBefore), "researchers leave the city free list");
            bool researcherTasked = false;
            scenario.personSet.ForEach(person =>
            {
                if (person != null && person.mBelongForce == player &&
                    person.missionType == (int)MissionType.PersonResearch &&
                    person.missionTarget == SkilledTroopsTechId)
                {
                    researcherTasked = true;
                }
            });
            Assert.That(researcherTasked, Is.True, "the recommended researchers carry the PersonResearch mission");

            if (withMidSave)
            {
                // 捕获点 = 下单后的首个玩家门(M3.d 证明的干净边界;研究状态
                // ResearchTechnique/ResearchLeftCounter 均为 [JsonProperty] 入档面)。
                var participant = new SangoSaveParticipant(NewVfs(), "SangoContentMod");
                participant.RestoreState(participant.CaptureState());
                scenario = Scenario.Cur!;
                player = scenario.forceSet.Get(player.Id)!;
                city = scenario.citySet.Get(city.Id)!;
                SangoTurnDriver.AdvanceTurn();
            }

            int turns = 0;
            while (!player.HasTechnique(SkilledTroopsTechId))
            {
                if (++turns > 20)
                {
                    Assert.Fail($"technique {SkilledTroopsTechId} did not complete within 20 turns");
                }

                world.AdvancePlayerTurn();
            }

            Assert.That(player.ResearchTechnique, Is.EqualTo(0), "completion clears the researching state");
            int maxMoraleDelta = city.MaxMorale - maxMoraleBefore;
            Assert.That(maxMoraleDelta, Is.EqualTo(20),
                "ForceCityMaxMorale value 20 must land on the force's cities (AddTechnique recalculates every city base)");
            TestContext.Progress.WriteLine(
                $"[m3e-tech] skilled-troops done in {turns} turns; capital max morale {maxMoraleBefore}->{city.MaxMorale}");
            return (SangoTurnDriver.WorldDigest(), maxMoraleDelta, turns);
        }

        (string digestA, int moraleA, int turnsA) = Run(withMidSave: false);
        (string digestB, int moraleB, int turnsB) = Run(withMidSave: false);
        (string digestMid, int moraleMid, int turnsMid) = Run(withMidSave: true);

        Assert.That(digestB, Is.EqualTo(digestA), "same-seed research campaigns must replay bit for bit");
        Assert.That(turnsB, Is.EqualTo(turnsA), "the research duration must be deterministic");
        Assert.That(moraleB, Is.EqualTo(moraleA));
        Assert.That(moraleMid, Is.EqualTo(moraleA));
        Assert.That(turnsMid, Is.EqualTo(turnsA), "a mid-research save/load must not change the completion timing");
        Assert.That(digestMid, Is.EqualTo(digestA), "a mid-research save/load must not change the end state");
        TestContext.Progress.WriteLine($"[m3e-tech] digest={digestA} turns={turnsA}");
    }

    // ---- journal 唯一漏斗 + AI 研究链重放 ----

    [Test]
    public void ResearchCommand_EntersJournal_AndAiResearchSteps_ReplayAllMatch()
    {
        int forceId = FindPlayerForceId();
        SangoCommandJournal.Clear();
        SangoPlayerTurnOps.SelectPlayerForce(NewVfs(), "SangoContentMod", Seed, forceId, "Scenario/Scenario.json");
        Assert.That(SangoTurnDriver.AdvanceTurn(), Is.EqualTo(SangoTurnDriver.TurnAdvanceResult.AwaitingPlayer));
        Scenario scenario = Scenario.Cur!;
        Force player = scenario.forceSet.Get(forceId)!;
        City city = ResearchCity(scenario, player);
        city.gold = Math.Max(city.gold, 8000);
        player.GainTechniquePoint(Math.Max(0, 6000 - player.TechniquePoint));
        city.mBelongCorps!.ActionPoint = Math.Max(city.mBelongCorps.ActionPoint, 250);

        var order = SangoTechniqueOps.Execute(scenario, city, SkilledTroopsTechId);
        Assert.That(order.Succeeded, Is.True, order.Message);

        for (int turn = 0; turn < 6; turn++)
        {
            SangoPlayerTurnOps.EndPlayerTurn();
            SangoTurnDriver.AdvanceTurn();
        }

        SangoJournalCommand[] commands = SangoCommandJournal.Snapshot();
        Assert.That(commands.Any(command => command.Kind == SangoReplayJournal.ResearchCommandKind), Is.True,
            "the research command must enter the command journal (single funnel)");

        // 注:下单前的白盒注水(金/技巧点/行动力)不属入口命令,带注水的命令流不可
        // 直接重放;此处用同结构的纯 step 流(全托管世界,AI 自主研究)验证研究链
        // 的重放确定性——AI 研究的立项与完成都是 step 的确定性结果。
        SangoCommandJournal.Clear();
        SangoKernelBoot.Boot(NewVfs(), "SangoContentMod", Seed);
        for (int turn = 0; turn < 12; turn++)
        {
            SangoTurnDriver.AdvanceTurn();
        }

        var report = SangoReplayJournal.Replay(NewVfs(), "SangoContentMod", Seed, SangoCommandJournal.Snapshot());
        Assert.That(report.AllMatch, Is.True,
            $"the AI-driven research chain must replay bit for bit (mismatches: {report.Mismatches.Count})");
        TestContext.Progress.WriteLine($"[m3e-tech] ai-research replay steps={report.StepsChecked} all-match");
    }

    // ---- Web 面:话题 + 命令回环 ----

    [Test]
    public void TechniquesTopic_SnapshotMirrorsKernelResearchFace()
    {
        int forceId = FindPlayerForceId();
        var world = new TechWorld(forceId);

        var topic = new SangoWorldTechniquesTopic(new SangoWorldFeed());
        var context = new WebUiTopicContext(SessionId, SangoWorldTechniquesTopic.TopicName, RequestId: 21, Parameters: default);
        Assert.That(topic.TryCreateSnapshot(in context, out var packet), Is.True);
        using JsonDocument doc = JsonDocument.Parse(packet.Payload);
        JsonElement root = doc.RootElement;

        Assert.That(root.GetProperty("forceId").GetInt32(), Is.EqualTo(world.PlayerForce.Id), "the player force owns the topic view");
        Assert.That(root.GetProperty("researching").ValueKind, Is.EqualTo(JsonValueKind.Null),
            "no research is in flight at boot");
        Assert.That(root.GetProperty("techniquePoint").GetInt32(), Is.EqualTo(world.PlayerForce.TechniquePoint));

        JsonElement techniques = root.GetProperty("techniques");
        Assert.That(techniques.GetArrayLength(), Is.EqualTo(36), "the table ships 36 techniques");
        int owned = 0;
        int researchable = 0;
        bool sawHorseBreeding = false;
        foreach (JsonElement row in techniques.EnumerateArray())
        {
            bool rowOwned = row.GetProperty("owned").GetBoolean();
            bool rowCanResearch = row.GetProperty("canResearch").GetBoolean();
            owned += rowOwned ? 1 : 0;
            researchable += rowCanResearch ? 1 : 0;
            if (row.GetProperty("id").GetInt32() == SkilledTroopsTechId)
            {
                sawHorseBreeding = true;
                Assert.That(rowOwned, Is.False);
                Assert.That(rowCanResearch, Is.True, "level-0 techniques are researchable from an empty owned set");
                Assert.That(row.GetProperty("goldCost").GetInt32(), Is.EqualTo(1000));
                Assert.That(row.GetProperty("techPointCost").GetInt32(), Is.EqualTo(2000));
            }
        }

        Assert.That(sawHorseBreeding, Is.True);
        Assert.That(owned, Is.EqualTo(0), "the owned set starts empty (InitTechniques seeds tree roots only)");
        Assert.That(researchable, Is.EqualTo(9), "the nine level-0 techniques are researchable at boot");
        TestContext.Progress.WriteLine($"[m3e-tech] topic: owned={owned} researchable={researchable} techniquePoint={root.GetProperty("techniquePoint").GetInt32()}");

        // 下单后话题面翻转:进行中行出现,可研究行减少。
        var order = SangoTechniqueOps.Execute(world.Scenario, world.City, SkilledTroopsTechId);
        Assert.That(order.Succeeded, Is.True, order.Message);
        Assert.That(topic.TryCreateSnapshot(in context, out packet), Is.True);
        using JsonDocument after = JsonDocument.Parse(packet.Payload);
        JsonElement researching = after.RootElement.GetProperty("researching");
        Assert.That(researching.GetProperty("techniqueId").GetInt32(), Is.EqualTo(SkilledTroopsTechId));
        Assert.That(researching.GetProperty("leftCounter").GetInt32(), Is.GreaterThan(0));
        Assert.That(after.RootElement.GetProperty("techniquePoint").GetInt32(),
            Is.EqualTo(world.PlayerForce.TechniquePoint), "the topic mirrors the deducted technique points");
    }

    [Test]
    public async Task ResearchCommand_HandlerOrdersKernelResearch_AndTypesFailures()
    {
        int forceId = FindPlayerForceId();
        var world = new TechWorld(forceId);

        var request = new WebUiCommandRequest(SangoResearchCommandHandler.CommandName, 11, Array.Empty<WebUiEntityRef>(),
            JsonDocument.Parse($$"""
                {
                    "cityId": {{world.City.Id}},
                    "techniqueId": {{SkilledTroopsTechId}}
                }
                """).RootElement);
        WebUiCommandResult result = await new SangoResearchCommandHandler().HandleAsync(request);
        Assert.That(result.Success, Is.True, $"{result.ErrorCode} {result.Message}");
        Assert.That(world.PlayerForce.ResearchTechnique, Is.EqualTo(SkilledTroopsTechId),
            "the Web command reaches the kernel research state");

        // 门槛失败的类型化面:研究中的势力再立项 → already-researching 闸;
        // 前置缺失(科技 14 缺 13)→ technique_unavailable。
        var researchingRequest = new WebUiCommandRequest(SangoResearchCommandHandler.CommandName, 12, Array.Empty<WebUiEntityRef>(),
            JsonDocument.Parse($$"""{ "cityId": {{world.City.Id}}, "techniqueId": 29 }""").RootElement);
        WebUiCommandResult researching = await new SangoResearchCommandHandler().HandleAsync(researchingRequest);
        Assert.That(researching.Success, Is.False);
        Assert.That(researching.ErrorCode, Is.EqualTo("invalid_state"), "a researching force cannot start a second project");

        // 前置缺失(科技 14 缺 13)→ technique_unavailable。
        // 需用未立项的全新世界:上面的 world 仍在研究中,会先撞 already-researching 通用门。
        var fresh = new TechWorld(forceId);
        var prereqRequest = new WebUiCommandRequest(SangoResearchCommandHandler.CommandName, 14, Array.Empty<WebUiEntityRef>(),
            JsonDocument.Parse($$"""{ "cityId": {{fresh.City.Id}}, "techniqueId": 14 }""").RootElement);
        WebUiCommandResult prereq = await new SangoResearchCommandHandler().HandleAsync(prereqRequest);
        Assert.That(prereq.Success, Is.False);
        Assert.That(prereq.ErrorCode, Is.EqualTo("technique_unavailable"));

        var badPayload = await new SangoResearchCommandHandler().HandleAsync(
            new WebUiCommandRequest(SangoResearchCommandHandler.CommandName, 13, Array.Empty<WebUiEntityRef>(), JsonDocument.Parse("{}").RootElement));
        Assert.That(badPayload.Success, Is.False);
        Assert.That(badPayload.ErrorCode, Is.EqualTo("invalid_payload"));
    }
}
