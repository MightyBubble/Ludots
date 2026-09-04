// M2.d 入口命令 journal:内核层等价的确定性回放录制(deterministic_replay 范式在
// 内核世界的落法——引擎 ReplayRecorder 面向 AuthoritativeFrame 输入动作流,本仓的
// sango 命令从 WebUI DataPlane 直达内核 op,不走引擎输入管线,故在内核层记"影响
// 模拟的入口命令",回放 = 同种子全新 Boot 后按序重放命令流,逐回合比对 WorldDigest)。
// 覆盖面(全部经唯一漏斗自动记录,无调用方自觉性依赖):
//   step        SangoTurnDriver.AdvanceTurn 的一切调用(web endTurn→TurnAdvanced、
//               SangoStepTurns、seed 事件内的推进、测试驱动),记录推进后回合号与 digest;
//   createTroop SangoTroopOps.CreateTroop(成功路径,含请求参数与编成产物 troopId);
//   moveTroop   SangoTroopOps.MoveTroop(成功路径,含委任分派 field-strike/occupation/
//               enter-city/building-fix;分派内部的 SetMission 由 moveTroop 记录承载);
//   cityCommand SangoCityOps 四型(train/search/reward/recruit,成功路径);
//   diplomacyCommand SangoDiplomacyOps 三型(alliance/sendGift/discardAlliance,成功路径;
//               M3.d 外交命令面);
//   researchCommand SangoTechniqueOps 玩家研究下单(成功路径;M3.e 科技命令面);
//   setMission  Entry 级直接授任务(seed 对阵;moveTroop 之外的显式入口)。
// 明确不入 journal:
//   sango.save      只读取证,不改世界;
//   sango.load      世界被 journal 因果链之外的存档改写,重放侧无法从命令序列导出;
//                   跨存档回放的验收变体由 SangoReplayJournal 的 midHook(捕获/回灌)覆盖;
//   内核 AI 自主决策(Force.Run/CorpsAI/TroopAI 的内部 SetMission 等)——它们是 step
//                   命令的确定性结果,不是入口命令;
//   SangoSeedTroops/SangoSeedBattle 整体——分解为上述原语记录,重放无需 seed 专属逻辑。

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Modding;
using Sango.Core;

namespace Sango.Runtime
{
    /// <summary>journal 单条命令:序号、当时回合号、命令类、参数 JSON、(step)推进后 digest。</summary>
    public sealed record SangoJournalCommand(long Seq, int Turn, string Kind, string ArgsJson, string? DigestAfter);

    /// <summary>createTroop 的参数面(SangoTroopOps.CreateTroop 同形,缺省项 null)。</summary>
    public sealed record SangoCreateTroopArgs(
        int CityId,
        int[] PersonIds,
        int? LandTroopTypeId,
        int? WaterTroopTypeId,
        int Troops,
        int Gold,
        int Food,
        int? TroopId);

    public sealed record SangoMoveTroopArgs(int TroopId, int X, int Y, string Action);

    public sealed record SangoSetMissionArgs(int TroopId, int MissionType, int TargetId);

    public sealed record SangoCityCommandArgs(string Type, int CityId, int[] PersonIds, int? TargetPersonId);

    /// <summary>diplomacyCommand 的参数面(SangoDiplomacyOps.Execute 同形)。</summary>
    public sealed record SangoDiplomacyCommandArgs(
        string Type,
        int CityId,
        int PersonId,
        int TargetForceId,
        int ResourceValue);

    /// <summary>
    /// researchCommand 的参数面(SangoTechniqueOps.Execute 同形)。personIds 空数组 =
    /// 军师自动推荐面(原版窗口默认),重放侧同序复现推荐结果。
    /// </summary>
    public sealed record SangoResearchCommandArgs(int TechniqueId, int CityId, int[] PersonIds);

    /// <summary>step 命令的空参数载荷(期望 digest 在记录的 DigestAfter 字段,不在参数里)。</summary>
    public sealed record SangoStepArgs
    {
        public static readonly SangoStepArgs Instance = new();
    }

    /// <summary>
    /// 进程级命令录制环(内核单世界,静态生命周期)。录制点全部在内核 op 成功路径,
    /// 读取(快照/导出)不参与模拟。重放期间 op 再次录制只是进环,不影响已导出的快照。
    /// </summary>
    public static class SangoCommandJournal
    {
        // 环容:15 回合战局剧本 ~20 条;长会话(百余回合 + 命令)远低于此,翻页即弃旧。
        public const int MaxEntries = 512;

        private static readonly object Sync = new();
        private static readonly Queue<SangoJournalCommand> Entries = new();
        private static long _seq;

