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
    /// M2.b 部队编成/移动验收(照 SangoRealMapTests 惯例:反射调用 SangoRuntime,不引用
    /// SangoSimMod 工程)。覆盖:
    ///   1. 编成走真实链(CityExpedition.DoJob 语义):troopsSet 落部队、落位城中心格、
    ///      城兵/粮/金/行动力/空闲武将结算、digest 变化;
    ///   2. 移动走真实链(TroopSystem/TroopActionStay 语义):位置变更、路径步数与
    ///      Map.GetMovePath 一致、行动结束(ActionOver)、越程/占位/再动被原版门槛拒绝;
    ///   3. 播种部队 AI 行为与确定性:无任务 AI 部队被 TroopMissionBehaviour getter 自动
    ///      指派回城任务,抵家城即 EnterCity 解散(首回合实证);同种子双跑 10 回合 digest 逐位相等;
    ///   4. 存档对称:播种→移动→存→读→续跑 与 不存档直跑 digest 逐位相等
    ///      (Troop.cell 经 XY2CellConverter 入档/回灌);
    ///   5. 部队标记投影:cell → 世界 cm 摆点按 M2.a 公式。
    /// </summary>
    [TestFixture]
    public sealed class SangoTroopOpsTests
    {
        private const int Seed = 20260902;
        private const int TenTurns = 10;

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

        // ---- 反射小门面:只封装本套测试触碰的内核面,全部为内核既有公共成员。 ----

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

            // 存档回灌会整体替换 Scenario.Cur,所有访问都取当前单例,不缓存实例。
            public object Scenario =>
                Sim.GetType("Sango.Core.Scenario", throwOnError: true)!
                    .GetProperty("Cur", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

            public IEnumerable Cities() => EnumerateSet(FieldValue(Scenario, "citySet"));

            public IEnumerable Troops() => EnumerateSet(FieldValue(Scenario, "troopsSet"));

            public object? TroopById(int id) => Call(FieldValue(Scenario, "troopsSet"), "Get", id);

            public object Map => PropertyValue(Scenario, "Map")!;

            public object? GetCell(int x, int y) => Call(PropertyValue(Map, "CellSet"), "GetCell", x, y);

            public object GetCellOrThrow(int x, int y) => GetCell(x, y) ?? throw new InvalidOperationException($"no cell at ({x},{y})");

            public void AdvanceTurn() => Sim.GetType("Sango.Runtime.SangoTurnDriver", throwOnError: true)!
                .GetMethod("AdvanceTurn", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);

            public string WorldDigest() => (string)Sim.GetType("Sango.Runtime.SangoTurnDriver", throwOnError: true)!
                .GetMethod("WorldDigest", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;

            /// <summary>(Succeeded, ErrorCode, Message, Item2=创建的 Troop 或移动路径步数)。</summary>
            public (bool Succeeded, string ErrorCode, string Message, object? Payload) CreateTroop(
                object city, int[] personIds, int troops, int gold, int food)
            {
                object tuple = Sim.GetType("Sango.Runtime.SangoTroopOps", throwOnError: true)!
                    .GetMethod("CreateTroop", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object?[] { Scenario, city, personIds, null, null, troops, gold, food })!;
                return ReadTuple(tuple);
            }

            public (bool Succeeded, string ErrorCode, string Message, object? Payload) MoveTroop(object troop, object destCell)
            {
                object tuple = Sim.GetType("Sango.Runtime.SangoTroopOps", throwOnError: true)!
                    .GetMethod("MoveTroop", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object[] { Scenario, troop, destCell })!;
                return ReadTuple(tuple);
            }

            public int MovePathSteps(object troop, object destCell)
            {
                object path = Activator.CreateInstance(
                    typeof(List<>).MakeGenericType(Sim.GetType("Sango.Core.Cell", throwOnError: true)!))!;
                Call(Map, "GetMovePath", troop, destCell, path);
                return (int)PropertyValue(path, "Count")!;
            }

            public void FillMoveRange(object troop)
            {
                object range = FieldValue(troop, "MoveRange");
                Call(range, "Clear");
                Call(Map, "GetMoveRange", troop, range);
            }

            public IEnumerable MoveRangeOf(object troop) => (IEnumerable)FieldValue(troop, "MoveRange");

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
            // SangoObjectSet 只暴露非泛型 GetEnumerator(返回数组枚举器),逐项手摇。
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

        // 内核成员字段/属性混用(City.mBelongForce 是字段,Troop.mBelongForce 是属性),
        // 访问器双探测,缺失才返回 null。
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

        // CityExpedition.IsValid 的镜像(经一回合行动力发放后):
        // 城兵力/军粮>0、空闲武将>0、军团行动力>=MakeTroop 费。
        static object FindEligibleCity(Kernel kernel)
        {
            int cost = MakeTroopCostAP(kernel.Sim);
            foreach (object city in kernel.Cities())
            {
                if (PropertyValue(city, "mBelongForce") == null || PropertyValue(city, "mBelongCorps") == null)
                {
                    continue;
                }

                object corps = PropertyValue(city, "mBelongCorps")!;
                if (IntOf(city, "troops") > 0 && IntOf(city, "food") > 0 &&
                    FreePersons(city).Count > 0 && IntOf(corps, "ActionPoint") >= cost)
                {
                    return city;
                }
            }

            throw new InvalidOperationException("no city passes the CityExpedition.IsValid gate after one turn");
        }

        static (object City, int[] PersonIds) SeedOneTroop(Kernel kernel, int troops = 3000)
        {
            kernel.AdvanceTurn();
            object city = FindEligibleCity(kernel);
            var personIds = FreePersons(city).Take(3).Select(person => IntOf(person, "Id")).ToArray();
            (bool ok, string error, _, object? troop) = kernel.CreateTroop(city, personIds, troops, 500, 20000);
            Assert.That(ok, Is.True, $"seed troop failed: {error}");
            Assert.That(troop, Is.Not.Null);
            return (city, personIds);
        }

        static object PickEmptyRangeCell(Kernel kernel, object troop)
        {
            kernel.FillMoveRange(troop);
            object? best = null;
            int bestDistance = -1;
            foreach (object cell in kernel.MoveRangeOf(troop))
            {
                if (PropertyValue(cell, "troop") != null || PropertyValue(cell, "building") != null)
                {
                    continue;
                }

                int distance = Math.Abs(IntOf(cell, "x") - IntOf(troop, "x")) + Math.Abs(IntOf(cell, "y") - IntOf(troop, "y"));
                if (distance > bestDistance)
                {
                    best = cell;
                    bestDistance = distance;
                }
            }

            return best ?? throw new InvalidOperationException("move range has no empty cell");
        }

        // ---- 断言面 ----

        [Test]
        public void CreateTroop_RealChain_EnrollsTroopAndSettlesCityLedger()
        {
            Assembly sim = LoadSangoSimMod();
            var kernel = new Kernel(sim, Seed);
            kernel.AdvanceTurn();
            object city = FindEligibleCity(kernel);
            var personIds = FreePersons(city).Take(2).Select(person => IntOf(person, "Id")).ToArray();
            int corpsId = IntOf(PropertyValue(city, "mBelongCorps")!, "Id");

            int cityTroopsBefore = IntOf(city, "troops");
            int cityGoldBefore = IntOf(city, "gold");
            int cityFoodBefore = IntOf(city, "food");
            int freeBefore = FreePersons(city).Count;
            int actionPointBefore = IntOf(PropertyValue(city, "mBelongCorps")!, "ActionPoint");
            string digestBefore = kernel.WorldDigest();

            (bool ok, string error, _, object? troop) = kernel.CreateTroop(city, personIds, 3000, 500, 20000);
            Assert.That(ok, Is.True, $"createTroop rejected: {error}");
            Assert.That(troop, Is.Not.Null);

            // EnsureTroop:落位城中心格 + cell.troop 双向占据;scenario.Add 已入 troopsSet。
            object centerCell = PropertyValue(city, "CenterCell")!;
            Assert.That(FieldValue(troop!, "cell"), Is.SameAs(centerCell), "EnsureTroop must anchor the troop at the city center cell");
            Assert.That(PropertyValue(centerCell, "troop"), Is.SameAs(troop), "center cell must carry the new troop");
            Assert.That(kernel.Troops(), Has.Some.SameAs(troop), "troopsSet must contain the created troop");
            Assert.That(IntOf(troop!, "Id"), Is.GreaterThan(0));

            // DoJob 结算:城兵/粮/金按表单值扣减,武将出列。
            Assert.That(IntOf(city, "troops"), Is.LessThan(cityTroopsBefore), "city troops must pay the enlisted soldiers");
            Assert.That(IntOf(city, "gold"), Is.LessThan(cityGoldBefore), "city gold must pay the requested purse and troop type costs");
            Assert.That(IntOf(city, "food"), Is.LessThan(cityFoodBefore), "city food must pay the requested rations and troop type costs");
            Assert.That(FreePersons(city).Count, Is.EqualTo(freeBefore - personIds.Length),
                "expedition members must leave the city freePersons list");
            // ReduceActionPoint 只对玩家军团扣减(headless 全 AI 托管,原版语义:AI 军团
            // 不走行动力账,DoJob 的 ReduceActionPoint 调用对 AI 军团是无操作)。
            Assert.That(IntOf(PropertyValue(city, "mBelongCorps")!, "ActionPoint"),
                Is.EqualTo(actionPointBefore), "AI corps action points are not debited (original ReduceActionPoint IsPlayer gate)");

            // 部队归属经 Leader 派生:corps 与编成城一致。
            Assert.That(IntOf(PropertyValue(troop!, "mBelongCorps")!, "Id"), Is.EqualTo(corpsId));

            // digest 部队行加入:编成必须改变世界指纹。
            Assert.That(kernel.WorldDigest(), Is.Not.EqualTo(digestBefore), "WorldDigest must change after troop creation");
        }

        [Test]
        public void MoveTroop_RealChain_LandsOnTargetWithPathMatchingKernel()
        {
            Assembly sim = LoadSangoSimMod();
            var kernel = new Kernel(sim, Seed);
            SeedOneTroop(kernel);
            object troop = kernel.Troops().Cast<object>().First();

            object dest = PickEmptyRangeCell(kernel, troop);
            int destX = IntOf(dest, "x");
            int destY = IntOf(dest, "y");
            int expectedSteps = kernel.MovePathSteps(troop, dest);
            Assert.That(expectedSteps, Is.GreaterThan(1), "a distant empty cell must yield a multi-step path");
            string digestBefore = kernel.WorldDigest();

            (bool ok, string error, _, object? steps) = kernel.MoveTroop(troop, dest);
            Assert.That(ok, Is.True, $"moveTroop rejected: {error}");
            Assert.That(Convert.ToInt32(steps), Is.EqualTo(expectedSteps),
                "settled move steps must equal the kernel GetMovePath length (path settlement parity)");

            Assert.That(IntOf(troop, "x"), Is.EqualTo(destX), "troop must land on the target cell x");
            Assert.That(IntOf(troop, "y"), Is.EqualTo(destY), "troop must land on the target cell y");
            Assert.That(PropertyValue(kernel.GetCellOrThrow(destX, destY), "troop"), Is.SameAs(troop),
                "the destination cell must now carry the troop");
            Assert.That((bool)PropertyValue(troop, "ActionOver")!, Is.True,
                "TroopActionStay.OnMoveDone semantics: the move ends the troop's action");

            // 移动后 digest 变化(部队行 cell 字段移动)。
            Assert.That(kernel.WorldDigest(), Is.Not.EqualTo(digestBefore), "WorldDigest must change after the troop move");
        }

        [Test]
        public void MoveTroop_OriginalGates_RejectOutOfRangeOccupiedAndSecondOrder()
        {
            Assembly sim = LoadSangoSimMod();
            var kernel = new Kernel(sim, Seed);
            SeedOneTroop(kernel);
            object troop = kernel.Troops().Cast<object>().First();
            int originX = IntOf(troop, "x");
            int originY = IntOf(troop, "y");

            // 越程:全图扫描第一个不在移动范围内的存活格(移动力有限必有;不依赖
            // 地图尺寸/角落假设,CI 合成图与真图同逻辑)。原版该路径走多回合委任链。
            kernel.FillMoveRange(troop);
            var rangeCells = kernel.MoveRangeOf(troop).Cast<object>().ToList();
            object? farCell = null;
            int mapWidth = (int)PropertyValue(kernel.Map, "Width")!;
            int mapHeight = (int)PropertyValue(kernel.Map, "Height")!;
            for (int x = 0; farCell == null && x < mapWidth; x++)
            {
                for (int y = 0; y < mapHeight; y++)
                {
                    object? cell = kernel.GetCell(x, y);
                    if (cell != null && !rangeCells.Contains(cell))
                    {
                        farCell = cell;
                        break;
                    }
                }
            }

            Assert.That(farCell, Is.Not.Null, "the map must contain a cell outside the troop move range");
            (bool ok, string error, _, _) = kernel.MoveTroop(troop, farCell!);
            Assert.That(ok, Is.False, "out-of-range move must be rejected");
            Assert.That(error, Is.EqualTo("out_of_range"));
            Assert.That(IntOf(troop, "x"), Is.EqualTo(originX), "rejected move must not change the troop position");
            Assert.That(IntOf(troop, "y"), Is.EqualTo(originY), "rejected move must not change the troop position");

            // 占位:异城中心格(建筑占位;接近/战斗是 M2.c 面)——若恰在移动范围内则断言拒绝。
            int homeCityId = IntOf(PropertyValue(troop, "mBelongCity")!, "Id");
            object? otherCenter = kernel.Cities()
                .Cast<object>()
                .Where(candidate => IntOf(candidate, "Id") != homeCityId)
                .Select(candidate => PropertyValue(candidate, "CenterCell"))
                .FirstOrDefault(cell => rangeCells.Contains(cell));
            if (otherCenter != null)
            {
                (ok, error, _, _) = kernel.MoveTroop(troop, otherCenter);
                Assert.That(ok, Is.False, "occupied target must be rejected");
                Assert.That(error, Is.EqualTo("occupied_target"));
                Assert.That(IntOf(troop, "x"), Is.EqualTo(originX), "rejected move must not change the troop position");
            }

            // 正常移动一次 → 同回合再动被拒(ActionOver,原版命令菜单门)。
            object dest = PickEmptyRangeCell(kernel, troop);
            (ok, error, _, _) = kernel.MoveTroop(troop, dest);
            Assert.That(ok, Is.True, $"first move must succeed: {error}");
            (ok, error, _, _) = kernel.MoveTroop(troop, dest);
            Assert.That(ok, Is.False, "second order in the same turn must be rejected");
            Assert.That(error, Is.EqualTo("troop_acted"));
        }

        [Test]
        public void SeededTroop_AI_AutoReturnsAndDissolvesIntoHomeCity_SameSeedDigestIdentical()
        {
            Assembly sim = LoadSangoSimMod();

            string RunOnce()
            {
                var kernel = new Kernel(sim, Seed);
                (object city, _) = SeedOneTroop(kernel);
                object troop = kernel.Troops().Cast<object>().First();
                int troopId = IntOf(troop, "Id");
                int soldiers = IntOf(troop, "troops");
                int cityTroopsAtSeed = IntOf(city, "troops");

                // AI 部队行为现状结论(实证):无任务 AI 部队经 TroopMissionBehaviour getter
                // 自动获得 TroopReturnCity 任务;播种即在家城中心,TryMoveToCity 判定已抵,
                // EnterCity 把兵/粮/金入城并 Clear——部队从 troopsSet 移除。首回合即可观察。
                kernel.AdvanceTurn();
                Assert.That((bool)PropertyValue(troop, "IsAlive")!, Is.False,
                    "the missionless AI troop must be absorbed into its home city (auto ReturnCity mission)");
                Assert.That(kernel.TroopById(troopId), Is.Null, "the dissolved troop must leave troopsSet");
                Assert.That(IntOf(city, "troops"), Is.GreaterThan(cityTroopsAtSeed),
                    $"EnterCity must return the enlisted soldiers to the city garrison ({cityTroopsAtSeed} -> {IntOf(city, "troops")}, troop carried {soldiers})");

                for (int i = 1; i < TenTurns; i++)
                {
                    kernel.AdvanceTurn();
                }

                return kernel.WorldDigest();
            }

            string first = RunOnce();
            string second = RunOnce();
            TestContext.Progress.WriteLine($"[m2b-troop-determinism] digest={first}");
            Assert.That(second, Is.EqualTo(first), "same-seed double run with a seeded troop must match bit for bit over 10 turns");
        }

        [Test]
        public void SeededTroop_MoveSaveRestore_ContinuesIdenticalToUnsavedChain()
        {
            Assembly sim = LoadSangoSimMod();

            string ChainA()
            {
                var kernel = new Kernel(sim, Seed);
                SeedOneTroop(kernel);
                object troop = kernel.Troops().Cast<object>().First();
                object dest = PickEmptyRangeCell(kernel, troop);
                (bool ok, string error, _, _) = kernel.MoveTroop(troop, dest);
                Assert.That(ok, Is.True, $"move before save failed: {error}");

                // 移动后立即捕获:部队在野外存活,入档/回灌的 cell 对称性有活体对象可断言;
                // 其后 AI 回城解散在两条链同步发生,digest 语义不受影响。
                object capture = kernel.Capture();
                int troopId = IntOf(troop, "Id");
                int xAfterMove = IntOf(troop, "x");
                int yAfterMove = IntOf(troop, "y");
                kernel.Restore(capture);

                object restored = kernel.TroopById(troopId)
                    ?? throw new InvalidOperationException("restored world lost the troop");
                Assert.That((bool)PropertyValue(restored, "IsAlive")!, Is.True, "restored troop must stay alive mid-field");
                Assert.That(IntOf(restored, "x"), Is.EqualTo(xAfterMove), "Troop.cell must round-trip via XY2CellConverter");
                Assert.That(IntOf(restored, "y"), Is.EqualTo(yAfterMove), "Troop.cell must round-trip via XY2CellConverter");

                for (int i = 0; i < 3; i++)
                {
                    kernel.AdvanceTurn();
                }

                return kernel.WorldDigest();
            }

            string ChainB()
            {
                var kernel = new Kernel(sim, Seed);
                SeedOneTroop(kernel);
                object troop = kernel.Troops().Cast<object>().First();
                object dest = PickEmptyRangeCell(kernel, troop);
                (bool ok, string error, _, _) = kernel.MoveTroop(troop, dest);
                Assert.That(ok, Is.True, $"move in the unsaved chain failed: {error}");
                for (int i = 0; i < 3; i++)
                {
                    kernel.AdvanceTurn();
                }

                return kernel.WorldDigest();
            }

            string across = ChainA();
            string neverSaved = ChainB();
            TestContext.Progress.WriteLine($"[m2b-troop-save] across={across} never={neverSaved}");
            Assert.That(across, Is.EqualTo(neverSaved),
                "seed→move→save→restore→continue must replay the unsaved chain bit for bit (troop payload included)");
        }

        [Test]
        public void TroopMarkers_BuildPlacements_ProjectCellToWorldCm()
        {
            Assembly sim = LoadSangoSimMod();
            var kernel = new Kernel(sim, Seed);
            SeedOneTroop(kernel);
            object troop = kernel.Troops().Cast<object>().First();

            float gridSize = (float)PropertyValue(kernel.Map, "GridSize")!;
            int mapWidth = (int)PropertyValue(kernel.Map, "Width")!;
            int halfWorldCm = (int)(mapWidth * gridSize * 100f / 2f);
            int troopX = IntOf(troop, "x");
            int troopY = IntOf(troop, "y");

            object placements = sim.GetType("Sango.Runtime.SangoTroopMarkers", throwOnError: true)!
                .GetMethod("BuildPlacements", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new[] { kernel.Scenario })!;
            var list = (IList)placements;
            Assert.That(list.Count, Is.EqualTo(1), "one seeded troop must project one marker placement");

            object placement = list[0]!;
            Assert.That((int)PropertyValue(placement, "TroopId")!, Is.EqualTo(IntOf(troop, "Id")));
            var position = (System.Numerics.Vector3)PropertyValue(placement, "PositionCm")!;
            Assert.That(position.X, Is.EqualTo((troopY * gridSize + gridSize * 0.5f) * 100f - halfWorldCm).Within(1),
                "east placement follows the M2.a cell→world formula");
            Assert.That(position.Z, Is.EqualTo((troopX * gridSize + gridSize * 0.5f) * 100f - halfWorldCm).Within(1),
                "north placement follows the M2.a cell→world formula");
            int forceId = (int)PropertyValue(placement, "ForceId")!;
            Assert.That(forceId, Is.GreaterThan(0), "the seeded troop belongs to a force (marker force color)");
        }
    }
}
