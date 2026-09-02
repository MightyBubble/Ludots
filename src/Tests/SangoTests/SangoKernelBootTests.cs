using System;
using System.IO;
using System.Reflection;
using Ludots.Core.Modding;
using NUnit.Framework;

namespace Sango.Tests
{
    /// <summary>
    /// M1.b 内核启动 + 回合链验收:真实 Scenario.json 经 Ludots VFS 装出世界,headless
    /// 连推 10 回合,同种子确定性 digest 相等、换种子不同。
    /// 测试不引用 SangoSimMod 工程(csproj 不动),按 mod 主程序集产物路径加载(同
    /// SangoSimSmokeTests 惯例);对 SangoRuntime 的调用走反射。
    /// </summary>
    [TestFixture]
    public sealed class SangoKernelBootTests
    {
        private const int SeedA = 20260902;
        private const int SeedB = 19940801;
        private const int TurnsToAdvance = 10;

        private static Assembly LoadSangoSimMod()
        {
            string dll = Path.Combine(
                RepoRoot(), "mods", "sango", "SangoSimMod", "bin", "net9.0", "SangoSimMod.dll");
            Assert.That(File.Exists(dll), Is.True, $"SangoSimMod build output missing: {dll} (run dotnet build first)");
            return Assembly.LoadFrom(dll);
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

        private static IVirtualFileSystem NewVfs()
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("SangoContentMod", Path.Combine(RepoRoot(), "mods", "sango", "SangoContentMod"));
            return vfs;
        }

        private static object Boot(Assembly sim, int seed)
        {
            Type bootType = sim.GetType("Sango.Runtime.SangoKernelBoot", throwOnError: true)!;
            object result = bootType
                .GetMethod("Boot", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object[] { NewVfs(), "SangoContentMod", seed, "Scenario/Scenario.json" })!;
            Assert.That(result, Is.Not.Null, "Boot must return a BootResult");
            return result;
        }

        private static void AdvanceTurn(Assembly sim)
        {
            sim.GetType("Sango.Runtime.SangoTurnDriver", throwOnError: true)!
                .GetMethod("AdvanceTurn", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, null);
        }

        private static string DescribeTurn(Assembly sim)
        {
            return (string)sim.GetType("Sango.Runtime.SangoTurnDriver", throwOnError: true)!
                .GetMethod("DescribeTurn", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, null)!;
        }

        private static string WorldDigest(Assembly sim)
        {
            return (string)sim.GetType("Sango.Runtime.SangoTurnDriver", throwOnError: true)!
                .GetMethod("WorldDigest", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, null)!;
        }

        private static object CurScenario(Assembly sim)
        {
            Type scenarioType = sim.GetType("Sango.Core.Scenario", throwOnError: true)!;
            return scenarioType.GetProperty("Cur", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;
        }

        private static int CityGold(object city) => (int)city.GetType().GetField("gold")!.GetValue(city)!;

        private static int CityFood(object city) => (int)city.GetType().GetField("food")!.GetValue(city)!;

        private static int CityId(object city) => (int)city.GetType().GetProperty("Id")!.GetValue(city)!;

        private static object CitySetOf(Assembly sim)
        {
            object scenario = CurScenario(sim);
            return scenario.GetType().GetField("citySet")!.GetValue(scenario)!;
        }

        private static object? CityAt(object citySet, int index)
        {
            return citySet.GetType()
                .GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(citySet, new object[] { index });
        }

        private static int TurnCountOf(Assembly sim)
        {
            object scenario = CurScenario(sim);
            object info = scenario.GetType().GetProperty("Info")!.GetValue(scenario)!;
            return (int)info.GetType().GetField("turnCount")!.GetValue(info)!;
        }