        private static readonly JsonSerializerOptions ArgsOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>记录一条入口命令(仅命令成功后调用;turn 取 Scenario.Cur 当前回合)。</summary>
        public static void Record(string kind, object args, string? digestAfter = null)
        {
            if (string.IsNullOrEmpty(kind)) throw new ArgumentNullException(nameof(kind));
            ArgumentNullException.ThrowIfNull(args);
            Scenario scenario = Scenario.Cur
                ?? throw new InvalidOperationException("Sango kernel is not booted; journal recording requires Scenario.Cur.");

            SangoJournalCommand entry = new(
                System.Threading.Interlocked.Increment(ref _seq),
                scenario.Info.turnCount,
                kind,
                JsonSerializer.Serialize(args, args.GetType(), ArgsOptions),
                digestAfter);
            lock (Sync)
            {
                Entries.Enqueue(entry);
                while (Entries.Count > MaxEntries)
                {
                    Entries.Dequeue();
                }
            }
        }

        public static SangoJournalCommand[] Snapshot()
        {
            lock (Sync)
            {
                return Entries.ToArray();
            }
        }

        public static void Clear()
        {
            lock (Sync)
            {
                Entries.Clear();
            }
        }

        public static string ExportJson() => JsonSerializer.Serialize(Snapshot(), ArgsOptions);

        public static SangoJournalCommand[] ImportJson(string json)
        {
            return JsonSerializer.Deserialize<SangoJournalCommand[]>(json, ArgsOptions)
                ?? throw new InvalidOperationException("Sango command journal JSON is not a command array.");
        }
    }

    public sealed record SangoReplayStepCheck(long Seq, int Turn, string Expected, string Actual);

    /// <summary>一次重放的验收报告:应用命令数、比对步数、失配清单(空即逐位相等)。</summary>
    public sealed class SangoReplayReport
    {
        public int CommandsApplied { get; internal set; }
        public int StepsChecked { get; internal set; }
        public List<SangoReplayStepCheck> Mismatches { get; } = new();
        public List<string> TurnDigests { get; } = new();
        public bool AllMatch => Mismatches.Count == 0;
    }

    /// <summary>
    /// 重放器:同种子全新 Boot(SangoKernelBoot.Boot)后按序重放 journal 命令,每条 step
    /// 命令后比对 WorldDigest 与录制值。存档叠加变体:midHook 在指定命令前插入一次
    /// 捕获回灌(SangoSaveParticipant.Capture/Restore),等价实录链的中途读档。
    /// 命令失败 fail-fast 抛错(重放链与实录链在确定性下不允许分叉出"静默拒绝")。
    /// </summary>
    public static class SangoReplayJournal
    {
        public static SangoReplayReport Replay(
            IVirtualFileSystem vfs,
            string contentModId,
            int seed,
            IReadOnlyList<SangoJournalCommand> commands,
            Action<SangoJournalCommand>? midHook = null,
            string scenarioAssetPath = "Scenario/Scenario.json")
        {
            ArgumentNullException.ThrowIfNull(vfs);
            ArgumentNullException.ThrowIfNull(contentModId);
            ArgumentNullException.ThrowIfNull(commands);

            SangoKernelBoot.Boot(vfs, contentModId, seed, scenarioAssetPath);
            var report = new SangoReplayReport();
            foreach (SangoJournalCommand command in commands)
            {
                midHook?.Invoke(command);
                Apply(command, vfs, contentModId, scenarioAssetPath);
                report.CommandsApplied++;
                if (command.Kind == StepKind)
                {
                    if (command.DigestAfter == null)
                    {
                        throw new InvalidOperationException($"journal step #{command.Seq} lacks the recorded digest; cannot verify replay.");
                    }

                    string digest = SangoTurnDriver.WorldDigest();
                    report.StepsChecked++;
                    report.TurnDigests.Add(digest);
                    if (!string.Equals(digest, command.DigestAfter, StringComparison.Ordinal))
                    {
                        report.Mismatches.Add(new SangoReplayStepCheck(command.Seq, command.Turn, command.DigestAfter, digest));
                    }
                }
            }

            return report;
        }

        public const string StepKind = "step";
        public const string CreateTroopKind = "createTroop";
        public const string MoveTroopKind = "moveTroop";
        public const string SetMissionKind = "setMission";
        public const string CityCommandKind = "cityCommand";
        public const string DiplomacyCommandKind = "diplomacyCommand";
        public const string ResearchCommandKind = "researchCommand";
        // M3.a 玩家命令:开局选势力(world-setup,重放侧带玩家重装世界)与玩家回合
        // 「进行」(PlayerEndTurn 等价,见 SangoPlayerTurnOps)。
        public const string SelectPlayerForceKind = SangoPlayerTurnOps.SelectPlayerForceKind;
        public const string EndPlayerTurnKind = SangoPlayerTurnOps.EndPlayerTurnKind;

