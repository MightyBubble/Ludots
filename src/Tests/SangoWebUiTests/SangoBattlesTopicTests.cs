using System.Text.Json;
using Ludots.Core.Modding;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;
using Sango.Core;
using Sango.Runtime;
using Sango.WebUi;

namespace Ludots.Tests.SangoWebUi;

/// <summary>
/// M2.d Web UI 投影验收:战报面板话题(sango.world.battles)与部队任务态字段对真实内核
/// 端到端。战报真源 = SangoCombatAnnals 结构化 per-battle 卡;任务态真源 = Troop.
/// missionType/missionTarget 的机会主义语义展示语。剧本与 SangoReplayTests 同构:
/// 最近互敌城对各编成一队 → 互授歼灭任务 → 推进至接火。
/// </summary>
[TestFixture]
public sealed class SangoBattlesTopicTests
{
    private const string SessionId = "sango-battles-tests";
    private const int Seed = 20260902;

    private SangoWorldFeed _feed = null!;
    private SangoCombatAnnals _annals = null!;

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

    [OneTimeSetUp]
    public void SeedMutualDestroyEncounter()
    {
        SangoKernelBoot.Boot(NewVfs(), "SangoContentMod", Seed, "Scenario/Scenario.json");
        var feed = new SangoWorldFeed();
        feed.AttachPlayerMessageSystem();
        feed.AttachCombatAnnals();
        _feed = feed;
        _annals = feed.CombatAnnals;

        SangoTurnDriver.AdvanceTurn();
        Scenario scenario = Scenario.Cur!;
        City? home = null;
        City? foe = null;
        int best = int.MaxValue;
        var cities = new List<City>();
        scenario.citySet.ForEach(city => cities.Add(city));
        foreach (City a in cities)
        {
            if (a?.mBelongForce == null || a.mBelongCorps == null || !PassGate(a))
            {
                continue;
            }

            foreach (City b in cities)
            {
                if (b == a || b?.mBelongForce == null || b.mBelongCorps == null || !a.IsEnemy(b) || !PassGate(b))
                {
                    continue;
                }

                int distance = scenario.Map.Distance(a.CenterCell, b.CenterCell);
                if (distance < best)
                {
                    (home, foe, best) = (a, b, distance);
                }
            }
        }

        Assert.That(home, Is.Not.Null, "scenario must expose a mutually hostile gate-passing city pair");
        Troop attacker = SeedTroop(scenario, home!);
        Troop defender = SeedTroop(scenario, foe!);
        attacker.SetMission(MissionType.TroopDestroyTroop, defender.Id);
        defender.SetMission(MissionType.TroopDestroyTroop, attacker.Id);

        bool sawBattle = false;
        for (int turn = 0; turn < 20; turn++)
        {
            SangoTurnDriver.AdvanceTurn();
            if (!sawBattle && _annals.SnapshotBattles().Length > 0)
            {
                sawBattle = true;
                // 首张卡出现后再推 3 回合:账本需要第二次观测才能呈现 start→end 的变化行。
                for (int extra = 0; extra < 3 && turn + extra + 1 < 20; extra++)
                {
                    SangoTurnDriver.AdvanceTurn();
                }

                break;
            }
        }

        Assert.That(sawBattle, Is.True,
            "the mutual-destroy encounter must produce battle cards within the turn budget");
    }

    static bool PassGate(City city)
    {
        int cost = JobType.GetJobCostAP((int)CityJobType.MakeTroop);
        return city.troops > 0 && city.food > 0 && city.freePersons.Count > 0 &&
               city.mBelongCorps!.ActionPoint >= cost;
    }

    static Troop SeedTroop(Scenario scenario, City city)
    {
        var persons = new List<int>();
        foreach (Person person in city.freePersons)
        {
            if (person != null && persons.Count < SangoTroopOps.MaxMembers)
            {
                persons.Add(person.Id);
            }
        }

        (SangoTroopOpResult result, Troop? troop) = SangoTroopOps.CreateTroop(
            scenario, city, persons, troops: 3000, food: 20_000);
        Assert.That(result.Succeeded && troop != null, Is.True, $"seed troop failed: {result.ErrorCode}");
        return troop!;
    }

