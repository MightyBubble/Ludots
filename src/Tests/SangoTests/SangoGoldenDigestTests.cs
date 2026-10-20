using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Ludots.Core.Modding;
using NUnit.Framework;

namespace Sango.Tests
{
    /// <summary>
    /// 跨提交 WorldDigest golden 基线(#1847 验收纪律的落地设施):固定种子跑
    /// SangoTurnDriver.WorldDigest,与检入的 golden 文件逐回合比对。同进程内的
    /// 确定性/回放/存档回环对拍由 SangoReplayTests/SangoSaveRoundtripTests 承担;
    /// 本测试补的是「改动前后跨提交」这条线——epic 各任务的「对拍逐位相等」以此
    /// 为门,合法断点(T0.1/T2.2/T3.6 子项)用 SANGO_GOLDEN_REGEN=1 重建并在 PR 里
    /// 附差异说明。
    /// golden 校准在合成图轨(DefaultMap.bin 缺席);bin 在场走真图,digest 不同,
    /// 按 RealMap 套件惯例跳过(真图轨的对拍由 RealMap_SaveRoundtrip 承担)。
    /// </summary>
    [TestFixture]
    public sealed class SangoGoldenDigestTests
    {
        private const int Seed = 20260902;
        private static readonly int[] ProbeTurns = { 1, 4, 12 };
        private const string GoldenFile = "sango_world_digest.synthetic.txt";

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

        private static string GoldenPath()
        {
            return Path.Combine(RepoRoot(), "src", "Tests", "SangoTests", "Golden", GoldenFile);
        }

        private static void RequireSyntheticLane()
        {
            string bin = Path.Combine(RepoRoot(), "mods", "sango", "SangoContentMod", "assets", "Map", "DefaultMap.bin");
            if (File.Exists(bin))
                Assert.Ignore($"golden is calibrated on the synthetic lane; DefaultMap.bin present (real-map digest differs): {bin}");
        }

        private static Assembly LoadSangoSimMod()
        {
            string dll = Path.Combine(
                RepoRoot(), "mods", "sango", "SangoSimMod", "bin", "net9.0", "SangoSimMod.dll");
            Assert.That(File.Exists(dll), Is.True, $"SangoSimMod build output missing: {dll} (run dotnet build first)");
            return Assembly.LoadFrom(dll);
        }

        private static void Boot(Assembly sim)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("SangoContentMod", Path.Combine(RepoRoot(), "mods", "sango", "SangoContentMod"));
            sim.GetType("Sango.Runtime.SangoKernelBoot", throwOnError: true)!
                .GetMethod("Boot", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object[] { vfs, "SangoContentMod", Seed, "Scenario/Scenario.json" });
        }

        private static void AdvanceTurn(Assembly sim)
        {
            sim.GetType("Sango.Runtime.SangoTurnDriver", throwOnError: true)!
                .GetMethod("AdvanceTurn", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, null);
        }

        private static string WorldDigest(Assembly sim)
        {
            return (string)sim.GetType("Sango.Runtime.SangoTurnDriver", throwOnError: true)!
                .GetMethod("WorldDigest", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, null)!;
        }

        private static Dictionary<int, string> RunProbeChain(Assembly sim)
        {
            Boot(sim);
            var digests = new Dictionary<int, string>();
            int turn = 0;
            int probeIndex = 0;
            while (probeIndex < ProbeTurns.Length)
            {
                AdvanceTurn(sim);
                turn++;
                if (turn == ProbeTurns[probeIndex])
                {
                    digests[turn] = WorldDigest(sim);
                    probeIndex++;
                }
            }

            return digests;
        }

        [Test]
        public void WorldDigest_MatchesCheckedInGolden_SyntheticLane()
        {
            RequireSyntheticLane();
            Assembly sim = LoadSangoSimMod();
            Dictionary<int, string> actual = RunProbeChain(sim);

            string goldenPath = GoldenPath();
            if (Environment.GetEnvironmentVariable("SANGO_GOLDEN_REGEN") == "1")
            {
                var lines = new List<string>
                {
                    "# sango WorldDigest golden - synthetic lane (no DefaultMap.bin)",
                    $"# seed={Seed} scenario=Scenario/Scenario.json probes={string.Join(",", ProbeTurns)}",
                };
                foreach (int turn in ProbeTurns)
                {
                    lines.Add($"turn={turn} {actual[turn]}");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
                File.WriteAllLines(goldenPath, lines);
                Assert.Pass($"golden regenerated: {goldenPath} (attach the git diff as the divergence note)");
            }

            Assert.That(File.Exists(goldenPath), Is.True,
                $"golden file missing: {goldenPath} (generate once with SANGO_GOLDEN_REGEN=1)");
            var expected = new Dictionary<int, string>();
            foreach (string line in File.ReadAllLines(goldenPath))
            {
                if (line.StartsWith("turn=", StringComparison.Ordinal))
                {
                    string[] parts = line.Split(' ');
                    expected[int.Parse(parts[0].AsSpan("turn=".Length))] = parts[1];
                }
            }

            foreach (int turn in ProbeTurns)
            {
                Assert.That(actual, Contains.Key(turn), $"golden has no probe for turn {turn}; regenerate golden");
                Assert.That(expected, Contains.Key(turn), $"golden file lacks turn {turn}; regenerate golden");
                Assert.That(actual[turn], Is.EqualTo(expected[turn]),
                    $"WorldDigest diverged at turn {turn} - behavior change vs golden " +
                    "(legal breakpoints rebuild golden via SANGO_GOLDEN_REGEN=1 and attach a divergence note)");
            }
        }
    }
}
