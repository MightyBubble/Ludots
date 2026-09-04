using System.Text;
using Ludots.Core.Modding;
using NUnit.Framework;
using Sango.Core;
using Sango.Runtime;
using Sango.WebUi;

namespace Ludots.Tests.SangoWebUi;

/// <summary>
/// M3.e 任务二:事件系统加载器验收(DiplomacyEvent 数据表驱动面 + ScenarioEvent 表驱动面)。
/// 逆向基线(sango-src,以源码为准):
///   · 上游 Ships 18 张 Data/DiplomacyEvent 表与 3 张 Data/ScenarioEvent 表,但全仓库
///     无任何加载器(DiplomacyEventManager.Init 整体注释);表 1–5 与注释里的硬编码
///     五条逐一对应,是同一设计的数据化扩展。M3.e 落地 = 表装载 + 原注释触发面
///     (OnTurnStart 全存活势力对扫描,关系区间 + GameRandom.Chance(Probability),
///     每回合上限 3 次)。
///   · 搜索演出(CityPersonSearchingEvent)在原版用硬编码台词;表 10/11 的
///     formatContent 与其逐字同源。移植取舍:发现语义单源 = 内核 DoJobSearching
///     (发现 → Unemployed + wildPersons + 玩家消息行),表侧只供演出文本——
///     11 组台词不镜像入消息流(内核发现行已覆盖),10 组(搜索失败,内核无消息)
///     镜像入消息流作为失败面的承载。
/// 覆盖(全部 headless,真实 Scenario.json 内核):
///   1. DiplomacyEvent:两类效果型(AddRelation +50 / ReduceRelation -100)端到端
///      触发——消息流入 feed、RelationMap 逐对值变化与消息 1:1、每回合上限 3;
///      同种子双跑 digest 与逐回合事件行序列逐位一致;
///   2. ScenarioEvent:军师慰问(1 组)随玩家回合开始入消息流(表文本 + 变量解析);
///      搜索失败(10 组)表文本入消息流;搜索到人才(11 组)走内核发现链且不双消息;
///   3. 存档面:事件效果只落 RelationMap([JsonProperty] 入档)与消息流(展示面);
///     _eventTriggerCount 是回合内瞬态(回合开始重置;中途回灌由 Start 恢复分支的
///     HasTurnStarted=true 挡住不重演)。玩家回合中存档与回合边界存档各回灌一条,
///     续跑 digest + 关系指纹与直跑链一致。
/// </summary>
[TestFixture]
public sealed class SangoEventTests
{
    private const int Seed = 20260905;
    private const int DiplomacyWindowTurns = 8;
    private const int SearchAttemptBudget = 40;

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

    // ApplyEffect 的全部消息形态(表 1–18 覆盖):关系增减/复合型/结盟/停战。
    private static bool IsDiplomacyEventLine(string text) =>
        text.Contains("关系增加了") || text.Contains("关系减少了") || text.Contains("[外交事件]") ||
        text.Contains("结成同盟！") || (text.Contains("请求与") && text.Contains("停战！"));

    private sealed class TurnRecord
    {
        public int Turn;
        public bool MonthCrossed;
        public readonly List<string> EventLines = new();
        public readonly List<(int A, int B, int Delta)> PairDeltas = new();
    }

    // 月度关系漂移(DiplomacyManager,diplomacyMonthlyNormalRelationDecrease=100)也改写
    // RelationMap 全片,幅度与 ReduceRelation 事件同量级;跨月回合单独标记,不参与
    // "事件行数 == 关系变化对数"的 1:1 断言与 ±50/±100 值指纹断言。
    private static int[][] CloneRelations(Scenario scenario)
    {
        var clone = new int[scenario.RelationMap.Length][];
        for (int i = 0; i < clone.Length; i++)
        {
            clone[i] = (int[])scenario.RelationMap[i].Clone();
        }

        return clone;
    }

    // Add/ReduceRelation 对称写 [a][b] 与 [b][a];只数上三角,一行事件 == 一对变化。
    private static List<(int A, int B, int Delta)> RelationDeltas(int[][] before, int[][] after)
    {
        var deltas = new List<(int A, int B, int Delta)>();
        for (int i = 0; i < before.Length; i++)
        {
            for (int j = i + 1; j < before[i].Length; j++)
            {
                if (after[i][j] != before[i][j])
                {
                    deltas.Add((i, j, after[i][j] - before[i][j]));
                }
            }
        }

        return deltas;
    }

