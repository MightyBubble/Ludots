using System;
using System.Text.Json.Nodes;
using Ludots.AgentBridge;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Arch.System;

namespace Sango.Runtime
{
    /// <summary>
    /// T0.8(#1841/#1847):原 AgentBridge events.fire 取证事件键(SangoSeedTroops/
    /// SangoSeedBattle/SangoStepTurns{n}/SangoJournalReport,步数编码进键名)改
    /// IAgentTool——带 InputSchema,经 ludots.tools.list 与 MCP 直通可见,结构化
    /// 返回。开局选势力事件族(SangoSelectPlayerForce{n})与 Web UI 命令
    /// sango.selectPlayerForce 同数据面,保留事件通道不工具化。
    /// 注册照 RngShowcaseSystem 先例:系统逐 tick 探 AgentBridge 的工具注册表
    /// 服务,幂等注册,与宿主 mod 装载顺序解耦;无 AgentBridge 的宿主(含
    /// headless 测试)整组不生效。
    /// </summary>
    public static class SangoAgentTools
    {
        public const string SeedTroopsName = "ludots.sango.seed_troops";
        public const string SeedBattleName = "ludots.sango.seed_battle";
        public const string StepTurnsName = "ludots.sango.step_turns";
        public const string JournalReportName = "ludots.sango.journal_report";
    }

    /// <summary>工具注册器:挂在 Cleanup 相位逐 tick 探测,拿到注册表后一次性登记。</summary>
    public sealed class SangoAgentToolRegistrationSystem : ISystem<float>
    {
        // 名字耦合 AgentBridgeModEntry.ToolRegistryKey,无编译期 mod 依赖(RngShowcase 先例)。
        private static readonly ServiceKey<AgentToolRegistry> BridgeToolRegistryKey = new("AgentToolRegistry");

        private readonly Func<GameEngine?> _engine;
        private readonly IVirtualFileSystem _vfs;
        private bool _registered;

