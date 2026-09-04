using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Sango.Tests
{
    /// <summary>
    /// M3 末读模型桥验收(SangoEntityMirror,反射调用 SangoRuntime,不引用 SangoSimMod
    /// 工程——同 SangoCombatTests 惯例)。覆盖:
    ///   1. 启动镜像 == 内核真相:逐类计数 + 字段全等(城全量,武将抽样,部队全量;
    ///      势力/位置换算同轴断言,城数 88 按 SangoRealMapTests 口径);
    ///   2. 播种战斗推 10 回合(白城攻陷 + 俘将)后镜像 == 内核,且逐回合部队计数一致
    ///      (事件驱动生灭正确性);战斗种子从固定梯子探测选取(城陷 + 俘将双命中);
    ///   3. digest 零影响:同种子同剧本双跑(带镜像/不带镜像)WorldDigest 逐位相等
    ///      ——读模型纯净性,后续 ECS 溶解的合同基线;
    ///   4. 内核世界替换(selectPlayerForce/读档路径)→ Reconcile 对账全量重建。
    /// 镜像物化走 Layer 0 原子 op(MaterializeTemplate + SangoSimMod assets 模板),测试
    /// 侧从 mod 真实资产拷贝模板进临时 Core 挂载(运行时复制,SSOT 仍是 mod 资产)。
    /// </summary>
    [TestFixture]
    public sealed class SangoEntityMirrorTests
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

        /// <summary>
        /// 临时 Core 挂载(目录 + 拷贝 mod 真实模板资产 + 最小 config_catalog),产出与引擎
        /// 同链的模板注册表;镜像物化服务的其余组件与 GasTests 同款直构。
        /// </summary>
        private static (EntityLifecycleRuntimeServices Services, string TempRoot) BuildLifecycleServices(World world)
        {
            string modTemplates = Path.Combine(RepoRoot(), "mods", "sango", "SangoSimMod", "assets", "Entities", "templates.json");
            Assert.That(File.Exists(modTemplates), Is.True, "SangoSimMod must ship assets/Entities/templates.json (mirror templates)");

            string tempRoot = Path.Combine(Path.GetTempPath(), $"SangoMirrorTpl_{Guid.NewGuid():N}");
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

            var services = new EntityLifecycleRuntimeServices(
                world,
                templates,
                new EntityTemplateKeyRegistry(),
                new PresentationStableIdAllocator(),
                new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));
            return (services, tempRoot);
        }

        private static object AttachMirror(Assembly sim, World world, out string tempRoot)
        {
            (EntityLifecycleRuntimeServices services, tempRoot) = BuildLifecycleServices(world);
            return Activator.CreateInstance(
                sim.GetType("Sango.Runtime.SangoEntityMirrorRuntime", throwOnError: true)!,
                world, services)!;
        }

        // ---- 反射小门面:本套测试触碰的内核/镜像面,全部为既有公共成员。 ----

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

            public IEnumerable Troops() => EnumerateSet(FieldValue(Scenario, "troopsSet"));

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

            public (bool Succeeded, string ErrorCode, string Message) MoveTroop(object troop, object destCell)
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
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(m => m.Name == "CreateTroop" && m.GetParameters().Length == 8)!
                    .Invoke(null, new object?[] { Scenario, city, personIds, null, null, troops, gold, food })!;
                object? payload = tuple.GetType().GetFields().FirstOrDefault(field => field.Name == "Item2")?.GetValue(tuple);
                (bool ok, string error, string message) = ReadTuple(tuple);
                return (ok, error, message, payload);
            }

            public void SetDestroyMission(object troop, int targetTroopId)
            {
                Type missionType = Sim.GetType("Sango.Core.MissionType", throwOnError: true)!;
                Call(troop, "SetMission", Enum.Parse(missionType, "TroopDestroyTroop"), targetTroopId);
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

            static (bool, string, string) ReadTuple(object tuple)
            {
                Type t = tuple.GetType();
                object result = t.GetField("Item1")!.GetValue(tuple)!;
                return (
                    (bool)result.GetType().GetProperty("Succeeded")!.GetValue(result)!,
                    (string)result.GetType().GetProperty("ErrorCode")!.GetValue(result)!,
                    (string)result.GetType().GetProperty("Message")!.GetValue(result)!);
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

        static int CountOf(object listObject) => Convert.ToInt32(PropertyValue(listObject, "Count"));

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

        static object SeedTroopAtCity(Kernel kernel, object city, int troopsWanted)
        {
            var personIds = FreePersons(city)
                .OrderByDescending(person => IntOf(person, "Command"))
                .Take(3)
                .Select(person => IntOf(person, "Id"))
                .ToArray();
            (bool ok, string error, _, object? troop) = kernel.CreateTroop(city, personIds, troopsWanted, 500, Math.Min(20000, IntOf(city, "food") / 2));
            Assert.That(ok, Is.True, $"seed troop failed: {error}");
            Assert.That(troop, Is.Not.Null, "CreateTroop must return the troop on success");
            return troop!;
        }

        // ---- 镜像探针读取 ----

        private sealed class MirrorHandle
        {
            public readonly object Runtime;

            public MirrorHandle(object runtime)
            {
                Runtime = runtime;
            }

            public List<object> Snapshot(string kind)
            {
                Type kindType = Runtime.GetType().Assembly.GetType("Sango.Runtime.SangoMirrorKind", throwOnError: true)!;
                object list = Runtime.GetType()
                    .GetMethod("Snapshot", BindingFlags.Public | BindingFlags.Instance)!
                    .Invoke(Runtime, new object[] { Enum.Parse(kindType, kind) })!;
                return ((IEnumerable)list).Cast<object>().ToList();
            }

            public int Count(string property)
            {
                return (int)Runtime.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.GetValue(Runtime)!;
            }

            public void Reconcile() => Runtime.GetType()
                .GetMethod("Reconcile", BindingFlags.Public | BindingFlags.Instance)!.Invoke(Runtime, null);

            public void Dispose() => Runtime.GetType()
                .GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance)!.Invoke(Runtime, null);

            public static int KernelIdOf(object probe) => (int)probe.GetType().GetProperty("KernelId")!.GetValue(probe)!;

            public static string NameOf(object probe) => (string)probe.GetType().GetProperty("Name")!.GetValue(probe)!;

            public static System.Numerics.Vector2 PositionOf(object probe) =>
                (System.Numerics.Vector2)probe.GetType().GetProperty("PositionCm")!.GetValue(probe)!;

            public static int ForceIdOf(object probe)
            {
                object force = probe.GetType().GetProperty("Force")!.GetValue(probe)!;
                return (int)force.GetType().GetField("ForceId")!.GetValue(force)!;
            }
        }

        static int StatsOf(object probe, string field)
        {
            object stats = probe.GetType().GetProperty("Stats")!.GetValue(probe)!;
            return Convert.ToInt32(stats.GetType().GetField(field)!.GetValue(stats));
        }

        // ---- 内核真源期望值 ----

        static (float X, float Y) CellToCm(Kernel kernel, int cellX, int cellY)
        {
            float cellMeters = Convert.ToSingle(PropertyValue(kernel.Map, "GridSize"));
            int width = Convert.ToInt32(PropertyValue(kernel.Map, "Width"));
            float halfWorldCm = width * cellMeters * 100f / 2f;
            return (
                (cellY * cellMeters + cellMeters * 0.5f) * 100f - halfWorldCm,
                (cellX * cellMeters + cellMeters * 0.5f) * 100f - halfWorldCm);
        }

        static (float X, float Y) PersonCellCm(Kernel kernel, object person)
        {
            object? troop = PropertyValue(person, "mTroop");
            if (troop != null && FieldValue(troop, "cell") != null)
            {
                object cell = FieldValue(troop, "cell");
                return CellToCm(kernel, IntOf(cell, "x"), IntOf(cell, "y"));
            }

            object? city = PropertyValue(person, "mCurrentCity") ?? PropertyValue(person, "mBelongCity");
            if (city != null)
            {
                return CellToCm(kernel, IntOf(city, "x"), IntOf(city, "y"));
            }

            return (0f, 0f);
        }

        static void AssertMirrorMatchesKernel(Kernel kernel, MirrorHandle mirror)
        {
            var cities = kernel.Cities().Cast<object>().ToList();
            var persons = kernel.Persons().Cast<object>().ToList();
            var troops = kernel.Troops().Cast<object>().Where(troop => BoolOf(troop, "IsAlive")).ToList();

            Assert.That(mirror.Count("CityCount"), Is.EqualTo(cities.Count), "mirrored city count must equal kernel non-null cities");
            Assert.That(mirror.Count("PersonCount"), Is.EqualTo(persons.Count), "mirrored person count must equal kernel non-null persons");
            Assert.That(mirror.Count("TroopCount"), Is.EqualTo(troops.Count), "mirrored troop count must equal kernel alive troops");

            Dictionary<int, object> cityProbeById = mirror.Snapshot("City").ToDictionary(MirrorHandle.KernelIdOf);
            foreach (object city in cities)
            {
                int id = IntOf(city, "Id");
                Assert.That(cityProbeById.ContainsKey(id), Is.True, $"city {id} must be mirrored");
                object probe = cityProbeById[id];
                object? force = PropertyValue(city, "mBelongForce");
                Assert.That(MirrorHandle.ForceIdOf(probe), Is.EqualTo(force == null ? 0 : IntOf(force, "Id")),
                    $"city {id} force id mismatch");
                Assert.That(StatsOf(probe, "Gold"), Is.EqualTo(IntOf(city, "gold")), $"city {id} gold mismatch");
                Assert.That(StatsOf(probe, "Food"), Is.EqualTo(IntOf(city, "food")), $"city {id} food mismatch");
                Assert.That(StatsOf(probe, "Population"), Is.EqualTo(IntOf(city, "population")), $"city {id} population mismatch");
                Assert.That(StatsOf(probe, "Durability"), Is.EqualTo(IntOf(city, "durability")), $"city {id} durability mismatch");
                Assert.That(StatsOf(probe, "PersonCount"), Is.EqualTo(CountOf(FieldValue(city, "allPersons"))),
                    $"city {id} person count mismatch");

                (float expectedX, float expectedY) = CellToCm(kernel, IntOf(city, "x"), IntOf(city, "y"));
                var position = MirrorHandle.PositionOf(probe);
                Assert.That(position.X, Is.EqualTo(expectedX).Within(0.01f), $"city {id} position X mismatch");
                Assert.That(position.Y, Is.EqualTo(expectedY).Within(0.01f), $"city {id} position Y mismatch");
            }

            Dictionary<int, object> personProbeById = mirror.Snapshot("Person").ToDictionary(MirrorHandle.KernelIdOf);
            for (int index = 0; index < persons.Count; index += 7)
            {
                object person = persons[index];
                int id = IntOf(person, "Id");
                Assert.That(personProbeById.ContainsKey(id), Is.True, $"person {id} must be mirrored");
                object probe = personProbeById[id];
                object? force = PropertyValue(person, "mBelongForce");
                Assert.That(MirrorHandle.ForceIdOf(probe), Is.EqualTo(force == null ? 0 : IntOf(force, "Id")),
                    $"person {id} force id mismatch");
                Assert.That(StatsOf(probe, "Loyalty"), Is.EqualTo(IntOf(person, "loyalty")), $"person {id} loyalty mismatch");
                Assert.That(StatsOf(probe, "Command"), Is.EqualTo(IntOf(person, "Command")), $"person {id} command mismatch");
                Assert.That(StatsOf(probe, "Strength"), Is.EqualTo(IntOf(person, "Strength")), $"person {id} strength mismatch");
                Assert.That(StatsOf(probe, "Intelligence"), Is.EqualTo(IntOf(person, "Intelligence")), $"person {id} intelligence mismatch");
                Assert.That(StatsOf(probe, "Politics"), Is.EqualTo(IntOf(person, "Politics")), $"person {id} politics mismatch");
                Assert.That(StatsOf(probe, "State"), Is.EqualTo(IntOf(person, "state")), $"person {id} state mismatch");

                (float expectedX, float expectedY) = PersonCellCm(kernel, person);
                var position = MirrorHandle.PositionOf(probe);
                Assert.That(position.X, Is.EqualTo(expectedX).Within(0.01f), $"person {id} position X mismatch");
                Assert.That(position.Y, Is.EqualTo(expectedY).Within(0.01f), $"person {id} position Y mismatch");
            }

            Dictionary<int, object> troopProbeById = mirror.Snapshot("Troop").ToDictionary(MirrorHandle.KernelIdOf);
            foreach (object troop in troops)
            {
                int id = IntOf(troop, "Id");
                Assert.That(troopProbeById.ContainsKey(id), Is.True, $"troop {id} must be mirrored");
                object probe = troopProbeById[id];
                object? force = PropertyValue(troop, "mBelongForce");
                Assert.That(MirrorHandle.ForceIdOf(probe), Is.EqualTo(force == null ? 0 : IntOf(force, "Id")),
                    $"troop {id} force id mismatch");
                Assert.That(StatsOf(probe, "Troops"), Is.EqualTo(IntOf(troop, "troops")), $"troop {id} troops mismatch");
                Assert.That(StatsOf(probe, "Morale"), Is.EqualTo(IntOf(troop, "morale")), $"troop {id} morale mismatch");
                Assert.That(StatsOf(probe, "MissionTypeId"), Is.EqualTo(IntOf(troop, "missionType")), $"troop {id} mission mismatch");
                Assert.That(MirrorHandle.NameOf(probe), Does.Contain($"[sango.troop {id}]"),
                    $"troop {id} mirror name must carry the queryable sango type tag");

                (float expectedX, float expectedY) = CellToCm(kernel, IntOf(troop, "x"), IntOf(troop, "y"));
                var position = MirrorHandle.PositionOf(probe);
                Assert.That(position.X, Is.EqualTo(expectedX).Within(0.01f), $"troop {id} position X mismatch");
                Assert.That(position.Y, Is.EqualTo(expectedY).Within(0.01f), $"troop {id} position Y mismatch");
            }
        }

        /// <summary>
        /// 播种战斗剧本(双跑共用,镜像注入是唯一差异):野战互歼对阵(100% 单挑覆写,
        /// 覆盖俘将链)+ 白城攻城(传送贴城)。返回 (城陷, 俘将数, 逐回合 digest, 攻陷城)。
        /// 带镜像时逐回合断言镜像部队计数 == 内核存活数(事件驱动生灭正确性)。
        /// </summary>
        private static (bool Fell, int Prisoners, List<string> Digests, object TargetCity) RunBattleScript(
            Assembly sim, Kernel kernel, MirrorHandle? mirror)
        {
            kernel.AdvanceTurn();

            // 野战对阵:近邻异势力两城各编一队,互授歼灭任务。
            object home = kernel.Cities().Cast<object>().First(city => PassExpeditionGate(sim, city));
            object attacker = SeedTroopAtCity(kernel, home, 3000);
            object attackerForce = PropertyValue(attacker, "mBelongForce")!;
            kernel.FillMoveRange(attacker);
            object? openCell = kernel.MoveRangeOf(attacker).Cast<object?>()
                .FirstOrDefault(cell => cell != null && PropertyValue(cell, "troop") == null && PropertyValue(cell, "building") == null);
            Assert.That(openCell, Is.Not.Null, "attacker move range must expose an open cell");
            (bool moved, string moveError, _) = kernel.MoveTroop(attacker, openCell!);
            Assert.That(moved, Is.True, $"attacker cannot leave the city block: {moveError}");
            kernel.ResetActionOver(attacker);

            object homeCenter = PropertyValue(home, "CenterCell")!;
            object foe = kernel.Cities().Cast<object>()
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
                object neighbor = kernel.GetNeighbor(attackerCell, dir);
                if (neighbor != null && PropertyValue(neighbor, "troop") == null && PropertyValue(neighbor, "building") == null)
                {
                    dest = neighbor;
                }
            }

            Assert.That(dest, Is.Not.Null, "the attacker's open-field cell must expose an empty neighbor");
            kernel.TeleportTroop(defender, dest!);
            kernel.SetDestroyMission(attacker, IntOf(defender, "Id"));
            kernel.SetDestroyMission(defender, IntOf(attacker, "Id"));

            // 白城攻城:守军最少的无主城 + 最近过门槛城编成攻城队,传送贴城。
            object targetCity = kernel.Cities().Cast<object>()
                .Where(city => PropertyValue(city, "mBelongForce") == null)
                .OrderBy(city => IntOf(city, "troops"))
                .First();
            object staging = kernel.Cities().Cast<object>()
                .Where(city => PassExpeditionGate(sim, city) && !ReferenceEquals(city, home) && !ReferenceEquals(city, foe))
                .OrderBy(city => kernel.Distance(PropertyValue(city, "CenterCell")!, PropertyValue(targetCity, "CenterCell")!))
                .First();
            object siegeTroop = SeedTroopAtCity(kernel, staging, 3000);
            object targetCenter = PropertyValue(targetCity, "CenterCell")!;
            object? siegeDest = FindOpenCellNear(kernel, targetCenter);
            Assert.That(siegeDest, Is.Not.Null, "the white city must expose an open staging cell within two rings");
            kernel.TeleportTroop(siegeTroop, siegeDest!);

            kernel.AttachChallenges("Duel", 100);
            var digests = new List<string>();
            try
            {
                (bool ok, string error, _) = kernel.MoveTroop(attacker, dest!);
                Assert.That(ok, Is.True, $"field strike dispatch rejected: {error}");

                for (int turn = 0; turn < TurnsToAdvance; turn++)
                {
                    kernel.AdvanceTurn();
                    digests.Add(kernel.WorldDigest());
                    if (mirror != null)
                    {
                        int alive = kernel.Troops().Cast<object>().Count(troop => BoolOf(troop, "IsAlive"));
                        Assert.That(mirror.Count("TroopCount"), Is.EqualTo(alive),
                            $"turn {turn}: mirror troop count must track kernel alive troops (event-driven spawn/despawn)");
                    }
                }
            }
            finally
            {
                kernel.DetachChallenges();
            }

            bool fell = PropertyValue(targetCity, "mBelongCorps") != null;
            int prisoners = CountCaptives(kernel);
            return (fell, prisoners, digests, targetCity);
        }

        /// <summary>俘将真源计数:势力 BeCaptiveList(单挑/部队/城陷俘获的共同落账)。</summary>
        static int CountCaptives(Kernel kernel)
        {
            int count = 0;
            foreach (object force in EnumerateSet(FieldValue(kernel.Scenario, "forceSet")))
            {
                count += Convert.ToInt32(PropertyValue(FieldValue(force, "BeCaptiveList"), "Count"));
            }

            return count;
        }

        private static (bool Fell, int Prisoners, List<string> Digests, object TargetCity) RunBattleScriptFresh(
            Assembly sim, int seed, MirrorHandle? mirror)
        {
            var kernel = new Kernel(sim, seed);
            if (mirror == null)
            {
                return RunBattleScript(sim, kernel, null);
            }

            // 镜像跑:内核启动后立即注入,再进剧本(镜像订阅覆盖全部回合推进)。
            using var world = World.Create();
            object mirrorRuntime = AttachMirror(sim, world, out string tempRoot);
            var handle = new MirrorHandle(mirrorRuntime);
            try
            {
                return RunBattleScript(sim, kernel, handle);
            }
            finally
            {
                handle.Dispose();
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        // ---- 测试 ----

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

        [Test]
        public void Mirror_Boot_MatchesKernelTruth()
        {
            Assembly sim = LoadSangoSimMod();
            var kernel = new Kernel(sim, 20260902);
            using var world = World.Create();
            object mirrorRuntime = AttachMirror(sim, world, out string tempRoot);
            var mirror = new MirrorHandle(mirrorRuntime);
            try
            {
                AssertMirrorMatchesKernel(kernel, mirror);
                var cityProbes = mirror.Snapshot("City");
                Assert.That(cityProbes.Count, Is.EqualTo(88), "the scenario must mirror its 88 non-null cities (SangoRealMapTests 口径)");
                foreach (object probe in cityProbes)
                {
                    Assert.That(MirrorHandle.NameOf(probe), Does.Contain("[sango.city "),
                        "city mirror names must carry the queryable sango type tag for entities.query forensics");
                }

                Assert.That(mirror.Snapshot("Person").Count, Is.GreaterThan(800), "the scenario must mirror its full person roster");
                Assert.That(mirror.Count("TroopCount"), Is.EqualTo(0), "a fresh boot carries no alive troops");
                Console.Out.WriteLine($"[m3h-boot] cities={mirror.Count("CityCount")} persons={mirror.Count("PersonCount")} troops={mirror.Count("TroopCount")}");
            }
            finally
            {
                mirror.Dispose();
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Test]
        public void Mirror_TenTurnBattle_MatchesKernel_AndDigestIgnoresMirror()
        {
            Assembly sim = LoadSangoSimMod();

            // 种子探测:找城陷 + 俘将双命中的种子(镜像缺席,纯剧本确定性)。
            int chosen = 0;
            foreach (int seed in SeedLadder)
            {
                (bool fell, int prisoners, _, _) = RunBattleScriptFresh(sim, seed, null);
                Console.Out.WriteLine($"[m3h-seed-probe] seed {seed}: cityFall={fell} prisoners={prisoners}");
                if (fell && prisoners > 0)
                {
                    chosen = seed;
                    break;
                }
            }

            Assert.That(chosen, Is.Not.EqualTo(0), "no ladder seed reached city fall with captures; widen the ladder");

            // 跑 A:带镜像(内核启动后立即注入)——镜像 == 内核 + 城陷/俘将命中。
            var kernelA = new Kernel(sim, chosen);
            using (var world = World.Create())
            {
                object mirrorRuntime = AttachMirror(sim, world, out string tempRoot);
                var mirror = new MirrorHandle(mirrorRuntime);
                try
                {
                    (bool fell, int prisoners, List<string> digestsA, object targetCity) = RunBattleScript(sim, kernelA, mirror);
                    AssertMirrorMatchesKernel(kernelA, mirror);
                    Assert.That(fell, Is.True, "the seeded white city must fall within the turn budget");
                    Assert.That(prisoners, Is.GreaterThan(0), "the seeded duels must produce captured persons");
                    Console.Out.WriteLine($"[m3h-mirror-run] seed {chosen}: cityFall={fell} prisoners={prisoners}; " +
                                          $"mirror cities={mirror.Count("CityCount")} persons={mirror.Count("PersonCount")} troops={mirror.Count("TroopCount")}");

                    // 城陷镜像语义:归属变更改 ForceRef,城实体不消失。
                    int targetId = IntOf(targetCity, "Id");
                    object probe = mirror.Snapshot("City").Single(candidate => MirrorHandle.KernelIdOf(candidate) == targetId);
                    object? newForce = PropertyValue(targetCity, "mBelongForce");
                    Assert.That(MirrorHandle.ForceIdOf(probe), Is.EqualTo(newForce == null ? 0 : IntOf(newForce, "Id")),
                        "the fallen city mirror must carry the new owning force");
                    Assert.That(mirror.Count("CityCount"), Is.EqualTo(88), "city fall must not remove the city mirror entity");

                    // 跑 B:同种子同剧本,不注入镜像——逐回合 digest 逐位相等(读模型纯净性)。
                    (_, _, List<string> digestsB, _) = RunBattleScriptFresh(sim, chosen, null);
                    Assert.That(digestsA.Count, Is.EqualTo(digestsB.Count), "both runs must advance the same number of turns");
                    for (int turn = 0; turn < digestsB.Count; turn++)
                    {
                        Assert.That(digestsA[turn], Is.EqualTo(digestsB[turn]),
                            $"turn {turn}: WorldDigest must be bit-identical with and without the mirror attached");
                    }

                    Console.Out.WriteLine($"[m3h-digest] {digestsB.Count} turns bit-identical across mirror on/off (seed {chosen})");
                }
                finally
                {
                    mirror.Dispose();
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        [Test]
        public void Mirror_Rebuilds_WhenKernelWorldReplaced()
        {
            Assembly sim = LoadSangoSimMod();
            var kernel = new Kernel(sim, 20260902);
            using var world = World.Create();
            object mirrorRuntime = AttachMirror(sim, world, out string tempRoot);
            var mirror = new MirrorHandle(mirrorRuntime);
            try
            {
                object probeCity = kernel.Cities().Cast<object>().OrderBy(city => IntOf(city, "Id")).First();
                int probeId = IntOf(probeCity, "Id");
                int goldAfterBoot = IntOf(probeCity, "gold");

                kernel.AdvanceTurn();
                int goldAfterTurn = IntOf(kernel.Cities().Cast<object>().Single(city => IntOf(city, "Id") == probeId), "gold");
                object probe = mirror.Snapshot("City").Single(candidate => MirrorHandle.KernelIdOf(candidate) == probeId);
                Assert.That(StatsOf(probe, "Gold"), Is.EqualTo(goldAfterTurn), "turn-boundary refresh must track kernel gold");

                // 内核世界替换(读档/重装路径):Reconcile 全量重建,镜像回到新世界初值。
                new Kernel(sim, 20260902);
                mirror.Reconcile();
                probe = mirror.Snapshot("City").Single(candidate => MirrorHandle.KernelIdOf(candidate) == probeId);
                Assert.That(StatsOf(probe, "Gold"), Is.EqualTo(goldAfterBoot),
                    "world replacement rebuild must restore the fresh-boot snapshot");
                Assert.That(mirror.Count("TroopCount"), Is.EqualTo(0), "the replacement world starts with no alive troops");
            }
            finally
            {
                mirror.Dispose();
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