        [Test]
        public void Boot_RealScenario_BuildsWorldFromVfsAssets()
        {
            Assembly sim = LoadSangoSimMod();
            object result = Boot(sim, SeedA);

            int persons = (int)result.GetType().GetProperty("Persons")!.GetValue(result)!;
            int cities = (int)result.GetType().GetProperty("Cities")!.GetValue(result)!;
            int forces = (int)result.GetType().GetProperty("Forces")!.GetValue(result)!;
            int systems = (int)result.GetType().GetProperty("RegisteredSystems")!.GetValue(result)!;

            // 剧本内容核对:personSet 850(库 + 剧本实例),citySet 88(城/关/港),forceSet 42。
            TestContext.Progress.WriteLine($"[boot] persons={persons} cities={cities} forces={forces} systems={systems}");

            Assert.That(persons, Is.GreaterThanOrEqualTo(800), "persons loaded from Scenario.json + PersonLibrary");
            Assert.That(cities, Is.InRange(40, 200), "cities (incl. gates/ports) from Scenario.json");
            Assert.That(forces, Is.InRange(8, 100), "forces from Scenario.json");

            // GameSystemManager 反射扫描在测试宿主下必须扫到 mod 程序集(D2 验证点)。
            Assert.That(systems, Is.GreaterThanOrEqualTo(1), "GameSystemManager.Init must register [GameSystem] singletons");

            // 至少一个城市带非零金粮(城市对象为真实剧本数据,非合成占位)。
            object citySet = CitySetOf(sim);
            int cityTotal = (int)citySet.GetType().GetProperty("Count")!.GetValue(citySet)!;
            bool anyGoldOrFood = false;
            int checkedCities = 0;
            for (int i = 0; i < cityTotal && checkedCities < 10; i++)
            {
                object? city = CityAt(citySet, i);
                if (city == null)
                {
                    continue;
                }
                checkedCities++;
                if (CityGold(city) > 0 || CityFood(city) > 0)
                {
                    anyGoldOrFood = true;
                    TestContext.Progress.WriteLine(
                        $"[boot] sample city id={CityId(city)} gold={CityGold(city)} food={CityFood(city)}");
                    break;
                }
            }

            Assert.That(anyGoldOrFood, Is.True, "at least one city should carry gold/food from the scenario file");
        }

        [Test]
        public void AdvanceTurn_TenTurns_InternalAffairsProgress()
        {
            Assembly sim = LoadSangoSimMod();
            Boot(sim, SeedA);

            object citySet = CitySetOf(sim);
            int turnCountBefore = TurnCountOf(sim);

            // 采样 3 个有归属势力的城市(内政 AI 会处置它们)。
            var samples = new System.Collections.Generic.List<(int id, int gold, int food)>();
            int cityTotal = (int)citySet.GetType().GetProperty("Count")!.GetValue(citySet)!;
            for (int i = 0; i < cityTotal && samples.Count < 3; i++)
            {
                object? city = CityAt(citySet, i);
                if (city == null)
                {
                    continue;
                }
                int belongForce = (int)city.GetType().GetField("BelongForce")!.GetValue(city)!;
                if (belongForce > 0)
                {
                    samples.Add((CityId(city), CityGold(city), CityFood(city)));
                }
            }

            Assert.That(samples.Count, Is.EqualTo(3), "scenario should offer at least 3 owned cities to sample");

            for (int i = 0; i < TurnsToAdvance; i++)
            {
                AdvanceTurn(sim);
                TestContext.Progress.WriteLine(DescribeTurn(sim));
            }

            int turnCountAfter = TurnCountOf(sim);
            Assert.That(turnCountAfter, Is.EqualTo(turnCountBefore + TurnsToAdvance), "10 turns must complete without exceptions");

            foreach ((int id, int gold, int food) in samples)
            {
                object? city = FindCityById(citySet, id);
                Assert.That(city, Is.Not.Null, $"sampled city {id} must remain resolvable");
                bool changed = CityGold(city!) != gold || CityFood(city!) != food;
                TestContext.Progress.WriteLine(
                    $"[turn10] city {id}: gold {gold}->{CityGold(city!)} food {food}->{CityFood(city!)}");
                Assert.That(changed, Is.True,
                    $"city {id} gold/food should drift under AI internal affairs over 10 turns");
            }
        }

        [Test]
        public void Determinism_SameSeedReplaysIdenticalDigest()
        {
            Assembly sim = LoadSangoSimMod();

            Boot(sim, SeedA);
            RunTurns(sim, TurnsToAdvance);
            string digestA1 = WorldDigest(sim);

            Boot(sim, SeedA);
            RunTurns(sim, TurnsToAdvance);
            string digestA2 = WorldDigest(sim);

            Boot(sim, SeedB);
            RunTurns(sim, TurnsToAdvance);
            string digestB = WorldDigest(sim);

            TestContext.Progress.WriteLine($"[determinism] seedA digest = {digestA1}");
            TestContext.Progress.WriteLine($"[determinism] seedB digest = {digestB}");

            Assert.That(digestA2, Is.EqualTo(digestA1), "same-seed full reboot + 10 turns must reproduce the world digest");
            Assert.That(digestB, Is.Not.EqualTo(digestA1), "a different seed must change the world trajectory");
        }

        private static void RunTurns(Assembly sim, int turns)
        {
            for (int i = 0; i < turns; i++)
            {
                AdvanceTurn(sim);
            }
        }

        private static object? FindCityById(object citySet, int id)
        {
            return citySet.GetType()
                .GetMethod("Get", new[] { typeof(int) })?
                .Invoke(citySet, new object[] { id });
        }
    }
}