        public SangoAgentToolRegistrationSystem(Func<GameEngine?> engine, IVirtualFileSystem vfs)
        {
            _engine = engine;
            _vfs = vfs;
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (_registered)
            {
                return;
            }

            GameEngine? engine = _engine();
            if (engine == null || !engine.TryGetService(BridgeToolRegistryKey, out AgentToolRegistry? registry) || registry == null)
            {
                return;
            }

            registry.Register(new SangoSeedTroopsTool(_vfs));
            registry.Register(new SangoSeedBattleTool(_vfs));
            registry.Register(new SangoStepTurnsTool());
            registry.Register(new SangoJournalReportTool());
            _registered = true;
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 开发播种:按 citySet 顺序找满足出征门槛的城市各编成一支部队(SangoTroopOps.CreateTroop
    /// 走真实门槛与结算),预算 8 支;重复触发继续吃剩余预算,门槛耗尽自然拒绝。
    /// </summary>
    public sealed class SangoSeedTroopsTool : IAgentTool
    {
        private const int Budget = 8;
        private readonly IVirtualFileSystem _vfs;

        public SangoSeedTroopsTool(IVirtualFileSystem vfs) => _vfs = vfs;

        public string Name => SangoAgentTools.SeedTroopsName;
        public string Description => "Seed up to 8 expedition troops from eligible cities (dev forensics; repeats consume remaining budget).";
        public JsonObject? InputSchema => null;

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            if (Sango.Core.Scenario.Cur == null)
            {
                SangoKernelBoot.Boot(_vfs, "SangoContentMod", SangoTurnDriver.DefaultSeed);
            }

            var scenario = Sango.Core.Scenario.Cur;
            // 开局各军团行动力为 0(Corps.OnForceTurnStart 才发放),先推进一回合再播种
            // (与 M1.c 命令测试的次序约定一致);纯开发取证路径,不进正式测试链。
            SangoTurnDriver.AdvanceTurn();
            int created = 0;
            int rejected = 0;
            scenario.citySet.ForEach(city =>
            {
                if (city == null || created >= Budget)
                {
                    return;
                }

                var persons = new System.Collections.Generic.List<int>();
                foreach (Sango.Core.Person person in city.freePersons)
                {
                    if (person != null && persons.Count < SangoTroopOps.MaxMembers)
                    {
                        persons.Add(person.Id);
                    }
                }

                (SangoTroopOpResult result, Sango.Core.Troop? troop) = SangoTroopOps.CreateTroop(
                    scenario, city, persons,
                    troops: Math.Min(3000, city.troops),
                    food: Math.Min(20000, city.food / 2));
                if (result.Succeeded && troop != null)
                {
                    created++;
                    MoveSeedTroopAside(scenario, troop);
                }
                else
                {
                    rejected++;
                }
            });

            return new JsonObject
            {
                ["created"] = created,
                ["rejected"] = rejected,
                ["budget"] = Budget,
            };
        }

        // 取证观感:播种部队走真实移动链挪出城中心几格(与城标 sphere 错开,否则同格叠影);
        // 移动失败(占位/越程)只影响构图,不影响播种本身,吞掉不抛。
        static void MoveSeedTroopAside(Sango.Core.Scenario scenario, Sango.Core.Troop troop)
        {
            troop.MoveRange.Clear();
            scenario.Map.GetMoveRange(troop, troop.MoveRange);
            Sango.Core.Cell? best = null;
            int bestDistance = 2; // 至少隔 2 格,避免与城标叠影
            foreach (Sango.Core.Cell cell in troop.MoveRange)
            {
                if (cell.troop != null || cell.building != null)
                {
                    continue;
                }

                int distance = Math.Abs(cell.x - troop.x) + Math.Abs(cell.y - troop.y);
                if (distance > bestDistance)
                {
                    best = cell;
                    bestDistance = distance;
                }
            }

            if (best != null)
            {
                SangoTroopOps.MoveTroop(scenario, troop, best);
            }
        }
    }

    /// <summary>
    /// 开发播种·敌对遭遇(M2.c 取证):找相互敌对且都过出征门槛的最近两城各编成一支部队,
    /// 互授歼灭任务;只摆对阵不推回合,战斗由后续回合推进自然展开。重复触发幂等拒绝
    /// (已有存活部队即视为已播种)。
    /// </summary>
    public sealed class SangoSeedBattleTool : IAgentTool
    {
        private readonly IVirtualFileSystem _vfs;

        public SangoSeedBattleTool(IVirtualFileSystem vfs) => _vfs = vfs;

        public string Name => SangoAgentTools.SeedBattleName;
        public string Description => "Seed one mutual-annihilation encounter between the nearest hostile eligible city pair (dev forensics; idempotent refusal once any troop exists).";
        public JsonObject? InputSchema => null;

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            if (Sango.Core.Scenario.Cur == null)
            {
                SangoKernelBoot.Boot(_vfs, "SangoContentMod", SangoTurnDriver.DefaultSeed);
            }

            var scenario = Sango.Core.Scenario.Cur;
            if (scenario.troopsSet.Count > 0)
            {
                return new JsonObject { ["seeded"] = false, ["reason"] = "world already has troops" };
            }

            SangoTurnDriver.AdvanceTurn();

            Sango.Core.City? home = null;
            Sango.Core.City? foe = null;
            int bestDistance = int.MaxValue;
            var cities = new System.Collections.Generic.List<Sango.Core.City>();
            scenario.citySet.ForEach(city => cities.Add(city));
            foreach (Sango.Core.City a in cities)
            {
                if (a?.mBelongForce == null || a.mBelongCorps == null || !PassExpeditionGate(a))
                {
                    continue;
                }

                foreach (Sango.Core.City b in cities)
                {
                    if (b == a || b?.mBelongForce == null || b.mBelongCorps == null || !a.IsEnemy(b) || !PassExpeditionGate(b))
                    {
                        continue;
                    }

                    int distance = scenario.Map.Distance(a.CenterCell, b.CenterCell);
                    if (distance < bestDistance)
                    {
                        (home, foe, bestDistance) = (a, b, distance);
                    }
                }
            }

            if (home == null || foe == null)
            {
                return new JsonObject { ["seeded"] = false, ["reason"] = "no mutually hostile eligible city pair" };
            }

            Sango.Core.Troop? attacker = SeedEncounterTroop(scenario, home);
            Sango.Core.Troop? defender = SeedEncounterTroop(scenario, foe);
            if (attacker == null || defender == null)
            {
                return new JsonObject { ["seeded"] = false, ["reason"] = "expedition gate rejected a side" };
            }

            SangoTroopWriteFace.ApplyMission(attacker, Sango.Core.MissionType.TroopDestroyTroop, defender.Id);
            SangoTroopWriteFace.ApplyMission(defender, Sango.Core.MissionType.TroopDestroyTroop, attacker.Id);
            // 工具级直接授任务(moveTroop 分派之外的显式入口),入命令 journal;
            // 编成与回合推进已由 op 层自动记录,seed 对阵由此分解为可重放原语序列。
            SangoCommandJournal.Record(SangoReplayJournal.SetMissionKind,
                new SangoSetMissionArgs(attacker.Id, (int)Sango.Core.MissionType.TroopDestroyTroop, defender.Id));
            SangoCommandJournal.Record(SangoReplayJournal.SetMissionKind,
                new SangoSetMissionArgs(defender.Id, (int)Sango.Core.MissionType.TroopDestroyTroop, attacker.Id));
            return new JsonObject
            {
                ["seeded"] = true,
                ["attacker"] = $"{attacker.Name}({home.Name})",
                ["defender"] = $"{defender.Name}({foe.Name})",
                ["distance"] = bestDistance,
            };
        }

        static bool PassExpeditionGate(Sango.Core.City city)
        {
            int cost = Sango.Core.JobType.GetJobCostAP((int)Sango.Core.CityJobType.MakeTroop);
            return city.troops > 0 && city.food > 0 && city.freePersons.Count > 0 &&
                   SangoCorpsReadFace.ActionPoint(city) >= cost;
        }

        static Sango.Core.Troop? SeedEncounterTroop(Sango.Core.Scenario scenario, Sango.Core.City city)
        {
            var persons = new System.Collections.Generic.List<int>();
            foreach (Sango.Core.Person person in city.freePersons)
            {
                if (person != null && persons.Count < SangoTroopOps.MaxMembers)
                {
                    persons.Add(person.Id);
                }
            }

            (SangoTroopOpResult result, Sango.Core.Troop? troop) = SangoTroopOps.CreateTroop(
                scenario, city, persons,
                troops: Math.Min(3000, city.troops),
                food: Math.Min(20000, city.food / 2));
            return result.Succeeded ? troop : null;
        }
    }

