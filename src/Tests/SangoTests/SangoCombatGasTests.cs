using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Arch.Core;
using Arch.Core.Utils;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using NUnit.Framework;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Presentation;

namespace Sango.Tests
{
    /// <summary>
    /// D-4' 战斗解算 GAS 化验收。对拍合同(同 D-1'~D-3'):同种子同命令流,内核解算跑一遍
    /// (SangoCombatNativeRuntime 不挂,SkillInstance.Action 经内核泵原样执行)与原生战斗跑
    /// 一遍(挂载城/武将/部队 + 战斗运行时,技能演出事件经转移泵改走 GAS 激活)全量 digest
    /// 逐位相等;战报(SangoCombatAnnals)两运行逐行相等(订阅面不变 + 演出等价)。
    /// 覆盖:野战会战(技能使用/反击/单挑链/溃灭俘获)、攻城两段、公式转写三方复算
    /// (内核静态 vs SangoCombatSteps vs 手算)、Skills.json→GAS 资产映射完备、world.bin
    /// 回环、战斗中存档续跑一致。
    /// </summary>
    [TestFixture]
    public sealed class SangoCombatGasTests
    {
        private const int Seed = 20260903;
        private const int WarTurns = 10;
        private const int SiegeTurns = 12;

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

        // ---- 原生运行时挂载(城/武将/部队 + 战斗;D-3' BuildServices 同款) ----

        private sealed class NativeStack : IDisposable
        {
            public World World = null!;
            public object CityRuntime = null!;
            public object PersonRuntime = null!;
            public object TroopRuntime = null!;
            public object CombatRuntime = null!;
            public string TempRoot = string.Empty;

            public void Dispose() => Release();

            internal void Release()
            {
                DisposeQuiet(CombatRuntime);
                DisposeQuiet(TroopRuntime);
                DisposeQuiet(PersonRuntime);
                DisposeQuiet(CityRuntime);
                try
                {
                    World.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }

                if (Directory.Exists(TempRoot))
                {
                    Directory.Delete(TempRoot, recursive: true);
                }
            }

            static void DisposeQuiet(object? runtime)
            {
                if (runtime == null)
                {
                    return;
                }

                try
                {
                    runtime.GetType().GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance)!.Invoke(runtime, null);
                }
                catch (TargetInvocationException ex) when (ex.InnerException is ObjectDisposedException)
                {
                }
            }
        }

        private static NativeStack AttachNative(Assembly sim)
        {
            string modTemplates = Path.Combine(RepoRoot(), "mods", "sango", "SangoSimMod", "assets", "Entities", "templates.json");
            string tempRoot = Path.Combine(Path.GetTempPath(), $"SangoCombatGas_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(tempRoot, "Entities"));
            File.Copy(modTemplates, Path.Combine(tempRoot, "Entities", "templates.json"));
            File.WriteAllText(
                Path.Combine(tempRoot, "config_catalog.json"),
                "[{ \"Path\": \"Entities/templates.json\", \"Policy\": \"ArrayById\", \"IdField\": \"id\" }]");

            var world = World.Create();
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", tempRoot);
            var pipeline = new ConfigPipeline(vfs, new ModLoader(vfs, new FunctionRegistry(), new TriggerManager()));
            var templates = new DataRegistry<EntityTemplate>(pipeline);
            templates.Load("Entities/templates.json", ConfigCatalogLoader.Load(pipeline));
            var tagOps = new Ludots.Core.Gameplay.GAS.TagOps(
                new Ludots.Core.Gameplay.GAS.DirtyEntityQueue(Ludots.Core.Gameplay.GAS.GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
                new Ludots.Core.Gameplay.GAS.TagRuleRegistry());
            var services = new Ludots.Core.Gameplay.Lifecycle.EntityLifecycleRuntimeServices(
                world, templates, new EntityTemplateKeyRegistry(),
                new PresentationStableIdAllocator(), tagOps);

            var stack = new NativeStack { World = world, TempRoot = tempRoot };
            try
            {
                stack.CityRuntime = sim.GetType("Sango.Runtime.SangoCityNativeRuntime", throwOnError: true)!
                    .GetMethod("AttachBare", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object[] { world, services, tagOps })!;
                stack.PersonRuntime = sim.GetType("Sango.Runtime.SangoPersonNativeRuntime", throwOnError: true)!
                    .GetMethod("AttachBare", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object[] { world, services, tagOps })!;
                stack.TroopRuntime = sim.GetType("Sango.Runtime.SangoTroopNativeRuntime", throwOnError: true)!
                    .GetMethod("AttachBare", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object[] { world, services, tagOps })!;
                stack.CombatRuntime = sim.GetType("Sango.Runtime.SangoCombatNativeRuntime", throwOnError: true)!
                    .GetMethod("AttachBare", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object[] { world, Path.Combine(RepoRoot(), "mods", "sango", "SangoSimMod") })!;
                return stack;
            }
            catch
            {
                stack.Dispose();
                throw;
            }
        }

        // ---- 内核门面(反射,公共成员;SangoCombatTests 同款) ----

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
                    annals.GetType().GetEvent("LinePublished")!.AddEventHandler(
                        annals, (Action<string>)(line => sink.Add(line)));
                }

                annals.GetType().GetMethod("Attach", BindingFlags.Public | BindingFlags.Instance)!.Invoke(annals, null);
                return annals;
            }

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

