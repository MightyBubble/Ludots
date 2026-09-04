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
    /// D-1' 城域原生实现对拍验收:同种子同命令流(四型内政令 + 播种攻城 + 推 N 回合,
    /// 含城陷)老内核跑一遍(不挂原生运行时,digest 城域行=内核源)与原生城域跑一遍
    /// (挂载,digest 城域行=组件源),逐回合逐位相等——"正经重写没有改变玩法语义"的铁证。
    /// 附加面:
    ///   - 运行内双源对账:原生跑的每一回合,组件源城行 == 内核源城行(保留面对账);
    ///   - 命令面:四型内政令在两路同参数下同成败(门槛/结算等价);
    ///   - 经济账本:LastSalaryGold 按内核公式独立复算断言,LastIncomeGold 落 ±5% 抽带;
    ///   - world.bin:城实体 + GAS 属性随引擎原生序列化存读回环。
    /// 反射调用 SangoRuntime(不引用 SangoSimMod 工程——同 SangoEntityMirrorTests 惯例)。
    /// </summary>
    [TestFixture]
    public sealed class SangoNativeCityTests
    {
        private const int TurnsToAdvance = 10;
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

        /// <summary>临时 Core 挂载(目录 + 拷贝 mod 真实模板资产 + 最小 config_catalog)。</summary>
        private static (EntityLifecycleRuntimeServices Services, TagOps TagOps, string TempRoot) BuildCityServices(World world)
        {
            string modTemplates = Path.Combine(RepoRoot(), "mods", "sango", "SangoSimMod", "assets", "Entities", "templates.json");
            Assert.That(File.Exists(modTemplates), Is.True, "SangoSimMod must ship assets/Entities/templates.json (sango.city template)");

            string tempRoot = Path.Combine(Path.GetTempPath(), $"SangoCityTpl_{Guid.NewGuid():N}");
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

            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
            var services = new EntityLifecycleRuntimeServices(
                world,
                templates,
                new EntityTemplateKeyRegistry(),
                new PresentationStableIdAllocator(),
                tagOps);
            return (services, tagOps, tempRoot);
        }

        private static object AttachNative(Assembly sim, World world, out string tempRoot)
        {
            (EntityLifecycleRuntimeServices services, TagOps tagOps, tempRoot) = BuildCityServices(world);
            return sim.GetType("Sango.Runtime.SangoCityNativeRuntime", throwOnError: true)!
                .GetMethod("AttachBare", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object[] { world, services, tagOps })!;
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

            public List<string> KernelCityRows() =>
                (List<string>)Sim.GetType("Sango.Runtime.SangoCityNativeRuntime", throwOnError: true)!
                    .GetMethod("KernelCityRows", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new[] { Scenario })!;

            public List<string> NativeCityRows(object nativeRuntime) =>
                ((IEnumerable)nativeRuntime.GetType()
                    .GetMethod("CityDigestRows", BindingFlags.Public | BindingFlags.Instance)!
                    .Invoke(nativeRuntime, null)!).Cast<string>().ToList();

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

        // ---- 对拍剧本(两路共用;native = 原生城域跑) ----

        private sealed class ScriptResult
        {
            public List<string> Digests = new();
            public List<(string Type, int CityId, bool Succeeded)> CommandResults = new();
            public bool WhiteCityFell;
            public object? NativeRuntime;
            public string? TempRoot;
            public World? World;

            /// <summary>世界由调用方持有(world.bin 断言要在运行后序列化);失败路径由 RunScript 清理。</summary>
            public void Release()
            {
                if (NativeRuntime != null)
                {
                    NativeRuntime.GetType().GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance)!
                        .Invoke(NativeRuntime, null);
                    NativeRuntime = null;
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
        /// 剧本:启动 → 推 1 回合(AP 发放)→ 四型内政令(逐城探测,成功即记;两路各自
        /// 执行同一探测循环,成败/城市对照)→ 播种攻城(守军最少白城 + 最近过门槛城贴城)
        /// → 推 10 回合逐回合 digest。原生跑每回合另做组件源 == 内核源双断言。
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
            object nativeRuntime;
            string tempRoot;
            try
            {
                nativeRuntime = AttachNative(sim, world, out tempRoot);
            }
            catch
            {
                world.Dispose();
                result.World = null;
                throw;
            }

            result.TempRoot = tempRoot;
            try
            {
                return RunScriptCore(sim, kernel, nativeRuntime, result, turns);
            }
            catch
            {
                result.Release();
                throw;
            }
        }

        static ScriptResult RunScriptCore(Assembly sim, Kernel kernel, object? nativeRuntime, ScriptResult result, int turns)
        {
            result.NativeRuntime = nativeRuntime;
            kernel.AdvanceTurn();

            // 四型内政令探测(同循环两路同跑;成败对照即门槛/结算等价证明)。
            List<object> cities = kernel.Cities().Cast<object>().ToList();
            result.CommandResults.Add(RunCommandProbe(kernel, sim, "train", cities, city => PersonIdsOf(city, 2), _ => 0));
            result.CommandResults.Add(RunCommandProbe(kernel, sim, "search", cities, city => PersonIdsOf(city, 1), _ => 0));
            result.CommandResults.Add(RunCommandProbe(kernel, sim, "reward", cities, RewardTargetIds, _ => 0));
            result.CommandResults.Add(RunCommandProbe(kernel, sim, "recruit", cities, city => PersonIdsOf(city, 1), city => RecruitTarget(kernel, sim, city)));

            // 播种攻城:攻方过门槛城出队挪出城;守军最少白城 + 最近过门槛城编队传送贴城。
            object home = cities.First(city => PassExpeditionGate(sim, city));
            object attacker = SeedTroopAtCity(kernel, home, 3000);
            kernel.FillMoveRange(attacker);
            object? openCell = kernel.MoveRangeOf(attacker).Cast<object?>()
                .FirstOrDefault(cell => cell != null && PropertyValue(cell, "troop") == null && PropertyValue(cell, "building") == null);
            Assert.That(openCell, Is.Not.Null, "attacker move range must expose an open cell");
            (bool moved, string moveError) = kernel.MoveTroop(attacker, openCell!);
            Assert.That(moved, Is.True, $"attacker cannot leave the city block: {moveError}");
            kernel.ResetActionOver(attacker);

            object targetCity = cities
                .Where(city => PropertyValue(city, "mBelongForce") == null)
                .OrderBy(city => IntOf(city, "troops"))
                .First();
            object staging = cities
                .Where(city => PassExpeditionGate(sim, city) && !ReferenceEquals(city, home))
                .OrderBy(city => kernel.Distance(PropertyValue(city, "CenterCell")!, PropertyValue(targetCity, "CenterCell")!))
                .First();
            object siegeTroop = SeedTroopAtCity(kernel, staging, 3000);
            object? siegeDest = FindOpenCellNear(kernel, PropertyValue(targetCity, "CenterCell")!);
            Assert.That(siegeDest, Is.Not.Null, "the white city must expose an open staging cell within two rings");
            kernel.TeleportTroop(siegeTroop, siegeDest!);

            for (int turn = 0; turn < turns; turn++)
            {
                kernel.AdvanceTurn();
                result.Digests.Add(kernel.WorldDigest());
                if (nativeRuntime != null)
                {
                    List<string> componentRows = kernel.NativeCityRows(nativeRuntime);
                    List<string> kernelRows = kernel.KernelCityRows();
                    Assert.That(componentRows.Count, Is.EqualTo(kernelRows.Count), $"turn {turn}: component/kernel city row count must match");
                    for (int row = 0; row < kernelRows.Count; row++)
                    {
                        Assert.That(componentRows[row], Is.EqualTo(kernelRows[row]),
                            $"turn {turn}: component-source city row must equal kernel-source row (index {row})");
                    }
                }
            }

            result.WhiteCityFell = PropertyValue(targetCity, "mBelongCorps") != null;
            return result;
        }

        static int[] PersonIdsOf(object city, int take) =>
            FreePersons(city).Take(take).Select(person => IntOf(person, "Id")).ToArray();

        static (string Type, int CityId, bool Succeeded) RunCommandProbe(
            Kernel kernel, Assembly sim, string type, List<object> cities, Func<object, int[]> personIdsOf, Func<object, int> targetOf)
        {
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

                (bool ok, _) = kernel.CityCommand(city, type, personIds, targetOf(city));
                if (ok)
                {
                    return (type, IntOf(city, "Id"), true);
                }
            }

            return (type, 0, false);
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
        public void NativeCity_Parity_DigestsBitIdentical_WithCityFall()
        {
            Assembly sim = LoadSangoSimMod();

            // 种子探测(内核路):找攻陷白城命中的种子。
            int chosen = 0;
            foreach (int seed in SeedLadder)
            {
                ScriptResult probe = RunScript(sim, seed, native: false);
                Console.Out.WriteLine($"[d1p-seed-probe] seed {seed}: whiteCityFell={probe.WhiteCityFell} commands={string.Join(",", probe.CommandResults.Select(c => $"{c.Type}={c.Succeeded}"))}");
                if (probe.WhiteCityFell)
                {
                    chosen = seed;
                    break;
                }
            }

            Assert.That(chosen, Is.Not.EqualTo(0), "no ladder seed reached a white-city fall; widen the ladder");

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

                for (int i = 0; i < runA.CommandResults.Count; i++)
                {
                    Assert.That(runB.CommandResults[i].Succeeded, Is.EqualTo(runA.CommandResults[i].Succeeded),
                        $"command {runA.CommandResults[i].Type}: gate/execution parity must hold");
                    Assert.That(runB.CommandResults[i].CityId, Is.EqualTo(runA.CommandResults[i].CityId),
                        $"command {runA.CommandResults[i].Type}: the same city must accept the order in both runs");
                }

                Assert.That(runB.WhiteCityFell, Is.True, "the native run must also reach the white-city fall");
                Console.Out.WriteLine($"[d1p-parity] seed {chosen}: {runA.Digests.Count} turns bit-identical; whiteCityFell={runA.WhiteCityFell}; " +
                                      $"commands=[{string.Join(", ", runA.CommandResults.Select(c => $"{c.Type}:{c.CityId}={c.Succeeded}"))}]");
            }
            finally
            {
                DisposeNative(runB);
            }
        }

        [Test]
        public void NativeCity_EconomyLedger_MatchesKernelFormulas()
        {
            Assembly sim = LoadSangoSimMod();
            // 剧本自带 1 次前置推进,turns: 8 → 总 9 推进 = 月边界回合(旬×3):
            // IncreaseDate 在全部势力行动之后,月始俸给输入 == 该回合末名单,账本可与
            // 内核公式终态比对(月边界后的官职流动会引入漂移)。
            ScriptResult run = RunScript(sim, 20260902, native: true, turns: 8);
            try
            {
                var kernel = new KernelPassiveAccessor(sim);
                List<object> probes = ReadProbes(run.NativeRuntime!);
                Assert.That(probes.Count, Is.EqualTo(88), "the native run must carry all 88 city entities");
                int salaryChecked = 0;
                int incomeBanded = 0;
                foreach (object probe in probes)
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
                        $"city {cityId}: native salary ledger must equal the kernel GoldCost formula");

                    int totalGainGold = Convert.ToInt32(FieldValue(economy, "TotalGainGold"));
                    Assert.That(totalGainGold, Is.EqualTo(IntOf(city, "totalGainGold")),
                        $"city {cityId}: native harvest totals must equal the write-through kernel transient");

                    int drawGold = Convert.ToInt32(FieldValue(economy, "LastIncomeDrawGold"));
                    int incomeBase = Convert.ToInt32(FieldValue(economy, "LastIncomeBaseGold"));
                    if (incomeBase > 0)
                    {
                        int low = (int)(incomeBase * 0.95);
                        Assert.That(drawGold, Is.InRange(low, Math.Max(low + 1, (int)(incomeBase * 1.05))),
                            $"city {cityId}: native monthly income draw must sit in the ±5% band of its draw-time base");
                        incomeBanded++;
                    }

                    salaryChecked++;
                }

                Assert.That(salaryChecked, Is.GreaterThan(0), "the script must reach at least one owned city");
                Console.Out.WriteLine($"[d1p-ledger] salary checked on {salaryChecked} cities; income band checked on {incomeBanded}");
            }
            finally
            {
                DisposeNative(run);
            }
        }

        [Test]
        public void NativeCity_WorldBin_PersistsCityEntitiesAndGasAttributes()
        {
            Assembly sim = LoadSangoSimMod();
            ScriptResult run = RunScript(sim, 20260902, native: true);
            try
            {
                FieldInfo worldField = run.NativeRuntime!.GetType().GetField("_world", BindingFlags.NonPublic | BindingFlags.Instance)!;
                World world = (World)worldField.GetValue(run.NativeRuntime)!;

                var serializer = new LudotsBinaryWorldSerializer();
                byte[] bytes = serializer.Serialize(world);
                Assert.That(bytes.Length, Is.GreaterThan(0), "world.bin payload must not be empty");
                using World restored = serializer.Deserialize(bytes);

                ComponentType identityType = sim.GetType("Sango.Runtime.SangoCityIdentity", throwOnError: true)!;
                var cityEntities = new List<Entity>();
                restored.Query(in QueryDescription.Null, entity =>
                {
                    if (restored.Has(entity, identityType))
                    {
                        cityEntities.Add(entity);
                    }
                });
                Assert.That(cityEntities.Count, Is.EqualTo(88), "world.bin must round-trip all 88 city entities");

                Type attributesType = sim.GetType("Sango.Runtime.SangoCityAttributes", throwOnError: true)!;
                int goldId = (int)attributesType.GetProperty("GoldId")!.GetValue(null)!;
                int foodId = (int)attributesType.GetProperty("FoodId")!.GetValue(null)!;

                var kernel = new KernelPassiveAccessor(sim);
                MethodInfo getCurrent = typeof(AttributeBuffer).GetMethod("GetCurrent")!;
                int compared = 0;
                foreach (Entity entity in cityEntities.Take(10))
                {
                    object identity = restored.Get(entity, identityType)!;
                    int cityId = (int)identity.GetType().GetField("CityId")!.GetValue(identity)!;
                    object? city = kernel.GetCity(cityId);
                    Assert.That(city, Is.Not.Null, $"restored city {cityId} must exist in the kernel world");

                    var buffer = restored.Get<AttributeBuffer>(entity);
                    Assert.That(buffer.HasAttribute(goldId), Is.True, $"restored city {cityId} must carry sango.city.gold");
                    Assert.That(buffer.HasAttribute(foodId), Is.True, $"restored city {cityId} must carry sango.city.food");
                    Assert.That((int)(float)getCurrent.Invoke(buffer, new object?[] { goldId })!, Is.EqualTo(IntOf(city!, "gold")),
                        $"restored city {cityId} gold attribute must equal the kernel value");
                    Assert.That((int)(float)getCurrent.Invoke(buffer, new object?[] { foodId })!, Is.EqualTo(IntOf(city!, "food")),
                        $"restored city {cityId} food attribute must equal the kernel value");
                    compared++;
                }

                Console.Out.WriteLine($"[d1p-worldbin] {bytes.Length} bytes; {cityEntities.Count} city entities; gold/food compared on {compared}");
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

        static List<object> ReadProbes(object nativeRuntime) =>
            ((IEnumerable)nativeRuntime.GetType()
                .GetMethod("Snapshot", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(nativeRuntime, null)!).Cast<object>().ToList();
    }
}
