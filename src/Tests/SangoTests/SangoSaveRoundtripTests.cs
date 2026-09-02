using System;
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using Ludots.Core.Modding;
using NUnit.Framework;

namespace Sango.Tests
{
    /// <summary>
    /// M1.d 存档域 sango.sim 验收:Capture 落 JsonNode(城市/武将/回合数/随机态)→
    /// Restore 回灌内核 → 被存时点 digest 逐位相等 → 跨存档边界续跑与从不存档的直跑链
    /// digest 逐位相等(随机流位置入档)→ 换种子各自内部一致且互相不同。
    /// 测试不引用 SangoSimMod 工程(csproj 不动),按 mod 主程序集产物路径加载并反射调用
    /// (同 SangoKernelBootTests 惯例)。
    /// </summary>
    [TestFixture]
    public sealed class SangoSaveRoundtripTests
    {
        private const int SeedA = 20260902;
        private const int SeedB = 19940801;
        private const int TurnsBeforeCapture = 3;
        private const int TurnsAfterRestore = 2;

        // 换种子分岔窗口:M1.b 确定性测试实证种子差异由武将层(忠诚流动/官职/状态)
        // 承接,3-5 回合内 digest 尚未分岔,10 回合起可辨(SangoTurnDriver.WorldDigest 注释)。
        private const int DivergenceTurns = 10;

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

        private static IVirtualFileSystem NewVfs()
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("SangoContentMod", Path.Combine(RepoRoot(), "mods", "sango", "SangoContentMod"));
            return vfs;
        }

        private static object Boot(Assembly sim, int seed)
        {
            Type bootType = sim.GetType("Sango.Runtime.SangoKernelBoot", throwOnError: true)!;
            return bootType
                .GetMethod("Boot", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object[] { NewVfs(), "SangoContentMod", seed, "Scenario/Scenario.json" })!;
        }

        private static object CreateParticipant(Assembly sim, IVirtualFileSystem vfs)
        {
            Type participantType = sim.GetType("Sango.Runtime.SangoSaveParticipant", throwOnError: true)!;
            return Activator.CreateInstance(
                participantType, new object[] { vfs, "SangoContentMod", "Scenario/Scenario.json" })!;
        }