            public int TranscribedDamage(object attacker, object defender, int atkBounds)
            {
                MethodInfo method = Sim.GetType("Sango.Runtime.SangoCombatSteps", throwOnError: true)!
                    .GetMethod("CalculateDamageTroopVsTroop", BindingFlags.Public | BindingFlags.Static)!;
                return (int)method.Invoke(null, new[] { attacker, defender, atkBounds })!;
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
            return troop!;
        }

        // 野战夹具(SangoCombatTests 同款):攻方出城摆开阔地,守方贴脸。
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

        /// <summary>野战会话:首击 + 互授歼灭 + N 回合推进;产出逐回合 digest 与战报行。</summary>
        private sealed class WarOutcome
        {
            public List<string> Digests = new();
            public List<string> Annals = new();
        }

        static WarOutcome RunFieldWar(Assembly sim, int seed, bool native, int turns)
        {
            NativeStack? stack = null;
            try
            {
                Kernel kernel = new Kernel(sim, seed);
                if (native)
                {
                    // boot 后挂载:城/武将/部队/战斗运行时构造即全量物化(D-3' 同款)。
                    stack = AttachNative(sim);
                }
                FieldEncounter encounter = SeedFieldEncounter(kernel);
                var outcome = new WarOutcome();
                object annals = kernel.CreateAnnals(outcome.Annals);
                try
                {
                    (bool ok, string error, _, _) = kernel.MoveTroop(encounter.Attacker, encounter.DefenderCell);
                    Assert.That(ok, Is.True, $"strike dispatch rejected: {error}");
                    kernel.SetDestroyMission(encounter.Defender, IntOf(encounter.Attacker, "Id"));
                    outcome.Digests.Add(kernel.WorldDigest());
                    for (int i = 0; i < turns; i++)
                    {
                        kernel.AdvanceTurn();
                        outcome.Digests.Add(kernel.WorldDigest());
                    }
                }
                finally
                {
                    kernel.DisposeAnnals(annals);
                }

                return outcome;
            }
            finally
            {
                stack?.Dispose();
            }
        }

        // ---- 测试 ----

        [Test]
        public void FieldWar_DualSource_DigestAndAnnalsBitIdentical()
        {
            Assembly sim = LoadSangoSimMod();

            WarOutcome kernelRun = RunFieldWar(sim, Seed, native: false, WarTurns);
            WarOutcome nativeRun = RunFieldWar(sim, Seed, native: true, WarTurns);

            Assert.That(nativeRun.Digests.Count, Is.EqualTo(kernelRun.Digests.Count),
                "both runs must advance the same number of turns");
            for (int i = 0; i < kernelRun.Digests.Count; i++)
            {
                if (nativeRun.Digests[i] != kernelRun.Digests[i])
                {
                    Console.Out.WriteLine($"[d4p-war-diff] first digest divergence at turn {i}");
                    Console.Out.WriteLine("[d4p-war-diff] kernel annals head:");
                    foreach (string line in kernelRun.Annals.Take(10)) Console.Out.WriteLine($"  K| {line}");
                    Console.Out.WriteLine("[d4p-war-diff] native annals head:");
                    foreach (string line in nativeRun.Annals.Take(10)) Console.Out.WriteLine($"  N| {line}");
                }

                Assert.That(nativeRun.Digests[i], Is.EqualTo(kernelRun.Digests[i]),
                    $"turn {i}: native GAS combat digest must match the kernel oracle bit for bit");
            }

            Assert.That(nativeRun.Annals, Is.EqualTo(kernelRun.Annals),
                "combat annals must be line-for-line identical between the kernel and native paths");
            Assert.That(kernelRun.Annals.Any(line => line.StartsWith("[战斗]")), Is.True,
                "the war script must produce strike lines");
            Console.Out.WriteLine($"[d4p-war] turns={kernelRun.Digests.Count - 1} lines={kernelRun.Annals.Count} final={kernelRun.Digests[^1]}");

            // 换种子分岔(防假阳)。
            WarOutcome otherSeed = RunFieldWar(sim, 19940801, native: false, WarTurns);
            Assert.That(otherSeed.Digests[^1], Is.Not.EqualTo(kernelRun.Digests[^1]),
                "a different seed must diverge the war digest");
        }

