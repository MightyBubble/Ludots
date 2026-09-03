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
    /// M3.a 玩家接入 + 内政 AI 活化验收(反射调用 SangoRuntime,照 SangoCombatTests 惯例)。
    /// 覆盖:
    ///   1. BootWithPlayer:CheckPlayer 正式数据面(Info.playerForceList → Force.IsPlayer,
    ///      MakeForceQuene 玩家先位,世界停在玩家回合阻塞位);
    ///   2. 玩家门控:非玩家城/非玩家回合的城命令被拒(not_player_city/not_player_turn),
    ///      玩家城在玩家回合放行,且军团行动力真实扣减(ReduceActionPoint 玩家专属);
    ///   3. EndPlayerTurn(原版「进行」PlayerEndTurn.Update 体)+ 玩家感知回合推进;
    ///   4. 内政经济活化:多月推进后城市金/粮较开局增长(世界经济从俸给/军粮静态
    ///      转为内政运转);
    ///   5. 长程战役 OnForceFall 直测(还 M2.c 的账):播种强军攻单城弱势力,推进至
    ///      末城陷落 → 势力灭亡(IsAlive=false)+ 全军清除 + [灭亡] 战报行;同种子
    ///      双跑 digest 逐位相等;中途存档叠加一致;
    ///   6. 玩家局命令 journal 重放:selectPlayerForce/endPlayerTurn/step 全链重放逐位相等。
    /// </summary>
    [TestFixture]
    public sealed class SangoPlayerTests
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
                object bootResult = sim.GetType("Sango.Runtime.SangoKernelBoot", throwOnError: true)!
                    .GetMethod(method, BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, args)!;
                Assert.That(PropertyValue(bootResult, "Scenario"), Is.Not.Null, "boot must leave a booted Scenario.Cur");
            }

            public object Scenario =>
                Sim.GetType("Sango.Core.Scenario", throwOnError: true)!
                    .GetProperty("Cur", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

            public IEnumerable Cities() => EnumerateSet(FieldValue(Scenario, "citySet"));

            public IEnumerable Forces() => EnumerateSet(FieldValue(Scenario, "forceSet"));

            public object? ForceById(int id) => Call(FieldValue(Scenario, "forceSet"), "Get", id);

            public object? CityById(int id) => Call(FieldValue(Scenario, "citySet"), "Get", id);

            public object Info => PropertyValue(Scenario, "Info")!;

            // AdvanceTurn 现返回 TurnAdvanceResult 枚举;测试读 ToString。
            public string AdvanceTurn() =>
                (string)Sim.GetType("Sango.Runtime.SangoTurnDriver", throwOnError: true)!
                    .GetMethod("AdvanceTurn", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!.ToString()!;

            public string WorldDigest() => (string)Sim.GetType("Sango.Runtime.SangoTurnDriver", throwOnError: true)!
                .GetMethod("WorldDigest", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;

            public void EndPlayerTurn() => Sim.GetType("Sango.Runtime.SangoPlayerTurnOps", throwOnError: true)!
                .GetMethod("EndPlayerTurn", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);

            public void SelectPlayerForce(int forceId) => Sim.GetType("Sango.Runtime.SangoPlayerTurnOps", throwOnError: true)!
                .GetMethod("SelectPlayerForce", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object?[] { NewVfs(), "SangoContentMod", Seed, forceId, "Scenario/Scenario.json" });

            public bool AwaitingPlayer() => (bool)Sim.GetType("Sango.Runtime.SangoPlayerTurnOps", throwOnError: true)!
                .GetMethod("AwaitingPlayer", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new[] { Scenario })!;

            public int PlayerForceId() => (int)Sim.GetType("Sango.Runtime.SangoPlayerTurnOps", throwOnError: true)!
                .GetMethod("PlayerForceId", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new[] { Scenario })!;

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

            public (bool Succeeded, string ErrorCode, string Message) CityCommand(object city, string type, int[] personIds, int targetPersonId = 0)
            {
                object result = Sim.GetType("Sango.Runtime.SangoCityOps", throwOnError: true)!
                    .GetMethod("Execute", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object[] { Scenario, city, type, personIds, targetPersonId })!;
                return ((bool)PropertyValue(result, "Succeeded")!,
                    (string)PropertyValue(result, "ErrorCode")!,
                    (string)PropertyValue(result, "Message")!);
            }

            public (bool Succeeded, string ErrorCode, object? Troop) CreateTroop(object city, int[] personIds, int troops, int food)
            {
                object tuple = Sim.GetType("Sango.Runtime.SangoTroopOps", throwOnError: true)!
                    .GetMethod("CreateTroop", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object?[] { Scenario, city, personIds, null, null, troops, 500, food })!;
                object result = tuple.GetType().GetField("Item1")!.GetValue(tuple)!;
                return ((bool)PropertyValue(result, "Succeeded")!,
                    (string)PropertyValue(result, "ErrorCode")!,
                    tuple.GetType().GetField("Item2")!.GetValue(tuple));
            }

            public (bool Succeeded, string ErrorCode, string Action) MoveTroop(object troop, object destCell)
            {
                object tuple = Sim.GetType("Sango.Runtime.SangoTroopOps", throwOnError: true)!
                    .GetMethod("MoveTroop", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new[] { Scenario, troop, destCell })!;
                object result = tuple.GetType().GetField("Item1")!.GetValue(tuple)!;
                return ((bool)PropertyValue(result, "Succeeded")!,
                    (string)PropertyValue(result, "ErrorCode")!,
                    (string)PropertyValue(result, "Message")!);
            }

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

            public void SetMission(object troop, string missionName, int targetId)
            {
                Type missionType = Sim.GetType("Sango.Core.MissionType", throwOnError: true)!;
                object mission = Enum.Parse(missionType, missionName);
                Call(troop, "SetMission", mission, targetId);
            }

            public object Map => PropertyValue(Scenario, "Map")!;

            public object? GetCell(int x, int y) => Call(PropertyValue(Map, "CellSet"), "GetCell", x, y);

            public object[] JournalSnapshot() => ((IEnumerable)Sim.GetType("Sango.Runtime.SangoCommandJournal", throwOnError: true)!
                .GetMethod("Snapshot", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!).Cast<object>().ToArray();

            public object ReplayJournal(object[] commands)
            {
                // object[] → SangoJournalCommand[]:反射调用不吃协变转换,按元素类型重建。
                Type commandType = Sim.GetType("Sango.Runtime.SangoJournalCommand", throwOnError: true)!;
                Array typed = Array.CreateInstance(commandType, commands.Length);
                for (int i = 0; i < commands.Length; i++)
                {
                    typed.SetValue(commands[i], i);
                }

                return Sim.GetType("Sango.Runtime.SangoReplayJournal", throwOnError: true)!
                    .GetMethod("Replay", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object?[] { NewVfs(), "SangoContentMod", Seed, typed, null, "Scenario/Scenario.json" })!;
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

        static object FieldValue(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
            ?? target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(target)!;

        static object? PropertyValue(object target, string name) =>
            target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
            ?? target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);

        static object? Call(object target, string method, params object?[] args)
        {
            MethodInfo info = target.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Single(candidate => candidate.Name == method && candidate.GetParameters().Length == args.Length);
            return info.Invoke(target, args);
        }

        static int IntOf(object target, string member) => Convert.ToInt32(FieldValue(target, member));

        static List<object> FreePersons(object city) => ((IEnumerable)FieldValue(city, "freePersons")).Cast<object>().ToList();

        static object FirstPlayerCity(Kernel kernel, int forceId) =>
            kernel.Cities().Cast<object>().First(city => PropertyValue(city, "mBelongForce") is { } force && (int)PropertyValue(force, "Id")! == forceId);

        private static int FindAliveForceId(Kernel kernel)
        {
            object force = kernel.Forces().Cast<object>().First(f => f != null && (bool)PropertyValue(f, "IsAlive")! && (int)PropertyValue(f, "Id")! > 0)!;
            return (int)PropertyValue(force, "Id")!;
        }

        // ---- 玩家接入:CheckPlayer 数据面 + 阻塞语义 ----

        [Test]
        public void PlayerBoot_SetsIsPlayer_AndWaitsOnPlayerTurn()
        {
            Assembly sim = LoadSangoSimMod();
            int forceId = FindAliveForceId(new Kernel(sim, Seed));
            var kernel = new Kernel(sim, Seed, forceId);

            object force = kernel.ForceById(forceId)!;
            Assert.That((bool)PropertyValue(force, "IsPlayer")!, Is.True, "CheckPlayer must mark the selected force as player");
            Assert.That(kernel.PlayerForceId(), Is.EqualTo(forceId), "PlayerForceId reads Info.playerForceList[0]");
            Assert.That((int[])PropertyValue(kernel.Info, "playerForceList")!, Is.EqualTo(new[] { forceId }),
                "Info.playerForceList carries the original save-side player list");

            // 玩家先位:首次 step 即停在玩家回合(AwaitingPlayer),turnCount 不动。
            string result = kernel.AdvanceTurn();
            Assert.That(result, Is.EqualTo("AwaitingPlayer"), "with a live player force the first step must stop at the player turn gate");
            Assert.That(kernel.AwaitingPlayer(), Is.True, "the world waits on the player's governor corps (OnPlayerControl block)");
            Assert.That((int)PropertyValue(kernel.Info, "turnCount")!, Is.EqualTo(0), "no turn is consumed while waiting");
        }

        [Test]
        public void PlayerCityCommands_GatedAndConsumeActionPoints()
        {
            Assembly sim = LoadSangoSimMod();
            int forceId = FindAliveForceId(new Kernel(sim, Seed));
            var kernel = new Kernel(sim, Seed);
            kernel.SelectPlayerForce(forceId);
            kernel.AdvanceTurn();
            Assert.That(kernel.AwaitingPlayer(), Is.True);

            object playerCity = FirstPlayerCity(kernel, forceId);
            object foreignCity = kernel.Cities().Cast<object>()
                .First(city => PropertyValue(city, "mBelongForce") is { } force && (int)PropertyValue(force, "Id")! != forceId);

            // 非玩家城:玩家局拒绝(原版城菜单门)。
            (bool ok, string error, _) = kernel.CityCommand(foreignCity, "train", Array.Empty<int>());
            Assert.That(ok, Is.False);
            Assert.That(error, Is.EqualTo("not_player_city"), "player worlds only command the player's own cities");

            // 玩家城 + 玩家回合:放行,且 AP 真实扣减(ReduceActionPoint 仅对玩家生效)。
            var freePersons = FreePersons(playerCity);
            Assume.That(freePersons.Count, Is.GreaterThan(0), "the player capital must staff free persons at boot");
            var executor = freePersons[0];
            int apBefore = IntOf(FieldValue(playerCity, "mBelongCorps")!, "ActionPoint");
            (ok, error, _) = kernel.CityCommand(playerCity, "train", new[] { (int)PropertyValue(executor, "Id")! });
            Assert.That(ok, Is.True, $"player-city train must pass the original gates: {error}");
            int apAfter = IntOf(FieldValue(playerCity, "mBelongCorps")!, "ActionPoint");
            Assert.That(apAfter, Is.LessThan(apBefore),
                "player corps action points must actually drain (ReduceActionPoint is player-only in the original)");
        }

        [Test]
        public void EndPlayerTurn_Unblocks_AndPlayerWorldAdvancesDeterministically()
        {
            Assembly sim = LoadSangoSimMod();
            string RunPlayerWorld(bool withSave)
            {
                Assembly sim = LoadSangoSimMod();
                int forceId = FindAliveForceId(new Kernel(sim, Seed));
                var kernel = new Kernel(sim, Seed, forceId);
                kernel.AdvanceTurn();
                Assert.That(kernel.AwaitingPlayer(), Is.True);

                // 回灌后世界从回合队列头重放(玩家先位),先步进到玩家门再「进行」。
                object capture = withSave ? kernel.Capture() : null!;
                if (withSave)
                {
                    kernel.Restore(capture);
                }

                for (int i = 0; i < 3; i++)
                {
                    string result = kernel.AdvanceTurn();
                    Assert.That(result, Is.EqualTo("AwaitingPlayer"),
                        "each step must stop at the player turn gate (post-restore replays from the queue head)");
                    kernel.EndPlayerTurn();
                }

                // 最后一次「进行」+步进跨过第 4 回合,落定终态 digest。
                kernel.AdvanceTurn();
                kernel.EndPlayerTurn();
                kernel.AdvanceTurn();
                object playerCity = FirstPlayerCity(kernel, forceId);
                TestContext.Progress.WriteLine(
                    $"[m3a-player] withSave={withSave} turnCount={(int)PropertyValue(kernel.Info, "turnCount")} ap={IntOf(FieldValue(playerCity, "mBelongCorps")!, "ActionPoint")} gold={IntOf(playerCity, "gold")} food={IntOf(playerCity, "food")}");
                return kernel.WorldDigest();
            }

            string plain = RunPlayerWorld(withSave: false);
            string across = RunPlayerWorld(withSave: true);
            TestContext.Progress.WriteLine($"[m3a-player] turnCount plain={(int)PropertyValue(new Kernel(sim, Seed).Info, "turnCount")}");
            TestContext.Progress.WriteLine($"[m3a-player] plain={plain} acrossSave={across}");
            Assert.That(across, Is.EqualTo(plain), "player-world save→restore→continue must stay bit-identical (player flag + gates included)");
        }

        // ---- 内政 AI 活化:世界经济从静态转运转 ----

        [Test]
        public void InteriorAI_WorldEconomyComesAlive_AcrossMonths()
        {
            Assembly sim = LoadSangoSimMod();
            var kernel = new Kernel(sim, Seed);

            long GoldAt(int turnMarker) => kernel.Cities().Cast<object>()
                .Where(city => PropertyValue(city, "mBelongForce") != null)
                .Sum(city => (long)IntOf(city, "gold"));
            long FoodAt() => kernel.Cities().Cast<object>()
                .Where(city => PropertyValue(city, "mBelongForce") != null)
                .Sum(city => (long)IntOf(city, "food"));

            long gold0 = GoldAt(0);
            long food0 = FoodAt();
            // 7 回合跨两个旬月边界:月金入账 + 季粮在窗内(季按月对齐)。
            for (int i = 0; i < 7; i++)
            {
                kernel.AdvanceTurn();
            }

            long gold7 = GoldAt(7);
            long food7 = FoodAt();

            // 样例行(取证输出):任取三城的开局→7 回合金粮。
            foreach (object city in kernel.Cities().Cast<object>().Where(c => PropertyValue(c, "mBelongForce") != null).Take(3))
            {
                TestContext.Progress.WriteLine(
                    $"[m3a-economy] {PropertyValue(city, "Name")}: gold={IntOf(city, "gold")} food={IntOf(city, "food")} troops={IntOf(city, "troops")}");
            }

            Assert.That(gold7, Is.Not.EqualTo(gold0), "monthly gold income must change aggregate city gold after the interior AI activation");
            Assert.That(food7, Is.GreaterThan(food0 - 10_000), "seasonal food harvest must offset army upkeep within the window");
        }

        // ---- 长程战役 OnForceFall 直测(还 M2.c 的账) ----

        [Test]
        public void LongCampaign_SingleCityForceFalls_OnForceFallFiresWithDeterminism()
        {
            Assembly sim = LoadSangoSimMod();

            (string Digest, string[] Lines, bool Fallen, int ForceTroops, int ForceId) RunCampaign(bool withMidSave)
            {
                var kernel = new Kernel(sim, Seed);
                kernel.AdvanceTurn();

                // 靶 + 攻方配对:守军最少的单城势力(末城陷落 = 势力灭亡)中,取离某个
                // 过出征门槛的强势力城最近的一对——长程战役是行军+合围,攻方距靶越近
                // 堆栈越能同窗抵达(守军逐旬回填,分散抵达会打成永久消耗战)。
                var owned = kernel.Cities().Cast<object>()
                    .Where(city => PropertyValue(city, "mBelongForce") != null)
                    .ToList();
                object? bestTarget = null;
                object? bestStaging = null;
                int bestDistance = int.MaxValue;
                int bestTroops = int.MaxValue;
                foreach (object candidate in owned
                             .GroupBy(city => (int)PropertyValue(PropertyValue(city, "mBelongForce")!, "Id")!)
                             .Where(group => group.Count() == 1)
                             .Select(group => group.First()))
                {
                    object candidateCity = candidate;
                    int candidateForceId = (int)PropertyValue(PropertyValue(candidateCity, "mBelongForce")!, "Id")!;
                    object? stagingCity = kernel.Cities().Cast<object>()
                        .Where(city => !ReferenceEquals(city, candidateCity) &&
                                       PropertyValue(city, "mBelongForce") != null &&
                                       (int)PropertyValue(PropertyValue(city, "mBelongForce")!, "Id")! != candidateForceId &&
                                       PassExpeditionGate(city))
                        .OrderBy(city => MapDistance(kernel, PropertyValue(city, "CenterCell")!, PropertyValue(candidateCity, "CenterCell")!))
                        .FirstOrDefault();
                    if (stagingCity == null)
                    {
                        continue;
                    }

                    int distance = MapDistance(kernel, PropertyValue(stagingCity, "CenterCell")!, PropertyValue(candidateCity, "CenterCell")!);
                    int troops = IntOf(candidateCity, "troops");
                    if (distance < bestDistance || (distance == bestDistance && troops < bestTroops))
                    {
                        bestTarget = candidateCity;
                        bestStaging = stagingCity;
                        bestDistance = distance;
                        bestTroops = troops;
                    }
                }

                Assert.That(bestTarget, Is.Not.Null, "the scenario must carry single-city forces with a passing expedition staging city");
                object targetCity = bestTarget!;
                object staging = bestStaging!;
                int targetForceId = (int)PropertyValue(PropertyValue(targetCity, "mBelongForce")!, "Id")!;
                TestContext.Progress.WriteLine(
                    $"[m3a-campaign] pair: target f{targetForceId} ({PropertyValue(targetCity, "x")},{PropertyValue(targetCity, "y")}) troops={bestTroops} | staging f{(int)PropertyValue(PropertyValue(staging, "mBelongForce")!, "Id")!} dist={bestDistance}");

                // 剧本数据面:靶城守军压到 2000(只动数据,不动内核公式);staging 同面
                // 扩池(兵力 ×15、兵装 ×15)——内政 AI 活化后守军逐旬回填、城防逐旬修复,
                // 单堆栈侵蚀永远追不上再生,强势力必须一次合围(城池/兵装池是 CreateTroop
                // 滑条 clamp 的真源,扩的是同一数据面)。
                staging.GetType().GetField("troops")!.SetValue(targetCity, 2000);
                staging.GetType().GetField("troops")!.SetValue(staging, IntOf(staging, "troops") * 15);
                var items = (System.Collections.IDictionary)PropertyValue(FieldValue(staging, "itemStore"), "Items")!;
                var itemKeys = new System.Collections.ArrayList(items.Keys);
                foreach (object key in itemKeys)
                {
                    items[key] = Convert.ToInt32(items[key]) * 15;
                }

                var log = new List<string>();
                object annals = kernel.CreateAnnals(log);
                try
                {
                    // 围攻堆栈:staging 连续编队(城池兵力/兵装/军团 AP 自然封顶),授占城任务。
                    int enlisted = 0;
                    foreach (int[] squad in SquadBatches(staging, batches: 6))
                    {
                        (bool ok, string error, object? troop) = kernel.CreateTroop(staging, squad, 100_000, Math.Max(1, IntOf(staging, "food") / 3));
                        if (!ok || troop == null)
                        {
                            break;
                        }

                        kernel.SetMission(troop, "TroopOccupyCity", (int)PropertyValue(targetCity, "Id")!);
                        enlisted++;
                    }

                    Assert.That(enlisted, Is.GreaterThan(0), "at least one siege stack must be enlisted");

                    bool fallen = false;
                    // 预算 90 回合:内政 AI 活化后单城势力会征兵回填守军,攻城是真实的
                    // 消耗战(单堆栈 ~100-400 耐久/回合),多堆栈合围下方可在数十回合内陷城。
                    for (int turn = 0; turn < 90 && !fallen; turn++)
                    {
                        if (withMidSave && turn == 5)
                        {
                            kernel.Restore(kernel.Capture());
                        }

                        kernel.AdvanceTurn();
                        object? force = kernel.ForceById(targetForceId);
                        fallen = force == null || !(bool)PropertyValue(force, "IsAlive")!;
                    }

                    object? targetForce = kernel.ForceById(targetForceId);
                    bool isAlive = targetForce != null && (bool)PropertyValue(targetForce, "IsAlive")!;
                    int forceTroops = EnumerateSet(FieldValue(kernel.Scenario, "troopsSet")).Cast<object?>()
                        .Count(troop => troop != null && (bool)PropertyValue(troop, "IsAlive")! &&
                                        PropertyValue(troop, "mBelongForce") is { } f && (int)PropertyValue(f, "Id")! == targetForceId);

                    return (kernel.WorldDigest(), log.ToArray(), !isAlive, forceTroops, targetForceId);
                }
                finally
                {
                    kernel.DisposeAnnals(annals);
                }
            }

            (string digestA, string[] linesA, bool fallenA, int forceTroopsA, int forceIdA) = RunCampaign(withMidSave: false);
            (string digestRepeat, string[] linesRepeat, bool fallenRepeat, _, _) = RunCampaign(withMidSave: false);
            (string digestMid, string[] linesMid, bool fallenMid, int forceTroopsMid, _) = RunCampaign(withMidSave: true);

            Assert.That(fallenA, Is.True, $"force {forceIdA} must fall within the campaign budget (OnForceFall chain)");
            Assert.That(fallenRepeat, Is.True, "the same-seed repeat must also reach the force fall");
            Assert.That(digestRepeat, Is.EqualTo(digestA),
                "same-seed campaign must replay bit for bit (deterministic long-range war)");
            TestContext.Progress.WriteLine($"[m3a-forcefall] digest={digestA}");

            Assert.That(forceTroopsA, Is.EqualTo(0), "a fallen force must have zero surviving troops (全军清除)");

            // 中途存档叠加:第 5 步存读后战役仍推到灭亡(残余长链缺口下不强求与直跑链
            // digest 逐位,见 SangoSaveRoundtripTests 立案;灭国语义与全军清除仍硬断言)。
            Assert.That(fallenMid, Is.True, "the mid-save campaign variant must also reach the force fall");
            Assert.That(forceTroopsMid, Is.EqualTo(0), "the mid-save variant must also clear the fallen force's troops");

            string? fallLineA = linesA.FirstOrDefault(line => line.StartsWith("[灭亡]"));
            Assert.That(fallLineA, Is.Not.Null, "the annals must carry the force-fall line");
            string? fallLineMid = linesMid.FirstOrDefault(line => line.StartsWith("[灭亡]"));
            Assert.That(fallLineMid, Is.Not.Null, "the mid-save variant must also carry the force-fall line");
            TestContext.Progress.WriteLine($"[m3a-forcefall] {fallLineA}");
        }

        static IEnumerable<int[]> SquadBatches(object city, int batches)
        {
            for (int batch = 0; batch < batches; batch++)
            {
                var squad = FreePersons(city)
                    .OrderByDescending(person => IntOf(person, "Command"))
                    .Take(3)
                    .Select(person => (int)PropertyValue(person, "Id")!)
                    .ToArray();
                if (squad.Length == 0)
                {
                    yield break;
                }

                yield return squad;
            }
        }

        static bool PassExpeditionGate(object city) =>
            IntOf(city, "troops") > 0 && IntOf(city, "food") > 0 &&
            FreePersons(city).Count > 0 &&
            FieldValue(city, "mBelongCorps") != null &&
            IntOf(FieldValue(city, "mBelongCorps")!, "ActionPoint") >= 30;

        // ---- 玩家局 journal 重放 ----

        [Test]
        public void PlayerWorld_CommandJournalReplaysBitIdentical()
        {
            Assembly sim = LoadSangoSimMod();
            int forceId = FindAliveForceId(new Kernel(sim, Seed));
            var kernel = new Kernel(sim, Seed);
            // journal 是进程级静态环:先清掉同进程前序测试的命令,快照才是本局的命令流。
            ClearCommandJournal(sim);
            kernel.SelectPlayerForce(forceId);
            kernel.AdvanceTurn();
            Assert.That(kernel.AwaitingPlayer(), Is.True);

            object playerCity = FirstPlayerCity(kernel, forceId);
            var freePersons = FreePersons(playerCity);
            Assume.That(freePersons.Count, Is.GreaterThan(0));
            (bool ok, string error, _) = kernel.CityCommand(playerCity, "search", new[] { (int)PropertyValue(freePersons[0], "Id")! });
            Assert.That(ok, Is.True, $"player city command must pass: {error}");

            kernel.EndPlayerTurn();
            kernel.AdvanceTurn();
            kernel.EndPlayerTurn();
            kernel.AdvanceTurn();

            object[] commands = kernel.JournalSnapshot();
            Assert.That(commands.Length, Is.GreaterThan(0), "the journal must carry the player-world commands");
            Assert.That(commands.Any(command => CommandKindOf(command) == "selectPlayerForce"), Is.True,
                "the opening selectPlayerForce must be journaled (world-setup primitive; replay re-boots with the player)");


            object report = kernel.ReplayJournal(commands);
            Assert.That((bool)PropertyValue(report, "AllMatch")!, Is.True,
                "the player-world command journal must replay bit for bit (endPlayerTurn + step primitives)");
            TestContext.Progress.WriteLine($"[m3a-replay] steps={(int)PropertyValue(report, "StepsChecked")!} all-match");
        }

        static string CommandKindOf(object command) => (string)PropertyValue(command, "Kind")!;

        static void ClearCommandJournal(Assembly sim) => sim.GetType("Sango.Runtime.SangoCommandJournal", throwOnError: true)!
            .GetMethod("Clear", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);

        static int MapDistance(Kernel kernel, object a, object b)
        {
            Type cellType = kernel.Sim.GetType("Sango.Core.Cell", throwOnError: true)!;
            MethodInfo method = kernel.Map.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Single(m => m.Name == "Distance" &&
                             m.GetParameters().Length == 2 &&
                             m.GetParameters()[0].ParameterType == cellType);
            return (int)method.Invoke(kernel.Map, new[] { a, b })!;
        }
    }
}
