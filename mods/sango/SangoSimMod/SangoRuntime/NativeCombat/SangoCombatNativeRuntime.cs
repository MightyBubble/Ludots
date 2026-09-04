// D-4' 战斗解算 GAS 化运行时:mod 私有同步 GAS 执行栈(引擎类直构,不建平行内容管线——
// effect/preset/ability 全部由引擎 loader 从本 mod 资产 JSON 编译)+ 引擎正式激活面
// (AbilitySystem.TryActivateAbility → EffectRequestQueue → EffectProcessingLoopSystem
// 同步排空)+ 内核演出队列转移泵(战斗路径 divert)。
//
// 路由合同(D 系列惯例):
//   挂载且为当前世界权威 → 三个 mod 持泵位(SangoTurnDriver.AdvanceTurn 每 Run() 前、
//   SangoTroopOps.PumpRenderEvents、SangoPlayerTurnOps 泵位)以逐事件 FIFO 泵复刻
//   RenderEvent.Update 的循环形状(依赖队列先行、头部未完即止、同拍追加同拍排空),
//   TroopSpellSkill{,Critical,Fail}Event 三型在 Enter(内核演出副作用原样)后改走
//   GAS 激活(解算语义 = SangoCombatSteps),OnSkillActionEnd/SetAniShow/IsDone 完成语义
//   原位补齐——Troop.SpellSkill 守卫由内核自行完结,任务行为体零感知;
//   未挂载 → 调用面照旧走内核 RenderEvent.Instance.Update(预言机路径,零改动)。
//   内核释放判定(CheckSuccess/CheckCritical 掷点)保持在内核 SpellSkill(非本波移交面),
//   共享 GameRandom 流,掷点位与内核路径逐位一致。
//
// 激活载体:部队实体(D-3' MaterializeTemplate 产物)+ AbilityStateBuffer"当前施放位"
// 单槽(AbilityStateBuffer 容量 8 < 部队技能上限 24,常驻槽位不可行;按施放置位,
// 激活仍走 TryActivateAbility 全验证链——引擎侧常驻槽位扩容登记为缺口评估)。
//
// 对拍合同(同 D-1'/D-2'/D-3'):老内核跑一遍(本运行时不挂,digest=内核源,内核解算
// 经内核泵原样执行)与原生战斗跑一遍(挂载,转移泵+GAS 解算,digest=组件源)同种子同
// 命令流,全量 digest 逐位相等。随机流次序铁律:解算内全部掷点(GameRandom 共享流)
// 与内核 SkillInstance.ChangeTroops/DoOffset 同位同序;内核保留面调用(GainEP/OnFall/
// SkillEffect.Action/UpdateCell/ChangeFood/Clear)与内核路径同一调用=同一副作用位。

using System;
using System.Collections.Generic;
using System.Reflection;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Sango.Core;
using Sango.Render;

namespace Sango.Runtime
{
    /// <summary>原生战斗解算运行时(进程单世界:SangoCombatNativeRuntime.Active)。</summary>
    public sealed class SangoCombatNativeRuntime : IDisposable
    {
        public const string ModId = "SangoSimMod";
        public const string SkillEffectIdPrefix = "Effect.Sango.Skill.";
        public const string SkillAbilityIdPrefix = "Ability.Sango.Skill.";
        public const string DebateWinAbilityId = "Ability.Sango.Challenge.DebateWin";
        public const string DebateLoseAbilityId = "Ability.Sango.Challenge.DebateLose";

        const int MaxPumpEvents = 10_000;
        const int MaxEffectDrainFrames = 16;

        readonly World _world;
        readonly EffectRequestQueue _requests;
        readonly GasBudget _budget;
        readonly GasClocks _clocks;
        readonly DiscreteClock _clock;
        readonly AbilitySystem _abilitySystem;
        readonly EffectProcessingLoopSystem _processing;
        readonly Dictionary<int, int> _skillAbilityIds = new();
        readonly Dictionary<string, int> _challengeAbilityIds = new(StringComparer.Ordinal);
        readonly Scenario _scenario;
        bool _disposed;

