using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using EngineLog = Ludots.Core.Diagnostics.Log;
using EngineLogChannel = Ludots.Core.Diagnostics.LogChannel;
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
    /// M2.b:同挂点建 SangoTroopMarkerRuntime(部队增删改经内核部队事件驱动,见其注释)。
    /// M2.d/T0.8:开发取证面 ludots.sango.* 四个 IAgentTool(播种/敌对播种/回合推进/
    /// journal 报告,见 SangoAgentTools——步数等参数走 InputSchema 不再编码进键名;
    /// 无 AgentBridge 的宿主整组不生效)。开局选势力事件族 SangoSelectPlayerForce{n}
    /// 保留(与 Web UI sango.selectPlayerForce 同数据面)。
    /// </summary>
    public sealed class SangoSimModEntry : IMod
    {
        private static readonly EngineLogChannel EngineChannel = EngineLog.GetOrCreateModChannel("SangoSimMod");

        private const string SelectPlayerForceEventKey = "SangoSelectPlayerForce";
        private const int MaxPlayerForceIdPerFire = 32;

        private static SaveParticipantRegistry? _registeredRegistry;
        private static SangoTroopMarkerRuntime? _troopMarkers;
        private static GameEngine? _engine;

        public void OnLoad(IModContext context)
        {
            IVirtualFileSystem vfs = context.VFS;
            context.Log("[SangoSimMod] Loaded (M1.b: kernel boot on first manual turn, assets via VFS; M1.d: sango.sim save participant; M2.a: MapLoaded field/city-marker sync; M2.b: troop markers; T0.8: ludots.sango.* agent tools; M3.f: combat challenge trigger online; M3-end: entity mirror read model; D-1': native city entities + GAS attributes; D-2': native person entities + GAS attributes; D-3': native troop/corps entities + GAS attributes)");
            // D-1' 城 GAS 属性注册(引擎 AttributeRegistry 唯一表;模板 sango.city 的
            // AttributeBuffer 数据按名解析到同一批 id)。幂等:重名注册由注册表显式拒绝。
            foreach (string attributeName in new[]
                     {
                         SangoCityAttributes.Gold, SangoCityAttributes.Food,
                         SangoCityAttributes.Population, SangoCityAttributes.Durability,
                     })
            {
                if (context.Registries.GetAttributeId(attributeName) == -1)
                {
                    context.Registries.RegisterAttribute(attributeName);
                }
            }

            // D-2' 武将 GAS 属性注册(同表同规;模板 sango.person 的 AttributeBuffer
            // 数据按名解析到同一批 id)。
            foreach (string attributeName in new[]
                     {
                         SangoPersonAttributes.Command, SangoPersonAttributes.Strength,
                         SangoPersonAttributes.Intelligence, SangoPersonAttributes.Politics,
                         SangoPersonAttributes.Loyalty,
                     })
            {
                if (context.Registries.GetAttributeId(attributeName) == -1)
                {
                    context.Registries.RegisterAttribute(attributeName);
                }
            }

            // D-3' 部队 GAS 属性注册(同表同规;模板 sango.troop 的 AttributeBuffer
            // 数据按名解析到同一批 id)。
            foreach (string attributeName in new[]
                     {
                         SangoTroopAttributes.Troops, SangoTroopAttributes.Morale, SangoTroopAttributes.Food,
                     })
            {
                if (context.Registries.GetAttributeId(attributeName) == -1)
                {
                    context.Registries.RegisterAttribute(attributeName);
                }
            }

            // M3.f 战斗演出触发(单挑/舌战):静态内核事件订阅,与内核同进程生命周期
            // (内核未启动时事件不来);headless 测试按用例自行 Attach/Detach。
            SangoChallengeOps.Attach();
            // D-4' 战斗解算 GAS 化:mod builtin 阶段 handler 注册进引擎扩展表(引擎配置
            // 编译本 mod 的 preset/effects 时按 key 解析;运行时自持同步栈另行注册同一函数)。
            context.Extensions.Gas.RegisterBuiltinHandler(
                SangoCombatSteps.SkillActionHandlerKey,
                SangoCombatSteps.SkillActionHandler,
                Ludots.Core.Gameplay.GAS.EffectOperationMetadata.GasTransactional(SangoCombatSteps.SkillActionHandlerKey));
            context.Extensions.Gas.RegisterBuiltinHandler(
                SangoCombatSteps.MoraleChangeHandlerKey,
                SangoCombatSteps.MoraleChangeHandler,
                Ludots.Core.Gameplay.GAS.EffectOperationMetadata.GasTransactional(SangoCombatSteps.MoraleChangeHandlerKey));
            // M3 末读模型守卫:Cleanup 相位对账内核世界替换;引擎实例由事件面喂入
            // (ISystemRegistrar 正式面注册,见 SangoEntityMirrorSystem 注释)。
            context.Systems.RegisterSystem(new SangoEntityMirrorSystem(() => _engine), SystemGroup.Cleanup);
            // D-1' 原生城域系统:回合结算(Cleanup,仿真结算后的城域维护相位)与
            // 城 AI(InputCollection,引擎 AI 相位——UtilityAiThinkScheduleSystem 同组)。
            context.Systems.RegisterSystem(new SangoCitySettlementSystem(() => _engine), SystemGroup.Cleanup);
            context.Systems.RegisterSystem(new SangoCityAISystem(() => _engine), SystemGroup.InputCollection);
            // D-2' 原生武将域系统:回合结算(Cleanup,与城域结算同相位——掉忠/登场/
            // 逃逸的组件落账与运行时自举)。
            context.Systems.RegisterSystem(new SangoPersonSettlementSystem(() => _engine), SystemGroup.Cleanup);
            // D-3' 原生部队/军团域系统:回合结算(Cleanup,与城/武将域结算同相位——
            // 耗粮/断粮士气/AP 发放的镜像对账与运行时自举)。
            context.Systems.RegisterSystem(new SangoTroopSettlementSystem(() => _engine), SystemGroup.Cleanup);
            context.OnEvent(GameEvents.MapLoaded, OnMapLoaded(vfs));
            context.OnEvent(GameEvents.TurnAdvanced, OnTurnAdvanced(vfs));

            // T0.8:原取证事件键族(SangoSeedTroops/SangoSeedBattle/SangoStepTurns{n}/
            // SangoJournalReport)退役,改 ludots.sango.* 四个 IAgentTool(见 SangoAgentTools)。
            context.Systems.RegisterSystem(new SangoAgentToolRegistrationSystem(() => _engine, vfs), SystemGroup.Cleanup);

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
            SangoCombatNativeRuntime.Active?.Dispose();
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

            // D-3':部队标记 owner 迁移——原生部队运行时先行(标记 presenter 挂接在
            // 部队实体上),缺席即标记运行时构造失败(fail-fast,不回落双实体路径)。
            SangoTroopNativeRuntime troopRuntime = SangoTroopNativeRuntime.Attach(engine);
            // D-4':战斗解算 GAS 化运行时(依赖部队运行时的实体面;mod 根由引擎 VFS 解析,
            // 解析不到即本宿主未挂 SangoSimMod 资产,战斗保持内核路径)。
            SangoCombatNativeRuntime.Attach(engine);
            _troopMarkers?.Dispose();
            _troopMarkers = new SangoTroopMarkerRuntime(engine.World, troopRuntime, presenterRuntime, definitions, stableIds);
            _troopMarkers.SyncAll(Sango.Core.Scenario.Cur);
            EngineLog.Info(EngineChannel, $"[SangoSimMod] M2.a: field layers populated; {spawned} city markers spawned; M3.b: {labelsSpawned} map labels spawned; M2.b/D-3': troop marker runtime online on native troop entities ({_troopMarkers.ActiveMarkers} troops)");
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
                    EngineLog.Info(EngineChannel, $"[SangoSimMod] M3.b select player force {forceId} rejected: {ex.Message}");
                    return Task.CompletedTask;
                }

                SyncWorldToEngine(context);
                EngineLog.Info(EngineChannel, $"[SangoSimMod] M3.b select player force {forceId}: world reloaded with player and markers resynced");
                return Task.CompletedTask;
            };
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
        // D-1':原生城运行时先行(城实体/属性/保序组件;镜像城分支随其挂载退役);
        // D-2':原生武将运行时同理先行(镜像武将分支随其挂载退役);
        // D-3':原生部队/军团运行时同理先行(镜像部队分支随其挂载退役;presenter
        // 部队标记 owner 迁部队实体,标记运行时按其挂载为前置)。
        private static void EnsureEntityMirror(GameEngine engine)
        {
            _engine = engine;
            if (Sango.Core.Scenario.Cur == null)
            {
                return;
            }

            SangoCityNativeRuntime.Attach(engine);
            SangoPersonNativeRuntime.Attach(engine);
            SangoTroopNativeRuntime.Attach(engine);
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
