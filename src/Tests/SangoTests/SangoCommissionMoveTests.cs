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
    /// M3.c 委任移动多回合链验收(原版 TroopInteractiveMoveToCell → MissionType.TroopMovetoCell):
    ///   a) 越程 moveTroop 授 TroopMovetoCell 任务(missionParams1/2=目标格),逐回合沿
    ///      直达路径推进且推进量吃满移动力上限(下一格将超出移动力或已抵达),抵达后
    ///      ActionOver;玩家控部队任务清空待命,AI 托管部队转 TroopReturnCity(原版
    ///      TroopMovetoCell.Prepare 完结分支的两态);
    ///   b) 同种子双跑 digest 逐位相等;换种子失配;
    ///   c) 行军中途存档→读→续跑 == 不存档链(多回合任务跨存档边界);
    ///   d) 途中目标格被敌占:原版语义 = TryMoveToCell 推进到可及边界后逐回合 DoAI 即时
    ///      完结(ActionOver),任务保持 TroopMovetoCell 等待——不打断、不转攻击
    ///      (敌占格进不了 MoveRange,Cell.CanPassThrough 势力门;TroopMovetoCell.DoAI
    ///      无目标格占位检查)。堵路解除后任务自动续走至抵达。
    /// </summary>
    [TestFixture]
    public sealed class SangoCommissionMoveTests
    {
        private const int Seed = 20260902;
        private const int DivergentSeed = 19940801;
        private const int MaxMarchTurns = 30;

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

            public Kernel(Assembly sim, int seed, int playerForceId = 0)
            {
                Sim = sim;
                string method = playerForceId > 0 ? "BootWithPlayer" : "Boot";
                object?[] args = playerForceId > 0
                    ? new object?[] { NewVfs(), "SangoContentMod", seed, playerForceId, "Scenario/Scenario.json" }
                    : new object?[] { NewVfs(), "SangoContentMod", seed, "Scenario/Scenario.json" };
                sim.GetType("Sango.Runtime.SangoKernelBoot", throwOnError: true)!
                    .GetMethod(method, BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, args);
            }

            public object Scenario =>
                Sim.GetType("Sango.Core.Scenario", throwOnError: true)!
                    .GetProperty("Cur", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

            public object Map => PropertyValue(Scenario, "Map")!;

            public IEnumerable Cities() => EnumerateSet(FieldValue(Scenario, "citySet"));

            public IEnumerable Forces() => EnumerateSet(FieldValue(Scenario, "forceSet"));

            public object? TroopById(int id) => Call(FieldValue(Scenario, "troopsSet"), "Get", id);

            public void AdvanceTurn() => Sim.GetType("Sango.Runtime.SangoTurnDriver", throwOnError: true)!
                .GetMethod("AdvanceTurn", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);

            public string WorldDigest() => (string)Sim.GetType("Sango.Runtime.SangoTurnDriver", throwOnError: true)!
                .GetMethod("WorldDigest", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;

            public object? GetCell(int x, int y) => Call(Map, "GetCell", x, y);

            public int MapDistance(object a, object b)
            {
                Type cellType = Sim.GetType("Sango.Core.Cell", throwOnError: true)!;
                MethodInfo method = Map.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Single(m => m.Name == "Distance" && m.GetParameters().Length == 2 &&
                                 m.GetParameters()[0].ParameterType == cellType);
                return (int)method.Invoke(Map, new[] { a, b })!;
            }

            public int MissionTypeValue(string name) =>
                (int)Enum.Parse(Sim.GetType("Sango.Core.MissionType", throwOnError: true)!, name);

            public (bool Succeeded, string ErrorCode, string Action, object? Troop) CreateTroop(
                object city, int[] personIds, int troops, int food)
            {
                object tuple = Sim.GetType("Sango.Runtime.SangoTroopOps", throwOnError: true)!
                    .GetMethod("CreateTroop", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object?[] { Scenario, city, personIds, null, null, troops, 0, food })!;
                object result = tuple.GetType().GetField("Item1")!.GetValue(tuple)!;
                return ((bool)PropertyValue(result, "Succeeded")!,
                    (string)PropertyValue(result, "ErrorCode")!,
                    (string)PropertyValue(result, "Message")!,
                    tuple.GetType().GetField("Item2")!.GetValue(tuple));
            }

            public (bool Succeeded, string ErrorCode, string Action, int PathSteps) MoveTroop(object troop, object destCell)
            {
                object tuple = Sim.GetType("Sango.Runtime.SangoTroopOps", throwOnError: true)!
                    .GetMethod("MoveTroop", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object[] { Scenario, troop, destCell })!;
                object result = tuple.GetType().GetField("Item1")!.GetValue(tuple)!;
                return ((bool)PropertyValue(result, "Succeeded")!,
                    (string)PropertyValue(result, "ErrorCode")!,
                    (string)PropertyValue(result, "Message")!,
                    (int)tuple.GetType().GetField("Item2")!.GetValue(tuple)!);
            }

            public void SetMission(object troop, string missionTypeName, int targetId)
            {
                Type missionType = Sim.GetType("Sango.Core.MissionType", throwOnError: true)!;
                Call(troop, "SetMission", Enum.Parse(missionType, missionTypeName), targetId);
            }

            public int MissionTypeOf(object troop) => IntOf(troop, "missionType");

            public object Capture() => Activator.CreateInstance(
                Sim.GetType("Sango.Runtime.SangoSaveParticipant", throwOnError: true)!,
                new object[] { NewVfs(), "SangoContentMod", "Scenario/Scenario.json" })!
                .GetType().GetMethod("CaptureState", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(Activator.CreateInstance(
                    Sim.GetType("Sango.Runtime.SangoSaveParticipant", throwOnError: true)!,
                    new object[] { NewVfs(), "SangoContentMod", "Scenario/Scenario.json" }), null)!;

            public void Restore(object capture) => Activator.CreateInstance(
                Sim.GetType("Sango.Runtime.SangoSaveParticipant", throwOnError: true)!,
                new object[] { NewVfs(), "SangoContentMod", "Scenario/Scenario.json" })!
                .GetType().GetMethod("RestoreState", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(Activator.CreateInstance(
                    Sim.GetType("Sango.Runtime.SangoSaveParticipant", throwOnError: true)!,
                    new object[] { NewVfs(), "SangoContentMod", "Scenario/Scenario.json" }),
                    new[] { capture });

            public void EndPlayerTurn() => Sim.GetType("Sango.Runtime.SangoPlayerTurnOps", throwOnError: true)!
                .GetMethod("EndPlayerTurn", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);

            public bool AwaitingPlayer() => (bool)Sim.GetType("Sango.Runtime.SangoPlayerTurnOps", throwOnError: true)!
                .GetMethod("AwaitingPlayer", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new[] { Scenario })!;

            public int LandMoveAbility(object troop) => IntOf(troop, "landMoveAbility");

            public int MoveCost(object troop, object cell) => (int)Call(troop, "MoveCost", cell)!;

            public int CellX(object cell) => IntOf(cell, "x");

            public int CellY(object cell) => IntOf(cell, "y");

            public void UpdateCell(object troop, object destCell, object lastCell, bool isEndMove) =>
                Call(troop, "UpdateCell", destCell, lastCell, isEndMove);

            /// <summary>以 troop 当前格为起点的直达路径(GetDirectMovePath,与任务行为同源)。</summary>
            public List<object> DirectPathFromTroop(object troop, object destCell)
            {
                MethodInfo method = Map.GetType()
                    .GetMethod("GetDirectMovePath", BindingFlags.Public | BindingFlags.Instance)!;
                var pathList = (IList)Activator.CreateInstance(
                    typeof(List<>).MakeGenericType(Sim.GetType("Sango.Core.Cell", true)!))!;
                method.Invoke(Map, new object?[] { troop, destCell, pathList, null });
                return pathList.Cast<object>().ToList();
            }

            internal static IEnumerable EnumerateSet(object set)
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

            internal static object FieldValue(object target, string name)
            {
                Type type = target.GetType();
                return type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
                    ?? type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(target)!;
            }

            internal static object? PropertyValue(object target, string name)
            {
                Type type = target.GetType();
                return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
                    ?? type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
            }

            internal static object? Call(object target, string method, params object?[] args)
            {
                MethodInfo info = target.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Single(candidate => candidate.Name == method && candidate.GetParameters().Length == args.Length);
                return info.Invoke(target, args);
            }

            internal static int IntOf(object target, string member)
            {
                object? value = target.GetType().GetField(member, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
                    ?? target.GetType().GetProperty(member, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
                return Convert.ToInt32(value);
            }
        }

        // ---- 剧本共享面 ----

        static List<object> FreePersons(object city) =>
            ((IEnumerable)Kernel.PropertyValue(city, "freePersons")!).Cast<object>().ToList();

        static bool PassExpeditionGate(object city) =>
            Kernel.PropertyValue(city, "mBelongForce") != null && Kernel.PropertyValue(city, "mBelongCorps") != null &&
            Kernel.IntOf(city, "troops") > 0 && Kernel.IntOf(city, "food") > 0 &&
            FreePersons(city).Count > 0;

        static object FirstGateCity(Kernel kernel) =>
            kernel.Cities().Cast<object>().First(PassExpeditionGate);

        /// <summary>
        /// 行军目标选择:优先"同势力远城对"路径中段的可驻空格(行军全程走己方腹地,排除
        /// 途中遭遇战对多回合链的干扰);无远城对时退化为环绕扫描的远距可驻空格。
        /// 固定扫描序,确定性成立。
        /// </summary>
        static (object Home, int ForceId, int MateCityId) SelectMarchCityPair(Kernel probe)
        {
            var gateCities = probe.Cities().Cast<object>().Where(PassExpeditionGate).ToList();
            var allCities = probe.Cities().Cast<object>().ToList();
            object? bestHome = null;
            object? bestMate = null;
            int bestPair = 0;
            int bestForceId = 0;
            foreach (object a in gateCities)
            {
                object forceA = Kernel.PropertyValue(a, "mBelongForce")!;
                foreach (object b in allCities)
                {
                    if (ReferenceEquals(a, b) || !ReferenceEquals(Kernel.PropertyValue(b, "mBelongForce")!, forceA))
                    {
                        continue;
                    }

                    int d = probe.MapDistance(Kernel.PropertyValue(a, "CenterCell")!, Kernel.PropertyValue(b, "CenterCell")!);
                    if (d > bestPair)
                    {
                        (bestHome, bestMate, bestPair) = (a, b, d);
                        bestForceId = Kernel.IntOf(forceA, "Id");
                    }
                }
            }

            if (bestHome == null || bestMate == null || bestPair < 20)
            {
                object fallbackHome = FirstGateCity(probe);
                return (fallbackHome, Kernel.IntOf(Kernel.PropertyValue(fallbackHome, "mBelongForce")!, "Id"), 0);
            }

            return (bestHome, bestForceId, Kernel.IntOf(bestMate, "Id"));
        }

        static object FindMarchTarget(Kernel kernel, object troop, object home, int mateCityId, int ability)
        {
            if (mateCityId > 0)
            {
                object mate = kernel.Cities().Cast<object>().First(c => Kernel.IntOf(c, "Id") == mateCityId);
                var path = kernel.DirectPathFromTroop(troop, Kernel.PropertyValue(mate, "CenterCell")!);
                for (int back = 6; back < path.Count; back++)
                {
                    object candidate = path[path.Count - back];
                    if ((bool)Kernel.Call(candidate, "CanStay", troop)! && (bool)Kernel.PropertyValue(candidate, "moveAble")! &&
                        kernel.MapDistance(Kernel.PropertyValue(home, "CenterCell")!, candidate) >= Math.Max(20, ability))
                    {
                        return candidate;
                    }
                }
            }

            return FindFarStayableCell(kernel, troop, Kernel.PropertyValue(home, "CenterCell")!, ability);
        }

        static object FindFarStayableCell(Kernel kernel, object troop, object center, int ability)
        {
            int cx = Kernel.IntOf(center, "x");
            int cy = Kernel.IntOf(center, "y");
            int maxD = Math.Min(80, Math.Max(
                Convert.ToInt32(Kernel.PropertyValue(kernel.Map, "Width")),
                Convert.ToInt32(Kernel.PropertyValue(kernel.Map, "Height"))) - 2);
            int[] preferredDistances = { Math.Max(60, ability * 3), ability * 2 + 2 };
            foreach (int preferred in preferredDistances.Select(d => Math.Min(d, maxD)).Distinct())
            {
                for (int d = Math.Min(preferred + 5, maxD); d >= preferred; d--)
                {
                    var offsets = new (int Dx, int Dy)[] { (d, 0), (0, d), (-d, 0), (0, -d), (d, d), (-d, -d), (d, -d), (-d, d) };
                    foreach ((int dx, int dy) in offsets)
                    {
                        object? cell = kernel.GetCell(cx + dx, cy + dy);
                        if (cell == null)
                        {
                            continue;
                        }

                        if ((bool)Kernel.Call(cell, "CanStay", troop)! && (bool)Kernel.PropertyValue(cell, "moveAble")! &&
                            kernel.MapDistance(center, cell) >= preferred)
                        {
                            return cell;
                        }
                    }
                }
            }

            throw new InvalidOperationException(
                "no stayable far cell found for the march scenario from (" + cx + "," + cy + ").");
        }

        static (object Troop, object TargetCell, int Ability, (int X, int Y) Spawn) CommissionTroop(
            Kernel kernel, object home, int mateCityId)
        {
            (_, _, _, object? troop) = kernel.CreateTroop(
                home, FreePersons(home).Take(1).Select(p => Kernel.IntOf(p, "Id")).ToArray(), 3000, 20_000);
            Assert.That(troop, Is.Not.Null, "troop formation must pass the expedition gate");
            int ability = kernel.LandMoveAbility(troop!);
            object target = FindMarchTarget(kernel, troop!, home, mateCityId, ability);
            var spawn = (Kernel.IntOf(troop!, "x"), Kernel.IntOf(troop!, "y"));
            (bool ok, string error, string action, _) = kernel.MoveTroop(troop!, target);
            Assert.That(ok, Is.True, "commissioned move must be accepted: " + error);
            Assert.That(action, Is.EqualTo("commission-move"));
            return (troop!, target, ability, spawn);
        }

        /// <summary>
        /// 玩家局行军链(原版委任移动的唯一使用面:TroopInteractiveMoveToCell 的菜单门 =
        /// 玩家势力且当前行动):BootWithPlayer → 首次 step 停玩家门 → 编成+委任 →
        /// 每回合 EndPlayerTurn(「进行」,泵玩家带任务部队) + step(推进全世界,回到玩家门)。
        /// 每回合在玩家门记录位置/回合起点直达路径/digest。
        /// </summary>
        sealed class MarchRun
        {
            public Kernel KernelRef = null!;
            public int TroopId;
            public object TargetCell = null!;
            public (int X, int Y) Spawn;
            public int TotalDistance;
            public int Ability;
            public List<(int X, int Y)> Positions = new();
            public List<List<(int X, int Y)>> TurnPaths = new();
            public List<string> Digests = new();
            public int ArrivalIndex = -1;
            public bool ArrivalActionOver;

            public object Troop => KernelRef.TroopById(TroopId)!;

            public bool OnTarget => (Kernel.IntOf(Troop, "x"), Kernel.IntOf(Troop, "y")) ==
                (KernelRef.CellX(TargetCell), KernelRef.CellY(TargetCell));
        }

        static MarchRun RunMarch(Assembly sim, int seed, int? captureAfterTurns, int extraTurnsAfterArrival)
        {
            int forceId;
            int homeId;
            int mateCityId;
            {
                var probe = new Kernel(sim, seed);
                (object probeHome, int fid, int mate) = SelectMarchCityPair(probe);
                forceId = fid;
                homeId = Kernel.IntOf(probeHome, "Id");
                mateCityId = mate;
            }

            var kernel = new Kernel(sim, seed, forceId);
            kernel.AdvanceTurn();
            Assert.That(kernel.AwaitingPlayer(), Is.True, "player world must stop at the player gate");
            object home = kernel.Cities().Cast<object>().First(c => Kernel.IntOf(c, "Id") == homeId);

            (object troop, object target, int ability, var spawn) = CommissionTroop(kernel, home, mateCityId);
            var run = new MarchRun
            {
                KernelRef = kernel,
                TroopId = Kernel.IntOf(troop, "Id"),
                TargetCell = target,
                Ability = ability,
                Spawn = spawn,
                TotalDistance = kernel.MapDistance(Kernel.PropertyValue(troop, "cell")!, target),
            };
            Assert.That(run.TotalDistance, Is.GreaterThan(run.Ability),
                "the march target must be far enough to require multiple turns");
            Assert.That(kernel.MissionTypeOf(troop), Is.EqualTo(kernel.MissionTypeValue("TroopMovetoCell")),
                "moveTroop must grant the TroopMovetoCell mission (TroopInteractiveMoveToCell.OnEnter)");
            Assert.That(Kernel.IntOf(troop, "missionParams1"), Is.EqualTo(kernel.CellX(target)));
            Assert.That(Kernel.IntOf(troop, "missionParams2"), Is.EqualTo(kernel.CellY(target)));

            // 段 0 = moveTroop 泵出的首回合推进(首拍路径此刻已消费,记空表)。
            run.Positions.Add((Kernel.IntOf(troop, "x"), Kernel.IntOf(troop, "y")));
            run.TurnPaths.Add(new List<(int, int)>());
            run.Digests.Add(kernel.WorldDigest());

            int turns = 0;
            while (!run.OnTarget && turns < MaxMarchTurns)
            {
                turns++;
                object marcher = run.Troop;
                var path = kernel.DirectPathFromTroop(marcher, target)
                    .Select(c => (Kernel.IntOf(c, "x"), Kernel.IntOf(c, "y"))).ToList();
                kernel.EndPlayerTurn();
                if (run.OnTarget && run.ArrivalIndex < 0)
                {
                    // 抵达发生在「进行」泵内(DoAI 完结自置 ActionOver,OnAIDone 同义)。
                    run.ArrivalActionOver = (bool)Kernel.PropertyValue(run.Troop, "ActionOver")!;
                }

                kernel.AdvanceTurn();
                run.Positions.Add((Kernel.IntOf(run.Troop, "x"), Kernel.IntOf(run.Troop, "y")));
                run.TurnPaths.Add(path);
                run.Digests.Add(kernel.WorldDigest());
                if (run.OnTarget && run.ArrivalIndex < 0)
                {
                    run.ArrivalIndex = run.Positions.Count - 1;
                }

                if (captureAfterTurns == turns)
                {
                    // 玩家门静止态存档(CurRunForce=玩家);回灌按 curForceId 静默 drain 回
                    // 当前势力,但 CurRunCorps 等军团运行位不入档——先 step 回玩家门
                    // (不消耗模拟,与 SangoPlayerTests 的回灌节奏一致),行军任务面
                    // (missionType/params)随 [JsonProperty] 捕获,续跑逐位对齐。
                    kernel.Restore(kernel.Capture());
                    kernel.AdvanceTurn();
                    Assert.That(kernel.AwaitingPlayer(), Is.True,
                        "post-restore step must come back to the player gate");
                }
            }

            Assert.That(run.ArrivalIndex, Is.GreaterThanOrEqualTo(0),
                "the commissioned troop must arrive within the turn budget");

            for (int i = 0; i < extraTurnsAfterArrival; i++)
            {
                kernel.EndPlayerTurn();
                kernel.AdvanceTurn();
                run.Positions.Add((Kernel.IntOf(run.Troop, "x"), Kernel.IntOf(run.Troop, "y")));
                run.TurnPaths.Add(new List<(int, int)>());
                run.Digests.Add(kernel.WorldDigest());
            }

            return run;
        }

        // ---- a) 多回合推进 + 抵达终态 ----

        [Test]
        public void CommissionMove_AdvancesFullAllowancePerTurn_AndArrives()
        {
            Assembly sim = LoadSangoSimMod();
            MarchRun run = RunMarch(sim, Seed, captureAfterTurns: null, extraTurnsAfterArrival: 2);
            Kernel kernel = run.KernelRef;
            object troop = run.Troop;

            // 逐段断言(段 k = 回合 k 的推进,路径 = 该回合起点直达路径,与任务行为同源):
            // 推进 ≥1 格、累计移动消耗 ≤ 移动力上限、停点只有"下一格将超出移动力"或
            // "已抵达"两种(推进吃满移动力上限)。
            int ability = run.Ability;
            Assert.That(ability, Is.GreaterThan(0));
            // 段 0 = moveTroop 泵出的本回合首推进(interactive Update 语义);
            // 段 1 = 命令回合的「进行」收束(ActionOver 已置,泵跳过,合法不推进);
            // 段 2.. = 每回合一次满额度推进。
            Assert.That(run.Positions[0], Is.Not.EqualTo(run.Spawn),
                "the moveTroop pump must perform the command turn's advance (interactive Update semantics)");
            Assert.That(run.ArrivalIndex, Is.GreaterThanOrEqualTo(2),
                "the march must span at least two post-command turns");
            for (int i = 2; i <= run.ArrivalIndex; i++)
            {
                (int fromX, int fromY) = run.Positions[i - 1];
                (int toX, int toY) = run.Positions[i];
                Assert.That((toX, toY), Is.Not.EqualTo((fromX, fromY)),
                    "turn " + i + " must advance (not stall before arrival)");
                var cells = run.TurnPaths[i];
                Assert.That(cells, Is.Not.Empty, "turn " + i + " direct path must be recorded");
                int fromIndex = cells.IndexOf((fromX, fromY));
                int toIndex = cells.IndexOf((toX, toY));
                Assert.That(fromIndex, Is.GreaterThanOrEqualTo(0),
                    "turn " + i + " start (" + fromX + "," + fromY + ") must be on the direct path");
                Assert.That(toIndex, Is.GreaterThan(fromIndex),
                    "turn " + i + " must advance along the direct path to (" + toX + "," + toY + ")");

                // 移动消耗(ZOC 格按引擎语义 cost 重置为移动力上限,累计可超 ability,
                // 见 Map.GetMoveRange 的 IsZOC 分支):断言推进有实耗 + 停点只有
                // "下一格将超出移动力"或"已抵达"两种(推进吃满移动力上限)。
                int spent = 0;
                for (int k = fromIndex + 1; k <= toIndex; k++)
                {
                    spent += kernel.MoveCost(troop, kernel.GetCell(cells[k].X, cells[k].Y)!);
                }

                Assert.That(spent, Is.GreaterThan(0), "turn " + i + " must consume movement points");

                bool arrived = toIndex == cells.Count - 1;
                bool nextWouldExceed = arrived ||
                                       spent + kernel.MoveCost(troop, kernel.GetCell(cells[toIndex + 1].X, cells[toIndex + 1].Y)!) > ability;
                Assert.That(nextWouldExceed, Is.True,
                    "turn " + i + " must stop only when the allowance is exhausted or the target is reached");
            }

            // 抵达终态:「进行」泵内 DoAI 完结自置 ActionOver;玩家控部队任务清空待命
            // (原版 TroopMovetoCell.Prepare 完结分支:IsPlayerControl → ClearMission)。
            Assert.That((bool)Kernel.PropertyValue(troop, "IsAlive")!, Is.True);
            Assert.That(run.ArrivalIndex, Is.GreaterThanOrEqualTo(1), "the march must span multiple turns");
            Assert.That(run.ArrivalActionOver, Is.True, "the arrival turn's DoAI completion must set ActionOver");
            Assert.That(kernel.MissionTypeOf(troop), Is.EqualTo(0),
                "a player-controlled troop clears the mission on arrival (standby)");
            Assert.That(run.Positions[(run.ArrivalIndex + 1)..].All(p => p == run.Positions[run.ArrivalIndex]), Is.True,
                "post-arrival turns must hold position (standby)");
            Console.Out.WriteLine("[m3c] ability=" + ability + " turns=" + run.ArrivalIndex +
                                  " distance=" + run.TotalDistance + " digest=" + run.Digests[run.ArrivalIndex][..12] + "...");
        }

        // ---- b) 确定性 ----

        [Test]
        public void CommissionMove_SameSeedBitIdentical_DifferentSeedDiverges()
        {
            Assembly sim = LoadSangoSimMod();
            MarchRun first = RunMarch(sim, Seed, captureAfterTurns: null, extraTurnsAfterArrival: 2);
            MarchRun second = RunMarch(sim, Seed, captureAfterTurns: null, extraTurnsAfterArrival: 2);
            Assert.That(second.Digests.Count, Is.EqualTo(first.Digests.Count));
            Assert.That(second.Positions, Is.EqualTo(first.Positions),
                "same-seed marches must trace identical cell sequences");
            for (int i = 0; i < first.Digests.Count; i++)
            {
                Assert.That(second.Digests[i], Is.EqualTo(first.Digests[i]), "march digest #" + (i + 1) + " diverged");
            }

            Console.Out.WriteLine("[m3c] same-seed march: " + first.Digests.Count + " digests bit-identical, e.g. step 1 " +
                                  first.Digests[0][..12] + "... == " + second.Digests[0][..12] + "...");

            MarchRun divergent = RunMarch(sim, DivergentSeed, captureAfterTurns: null, extraTurnsAfterArrival: 0);
            int mismatches = Enumerable.Range(0, Math.Min(first.Digests.Count, divergent.Digests.Count))
                .Count(i => divergent.Digests[i] != first.Digests[i]);
            Assert.That(mismatches, Is.GreaterThan(0),
                "a different seed must break march digest equality (guards against an empty chain)");
            Console.Out.WriteLine("[m3c] divergent seed: " + mismatches + "/" + first.Digests.Count + " march digests differ");
        }

        // ---- c) 行军中途存档 → 读 → 续跑 == 直跑链 ----

        [Test]
        public void CommissionMove_MidMarchSaveAndLoad_MatchesDirectChain()
        {
            Assembly sim = LoadSangoSimMod();
            MarchRun direct = RunMarch(sim, Seed, captureAfterTurns: null, extraTurnsAfterArrival: 2);
            MarchRun viaSave = RunMarch(sim, Seed, captureAfterTurns: 2, extraTurnsAfterArrival: 2);
            Assert.That(viaSave.Digests.Count, Is.EqualTo(direct.Digests.Count),
                "the save/load chain must cover the same turns as the direct chain");
            Assert.That(viaSave.Positions, Is.EqualTo(direct.Positions),
                "the save/load march must trace the same cells as the direct march");
            for (int i = 0; i < direct.Digests.Count; i++)
            {
                Assert.That(viaSave.Digests[i], Is.EqualTo(direct.Digests[i]),
                    "post-restore march digest #" + (i + 1) + " diverged");
            }

            Console.Out.WriteLine("[m3c] mid-march restore at march turn 2; all " + direct.Digests.Count +
                                  " digests bit-identical (e.g. " + direct.Digests[^1][..12] + "...)");
        }

        // ---- d) 途中目标格被敌占(原版语义:等待,不打断不转攻击) ----

        [Test]
        public void CommissionMove_TargetOccupiedByEnemy_MissionWaits()
        {
            Assembly sim = LoadSangoSimMod();
            // 全托管局:堵路部队经敌方城编成(玩家局的城门只放行玩家城,敌方播种进不来);
            // 行军方/目标沿用己方腹地城对,窗口短,城 AI 出征连锁(M3.a)不在面内。
            var kernel = new Kernel(sim, Seed);
            kernel.AdvanceTurn();
            (object pairHome, _, int mateCityId) = SelectMarchCityPair(kernel);
            object home = kernel.Cities().Cast<object>().First(c => Kernel.IntOf(c, "Id") == Kernel.IntOf(pairHome, "Id"));
            (object troop, object target, _, _) = CommissionTroop(kernel, home, mateCityId);

            // 敌占播种:与行军方敌对的城编成堵路部队,经内核公开 UpdateCell 落到目标格,
            // 授 TroopStay 原地驻守(无任务 AI 部队会自动回城,见 M2.b 结论)。
            object blockerHome = kernel.Cities().Cast<object>().First(city =>
                !ReferenceEquals(city, home) && PassExpeditionGate(city) &&
                CitiesAreEnemies(sim, home, city));
            (_, _, _, object? blocker) = kernel.CreateTroop(
                blockerHome, FreePersons(blockerHome).Take(1).Select(p => Kernel.IntOf(p, "Id")).ToArray(), 3000, 20_000);
            Assert.That(blocker, Is.Not.Null, "the blocking troop formation must pass its gates");
            kernel.UpdateCell(blocker!, target, Kernel.PropertyValue(blocker!, "cell")!, isEndMove: true);
            kernel.SetMission(blocker!, "TroopStay", 0);
            int blockedDistance = kernel.MapDistance(Kernel.PropertyValue(troop, "cell")!, target);
            Assert.That(blockedDistance, Is.GreaterThan(3),
                "the block must be seeded while the marcher is still en route");

            // 原版语义断言(依据 TroopMovetoCell.DoAI/TryMoveToCell:敌占格进不了 MoveRange,
            // 推进到可及边界后 MoveTo(原地) 立即真 → DoAI 完结 → ActionOver;任务不清、
            // 不转攻击):任务保持 TroopMovetoCell,部队永不上目标格,留在可及邻域等待。
            int movetoCell = kernel.MissionTypeValue("TroopMovetoCell");
            for (int turn = 0; turn < 5; turn++)
            {
                kernel.AdvanceTurn();
                Assert.That(kernel.MissionTypeOf(troop), Is.EqualTo(movetoCell),
                    "turn " + (turn + 1) + ": the mission must stay TroopMovetoCell while the target is enemy-held");
                Assert.That((Kernel.IntOf(troop, "x"), Kernel.IntOf(troop, "y")),
                    Is.Not.EqualTo((kernel.CellX(target), kernel.CellY(target))),
                    "the marcher must never step onto the enemy-held target cell");
                Assert.That((bool)Kernel.PropertyValue(troop, "IsAlive")!, Is.True);
                int distance = kernel.MapDistance(Kernel.PropertyValue(troop, "cell")!, target);
                Assert.That(distance, Is.GreaterThan(0).And.LessThanOrEqualTo(blockedDistance + 1),
                    "turn " + (turn + 1) + ": the marcher must hold near the border (distance " + distance + ")");
            }

            // 堵路部队移开后任务自动续走(等待而非取消的另一半证据)。
            kernel.UpdateCell(blocker!, Kernel.PropertyValue(blockerHome, "CenterCell")!, target, isEndMove: true);
            int arrival = -1;
            for (int turn = 0; turn < MaxMarchTurns && arrival < 0; turn++)
            {
                kernel.AdvanceTurn();
                if (Kernel.IntOf(troop, "x") == kernel.CellX(target) && Kernel.IntOf(troop, "y") == kernel.CellY(target))
                {
                    arrival = turn;
                }
            }

            Assert.That(arrival, Is.GreaterThanOrEqualTo(0),
                "after the blocker leaves, the waiting TroopMovetoCell mission must resume and arrive");
            Console.Out.WriteLine("[m3c] enemy-held target: mission waited 5 turns, resumed, arrived after " +
                                  (arrival + 1) + " more turns");
        }

        static bool CitiesAreEnemies(Assembly sim, object a, object b)
        {
            Type buildingType = sim.GetType("Sango.Core.BuildingBase", throwOnError: true)!;
            MethodInfo method = a.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Single(m => m.Name == "IsEnemy" && m.GetParameters().Length == 1 &&
                             m.GetParameters()[0].ParameterType == buildingType);
            return (bool)method.Invoke(a, new[] { b })!;
        }
    }
}
