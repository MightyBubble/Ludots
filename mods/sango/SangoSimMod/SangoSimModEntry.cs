using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Sango.Runtime;

namespace Sango
{
    /// <summary>
    /// SangoSimMod 入口(M1.b):把 Ludots 的 Manual 回合时钟事件转译为 sango 回合推进。
    /// assets/GAS/clock.json = Manual → GasClockSystem 每次 RequestStep(1) 触发一次
    /// TurnAdvanced(PLAN D5);首个事件到达时按默认种子惰性启动内核(真实运行时序里
    /// 时钟先于任何地图输入,无需独立 GameStart 挂点)。
    /// M1.d:每次回合事件时向引擎存档注册表补注册 sango.sim 域(注册表由宿主按核心服务
    /// 就绪度建出,缺席即该宿主无持久化基建,不代建)。
    /// M2.a:MapLoaded 时(raylib 宿主先 GameStart 后装载启动地图,故挂点在 MapLoaded 而非
    /// GameStart;内核与 SangoWebUiModEntry 收敛于同一 Scenario.Cur 判空门)把内核地图灌入
    /// 引擎 Field2D 层并落地城池标记 presenter;地图会话未启用 sango 层的宿主(非 sango
    /// 地图)整体跳过。
    /// M2.b:同挂点建 SangoTroopMarkerRuntime(部队增删改经内核部队事件驱动,见其注释);
    /// 另挂开发播种事件 SangoSeedTroops(AgentBridge events.fire 触发,验收取证用:
    /// 开局无部队且 AI 不自行编成,需外部播种才能看到部队标记;事件键只在带 AgentBridge
    /// 的开发宿主存在,生产链路无感)。
    /// M2.d:开发便利事件 SangoStepTurns{n}(1..99,经引擎 Manual 时钟 RequestStep(n) 累积
    /// 待步数,GasClockSystem 逐 fixed tick 消费并发 TurnAdvanced——不绕过 clock;取证脚本
    /// 一次触发推进 n 回合)与 SangoJournalReport(命令 journal 运行时可见性,取证用;
    /// journal 全量重放会换掉 Scenario.Cur,单世界内核里不可在运行中做,逐位重放由
    /// headless SangoReplayTests 承担)。
    /// </summary>
    public sealed class SangoSimModEntry : IMod
    {
        private const string SeedTroopsEventKey = "SangoSeedTroops";
        private const int SeedTroopBudget = 8;
        private const string SeedBattleEventKey = "SangoSeedBattle";
        private const string StepTurnsEventKey = "SangoStepTurns";
        private const int MaxStepTurnsPerFire = 99;
        private const string JournalReportEventKey = "SangoJournalReport";
        private const string SelectPlayerForceEventKey = "SangoSelectPlayerForce";
        private const int MaxPlayerForceIdPerFire = 32;

        private static SaveParticipantRegistry? _registeredRegistry;
        private static SangoTroopMarkerRuntime? _troopMarkers;
        private static GameEngine? _engine;

