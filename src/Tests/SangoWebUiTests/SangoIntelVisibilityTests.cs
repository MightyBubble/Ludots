using System.Text.Json;
using Ludots.Core.Modding;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;
using Sango.Core;
using Sango.Runtime;
using Sango.WebUi;

namespace Ludots.Tests.SangoWebUi;

/// <summary>
/// M3.d 任务一:玩家视角情报面(原版"战争迷雾"语义的逆向结论落地)。
/// 逆向结论(sango-src,以源码为准):
///   · 原版无视野遮蔽系统:无 explored/unexplored 地图格、无 LOS、无按势力归属的
///     城/部队情报隐藏(MapFog 是季节大气距离雾,纯渲染;TroopInformation 的"全部队"
///     列表直接枚举 troopsSet 无过滤;CityInformation 对任意城输出同一字段全集)。
///   · 唯一"情报未探索"面 = PersonStateType.Invisible 未发现武将:藏于 City.
///     invisiblePersons(不入 allPersons/wildPersons),城内政"探索"(DoJobSearching,
///     概率 20+政治*3/5)发现后转 Unemployed 入 wildPersons 可见;另有 OnTurnStart
///     Chance(10) 的随机登场面。
/// 本文件断言数据面:玩家势力能看到什么 = 全图(城/部队/非未发现武将,无归属过滤),
/// 看不到什么 = invisiblePersons(任何城的投影面零泄漏),探索发现后转入可见面;
/// 同种子双跑确定性。
/// </summary>
[TestFixture]
public sealed class SangoIntelVisibilityTests
{
    private const int Seed = 20260903;

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

    private static int FindPlayerForceId(Scenario scenario)
    {
        foreach (Force? force in scenario.forceSet)
        {
            if (force != null && force.IsAlive && force.Id > 0)
            {
                return force.Id;
            }
        }

        throw new InvalidOperationException("the scenario has no alive force to play");
    }

    private static List<City> CitiesOf(Scenario scenario, Force force)
    {
        var cities = new List<City>();
        scenario.citySet.ForEach(city =>
        {
            if (city != null && city.mBelongForce == force)
            {
                cities.Add(city);
            }
        });
        return cities;
    }

    private static int CityGraphDistance(City a, City b) => a.Distance(b);