        // 演出队列转移泵的内核接缝(私有字段只读访问 + RemoveAt 逐事件;漂移即类型化抛错)。
        static readonly FieldInfo? EventQueueField = typeof(RenderEvent).GetField("eventQueue", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo? DependsEventQueueField = typeof(RenderEvent).GetField("dependsEventQueue", BindingFlags.NonPublic | BindingFlags.Instance);

        public static SangoCombatNativeRuntime? Active { get; private set; }

        SangoCombatNativeRuntime(World world, string modRoot, string coreModRoot)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _scenario = Scenario.Cur
                ?? throw new InvalidOperationException("SangoCombatNativeRuntime requires a booted kernel (Scenario.Cur).");
            if (SangoTroopNativeRuntime.Active is not { IsDisposed: false } troopRuntime || !troopRuntime.IsCurrentKernel)
            {
                throw new InvalidOperationException(
                    "SangoCombatNativeRuntime requires the native troop runtime (D-3') attached on the same kernel; attach it first.");
            }

            if (EventQueueField == null || DependsEventQueueField == null)
            {
                throw new InvalidOperationException(
                    "Kernel seam moved: RenderEvent private queues no longer exist; re-audit the D-4' combat divert pump.");
            }

            (_requests, _budget, _clock, _clocks, AbilitySystem abilitySystem, EffectProcessingLoopSystem processing) =
                BuildGasStack(modRoot, coreModRoot);
            _abilitySystem = abilitySystem;
            _processing = processing;
            Active = this;
        }

        public bool IsDisposed => _disposed;

        /// <summary>权威性判定(通用合同):世界被替换后、重建前,权威回到内核解算。</summary>
        public bool IsCurrentKernel => !_disposed && ReferenceEquals(_scenario, Scenario.Cur);

        // 引擎宿主挂载(幂等):由引擎 VFS 解析本 mod 与 LudotsCoreMod 物理根(构建目录或
        // 仓内布局都可),内核世界与部队运行时未变即复用。
        public static SangoCombatNativeRuntime? Attach(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            if (Scenario.Cur == null)
            {
                return null;
            }

            IVirtualFileSystem vfs = engine.VFS
                ?? throw new InvalidOperationException("SangoCombatNativeRuntime requires the engine VFS to resolve the mod root.");
            if (!vfs.TryResolveFullPath($"{ModId}:assets/mod.json", out string? manifestPath))
            {
                return null;
            }

            // 解析位是 <root>/assets/mod.json,mod 根要再剥一层 assets。
            string modRoot = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(manifestPath)!)!;
            string? coreModRoot = ResolveCoreModRootFromVfs(vfs)
                ?? ResolveCoreModRootSibling(modRoot);
            return coreModRoot == null ? null : AttachWithRoot(engine.World, modRoot, coreModRoot);
        }

        /// <summary>测试/裸世界挂载(modRoot = SangoSimMod 物理根,资产编译源)。</summary>
        public static SangoCombatNativeRuntime AttachBare(World world, string modRoot) =>
            AttachWithRoot(world, modRoot, ResolveCoreModRootSibling(modRoot)
                ?? throw new InvalidOperationException(
                    "Sango combat GAS stack requires LudotsCoreMod assets (game.json constants) next to SangoSimMod; the mods layout drifted."));

        static SangoCombatNativeRuntime AttachWithRoot(World world, string modRoot, string coreModRoot)
        {
            if (Active is { IsDisposed: false } existing &&
                ReferenceEquals(existing._scenario, Scenario.Cur) &&
                existing._world == world)
            {
                return existing;
            }

            Active?.Dispose();
            return new SangoCombatNativeRuntime(world, modRoot, coreModRoot);
        }