        [Test]
        public void Siege_DualSource_DigestAndAnnalsBitIdentical()
        {
            Assembly sim = LoadSangoSimMod();

            WarOutcome RunSiege(bool native)
            {
                NativeStack? stack = null;
                try
                {
                    var kernel = new Kernel(sim, Seed);
                    if (native)
                    {
                        stack = AttachNative(sim);
                    }

                    kernel.AdvanceTurn();

                    var whiteCities = kernel.Cities().Cast<object>()
                        .Where(city => PropertyValue(city, "mBelongForce") == null)
                        .OrderBy(city => IntOf(city, "troops"))
                        .ToList();
                    Assert.That(whiteCities, Is.Not.Empty, "the scenario must carry unowned (white) cities");

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

                    var outcome = new WarOutcome();
                    object annals = kernel.CreateAnnals(outcome.Annals);
                    try
                    {
                        // 攻城堆栈:staging 城 2 队(远程兵种优先,原版围攻形态)。
                        var stacks = new List<object>();
                        while (stacks.Count < 2 && FreePersons(stagingCity).Count >= 1)
                        {
                            int[] squad = FreePersons(stagingCity)
                                .OrderByDescending(person => IntOf(person, "Command"))
                                .Take(3)
                                .Select(person => IntOf(person, "Id"))
                                .ToArray();
                            (bool ok, string _, _, object? troop) = kernel.CreateTroop(
                                stagingCity, squad, 100000, 500, Math.Max(1, IntOf(stagingCity, "food") / 3),
                                RangedLandTroopTypeId(kernel, stagingCity));
                            if (!ok)
                            {
                                break;
                            }

                            stacks.Add(troop!);
                        }

                        Assert.That(stacks, Is.Not.Empty, "at least one siege stack must be enlisted");
                        object targetCenter = PropertyValue(targetCity, "CenterCell")!;
                        foreach (object troop in stacks)
                        {
                            (bool ok, string error, string action, _) = kernel.MoveTroop(troop, targetCenter);
                            Assert.That(ok, Is.True, $"occupation dispatch rejected: {error}");
                            Assert.That(action, Is.EqualTo("occupation"));
                        }

                        outcome.Digests.Add(kernel.WorldDigest());
                        for (int i = 0; i < SiegeTurns; i++)
                        {
                            kernel.AdvanceTurn();
                            outcome.Digests.Add(kernel.WorldDigest());
                        }
                    }
                    finally
                    {
                        kernel.DisposeAnnals(annals);
                    }

                    return outcome;
                }
                finally
                {
                    stack?.Dispose();
                }
            }

            WarOutcome kernelRun = RunSiege(native: false);
            WarOutcome nativeRun = RunSiege(native: true);

            Assert.That(nativeRun.Digests.Count, Is.EqualTo(kernelRun.Digests.Count));
            for (int i = 0; i < kernelRun.Digests.Count; i++)
            {
                Assert.That(nativeRun.Digests[i], Is.EqualTo(kernelRun.Digests[i]),
                    $"siege turn {i}: native GAS siege digest must match the kernel oracle bit for bit");
            }

            Assert.That(nativeRun.Annals, Is.EqualTo(kernelRun.Annals),
                "siege annals must be line-for-line identical between the kernel and native paths");
            Assert.That(kernelRun.Annals.Any(line => line.StartsWith("[攻城]")), Is.True,
                "the siege script must reach the two-phase city damage resolution");
            Console.Out.WriteLine($"[d4p-siege] turns={SiegeTurns} lines={kernelRun.Annals.Count} fell={kernelRun.Annals.Any(l => l.StartsWith("[城陷]"))} final={kernelRun.Digests[^1]}");
        }

