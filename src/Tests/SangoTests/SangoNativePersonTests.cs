using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Arch.Core;
using Arch.Core.Utils;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Modding;
using Ludots.Core.Persistence;
using Ludots.Core.Presentation;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Sango.Tests
{
    /// <summary>
    /// D-2' 武将域原生实现对拍验收:同种子同命令流(四型内政令的武将写面 + 逐回合褒奖链 +
    /// 播种攻城俘将 + 推 N 回合)老内核跑一遍(不挂任何原生运行时,digest 武将行=内核源)与
    /// 原生城+武将域跑一遍(挂载,武将行=组件源),逐回合逐位相等。附加面:
    ///   - 运行内双源对账:原生跑的每一回合,组件源武将行 == 内核源武将行;
    ///   - 写面账本:训练(功勋/经验/行动位)、褒奖(忠诚+10)、招揽(任务态)按内核语义
    ///     独立复算断言(SangoPersonLedger 探针);
    ///   - 俸给账本:LastSalaryGold(组件源产出)按内核 GoldCost 公式(PONO 复算)断言;
    ///   - 俘将面:攻城俘获后内核俘囚数 == 组件状态直方图俘囚数;
    ///   - world.bin:武将实体 + GAS 属性 + 组件随引擎原生序列化存读回环。
    /// 反射调用 SangoRuntime(不引用 SangoSimMod 工程——同 SangoNativeCityTests 惯例)。
    /// </summary>
    [TestFixture]
    public sealed class SangoNativePersonTests
    {
        private const int TurnsToAdvance = 10;
        // 梯子含 2026 系列(D-1' 同族);俘将链按白城陷落 + 俘囚>0 探测择种。
        private static readonly int[] SeedLadder = { 20260902, 20260903, 20260904, 20260905, 20260906, 20260907, 20260908, 20260909 };

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

        /// <summary>临时 Core 挂载(目录 + 拷贝 mod 真实模板资产 + 最小 config_catalog)。</summary>
        private static (EntityLifecycleRuntimeServices Services, Ludots.Core.Gameplay.GAS.TagOps TagOps, string TempRoot) BuildServices(World world)
        {
            string modTemplates = Path.Combine(RepoRoot(), "mods", "sango", "SangoSimMod", "assets", "Entities", "templates.json");
            Assert.That(File.Exists(modTemplates), Is.True, "SangoSimMod must ship assets/Entities/templates.json (sango.city/sango.person templates)");

            string tempRoot = Path.Combine(Path.GetTempPath(), $"SangoPersonTpl_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(tempRoot, "Entities"));
            File.Copy(modTemplates, Path.Combine(tempRoot, "Entities", "templates.json"));
            File.WriteAllText(
                Path.Combine(tempRoot, "config_catalog.json"),
                "[{ \"Path\": \"Entities/templates.json\", \"Policy\": \"ArrayById\", \"IdField\": \"id\" }]");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", tempRoot);
            var pipeline = new ConfigPipeline(vfs, new ModLoader(vfs, new FunctionRegistry(), new TriggerManager()));
            var templates = new DataRegistry<EntityTemplate>(pipeline);
            templates.Load("Entities/templates.json", ConfigCatalogLoader.Load(pipeline));

            var tagOps = new Ludots.Core.Gameplay.GAS.TagOps(
                new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
            var services = new EntityLifecycleRuntimeServices(
                world,
                templates,
                new EntityTemplateKeyRegistry(),
                new PresentationStableIdAllocator(),
                tagOps);
            return (services, tagOps, tempRoot);
        }

        private static (object CityRuntime, object PersonRuntime, string TempRoot) AttachNative(Assembly sim, World world)
        {
            (EntityLifecycleRuntimeServices services, Ludots.Core.Gameplay.GAS.TagOps tagOps, string tempRoot) = BuildServices(world);
            object city = sim.GetType("Sango.Runtime.SangoCityNativeRuntime", throwOnError: true)!
                .GetMethod("AttachBare", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object[] { world, services, tagOps })!;
            object person = sim.GetType("Sango.Runtime.SangoPersonNativeRuntime", throwOnError: true)!
                .GetMethod("AttachBare", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object[] { world, services, tagOps })!;
            return (city, person, tempRoot);
        }

        // ---- 内核小门面(反射,公共成员) ----

        private sealed class Kernel
        {
            public readonly Assembly Sim;

            public Kernel(Assembly sim, int seed)
            {
                Sim = sim;
                sim.GetType("Sango.Runtime.SangoKernelBoot", throwOnError: true)!
                    .GetMethod("Boot", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object[] { NewVfs(), "SangoContentMod", seed, "Scenario/Scenario.json" });
                Assert.That(Scenario, Is.Not.Null, "Boot must leave a booted Scenario.Cur");
            }

            public object Scenario =>
                Sim.GetType("Sango.Core.Scenario", throwOnError: true)!
                    .GetProperty("Cur", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

            public object Map => PropertyValue(Scenario, "Map")!;

            public IEnumerable Cities() => EnumerateSet(FieldValue(Scenario, "citySet"));

            public IEnumerable Persons() => EnumerateSet(FieldValue(Scenario, "personSet"));

            public object? GetCity(int cityId) => Call(FieldValue(Scenario, "citySet"), "Get", cityId);

            public object? GetPerson(int personId) => Call(FieldValue(Scenario, "personSet"), "Get", personId);

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

            public List<string> KernelPersonRows() =>
                (List<string>)Sim.GetType("Sango.Runtime.SangoPersonNativeRuntime", throwOnError: true)!
                    .GetMethod("KernelPersonRows", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new[] { Scenario })!;

            public List<string> NativePersonRows(object personRuntime) =>
                ((IEnumerable)personRuntime.GetType()
                    .GetMethod("PersonDigestRows", BindingFlags.Public | BindingFlags.Instance)!
                    .Invoke(personRuntime, null)!).Cast<string>().ToList();

            public (bool Succeeded, string ErrorCode) CityCommand(object city, string type, int[] personIds, int targetPersonId)
            {
                object result = Sim.GetType("Sango.Runtime.SangoCityOps", throwOnError: true)!
                    .GetMethod("Execute", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object[] { Scenario, city, type, personIds, targetPersonId })!;
                return (
                    (bool)result.GetType().GetProperty("Succeeded")!.GetValue(result)!,
                    (string)result.GetType().GetProperty("ErrorCode")!.GetValue(result)!);
            }

            public (bool Succeeded, string ErrorCode, object? Payload) CreateTroop(object city, int[] personIds, int troops, int gold, int food)
            {
                object tuple = Sim.GetType("Sango.Runtime.SangoTroopOps", throwOnError: true)!
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(m => m.Name == "CreateTroop" && m.GetParameters().Length == 8)!
                    .Invoke(null, new object?[] { Scenario, city, personIds, null, null, troops, gold, food })!;
                object? payload = tuple.GetType().GetFields().FirstOrDefault(field => field.Name == "Item2")?.GetValue(tuple);
                object result = tuple.GetType().GetField("Item1")!.GetValue(tuple)!;
                return (
                    (bool)result.GetType().GetProperty("Succeeded")!.GetValue(result)!,
                    (string)result.GetType().GetProperty("ErrorCode")!.GetValue(result)!,
                    payload);
            }

            public (bool Succeeded, string ErrorCode) MoveTroop(object troop, object destCell)
            {
                object tuple = Sim.GetType("Sango.Runtime.SangoTroopOps", throwOnError: true)!
                    .GetMethod("MoveTroop", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object[] { Scenario, troop, destCell })!;
                object result = tuple.GetType().GetField("Item1")!.GetValue(tuple)!;
                return (
                    (bool)result.GetType().GetProperty("Succeeded")!.GetValue(result)!,
                    (string)result.GetType().GetProperty("ErrorCode")!.GetValue(result)!);
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

            public object? GetNeighbor(object cell, int dir) => Call(Map, "GetNeighbor", cell, dir);

            public void SetDestroyMission(object troop, int targetTroopId)
            {
                Type missionType = Sim.GetType("Sango.Core.MissionType", throwOnError: true)!;
                Call(troop, "SetMission", Enum.Parse(missionType, "TroopDestroyTroop"), targetTroopId);
            }

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
        }

        static int JobMeritGain(Assembly sim, string jobEnumName)
        {
            Type jobType = sim.GetType("Sango.Core.JobType", throwOnError: true)!;
            Type jobEnumType = sim.GetType("Sango.Core.CityJobType", throwOnError: true)!;
            int jobId = (int)Enum.Parse(jobEnumType, jobEnumName);
            return (int)jobType.GetMethod("GetJobMeritGain", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { jobId })!;
        }

        static int ForceIdOf(object person)
        {
            object? force = PropertyValue(person, "mBelongForce");
            return force == null ? 0 : IntOf(force, "Id");
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

        static List<object> FreePersons(object city) => ((IEnumerable)FieldValue(city, "freePersons")).Cast<object>().ToList();

        static int MakeTroopCostAP(Assembly sim)
        {
            Type jobType = sim.GetType("Sango.Core.JobType", throwOnError: true)!;
            Type jobEnumType = sim.GetType("Sango.Core.CityJobType", throwOnError: true)!;
            int makeTroopId = (int)Enum.Parse(jobEnumType, "MakeTroop");
            return (int)jobType.GetMethod("GetJobCostAP", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { makeTroopId })!;
        }

        static bool PassExpeditionGate(Assembly sim, object city)
        {
            int cost = MakeTroopCostAP(sim);
            return IntOf(city, "troops") > 0 && IntOf(city, "food") > 0 &&
                   FreePersons(city).Count > 0 &&
                   IntOf(PropertyValue(city, "mBelongCorps")!, "ActionPoint") >= cost &&
                   PropertyValue(city, "mBelongForce") != null && PropertyValue(city, "mBelongCorps") != null;
        }

        // ---- 对拍剧本(两路共用;native = 原生城+武将域跑) ----

        private sealed class WriteFaceCapture
        {
            public string Type = "";
            public int CityId;
            public List<int> PersonIds = new();
            public Dictionary<int, (int Merit, int Exp, int LevelId, int Loyalty, int MissionType)> Before = new();
            public Dictionary<int, (int Merit, int Exp, int LevelId, int Loyalty, int MissionType)> After = new();
        }

        private sealed class ScriptResult
        {
            public List<string> Digests = new();
            public List<(string Type, int CityId, bool Succeeded)> CommandResults = new();
            public List<WriteFaceCapture> Captures = new();
            public bool WhiteCityFell;
            public int PrisonerCount;
            public object? CityRuntime;
            public object? PersonRuntime;
            public string? TempRoot;
            public World? World;

            /// <summary>世界由调用方持有(world.bin 断言要在运行后序列化);失败路径由 RunScript 清理。</summary>
            public void Release()
            {
                if (PersonRuntime != null)
                {
                    PersonRuntime.GetType().GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance)!
                        .Invoke(PersonRuntime, null);
                    PersonRuntime = null;
                }

                if (CityRuntime != null)
                {
                    CityRuntime.GetType().GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance)!
                        .Invoke(CityRuntime, null);
                    CityRuntime = null;
                }

                World?.Dispose();
                World = null;
                if (TempRoot != null)
                {
                    Directory.Delete(TempRoot, recursive: true);
                    TempRoot = null;
                }
            }
        }

        /// <summary>
        /// 剧本:启动 → 推 1 回合(AP 发放)→ 四型内政令(逐城探测,成功即记;写面前后值
        /// 捕获供公式复算)→ 播种攻城(守军最少白城 + 最近过门槛城贴城)→ 推 10 回合逐回合
        /// digest + 逐回合褒奖链探测(同循环两路同跑)。原生跑每回合另做武将行组件源 ==
        /// 内核源双断言。
        /// </summary>
        private static ScriptResult RunScript(Assembly sim, int seed, bool native, int turns = TurnsToAdvance)
        {
            var kernel = new Kernel(sim, seed);
            var result = new ScriptResult();
            if (!native)
            {
                return RunScriptCore(sim, kernel, null, null, result, turns);
            }

            World world = World.Create();
            result.World = world;
            try
            {
                (result.CityRuntime, result.PersonRuntime, result.TempRoot) = AttachNative(sim, world);
                return RunScriptCore(sim, kernel, result.CityRuntime, result.PersonRuntime, result, turns);
            }
            catch
            {
                result.Release();
                throw;
            }
        }

        static ScriptResult RunScriptCore(
            Assembly sim, Kernel kernel, object? cityRuntime, object? personRuntime, ScriptResult result, int turns)
        {
            kernel.AdvanceTurn();

            // 四型内政令探测(写面前后值捕获;成败对照即门槛/结算等价证明)。
            List<object> cities = kernel.Cities().Cast<object>().ToList();
            result.Captures.Add(RunCommandProbe(kernel, sim, "train", cities, city => PersonIdsOf(city, 2), _ => 0));
            result.Captures.Add(RunCommandProbe(kernel, sim, "search", cities, city => PersonIdsOf(city, 1), _ => 0));
            result.Captures.Add(RunCommandProbe(kernel, sim, "reward", cities, RewardTargetIds, _ => 0));
            result.Captures.Add(RunCommandProbe(kernel, sim, "recruit", cities, city => PersonIdsOf(city, 1), city => RecruitTarget(kernel, sim, city)));

            // 播种战斗(俘将链):野战互歼对阵(近邻异势力两城各编一队,互授歼灭任务,
            // 100% 单挑覆写——单挑俘将 30%/次,覆盖俘获→state 面)+ 白城攻城(守军最少
            // 无主城 + 最近过门槛城贴城,覆盖城陷面)。
            object home = cities.First(city => PassExpeditionGate(sim, city));
            object attacker = SeedTroopAtCity(kernel, home, 3000);
            object attackerForce = PropertyValue(attacker, "mBelongForce")!;
            kernel.FillMoveRange(attacker);
            object? openCell = kernel.MoveRangeOf(attacker).Cast<object?>()
                .FirstOrDefault(cell => cell != null && PropertyValue(cell, "troop") == null && PropertyValue(cell, "building") == null);
            Assert.That(openCell, Is.Not.Null, "attacker move range must expose an open cell");
            (bool moved, string moveError) = kernel.MoveTroop(attacker, openCell!);
            Assert.That(moved, Is.True, $"attacker cannot leave the city block: {moveError}");
            kernel.ResetActionOver(attacker);

            object homeCenter = PropertyValue(home, "CenterCell")!;
            object foe = cities
                .Where(city => PropertyValue(city, "mBelongForce") != null &&
                               !ReferenceEquals(PropertyValue(city, "mBelongForce"), attackerForce) &&
                               PassExpeditionGate(sim, city))
                .OrderBy(city => kernel.Distance(homeCenter, PropertyValue(city, "CenterCell")!))
                .First();
            object defender = SeedTroopAtCity(kernel, foe, 3000);
            object attackerCell = FieldValue(attacker, "cell");
            object? dest = null;
            for (int dir = 0; dir < 6 && dest == null; dir++)
            {
                object neighbor = kernel.GetNeighbor(attackerCell, dir)!;
                if (neighbor != null && PropertyValue(neighbor, "troop") == null && PropertyValue(neighbor, "building") == null)
                {
                    dest = neighbor;
                }
            }

            Assert.That(dest, Is.Not.Null, "the attacker's open-field cell must expose an empty neighbor");
            kernel.TeleportTroop(defender, dest!);
            kernel.SetDestroyMission(attacker, IntOf(defender, "Id"));
            kernel.SetDestroyMission(defender, IntOf(attacker, "Id"));

            object targetCity = cities
                .Where(city => PropertyValue(city, "mBelongForce") == null)
                .OrderBy(city => IntOf(city, "troops"))
                .First();
            object staging = cities
                .Where(city => PassExpeditionGate(sim, city) && !ReferenceEquals(city, home) && !ReferenceEquals(city, foe))
                .OrderBy(city => kernel.Distance(PropertyValue(city, "CenterCell")!, PropertyValue(targetCity, "CenterCell")!))
                .First();
            object siegeTroop = SeedTroopAtCity(kernel, staging, 3000);
            object? siegeDest = FindOpenCellNear(kernel, PropertyValue(targetCity, "CenterCell")!);
            Assert.That(siegeDest, Is.Not.Null, "the white city must expose an open staging cell within two rings");
            kernel.TeleportTroop(siegeTroop, siegeDest!);

            kernel.AttachChallenges("Duel", 100);
            try
            {
                (bool struck, string strikeError) = kernel.MoveTroop(attacker, dest!);
                Assert.That(struck, Is.True, $"field strike dispatch rejected: {strikeError}");
                AdvanceTurns(kernel, personRuntime, result, turns);
            }
            finally
            {
                kernel.DetachChallenges();
            }

            result.WhiteCityFell = PropertyValue(targetCity, "mBelongCorps") != null;
            result.PrisonerCount = CountCaptives(kernel);
            return result;
        }

        /// <summary>俘将真源计数:势力 BeCaptiveList(单挑/部队/城陷俘获的共同落账;state==Prisoner 是入狱面,城陷/献俘时才置位)。</summary>
        static int CountCaptives(Kernel kernel)
        {
            int count = 0;
            foreach (object force in EnumerateSet(FieldValue(kernel.Scenario, "forceSet")))
            {
                count += Convert.ToInt32(PropertyValue(FieldValue(force, "BeCaptiveList"), "Count"));
            }

            return count;
        }

        static void AdvanceTurns(Kernel kernel, object? personRuntime, ScriptResult result, int turns)
        {
            List<object> cities = kernel.Cities().Cast<object>().ToList();
            for (int turn = 0; turn < turns; turn++)
            {
                kernel.AdvanceTurn();
                // 褒奖链:逐回合再探一轮褒奖(jobCounter/AP 逐回合复位,成功即褒奖链续跑)。
                result.Captures.Add(RunCommandProbe(kernel, kernel.Sim, "reward", cities, RewardTargetIds, _ => 0));
                result.Digests.Add(kernel.WorldDigest());
                if (personRuntime != null)
                {
                    List<string> componentRows = kernel.NativePersonRows(personRuntime);
                    List<string> kernelRows = kernel.KernelPersonRows();
                    Assert.That(componentRows.Count, Is.EqualTo(kernelRows.Count), $"turn {turn}: component/kernel person row count must match");
                    for (int row = 0; row < kernelRows.Count; row++)
                    {
                        Assert.That(componentRows[row], Is.EqualTo(kernelRows[row]),
                            $"turn {turn}: component-source person row must equal kernel-source row (index {row})");
                    }
                }
            }
        }

        static int[] PersonIdsOf(object city, int take) =>
            FreePersons(city).Take(take).Select(person => IntOf(person, "Id")).ToArray();

        static (int Merit, int Exp, int LevelId, int Loyalty, int MissionType) SnapshotOf(object person) =>
            (IntOf(person, "merit"),
             IntOf(person, "Exp"),
             IntOf(PropertyValue(person, "Level")!, "Id"),
             IntOf(person, "loyalty"),
             IntOf(person, "missionType"));

        static WriteFaceCapture RunCommandProbe(
            Kernel kernel, Assembly sim, string type, List<object> cities, Func<object, int[]> personIdsOf, Func<object, int> targetOf)
        {
            var capture = new WriteFaceCapture { Type = type };
            foreach (object city in cities)
            {
                if (PropertyValue(city, "mBelongForce") == null)
                {
                    continue;
                }

                int[] personIds = personIdsOf(city);
                if (personIds.Length == 0)
                {
                    continue;
                }

                capture.CityId = IntOf(city, "Id");
                capture.PersonIds = personIds.ToList();
                capture.Before = personIds
                    .Where(id => kernel.GetPerson(id) != null)
                    .ToDictionary(id => id, id => SnapshotOf(kernel.GetPerson(id)!));
                (bool ok, _) = kernel.CityCommand(city, type, personIds, targetOf(city));
                capture.After = personIds
                    .Where(id => kernel.GetPerson(id) != null)
                    .ToDictionary(id => id, id => SnapshotOf(kernel.GetPerson(id)!));
                if (ok)
                {
                    return capture;
                }
            }

            capture.CityId = 0;
            return capture;
        }

        static int[] RewardTargetIds(object city)
        {
            object? force = PropertyValue(city, "mBelongForce");
            if (force == null)
            {
                return Array.Empty<int>();
            }

            Assembly sim = LoadSangoSimMod();
            object? governor = PropertyValue(force, "mGovernor");
            return KernelAccessor.Persons(sim)
                .Where(person => PropertyValue(person, "mBelongForce") == force &&
                                 !ReferenceEquals(person, governor) &&
                                 PropertyValue(person, "mTroop") == null &&
                                 IntOf(person, "loyalty") < 100)
                .Take(1)
                .Select(person => IntOf(person, "Id"))
                .ToArray();
        }

        static int RecruitTarget(Kernel kernel, Assembly sim, object city)
        {
            object? force = PropertyValue(city, "mBelongForce");
            if (force == null)
            {
                return 0;
            }

            Type stateType = sim.GetType("Sango.Core.PersonStateType", throwOnError: true)!;
            int governor = (int)Enum.Parse(stateType, "Governor");
            int prisoner = (int)Enum.Parse(stateType, "Prisoner");
            int unemployed = (int)Enum.Parse(stateType, "Unemployed");
            foreach (object person in kernel.Persons())
            {
                int state = IntOf(person, "state");
                if (PropertyValue(person, "mBelongForce") != force)
                {
                    if (state != governor && state != prisoner)
                    {
                        return IntOf(person, "Id");
                    }
                }
                else if (state == unemployed || state == prisoner)
                {
                    return IntOf(person, "Id");
                }
            }

            return 0;
        }

        private static class KernelAccessor
        {
            public static IEnumerable<object> Persons(Assembly sim)
            {
                object scenario = sim.GetType("Sango.Core.Scenario", throwOnError: true)!
                    .GetProperty("Cur", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
                return EnumerateSet(FieldValue(scenario, "personSet")).Cast<object>();
            }
        }

        /// <summary>被动内核读取(不再 Boot——断言阶段严禁替换在跑世界)。</summary>
        private sealed class KernelPassiveAccessor
        {
            readonly object _scenario;

            public KernelPassiveAccessor(Assembly sim)
            {
                _scenario = sim.GetType("Sango.Core.Scenario", throwOnError: true)!
                    .GetProperty("Cur", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
                Assert.That(_scenario, Is.Not.Null, "assertions require the running kernel world");
            }

            public object? GetCity(int cityId) => Call(FieldValue(_scenario, "citySet"), "Get", cityId);

            public object? GetPerson(int personId) => Call(FieldValue(_scenario, "personSet"), "Get", personId);
        }

        static object SeedTroopAtCity(Kernel kernel, object city, int troopsWanted)
        {
            var personIds = FreePersons(city)
                .OrderByDescending(person => IntOf(person, "Command"))
                .Take(3)
                .Select(person => IntOf(person, "Id"))
                .ToArray();
            (bool ok, string error, object? troop) = kernel.CreateTroop(city, personIds, troopsWanted, 500, Math.Min(20000, IntOf(city, "food") / 2));
            Assert.That(ok, Is.True, $"seed troop failed: {error}");
            return troop!;
        }


        static object? FindOpenCellNear(Kernel kernel, object center)
        {
            for (int ring = 1; ring <= 2; ring++)
            {
                var frontier = new List<object> { center };
                for (int step = 0; step < ring; step++)
                {
                    var next = new List<object>();
                    foreach (object cell in frontier)
                    {
                        for (int dir = 0; dir < 6; dir++)
                        {
                            object? neighbor = kernel.GetNeighbor(cell, dir);
                            if (neighbor != null)
                            {
                                next.Add(neighbor);
                            }
                        }
                    }

                    frontier = next;
                }

                foreach (object cell in frontier)
                {
                    if (PropertyValue(cell, "troop") == null && PropertyValue(cell, "building") == null)
                    {
                        return cell;
                    }
                }
            }

            return null;
        }

        private static void DisposeNative(ScriptResult result) => result.Release();

        // ---- 测试 ----

        [Test]
        public void NativePerson_Parity_DigestsBitIdentical_WithBattleCapturesAndRewardChain()
        {
            Assembly sim = LoadSangoSimMod();

            // 种子探测(内核路):找"目标城陷落且带俘将"的种子(战斗+俘将链)。
            int chosen = 0;
            foreach (int seed in SeedLadder)
            {
                ScriptResult probe = RunScript(sim, seed, native: false);
                Console.Out.WriteLine($"[d2p-seed-probe] seed {seed}: whiteCityFell={probe.WhiteCityFell} prisoners={probe.PrisonerCount} commands=[{string.Join(",", probe.Captures.Select(c => $"{c.Type}={(c.CityId != 0 ? 1 : 0)}"))}]");
                if (probe.WhiteCityFell && probe.PrisonerCount > 0)
                {
                    chosen = seed;
                    break;
                }
            }

            Assert.That(chosen, Is.Not.EqualTo(0), "no ladder seed reached a target-city fall with prisoners; widen the ladder");

            ScriptResult runA = RunScript(sim, chosen, native: false);
            ScriptResult runB = RunScript(sim, chosen, native: true);
            try
            {
                Assert.That(runB.Digests.Count, Is.EqualTo(runA.Digests.Count), "both runs must advance the same number of turns");
                for (int turn = 0; turn < runA.Digests.Count; turn++)
                {
                    Assert.That(runB.Digests[turn], Is.EqualTo(runA.Digests[turn]),
                        $"turn {turn}: WorldDigest must be bit-identical between kernel-source and component-source runs");
                }

                // 命令面成败对照(含逐回合褒奖链)。
                var flatA = runA.Captures.Select(c => (c.Type, c.CityId)).ToList();
                var flatB = runB.Captures.Select(c => (c.Type, c.CityId)).ToList();
                Assert.That(flatB, Is.EqualTo(flatA), "command gate/execution parity must hold across the initial wave and the per-turn reward chain");

                Assert.That(runB.WhiteCityFell, Is.True, "the native run must also reach the target-city fall");
                Assert.That(runB.PrisonerCount, Is.EqualTo(runA.PrisonerCount),
                    "the native run must observe the same prisoner population (capture face parity)");
                Assert.That(runB.PrisonerCount, Is.GreaterThan(0), "the chosen seed must produce prisoners (capture face coverage)");
                Console.Out.WriteLine($"[d2p-parity] seed {chosen}: {runA.Digests.Count} turns bit-identical; targetFell={runA.WhiteCityFell}; prisoners={runA.PrisonerCount}; captures=[{string.Join(", ", flatA.Select(c => $"{c.Type}:{c.Item2}"))}]");
            }
            finally
            {
                DisposeNative(runB);
            }
        }

        [Test]
        public void NativePerson_WriteFaceLedger_MatchesKernelSemantics()
        {
            Assembly sim = LoadSangoSimMod();
            ScriptResult run = RunScript(sim, 20260902, native: true, turns: 2);
            try
            {
                var kernel = new KernelPassiveAccessor(sim);
                var probes = ReadProbes(run.PersonRuntime!);
                Assert.That(probes.Count, Is.EqualTo(850), "the native run must carry all 850 person entities");
                var probeById = probes.ToDictionary(p => (int)p.GetType().GetProperty("PersonId")!.GetValue(p)!);

                // 训练写面:功勋 +GetJobMeritGain、经验在无升级时 +同值、行动位、账本计量。
                WriteFaceCapture train = run.Captures.FirstOrDefault(c => c.Type == "train" && c.CityId != 0);
                Assert.That(train, Is.Not.Null, "the script must execute at least one train order");
                int trainMeritGain = JobMeritGain(sim, "TrainTroops");
                int trainChecked = 0;
                foreach (int personId in train.PersonIds)
                {
                    object? person = kernel.GetPerson(personId);
                    Assert.That(person, Is.Not.Null, $"train person {personId} must exist");
                    var before = train.Before[personId];
                    var after = train.After[personId];
                    object probe = probeById[personId];
                    int componentMerit = (int)probe.GetType().GetProperty("Merit")!.GetValue(probe)!;

                    Assert.That(after.Merit - before.Merit, Is.EqualTo(trainMeritGain),
                        $"train person {personId}: kernel merit delta must equal the job merit gain");
                    // 组件 == 当前内核真值(后续回合的 AI 内政继续写内核,同步面落账——write-through 一致性合同)。
                    Assert.That(componentMerit, Is.EqualTo(IntOf(person!, "merit")),
                        $"train person {personId}: component merit must equal the live kernel value");
                    Assert.That(after.Exp - before.Exp == trainMeritGain || after.LevelId != before.LevelId,
                        Is.True,
                        $"train person {personId}: exp delta must equal the job gain, except across a level-up wrap");

                    object ledger = probe.GetType().GetProperty("Ledger")!.GetValue(probe)!;
                    Assert.That((int)ledger.GetType().GetField("LastMeritGain")!.GetValue(ledger)!, Is.EqualTo(trainMeritGain),
                        $"train person {personId}: ledger must record the job merit gain");
                    Assert.That((int)ledger.GetType().GetField("TrainCount")!.GetValue(ledger)!, Is.GreaterThanOrEqualTo(1),
                        $"train person {personId}: ledger must count the native train");
                    trainChecked++;
                }

                Assert.That(trainChecked, Is.GreaterThan(0), "the script must execute at least one train order");

                // 褒奖写面:忠诚 +10,属性源 == 当前内核真值,账本计量。
                WriteFaceCapture reward = run.Captures.FirstOrDefault(c => c.Type == "reward" && c.CityId != 0);
                Assert.That(reward, Is.Not.Null, "the script must execute at least one reward order");
                foreach (int personId in reward.PersonIds)
                {
                    var before = reward.Before[personId];
                    var after = reward.After[personId];
                    object? person = kernel.GetPerson(personId);
                    Assert.That(person, Is.Not.Null, $"reward person {personId} must exist");
                    Assert.That(after.Loyalty - before.Loyalty, Is.EqualTo(10),
                        $"reward person {personId}: kernel loyalty delta must equal +10");
                    object probe = probeById[personId];
                    Assert.That((int)probe.GetType().GetProperty("Loyalty")!.GetValue(probe)!, Is.EqualTo(IntOf(person!, "loyalty")),
                        $"reward person {personId}: component loyalty attribute must equal the live kernel value");
                    object ledger = probe.GetType().GetProperty("Ledger")!.GetValue(probe)!;
                    Assert.That((int)ledger.GetType().GetField("LastLoyaltyDelta")!.GetValue(ledger)!, Is.EqualTo(10),
                        $"reward person {personId}: ledger must record +10");
                }

                // 招揽写面:异地走任务态(组件 == 内核 missionType),行动位同拍。
                WriteFaceCapture recruit = run.Captures.FirstOrDefault(c => c.Type == "recruit" && c.CityId != 0);
                Assert.That(recruit, Is.Not.Null, "the script must execute at least one recruit order");
                Type missionEnum = sim.GetType("Sango.Core.MissionType", throwOnError: true)!;
                int recruitMission = (int)Enum.Parse(missionEnum, "PersonRecruitPerson");
                int executorId = recruit.PersonIds[0];
                object executorProbe = probeById[executorId];
                object executorMission = executorProbe.GetType().GetProperty("Mission")!.GetValue(executorProbe)!;
                Assert.That((int)executorMission.GetType().GetField("MissionType")!.GetValue(executorMission)!,
                    Is.EqualTo(IntOf(kernel.GetPerson(executorId)!, "missionType")),
                    "recruit executor: component mission type must equal the kernel value");
                Assert.That((int)executorMission.GetType().GetField("MissionType")!.GetValue(executorMission)! == recruitMission ||
                            IntOf(kernel.GetPerson(executorId)!, "missionType") == (int)Enum.Parse(missionEnum, "PersonReturn"),
                    Is.True,
                    "recruit executor: the mission face must carry the recruit/return mission");
                object executorLedger = executorProbe.GetType().GetProperty("Ledger")!.GetValue(executorProbe)!;
                Assert.That((int)executorLedger.GetType().GetField("MissionCount")!.GetValue(executorLedger)!, Is.GreaterThanOrEqualTo(1),
                    "recruit executor: ledger must count the native mission write");
                Console.Out.WriteLine($"[d2p-ledger] train checked on {trainChecked}; reward on {reward.PersonIds.Count}; recruit executor {executorId}");
            }
            finally
            {
                DisposeNative(run);
            }
        }

        [Test]
        public void NativePerson_SalaryLedger_ComponentSource_MatchesKernelFormula()
        {
            Assembly sim = LoadSangoSimMod();
            // 剧本自带 1 次前置推进,turns: 8 → 总 9 推进 = 月边界回合(旬×3):
            // 俸给输入 == 该回合末名单,账本可与内核公式终态比对。
            ScriptResult run = RunScript(sim, 20260902, native: true, turns: 8);
            try
            {
                var kernel = new KernelPassiveAccessor(sim);
                List<object> cityProbes = ReadCityProbes(run.CityRuntime!);
                Assert.That(cityProbes.Count, Is.EqualTo(88), "the native run must carry all 88 city entities");
                int salaryChecked = 0;
                foreach (object probe in cityProbes)
                {
                    int cityId = (int)probe.GetType().GetProperty("CityId")!.GetValue(probe)!;
                    object? city = kernel.GetCity(cityId);
                    if (city == null || PropertyValue(city, "mBelongCorps") == null)
                    {
                        continue;
                    }

                    object economy = probe.GetType().GetProperty("Economy")!.GetValue(probe)!;
                    int lastSalary = Convert.ToInt32(FieldValue(economy, "LastSalaryGold"));
                    Assert.That(lastSalary, Is.EqualTo(KernelGoldCost(city)),
                        $"city {cityId}: native salary ledger (person-component source) must equal the kernel GoldCost formula");
                    salaryChecked++;
                }

                Assert.That(salaryChecked, Is.GreaterThan(0), "the script must reach at least one owned city");
                Console.Out.WriteLine($"[d2p-salary] component-source salary ledger checked on {salaryChecked} cities");
            }
            finally
            {
                DisposeNative(run);
            }
        }

        [Test]
        public void NativePerson_WorldBin_PersistsPersonEntitiesComponentsAndGasAttributes()
        {
            Assembly sim = LoadSangoSimMod();
            ScriptResult run = RunScript(sim, 20260902, native: true);
            try
            {
                FieldInfo worldField = run.PersonRuntime!.GetType().GetField("_world", BindingFlags.NonPublic | BindingFlags.Instance)!;
                World world = (World)worldField.GetValue(run.PersonRuntime)!;

                var serializer = new LudotsBinaryWorldSerializer();
                byte[] bytes = serializer.Serialize(world);
                Assert.That(bytes.Length, Is.GreaterThan(0), "world.bin payload must not be empty");
                using World restored = serializer.Deserialize(bytes);

                ComponentType identityType = sim.GetType("Sango.Runtime.SangoPersonIdentity", throwOnError: true)!;
                var personEntities = new List<Entity>();
                restored.Query(in QueryDescription.Null, entity =>
                {
                    if (restored.Has(entity, identityType))
                    {
                        personEntities.Add(entity);
                    }
                });
                Assert.That(personEntities.Count, Is.EqualTo(850), "world.bin must round-trip all 850 person entities");

                Type attributesType = sim.GetType("Sango.Runtime.SangoPersonAttributes", throwOnError: true)!;
                int loyaltyId = (int)attributesType.GetProperty("LoyaltyId")!.GetValue(null)!;
                int commandId = (int)attributesType.GetProperty("CommandId")!.GetValue(null)!;
                ComponentType membershipType = sim.GetType("Sango.Runtime.SangoPersonMembership", throwOnError: true)!;
                ComponentType careerType = sim.GetType("Sango.Runtime.SangoPersonCareer", throwOnError: true)!;

                var kernel = new KernelPassiveAccessor(sim);
                MethodInfo getCurrent = typeof(AttributeBuffer).GetMethod("GetCurrent")!;
                int compared = 0;
                foreach (Entity entity in personEntities.Take(25))
                {
                    object identity = restored.Get(entity, identityType)!;
                    int personId = (int)identity.GetType().GetField("PersonId")!.GetValue(identity)!;
                    object? person = kernel.GetPerson(personId);
                    Assert.That(person, Is.Not.Null, $"restored person {personId} must exist in the kernel world");

                    var buffer = restored.Get<AttributeBuffer>(entity);
                    Assert.That(buffer.HasAttribute(loyaltyId), Is.True, $"restored person {personId} must carry sango.person.loyalty");
                    Assert.That((int)(float)getCurrent.Invoke(buffer, new object?[] { loyaltyId })!, Is.EqualTo(IntOf(person!, "loyalty")),
                        $"restored person {personId} loyalty attribute must equal the kernel value");
                    Assert.That((int)(float)getCurrent.Invoke(buffer, new object?[] { commandId })!, Is.EqualTo(IntOf(person!, "Command")),
                        $"restored person {personId} command attribute must equal the kernel computed value");

                    object membership = restored.Get(entity, membershipType)!;
                    Assert.That((int)membership.GetType().GetField("BelongForceId")!.GetValue(membership)!,
                        Is.EqualTo(ForceIdOf(person!)),
                        $"restored person {personId} membership force must equal the kernel live reference");
                    object career = restored.Get(entity, careerType)!;
                    Assert.That((int)career.GetType().GetField("Merit")!.GetValue(career)!, Is.EqualTo(IntOf(person!, "merit")),
                        $"restored person {personId} career merit must equal the kernel value");
                    compared++;
                }

                Console.Out.WriteLine($"[d2p-worldbin] {bytes.Length} bytes; {personEntities.Count} person entities; loyalty/command/membership/career compared on {compared}");
            }
            finally
            {
                DisposeNative(run);
            }
        }

        static int KernelGoldCost(object city)
        {
            int goldCost = 0;
            foreach (object? person in (IEnumerable)FieldValue(FieldValue(city, "allPersons"), "objects"))
            {
                object? official = person == null ? null : PropertyValue(person, "Official");
                if (official != null)
                {
                    goldCost += IntOf(official, "cost");
                }
            }

            foreach (object? captive in (IEnumerable)FieldValue(FieldValue(city, "captiveList"), "objects"))
            {
                if (captive != null)
                {
                    goldCost += 100;
                }
            }

            return goldCost;
        }

        static List<object> ReadProbes(object personRuntime) =>
            ((IEnumerable)personRuntime.GetType()
                .GetMethod("Snapshot", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(personRuntime, null)!).Cast<object>().ToList();

        static List<object> ReadCityProbes(object cityRuntime) =>
            ((IEnumerable)cityRuntime.GetType()
                .GetMethod("Snapshot", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(cityRuntime, null)!).Cast<object>().ToList();
    }
}