    [Test]
    public void BattlesTopic_SnapshotCarriesStructuredBattleCards()
    {
        var producer = new SangoWorldBattlesTopic(_feed);
        var context = new WebUiTopicContext(SessionId, SangoWorldBattlesTopic.TopicName, RequestId: 7, Parameters: default);
        Assert.That(producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
        using JsonDocument doc = JsonDocument.Parse(packet.Payload);
        JsonElement root = doc.RootElement;
        Assert.That(root.GetProperty("turnCount").GetInt32(), Is.GreaterThan(0));
        JsonElement battles = root.GetProperty("battles");
        Assert.That(battles.GetArrayLength(), Is.GreaterThan(0));

        bool anyStrike = false;
        bool anyDamage = false;
        bool anyChange = false;
        foreach (JsonElement battle in battles.EnumerateArray())
        {
            Assert.That(battle.GetProperty("seq").GetInt64(), Is.GreaterThan(0));
            Assert.That(battle.GetProperty("turnStart").GetInt32(), Is.GreaterThan(0));
            Assert.That(battle.GetProperty("turnLast").GetInt32(), Is.GreaterThanOrEqualTo(battle.GetProperty("turnStart").GetInt32()));

            JsonElement attacker = battle.GetProperty("attacker");
            Assert.That(attacker.GetProperty("name").GetString(), Is.Not.Null.And.Not.Empty);
            Assert.That(attacker.GetProperty("forceName").GetString(), Is.Not.Null.And.Not.Empty);
            Assert.That(attacker.GetProperty("kind").GetString(), Is.EqualTo("troop"));
            JsonElement defender = battle.GetProperty("defender");
            Assert.That(defender.GetProperty("name").GetString(), Is.Not.Null.And.Not.Empty);

            anyDamage |= battle.GetProperty("damageDealt").GetInt32() > 0;
            string result = battle.GetProperty("result").GetString() ?? string.Empty;
            Assert.That(result, Is.Not.Empty, "every card carries a result (ongoing or terminal)");

            foreach (JsonElement change in battle.GetProperty("troopChanges").EnumerateArray())
            {
                anyChange |= change.GetProperty("end").GetInt32() < change.GetProperty("start").GetInt32();
            }

            foreach (JsonElement e in battle.GetProperty("events").EnumerateArray())
            {
                anyStrike |= e.GetProperty("kind").GetString() == "strike" && e.GetProperty("damage").GetInt32() > 0;
            }
        }

        Assert.That(anyStrike, Is.True, "cards must carry strike events with damage (伤害序列)");
        Assert.That(anyDamage, Is.True, "cards must aggregate damage");
        Assert.That(anyChange, Is.True, "cards must carry troop count changes (troopChanges)");
    }

    [Test]
    public void TroopsTopic_RowsCarryMissionStateWithOpportunisticWording()
    {
        var producer = new SangoWorldTroopsTopic(_feed);
        var context = new WebUiTopicContext(SessionId, SangoWorldTroopsTopic.TopicName, RequestId: 9, Parameters: default);
        Assert.That(producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
        using JsonDocument doc = JsonDocument.Parse(packet.Payload);
        JsonElement troops = doc.RootElement.GetProperty("troops");
        Assert.That(troops.GetArrayLength(), Is.GreaterThan(0), "the seeded encounter leaves live troops");

        JsonElement? missioned = null;
        string? missionedLabel = null;
        int missionedId = 0;
        foreach (JsonElement troop in troops.EnumerateArray())
        {
            if (troop.GetProperty("missionType").GetInt32() == (int)MissionType.TroopDestroyTroop && missioned == null)
            {
                missioned = troop;
                missionedLabel = troop.GetProperty("missionLabel").GetString();
                missionedId = troop.GetProperty("id").GetInt32();
            }

            if (troop.GetProperty("missionType").GetInt32() == (int)MissionType.TroopOccupyCity)
            {
                string occupyLabel = troop.GetProperty("missionLabel").GetString() ?? string.Empty;
                Assert.That(occupyLabel, Does.Contain("攻占").And.Contains("顺势"),
                    "occupation missions must be phrased with opportunistic semantics");
            }
        }

        Assert.That(missioned, Is.Not.Null, "a mutual-destroy mission troop must be projected");
        Assert.That(missionedLabel, Does.Contain("歼灭"), "destroy missions use the destroy wording");
        string? targetName = null;
        Scenario.Cur!.troopsSet.ForEach(troop =>
        {
            if (targetName == null && troop != null && troop.IsAlive && troop.Id != missionedId)
            {
                targetName = troop.Name;
            }
        });
        if (targetName != null)
        {
            Assert.That(missionedLabel, Does.Contain(targetName!),
                "mission labels must name the appointed target");
        }
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        _annals.Dispose();
    }
}
