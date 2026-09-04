using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Ludots.Core.Modding;
using NUnit.Framework;

namespace Sango.Tests
{
    /// <summary>
    /// M3.f AI 外交激活验收。激活面 = Force.AIPrepare 的 AICommandList.Add(ForceAI.AIDiplomacy)
    /// (上游注释停用行,移植侧唯一内核改动);AIDiplomacy(ForceAI:57-372)依赖面评估结论:
    ///   完整子集 = 停战(DiplomacyActionTruce)/结盟(DiplomacyActionAlliance)/送礼
    ///   (DiplomacyActionSendGift),全部四方法齐 + GameEvent 尾事件;空壳子集 = Trade/
    ///   AllianceRequest/Marriage(CreateDiplomacyAction default→null → 成功率 0 → 静默跳过,
    ///   无异常路径);使者推荐/邻居表/免疫期/关系矩阵全在库且可存档(RelationMap/
    ///   DiplomacyImmunityTime [JsonProperty])。执行语义(上游 quirk,随激活生效):
    ///   PerformDiplomacyAction 即时 Perform,不派使者;送礼只给接收方加金不扣发送方
    ///   (扣款在无人调用的 OnDispatch)——金币铸造量在长跑测试里量化报告。
    /// 覆盖:
    ///   1. 激活后 AI 外交真实发生:跨过 TurnCount>=10 门后推进,[外交] 消息行(送礼/结盟/
    ///      停战)出现,RelationMap 相对开门时刻漂移,allianceSet 有同盟(或同盟拒绝行可见);
    ///   2. 长时稳定:30 回合无异常,输出金币铸造/同盟/关系统计(世界平衡影响量化);
    ///   3. 确定性:同种子双跑 digest 逐位相等,换种子发散;
    ///   4. 存档:激活面内跨存档续跑 == 直跑(免疫期/关系/同盟全入档)。
    /// </summary>
    [TestFixture]
    public sealed class SangoAiDiplomacyTests
    {
        private const int Seed = 20260902;
        private const int DivergentSeed = 19940801;

        // AIDiplomacy 的 TurnCount>=10 门 + 月度关系漂移需要跨月:36 回合 ≈ 12 旬月界。
        private const int LongRunTurns = 36;

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

        private static Assembly LoadSangoSimMod()
        {
            string dll = Path.Combine(
                RepoRoot(), "mods", "sango", "SangoSimMod", "bin", "net9.0", "SangoSimMod.dll");
            Assert.That(File.Exists(dll), Is.True, $"SangoSimMod build output missing: {dll} (run dotnet build first)");
            return Assembly.LoadFrom(dll);
        }

        private sealed class Kernel
        {
            public readonly Assembly Sim;

            public Kernel(Assembly sim, int seed)
            {
                Sim = sim;
                object bootResult = sim.GetType("Sango.Runtime.SangoKernelBoot", throwOnError: true)!
                    .GetMethod("Boot", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object[] { NewVfs(), "SangoContentMod", seed, "Scenario/Scenario.json" })!;
                Assert.That(PropertyValue(bootResult, "Scenario"), Is.Not.Null, "Boot must leave a booted Scenario.Cur");
            }

