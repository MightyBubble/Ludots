using System;
using System.IO;
using System.Reflection;
using Ludots.Core.Modding;
using NUnit.Framework;

namespace Sango.Tests
{
    /// <summary>
    /// T0.1(#1841/#1847):TerrainType.GetRandomFood 从未播种的 GameUtility.RandomDigit
    /// 改道 GameRandom 可种子化流。该路径在现有模拟链无活调用方(GetFood 的 isRandom
    /// 分支无人消费),WorldDigest 不受影响;本测试证明改道本身成立——同种子两次冷启动
    /// 的首次取值逐位相同,且调用前后 GameRandom 流状态不同(取数确实落在种子流上)。
    /// </summary>
    [TestFixture]
    public sealed class SangoTerrainFoodRandomTests
    {
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

        private static Assembly LoadSangoSimMod()
        {
            string dll = Path.Combine(
                RepoRoot(), "mods", "sango", "SangoSimMod", "bin", "net9.0", "SangoSimMod.dll");
            Assert.That(File.Exists(dll), Is.True, $"SangoSimMod build output missing: {dll} (run dotnet build first)");
            return Assembly.LoadFrom(dll);
        }

        private static void Boot(Assembly sim, int seed)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("SangoContentMod", Path.Combine(RepoRoot(), "mods", "sango", "SangoContentMod"));
            sim.GetType("Sango.Runtime.SangoKernelBoot", throwOnError: true)!
                .GetMethod("Boot", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object[] { vfs, "SangoContentMod", seed, "Scenario/Scenario.json" });
        }

        private static object GrassTerrain(Assembly sim)
        {
            object scenario = sim.GetType("Sango.Core.Scenario", throwOnError: true)!
                .GetProperty("Cur", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            object commonData = scenario.GetType().GetProperty("CommonData")!.GetValue(scenario)!;
            object terrainTypes = commonData.GetType().GetField("TerrainTypes")!.GetValue(commonData)!;
            return terrainTypes.GetType().GetMethod("Get")!.Invoke(terrainTypes, new object[] { 1 })!;
        }

        private static int FirstRandomFood(Assembly sim, int seed)
        {
            Boot(sim, seed);
            object grass = GrassTerrain(sim);
            return (int)grass.GetType().GetMethod("GetRandomFood")!.Invoke(grass, new object[] { 0 })!;
        }

        private static int[] RandomState(Assembly sim)
        {
            return (int[])sim.GetType("Sango.Core.GameRandom", throwOnError: true)!
                .GetMethod("ExportState", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, null)!;
        }

        [Test]
        public void GetRandomFood_ReadsFromSeededStream_SameSeedColdBootsAgree()
        {
            Assembly sim = LoadSangoSimMod();

            int foodA = FirstRandomFood(sim, Seed);
            int foodB = FirstRandomFood(sim, Seed);
            Assert.That(foodA, Is.EqualTo(foodB),
                "same seed must yield the same first GetRandomFood value (seeded stream)");

            // 取数确实落在 GameRandom 流上:同种子冷启动,调用一次 GetRandomFood 后
            // 流状态必须与不调用时不同(未播种旁路则两者相同)。
            Boot(sim, Seed);
            int[] stateWithoutFood = RandomState(sim);
            Boot(sim, Seed);
            object grass = GrassTerrain(sim);
            grass.GetType().GetMethod("GetRandomFood")!.Invoke(grass, new object[] { 0 });
            int[] stateWithFood = RandomState(sim);
            Assert.That(stateWithFood, Is.Not.EqualTo(stateWithoutFood),
                "GetRandomFood must consume the seeded GameRandom stream");
        }
    }
}
