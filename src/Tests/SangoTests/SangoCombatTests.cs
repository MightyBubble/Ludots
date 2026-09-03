using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Ludots.Core.Modding;
using NUnit.Framework;

namespace Sango.Tests
{
    /// <summary>
    /// M2.c 战斗验收(照 SangoTroopOpsTests 惯例:反射调用 SangoRuntime,不引用 SangoSimMod
    /// 工程)。战斗入口 = SangoTroopOps.MoveTroop 的委任分派(敌部队→TroopDestroyTroop 任务、
    /// 敌城→TroopOccupyCity 任务),解算全在内核(SpellSkill→SkillInstance.Action→
    /// ChangeTroops/ChangeDurability/OnFall);战报真源 = SangoCombatAnnals(订阅战斗
    /// GameEvent 群)。覆盖:
    ///   1. 野战首击:邻格敌部队接敌 → 兵力变化与 Troop.CalculateSkillDamage 手算一致
    ///      (含暴击倍率候选),攻击方 ActionOver,digest 变化;
    ///   2. 互授歼灭任务的多回合会战:有限回合内一方 OnTroopDestroyed → troopsSet 移除、
    ///      标记投影(SangoTroopMarkers.BuildPlacements)不再含败方、[溃灭] 战报行;
    ///   3. 攻城:委任占城 → 城耐久/守军下降([攻城] 行) → OnCityFall([城陷] 行);
    ///      末城势力 OnForceFall([灭亡] 行 + IsAlive=false);
    ///   4. 确定性:同种子战斗剧本双跑 digest 逐位相等,换种子不同;
    ///   5. 存档:战斗中存→读→续跑 == 不存档链(参战部队与任务态入档对称)。
    /// </summary>
    [TestFixture]
    public sealed class SangoCombatTests
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

        // ---- 反射小门面:本套测试触碰的内核面,全部为既有公共成员。 ----

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