    [Test]
    public void PlayerIntelFace_FullWorldVisible_InvisiblePersonsHidden_SearchDiscovers()
    {
        int PlayerForceId = FindPlayerForceId(SangoKernelBoot.Boot(NewVfs(), "SangoContentMod", Seed).Scenario);

        (string Digest, int DiscoveredPersonId, int PlayerForceId, int VisiblePersons, int InvisibleTotal) RunWorld()
        {
            SangoKernelBoot.BootWithPlayer(NewVfs(), "SangoContentMod", Seed, PlayerForceId, "Scenario/Scenario.json");
            Scenario scenario = Scenario.Cur!;
            int playerForceId = SangoPlayerTurnOps.PlayerForceId(scenario);
            Force playerForce = scenario.forceSet.Get(playerForceId)!;
            Assert.That(SangoTurnDriver.AdvanceTurn(), Is.EqualTo(SangoTurnDriver.TurnAdvanceResult.AwaitingPlayer));

            // ---- 面一:全图可见(城,无归属过滤)----
            var citiesProducer = new SangoWorldCitiesTopic(new SangoWorldFeed());
            var context = new WebUiTopicContext("intel-tests", SangoWorldCitiesTopic.TopicName, RequestId: 7, Parameters: default);
            Assert.That(citiesProducer.TryCreateSnapshot(in context, out var citiesPacket), Is.True);
            using JsonDocument doc = JsonDocument.Parse(citiesPacket.Payload);
            JsonElement cityRows = doc.RootElement.GetProperty("cities");
            int kernelCityCount = 0;
            scenario.citySet.ForEach(_ => kernelCityCount++);
            Assert.That(cityRows.GetArrayLength(), Is.EqualTo(kernelCityCount),
                "the projected city face must cover every city (original has no per-force map fog)");
            var foreignForceIds = new HashSet<int>();
            foreach (JsonElement row in cityRows.EnumerateArray())
            {
                if (row.GetProperty("forceId").GetInt32() is int forceId && forceId > 0 && forceId != playerForceId)
                {
                    foreignForceIds.Add(forceId);
                }
            }

            Assert.That(foreignForceIds.Count, Is.GreaterThan(1),
                "enemy cities must be visible with their owning force (ownership does not hide)");

            // ---- 面一:全图可见(部队,含非玩家势力)----
            // 开局无部队;内政 AI 活化后他势力陆续出征,推进直到出现非玩家部队。
            int guard = 0;
            bool foreignTroopSeen = false;
            while (!foreignTroopSeen && guard++ < 16)
            {
                foreach (Troop? troop in scenario.troopsSet)
                {
                    if (troop != null && troop.IsAlive && troop.mBelongForce != playerForce)
                    {
                        foreignTroopSeen = true;
                        break;
                    }
                }

                if (!foreignTroopSeen)
                {
                    SangoPlayerTurnOps.EndPlayerTurn();
                    SangoTurnDriver.AdvanceTurn();
                }
            }

            var troopsProducer = new SangoWorldTroopsTopic(new SangoWorldFeed());
            context = new WebUiTopicContext("intel-tests", SangoWorldTroopsTopic.TopicName, RequestId: 8, Parameters: default);
            Assert.That(troopsProducer.TryCreateSnapshot(in context, out var troopsPacket), Is.True);
            using JsonDocument troopsDoc = JsonDocument.Parse(troopsPacket.Payload);
            JsonElement troopRows = troopsDoc.RootElement.GetProperty("troops");
            int aliveKernelTroops = 0;
            int foreignProjected = 0;
            scenario.troopsSet.ForEach(troop =>
            {
                if (troop != null && troop.IsAlive)
                {
                    aliveKernelTroops++;
                }
            });
            foreach (JsonElement row in troopRows.EnumerateArray())
            {
                if (row.GetProperty("forceId").GetInt32() != playerForceId)
                {
                    foreignProjected++;
                }
            }

            Assert.That(troopRows.GetArrayLength(), Is.EqualTo(aliveKernelTroops),
                "the projected troop face must cover every alive troop regardless of force");
            if (foreignTroopSeen)
            {
                Assert.That(foreignProjected, Is.GreaterThan(0),
                    "foreign troops are fully visible once they exist (original troop list has no filter)");
            }

            // ---- 面二:情报未探索(Invisible 零泄漏)----
            int invisibleTotal = 0;
            foreach (City? city in scenario.citySet)
            {
                if (city == null)
                {
                    continue;
                }

                invisibleTotal += city.invisiblePersons.Count;
                var projectedIds = new HashSet<int>();
                foreach (Person person in city.allPersons)
                {
                    projectedIds.Add(person.Id);
                }

                foreach (Person person in city.wildPersons)
                {
                    projectedIds.Add(person.Id);
                }

                foreach (Person person in city.invisiblePersons)
                {
                    Assert.That(projectedIds, Does.Not.Contain(person.Id),
                        $"invisible person {person.Id} leaked into the visible lists of city {city.Id}");
                    Assert.That(person.state, Is.EqualTo((int)PersonStateType.Invisible));
                }
            }

            Assume.That(invisibleTotal, Is.GreaterThan(0),
                "the scenario boot must seed undiscovered persons (state==0 random-city placement)");

            // ---- 面三:探索发现(玩家城,未发现 → 在野可见 + 消息行)----
            City? playerCity = CitiesOf(scenario, playerForce).FirstOrDefault(city =>
                city.invisiblePersons.Count > 0 && city.freePersons.Count > 0 &&
                city.mBelongCorps != null && city.mBelongCorps.ActionPoint >= 30);
            Assume.That(playerCity, Is.Not.Null,
                "the player force must own a gate-passing city with undiscovered persons at boot");
            var feed = new SangoWorldFeed();
            feed.AttachPlayerMessageSystem();

            int discoveredPersonId = 0;
            for (int attempt = 0; attempt < 40 && discoveredPersonId == 0; attempt++)
            {
                Person executor = playerCity.freePersons
                    .Where(person => person != null)
                    .OrderByDescending(person => person.Politics)
                    .First();
                var result = SangoCityOps.Execute(scenario, playerCity, "search", new[] { executor.Id });
                Assert.That(result.Succeeded, Is.True, $"search order must pass the original gates: {result.Message}");

                SangoPlayerTurnOps.EndPlayerTurn();
                SangoTurnDriver.AdvanceTurn();

                foreach (Person person in playerCity.wildPersons)
                {
                    if (person.state == (int)PersonStateType.Unemployed)
                    {
                        discoveredPersonId = person.Id;
                    }
                }
            }

            Assert.That(discoveredPersonId, Is.Not.EqualTo(0),
                "the seeded random stream must discover at least one person within the attempt budget");
            Person discovered = scenario.personSet.Get(discoveredPersonId)!;
            Assert.That(playerCity.invisiblePersons, Does.Not.Contain(discovered),
                "the discovered person must leave the invisible pool");
            Assert.That(discovered.state, Is.Not.EqualTo((int)PersonStateType.Invisible),
                "the discovered person is no longer undiscovered");
            SangoMessageRow[] messages = feed.SnapshotMessages();
            Assert.That(messages.Any(row => row.Text.Contains("发现人才")),
                "the original discovery message must reach the player message feed (player-gated path)");

            TestContext.Progress.WriteLine(
                $"[m3d-intel] player f{playerForceId} discovered person {discoveredPersonId} ({discovered.Name}) in {playerCity.Name}; invisible pool now {playerCity.invisiblePersons.Count}");

            int visiblePersons = 0;
            scenario.citySet.ForEach(city =>
            {
                if (city == null)
                {
                    return;
                }

                visiblePersons += city.allPersons.Count + city.wildPersons.Count;
            });
            return (SangoTurnDriver.WorldDigest(), discoveredPersonId, playerForceId, visiblePersons, invisibleTotal);
        }

        (string digestA, int discoveredA, int forceA, _, _) = RunWorld();
        (string digestB, int discoveredB, int forceB, _, _) = RunWorld();
        Assert.That(forceA, Is.EqualTo(forceB));
        Assert.That(discoveredA, Is.EqualTo(discoveredB),
            "the same seed must discover the same person (deterministic intel face)");
        Assert.That(digestA, Is.EqualTo(digestB), "same-seed worlds must replay bit for bit");
        TestContext.Progress.WriteLine($"[m3d-intel] digest={digestA}");
    }
}