        private static JsonNode Capture(Assembly sim, object participant)
        {
            return (JsonNode)participant.GetType()
                .GetMethod("CaptureState", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(participant, null)!;
        }

        private static void Restore(Assembly sim, object participant, JsonNode capture)
        {
            participant.GetType()
                .GetMethod("RestoreState", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(participant, new object[] { capture });
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

        private static int TurnCountOf(Assembly sim)
        {
            object scenario = CurScenario(sim);
            object info = scenario.GetType().GetProperty("Info")!.GetValue(scenario)!;
            return (int)info.GetType().GetField("turnCount")!.GetValue(info)!;
        }

        private static object CurScenario(Assembly sim)
        {
            Type scenarioType = sim.GetType("Sango.Core.Scenario", throwOnError: true)!;
            return scenarioType.GetProperty("Cur", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;
        }

        private static void RunTurns(Assembly sim, int turns)
        {
            for (int i = 0; i < turns; i++)
            {
                AdvanceTurn(sim);
            }
        }

        /// <summary>链 A:Boot→N 回合→Capture→Restore→再推 2 回合;返回(存时点 digest, 终点 digest)。</summary>
        private static (string AtCapture, string AtEnd) RunChainAcrossSaveBoundary(Assembly sim, IVirtualFileSystem vfs, int seed, int turnsBeforeCapture)
        {
            Boot(sim, seed);
            RunTurns(sim, turnsBeforeCapture);
            object participant = CreateParticipant(sim, vfs);
            JsonNode capture = Capture(sim, participant);
            string digestAtCapture = WorldDigest(sim);
            Restore(sim, participant, capture);
            RunTurns(sim, TurnsAfterRestore);
            return (digestAtCapture, WorldDigest(sim));
        }

        /// <summary>链 B:Boot→N+2 回合,从不存档;返回终点 digest。</summary>
        private static string RunChainNeverSaved(Assembly sim, int seed, int turnsBeforeCapture)
        {
            Boot(sim, seed);
            RunTurns(sim, turnsBeforeCapture + TurnsAfterRestore);
            return WorldDigest(sim);
        }

        [Test]
        public void Capture_AfterThreeTurns_ProducesNodeWithWorldAndRandomState()
        {
            Assembly sim = LoadSangoSimMod();
            IVirtualFileSystem vfs = NewVfs();
            Boot(sim, SeedA);
            RunTurns(sim, TurnsBeforeCapture);
            object participant = CreateParticipant(sim, vfs);

            JsonNode node = Capture(sim, participant);
            Assert.That(node, Is.Not.Null);

            JsonObject root = node.AsObject();
            JsonObject scenario = root["scenario"].AsObject();

            JsonObject personSet = scenario["personSet"].AsObject();
            Assert.That(personSet.Count, Is.GreaterThanOrEqualTo(800), "captured personSet must carry the full roster");

            JsonObject citySet = scenario["citySet"].AsObject();
            Assert.That(citySet.Count, Is.InRange(40, 200), "captured citySet must carry cities/gates/ports");

            int capturedTurnCount = scenario["Info"].AsObject()["turnCount"].GetValue<int>();
            Assert.That(capturedTurnCount, Is.EqualTo(TurnCountOf(sim)), "captured Info.turnCount must match the live kernel");

            JsonArray random = root["random"].AsArray();
            Assert.That(random.Count, Is.EqualTo(58), "GameRandom state is [inext, inextp, 56×seed]");

            TestContext.Progress.WriteLine(
                $"[save-a] persons={personSet.Count} cities={citySet.Count} turnCount={capturedTurnCount} randomLen={random.Count}");
        }

        [Test]
        public void Restore_IntoFreshBoot_ReproducesCapturePointDigest()
        {
            Assembly sim = LoadSangoSimMod();
            IVirtualFileSystem vfs = NewVfs();

            Boot(sim, SeedA);
            RunTurns(sim, TurnsBeforeCapture);
            object participant = CreateParticipant(sim, vfs);
            JsonNode capture = Capture(sim, participant);
            string digestAtCapture = WorldDigest(sim);

            Boot(sim, SeedA);
            Restore(sim, participant, capture);
            string digestAfterRestore = WorldDigest(sim);

            TestContext.Progress.WriteLine($"[save-b] atCapture={digestAtCapture} afterRestore={digestAfterRestore}");
            Assert.That(digestAfterRestore, Is.EqualTo(digestAtCapture),
                "restore into a fresh same-seed boot must reproduce the capture-point world digest bit for bit");
        }

        [Test]
        public void ContinueAcrossSaveBoundary_MatchesNeverSavedChain()
        {
            Assembly sim = LoadSangoSimMod();
            IVirtualFileSystem vfs = NewVfs();

            (string digestAtCapture, string digestChainA) = RunChainAcrossSaveBoundary(sim, vfs, SeedA, TurnsBeforeCapture);
            string digestChainB = RunChainNeverSaved(sim, SeedA, TurnsBeforeCapture);

            TestContext.Progress.WriteLine(
                $"[save-c] A(capture+restore+2t)={digestChainA} B(5t)={digestChainB} atCapture={digestAtCapture}");

            Assert.That(digestChainA, Is.EqualTo(digestChainB),
                "chain with a save/restore boundary must stay bit-identical to the never-saved chain (random stream position restored)");
            Assert.That(digestChainA, Is.Not.EqualTo(digestAtCapture),
                "the two extra turns must actually move the world (guards against a vacuous equality)");
        }

        [Test]
        public void DifferentSeed_StaysInternallyConsistentAndDivergesFromSeedA()
        {
            Assembly sim = LoadSangoSimMod();
            IVirtualFileSystem vfs = NewVfs();

            // 换种子重做 b:回灌复现存时点(10 回合窗口,分岔已可辨)。
            Boot(sim, SeedB);
            RunTurns(sim, DivergenceTurns);
            object participant = CreateParticipant(sim, vfs);
            JsonNode capture = Capture(sim, participant);
            string seedBDigestAtCapture = WorldDigest(sim);
            Boot(sim, SeedB);
            Restore(sim, participant, capture);
            Assert.That(WorldDigest(sim), Is.EqualTo(seedBDigestAtCapture),
                "seedB: restore into a fresh boot must reproduce the capture-point digest");

            // 换种子重做 c:跨存档边界一致。
            (string seedBDigestAtCapture2, string seedBDigestEnd) = RunChainAcrossSaveBoundary(sim, vfs, SeedB, DivergenceTurns);
            string seedBChainB = RunChainNeverSaved(sim, SeedB, DivergenceTurns);
            Assert.That(seedBDigestAtCapture2, Is.EqualTo(seedBDigestAtCapture),
                "seedB: capture point digest must be stable across the two chain runs");
            Assert.That(seedBDigestEnd, Is.EqualTo(seedBChainB),
                "seedB: save-boundary chain must equal the never-saved chain");

            // 与种子 A 的轨迹互相分岔。
            string seedAChainB = RunChainNeverSaved(sim, SeedA, DivergenceTurns);
            (string seedADigestAtCapture, _) = RunChainAcrossSaveBoundary(sim, vfs, SeedA, DivergenceTurns);

            TestContext.Progress.WriteLine(
                $"[save-d] seedB(12t)={seedBChainB} seedA(12t)={seedAChainB} atCaptureB={seedBDigestAtCapture} atCaptureA={seedADigestAtCapture}");

            Assert.That(seedBChainB, Is.Not.EqualTo(seedAChainB), "12-turn digests must diverge across seeds");
            Assert.That(seedBDigestAtCapture, Is.Not.EqualTo(seedADigestAtCapture), "capture-point digests must diverge across seeds");
        }
    }
}