            public object Scenario =>
                Sim.GetType("Sango.Core.Scenario", throwOnError: true)!
                    .GetProperty("Cur", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

            public void AdvanceTurn() => Sim.GetType("Sango.Runtime.SangoTurnDriver", throwOnError: true)!
                .GetMethod("AdvanceTurn", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);

            public string WorldDigest() => (string)Sim.GetType("Sango.Runtime.SangoTurnDriver", throwOnError: true)!
                .GetMethod("WorldDigest", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;

            public object CreateDiplomacyAnnals(List<string>? sink = null)
            {
                object annals = Activator.CreateInstance(
                    Sim.GetType("Sango.Runtime.SangoDiplomacyAnnals", throwOnError: true)!)!;
                if (sink != null)
                {
                    annals.GetType().GetEvent("LinePublished")!.AddEventHandler(
                        annals, (Action<string>)(line => sink.Add(line)));
                }

                annals.GetType().GetMethod("Attach", BindingFlags.Public | BindingFlags.Instance)!.Invoke(annals, null);
                return annals;
            }

            public void DisposeAnnals(object annals) =>
                annals.GetType().GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance)!.Invoke(annals, null);

            public int TurnCount() => IntOf(PropertyValue(Scenario, "Info")!, "turnCount");

            public int AllianceCount() => CountSet(FieldValue(Scenario, "allianceSet"));

            public int AliveForceCount() => AliveForces().Count;

            public int GetRelation(object a, object b) =>
                (int)Scenario.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Single(m => m.Name == "GetRelation" && m.GetParameters().Length == 2)
                    .Invoke(Scenario, new[] { a, b })!;

            public List<object> AliveForces() => EnumerateSet(FieldValue(Scenario, "forceSet"))
                .Where(force => BoolOf(force, "IsAlive"))
                .ToList();

            public object Capture()
            {
                object participant = Activator.CreateInstance(
                    Sim.GetType("Sango.Runtime.SangoSaveParticipant", throwOnError: true)!,
                    new object[] { NewVfs(), "SangoContentMod", "Scenario/Scenario.json" })!;
                return participant.GetType()
                    .GetMethod("CaptureState", BindingFlags.Public | BindingFlags.Instance)!.Invoke(participant, null)!;
            }

            public void Restore(object capture)
            {
                object participant = Activator.CreateInstance(
                    Sim.GetType("Sango.Runtime.SangoSaveParticipant", throwOnError: true)!,
                    new object[] { NewVfs(), "SangoContentMod", "Scenario/Scenario.json" })!;
                participant.GetType()
                    .GetMethod("RestoreState", BindingFlags.Public | BindingFlags.Instance)!.Invoke(participant, new object[] { capture });
            }

            static int CountSet(object set) => EnumerateSet(set).Count();

            static IEnumerable<object> EnumerateSet(object set)
            {
                var enumerator = (IEnumerator)set.GetType()
                    .GetMethod("GetEnumerator", Type.EmptyTypes)!.Invoke(set, null)!;
                while (enumerator.MoveNext())
                {
                    if (enumerator.Current != null)
                    {
                        yield return enumerator.Current;
                    }
                }
            }
        }

        static object? PropertyValue(object target, string name)
        {
            Type type = target.GetType();
            return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
                ?? type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
        }

        static object FieldValue(object target, string name)
        {
            Type type = target.GetType();
            return type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
                ?? type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(target)!;
        }