    private static string RelationFingerprint(Scenario scenario)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < scenario.RelationMap.Length; i++)
        {
            for (int j = i + 1; j < scenario.RelationMap[i].Length; j++)
            {
                builder.Append(i).Append(':').Append(j).Append('=').Append(scenario.RelationMap[i][j]).Append('\n');
            }
        }

        return builder.ToString();
    }

    private static long LastSeq(SangoWorldFeed feed)
    {
        SangoMessageRow[] rows = feed.SnapshotMessages();
        return rows.Length == 0 ? 0 : rows[^1].Seq;
    }

    private static List<SangoMessageRow> RowsSince(SangoWorldFeed feed, long seq)
    {
        return feed.SnapshotMessages().Where(row => row.Seq > seq).ToList();
    }

    // ---- DiplomacyEvent:端到端 + 上限 + 双跑确定性 ----

    [Test]
    public void DiplomacyEvents_TableDriven_TwoEffectTypes_CapThree_DeterministicDoubleRun()
    {
        (string Digest, string EventSequence) RunWorld()
        {
            SangoKernelBoot.Boot(NewVfs(), "SangoContentMod", Seed);
            Scenario scenario = Scenario.Cur!;
            var feed = new SangoWorldFeed();
            feed.AttachPlayerMessageSystem();

            var turns = new List<TurnRecord>();
            for (int step = 0; step < DiplomacyWindowTurns; step++)
            {
                int[][] before = CloneRelations(scenario);
                int monthBefore = scenario.Info.month;
                long seqBefore = LastSeq(feed);
                SangoTurnDriver.AdvanceTurn();

                var record = new TurnRecord
                {
                    Turn = scenario.Info.turnCount,
                    MonthCrossed = scenario.Info.month != monthBefore,
                };
                record.EventLines.AddRange(RowsSince(feed, seqBefore)
                    .Select(row => row.Text)
                    .Where(IsDiplomacyEventLine));
                record.PairDeltas.AddRange(RelationDeltas(before, CloneRelations(scenario)));
                turns.Add(record);
            }

            // 上限 3(所有回合,含跨月回合)与上限真的被踩到。
            Assert.That(turns.All(turn => turn.EventLines.Count <= 3), Is.True,
                "the per-turn diplomacy event cap (3) must hold on every turn");
            Assert.That(turns.Max(turn => turn.EventLines.Count), Is.EqualTo(3),
                "the sweep must actually reach the per-turn cap inside the window (hundreds of alive pairs at boot)");

            // 非跨月回合:关系变化对数 == 事件行数(每条效果行恰改一对关系);
            // 跨月回合只断言上限(月度漂移整片改写 RelationMap)。
            foreach (TurnRecord turn in turns.Where(turn => !turn.MonthCrossed))
            {
                Assert.That(turn.PairDeltas.Count, Is.EqualTo(turn.EventLines.Count),
                    $"turn {turn.Turn}: every relation-changing event line must map to exactly one changed pair " +
                    "(AI diplomacy and envoy chains are inactive upstream; monthly drift is excluded)");
            }

            // 两类效果型端到端:消息形态 + 表参数值(RelationValue 50/100)在 RelationMap 落地。
            Assert.That(turns.SelectMany(turn => turn.EventLines).Any(text => text.Contains("派遣使者访问")),
                Is.True, "the AddRelation effect (table 1) must reach the message feed");
            Assert.That(turns.SelectMany(turn => turn.EventLines).Any(text => text.Contains("边境发生了冲突")),
                Is.True, "the ReduceRelation effect (table 2) must reach the message feed");
            Assert.That(turns.Any(turn => !turn.MonthCrossed && turn.EventLines.Count > 0 &&
                    turn.PairDeltas.Any(delta => delta.Delta == 50)),
                Is.True, "an AddRelation hit must land exactly +50 (table EffectParams Value) on some pair");
            Assert.That(turns.Any(turn => !turn.MonthCrossed && turn.EventLines.Count > 0 &&
                    turn.PairDeltas.Any(delta => delta.Delta == -100)),
                Is.True, "a ReduceRelation hit must land exactly -100 (table EffectParams Value) on some pair");

            string sequence = string.Join("|",
                turns.Select(turn => string.Join(";", turn.EventLines.OrderBy(text => text, StringComparer.Ordinal))));
            TestContext.Progress.WriteLine(
                $"[m3e-event] diplomacy window: turns={turns.Count} eventLines={turns.Sum(turn => turn.EventLines.Count)} maxPerTurn={turns.Max(turn => turn.EventLines.Count)} sample={turns.First(turn => turn.EventLines.Count > 0).EventLines[0]}");
            return (SangoTurnDriver.WorldDigest(), sequence);
        }

        (string digestA, string sequenceA) = RunWorld();
        (string digestB, string sequenceB) = RunWorld();
        Assert.That(sequenceA, Is.EqualTo(sequenceB),
            "the same seed must emit the identical per-turn diplomacy event line sequence");
        Assert.That(digestA, Is.EqualTo(digestB), "same-seed worlds must replay bit for bit with events active");
        TestContext.Progress.WriteLine($"[m3e-event] digest={digestA}");
    }

    // ---- ScenarioEvent:1 组军师慰问 + 10/11 组搜索 ----

    [Test]
    public void ScenarioEvents_CounsellorGreeting_And_SearchTables_EndToEnd()
    {
        (string Digest, string Greeting) RunWorld()
        {
            int playerForceId = FirstPlayableForceId();
            SangoKernelBoot.BootWithPlayer(NewVfs(), "SangoContentMod", Seed, playerForceId, "Scenario/Scenario.json");
            Scenario scenario = Scenario.Cur!;
            var feed = new SangoWorldFeed();
            feed.AttachPlayerMessageSystem();

            // 装载面:boot 后三组表在册(1 军师慰问 / 10 搜索失败 / 11 搜索到人才)。
            Assert.That(ScenerioEventManager.Instance.eventGroups.Keys,
                Is.EqualTo(new[] { 1, 10, 11 }), "the boot line must load every ScenarioEvent group");
            Assert.That(ScenerioEventManager.Instance.GetGroup(1)[0].eventType, Is.EqualTo("PersonTalk"));

            Assert.That(SangoTurnDriver.AdvanceTurn(), Is.EqualTo(SangoTurnDriver.TurnAdvanceResult.AwaitingPlayer));
            Force player = scenario.forceSet.Get(SangoPlayerTurnOps.PlayerForceId(scenario))!;
            string expectedGreeting = player.Name + "大人，\n终于轮到我们了啊。";
            Assert.That(feed.SnapshotMessages().Any(row => row.Text == expectedGreeting),
                Is.True, "group 1 (counsellor greeting) must reach the message feed with {:ActionForce} resolved");

            // 搜索链:同城反复下令探索,直到失败面(10 组镜像)与发现面(内核发现行)都出现。
            // 消息行按回合窗口即取即数(feed 是 50 行环,回灌前旧行会被冲走)。
            City city = SearchCapableCity(scenario, player);
            bool failureSeen = false;
            bool dialogMirrorLeaked = false;
            Person? discovered = null;
            int discoveredLineCount = 0;
            for (int attempt = 0; attempt < SearchAttemptBudget && !(failureSeen && discovered != null); attempt++)
            {
                Person executor = city.freePersons.Where(person => person != null)
                    .OrderByDescending(person => person.Politics).First();
                var order = SangoCityOps.Execute(scenario, city, "search", new[] { executor.Id });
                Assert.That(order.Succeeded, Is.True, $"search order must pass the original gates: {order.Message}");

                var wildBefore = new HashSet<int>(city.wildPersons.Select(person => person.Id));
                long seqBefore = LastSeq(feed);
                SangoPlayerTurnOps.EndPlayerTurn();
                SangoTurnDriver.AdvanceTurn();
                foreach (SangoMessageRow row in RowsSince(feed, seqBefore))
                {
                    if (row.Text == "很遗憾, 什么都没有发现...")
                    {
                        failureSeen = true;
                    }

                    if (row.Text.Contains("搜索结果，"))
                    {
                        dialogMirrorLeaked = true;
                    }

                    if (discovered == null && row.Text.Contains($"在{city.ColorName}发现人才"))
                    {
                        Person? found = city.wildPersons
                            .FirstOrDefault(person => person != null && !wildBefore.Contains(person.Id));
                        if (found != null && row.Text.Contains($"发现人才{found.ColorName}。"))
                        {
                            discovered = found;
                            discoveredLineCount = 1;
                        }
                    }
                    else if (discovered != null && row.Text.Contains($"发现人才{discovered.ColorName}。"))
                    {
                        discoveredLineCount++;
                    }
                }
            }

            Assert.That(failureSeen, Is.True,
                "group 10 (search failure) table text must reach the message feed (mirror face for the player dialog)");
            Assert.That(discovered, Is.Not.Null,
                "the kernel discovery chain must fire within the attempt budget");
            Assert.That(discoveredLineCount, Is.EqualTo(1),
                "exactly one discovery line per found person: the group 11 dialog text stays dialog-side, no double trigger");
            Assert.That(dialogMirrorLeaked, Is.False,
                "the group 11 dialog text must not be mirrored into the feed (kernel line is the single source)");

            // 11 组内容保真:表 formatContent + 变量解析与原硬编码串逐字同源。
            string formatted = ScenerioEventManager.Instance.GetGroup(11)[0].FormatContent(new ScenarioEventData
            {
                TargetPerson = discovered!,
            });
            Assert.That(formatted, Is.EqualTo($"搜索结果，\n发现了名为{discovered!.Name}的武将。"));

            TestContext.Progress.WriteLine(
                $"[m3e-event] scenario faces: greeting='{expectedGreeting.Replace("\n", "\\n")}' failureSeen={failureSeen} discovered={discovered.Name} ({discovered.Id})");
            return (SangoTurnDriver.WorldDigest(), expectedGreeting);
        }

        (string digestA, string greetingA) = RunWorld();
        (string digestB, string greetingB) = RunWorld();
        Assert.That(greetingA, Is.EqualTo(greetingB));
        Assert.That(digestA, Is.EqualTo(digestB), "same-seed scenario-event worlds must replay bit for bit");
        TestContext.Progress.WriteLine($"[m3e-event] digest={digestA}");
    }

    // ---- 存档面:事件状态跨存档边界 ----

    [Test]
    public void EventState_AcrossSaveRestore_PlayerMidTurn_And_TurnBoundary()
    {
        // 面 1:玩家回合中(AwaitingPlayer)存档——M3.a 恢复分支(Start 按 curForceId
        // 静默 drain,HasTurnStarted=true)必须挡住 TurnStart 事件扫描与军师慰问的
        // 重演;续跑 digest + 关系指纹与直跑链一致。
        (string Digest, string Relations, int Greetings) PlayerChain(bool withMidSave)
        {
            int playerForceId = FirstPlayableForceId();
            SangoKernelBoot.BootWithPlayer(NewVfs(), "SangoContentMod", Seed, playerForceId, "Scenario/Scenario.json");
            Scenario scenario = Scenario.Cur!;
            var feed = new SangoWorldFeed();
            feed.AttachPlayerMessageSystem();
            string greeting = scenario.forceSet.Get(SangoPlayerTurnOps.PlayerForceId(scenario))!.Name +
                "大人，\n终于轮到我们了啊。";
            long seq = LastSeq(feed);
            Assert.That(SangoTurnDriver.AdvanceTurn(), Is.EqualTo(SangoTurnDriver.TurnAdvanceResult.AwaitingPlayer));
            Assert.That(RowsSince(feed, seq).Count(row => row.Text == greeting), Is.EqualTo(1),
                "the greeting fires exactly once at the first player turn start");

            if (withMidSave)
            {
                string relationsAtCapture = RelationFingerprint(scenario);
                var participant = new SangoSaveParticipant(NewVfs(), "SangoContentMod");
                participant.RestoreState(participant.CaptureState());
                scenario = Scenario.Cur!;
                Assert.That(RelationFingerprint(scenario), Is.EqualTo(relationsAtCapture),
                    "RelationMap (event-landed relation values) must survive the save round-trip");

                // 回灌后世界仍在玩家回合中(Start 恢复分支按 curForceId 静默 drain,
                // HasTurnStarted=true):一次 AdvanceTurn 重新抵达玩家阻塞位,期间
                // TurnStart 事件扫描与军师慰问都不得重演。
                seq = LastSeq(feed);
                Assert.That(SangoTurnDriver.AdvanceTurn(), Is.EqualTo(SangoTurnDriver.TurnAdvanceResult.AwaitingPlayer),
                    "the restored world must re-block at the player turn it was saved in");
                Assert.That(RowsSince(feed, seq).Any(row => row.Text == greeting), Is.False,
                    "restore must not re-fire the turn-start face (no second greeting without a new turn)");
            }

            int greetings = 1;
            for (int turn = 0; turn < 2; turn++)
            {
                seq = LastSeq(feed);
                SangoPlayerTurnOps.EndPlayerTurn();
                Assert.That(SangoTurnDriver.AdvanceTurn(), Is.EqualTo(SangoTurnDriver.TurnAdvanceResult.AwaitingPlayer));
                int newGreetings = RowsSince(feed, seq).Count(row => row.Text == greeting);
                Assert.That(newGreetings, Is.EqualTo(1),
                    $"player turn {turn + 2} must greet exactly once (group 1 fires per player turn start)");
                greetings += newGreetings;
            }

            return (SangoTurnDriver.WorldDigest(), RelationFingerprint(scenario), greetings);
        }

        (string digestDirect, string relationsDirect, int greetingsDirect) = PlayerChain(withMidSave: false);
        (string digestSaved, string relationsSaved, int greetingsSaved) = PlayerChain(withMidSave: true);
        Assert.That(greetingsSaved, Is.EqualTo(greetingsDirect),
            "the saved chain must greet exactly as often as the direct chain");
        Assert.That(relationsSaved, Is.EqualTo(relationsDirect),
            "event-driven relation state must continue identically across the mid-player-turn save");
        Assert.That(digestSaved, Is.EqualTo(digestDirect), "mid-player-turn save must replay bit for bit with events active");

        // 面 2:回合边界存档(全托管世界)——下一回合的事件扫描两侧各触发一次,续跑一致。
        string HeadlessChain(bool withMidSave)
        {
            SangoKernelBoot.Boot(NewVfs(), "SangoContentMod", Seed);
            for (int turn = 0; turn < 3; turn++)
            {
                SangoTurnDriver.AdvanceTurn();
            }

            if (withMidSave)
            {
                var participant = new SangoSaveParticipant(NewVfs(), "SangoContentMod");
                participant.RestoreState(participant.CaptureState());
            }

            for (int turn = 0; turn < 2; turn++)
            {
                SangoTurnDriver.AdvanceTurn();
            }

            return SangoTurnDriver.WorldDigest() + "|" + RelationFingerprint(Scenario.Cur!);
        }

        Assert.That(HeadlessChain(withMidSave: true), Is.EqualTo(HeadlessChain(withMidSave: false)),
            "turn-boundary save must continue the event-active world identically to the direct chain");
    }

    private static int FirstPlayableForceId()
    {
        BootResult probe = SangoKernelBoot.Boot(NewVfs(), "SangoContentMod", Seed);
        foreach (Force? force in probe.Scenario.forceSet)
        {
            if (force != null && force.IsAlive && force.Id > 0 && force.mCounsellor != null)
            {
                return force.Id;
            }
        }

        throw new InvalidOperationException("the scenario has no alive force with a counsellor to play");
    }

    private static City SearchCapableCity(Scenario scenario, Force player)
    {
        City? best = null;
        int bestInvisible = 0;
        scenario.citySet.ForEach(city =>
        {
            if (city == null || city.mBelongForce != player || city.mBelongCorps == null ||
                city.mBelongCorps.ActionPoint < 30 || city.freePersons.Count == 0)
            {
                return;
            }

            if (city.invisiblePersons.Count > bestInvisible)
            {
                bestInvisible = city.invisiblePersons.Count;
                best = city;
            }
        });
        return best ?? throw new InvalidOperationException(
            "the player force must own a search-capable city with undiscovered persons at boot");
    }
}