        public void OnLoad(IModContext context)
        {
            IVirtualFileSystem vfs = context.VFS;
            context.Log("[SangoSimMod] Loaded (M1.b: kernel boot on first manual turn, assets via VFS; M1.d: sango.sim save participant; M2.a: MapLoaded field/city-marker sync; M2.b: troop markers + seed event; M2.d: step-turns/journal dev events; M3.f: combat challenge trigger online; M3-end: entity mirror read model)");
            // M3.f 战斗演出触发(单挑/舌战):静态内核事件订阅,与内核同进程生命周期
            // (内核未启动时事件不来);headless 测试按用例自行 Attach/Detach。
            SangoChallengeOps.Attach();
            // M3 末读模型守卫:Cleanup 相位对账内核世界替换;引擎实例由事件面喂入
            // (ISystemRegistrar 正式面注册,见 SangoEntityMirrorSystem 注释)。
            context.Systems.RegisterSystem(new SangoEntityMirrorSystem(() => _engine), SystemGroup.Cleanup);
            context.OnEvent(GameEvents.MapLoaded, OnMapLoaded(vfs));
            context.OnEvent(GameEvents.TurnAdvanced, OnTurnAdvanced(vfs));
            context.OnEvent(new EventKey(SeedTroopsEventKey), OnSeedTroops(vfs));
            context.OnEvent(new EventKey(SeedBattleEventKey), OnSeedBattle(vfs));
            // SangoStepTurns{n}:bridge events.fire 只带事件键,步数编码进键名(裸键=1 步)。
            for (int count = 1; count <= MaxStepTurnsPerFire; count++)
            {
                context.OnEvent(new EventKey(count == 1 ? StepTurnsEventKey : $"{StepTurnsEventKey}{count}"), OnStepTurns(count));
            }

            context.OnEvent(new EventKey(JournalReportEventKey), OnJournalReport());

            // SangoSelectPlayerForce{n}:开局选势力的取证事件(与 Web UI sango.selectPlayerForce
            // 命令同一数据面;裸键=势力 1)。forceId 编码进键名,仅带 AgentBridge 的开发宿主可见。
            for (int forceId = 1; forceId <= MaxPlayerForceIdPerFire; forceId++)
            {
                context.OnEvent(new EventKey(forceId == 1 ? SelectPlayerForceEventKey : $"{SelectPlayerForceEventKey}{forceId}"), OnSelectPlayerForce(vfs, forceId));
            }
        }

        public void OnUnload()
        {
            SangoChallengeOps.Detach();
            SangoEntityMirrorRuntime.Active?.Dispose();
            _troopMarkers?.Dispose();
            _troopMarkers = null;
            _engine = null;
        }

        private static System.Func<ScriptContext, Task> OnMapLoaded(IVirtualFileSystem vfs)
        {
            return context =>
            {
                if (Sango.Core.Scenario.Cur == null)
                {
                    SangoKernelBoot.Boot(vfs, "SangoContentMod", SangoTurnDriver.DefaultSeed);
                }

                SyncWorldToEngine(context);
                return Task.CompletedTask;
            };
        }

        // Field2D 灌层 + 城标记只在"sango 地图会话 + presenter 管线"宿主生效:地图未启用
        // sango.terrainType 层即视为非 sango 地图,整体跳过(不代建);启用即 fail-fast。
        // M3.a 公开为正式入口:开局选势力(sango.selectPlayerForce)带玩家重装世界后,
        // Web UI 命令层调同一同步面刷新城/部队标记。
        public static void SyncWorldToEngine(ScriptContext context)
        {
            if (!context.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) || engine == null)
            {
                return;
            }

            EnsureEntityMirror(engine);
            SyncWorldPresentation(engine);
        }

        public static void SyncWorldPresentation(GameEngine engine)
        {
            Ludots.Core.Map.MapSession? session = engine.MapSessions?.FocusedSession;
            if (session?.Fields == null ||
                !session.Fields.TryGetByKey(SangoFieldLayers.TerrainTypeLayerKey, out _))
            {
                return;
            }

            SangoFieldLayers.Populate(session.Fields, Sango.Core.Scenario.Cur);

            var presenterRuntime = engine.GetService(CoreServiceKeys.PresenterEntityRuntime);
            var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry);
            var stableIds = engine.GetService(CoreServiceKeys.PresentationStableIdAllocator);
            if (presenterRuntime == null || definitions == null || stableIds == null)
            {
                return;
            }

            int spawned = SangoCityMarkers.Spawn(
                engine.World, presenterRuntime, definitions, stableIds,
                SangoCityMarkers.BuildPlacements(Sango.Core.Scenario.Cur));

            // M3.b 地名标注:177 条河流/地名走 WorldHud 文本链(数据/定义在 SangoTerrainMod
            // assets,提取源见 SangoMapLabels 头注);同 fail-fast 合同。
            int labelsSpawned = SangoMapLabels.Spawn(
                engine.World, presenterRuntime, definitions, stableIds,
                SangoMapLabels.LoadPlacements(engine.VFS ?? throw new InvalidOperationException("SangoMapLabels requires the engine VFS.")));