        static int? RangedLandTroopTypeId(Kernel kernel, object city)
        {
            Type troopTypeType = kernel.Sim.GetType("Sango.Core.TroopType", throwOnError: true)!;
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

        [Test]
        public void DamageFormula_Transcription_MatchesKernelStaticAndHandCalc()
        {
            Assembly sim = LoadSangoSimMod();
            var kernel = new Kernel(sim, Seed);
            FieldEncounter encounter = SeedFieldEncounter(kernel);

            object attacker = encounter.Attacker;
            object defender = encounter.Defender;
            int attackerTroops = IntOf(attacker, "troops");
            int attackerAttack = IntOf(attacker, "Attack");
            int defenderTroops = IntOf(defender, "troops");
            int defenderDefence = IntOf(defender, "Defence");

            int checkedCount = 0;
            foreach (string listName in new[] { "landSkills", "waterSkills", "StrategySkills" })
            {
                foreach (object skill in (IEnumerable)FieldValue(attacker, listName))
                {
                    if (skill == null)
                    {
                        continue;
                    }

                    string name = PropertyValue(skill, "Name")!.ToString()!;
                    int atkBounds = IntOf(skill, "atk");
                    int kernelStatic = kernel.CalculateSkillDamage(attacker, defender, skill);
                    int transcribed = kernel.TranscribedDamage(attacker, defender, atkBounds);
                    int hand = HandCalcSkillDamage(kernel, attacker, defender, atkBounds,
                        attackerTroops, attackerAttack, defenderTroops, defenderDefence);
                    Assert.That(transcribed, Is.EqualTo(kernelStatic),
                        $"SangoCombatSteps formula must match the kernel static for skill '{name}'");
                    Assert.That(hand, Is.EqualTo(kernelStatic),
                        $"hand-calc must match the kernel static for skill '{name}'");
                    checkedCount++;
                }
            }

            Assert.That(checkedCount, Is.GreaterThan(0), "the attacker must carry skills for formula sampling");
            Console.Out.WriteLine($"[d4p-formula] {checkedCount} skill formulas three-way verified");
        }

        static int HandCalcSkillDamage(
            Kernel kernel, object attacker, object defender, int atkBounds,
            int attackerTroops, int attackerAttack, int defenderTroops, int defenderDefence)
        {
            object variables = PropertyValue(kernel.Scenario, "Variables")!;
            float fightBaseDamage = FloatOf(variables, "fight_base_damage");
            float fightBaseTroopsNeed = FloatOf(variables, "fight_base_troops_need");
            double fightMagic = Convert.ToDouble(FloatOf(variables, "fight_damage_magic_number"));
            float fightBaseTroopCount = FloatOf(variables, "fight_base_troop_count");

            MethodInfo restrainMethod = kernel.Sim.GetType("Sango.Core.Troop", throwOnError: true)!
                .GetMethod("CalculateRestrainBoost", BindingFlags.Public | BindingFlags.Static)!;
            float restrain = (float)restrainMethod.Invoke(null, new[] { attacker, defender })!;
            float extra = FloatOf(attacker, "DamageTroopExtraFactor");

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

        [Test]
        public void SkillAssets_MatchSkillsTableExactly()
        {
            string skillsPath = Path.Combine(RepoRoot(), "mods", "sango", "SangoContentMod", "assets", "Data", "Common", "Skills.json");
            string effectsPath = Path.Combine(RepoRoot(), "mods", "sango", "SangoSimMod", "assets", "GAS", "effects.json");
            string abilitiesPath = Path.Combine(RepoRoot(), "mods", "sango", "SangoSimMod", "assets", "GAS", "abilities.json");

            using JsonDocument skillsDoc = JsonDocument.Parse(File.ReadAllText(skillsPath));
            using JsonDocument effectsDoc = JsonDocument.Parse(File.ReadAllText(effectsPath));
            using JsonDocument abilitiesDoc = JsonDocument.Parse(File.ReadAllText(abilitiesPath));

            var skills = skillsDoc.RootElement.GetProperty("Skills");
            var effects = effectsDoc.RootElement.EnumerateArray().ToList();
            var abilities = abilitiesDoc.RootElement.EnumerateArray().ToList();

            Assert.That(effects.Count(ev => ev.GetProperty("id").GetString()!.StartsWith("Effect.Sango.Skill.")),
                Is.EqualTo(skills.EnumerateObject().Count()), "one effect template per Skills.json row");
            Assert.That(abilities.Count(ab => ab.GetProperty("id").GetString()!.StartsWith("Ability.Sango.Skill.")),
                Is.EqualTo(skills.EnumerateObject().Count()), "one ability per Skills.json row");

            var effectsById = effects.ToDictionary(ev => ev.GetProperty("id").GetString()!);
            foreach (JsonProperty skillRow in skills.EnumerateObject())
            {
                int skillId = skillRow.Value.GetProperty("Id").GetInt32();
                JsonElement effect = effectsById[$"Effect.Sango.Skill.{skillId}"];
                Assert.That(effect.GetProperty("presetType").GetString(), Is.EqualTo("SangoSkillAction"),
                    $"skill {skillId} template must reference the SangoSkillAction preset");
                Assert.That(effect.GetProperty("lifetime").GetString(), Is.EqualTo("Instant"));

                var parameters = effect.GetProperty("configParams");
                int GetInt(string key) => parameters.GetProperty(key).GetProperty("value").GetInt32();
                Assert.That(GetInt("_ep.sangoSkillId"), Is.EqualTo(skillId));
                Assert.That(GetInt("_ep.atk"), Is.EqualTo(skillRow.Value.TryGetProperty("atk", out var atk) ? atk.GetInt32() : 0));
                Assert.That(GetInt("_ep.atkDurability"), Is.EqualTo(skillRow.Value.TryGetProperty("atkDurability", out var dur) ? dur.GetInt32() : 0));
                Assert.That(GetInt("_ep.costEnergy"), Is.EqualTo(skillRow.Value.GetProperty("costEnergy").GetInt32()));
                Assert.That(GetInt("_ep.canDamageTroop"), Is.EqualTo(Bool(skillRow.Value, "canDamageTroop")));
                Assert.That(GetInt("_ep.canDamageBuilding"), Is.EqualTo(Bool(skillRow.Value, "canDamageBuilding")));
                Assert.That(GetInt("_ep.canDamageTeam"), Is.EqualTo(Bool(skillRow.Value, "canDamageTeam")));
                Assert.That(GetInt("_ep.canSpellToCell"), Is.EqualTo(Bool(skillRow.Value, "canSpellToCell")));
                Assert.That(GetInt("_ep.blockFactor"), Is.EqualTo(skillRow.Value.TryGetProperty("blockFactor", out var bf) ? bf.GetInt32() : 0));

                AssertArray(parameters, skillRow.Value, "_ep.atkOffset", "atkOffsetPoint");
                AssertArray(parameters, skillRow.Value, "_ep.offset", "offsetAction");
            }

            Console.Out.WriteLine($"[d4p-assets] {skills.EnumerateObject().Count()} skills mapped to GAS effect templates + abilities");

            static int Bool(JsonElement row, string name) =>
                row.TryGetProperty(name, out var value) && value.GetBoolean() ? 1 : 0;

            static void AssertArray(JsonElement parameters, JsonElement row, string prefix, string fieldName)
            {
                int count = parameters.GetProperty($"{prefix}Count").GetProperty("value").GetInt32();
                int[] expected = row.TryGetProperty(fieldName, out var array) && array.ValueKind == JsonValueKind.Array
                    ? array.EnumerateArray().Select(v => v.GetInt32()).ToArray()
                    : Array.Empty<int>();
                Assert.That(count, Is.EqualTo(expected.Length), $"{fieldName} length must match");
                for (int i = 0; i < expected.Length; i++)
                {
                    Assert.That(parameters.GetProperty($"{prefix}.{i}").GetProperty("value").GetInt32(),
                        Is.EqualTo(expected[i]), $"{fieldName}[{i}] must match");
                }
            }
        }

        [Test]
        public void NativeCombat_MidBattleSaveRestore_MatchesUnsavedChain()
        {
            Assembly sim = LoadSangoSimMod();

            string Chain(bool withSave)
            {
                var kernel = new Kernel(sim, Seed);
                using NativeStack stack = AttachNative(sim);
                FieldEncounter encounter = SeedFieldEncounter(kernel);
                (bool ok, string error, _, _) = kernel.MoveTroop(encounter.Attacker, encounter.DefenderCell);
                Assert.That(ok, Is.True, $"strike dispatch rejected: {error}");
                kernel.SetDestroyMission(encounter.Defender, IntOf(encounter.Attacker, "Id"));

                if (withSave)
                {
                    object capture = kernel.Capture();
                    kernel.Restore(capture);
                }

                for (int i = 0; i < 5; i++)
                {
                    kernel.AdvanceTurn();
                }

                return kernel.WorldDigest();
            }

            string across = Chain(withSave: true);
            string neverSaved = Chain(withSave: false);
            Assert.That(across, Is.EqualTo(neverSaved),
                "mid-battle save→restore→continue must replay the unsaved chain bit for bit under native GAS combat");
        }

        [Test]
        public void NativeCombat_WorldBin_RoundTripsTroopsAndAttributes()
        {
            Assembly sim = LoadSangoSimMod();
            var kernel = new Kernel(sim, Seed);
            using NativeStack stack = AttachNative(sim);
            FieldEncounter encounter = SeedFieldEncounter(kernel);
            (bool ok, string error, _, _) = kernel.MoveTroop(encounter.Attacker, encounter.DefenderCell);
            Assert.That(ok, Is.True, $"strike dispatch rejected: {error}");
            kernel.SetDestroyMission(encounter.Defender, IntOf(encounter.Attacker, "Id"));
            for (int i = 0; i < 3; i++)
            {
                kernel.AdvanceTurn();
            }

            var serializer = new LudotsBinaryWorldSerializer();
            byte[] bytes = serializer.Serialize(stack.World);
            Assert.That(bytes.Length, Is.GreaterThan(0), "world.bin payload must not be empty");
            using World restored = serializer.Deserialize(bytes);

            ComponentType troopIdentityType = sim.GetType("Sango.Runtime.SangoTrooperIdentity", throwOnError: true)!;
            var troopEntities = new List<Entity>();
            restored.Query(in QueryDescription.Null, entity =>
            {
                if (restored.Has(entity, troopIdentityType))
                {
                    troopEntities.Add(entity);
                }
            });

            List<object> kernelTroops = EnumerateSet(FieldValue(kernel.Scenario, "troopsSet"))
                .Cast<object>()
                .Where(troop => troop != null && BoolOf(troop, "IsAlive"))
                .ToList();
            Assert.That(troopEntities.Count, Is.EqualTo(kernelTroops.Count),
                "world.bin must round-trip every alive troop entity after native combat");

            Type attributesType = sim.GetType("Sango.Runtime.SangoTroopAttributes", throwOnError: true)!;
            int troopsAttrId = (int)attributesType.GetProperty("TroopsId")!.GetValue(null)!;
            Type identityStruct = sim.GetType("Sango.Runtime.SangoTrooperIdentity", throwOnError: true)!;
            FieldInfo identityField = identityStruct.GetField("TroopId")!;
            int attrChecked = 0;
            foreach (Entity entity in troopEntities)
            {
                int troopId = (int)identityField.GetValue(restored.Get(entity, troopIdentityType))!;
                object? kernelTroop = Call(FieldValue(kernel.Scenario, "troopsSet"), "Get", troopId);
                if (kernelTroop == null || !BoolOf(kernelTroop, "IsAlive"))
                {
                    continue;
                }

                var buffer = restored.Get<AttributeBuffer>(entity);
                Assert.That((int)buffer.GetCurrent(troopsAttrId), Is.EqualTo(IntOf(kernelTroop, "troops")),
                    $"restored troop {troopId} troops attribute must equal the kernel value after native combat");
                attrChecked++;
            }

            Assert.That(attrChecked, Is.GreaterThan(0), "the war must leave alive troops with attributes to verify");
            Console.Out.WriteLine($"[d4p-worldbin] {troopEntities.Count} troop entities round-tripped; {attrChecked} troop attributes verified");
        }
    }
}
