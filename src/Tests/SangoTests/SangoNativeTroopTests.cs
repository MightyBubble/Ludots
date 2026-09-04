using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
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
    /// D-3' 部队/军团域原生实现对拍验收:剧本覆盖 编成 → 委任移动(多回合行军) →
    /// 野战 → 单挑(100% 覆写) → 溃灭/俘获 → 攻城陷落,同种子双源跑(老内核 vs 原生
    /// 部队/军团域挂载)≥10 回合 digest 逐位相等。附加面:
    ///   - 运行内双源对账:原生跑每回合,部队行组件源 == 内核源(digest 行);
    ///   - 移动段/士气落点/携粮逐位:每回合 (id, x, y, 兵力, 士气, 携粮) 轨迹,双跑
    ///     相等且组件源 == 内核源;
    ///   - 技能 CD 逐位:每回合 (技能 id, CD) 轨迹 + 组件 SangoTroopSkillCooldowns ==
    ///     内核 SkillInstance.CDCount;
    ///   - 军团面(消桥 #4):组件 AP/RewardCounter == 内核;AP 发放账本 == 轨迹差分;
    ///   - 生命周期:播种/溃灭/回城解散的事件驱动生灭计数;entities 面世界查询;
    ///   - world.bin:部队/军团实体 + GAS 属性 + 组件存读回环;
    ///   - 存档 CD 面:troopDomain 捕获→回灌回放 CD 逐位。
    /// 反射调用 SangoRuntime(不引用 SangoSimMod 工程——同 SangoNativePersonTests 惯例)。
    /// </summary>
    [TestFixture]
    public sealed class SangoNativeTroopTests
    {
        private const int TurnsToAdvance = 10;
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
            Assert.That(File.Exists(modTemplates), Is.True, "SangoSimMod must ship assets/Entities/templates.json (sango.troop/sango.corps templates)");

            string tempRoot = Path.Combine(Path.GetTempPath(), $"SangoTroopTpl_{Guid.NewGuid():N}");
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

        private static (object CityRuntime, object PersonRuntime, object TroopRuntime, string TempRoot) AttachNative(Assembly sim, World world)
        {
            (EntityLifecycleRuntimeServices services, Ludots.Core.Gameplay.GAS.TagOps tagOps, string tempRoot) = BuildServices(world);
            object city = sim.GetType("Sango.Runtime.SangoCityNativeRuntime", throwOnError: true)!
                .GetMethod("AttachBare", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object[] { world, services, tagOps })!;
            object person = sim.GetType("Sango.Runtime.SangoPersonNativeRuntime", throwOnError: true)!
                .GetMethod("AttachBare", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object[] { world, services, tagOps })!;
            object troop = sim.GetType("Sango.Runtime.SangoTroopNativeRuntime", throwOnError: true)!
                .GetMethod("AttachBare", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object[] { world, services, tagOps })!;
            return (city, person, troop, tempRoot);
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

            public IEnumerable Troops() => EnumerateSet(FieldValue(Scenario, "troopsSet"));

            public IEnumerable Corps() => EnumerateSet(FieldValue(Scenario, "corpsSet"));

            public object? GetCity(int cityId) => Call(FieldValue(Scenario, "citySet"), "Get", cityId);

            public object? GetTroop(int troopId) => Call(FieldValue(Scenario, "troopsSet"), "Get", troopId);

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

            public List<string> KernelTroopRows() =>
                (List<string>)Sim.GetType("Sango.Runtime.SangoTroopNativeRuntime", throwOnError: true)!
                    .GetMethod("KernelTroopRows", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new[] { Scenario })!;

            public List<string> NativeTroopRows(object troopRuntime) =>
                ((IEnumerable)troopRuntime.GetType()
                    .GetMethod("TroopDigestRows", BindingFlags.Public | BindingFlags.Instance)!
                    .Invoke(troopRuntime, null)!).Cast<string>().ToList();

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
            object? value = target.GetType().GetField(member, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
                ?? target.GetType().GetProperty(member, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
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

        static bool PassExpeditionGate(Assembly sim, object city)
        {
            int cost = MakeTroopCostAP(sim);
            return IntOf(city, "troops") > 0 && IntOf(city, "food") > 0 &&
                   FreePersons(city).Count > 0 &&
                   IntOf(PropertyValue(city, "mBelongCorps")!, "ActionPoint") >= cost &&
                   PropertyValue(city, "mBelongForce") != null && PropertyValue(city, "mBelongCorps") != null;
        }

        // ---- 对拍剧本(两路共用;native = 原生城+武将+部队/军团域跑) ----

        private sealed class TroopTrace
        {
            public string Digest = "";
            public List<string> KernelTroopRows = new();
            public List<string> TroopStateRows = new();
            public List<string> CorpsApRows = new();
        }

        private sealed class ScriptResult
        {
            public List<TroopTrace> Turns = new();
            public int CreatedTroops;
            public bool WhiteCityFell;
            public int PrisonerCount;
            public int CommissionMoveAccepted;
            public object? CityRuntime;
            public object? PersonRuntime;
            public object? TroopRuntime;
            public string? TempRoot;
            public World? World;

            public void Release()
            {
                if (TroopRuntime != null)
                {
                    TroopRuntime.GetType().GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance)!
                        .Invoke(TroopRuntime, null);
                    TroopRuntime = null;
                }

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
        /// 剧本:启动 → 推 1 回合(AP 发放)→ 编成(行军队)→ 委任移动(越程目标,
        /// 多回合行军)→ 编成野战对阵(敌对两军互授歼灭)→ 编成攻城队(守军最少白城)
        /// → 100% 单挑覆写下野战首击 → 推 10 回合(逐回合 digest + 部队轨迹 + 军团 AP
        /// 轨迹;原生跑每回合另做部队行组件源 == 内核源双断言)。
        /// </summary>
        private static ScriptResult RunScript(Assembly sim, int seed, bool native, int turns = TurnsToAdvance)
        {
            var kernel = new Kernel(sim, seed);
            var result = new ScriptResult();
            if (!native)
            {
                return RunScriptCore(sim, kernel, null, result, turns);
            }

            World world = World.Create();
            result.World = world;
            try
            {
                (result.CityRuntime, result.PersonRuntime, result.TroopRuntime, result.TempRoot) = AttachNative(sim, world);
                return RunScriptCore(sim, kernel, result.TroopRuntime, result, turns);
            }
            catch
            {
                result.Release();
                throw;
            }
        }

        static ScriptResult RunScriptCore(
            Assembly sim, Kernel kernel, object? troopRuntime, ScriptResult result, int turns)
        {
            kernel.AdvanceTurn();

            List<object> cities = kernel.Cities().Cast<object>().ToList();

            // 编成① 行军队:近距先挪出城块(城中心格占位),再授越程委任移动。
            object home = cities.First(city => PassExpeditionGate(sim, city));
            object marcher = SeedTroopAtCity(kernel, home, 3000);
            result.CreatedTroops++;
            kernel.FillMoveRange(marcher);
            object? openCell = kernel.MoveRangeOf(marcher).Cast<object?>()
                .FirstOrDefault(cell => cell != null && PropertyValue(cell, "troop") == null && PropertyValue(cell, "building") == null);
            Assert.That(openCell, Is.Not.Null, "marcher move range must expose an open cell");
            (bool moved, string moveError) = kernel.MoveTroop(marcher, openCell!);
            Assert.That(moved, Is.True, $"marcher cannot leave the city block: {moveError}");
            kernel.ResetActionOver(marcher);

            object homeCenter = PropertyValue(home, "CenterCell")!;
            object farCity = cities
                .Where(city => ReferenceEquals(city, home) == false)
                .OrderByDescending(city => kernel.Distance(homeCenter, PropertyValue(city, "CenterCell")!))
                .First();
            object? farCell = FindOpenCellNear(kernel, PropertyValue(farCity, "CenterCell")!, rings: 3);
            Assert.That(farCell, Is.Not.Null, "the far commission target must expose an open cell");
            (bool commissioned, string commissionError) = kernel.MoveTroop(marcher, farCell!);
            Assert.That(commissioned, Is.True, $"commissioned move dispatch rejected: {commissionError}");
            result.CommissionMoveAccepted = BoolOf(marcher, "IsAppoint") ? 1 : 0;

            // 编成②③ 野战对阵:异势力两城各编一队,贴脸互授歼灭(野战/单挑/溃灭/俘获面)。
            // 行军队已吃掉 home 的前三空闲武将,对阵双方各取不同城(编成门槛 freePersons
            // 独立成立);守方贴脸靠传送,两城间距离不构成约束。
            object attackerForce = PropertyValue(marcher, "mBelongForce")!;
            object foe = cities
                .Where(city => PropertyValue(city, "mBelongForce") != null &&
                               !ReferenceEquals(PropertyValue(city, "mBelongForce"), attackerForce) &&
                               PassExpeditionGate(sim, city))
                .OrderBy(city => kernel.Distance(homeCenter, PropertyValue(city, "CenterCell")!))
                .First();
            object attackerCity = cities
                .Where(city => !ReferenceEquals(city, home) && !ReferenceEquals(city, foe) &&
                               PassExpeditionGate(sim, city) &&
                               FreePersons(city).Count >= 3)
                .OrderBy(city => kernel.Distance(PropertyValue(city, "CenterCell")!, PropertyValue(foe, "CenterCell")!))
                .First();
            object attacker = SeedTroopAtCity(kernel, attackerCity, 3000);
            object defender = SeedTroopAtCity(kernel, foe, 3000);
            result.CreatedTroops += 2;
            // 攻击方先挪出城块(城中心格邻位皆建筑),再贴脸摆守方。
            kernel.FillMoveRange(attacker);
            object? attackerOpen = kernel.MoveRangeOf(attacker).Cast<object?>()
                .FirstOrDefault(cell => cell != null && PropertyValue(cell, "troop") == null && PropertyValue(cell, "building") == null);
            Assert.That(attackerOpen, Is.Not.Null, "attacker move range must expose an open cell");
            (bool attackerMoved, string attackerMoveError) = kernel.MoveTroop(attacker, attackerOpen!);
            Assert.That(attackerMoved, Is.True, $"attacker cannot leave the city block: {attackerMoveError}");
            kernel.ResetActionOver(attacker);
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

            // 编成④ 攻城队:守军最少白城 + 最近过门槛城贴城(攻城陷落面;与对阵城不同源,
            // 各自吃满编成门槛)。
            object targetCity = cities
                .Where(city => PropertyValue(city, "mBelongForce") == null)
                .OrderBy(city => IntOf(city, "troops"))
                .First();
            object staging = cities
                .Where(city => PassExpeditionGate(sim, city) && !ReferenceEquals(city, home) &&
                               !ReferenceEquals(city, foe) && !ReferenceEquals(city, attackerCity) &&
                               FreePersons(city).Count >= 3)
                .OrderBy(city => kernel.Distance(PropertyValue(city, "CenterCell")!, PropertyValue(targetCity, "CenterCell")!))
                .First();
            object siegeTroop = SeedTroopAtCity(kernel, staging, 3000);
            result.CreatedTroops++;
            object? siegeDest = FindOpenCellNear(kernel, PropertyValue(targetCity, "CenterCell")!, rings: 2);
            Assert.That(siegeDest, Is.Not.Null, "the white city must expose an open staging cell within two rings");
            kernel.TeleportTroop(siegeTroop, siegeDest!);

            kernel.AttachChallenges("Duel", 100);
            try
            {
                (bool struck, string strikeError) = kernel.MoveTroop(attacker, dest!);
                Assert.That(struck, Is.True, $"field strike dispatch rejected: {strikeError}");
                AdvanceTurns(kernel, troopRuntime, result, turns);
            }
            finally
            {
                kernel.DetachChallenges();
            }

            result.WhiteCityFell = PropertyValue(targetCity, "mBelongCorps") != null;
            result.PrisonerCount = CountCaptives(kernel);
            return result;
        }

        static int CountCaptives(Kernel kernel)
        {
            int count = 0;
            foreach (object force in EnumerateSet(FieldValue(kernel.Scenario, "forceSet")))
            {
                count += Convert.ToInt32(PropertyValue(FieldValue(force, "BeCaptiveList"), "Count"));
            }

            return count;
        }

        // 逐回合:digest + 内核部队行 + 部队状态轨迹(id:x:y:兵力:士气:携粮:CD 段)
        // + 军团 AP 轨迹;原生跑每回合组件源 == 内核源。
        static void AdvanceTurns(Kernel kernel, object? troopRuntime, ScriptResult result, int turns)
        {
            for (int turn = 0; turn < turns; turn++)
            {
                kernel.AdvanceTurn();
                var trace = new TroopTrace
                {
                    Digest = kernel.WorldDigest(),
                    KernelTroopRows = kernel.KernelTroopRows(),
                    TroopStateRows = KernelTroopStateRows(kernel),
                    CorpsApRows = KernelCorpsApRows(kernel),
                };
                result.Turns.Add(trace);
                if (troopRuntime != null)
                {
                    List<string> componentRows = kernel.NativeTroopRows(troopRuntime);
                    Assert.That(componentRows.Count, Is.EqualTo(trace.KernelTroopRows.Count), $"turn {turn}: component/kernel troop row count must match");
                    for (int row = 0; row < trace.KernelTroopRows.Count; row++)
                    {
                        Assert.That(componentRows[row], Is.EqualTo(trace.KernelTroopRows[row]),
                            $"turn {turn}: component-source troop row must equal kernel-source row (index {row})");
                    }
                }
            }
        }

        /// <summary>内核部队状态轨迹:id:x:y:兵力:士气:携粮:技能CD段(skillId=cd;序)。</summary>
        static List<string> KernelTroopStateRows(Kernel kernel)
        {
            var rows = new List<string>();
            foreach (object troop in kernel.Troops())
            {
                if (!BoolOf(troop, "IsAlive"))
                {
                    continue;
                }

                var cds = new List<string>();
                foreach (object? skill in KernelSkillsOf(troop))
                {
                    if (skill != null)
                    {
                        object skillData = FieldValue(skill, "skill")!;
                        cds.Add($"{IntOf(skillData, "Id")}={IntOf(skill, "CDCount")}");
                    }
                }

                rows.Add(
                    $"{IntOf(troop, "Id")}:{IntOf(troop, "x")}:{IntOf(troop, "y")}:{IntOf(troop, "troops")}:" +
                    $"{IntOf(troop, "morale")}:{IntOf(troop, "food")}:{string.Join(";", cds)}");
            }

            rows.Sort(StringComparer.Ordinal);
            return rows;
        }

        static IEnumerable KernelSkillsOf(object troop)
        {
            foreach (string listName in new[] { "landSkills", "waterSkills", "StrategySkills" })
            {
                object? list = troop.GetType().GetField(listName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(troop);
                if (list is IEnumerable skills)
                {
                    foreach (object? skill in skills)
                    {
                        yield return skill;
                    }
                }
            }
        }

        /// <summary>内核军团 AP 轨迹:corpsId:AP:RewardCounter。</summary>
        static List<string> KernelCorpsApRows(Kernel kernel)
        {
            var rows = new List<string>();
            foreach (object corps in kernel.Corps())
            {
                int rewardCounter = Convert.ToInt32(Call(corps, "GetJobCounter", (int)Enum.Parse(
                    kernel.Sim.GetType("Sango.Core.CityJobType", throwOnError: true)!, "Reward"))!);
                rows.Add($"{IntOf(corps, "Id")}:{IntOf(corps, "ActionPoint")}:{rewardCounter}");
            }

            rows.Sort(StringComparer.Ordinal);
            return rows;
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

        static object? FindOpenCellNear(Kernel kernel, object center, int rings = 2)
        {
            for (int ring = 1; ring <= rings; ring++)
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
        public void NativeTroop_Parity_DigestsAndTracesBitIdentical_AcrossMarchFieldDuelRoutSiege()
        {
            Assembly sim = LoadSangoSimMod();

            // 种子探测(内核路):找"白城陷落 + 俘将 + 有部队溃灭"的种子。
            int chosen = 0;
            foreach (int seed in SeedLadder)
            {
                ScriptResult probe = RunScript(sim, seed, native: false);
                int alive = probe.Turns.Count == 0 ? 0 : probe.Turns[^1].KernelTroopRows.Count;
                Console.Out.WriteLine($"[d3p-seed-probe] seed {seed}: whiteCityFell={probe.WhiteCityFell} prisoners={probe.PrisonerCount} aliveTroops={alive} commission={probe.CommissionMoveAccepted}");
                if (probe.WhiteCityFell && probe.PrisonerCount > 0 && probe.CreatedTroops - alive >= 1)
                {
                    chosen = seed;
                    break;
                }
            }

            Assert.That(chosen, Is.Not.EqualTo(0), "no ladder seed reached siege fall + prisoners + at least one routed troop; widen the ladder");

            ScriptResult runA = RunScript(sim, chosen, native: false);
            ScriptResult runB = RunScript(sim, chosen, native: true);
            try
            {
                Assert.That(runB.Turns.Count, Is.EqualTo(runA.Turns.Count), "both runs must advance the same number of turns");
                for (int turn = 0; turn < runA.Turns.Count; turn++)
                {
                    Assert.That(runB.Turns[turn].Digest, Is.EqualTo(runA.Turns[turn].Digest),
                        $"turn {turn}: WorldDigest must be bit-identical between kernel-source and component-source runs");
                    Assert.That(runB.Turns[turn].TroopStateRows, Is.EqualTo(runA.Turns[turn].TroopStateRows),
                        $"turn {turn}: troop state trace (movement/morale/food/skill CD) must be bit-identical across runs");
                    Assert.That(runB.Turns[turn].CorpsApRows, Is.EqualTo(runA.Turns[turn].CorpsApRows),
                        $"turn {turn}: corps AP/RewardCounter trace must be bit-identical across runs (bridge #4 read/write faces)");
                }

                Assert.That(runB.WhiteCityFell, Is.True, "the native run must also reach the target-city fall");
                Assert.That(runB.PrisonerCount, Is.EqualTo(runA.PrisonerCount), "capture face parity");
                Assert.That(runB.PrisonerCount, Is.GreaterThan(0), "the chosen seed must produce prisoners");
                Assert.That(runB.CommissionMoveAccepted, Is.EqualTo(1), "the commission mission must be armed (multi-turn march)");
                Console.Out.WriteLine($"[d3p-parity] seed {chosen}: {runA.Turns.Count} turns bit-identical (digest + troop state + corps AP); targetFell; prisoners={runA.PrisonerCount}");
            }
            finally
            {
                DisposeNative(runB);
            }
        }

        [Test]
        public void NativeTroop_ComponentsMatchKernel_LedgerAndLifecycleProbes()
        {
            Assembly sim = LoadSangoSimMod();
            ScriptResult run = RunScript(sim, 20260903, native: true);
            try
            {
                var runtime = run.TroopRuntime!;
                object scenario = new Func<object>(() => sim.GetType("Sango.Core.Scenario", throwOnError: true)!
                    .GetProperty("Cur", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!)();

                List<object> troopProbes = ReadTroopProbes(runtime);
                Dictionary<int, object> kernelTroops = KernelTroopIdMap(sim);
                Assert.That(troopProbes.Count, Is.EqualTo(kernelTroops.Count),
                    "native troop entities must cover exactly the alive kernel troops");

                int moraleChecked = 0;
                int cdChecked = 0;
                int missionChecked = 0;
                int foodCostChecked = 0;
                foreach (object probe in troopProbes)
                {
                    int troopId = (int)probe.GetType().GetProperty("TroopId")!.GetValue(probe)!;
                    object troop = kernelTroops[troopId];
                    int kernelMorale = IntOf(troop, "morale");
                    int kernelTroopCount = IntOf(troop, "troops");
                    int kernelFood = IntOf(troop, "food");
                    Assert.That((int)probe.GetType().GetProperty("Morale")!.GetValue(probe)!, Is.EqualTo(kernelMorale),
                        $"troop {troopId}: component morale attribute must equal kernel (bitwise morale landing)");
                    Assert.That((int)probe.GetType().GetProperty("Troops")!.GetValue(probe)!, Is.EqualTo(kernelTroopCount),
                        $"troop {troopId}: component troops attribute must equal kernel");
                    Assert.That((int)probe.GetType().GetProperty("Food")!.GetValue(probe)!, Is.EqualTo(kernelFood),
                        $"troop {troopId}: component food attribute must equal kernel");
                    Assert.That((int)probe.GetType().GetProperty("CellX")!.GetValue(probe)!, Is.EqualTo(IntOf(troop, "x")));
                    Assert.That((int)probe.GetType().GetProperty("CellY")!.GetValue(probe)!, Is.EqualTo(IntOf(troop, "y")));
                    moraleChecked++;

                    // 技能 CD 面:组件条目(技能 id + CD)与内核 SkillInstance 逐位。
                    object cooldowns = probe.GetType().GetProperty("SkillCooldowns")!.GetValue(probe)!;
                    var componentEntries = new List<(int SkillId, int Cd)>();
                    int count = (int)cooldowns.GetType().GetField("Count")!.GetValue(cooldowns)!;
                    MethodInfo skillIdAt = cooldowns.GetType().GetMethod("SkillIdAt")!;
                    MethodInfo cdAt = cooldowns.GetType().GetMethod("CdAt")!;
                    for (int i = 0; i < count; i++)
                    {
                        componentEntries.Add(((int)skillIdAt.Invoke(cooldowns, new object[] { i })!, (int)cdAt.Invoke(cooldowns, new object[] { i })!));
                    }

                    var kernelEntries = new List<(int SkillId, int Cd)>();
                    foreach (object? skill in KernelSkillsOf(troop))
                    {
                        if (skill != null)
                        {
                            kernelEntries.Add((IntOf(FieldValue(skill, "skill")!, "Id"), IntOf(skill, "CDCount")));
                        }
                    }

                    Assert.That(componentEntries, Is.EqualTo(kernelEntries),
                        $"troop {troopId}: skill cooldown component must equal kernel SkillInstance CDCounts in enumeration order");
                    cdChecked++;

                    // 任务态显式组件 == 内核 missionType/missionTarget/params。
                    object mission = probe.GetType().GetProperty("Mission")!.GetValue(probe)!;
                    Assert.That((int)mission.GetType().GetField("MissionType")!.GetValue(mission)!, Is.EqualTo(IntOf(troop, "missionType")),
                        $"troop {troopId}: component mission type must equal kernel (explicit mission component)");
                    Assert.That((int)mission.GetType().GetField("MissionTarget")!.GetValue(mission)!, Is.EqualTo(IntOf(troop, "missionTarget")));
                    Assert.That((int)mission.GetType().GetField("MissionParams1")!.GetValue(mission)!, Is.EqualTo(IntOf(troop, "missionParams1")));
                    Assert.That((int)mission.GetType().GetField("MissionParams2")!.GetValue(mission)!, Is.EqualTo(IntOf(troop, "missionParams2")));
                    missionChecked++;

                    // 耗粮账本 == 内核公式复算(PrepeareFoodCost 两段 ceiling)。
                    if (kernelFood > 0)
                    {
                        object variables = FieldValue(scenario, "Variables")!;
                        float baseCost = Convert.ToSingle(FieldValue(variables, "baseFoodCostInTroop"));
                        object troopType = FieldValue(troop, "TroopType")!;
                        float typeFactor = Convert.ToSingle(FieldValue(troopType, "foodCostFactor"));
                        int expected = (int)Math.Ceiling(baseCost * (kernelTroopCount + IntOf(troop, "woundedTroops")) * typeFactor);
                        expected = (int)Math.Ceiling(expected * Convert.ToSingle(FieldValue(troop, "foodCostFactor")));
                        object ledger = probe.GetType().GetProperty("Ledger")!.GetValue(probe)!;
                        Assert.That((int)ledger.GetType().GetField("LastFoodCost")!.GetValue(ledger)!, Is.EqualTo(expected),
                            $"troop {troopId}: ledger food cost must equal the kernel PrepeareFoodCost formula");
                        Assert.That((int)ledger.GetType().GetField("LastFoodCost")!.GetValue(ledger)!, Is.EqualTo(IntOf(troop, "foodCost")),
                            $"troop {troopId}: ledger food cost must equal the observed kernel value");
                        foodCostChecked++;
                    }
                }

                // 军团面(消桥 #4):组件 AP/RewardCounter == 内核;AP 发放账本 == 轨迹差分。
                List<object> corpsProbes = ReadCorpsProbes(runtime);
                Dictionary<int, object> kernelCorps = KernelCorpsIdMap(sim);
                Assert.That(corpsProbes.Count, Is.EqualTo(kernelCorps.Count), "native corps entities must cover the kernel corpsSet");
                Dictionary<int, (int PrevAp, int LastAp)> corpsApTrace = CorpsApTrace(run);
                int rewardCounterChecked = 0;
                int apLedgerChecked = 0;
                foreach (object probe in corpsProbes)
                {
                    int corpsId = (int)probe.GetType().GetProperty("CorpsId")!.GetValue(probe)!;
                    object corps = kernelCorps[corpsId];
                    Assert.That((int)probe.GetType().GetProperty("ActionPoint")!.GetValue(probe)!, Is.EqualTo(IntOf(corps, "ActionPoint")),
                        $"corps {corpsId}: component AP must equal kernel (bridge #4 component source)");
                    int rewardCounter = Convert.ToInt32(Call(corps, "GetJobCounter", (int)Enum.Parse(
                        sim.GetType("Sango.Core.CityJobType", throwOnError: true)!, "Reward"))!);
                    Assert.That((int)probe.GetType().GetProperty("RewardJobCounter")!.GetValue(probe)!, Is.EqualTo(rewardCounter),
                        $"corps {corpsId}: component RewardCounter must equal kernel jobCounter");
                    rewardCounterChecked++;

                    if (corpsApTrace.TryGetValue(corpsId, out (int PrevAp, int LastAp) trace))
                    {
                        object ledger = probe.GetType().GetProperty("Ledger")!.GetValue(probe)!;
                        int granted = (int)ledger.GetType().GetField("LastApGranted")!.GetValue(ledger)!;
                        Assert.That(granted, Is.EqualTo(Math.Max(0, trace.LastAp - trace.PrevAp)),
                            $"corps {corpsId}: AP grant ledger must equal the observed per-turn kernel delta");
                        apLedgerChecked++;
                    }
                }

                // 生命周期:播种物化计数 ≥ 剧本编成数(AI 编成也入计数);实体守恒 =
                // 物化数 - 溃灭/解散数 == 存活数(事件驱动物化/销毁闭环)。
                int spawnCount = (int)runtime.GetType().GetProperty("TroopSpawnCount")!.GetValue(runtime)!;
                int clearCount = (int)runtime.GetType().GetProperty("TroopClearCount")!.GetValue(runtime)!;
                int destroyCount = (int)runtime.GetType().GetProperty("TroopDestroyCount")!.GetValue(runtime)!;
                Assert.That(spawnCount, Is.GreaterThanOrEqualTo(run.CreatedTroops),
                    "every seeded troop (CreateTroop -> OnTroopCreated) must materialize a native entity");
                Assert.That(spawnCount - clearCount - destroyCount, Is.EqualTo(troopProbes.Count),
                    "native troop entities must conserve: materialized - destroyed/dissolved == alive");

                Console.Out.WriteLine($"[d3p-probes] morale/troops/food checked on {moraleChecked}; CD on {cdChecked}; mission on {missionChecked}; foodCost on {foodCostChecked}; corps AP/counter on {rewardCounterChecked}; AP ledger on {apLedgerChecked}; lifecycle spawn={spawnCount} clear={clearCount} destroy={destroyCount}");
            }
            finally
            {
                DisposeNative(run);
            }
        }

        [Test]
        public void NativeTroop_WorldBin_PersistsTroopCorpsEntitiesComponentsAndGasAttributes()
        {
            Assembly sim = LoadSangoSimMod();
            ScriptResult run = RunScript(sim, 20260903, native: true, turns: 3);
            try
            {
                FieldInfo worldField = run.TroopRuntime!.GetType().GetField("_world", BindingFlags.NonPublic | BindingFlags.Instance)!;
                World world = (World)worldField.GetValue(run.TroopRuntime)!;

                var serializer = new LudotsBinaryWorldSerializer();
                byte[] bytes = serializer.Serialize(world);
                Assert.That(bytes.Length, Is.GreaterThan(0), "world.bin payload must not be empty");
                using World restored = serializer.Deserialize(bytes);

                ComponentType troopIdentityType = sim.GetType("Sango.Runtime.SangoTrooperIdentity", throwOnError: true)!;
                ComponentType corpsIdentityType = sim.GetType("Sango.Runtime.SangoCorpsIdentity", throwOnError: true)!;
                var troopEntities = new List<Entity>();
                var corpsEntities = new List<Entity>();
                restored.Query(in QueryDescription.Null, entity =>
                {
                    if (restored.Has(entity, troopIdentityType))
                    {
                        troopEntities.Add(entity);
                    }

                    if (restored.Has(entity, corpsIdentityType))
                    {
                        corpsEntities.Add(entity);
                    }
                });

                Dictionary<int, object> kernelTroops = KernelTroopIdMap(sim);
                Dictionary<int, object> kernelCorps = KernelCorpsIdMap(sim);
                Assert.That(troopEntities.Count, Is.EqualTo(kernelTroops.Count), "world.bin must round-trip all troop entities");
                Assert.That(corpsEntities.Count, Is.EqualTo(kernelCorps.Count), "world.bin must round-trip all corps entities");

                Type attributesType = sim.GetType("Sango.Runtime.SangoTroopAttributes", throwOnError: true)!;
                int troopsId = (int)attributesType.GetProperty("TroopsId")!.GetValue(null)!;
                int moraleId = (int)attributesType.GetProperty("MoraleId")!.GetValue(null)!;
                ComponentType missionType = sim.GetType("Sango.Runtime.SangoTroopMission", throwOnError: true)!;
                ComponentType cooldownsType = sim.GetType("Sango.Runtime.SangoTroopSkillCooldowns", throwOnError: true)!;
                MethodInfo getCurrent = typeof(AttributeBuffer).GetMethod("GetCurrent")!;
                int attrChecked = 0;
                foreach (Entity entity in troopEntities)
                {
                    object identity = restored.Get(entity, troopIdentityType)!;
                    int troopId = (int)identity.GetType().GetField("TroopId")!.GetValue(identity)!;
                    Assert.That(kernelTroops.ContainsKey(troopId), Is.True, $"restored troop {troopId} must exist in the kernel world");

                    var buffer = restored.Get<AttributeBuffer>(entity);
                    Assert.That(buffer.HasAttribute(troopsId), Is.True, $"restored troop {troopId} must carry sango.troop.troops");
                    Assert.That((int)(float)getCurrent.Invoke(buffer, new object?[] { troopsId })!, Is.EqualTo(IntOf(kernelTroops[troopId], "troops")),
                        $"restored troop {troopId} troops attribute must equal the kernel value");
                    Assert.That((int)(float)getCurrent.Invoke(buffer, new object?[] { moraleId })!, Is.EqualTo(IntOf(kernelTroops[troopId], "morale")),
                        $"restored troop {troopId} morale attribute must equal the kernel value");
                    Assert.That(restored.Has(entity, missionType), Is.True, $"restored troop {troopId} must carry the mission component");
                    Assert.That(restored.Has(entity, cooldownsType), Is.True, $"restored troop {troopId} must carry the skill cooldown component");
                    attrChecked++;
                }

                // entities 面(世界查询):部队实体按播种/溃灭生灭——存活集 == 内核存活集。
                var restoredTroopIds = new HashSet<int>();
                foreach (Entity entity in troopEntities)
                {
                    object identity = restored.Get(entity, troopIdentityType)!;
                    restoredTroopIds.Add((int)identity.GetType().GetField("TroopId")!.GetValue(identity)!);
                }

                Assert.That(restoredTroopIds.OrderBy(id => id), Is.EqualTo(kernelTroops.Keys.OrderBy(id => id)),
                    "entity-query view of troops must match the kernel alive troop set (spawn/rout lifecycle)");
                Console.Out.WriteLine($"[d3p-worldbin] {bytes.Length} bytes; {troopEntities.Count} troop + {corpsEntities.Count} corps entities; attrs/mission/CD checked on {attrChecked}");
            }
            finally
            {
                DisposeNative(run);
            }
        }

        [Test]
        public void NativeTroop_SaveDomain_ReplaysSkillCooldownsIntoRestoredKernel()
        {
            Assembly sim = LoadSangoSimMod();
            ScriptResult run = RunScript(sim, 20260903, native: true, turns: 3);
            try
            {
                // 存档前观测:内核 CD 面(战斗用过的技能 CD>0)。
                Dictionary<int, List<(int SkillId, int Cd)>> before = KernelCooldownMap(sim);
                var participant = (ISaveParticipant)Activator.CreateInstance(
                    sim.GetType("Sango.Runtime.SangoSaveParticipant", throwOnError: true)!,
                    NewVfs(), "SangoContentMod", "Scenario/Scenario.json")!;
                JsonNode state = participant.CaptureState();
                Assert.That(state["troopDomain"], Is.Not.Null, "the sango.sim capture must carry the troopDomain node (D-3' skill CD face)");
                Assert.That(state["cityOrder"]!["troopSkillCd"], Is.Null,
                    "the retired SangoCityPersonOrder.TroopSkillCd section must no longer be captured");

                participant.RestoreState(state);

                foreach (KeyValuePair<int, List<(int SkillId, int Cd)>> entry in before)
                {
                    object? troop = sim.GetType("Sango.Core.Scenario", throwOnError: true)!
                        .GetProperty("Cur", BindingFlags.Public | BindingFlags.Static)!.GetValue(null) is { } scenario
                        ? Call(FieldValue(scenario, "troopsSet"), "Get", entry.Key)
                        : null;
                    if (troop == null)
                    {
                        continue;
                    }

                    var restoredCds = new List<(int SkillId, int Cd)>();
                    foreach (object? skill in KernelSkillsOf(troop))
                    {
                        if (skill != null)
                        {
                            restoredCds.Add((IntOf(FieldValue(skill, "skill")!, "Id"), IntOf(skill, "CDCount")));
                        }
                    }

                    Assert.That(restoredCds, Is.EqualTo(entry.Value),
                        $"troop {entry.Key}: restored kernel skill cooldowns must equal the pre-capture values (troopDomain replay)");
                }

                int withCd = before.Count(pair => pair.Value.Any(cd => cd.Cd > 0));
                Console.Out.WriteLine($"[d3p-save-cd] replayed CD face on {before.Count} troop(s); {withCd} with active cooldowns");
            }
            finally
            {
                DisposeNative(run);
            }
        }

        // ---- 探针读取(反射 record struct) ----

        static List<object> ReadTroopProbes(object runtime) => ((IEnumerable)runtime.GetType()
            .GetMethod("TroopSnapshot", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(runtime, null)!).Cast<object>().ToList();

        static List<object> ReadCorpsProbes(object runtime) => ((IEnumerable)runtime.GetType()
            .GetMethod("CorpsSnapshot", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(runtime, null)!).Cast<object>().ToList();

        static Dictionary<int, object> KernelTroopIdMap(Assembly sim)
        {
            object scenario = sim.GetType("Sango.Core.Scenario", throwOnError: true)!
                .GetProperty("Cur", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            var map = new Dictionary<int, object>();
            foreach (object troop in EnumerateSet(FieldValue(scenario, "troopsSet")))
            {
                if (BoolOf(troop, "IsAlive"))
                {
                    map[IntOf(troop, "Id")] = troop;
                }
            }

            return map;
        }

        static Dictionary<int, object> KernelCorpsIdMap(Assembly sim)
        {
            object scenario = sim.GetType("Sango.Core.Scenario", throwOnError: true)!
                .GetProperty("Cur", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            var map = new Dictionary<int, object>();
            foreach (object corps in EnumerateSet(FieldValue(scenario, "corpsSet")))
            {
                map[IntOf(corps, "Id")] = corps;
            }

            return map;
        }

        static Dictionary<int, List<(int SkillId, int Cd)>> KernelCooldownMap(Assembly sim)
        {
            var map = new Dictionary<int, List<(int SkillId, int Cd)>>();
            foreach (KeyValuePair<int, object> pair in KernelTroopIdMap(sim))
            {
                var cds = new List<(int SkillId, int Cd)>();
                foreach (object? skill in KernelSkillsOf(pair.Value))
                {
                    if (skill != null)
                    {
                        cds.Add((IntOf(FieldValue(skill, "skill")!, "Id"), IntOf(skill, "CDCount")));
                    }
                }

                map[pair.Key] = cds;
            }

            return map;
        }

        /// <summary>军团 AP 轨差(倒数第二拍 → 末拍):AP 发放账本断言的观测真源。</summary>
        static Dictionary<int, (int PrevAp, int LastAp)> CorpsApTrace(ScriptResult run)
        {
            Dictionary<int, int>? previousAp = null;
            Dictionary<int, int> lastAp = new();
            foreach (TroopTrace trace in run.Turns)
            {
                previousAp = lastAp;
                lastAp = new Dictionary<int, int>();
                foreach (string row in trace.CorpsApRows)
                {
                    string[] parts = row.Split(':');
                    lastAp[int.Parse(parts[0])] = int.Parse(parts[1]);
                }
            }

            Assert.That(run.Turns.Count, Is.GreaterThanOrEqualTo(2), "the AP grant ledger assertion needs at least two observed turns");
            var result = new Dictionary<int, (int PrevAp, int LastAp)>();
            if (previousAp != null)
            {
                foreach (KeyValuePair<int, int> entry in lastAp)
                {
                    if (previousAp.TryGetValue(entry.Key, out int prev))
                    {
                        result[entry.Key] = (prev, entry.Value);
                    }
                }
            }

            return result;
        }
    }
}