            _troopMarkers?.Dispose();
            _troopMarkers = new SangoTroopMarkerRuntime(engine.World, presenterRuntime, definitions, stableIds);
            _troopMarkers.SyncAll(Sango.Core.Scenario.Cur);
            Log.Info($"[SangoSimMod] M2.a: field layers populated; {spawned} city markers spawned; M3.b: {labelsSpawned} map labels spawned; M2.b: troop marker runtime online ({_troopMarkers.ActiveMarkers} troops)");
        }

        // 开发播种(AgentBridge events.fire SangoSeedTroops):按 citySet 顺序找满足出征
        // 门槛的城市各编成一支部队(SangoTroopOps.CreateTroop 走真实门槛与结算),预算
        // SeedTroopBudget 支,让标记/部队面板在无玩家操作的可视宿主里有可看对象;
        // 重复触发继续吃剩余预算,门槛耗尽自然拒绝。
        private static System.Func<ScriptContext, Task> OnSeedTroops(IVirtualFileSystem vfs)
        {
            return context =>
            {
                if (Sango.Core.Scenario.Cur == null)
                {
                    SangoKernelBoot.Boot(vfs, "SangoContentMod", SangoTurnDriver.DefaultSeed);
                }

                var scenario = Sango.Core.Scenario.Cur;
                // 开局各军团行动力为 0(Corps.OnForceTurnStart 才发放),先推进一回合再播种
                // (与 M1.c 命令测试的次序约定一致);纯开发取证路径,不进正式测试链。
                SangoTurnDriver.AdvanceTurn();
                int created = 0;
                int rejected = 0;
                scenario.citySet.ForEach(city =>
                {
                    if (city == null || created >= SeedTroopBudget)
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
                        troops: System.Math.Min(3000, city.troops),
                        food: System.Math.Min(20000, city.food / 2));
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

                Log.Info($"[SangoSimMod] M2.b seed event: {created} troop(s) created (budget {SeedTroopBudget}), {rejected} city gate rejection(s)");
                return Task.CompletedTask;
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

                int distance = System.Math.Abs(cell.x - troop.x) + System.Math.Abs(cell.y - troop.y);
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

        // 开发播种·敌对遭遇(AgentBridge events.fire SangoSeedBattle,M2.c 取证):找相互
        // 敌对且都过出征门槛的最近两城各编成一支部队(SangoTroopOps.CreateTroop 真实门槛),
        // 互授歼灭任务(TroopInteractiveDestroyTroop.OnEnter 的 SetMission 语义)。只摆对阵,
        // 不推回合——战斗由后续回合推进自然展开(CorpsAI.AITroops 逐回合驱动两军相撞),
        // 战报行进 SangoCombatAnnals。重复触发幂等拒绝(已有存活歼灭任务即视为已播种)。
        private static System.Func<ScriptContext, Task> OnSeedBattle(IVirtualFileSystem vfs)
        {
            return context =>
            {
                if (Sango.Core.Scenario.Cur == null)
                {
                    SangoKernelBoot.Boot(vfs, "SangoContentMod", SangoTurnDriver.DefaultSeed);
                }

                var scenario = Sango.Core.Scenario.Cur;
                if (scenario.troopsSet.Count > 0)
                {
                    Log.Info("[SangoSimMod] M2.c seed battle: world already has troops; refusing to seed a second encounter");
                    return Task.CompletedTask;
                }

                SangoTurnDriver.AdvanceTurn();

                Sango.Core.City? home = null;
                Sango.Core.City? foe = null;
                int bestDistance = int.MaxValue;
                System.Collections.Generic.List<Sango.Core.City> cities = new();
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
                    Log.Info("[SangoSimMod] M2.c seed battle: no mutually hostile eligible city pair found");
                    return Task.CompletedTask;
                }

                Sango.Core.Troop? attacker = SeedEncounterTroop(scenario, home);
                Sango.Core.Troop? defender = SeedEncounterTroop(scenario, foe);
                if (attacker == null || defender == null)
                {
                    Log.Info("[SangoSimMod] M2.c seed battle: expedition gate rejected a side; no encounter seeded");
                    return Task.CompletedTask;
                }

                attacker.SetMission(Sango.Core.MissionType.TroopDestroyTroop, defender.Id);
                defender.SetMission(Sango.Core.MissionType.TroopDestroyTroop, attacker.Id);
                // Entry 级直接授任务(moveTroop 分派之外的显式入口),入命令 journal;
                // 编成与回合推进已由 op 层自动记录,seed 对阵由此分解为可重放原语序列。
                SangoCommandJournal.Record(SangoReplayJournal.SetMissionKind,
                    new SangoSetMissionArgs(attacker.Id, (int)Sango.Core.MissionType.TroopDestroyTroop, defender.Id));
                SangoCommandJournal.Record(SangoReplayJournal.SetMissionKind,
                    new SangoSetMissionArgs(defender.Id, (int)Sango.Core.MissionType.TroopDestroyTroop, attacker.Id));
                Log.Info(
                    $"[SangoSimMod] M2.c seed battle: {attacker.Name}({home.Name}) vs {defender.Name}({foe.Name}), mutual destroy missions, distance {bestDistance}; advance turns to let the war unfold");
                return Task.CompletedTask;
            };
        }

        // 开发便利·回合推进(AgentBridge events.fire SangoStepTurns{n},M2.d 取证):对引擎
        // Manual 时钟 stepPolicy 调 RequestStep(n)(循环 RequestStep(1) 的等价累积),由
        // GasClockSystem 逐 fixed tick 消费并发 TurnAdvanced → 内核逐回合推进 + 话题逐回合
        // 推送——全程走正式时钟链。宿主无 Manual 时钟服务即类型化拒绝,不代推。
        private static System.Func<ScriptContext, Task> OnStepTurns(int count)
        {
            return context =>
            {
                if (!context.TryGet(CoreServiceKeys.GasClockStepPolicy, out Ludots.Core.Gameplay.GAS.GasClockStepPolicy? stepPolicy) ||
                    stepPolicy == null)
                {
                    Log.Info($"[SangoSimMod] M2.d step turns: host exposes no GasClockStepPolicy; refusing to step {count} turn(s) outside the clock");
                    return Task.CompletedTask;
                }

                stepPolicy.RequestStep(count);
                Log.Info($"[SangoSimMod] M2.d step turns: {count} manual step(s) requested via the engine clock; turns will advance one per fixed tick");
                return Task.CompletedTask;
            };
        }

        // 开局选势力取证事件:与 Web UI 命令 sango.selectPlayerForce 完全同一数据面
        // (SangoPlayerTurnOps.SelectPlayerForce → BootWithPlayer,CheckPlayer 正式链),
        // 成功后走同一 SyncWorldPresentation 重灌标记;开局外拒绝照原样落日志。
        private static System.Func<ScriptContext, Task> OnSelectPlayerForce(IVirtualFileSystem vfs, int forceId)
        {
            return context =>
            {
                try
                {
                    SangoPlayerTurnOps.SelectPlayerForce(vfs, "SangoContentMod", SangoTurnDriver.DefaultSeed, forceId);
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
                {
                    Log.Info($"[SangoSimMod] M3.b select player force {forceId} rejected: {ex.Message}");
                    return Task.CompletedTask;
                }

                SyncWorldToEngine(context);
                Log.Info($"[SangoSimMod] M3.b select player force {forceId}: world reloaded with player and markers resynced");
                return Task.CompletedTask;
            };
        }

        // 开发便利·journal 可见性(AgentBridge events.fire SangoJournalReport,M2.d 取证):
        // 打印命令 journal 的形状证据(按类计数、JSON 导出、首末记录)。全量重放会替换
        // Scenario.Cur,单世界内核不可在运行中执行——逐位重放验收在 headless SangoReplayTests。
        private static System.Func<ScriptContext, Task> OnJournalReport()
        {
            return _ =>
            {
                SangoJournalCommand[] commands = SangoCommandJournal.Snapshot();
                if (commands.Length == 0)
                {
                    Log.Info("[SangoSimMod] M2.d journal report: empty (no simulation entry commands recorded yet)");
                    return Task.CompletedTask;
                }

                var byKind = new System.Collections.Generic.SortedDictionary<string, int>();
                foreach (SangoJournalCommand command in commands)
                {
                    byKind[command.Kind] = byKind.GetValueOrDefault(command.Kind) + 1;
                }

                string kinds = string.Join(", ", System.Linq.Enumerable.Select(byKind, pair => $"{pair.Key}={pair.Value}"));
                string json = SangoCommandJournal.ExportJson();
                Log.Info($"[SangoSimMod] M2.d journal report: {commands.Length} command(s) [{kinds}]; export {json.Length} chars; live turn {commands[^1].Turn}");
                foreach (SangoJournalCommand command in commands.Take(3))
                {
                    Log.Info($"[SangoSimMod] M2.d journal #{command.Seq} turn {command.Turn} {command.Kind}: {command.ArgsJson}");
                }

                SangoJournalCommand? lastStep = System.Linq.Enumerable.LastOrDefault(commands, command => command.Kind == SangoReplayJournal.StepKind);
                if (lastStep != null)
                {
                    Log.Info($"[SangoSimMod] M2.d journal last step: turn {lastStep.Turn} digest {lastStep.DigestAfter}");
                }

                return Task.CompletedTask;
            };
        }

        static bool PassExpeditionGate(Sango.Core.City city)
        {
            int cost = Sango.Core.JobType.GetJobCostAP((int)Sango.Core.CityJobType.MakeTroop);
            return city.troops > 0 && city.food > 0 && city.freePersons.Count > 0 &&
                   city.mBelongCorps!.ActionPoint >= cost;
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
                troops: System.Math.Min(3000, city.troops),
                food: System.Math.Min(20000, city.food / 2));
            return result.Succeeded ? troop : null;
        }

        private static System.Func<ScriptContext, Task> OnTurnAdvanced(IVirtualFileSystem vfs)
        {
            return context =>
            {
                if (Sango.Core.Scenario.Cur == null)
                {
                    SangoKernelBoot.Boot(vfs, "SangoContentMod", SangoTurnDriver.DefaultSeed);
                }

                RegisterSaveParticipant(context, vfs);
                // 镜像先于回合推进挂载:step 命令落账(命令漏斗)时镜像已订阅,
                // 回合内结构事件与末尾刷新即时生效。
                if (context.TryGet(CoreServiceKeys.Engine, out GameEngine? turnEngine) && turnEngine != null)
                {
                    EnsureEntityMirror(turnEngine);
                }

                SangoTurnDriver.AdvanceTurn();
                return Task.CompletedTask;
            };
        }

        // 镜像挂载(幂等):内核已启动且引擎在座即挂;内核未启动时由镜像系统在内核
        // 启动后的帧自举(SangoWebUiMod 的 GameStart 启动先于本 mod 任何事件的路径)。
        private static void EnsureEntityMirror(GameEngine engine)
        {
            _engine = engine;
            if (Sango.Core.Scenario.Cur == null)
            {
                return;
            }

            SangoEntityMirrorRuntime.Attach(engine);
        }

        // 引擎注册表晚于 mod 装载建出(核心服务就绪时),且内核可能被 SangoWebUiMod 的
        // GameStart 启动抢先(本入口的惰性 Boot 分支不再进入),故挂在每回合事件上做一次
        // 幂等注册;同一注册表实例只注册一次(Register 对重复域显式抛错)。
        private static void RegisterSaveParticipant(ScriptContext context, IVirtualFileSystem vfs)
        {
            if (!context.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) || engine == null)
            {
                return;
            }

            SaveParticipantRegistry? registry = engine.GetService(CoreServiceKeys.SaveParticipants);
            if (registry == null || ReferenceEquals(registry, _registeredRegistry))
            {
                return;
            }

            registry.Register(new SangoSaveParticipant(vfs, "SangoContentMod"));
            _registeredRegistry = registry;
        }
    }
}