        static int IntOf(object target, string member)
        {
            object? value = target.GetType().GetField(member, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
                ?? target.GetType().GetProperty(member, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
            return Convert.ToInt32(value);
        }

        static bool BoolOf(object target, string member)
        {
            object? value = target.GetType().GetProperty(member, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
                ?? target.GetType().GetField(member, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
            return Convert.ToBoolean(value);
        }

        static string ForceName(object force) => PropertyValue(force, "Name")?.ToString() ?? "未知势力";

        // ---- 断言面 ----

        [Test]
        public void AiDiplomacy_FiresPastTurnGate_RelationsAlliancesAndAnnalsVisible()
        {
            Assembly sim = LoadSangoSimMod();
            var kernel = new Kernel(sim, Seed);
            var lines = new List<string>();
            object annals = kernel.CreateDiplomacyAnnals(lines);
            try
            {
                // 推到开门时刻(TurnCount>=10)拍关系基线,再跨月推进让外交与月度漂移展开。
                while (kernel.TurnCount() < 10)
                {
                    kernel.AdvanceTurn();
                }

                int relationsAtGate = RelationFingerprint(kernel);
                int alliancesAtGate = kernel.AllianceCount();

                for (int i = 0; i < LongRunTurns; i++)
                {
                    kernel.AdvanceTurn();
                }

                Console.Out.WriteLine(
                    $"[m3f-aidip] turn {kernel.TurnCount()}: {kernel.AliveForceCount()} alive forces, " +
                    $"alliances {alliancesAtGate}→{kernel.AllianceCount()}, diplomacy lines {lines.Count}");
                foreach (string line in lines.Take(12))
                {
                    Console.Out.WriteLine($"[m3f-aidip-line] {line}");
                }

                Assert.That(lines.Any(line => line.StartsWith("[外交]")), Is.True,
                    "AI diplomacy must produce [外交] annals lines (gifts/alliances/truces) once past the turn gate");
                Assert.That(lines.Any(line => line.Contains("赠送了")), Is.True,
                    "the SendGift branch (complete kernel action) must fire under this seed");

                Assert.That(RelationFingerprint(kernel) != relationsAtGate || kernel.AllianceCount() != alliancesAtGate, Is.True,
                    "relations must drift and/or alliances must form under active AI diplomacy");
            }
            finally
            {
                kernel.DisposeAnnals(annals);
            }
        }

        [Test]
        public void AiDiplomacy_LongRun_30Turns_StableWithQuantifiedGoldMinting()
        {
            Assembly sim = LoadSangoSimMod();
            var kernel = new Kernel(sim, Seed);
            var lines = new List<string>();
            object annals = kernel.CreateDiplomacyAnnals(lines);
            try
            {
                for (int i = 0; i < 30; i++)
                {
                    kernel.AdvanceTurn();
                }

                int gifts = lines.Count(line => line.Contains("赠送了"));
                int alliancesFormed = lines.Count(line => line.Contains("缔结了同盟"));
                int truces = lines.Count(line => line.Contains("达成了停战"));
                int refusals = lines.Count(line => line.Contains("被拒绝") || line.Contains("未能送达"));
                int minted = lines
                    .Where(line => line.Contains("赠送了"))
                    .Select(line => int.Parse(System.Text.RegularExpressions.Regex.Match(line, @"赠送了(\d+)金").Groups[1].Value))
                    .Sum();
                Console.Out.WriteLine(
                    $"[m3f-aidip-stability] 30 turns: gifts={gifts} (minted {minted} gold into receivers), alliances={alliancesFormed}, truces={truces}, refusals={refusals}, alive forces={kernel.AliveForceCount()}/{48}");
                Assert.That(kernel.AliveForceCount(), Is.GreaterThan(0), "the world must stay alive across the stability window");
            }
            finally
            {
                kernel.DisposeAnnals(annals);
            }
        }

        [Test]
        public void AiDiplomacy_SameSeedBitIdentical_OtherSeedDiffers()
        {
            Assembly sim = LoadSangoSimMod();

            string RunScript(int seed)
            {
                var kernel = new Kernel(sim, seed);
                for (int i = 0; i < 20; i++)
                {
                    kernel.AdvanceTurn();
                }

                return kernel.WorldDigest();
            }

            string first = RunScript(Seed);
            string second = RunScript(Seed);
            string other = RunScript(DivergentSeed);
            Console.Out.WriteLine($"[m3f-aidip-determinism] same={first} other={other}");
            Assert.That(second, Is.EqualTo(first), "same-seed world with active AI diplomacy must replay bit for bit");
            Assert.That(other, Is.Not.EqualTo(first), "a different seed must diverge the world digest");
        }

        [Test]
        public void AiDiplomacy_MidRunSaveRestore_MatchesUnsavedChain()
        {
            Assembly sim = LoadSangoSimMod();

            string ChainWithSave()
            {
                var kernel = new Kernel(sim, Seed);
                for (int i = 0; i < 12; i++)
                {
                    kernel.AdvanceTurn();
                }

                // 开门后、跨存档边界:免疫期/关系矩阵/同盟面全在档内。
                object capture = kernel.Capture();
                kernel.Restore(capture);
                for (int i = 0; i < 5; i++)
                {
                    kernel.AdvanceTurn();
                }

                return kernel.WorldDigest();
            }

            string ChainWithoutSave()
            {
                var kernel = new Kernel(sim, Seed);
                for (int i = 0; i < 17; i++)
                {
                    kernel.AdvanceTurn();
                }

                return kernel.WorldDigest();
            }

            string across = ChainWithSave();
            string neverSaved = ChainWithoutSave();
            Console.Out.WriteLine($"[m3f-aidip-save] across={across} never={neverSaved}");
            Assert.That(across, Is.EqualTo(neverSaved),
                "mid-run save→restore→continue with active AI diplomacy must replay the unsaved chain bit for bit");
        }

        static int RelationFingerprint(Kernel kernel)
        {
            // 活势力对关系和(只读漂移信号,不做精确断言)。
            List<object> forces = kernel.AliveForces();
            int sum = 0;
            for (int i = 0; i < forces.Count; i++)
            {
                for (int j = i + 1; j < forces.Count; j++)
                {
                    sum += kernel.GetRelation(forces[i], forces[j]);
                }
            }

            return sum;
        }
    }
}