        static void Apply(SangoJournalCommand command, IVirtualFileSystem vfs, string contentModId, string scenarioAssetPath)
        {
            Scenario scenario = Scenario.Cur
                ?? throw new InvalidOperationException("Sango kernel is not booted; replay commands require Scenario.Cur.");
            switch (command.Kind)
            {
                case StepKind:
                    SangoTurnDriver.AdvanceTurn();
                    return;
                case CreateTroopKind:
                {
                    SangoCreateTroopArgs args = Parse<SangoCreateTroopArgs>(command);
                    City? city = scenario.citySet.Get(args.CityId);
                    if (city == null)
                    {
                        throw MissingTarget(command, $"city {args.CityId}");
                    }

                    (SangoTroopOpResult result, _) = SangoTroopOps.CreateTroop(
                        scenario, city, args.PersonIds, args.LandTroopTypeId, args.WaterTroopTypeId,
                        args.Troops, args.Gold, args.Food);
                    RequireSuccess(command, result);
                    return;
                }
                case MoveTroopKind:
                {
                    SangoMoveTroopArgs args = Parse<SangoMoveTroopArgs>(command);
                    Troop? troop = scenario.troopsSet.Get(args.TroopId);
                    if (troop == null)
                    {
                        throw MissingTarget(command, $"troop {args.TroopId}");
                    }

                    (SangoTroopOpResult result, _) = SangoTroopOps.MoveTroop(
                        scenario, troop, scenario.Map.GetCell(args.X, args.Y));
                    RequireSuccess(command, result);
                    return;
                }
                case SetMissionKind:
                {
                    SangoSetMissionArgs args = Parse<SangoSetMissionArgs>(command);
                    Troop? troop = scenario.troopsSet.Get(args.TroopId);
                    if (troop == null)
                    {
                        throw MissingTarget(command, $"troop {args.TroopId}");
                    }

                    troop.SetMission((MissionType)args.MissionType, args.TargetId);
                    return;
                }
                case CityCommandKind:
                {
                    SangoCityCommandArgs args = Parse<SangoCityCommandArgs>(command);
                    City? city = scenario.citySet.Get(args.CityId);
                    if (city == null)
                    {
                        throw MissingTarget(command, $"city {args.CityId}");
                    }

                    RequireSuccess(command, SangoCityOps.Execute(scenario, city, args.Type, args.PersonIds, args.TargetPersonId ?? 0));
                    return;
                }
                case DiplomacyCommandKind:
                {
                    SangoDiplomacyCommandArgs args = Parse<SangoDiplomacyCommandArgs>(command);
                    City? city = scenario.citySet.Get(args.CityId);
                    if (city == null)
                    {
                        throw MissingTarget(command, $"city {args.CityId}");
                    }

                    RequireSuccess(command, SangoDiplomacyOps.Execute(
                        scenario, city, args.Type, new[] { args.PersonId }, args.TargetForceId, args.ResourceValue));
                    return;
                }
                case ResearchCommandKind:
                {
                    SangoResearchCommandArgs args = Parse<SangoResearchCommandArgs>(command);
                    City? city = scenario.citySet.Get(args.CityId);
                    if (city == null)
                    {
                        throw MissingTarget(command, $"city {args.CityId}");
                    }

                    RequireSuccess(command, SangoTechniqueOps.Execute(
                        scenario, city, args.TechniqueId,
                        args.PersonIds is { Length: > 0 } ? args.PersonIds : null));
                    return;
                }
                case SelectPlayerForceKind:
                {
                    // world-setup:重放以同种子带玩家重装世界(与实录同一条 Boot 数据面)。
                    SangoSelectPlayerForceArgs args = Parse<SangoSelectPlayerForceArgs>(command);
                    SangoPlayerTurnOps.SelectPlayerForce(vfs, contentModId, args.Seed, args.ForceId, scenarioAssetPath);
                    return;
                }
                case EndPlayerTurnKind:
                    SangoPlayerTurnOps.EndPlayerTurn();
                    return;
                default:
                    throw new InvalidOperationException($"Unknown sango journal command kind '{command.Kind}' (#{command.Seq}); the replay executor covers step/createTroop/moveTroop/setMission/cityCommand.");
            }
        }

        static T Parse<T>(SangoJournalCommand command) where T : class
        {
            return JsonSerializer.Deserialize<T>(command.ArgsJson, SangoCommandJournalJson.Options)
                ?? throw new InvalidOperationException($"journal command #{command.Seq} ({command.Kind}) args failed to parse: {command.ArgsJson}");
        }

        static void RequireSuccess(SangoJournalCommand command, SangoTroopOpResult result)
        {
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"journal command #{command.Seq} ({command.Kind}) was accepted live but rejected on replay: {result.ErrorCode} {result.Message}");
            }
        }

        static InvalidOperationException MissingTarget(SangoJournalCommand command, string target) =>
            new($"journal command #{command.Seq} ({command.Kind}) targets missing {target}; replay diverged from the recorded world.");
    }

    file static class SangoCommandJournalJson
    {
        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    }
}
