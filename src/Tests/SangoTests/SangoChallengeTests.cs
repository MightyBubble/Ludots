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
    /// M3.f 战斗演出验收(单挑/舌战;照 SangoCombatTests 惯例:反射调用 SangoRuntime,
    /// 不引用 SangoSimMod 工程)。逆向结论与接线合同见 SangoChallengeOps 文件头:
    /// 触发 = SkillInstance.Action 交锋幸存时刻(OnSkillDamageTroopAfter,邻格 + 概率),
    /// 解算全在内核(DuelSystem/DuelManager/Debate 族),结果落点 = 单挑内核 ±20 士气
    /// 直写 + 30% 俘将 + 20% 溃灭;舌战 ±10 士气(SangoChallengeOps 补全的唯一新语义)。
    /// 覆盖:
    ///   1. 单挑端到端:野战首击触发 → [单挑]/[单挑·终] 战报行(进 SangoCombatAnnals
    ///      同流)→ 士气落点逐位回对(内核 clamp 语义);俘将/溃灭附注与内核状态一致;
    ///   2. 舌战端到端:同一夹具 ForcedKind=Debate → [舌战]/[舌战·终] 行 + ±10 士气落点
    ///      (Troop.ChangeMorale 的 MaxMorale clamp);
    ///   3. 长时段混战(默认 5% 概率):互授歼灭任务 40 回合,挑战零异常,世界仍确定;
    ///   4. 确定性:带挑战的战斗剧本同种子双跑 digest 逐位相等,换种子发散;
    ///   5. 存档:首击挑战(带俘将终局,30% 路径正面覆盖)后入档回灌续跑 == 不入档链
    ///      (挑战全程同步结算,无跨帧状态;俘将三面经 cityOrder 捕获面回放,M3.g);
    ///   6. 溃灭收口(M3.g):20% 溃灭终局补 [溃灭] 行 + 涉战结构化卡全部终局收口
    ///      (Clear 不发 OnTroopDestroyed 是上游语义,收口在战报侧)。
    /// </summary>
    [TestFixture]
    public sealed class SangoChallengeTests
    {
        private const int Seed = 20260902;

        // 首击必须走攻击技能链(计略首手零伤不触发交锋幸存事件):固定种子梯取首个
        // 攻击首击的种子(与 SangoReplayTests 跨种子防假阳同法,梯本身确定)。
        private static readonly int[] SeedLadder = { 20260902, 20260903, 20260904, 20260905, 20260906, 20260907 };

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

            public object Map => PropertyValue(Scenario, "Map")!;

            public IEnumerable Cities() => EnumerateSet(FieldValue(Scenario, "citySet"));

            public IEnumerable Troops() => EnumerateSet(FieldValue(Scenario, "troopsSet"));

            public object? TroopById(int id) => Call(FieldValue(Scenario, "troopsSet"), "Get", id);

            public object GetNeighbor(object cell, int dir) => Call(Map, "GetNeighbor", cell, dir)!;

            public int Distance(object a, object b)
            {
                Type cellType = Sim.GetType("Sango.Core.Cell", throwOnError: true)!;
                MethodInfo method = Map.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Single(m => m.Name == "Distance" &&
                                 m.GetParameters().Length == 2 &&
                                 m.GetParameters()[0].ParameterType == cellType);
                return (int)method.Invoke(Map, new[] { a, b })!;
            }

            public void AdvanceTurn() => Sim.GetType("Sango.Runtime.SangoTurnDriver", throwOnError: true)!
                .GetMethod("AdvanceTurn", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);

            public string WorldDigest() => (string)Sim.GetType("Sango.Runtime.SangoTurnDriver", throwOnError: true)!
                .GetMethod("WorldDigest", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;

            public object CreateAnnals(List<string>? sink = null)
            {
                object annals = Activator.CreateInstance(
                    Sim.GetType("Sango.Runtime.SangoCombatAnnals", throwOnError: true)!)!;
                if (sink != null)
                {
                    annals.GetType().GetEvent("LinePublished")!.AddEventHandler(
                        annals, (Action<string>)(line => sink.Add(line)));
                }

                annals.GetType().GetMethod("Attach", BindingFlags.Public | BindingFlags.Instance)!.Invoke(annals, null);
                return annals;
            }

            public string[] AnnalsLines(object annals) =>
                (string[])annals.GetType()
                    .GetMethod("SnapshotLines", BindingFlags.Public | BindingFlags.Instance)!.Invoke(annals, null)!;

            public void DisposeAnnals(object annals) =>
                annals.GetType().GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance)!.Invoke(annals, null);

            public (bool Succeeded, string ErrorCode, string Message, object? Payload) MoveTroop(object troop, object destCell)
            {
                object tuple = Sim.GetType("Sango.Runtime.SangoTroopOps", throwOnError: true)!
                    .GetMethod("MoveTroop", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object[] { Scenario, troop, destCell })!;
                return ReadTuple(tuple);
            }

            public (bool Succeeded, string ErrorCode, string Message, object? Payload) CreateTroop(
                object city, int[] personIds, int troops, int gold, int food)
            {
                object tuple = Sim.GetType("Sango.Runtime.SangoTroopOps", throwOnError: true)!
                    .GetMethod("CreateTroop", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object?[] { Scenario, city, personIds, null, null, troops, gold, food })!;
                return ReadTuple(tuple);
            }

            public void SetDestroyMission(object troop, int targetTroopId)
            {
                Type missionType = Sim.GetType("Sango.Core.MissionType", throwOnError: true)!;
                object mission = Enum.Parse(missionType, "TroopDestroyTroop");
                Call(troop, "SetMission", mission, targetTroopId);
            }

            public void FillMoveRange(object troop)
            {
                object range = FieldValue(troop, "MoveRange");
                Call(range, "Clear");
                Call(Map, "GetMoveRange", troop, range);
            }

            public IEnumerable MoveRangeOf(object troop) => (IEnumerable)FieldValue(troop, "MoveRange");

            public void ResetActionOver(object troop) =>
                troop.GetType().GetProperty("ActionOver", BindingFlags.Public | BindingFlags.Instance)!.SetValue(troop, false);

            public void TeleportTroop(object troop, object destCell)
            {
                Call(troop, "UpdateCell", destCell, FieldValue(troop, "cell"), true);
            }

            // ---- M3.f 挑战面:概率/类型覆写 + Attach/Detach ----

            public void AttachChallenges(string? forcedKind, int chancePercent)
            {
                Type ops = Sim.GetType("Sango.Runtime.SangoChallengeOps", throwOnError: true)!;
                ops.GetField("ChallengeChancePercent", BindingFlags.Public | BindingFlags.Static)!.SetValue(null, chancePercent);
                object? kind = forcedKind == null
                    ? null
                    : Enum.Parse(Sim.GetType("Sango.Runtime.SangoChallengeKind", throwOnError: true)!, forcedKind);
                ops.GetField("ForcedKind", BindingFlags.Public | BindingFlags.Static)!.SetValue(null, kind);
                ops.GetMethod("Attach", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
            }

            public void DetachChallenges()
            {
                Type ops = Sim.GetType("Sango.Runtime.SangoChallengeOps", throwOnError: true)!;
                ops.GetMethod("Detach", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
                ops.GetField("ChallengeChancePercent", BindingFlags.Public | BindingFlags.Static)!.SetValue(null, 5);
                ops.GetField("ForcedKind", BindingFlags.Public | BindingFlags.Static)!.SetValue(null, null);
            }

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

            static (bool, string, string, object?) ReadTuple(object tuple)
            {
                Type t = tuple.GetType();
                object result = t.GetField("Item1")!.GetValue(tuple)!;
                object? item2 = t.GetFields().FirstOrDefault(field => field.Name == "Item2")?.GetValue(tuple);
                return (
                    (bool)result.GetType().GetProperty("Succeeded")!.GetValue(result)!,
                    (string)result.GetType().GetProperty("ErrorCode")!.GetValue(result)!,
                    (string)result.GetType().GetProperty("Message")!.GetValue(result)!,
                    item2);
            }
        }

        static IEnumerable EnumerateSet(object set)
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

        static object FieldValue(object target, string name)
        {
            Type type = target.GetType();
            return type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
                ?? type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(target)!;
        }

        static object? PropertyValue(object target, string name)
        {
            Type type = target.GetType();
            return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
                ?? type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
        }

        static object? Call(object target, string method, params object?[] args)
        {
            MethodInfo info = target.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Single(candidate => candidate.Name == method && candidate.GetParameters().Length == args.Length);
            return info.Invoke(target, args);
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

        static List<object> FreePersons(object city) => ((IEnumerable)FieldValue(city, "freePersons")).Cast<object>().ToList();

        static int MakeTroopCostAP(Assembly sim)
        {
            Type jobType = sim.GetType("Sango.Core.JobType", throwOnError: true)!;
            Type jobEnumType = sim.GetType("Sango.Core.CityJobType", throwOnError: true)!;
            int makeTroopId = (int)Enum.Parse(jobEnumType, "MakeTroop");
            return (int)jobType.GetMethod("GetJobCostAP", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { makeTroopId })!;
        }

        static bool PassExpeditionGate(object city)
        {
            int cost = MakeTroopCostAP(city.GetType().Assembly);
            return IntOf(city, "troops") > 0 && IntOf(city, "food") > 0 &&
                   FreePersons(city).Count > 0 &&
                   IntOf(PropertyValue(city, "mBelongCorps")!, "ActionPoint") >= cost &&
                   PropertyValue(city, "mBelongForce") != null && PropertyValue(city, "mBelongCorps") != null;
        }

        static object SeedTroopAtCity(Kernel kernel, object city, int troopsWanted)
        {
            var personIds = FreePersons(city)
                .OrderByDescending(person => IntOf(person, "Command"))
                .Take(3)
                .Select(person => IntOf(person, "Id"))
                .ToArray();
            (bool ok, string error, _, object? troop) = kernel.CreateTroop(
                city, personIds, troopsWanted, 500, IntOf(city, "food"));
            Assert.That(ok, Is.True, $"seed troop failed: {error}");
            Assert.That(troop, Is.Not.Null);
            return troop!;
        }

        private sealed class FieldEncounter
        {
            public readonly Kernel Kernel;
            public readonly object Attacker;
            public readonly object Defender;
            public readonly object DefenderCell;

            internal FieldEncounter(Kernel kernel, object attacker, object defender, object defenderCell)
            {
                Kernel = kernel;
                Attacker = attacker;
                Defender = defender;
                DefenderCell = defenderCell;
            }
        }

        static FieldEncounter SeedFieldEncounter(Kernel kernel)
        {
            kernel.AdvanceTurn();
            object home = kernel.Cities().Cast<object>().First(PassExpeditionGate);
            object attacker = SeedTroopAtCity(kernel, home, 3000);
            object attackerForce = PropertyValue(attacker, "mBelongForce")!;

            kernel.FillMoveRange(attacker);
            object? openCell = null;
            int bestDistance = -1;
            foreach (object cell in kernel.MoveRangeOf(attacker))
            {
                if (PropertyValue(cell, "troop") != null || PropertyValue(cell, "building") != null)
                {
                    continue;
                }

                int distance = Math.Abs(IntOf(cell, "x") - IntOf(attacker, "x")) +
                               Math.Abs(IntOf(cell, "y") - IntOf(attacker, "y"));
                if (distance > bestDistance)
                {
                    openCell = cell;
                    bestDistance = distance;
                }
            }

            Assert.That(openCell, Is.Not.Null, "the move range must expose an open cell away from the city block");
            (bool moved, string moveError, _, _) = kernel.MoveTroop(attacker, openCell!);
            Assert.That(moved, Is.True, $"attacker cannot leave the city block: {moveError}");
            kernel.ResetActionOver(attacker);

            object homeCenter = PropertyValue(home, "CenterCell")!;
            object foe = kernel.Cities().Cast<object>()
                .Where(city => PropertyValue(city, "mBelongForce") != null &&
                               !ReferenceEquals(PropertyValue(city, "mBelongForce"), attackerForce) &&
                               PassExpeditionGate(city))
                .OrderBy(city => kernel.Distance(homeCenter, PropertyValue(city, "CenterCell")!))
                .First();
            object defender = SeedTroopAtCity(kernel, foe, 3000);

            object attackerCell = FieldValue(attacker, "cell");
            object? dest = null;
            for (int dir = 0; dir < 6 && dest == null; dir++)
            {
                object neighbor = kernel.GetNeighbor(attackerCell, dir);
                if (neighbor != null && PropertyValue(neighbor, "troop") == null && PropertyValue(neighbor, "building") == null)
                {
                    dest = neighbor;
                }
            }

            Assert.That(dest, Is.Not.Null, "the attacker's open-field cell must expose an empty neighbor for the defender");
            kernel.TeleportTroop(defender, dest!);
            return new FieldEncounter(kernel, attacker, defender, dest!);
        }

        /// <summary>一次带挑战的首击:返回 (战报行日志, 首击是否为攻击技能)。调用方保证已 Attach 挑战与战报。</summary>
        static (List<string> Lines, bool StrikeLanded) StrikeOnce(Assembly sim, int seed, string forcedKind)
        {
            var kernel = new Kernel(sim, seed);
            FieldEncounter encounter = SeedFieldEncounter(kernel);
            var lines = new List<string>();
            object annals = kernel.CreateAnnals(lines);
            try
            {
                (bool ok, string error, string action, _) = kernel.MoveTroop(encounter.Attacker, encounter.DefenderCell);
                Assert.That(ok, Is.True, $"strike dispatch rejected: {error}");
                Assert.That(action, Is.EqualTo("field-strike"), "the occupied enemy-troop dispatch must route to the destroy mission");
            }
            finally
            {
                kernel.DisposeAnnals(annals);
            }

            return (lines, lines.Any(line => line.StartsWith("[战斗]")));
        }

        static int SelectStrikeSeed(Assembly sim, string forcedKind)
        {
            foreach (int seed in SeedLadder)
            {
                (List<string> lines, bool strikeLanded) = StrikeOnce(sim, seed, forcedKind);
                if (strikeLanded)
                {
                    return seed;
                }
            }

            throw new InvalidOperationException(
                "no seed in the fixed ladder produced an attack-skill first strike; widen the ladder");
        }

        /// <summary>带挑战的首击探测:返回 (首击是否攻击技, 挑战终局行)。</summary>
        static (bool StrikeLanded, string? ChallengeEndLine) StrikeWithChallengeOnce(Assembly sim, int seed, string forcedKind)
        {
            var kernel = new Kernel(sim, seed);
            FieldEncounter encounter = SeedFieldEncounter(kernel);
            var lines = new List<string>();
            object annals = kernel.CreateAnnals(lines);
            try
            {
                kernel.AttachChallenges(forcedKind, 100);
                (bool ok, string error, string action, _) = kernel.MoveTroop(encounter.Attacker, encounter.DefenderCell);
                Assert.That(ok, Is.True, $"strike dispatch rejected: {error}");
                Assert.That(action, Is.EqualTo("field-strike"), "the occupied enemy-troop dispatch must route to the destroy mission");
            }
            finally
            {
                kernel.DetachChallenges();
                kernel.DisposeAnnals(annals);
            }

            return (
                lines.Any(line => line.StartsWith("[战斗]")),
                lines.FirstOrDefault(line => line.StartsWith("[单挑·终]") || line.StartsWith("[舌战·终]")));
        }

        /// <summary>
        /// 存档回灌脚本的种子:首击为攻击技且单挑终局带俘将(被生擒)。M3.g 起俘虏三面
        /// (Troop.captiveList/Force.BeCaptiveList)按捕获序入 cityOrder 回放,带俘将的
        /// 存档链是正面验收面(此前用无俘将种子规避的洞已修)。
        /// </summary>
        static int SelectCapturingDuelSeed(Assembly sim)
        {
            foreach (int seed in SeedLadder.Concat(new[] { 20260908, 20260909, 20260910, 20260911, 20260912, 20260913 }))
            {
                (bool strikeLanded, string? endLine) = StrikeWithChallengeOnce(sim, seed, "Duel");
                if (strikeLanded && endLine != null && endLine.Contains("被生擒"))
                {
                    return seed;
                }
            }

            throw new InvalidOperationException(
                "no seed in the fixed ladder produced a capture-bearing attack-first duel; widen the ladder");
        }

        /// <summary>溃灭终局的种子:首击为攻击技且单挑终局带 20% 溃灭(全军溃灭)。</summary>
        static int SelectRoutDuelSeed(Assembly sim)
        {
            foreach (int seed in SeedLadder.Concat(new[] { 20260908, 20260909, 20260910, 20260911, 20260912, 20260913 }))
            {
                (bool strikeLanded, string? endLine) = StrikeWithChallengeOnce(sim, seed, "Duel");
                if (strikeLanded && endLine != null && endLine.Contains("全军溃灭"))
                {
                    return seed;
                }
            }

            throw new InvalidOperationException(
                "no seed in the fixed ladder produced a rout-bearing attack-first duel; widen the ladder");
        }

        // ---- 断言面 ----

        [Test]
        public void Duel_TriggeredFromFieldStrike_LandsMoraleCaptureAndAnnalsLines()
        {
            Assembly sim = LoadSangoSimMod();
            int seed = SelectStrikeSeed(sim, "Duel");
            Console.Out.WriteLine($"[m3f-duel-seed] {seed}");

            var kernel = new Kernel(sim, seed);
            FieldEncounter encounter = SeedFieldEncounter(kernel);
            object attacker = encounter.Attacker;
            object defender = encounter.Defender;
            int defenderTroops0 = IntOf(defender, "troops");

            var lines = new List<string>();
            object annals = kernel.CreateAnnals(lines);
            try
            {
                kernel.AttachChallenges("Duel", 100);
                (bool ok, string error, _, _) = kernel.MoveTroop(attacker, encounter.DefenderCell);
                Assert.That(ok, Is.True, $"strike dispatch rejected: {error}");
            }
            finally
            {
                kernel.DetachChallenges();
            }

            string? duelOpen = lines.FirstOrDefault(line => line.StartsWith("[单挑]"));
            string? duelEnd = lines.FirstOrDefault(line => line.StartsWith("[单挑·终]"));
            Assert.That(duelOpen, Is.Not.Null, "the annals must carry the duel open line naming both leaders");
            Assert.That(duelEnd, Is.Not.Null, "the annals must carry the duel end line");
            Console.Out.WriteLine($"[m3f-duel-open] {duelOpen}");
            Console.Out.WriteLine($"[m3f-duel-end] {duelEnd}");

            Assert.That(duelOpen!, Does.Contain(LeaderName(attacker)));
            Assert.That(duelOpen, Does.Contain(LeaderName(defender)));

            // 士气落点(DuelSystem.HandleAttackerWin/DefenderWin/HandleDraw 的直写语义):
            // 胜方 min(base+20,100),败方 max(base-20,0),平局双方 +5。
            // 基线取 [单挑] 行的开打快照(技能自身可能带士气副效果,夹具快照会被污染)。
            System.Text.RegularExpressions.Match[] moralePair = System.Text.RegularExpressions.Regex
                .Matches(duelOpen, @"士气 (\d+)")
                .Select(m => m)
                .ToArray();
            Assert.That(moralePair.Length, Is.EqualTo(2), $"the duel open line must carry both morale snapshots: {duelOpen}");
            int attackerMorale0 = int.Parse(moralePair[0].Groups[1].Value);
            int defenderMorale0 = int.Parse(moralePair[1].Groups[1].Value);
            System.Text.RegularExpressions.Match endPair = System.Text.RegularExpressions.Regex
                .Match(duelEnd!, @"攻军士气 (\d+),守军士气 (\d+)");
            Assert.That(endPair.Success, Is.True, $"the duel end line must carry both landed morales: {duelEnd}");
            int attackerMorale = int.Parse(endPair.Groups[1].Value);
            int defenderMorale = int.Parse(endPair.Groups[2].Value);
            if (duelEnd!.Contains("攻将获胜"))
            {
                Assert.That(attackerMorale, Is.EqualTo(Math.Min(attackerMorale0 + 20, 100)), "attacker morale must land +20 clamped at 100");
                Assert.That(defenderMorale, Is.EqualTo(Math.Max(defenderMorale0 - 20, 0)), "defender morale must land -20 floored at 0");
            }
            else if (duelEnd.Contains("守将获胜"))
            {
                Assert.That(defenderMorale, Is.EqualTo(Math.Min(defenderMorale0 + 20, 100)), "defender morale must land +20 clamped at 100");
                Assert.That(attackerMorale, Is.EqualTo(Math.Max(attackerMorale0 - 20, 0)), "attacker morale must land -20 floored at 0");
            }
            else
            {
                Assert.That(attackerMorale, Is.EqualTo(Math.Min(attackerMorale0 + 5, 100)), "draw must land attacker +5 clamped at 100");
                Assert.That(defenderMorale, Is.EqualTo(Math.Min(defenderMorale0 + 5, 100)), "draw must land defender +5 clamped at 100");
            }

            // 俘将/溃灭附注与内核状态一致(30%/20% 内核掷点,由战报行读出实际终局)。
            if (duelEnd.Contains("被生擒"))
            {
                object winnerTroop = duelEnd.Contains("攻将获胜") ? attacker : defender;
                object loserLeader = ReferenceEquals(winnerTroop, attacker) ? LeaderOf(defender) : LeaderOf(attacker);
                // SangoObjectList<Person> 不实现 IEnumerable(反射驱动其 GetEnumerator)。
                object captiveList = FieldValue(winnerTroop, "captiveList");
                var enumerator = (IEnumerator)captiveList.GetType()
                    .GetMethod("GetEnumerator", Type.EmptyTypes)!.Invoke(captiveList, null)!;
                bool found = false;
                while (enumerator.MoveNext())
                {
                    if (ReferenceEquals(enumerator.Current, loserLeader))
                    {
                        found = true;
                    }
                }

                Assert.That(found, Is.True, "the captured loser leader must sit in the winner troop's captive list");
            }

            if (duelEnd.Contains("全军溃灭"))
            {
                object loserTroop = duelEnd.Contains("攻将获胜") ? defender : attacker;
                Assert.That(BoolOf(loserTroop, "IsAlive"), Is.False, "the duel-destroyed troop must be dead");
                Assert.That(kernel.TroopById(IntOf(loserTroop, "Id")), Is.Null, "the duel-destroyed troop must leave troopsSet");

                // M3.g 单挑溃灭战报收口:Clear() 不发 OnTroopDestroyed(上游语义,和平
                // 解散共用该路径),战报侧在 [单挑·终] 补标准 [溃灭] 行;消息流里必须
                // 能看到该部队的溃灭行(败于对阵胜方)。
                string? routLine = lines.FirstOrDefault(line =>
                    line.StartsWith("[溃灭]") && line.Contains($"·{loserTroop.GetType().GetProperty("Name")!.GetValue(loserTroop)}("));
                Assert.That(routLine, Is.Not.Null,
                    "the duel-routed troop must carry a [溃灭] annals line (annals-side closure)");
                Console.Out.WriteLine($"[m3g-duel-rout] {routLine}");
            }
            else
            {
                // 未溃灭:兵力账不受单挑影响(单挑只动士气/俘将/溃灭三面;士气与俘将不在
                // WorldDigest 采集面,兵力行只受首击伤害影响)。
                Assert.That(IntOf(defender, "troops") <= defenderTroops0, Is.True, "the strike damage may reduce defender troops but the duel itself never adds any");
            }

            foreach (string line in lines.Take(8))
            {
                Console.Out.WriteLine($"[m3f-duel-annals] {line}");
            }
        }

        [Test]
        public void Duel_Rout_ClosesAnnalsLineAndBattleCards()
        {
            Assembly sim = LoadSangoSimMod();
            int seed = SelectRoutDuelSeed(sim);
            Console.Out.WriteLine($"[m3g-rout-seed] {seed}");

            var kernel = new Kernel(sim, seed);
            FieldEncounter encounter = SeedFieldEncounter(kernel);
            object attacker = encounter.Attacker;
            object defender = encounter.Defender;

            var lines = new List<string>();
            object annals = kernel.CreateAnnals(lines);
            try
            {
                kernel.AttachChallenges("Duel", 100);
                (bool ok, string error, _, _) = kernel.MoveTroop(attacker, encounter.DefenderCell);
                Assert.That(ok, Is.True, $"strike dispatch rejected: {error}");

                string? duelEnd = lines.FirstOrDefault(line => line.StartsWith("[单挑·终]"));
                Assert.That(duelEnd, Is.Not.Null);
                Assert.That(duelEnd!, Does.Contain("全军溃灭"), "the selected seed must end the duel with a rout");

                // M3.g 战报侧收口:Clear() 只发 OnTroopClear(上游语义,解散/吸收共用,
                // 内核补发击杀事件会让和平解散吃俘虏掷点)——战报必须在 [单挑·终] 之外
                // 补标准 [溃灭] 行,且结构化卡不再有该部队的 ongoing 卡。
                object loserTroop = duelEnd!.Contains("攻将获胜") ? defender : attacker;
                int loserId = IntOf(loserTroop, "Id");
                string loserName = (string)loserTroop.GetType().GetProperty("Name")!.GetValue(loserTroop)!;
                string? routLine = lines.FirstOrDefault(line =>
                    line.StartsWith("[溃灭]") && line.Contains($"·{loserName}("));
                Assert.That(routLine, Is.Not.Null, "the duel-routed troop must carry a [溃灭] line in the message stream");
                Console.Out.WriteLine($"[m3g-rout-line] {routLine}");

                var battles = (System.Array)annals.GetType()
                    .GetMethod("SnapshotBattles", BindingFlags.Public | BindingFlags.Instance)!.Invoke(annals, null)!;
                foreach (object battle in battles)
                {
                    object battleAttacker = battle.GetType().GetProperty("Attacker")!.GetValue(battle)!;
                    object battleDefender = battle.GetType().GetProperty("Defender")!.GetValue(battle)!;
                    bool involvesLoser = IdOf(battleAttacker) == loserId || IdOf(battleDefender) == loserId;
                    if (!involvesLoser)
                    {
                        continue;
                    }

                    string result = (string)battle.GetType().GetProperty("Result")!.GetValue(battle)!;
                    Assert.That(result, Is.Not.EqualTo("ongoing"),
                        "every battle card involving the duel-routed troop must be closed with a terminal result");
                    Assert.That(result, Is.EqualTo(
                        IdOf(battleDefender) == loserId ? "defender-destroyed" : "attacker-destroyed"),
                        "the closed card result must match the routed side");
                }
            }
            finally
            {
                kernel.DisposeAnnals(annals);
            }
        }

        static int IdOf(object participant) => (int)participant.GetType().GetProperty("Id")!.GetValue(participant)!;

        [Test]
        public void Debate_TriggeredFromFieldStrike_LandsMoraleAndAnnalsLines()
        {
            Assembly sim = LoadSangoSimMod();
            int seed = SelectStrikeSeed(sim, "Debate");
            Console.Out.WriteLine($"[m3f-debate-seed] {seed}");

            var kernel = new Kernel(sim, seed);
            FieldEncounter encounter = SeedFieldEncounter(kernel);
            object attacker = encounter.Attacker;
            object defender = encounter.Defender;
            int attackerMax = IntOf(attacker, "MaxMorale");
            int defenderMax = IntOf(defender, "MaxMorale");

            var lines = new List<string>();
            object annals = kernel.CreateAnnals(lines);
            try
            {
                kernel.AttachChallenges("Debate", 100);
                (bool ok, string error, _, _) = kernel.MoveTroop(attacker, encounter.DefenderCell);
                Assert.That(ok, Is.True, $"strike dispatch rejected: {error}");
            }
            finally
            {
                kernel.DetachChallenges();
            }

            string? debateOpen = lines.FirstOrDefault(line => line.StartsWith("[舌战]"));
            string? debateEnd = lines.FirstOrDefault(line => line.StartsWith("[舌战·终]"));
            Assert.That(debateOpen, Is.Not.Null, "the annals must carry the debate open line");
            Assert.That(debateEnd, Is.Not.Null, "the annals must carry the debate end line");
            Console.Out.WriteLine($"[m3f-debate-open] {debateOpen}");
            Console.Out.WriteLine($"[m3f-debate-end] {debateEnd}");

            Assert.That(debateOpen!, Does.Contain(LeaderName(attacker)));
            Assert.That(debateOpen, Does.Contain(LeaderName(defender)));

            // 舌战落点(SangoChallengeOps 补全语义):胜方 +10 / 败方 -10,平局不动;
            // 走 Troop.ChangeMorale(MaxMorale 夹取)。基线取 [舌战] 行快照,终值取
            // [舌战·终] 行快照(胜负由行内"X 驳倒 Y"的主语判定)。
            System.Text.RegularExpressions.Match[] moralePair = System.Text.RegularExpressions.Regex
                .Matches(debateOpen, @"士气 (\d+)")
                .ToArray();
            Assert.That(moralePair.Length, Is.EqualTo(2), $"the debate open line must carry both morale snapshots: {debateOpen}");
            int attackerMorale0 = int.Parse(moralePair[0].Groups[1].Value);
            int defenderMorale0 = int.Parse(moralePair[1].Groups[1].Value);
            System.Text.RegularExpressions.Match endPair = System.Text.RegularExpressions.Regex
                .Match(debateEnd!, @"攻军士气 (\d+),守军士气 (\d+)");
            Assert.That(endPair.Success, Is.True, $"the debate end line must carry both landed morales: {debateEnd}");
            int attackerMorale = int.Parse(endPair.Groups[1].Value);
            int defenderMorale = int.Parse(endPair.Groups[2].Value);
            if (debateEnd!.Contains("不分高下"))
            {
                Assert.That(attackerMorale, Is.EqualTo(attackerMorale0), "a debate draw must not touch attacker morale");
                Assert.That(defenderMorale, Is.EqualTo(defenderMorale0), "a debate draw must not touch defender morale");
            }
            else if (debateEnd.IndexOf(LeaderName(attacker)) < debateEnd.IndexOf(LeaderName(defender)))
            {
                Assert.That(attackerMorale, Is.EqualTo(Math.Min(attackerMorale0 + 10, attackerMax)), "attacker morale must land +10 clamped at MaxMorale");
                Assert.That(defenderMorale, Is.EqualTo(Math.Max(defenderMorale0 - 10, 0)), "defender morale must land -10 floored at 0");
            }
            else
            {
                Assert.That(defenderMorale, Is.EqualTo(Math.Min(defenderMorale0 + 10, defenderMax)), "defender morale must land +10 clamped at MaxMorale");
                Assert.That(attackerMorale, Is.EqualTo(Math.Max(attackerMorale0 - 10, 0)), "attacker morale must land -10 floored at 0");
            }

            foreach (string line in lines.Take(8))
            {
                Console.Out.WriteLine($"[m3f-debate-annals] {line}");
            }
        }

        [Test]
        public void MutualWar_DefaultChallengeChance_RunsCleanAcrossTurns()
        {
            Assembly sim = LoadSangoSimMod();
            var kernel = new Kernel(sim, Seed);
            FieldEncounter encounter = SeedFieldEncounter(kernel);
            var lines = new List<string>();
            object annals = kernel.CreateAnnals(lines);
            try
            {
                kernel.AttachChallenges(null, 5);
                (bool ok, string error, _, _) = kernel.MoveTroop(encounter.Attacker, encounter.DefenderCell);
                Assert.That(ok, Is.True, $"strike dispatch rejected: {error}");
                kernel.SetDestroyMission(encounter.Defender, IntOf(encounter.Attacker, "Id"));
                for (int turn = 0; turn < 40; turn++)
                {
                    kernel.AdvanceTurn();
                }
            }
            finally
            {
                kernel.DetachChallenges();
            }

            int duels = lines.Count(line => line.StartsWith("[单挑·终]"));
            int debates = lines.Count(line => line.StartsWith("[舌战·终]"));
            Console.Out.WriteLine($"[m3f-war] 40 turns of mutual war under the default 5% gate: {duels} duel(s), {debates} debate(s), {lines.Count} annals line(s)");
            Assert.That(lines.Count, Is.GreaterThan(0), "the war must produce annals lines");
        }

        [Test]
        public void ChallengeScript_SameSeedBitIdentical_OtherSeedDiffers()
        {
            Assembly sim = LoadSangoSimMod();
            int seed = SelectStrikeSeed(sim, "Duel");
            const int divergentSeed = 19940801;

            string RunScript(int runSeed)
            {
                var kernel = new Kernel(sim, runSeed);
                FieldEncounter encounter = SeedFieldEncounter(kernel);
                kernel.AttachChallenges("Duel", 100);
                try
                {
                    (bool ok, string error, _, _) = kernel.MoveTroop(encounter.Attacker, encounter.DefenderCell);
                    Assert.That(ok, Is.True, $"strike dispatch rejected: {error}");
                    kernel.SetDestroyMission(encounter.Defender, IntOf(encounter.Attacker, "Id"));
                    for (int i = 0; i < 10; i++)
                    {
                        kernel.AdvanceTurn();
                    }
                }
                finally
                {
                    kernel.DetachChallenges();
                }

                return kernel.WorldDigest();
            }

            string first = RunScript(seed);
            string second = RunScript(seed);
            string other = RunScript(divergentSeed);
            Console.Out.WriteLine($"[m3f-determinism] same={first} other={other}");
            Assert.That(second, Is.EqualTo(first), "same-seed challenge script must replay bit for bit");
            Assert.That(other, Is.Not.EqualTo(first), "a different seed must diverge the challenge script digest");
        }

        [Test]
        public void ChallengeScript_MidChallengeSaveRestore_MatchesUnsavedChain()
        {
            Assembly sim = LoadSangoSimMod();
            int seed = SelectCapturingDuelSeed(sim);

            string ChainWithSave()
            {
                var kernel = new Kernel(sim, seed);
                FieldEncounter encounter = SeedFieldEncounter(kernel);
                kernel.AttachChallenges("Duel", 100);
                try
                {
                    (bool ok, string error, _, _) = kernel.MoveTroop(encounter.Attacker, encounter.DefenderCell);
                    Assert.That(ok, Is.True, $"strike dispatch rejected: {error}");
                    kernel.SetDestroyMission(encounter.Defender, IntOf(encounter.Attacker, "Id"));

                    // 首击挑战结算完毕后入档(挑战全程同步,无跨帧状态);回灌后续跑 ==
                    // 不入档链,含 GameRandom 流位、部队士气与俘将三面(单挑 30% 路径)。
                    object capture = kernel.Capture();
                    kernel.Restore(capture);

                    // 回灌后俘将确实在档:胜方部队俘虏名单 + 败将原势力被俘名单都要
                    // 逐位重建(直跑链同点断言同一成员)。
                    AssertCaptivesRestored(kernel, encounter);

                    for (int i = 0; i < 5; i++)
                    {
                        kernel.AdvanceTurn();
                    }
                }
                finally
                {
                    kernel.DetachChallenges();
                }

                return kernel.WorldDigest();
            }

            string ChainWithoutSave()
            {
                var kernel = new Kernel(sim, seed);
                FieldEncounter encounter = SeedFieldEncounter(kernel);
                kernel.AttachChallenges("Duel", 100);
                try
                {
                    (bool ok, string error, _, _) = kernel.MoveTroop(encounter.Attacker, encounter.DefenderCell);
                    Assert.That(ok, Is.True, $"strike dispatch rejected: {error}");
                    kernel.SetDestroyMission(encounter.Defender, IntOf(encounter.Attacker, "Id"));
                    for (int i = 0; i < 5; i++)
                    {
                        kernel.AdvanceTurn();
                    }
                }
                finally
                {
                    kernel.DetachChallenges();
                }

                return kernel.WorldDigest();
            }

            string across = ChainWithSave();
            string neverSaved = ChainWithoutSave();
            Console.Out.WriteLine($"[m3f-challenge-save] across={across} never={neverSaved}");
            Assert.That(across, Is.EqualTo(neverSaved),
                "post-challenge save→restore→continue must replay the unsaved chain bit for bit");
        }

        // 俘将三面回放断言:挑出带俘虏的胜方部队(直跑链在此点必然存在——种子已筛出
        // 被生擒终局),校验部队 captiveList 与败将原势力 BeCaptiveList 的成员一致。
        static void AssertCaptivesRestored(Kernel kernel, FieldEncounter encounter)
        {
            int winnerId = WinnerTroopId(kernel, IntOf(encounter.Attacker, "Id"), IntOf(encounter.Defender, "Id"));
            object? winner = kernel.TroopById(winnerId);
            Assert.That(winner, Is.Not.Null, "the duel winner troop must still be alive at the capture point");

            object captiveList = FieldValue(winner!, "captiveList");
            var members = new List<object>();
            var enumerator = (IEnumerator)captiveList.GetType()
                .GetMethod("GetEnumerator", Type.EmptyTypes)!.Invoke(captiveList, null)!;
            while (enumerator.MoveNext())
            {
                if (enumerator.Current != null)
                {
                    members.Add(enumerator.Current);
                }
            }

            Assert.That(members.Count, Is.GreaterThan(0),
                "the duel capture face must survive the save boundary (troop.captiveList replayed)");

            foreach (object captive in members)
            {
                object? belongForce = PropertyValue(captive, "mBelongForce");
                Assert.That(belongForce, Is.Not.Null, "a captured person must keep their original force link");
                var beCaptive = ((IEnumerable)FieldValue(belongForce!, "BeCaptiveList")).Cast<object>().ToList();
                Assert.That(beCaptive.Contains(captive), Is.True,
                    "the captor force's BeCaptiveList must carry the captured person after restore");
            }
        }

        // 胜方部队 id:存活者即胜方(单挑 20% 溃灭只打输家;两存活时读攻方部队——
        // 攻/守胜负由俘虏所在部队判定,此处以"带俘虏的存活部队"为准)。
        static int WinnerTroopId(Kernel kernel, int attackerId, int defenderId)
        {
            foreach (int troopId in new[] { attackerId, defenderId })
            {
                object? troop = kernel.TroopById(troopId);
                if (troop == null)
                {
                    continue;
                }

                object captiveList = FieldValue(troop, "captiveList");
                var enumerator = (IEnumerator)captiveList.GetType()
                    .GetMethod("GetEnumerator", Type.EmptyTypes)!.Invoke(captiveList, null)!;
                if (enumerator.MoveNext())
                {
                    return troopId;
                }
            }

            Assert.Fail("neither troop carries captives at the capture point; the seed selection is inconsistent");
            return 0;
        }

        static object LeaderOf(object troop) => FieldValue(troop, "Leader")!;

        static string LeaderName(object troop) => PropertyValue(LeaderOf(troop), "Name")!.ToString()!;
    }
}