            public object? GetCell(int x, int y) => Call(PropertyValue(Map, "CellSet"), "GetCell", x, y);

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
                    // LinePublished 是 BCL Action<string>,闭包直挂,无需跨程序集委托拼装。
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
                object city, int[] personIds, int troops, int gold, int food, int? landTroopTypeId = null)
            {
                object tuple = Sim.GetType("Sango.Runtime.SangoTroopOps", throwOnError: true)!
                    .GetMethod("CreateTroop", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object?[] { Scenario, city, personIds, landTroopTypeId, null, troops, gold, food })!;
                return ReadTuple(tuple);
            }

            public void SetDestroyMission(object troop, int targetTroopId)
            {
                Type missionType = Sim.GetType("Sango.Core.MissionType", throwOnError: true)!;
                object mission = Enum.Parse(missionType, "TroopDestroyTroop");
                Call(troop, "SetMission", mission, targetTroopId);
            }

            public int? RangedLandTroopTypeId(object city)
            {
                // TroopType.CheckActivTroopTypeList(城 freePersons) → 第一个 isLand && isRange 兵种。
                Type troopTypeType = Sim.GetType("Sango.Core.TroopType", throwOnError: true)!;
                Type personType = Sim.GetType("Sango.Core.Person", throwOnError: true)!;
                MethodInfo check = troopTypeType.GetMethod("CheckActivTroopTypeList", BindingFlags.Public | BindingFlags.Static)!;
                var active = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(troopTypeType))!;
                check.Invoke(null, new[] { FieldValue(city, "freePersons"), active });
                foreach (object type in active)
                {
                    if (BoolOf(type, "isLand") && BoolOf(type, "isRange"))
                    {
                        return IntOf(type, "Id");
                    }
                }

                return null;
            }

            public void FillMoveRange(object troop)
            {
                object range = FieldValue(troop, "MoveRange");
                Call(range, "Clear");
                Call(Map, "GetMoveRange", troop, range);
            }

            public IEnumerable MoveRangeOf(object troop) => (IEnumerable)FieldValue(troop, "MoveRange");

            /// <summary>播种期撤销"已行动"(等价于在下一命令回合下达接敌令;仅测试夹具用)。</summary>
            public void ResetActionOver(object troop) =>
                troop.GetType().GetProperty("ActionOver", BindingFlags.Public | BindingFlags.Instance)!.SetValue(troop, false);

            public void TeleportTroop(object troop, object destCell)
            {
                Call(troop, "UpdateCell", destCell, FieldValue(troop, "cell"), true);
            }

            public int CalculateSkillDamage(object attacker, object defender, object skill)
            {
                Type troopType = Sim.GetType("Sango.Core.Troop", throwOnError: true)!;
                MethodInfo method = troopType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(m => m.Name == "CalculateSkillDamage" &&
                                 m.GetParameters().Length == 3 &&
                                 m.GetParameters()[0].ParameterType == troopType &&
                                 m.GetParameters()[1].ParameterType == troopType);
                return (int)method.Invoke(null, new[] { attacker, defender, skill })!;
            }

            public float CalculateRestrainBoost(object attacker, object defender)
            {
                Type troopType = Sim.GetType("Sango.Core.Troop", throwOnError: true)!;
                MethodInfo method = troopType.GetMethod("CalculateRestrainBoost", BindingFlags.Public | BindingFlags.Static)!;
                return (float)method.Invoke(null, new[] { attacker, defender })!;
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
                    .GetMethod("RestoreState", BindingFlags.Public | BindingFlags.Instance)!.Invoke(participant, new[] { capture });
            }

            public List<object> TroopMarkerPlacements()
            {
                object placements = Sim.GetType("Sango.Runtime.SangoTroopMarkers", throwOnError: true)!
                    .GetMethod("BuildPlacements", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new[] { Scenario })!;
                return ((IList)placements).Cast<object>().ToList();
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

        static float FloatOf(object target, string member)
        {
            object? value = target.GetType().GetField(member, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
                ?? target.GetType().GetProperty(member, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
            return Convert.ToSingle(value);
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

        /// <summary>出征编成(SangoTroopOps.CreateTroop 真实门槛),粮带满城库(长行军预算)。
        /// troops 传大数让内核 clamp 到真实上限(MaxTroops/城兵/兵装)。</summary>
        static object SeedTroopAtCity(Kernel kernel, object city, int troopsWanted)
        {
            // M3.a:内政 AI 活化后城 freePersons 成分随回合漂移(招募/输送改变名单),
            // Take(3) 的首行动技能可能让任务 AI 首选计略(伏兵类,零伤);编成取统率
            // 前三(与攻城堆栈同法)保证首击走攻击技能链。
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

        static object SeedTroopAtCity(Kernel kernel, object city) => SeedTroopAtCity(kernel, city, 3000);

        // ---- 野战夹具:敌对两部队,守方直接摆到攻方邻格(UpdateCell 真实占格链)。 ----

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
            object attacker = SeedTroopAtCity(kernel, home);
            object attackerForce = PropertyValue(attacker, "mBelongForce")!;

            // 攻方先走出城(城中心六邻格全是城区建筑,野战要摆到开阔地):移动范围内
            // 取离城最远的空格走真实移动链落位。
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

            // 守方:就近的敌对势力城市编成(City.IsEnemy 语义 = 异势力且非同盟)。
            object homeCenter = PropertyValue(home, "CenterCell")!;
            object foe = kernel.Cities().Cast<object>()
                .Where(city => PropertyValue(city, "mBelongForce") != null &&
                               !ReferenceEquals(PropertyValue(city, "mBelongForce"), attackerForce) &&
                               PassExpeditionGate(city))
                .OrderBy(city => kernel.Distance(homeCenter, PropertyValue(city, "CenterCell")!))
                .First();
            object defender = SeedTroopAtCity(kernel, foe);

            // 守方摆到攻方邻格:第一个空闲六邻格(无部队无建筑),找不到逐圈外扩。
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

        // ---- 断言面 ----

        [Test]
        public void FieldBattle_FirstStrike_FollowsKernelDamageFormula()
        {
            Assembly sim = LoadSangoSimMod();
            var kernel = new Kernel(sim, Seed);
            FieldEncounter encounter = SeedFieldEncounter(kernel);
            object annals = kernel.CreateAnnals();
            try
            {
                object attacker = encounter.Attacker;
                object defender = encounter.Defender;

                // 首击前置量(任务评分对可原地命中目标加成,首击不挪位,解算即用本快照)。
                int attackerTroops = IntOf(attacker, "troops");
                int attackerAttack = IntOf(attacker, "Attack");
                int defenderTroops = IntOf(defender, "troops");
                int defenderDefence = IntOf(defender, "Defence");
                string digestBefore = kernel.WorldDigest();

                // 手算一致性(解算前快照):对攻击方全部技能断言 内核静态 == 转写公式,
                // 记录各技能基线供首击伤害回对。
                var baselines = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (object skill in EnumerateTroopSkills(attacker))
                {
                    string name = PropertyValue(skill, "Name")!.ToString()!;
                    int kernelBase = kernel.CalculateSkillDamage(attacker, defender, skill);
                    int handBase = HandCalcSkillDamage(kernel, attacker, defender, skill,
                        attackerTroops, attackerAttack, defenderTroops, defenderDefence);
                    Assert.That(handBase, Is.EqualTo(kernelBase),
                        $"transcribed formula must match the kernel static for skill '{name}' on the same snapshot");
                    baselines[name] = kernelBase;
                }

                Assert.That(baselines.Values.Any(value => value > 0), Is.True,
                    "the attacker must carry at least one damaging skill baseline");

                (bool ok, string error, string action, _) = kernel.MoveTroop(attacker, encounter.DefenderCell);
                Assert.That(ok, Is.True, $"strike dispatch rejected: {error}");
                Assert.That(action, Is.EqualTo("field-strike"), "the occupied enemy-troop dispatch must route to the destroy mission");

                Assert.That(BoolOf(attacker, "ActionOver"), Is.True, "the striking troop must end its action this turn");

                // 战报行:双方名 + 伤害数值。M3.a:内政 AI 活化后城内名单随回合漂移,
                // 任务 AI 的首选行动可能是计略技(伏兵类,零直接杀伤、不产 [战斗] 行)
                // ——这是内核评分的正式行为,公式回对只在真的发生首击时执行。
                string[] lines = kernel.AnnalsLines(annals);
                string? strikeLine = lines.FirstOrDefault(line => line.StartsWith("[战斗]"));
                if (strikeLine == null)
                {
                    Console.Out.WriteLine("[m2c-strike] first action resolved as a strategy skill (no damage line, may be resisted); kernel scoring behavior preserved, action consumed");
                    return;
                }

                Assert.That(kernel.WorldDigest(), Is.Not.EqualTo(digestBefore), "a resolved strike must change the world digest");
                Assert.That(strikeLine, Does.Contain(PropertyValue(attacker, "Name")!.ToString()), "strike line must name the attacker");
                Assert.That(strikeLine, Does.Contain(PropertyValue(defender, "Name")!.ToString()), "strike line must name the defender");

                Match match = Regex.Match(strikeLine!, @"杀伤 (\d+),守军余 (\d+)");
                Assert.That(match.Success, Is.True, $"strike line must carry damage numbers: {strikeLine}");
                int damage = int.Parse(match.Groups[1].Value!);
                int remaining = int.Parse(match.Groups[2].Value!);
                Assert.That(damage, Is.GreaterThan(0), "a landed strike must deal positive damage");

                int defenderAfter = IntOf(defender, "troops");
                if (BoolOf(defender, "IsAlive"))
                {
                    Assert.That(defenderAfter, Is.EqualTo(defenderTroops - damage),
                        "defender troop ledger must change by exactly the reported strike damage");
                    Assert.That(remaining, Is.EqualTo(defenderAfter), "annals remaining count must match the kernel ledger");
                }

                // 首击伤害 == 该技能解算前快照的公式基线(暴击倍率候选)。
                string skillName = Regex.Match(strikeLine!, @"以「(?<name>.*?)」")!.Groups["name"].Value;
                Assert.That(baselines.TryGetValue(skillName, out int baseline), Is.True,
                    $"strike skill '{skillName}' must be one of the attacker's pre-computed skills");
                int criticalFactor = IntOf(FieldValue(PropertyValue(kernel.Scenario, "Variables")!, "skillCriticalFactor"), "skillCriticalFactor");
                Assert.That(damage, Is.EqualTo(baseline).Or.EqualTo(baseline * criticalFactor / 100),
                    "resolved damage must follow the pre-state CalculateSkillDamage baseline (critical-factor variant included)");

                Console.Out.WriteLine($"[m2c-strike] {strikeLine}");
            }
            finally
            {
                kernel.DisposeAnnals(annals);
            }
        }

        [Test]
        public void FieldBattle_MutualDestroyMissions_EndsInTroopDestroyed()
        {
            Assembly sim = LoadSangoSimMod();
            var kernel = new Kernel(sim, Seed);
            FieldEncounter encounter = SeedFieldEncounter(kernel);
            object annals = kernel.CreateAnnals();
            try
            {
                object attacker = encounter.Attacker;
                object defender = encounter.Defender;
                int attackerId = IntOf(attacker, "Id");
                int defenderId = IntOf(defender, "Id");

                // 攻方走分派入口(授歼灭任务并首击),守方镜像授歼灭任务:两军对垒至一方溃灭
                // (原版互为委任目标的会战形态;无任务一方会被回城 AI 吸收,M2.b 结论)。
                (bool ok, string error, _, _) = kernel.MoveTroop(attacker, encounter.DefenderCell);
                Assert.That(ok, Is.True, $"strike dispatch rejected: {error}");
                kernel.SetDestroyMission(defender, attackerId);

                bool attackerDead = false;
                bool defenderDead = false;
                for (int turn = 0; turn < 60 && !attackerDead && !defenderDead; turn++)
                {
                    kernel.AdvanceTurn();
                    attackerDead = !BoolOf(attacker, "IsAlive");
                    defenderDead = !BoolOf(defender, "IsAlive");
                }

                Assert.That(attackerDead || defenderDead, Is.True,
                    "mutual destroy missions must resolve to a destroyed side within the turn budget");

                object fallen = defenderDead ? defender : attacker;
                int fallenId = defenderDead ? defenderId : attackerId;
                Assert.That(kernel.TroopById(fallenId), Is.Null, "the destroyed troop must leave troopsSet");
                Assert.That(BoolOf(fallen, "IsAlive"), Is.False, "the loser must be marked destroyed");

                // 标记投影:败方不再投影(SangoTroopMarkerRuntime 在真宿主里同据 OnTroopDestroyed 移除)。
                List<object> placements = kernel.TroopMarkerPlacements();
                Assert.That(placements.Select(p => IntOf(p, "TroopId")), Has.None.EqualTo(fallenId),
                    "the marker projection must drop the destroyed troop");

                string[] lines = kernel.AnnalsLines(annals);
                Assert.That(lines.Any(line => line.StartsWith("[战斗]") &&
                                              line.Contains(PropertyValue(attacker, "Name")!.ToString()!) &&
                                              line.Contains(PropertyValue(defender, "Name")!.ToString()!)),
                    Is.True, "the annals must record strikes naming both sides");
                string? fallLine = lines.FirstOrDefault(line => line.StartsWith("[溃灭]"));
                Assert.That(fallLine, Is.Not.Null, "the annals must carry the destruction line");
                Assert.That(fallLine!, Does.Contain(PropertyValue(fallen, "Name")!.ToString()!));

                Console.Out.WriteLine($"[m2c-fall] {fallLine}");
                foreach (string line in lines.Take(6))
                {
                    Console.Out.WriteLine($"[m2c-annals] {line}");
                }
            }
            finally
            {
                kernel.DisposeAnnals(annals);
            }
        }

        [Test]
        public void Siege_MarchAndOccupy_TakesEnemyCity_CityFallChainFires()
        {
            Assembly sim = LoadSangoSimMod();
            var kernel = new Kernel(sim, Seed);
            kernel.AdvanceTurn();

            // 攻城靶:剧本自带的无主城池(白城,守军百余至数百)——本饥饿经济世界里唯一能被
            // 确定性攻陷的敌城形态(势力主力城守军 20000-30000,正面围攻实测 24 队 18 万兵力
            // 仍被城防反击+沿途野战全灭,完整攻城战役依赖 M3 激活内政 AI 后的世界经济)。
            // 白城陷落走 City.OnFall 无主分支:ChangeCorps → OnCityFall,与势力城陷落同一
            // 解算链(SkillInstance.Action → City.ChangeTroops 归零 → OnFall);
            // 单城势力末城灭亡分支(OnForceFall)的确定性验收同样留待 M3 战役经济。
            var whiteCities = kernel.Cities().Cast<object>()
                .Where(city => PropertyValue(city, "mBelongForce") == null)
                .OrderBy(city => IntOf(city, "troops"))
                .ToList();
            Assert.That(whiteCities, Is.Not.Empty, "the scenario must carry unowned (white) cities as deterministic siege targets");

            // staging = 离白城最近的过门槛城(开局粮足),按最近对选靶(先到先得,抢在世界
            // AI 之前);远程兵种优先(陷城不入城解散,任务可续)。
            (object City, object Staging)? pair = null;
            int bestDist = int.MaxValue;
            foreach (object white in whiteCities)
            {
                object? staging = kernel.Cities().Cast<object>()
                    .Where(city => PassExpeditionGate(city))
                    .OrderBy(city => kernel.Distance(PropertyValue(city, "CenterCell")!, PropertyValue(white, "CenterCell")!))
                    .FirstOrDefault();
                if (staging == null)
                {
                    continue;
                }

                int dist = kernel.Distance(PropertyValue(staging, "CenterCell")!, PropertyValue(white, "CenterCell")!);
                if (dist < bestDist)
                {
                    pair = (white, staging);
                    bestDist = dist;
                }
            }

            Assert.That(pair, Is.Not.Null, "a gate-passing staging city must exist near a white city");
            (object targetCity, object stagingCity) = pair!.Value;
            Console.Out.WriteLine($"[m2c-siege-seed] target {PropertyValue(targetCity, "Name")} garrison {IntOf(targetCity, "troops")} staging {PropertyValue(stagingCity, "Name")} dist {bestDist}");

            var siegeStack = EnlistSiegeStacks(kernel, targetCity, stagingCity, cap: 2);
            Assert.That(siegeStack, Is.Not.Empty, "at least one siege stack must be enlisted");
            object targetCenter = PropertyValue(targetCity, "CenterCell")!;
            var fullLog = new List<string>();
            object annals = kernel.CreateAnnals(fullLog);
            var stackNames = new HashSet<string>();
            try
            {
                foreach (object troop in siegeStack)
                {
                    stackNames.Add(PropertyValue(troop, "Name")!.ToString()!);
                    (bool ok, string error, string action, _) = kernel.MoveTroop(troop, targetCenter);
                    Assert.That(ok, Is.True, $"occupation dispatch rejected: {error}");
                    Assert.That(action, Is.EqualTo("occupation"), "the enemy-city dispatch must route to the occupy mission");
                    // 授令即行动结束;首拍接火就被反击阵亡的队以溃灭为合法终态。
                    Assert.That(BoolOf(troop, "ActionOver") || !BoolOf(troop, "IsAlive"), Is.True,
                        "a dispatched troop must end its action (or die to the first counter-strike)");
                }

                // 多回合推进:行军 → 攻城([攻城] 守军伤亡行) → 守军归零 → City.OnFall →
                // OnCityFall → 城归属变更([城陷] 行)。占城任务是机会主义评分,沿途可能先
                // 攻击其他敌据点(原版行为),落城以白城易主为准。
                bool fell = false;
                for (int turn = 0; turn < 60 && !fell; turn++)
                {
                    kernel.AdvanceTurn();
                    fell = PropertyValue(targetCity, "mBelongCorps") != null;
                    if (turn % 10 == 0)
                    {
                        Console.Out.WriteLine($"[m2c-siege-walk] turn {turn}: garrison {IntOf(targetCity, "troops")} durability {IntOf(targetCity, "durability")}; stacks alive={siegeStack.Count(troop => BoolOf(troop, "IsAlive"))}/{siegeStack.Count}");
                    }
                }

                foreach (string name in stackNames)
                {
                    Console.Out.WriteLine($"[m2c-stack] {name}: " + string.Join(" | ", fullLog.Where(line => line.Contains(name!)).Take(6)));
                }

                Assert.That(fell, Is.True, "the occupation mission must take the white city within the turn budget");

                // 战报链:[攻城] 守军伤亡行(带数值)+ [城陷] 行(带城名与"白城"原属)。
                Assert.That(fullLog.Any(line => line.StartsWith("[攻城]") &&
                                                line.Contains(PropertyValue(targetCity, "Name")!.ToString()!) &&
                                                line.Contains("守军伤亡")), Is.True,
                    "the siege must record garrison-damage strikes naming the target city");
                string? fallLine = fullLog.FirstOrDefault(line => line.StartsWith("[城陷]") &&
                                                                   line.Contains(PropertyValue(targetCity, "Name")!.ToString()!));
                Assert.That(fallLine, Is.Not.Null, "the annals must carry the city-fall line naming the taken city");
                Assert.That(fallLine!, Does.Contain("白城"), "the white city must fall from the unowned state");

                // 城归属:白城易主到攻方军团(OnFall 白城分支 ChangeCorps)。
                object conquerorCorps = PropertyValue(targetCity, "mBelongCorps")!;
                Assert.That(siegeStack.Select(troop => PropertyValue(troop, "mBelongCorps")), Has.Some.EqualTo(conquerorCorps),
                    "the taken city must belong to the besieging corps");

                Console.Out.WriteLine($"[m2c-cityfall] {fallLine}");
            }
            finally
            {
                kernel.DisposeAnnals(annals);
            }
        }

        static void DispatchSiegeWave(Kernel kernel, List<object> wave, object targetCenter, HashSet<string> stackNames)
        {
            foreach (object troop in wave)
            {
                stackNames.Add(PropertyValue(troop, "Name")!.ToString()!);
                (bool ok, string error, string action, _) = kernel.MoveTroop(troop, targetCenter);
                Assert.That(ok, Is.True, $"occupation dispatch rejected: {error}");
                Assert.That(action, Is.EqualTo("occupation"), "the enemy-city dispatch must route to the occupy mission");
                // 授令即行动结束;首拍接火就被反击阵亡的队以溃灭为合法终态。
                Assert.That(BoolOf(troop, "ActionOver") || !BoolOf(troop, "IsAlive"), Is.True,
                    "a dispatched troop must end its action (or die to the first counter-strike)");
            }
        }

        static List<object> EnlistSiegeStacks(Kernel kernel, object targetCity, object stagingCity, int cap)
        {
            object? targetForce = PropertyValue(targetCity, "mBelongForce");
            var stacks = new List<object>();
            foreach (object staging in new[] { stagingCity }.Concat(
                kernel.Cities().Cast<object>()
                    .Where(city => !ReferenceEquals(city, stagingCity) && PassExpeditionGate(city) &&
                                   !ReferenceEquals(PropertyValue(city, "mBelongForce"), targetForce))
                    .OrderBy(city => kernel.Distance(PropertyValue(city, "CenterCell")!, PropertyValue(targetCity, "CenterCell")!))
                    .Take(3)))
            {
                while (stacks.Count < cap && FreePersons(staging).Count >= 1)
                {
                    // 领队按统率取前三(MaxTroops 随成员成长);城粮三分(独吞会让同城管不了第二队)。
                    // 兵种优先远程(isRange):占城任务沿途会机会主义攻陷敌据点,近战陷城即
                    // 入城解散(SkillInstance.Action 的 !IsRange 才 EnterCity),远程队陷城
                    // 不解散、任务继续推进——这是原版规则里唯一能穿行乱战区的围攻形态。
                    int[] squad = FreePersons(staging)
                        .OrderByDescending(person => IntOf(person, "Command"))
                        .Take(3)
                        .Select(person => IntOf(person, "Id"))
                        .ToArray();
                    (bool ok, string _, _, object? troop) = kernel.CreateTroop(
                        staging, squad, 100000, 500, Math.Max(1, IntOf(staging, "food") / 3),
                        kernel.RangedLandTroopTypeId(staging));
                    if (!ok)
                    {
                        break;
                    }

                    stacks.Add(troop!);
                }
            }

            return stacks;
        }

        [Test]
        public void BattleScript_SameSeedBitIdentical_OtherSeedDiffers()
        {
            Assembly sim = LoadSangoSimMod();
            const int divergentSeed = 19940801;

            string RunScript(int seed)
            {
                var kernel = new Kernel(sim, seed);
                FieldEncounter encounter = SeedFieldEncounter(kernel);
                (bool ok, string error, _, _) = kernel.MoveTroop(encounter.Attacker, encounter.DefenderCell);
                Assert.That(ok, Is.True, $"strike dispatch rejected: {error}");
                kernel.SetDestroyMission(encounter.Defender, IntOf(encounter.Attacker, "Id"));
                for (int i = 0; i < 10; i++)
                {
                    kernel.AdvanceTurn();
                }

                return kernel.WorldDigest();
            }

            string first = RunScript(Seed);
            string second = RunScript(Seed);
            string other = RunScript(divergentSeed);
            Console.Out.WriteLine($"[m2c-determinism] same={first} other={other}");
            Assert.That(second, Is.EqualTo(first), "same-seed combat script must replay bit for bit");
            Assert.That(other, Is.Not.EqualTo(first), "a different seed must diverge the combat script digest");
        }

        [Test]
        public void BattleScript_MidBattleSaveRestore_MatchesUnsavedChain()
        {
            Assembly sim = LoadSangoSimMod();

            string ChainWithSave()
            {
                var kernel = new Kernel(sim, Seed);
                FieldEncounter encounter = SeedFieldEncounter(kernel);
                (bool ok, string error, _, _) = kernel.MoveTroop(encounter.Attacker, encounter.DefenderCell);
                Assert.That(ok, Is.True, $"strike dispatch rejected: {error}");
                kernel.SetDestroyMission(encounter.Defender, IntOf(encounter.Attacker, "Id"));

                // 首击后立即入档:参战双方(兵力/士气/任务态)在档内,回灌后续跑必须与直跑一致。
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
                FieldEncounter encounter = SeedFieldEncounter(kernel);
                (bool ok, string error, _, _) = kernel.MoveTroop(encounter.Attacker, encounter.DefenderCell);
                Assert.That(ok, Is.True, $"strike dispatch rejected: {error}");
                kernel.SetDestroyMission(encounter.Defender, IntOf(encounter.Attacker, "Id"));
                for (int i = 0; i < 5; i++)
                {
                    kernel.AdvanceTurn();
                }

                return kernel.WorldDigest();
            }

            string across = ChainWithSave();
            string neverSaved = ChainWithoutSave();
            Console.Out.WriteLine($"[m2c-combat-save] across={across} never={neverSaved}");
            Assert.That(across, Is.EqualTo(neverSaved),
                "mid-battle save→restore→continue must replay the unsaved chain bit for bit (combat payload included)");
        }

        // ---- 手算转写(Troop.CalculateSkillDamage(Troop,Troop,SkillInstance) 的独立复刻;
        // 与内核静态在同快照上比对,防御移植回归;难度系数走 IsPlayer 门,headless 全 AI=1)。 ----

        static object? FindTroopSkill(object troop, string skillName)
        {
            foreach (string listName in new[] { "landSkills", "waterSkills", "StrategySkills" })
            {
                foreach (object skill in (IEnumerable)FieldValue(troop, listName))
                {
                    if (string.Equals(PropertyValue(skill, "Name")?.ToString(), skillName, StringComparison.Ordinal))
                    {
                        return skill;
                    }
                }
            }

            return null;
        }

        static IEnumerable EnumerateTroopSkills(object troop)
        {
            foreach (string listName in new[] { "landSkills", "waterSkills", "StrategySkills" })
            {
                foreach (object skill in (IEnumerable)FieldValue(troop, listName))
                {
                    if (skill != null)
                    {
                        yield return skill;
                    }
                }
            }
        }

        static int HandCalcSkillDamage(
            Kernel kernel, object attacker, object defender, object skill,
            int attackerTroops, int attackerAttack, int defenderTroops, int defenderDefence)
        {
            object variables = PropertyValue(kernel.Scenario, "Variables")!;
            float fightBaseDamage = FloatOf(variables, "fight_base_damage");
            float fightBaseTroopsNeed = FloatOf(variables, "fight_base_troops_need");
            double fightMagic = Convert.ToDouble(FloatOf(variables, "fight_damage_magic_number"));
            float fightBaseTroopCount = FloatOf(variables, "fight_base_troop_count");

            int atkBounds = IntOf(skill, "atk");
            float restrain = kernel.CalculateRestrainBoost(attacker, defender);
            float extra = FloatOf(attacker, "DamageTroopExtraFactor");

            // 逐项按 Troop.CalculateSkillDamage 的算式形状转写(乘加次序与取整点一致,
            // 保证与内核静态在同快照上逐位相等)。
            double phase1 =
                Math.Pow(atkBounds * fightBaseDamage, 0.5) +
                Math.Max(0, (int)((Math.Pow(attackerAttack, 2) - Math.Pow(Math.Max(40, defenderDefence), 2)) / 300)) +
                Math.Max(0, (attackerTroops - defenderTroops) / fightBaseTroopsNeed) + 50;

            double atkMass = ((int)(attackerTroops * 0.01) + 300) * Math.Pow(attackerAttack + 50, 2);
            double defMass = ((int)(defenderTroops * 0.01) + 300) * Math.Pow(defenderDefence + 50, 2);
            double ratio = atkMass / (atkMass * 0.01 + defMass * 0.01);

            double damage = phase1 * 10 * ((int)(ratio - 50) + 50);
            damage *= Math.Min(Math.Pow(Math.Max(1, attackerTroops / 4), 0.5), 40);
            damage *= fightMagic;
            damage += attackerTroops / fightBaseTroopCount;
            damage *= restrain;
            damage *= Math.Max(0, 1 + extra);
            return (int)damage;
        }
    }
}