        static string? ResolveCoreModRootFromVfs(IVirtualFileSystem vfs) =>
            vfs.TryResolveFullPath("LudotsCoreMod:assets/game.json", out string? gameJson)
                // 解析位是 <root>/assets/game.json,mod 根要再剥一层 assets。
                ? System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(gameJson)!)
                : null;

        static string? ResolveCoreModRootSibling(string modRoot)
        {
            string modsRoot = System.IO.Path.GetDirectoryName(
                System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(modRoot))!)!;
            string candidate = System.IO.Path.Combine(modsRoot, "LudotsCoreMod");
            return System.IO.File.Exists(System.IO.Path.Combine(candidate, "assets", "game.json")) ? candidate : null;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _processing.Dispose();
            if (ReferenceEquals(Active, this))
            {
                Active = null;
            }

            _disposed = true;
        }

        // ---- 私有同步 GAS 执行栈(引擎 loader 编译本 mod 资产;GasTests 同构构造范式) ----

        (EffectRequestQueue, GasBudget, DiscreteClock, GasClocks, AbilitySystem, EffectProcessingLoopSystem) BuildGasStack(
            string modRoot, string coreModRoot)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount(ModId, modRoot);

            vfs.Mount("LudotsCoreMod", coreModRoot);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            modLoader.LoadedModIds.Add(ModId);
            modLoader.LoadedModIds.Add("LudotsCoreMod");
            var pipeline = new ConfigPipeline(vfs, modLoader);
            Ludots.Core.Config.GameConfig gameConfig = pipeline.MergeGameConfig();
            var chainOrderTypes = Ludots.Core.Gameplay.GAS.Systems.ResponseChainOrderTypes.RequireConfigured(
                new Ludots.Core.Gameplay.GAS.Systems.ResponseChainOrderTypes
                {
                    ChainPass = gameConfig.Constants.ResponseChainOrderTypeIds["chainPass"],
                    ChainNegate = gameConfig.Constants.ResponseChainOrderTypeIds["chainNegate"],
                    ChainActivateEffect = gameConfig.Constants.ResponseChainOrderTypeIds["chainActivateEffect"],
                },
                nameof(SangoCombatNativeRuntime));
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("GAS/preset_types.json", ConfigMergePolicy.ArrayById, idField: "id"));
            catalog.Add(new ConfigCatalogEntry("GAS/effects.json", ConfigMergePolicy.ArrayById, idField: "id"));
            catalog.Add(new ConfigCatalogEntry("GAS/abilities.json", ConfigMergePolicy.ArrayById, idField: "id"));
            var conflictReport = new ConfigConflictReport();

            var builtinHandlers = new BuiltinHandlerRegistry();
            SangoCombatSteps.RegisterHandlers(builtinHandlers);
            var presets = new PresetTypeRegistry();
            new PresetTypeLoader(pipeline, presets, builtinHandlers).Load(catalog, conflictReport);
            presets.Freeze();
            var templates = new EffectTemplateRegistry();
            new EffectTemplateLoader(pipeline, templates, presetTypes: presets).Load(catalog, conflictReport);
            var abilityDefinitions = new AbilityDefinitionRegistry();
            new AbilityExecLoader(pipeline, abilityDefinitions).Load(catalog, conflictReport);
            DeriveSynchronousOnActivateEffects(abilityDefinitions);
            EffectExecutionPlanCompiler.FinalizeAll(
                templates, presets, builtinHandlers, new GraphProgramRegistry(),
                Ludots.Core.NodeLibraries.GASGraph.GasGraphOpHandlerTable.Instance);

            var requests = new EffectRequestQueue();
            var budget = new GasBudget();
            var clock = new DiscreteClock();
            var clocks = new GasClocks(clock);
            var tagOps = new TagOps(
                new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
                new TagRuleRegistry());
            var phaseExecutor = new EffectPhaseExecutor(
                new GraphProgramRegistry(),
                presets,
                builtinHandlers,
                Ludots.Core.NodeLibraries.GASGraph.GasGraphOpHandlerTable.Instance,
                templates);
            var graphApi = new Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi(_world, effectRequests: requests, tagOps: tagOps);
            var abilitySystem = new AbilitySystem(_world, requests, abilityDefinitions, tagOps);
            var processing = new EffectProcessingLoopSystem(
                _world,
                requests,
                clock,
                new GasConditionRegistry(),
                lifetimeSnapshotCapacity: 16_384,
                fanOutCommandCapacity: GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                budget: budget,
                templates: templates,
                responseChainOrderTypes: chainOrderTypes,
                phaseExecutor: phaseExecutor,
                graphApi: graphApi,
                tagOps: tagOps)
            {
                MaxWorkUnitsPerSlice = 2_048,
            };

            IndexSkillAbilities(abilityDefinitions);
            return (requests, budget, clock, clocks, abilitySystem, processing);
        }

        /// <summary>
        /// exec 时间线的 tick-0 EffectSignal 在引擎语义上即"激活即发布"——战斗解算是单拍同步
        /// 激活(TryActivateAbility 的 OnActivateEffects 发布通道),这里按同一语义把 tick-0
        /// 信号派生为 OnActivateEffects(数据源仍是 abilities.json,非新 schema)。
        /// </summary>
        static void DeriveSynchronousOnActivateEffects(AbilityDefinitionRegistry abilityDefinitions)
        {
            int[] abilityIds = abilityDefinitions.RegisteredAbilityIds.ToArray();
            for (int index = 0; index < abilityIds.Length; index++)
            {
                int abilityId = abilityIds[index];
                if (abilityId <= 0 || !abilityDefinitions.TryGet(abilityId, out AbilityDefinition definition))
                {
                    continue;
                }

                AbilityExecSpec spec = definition.ExecSpec;
                if (spec.ItemCount <= 0)
                {
                    continue;
                }
                var onActivate = default(AbilityOnActivateEffects);
                for (int item = 0; item < spec.ItemCount; item++)
                {
                    if (spec.GetKind(item) == ExecItemKind.EffectSignal && spec.GetTick(item) == 0)
                    {
                        onActivate.Add(spec.GetTemplateId(item));
                    }
                }

                if (onActivate.Count > 0)
                {
                    definition.OnActivateEffects = onActivate;
                    definition.HasOnActivateEffects = true;
                    abilityDefinitions.Register(abilityId, definition);
                }
            }
        }

        void IndexSkillAbilities(AbilityDefinitionRegistry abilityDefinitions)
        {
            _skillAbilityIds.Clear();
            _challengeAbilityIds.Clear();
            foreach (int abilityId in abilityDefinitions.RegisteredAbilityIds)
            {
                // AbilityIdRegistry 是加载期 name→id 全局表;本栈加载后即为本栈权威,直接反查。
                string name = AbilityIdRegistry.GetName(abilityId);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (name.StartsWith(SkillAbilityIdPrefix, StringComparison.Ordinal))
                {
                    string suffix = name.Substring(SkillAbilityIdPrefix.Length);
                    if (int.TryParse(suffix, out int skillId))
                    {
                        _skillAbilityIds[skillId] = abilityId;
                    }
                }
                else if (name == DebateWinAbilityId || name == DebateLoseAbilityId)
                {
                    _challengeAbilityIds[name] = abilityId;
                }
            }
        }

        // ---- 激活面(引擎正式激活:TryActivateAbility 全验证链 + 同步排空) ----

        /// <summary>
        /// 技能解算激活:caster 部队实体的当前施放位指向该技能的 ability,经引擎激活面发布
        /// 效果请求并同步排空(解算在内核施放位内完成,随机流次序铁律)。
        /// </summary>
        public void ResolveSpell(Troop caster, SkillInstance skill, Cell spellCell, int criticalFactor)
        {
            ArgumentNullException.ThrowIfNull(caster);
            ArgumentNullException.ThrowIfNull(skill);
            ArgumentNullException.ThrowIfNull(spellCell);

            if (!_skillAbilityIds.TryGetValue(skill.skill?.Id ?? -1, out int abilityId))
            {
                throw new InvalidOperationException(
                    $"Sango combat GAS stack has no ability for skill {skill.skill?.Id} ({skill.Name}); the assets/GAS mapping is incomplete.");
            }

            Entity casterEntity = SangoTroopNativeRuntime.Active!.TroopEntityOrThrow(caster);
            EnsureCastSlot(casterEntity, abilityId);

            using (new SangoCombatCastScope
            {
                Runtime = this,
                Caster = caster,
                Skill = skill,
                SpellCell = spellCell,
                CriticalFactor = criticalFactor,
            })
            {
                if (!_abilitySystem.TryActivateAbility(casterEntity, 0))
                {
                    throw new InvalidOperationException(
                        $"Sango combat activation rejected for troop {caster.Id} skill {skill.skill?.Id}; the engine activation face refused the cast.");
                }

                DrainEffects();
            }
        }

        /// <summary>演出结果回写激活(舌战 ±10 士气):同一激活面。</summary>
        public void ResolveChallengeMorale(Troop troop, bool win)
        {
            ArgumentNullException.ThrowIfNull(troop);
            if (!_challengeAbilityIds.TryGetValue(win ? DebateWinAbilityId : DebateLoseAbilityId, out int abilityId))
            {
                throw new InvalidOperationException(
                    "Sango combat GAS stack has no debate challenge ability; the assets/GAS mapping is incomplete.");
            }

            Entity troopEntity = SangoTroopNativeRuntime.Active!.TroopEntityOrThrow(troop);
            EnsureCastSlot(troopEntity, abilityId);

            using (new SangoCombatCastScope
            {
                Runtime = this,
                Caster = troop,
                Skill = null,
                SpellCell = troop.cell,
                CriticalFactor = 100,
            })
            {
                if (!_abilitySystem.TryActivateAbility(troopEntity, 0))
                {
                    throw new InvalidOperationException(
                        $"Sango challenge activation rejected for troop {troop.Id}.");
                }

                DrainEffects();
            }
        }

        void EnsureCastSlot(Entity entity, int abilityId)
        {
            // fixed 缓冲索引需 unsafe,这里按"当前施放位"语义整建:单槽 = 本次施放的 ability。
            if (_world.Has<AbilityStateBuffer>(entity))
            {
                _world.Set(entity, default(AbilityStateBuffer));
            }
            else
            {
                _world.Add(entity, new AbilityStateBuffer());
            }

            _world.Get<AbilityStateBuffer>(entity).AddAbility(abilityId);
        }

        void DrainEffects()
        {
            for (int frame = 0; frame < MaxEffectDrainFrames && _requests.Count > 0; frame++)
            {
                _budget.Reset();
                _clocks.AdvanceFixedFrame();
                _processing.Update(1f);
                _clocks.AdvanceFixedFrame();
            }

            if (_requests.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Sango combat effect drain stalled with {_requests.Count} request(s) pending; the effect chain did not settle synchronously.");
            }
        }

        // ---- 数值同步(战斗写位的 GAS 属性正式写入;D-3' 部队运行时通道) ----

        internal void SyncTroopAfterCombat(Troop troop)
        {
            SangoTroopNativeRuntime.Active?.SyncTroop(troop);
        }

        internal void SyncCityAfterCombat(City? city)
        {
            _ = city; // 城域属性(sango.city.*)由城运行时的读缝/回合边界同步收敛(D-1' 合同)。
        }

        // ---- 演出队列转移泵(逐事件 FIFO;内核 RenderEvent.Update 循环形状的复刻) ----

        /// <summary>
        /// 战斗路径的持泵入口:复刻内核 RenderEvent.Update(依赖队列先行、主队列头进、
        /// 未完即停、同拍追加同拍继续),TroopSpellSkill 三型转移为 GAS 解算。返回语义与
        /// 内核 Update 一致(队列是否排空)。
        /// </summary>
        public bool Pump(Scenario scenario, float deltaTime)
        {
            if (_disposed || !ReferenceEquals(_scenario, Scenario.Cur))
            {
                return RenderEvent.Instance.Update(scenario, deltaTime);
            }

            RenderEvent renderEvent = RenderEvent.Instance;
            var dependsQueue = (List<IRenderEventBase>?)DependsEventQueueField!.GetValue(renderEvent)
                ?? throw new InvalidOperationException("Kernel seam moved: RenderEvent.dependsEventQueue is null; re-audit the D-4' pump.");
            var eventQueue = (List<IRenderEventBase>?)EventQueueField!.GetValue(renderEvent)
                ?? throw new InvalidOperationException("Kernel seam moved: RenderEvent.eventQueue is null; re-audit the D-4' pump.");

            int processed = 0;
            int dependsCount = dependsQueue.Count;
            if (dependsCount > 0)
            {
                for (int i = 0; i < dependsCount; ++i)
                {
                    IRenderEventBase renderEventBase = dependsQueue[i];
                    if (!renderEventBase.IsInited)
                    {
                        renderEventBase.IsInited = true;
                        renderEventBase.Enter(scenario);
                    }

                    if (ProcessEvent(renderEventBase, scenario, deltaTime, ref processed))
                    {
                        renderEventBase.IsDone = true;
                        renderEventBase.Exit(scenario);
                    }
                }
                dependsQueue.RemoveAll(x => x.IsDone);
            }

            if (eventQueue.Count == 0 && dependsCount > 0)
                return false;

            while (eventQueue.Count > 0)
            {
                IRenderEventBase current = eventQueue[0];
                if (!current.IsInited)
                {
                    current.IsInited = true;
                    current.Enter(scenario);
                }

                if (!ProcessEvent(current, scenario, deltaTime, ref processed))
                    return false;

                current.Exit(scenario);
                eventQueue.RemoveAt(0);
            }

            return true;
        }

        bool ProcessEvent(IRenderEventBase renderEvent, Scenario scenario, float deltaTime, ref int processed)
        {
            if (++processed > MaxPumpEvents)
            {
                throw new InvalidOperationException(
                    "Sango combat render-event pump exceeded its event budget; the event chain is stuck or looping.");
            }

            // 泵的 IsInited 位已先行调用内核 Enter(演出副作用与内核路径同位);
            // 转移只替换解算位(Action)与完成位,Enter/Exit 仍走内核事件自身。
            switch (renderEvent)
            {
                case TroopSpellSkillEvent spellEvent:
                    ResolveSpell(spellEvent.troop, spellEvent.skill, spellEvent.spellCell, 100);
                    GameEvent.OnSkillActionEnd?.Invoke(spellEvent.skill, spellEvent.spellCell, spellEvent.targetTroop, spellEvent.targetBuilding);
                    spellEvent.troop?.Render?.SetAniShow(0);
                    spellEvent.IsDone = true;
                    return true;
                case TroopSpellSkillCriticalEvent criticalEvent:
                    ResolveSpell(criticalEvent.troop, criticalEvent.skill, criticalEvent.spellCell, criticalEvent.criticalFactor);
                    GameEvent.OnSkillActionEnd?.Invoke(criticalEvent.skill, criticalEvent.spellCell, criticalEvent.targetTroop, criticalEvent.targetBuilding);
                    criticalEvent.troop?.Render?.SetAniShow(0);
                    criticalEvent.IsDone = true;
                    return true;
                case TroopSpellSkillFailEvent failEvent:
                {
                    // 内核 Enter 按 IsStrategy/IsRange 选替换技能(私有字段),这里以同一表达式自取。
                    SkillInstance? replaceSkill = failEvent.skill.IsStrategy()
                        ? null
                        : (failEvent.skill.IsRange() ? failEvent.troop.NormalRangeSkill : failEvent.troop.NormalSkill);
                    if (replaceSkill != null)
                    {
                        ResolveSpell(failEvent.troop, replaceSkill, failEvent.spellCell, 100);
                    }

                    GameEvent.OnSkillActionEnd?.Invoke(failEvent.skill, failEvent.spellCell, failEvent.targetTroop, failEvent.targetBuilding);
                    failEvent.troop?.Render?.SetAniShow(0);
                    failEvent.IsDone = true;
                    return true;
                }
                default:
                    return renderEvent.Update(scenario, deltaTime);
            }
        }
    }

    /// <summary>
    /// 战斗泵位收敛面:三个 mod 持泵位共用(挂载且权威 → 转移泵;否则内核泵,预言机路径)。
    /// </summary>
    public static class SangoCombatPump
    {
        /// <summary>泵一次演出队列(等价 RenderEvent.Instance.Update 的调用位)。</summary>
        public static bool Update(Scenario scenario, float deltaTime)
        {
            ArgumentNullException.ThrowIfNull(scenario);
            SangoCombatNativeRuntime? runtime = SangoCombatNativeRuntime.Active;
            if (runtime is { IsDisposed: false, IsCurrentKernel: true })
            {
                return runtime.Pump(scenario, deltaTime);
            }

            return RenderEvent.Instance.Update(scenario, deltaTime);
        }
    }
}