    /// <summary>
    /// 开发便利·回合推进(M2.d 取证):对引擎 Manual 时钟 stepPolicy 调 RequestStep(count)
    /// (循环 RequestStep(1) 的等价累积),由 GasClockSystem 逐 fixed tick 消费并发
    /// TurnAdvanced → 内核逐回合推进——全程走正式时钟链。宿主无 Manual 时钟服务即
    /// 类型化拒绝,不代推。
    /// </summary>
    public sealed class SangoStepTurnsTool : IAgentTool
    {
        private const int MaxCount = 99;

        public string Name => SangoAgentTools.StepTurnsName;
        public string Description => "Request N engine-clock manual steps; sango advances one turn per step (dev forensics).";
        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["count"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["minimum"] = 1,
                    ["maximum"] = MaxCount,
                    ["default"] = 1,
                },
            },
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            int count = 1;
            if (args != null && args.TryGetPropertyValue("count", out JsonNode? countNode) && countNode != null)
            {
                count = Math.Clamp((int)countNode!, 1, MaxCount);
            }

            if (!context.Engine.TryGetService(CoreServiceKeys.GasClockStepPolicy, out GasClockStepPolicy? stepPolicy) ||
                stepPolicy == null)
            {
                return new JsonObject
                {
                    ["requested"] = false,
                    ["reason"] = "host exposes no GasClockStepPolicy; refusing to step outside the clock",
                };
            }

            stepPolicy.RequestStep(count);
            return new JsonObject
            {
                ["requested"] = true,
                ["count"] = count,
            };
        }
    }

    /// <summary>
    /// 开发便利·journal 可见性(M2.d 取证):命令 journal 的形状证据(按类计数、JSON 导出
    /// 尺寸、首三条与末条 step 的 digest)。全量重放会替换 Scenario.Cur,单世界内核不可
    /// 在运行中执行——逐位重放验收在 headless SangoReplayTests。
    /// </summary>
    public sealed class SangoJournalReportTool : IAgentTool
    {
        public string Name => SangoAgentTools.JournalReportName;
        public string Description => "Report the sango command journal shape: per-kind counts, export size, head entries and last step digest (dev forensics).";
        public JsonObject? InputSchema => null;

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            SangoJournalCommand[] commands = SangoCommandJournal.Snapshot();
            if (commands.Length == 0)
            {
                return new JsonObject { ["total"] = 0, ["reason"] = "no simulation entry commands recorded yet" };
            }

            var byKind = new JsonObject();
            var kindCounts = new System.Collections.Generic.SortedDictionary<string, int>();
            foreach (SangoJournalCommand command in commands)
            {
                kindCounts[command.Kind] = kindCounts.GetValueOrDefault(command.Kind) + 1;
            }

            foreach (System.Collections.Generic.KeyValuePair<string, int> pair in kindCounts)
            {
                byKind[pair.Key] = pair.Value;
            }

            var head = new JsonArray();
            foreach (SangoJournalCommand command in commands)
            {
                if (head.Count >= 3)
                {
                    break;
                }

                head.Add(new JsonObject
                {
                    ["seq"] = command.Seq,
                    ["turn"] = command.Turn,
                    ["kind"] = command.Kind,
                    ["args"] = command.ArgsJson,
                });
            }

            SangoJournalCommand? lastStep = System.Linq.Enumerable.LastOrDefault(
                commands, command => command.Kind == SangoReplayJournal.StepKind);
            var result = new JsonObject
            {
                ["total"] = commands.Length,
                ["byKind"] = byKind,
                ["exportChars"] = SangoCommandJournal.ExportJson().Length,
                ["liveTurn"] = commands[^1].Turn,
                ["head"] = head,
            };
            if (lastStep != null)
            {
                result["lastStep"] = new JsonObject
                {
                    ["turn"] = lastStep.Turn,
                    ["digestAfter"] = lastStep.DigestAfter,
                };
            }

            return result;
        }
    }
}
