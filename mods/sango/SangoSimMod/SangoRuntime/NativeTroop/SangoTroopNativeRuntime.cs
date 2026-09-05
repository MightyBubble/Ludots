// D-3' 部队/军团域原生实现运行时:物化 sango.troop 实体群(生灭事件驱动)与
// sango.corps 实体群(剧本装载全量 + 增删事件即时化)、内核回合/命令/部队事件面的
// 组件落账、部队任务态写面(组件写 + 内核 write-through)、军团 AP/jobCounter 读写面
// (消 D-1' 桥 #4)、digest 部队行双源。
//
// 对拍合同(本波验收核心,同 D-1'/D-2'):
//   老内核跑一遍(本运行时不挂,digest 部队行=内核源)与原生部队/军团域跑一遍(挂载,
//   组件源)同种子同命令流,全量 digest 逐位相等。等价性由两段证明构成——
//   1) 跨运行 write-through:原生写面(任务态/军团 AP/jobCounter)把结果同时写内核
//      对象(经内核成员,SetMission/ReduceActionPoint 的事件位与玩家门原样触发)——
//      原生写错一位即内核偏移即失配;
//   2) 账本计值:SangoTroopLedger(耗粮/携粮/士气净变/出征天数)与 SangoCorpsLedger
//      (AP 发放)为同步观测,测试按内核公式(Troop.PrepeareFoodCost/断粮士气 30%/
//      Corps.AddActionPoint)独立复算断言。
//   内核保留面(部队/军团回合结算体 Troop.OnForceTurnStart/Corps.OnForceTurnStart 是
//   虚方法链,Scenario.Run → Force.OnForceTurnStart 直调,无事件订阅面可交换——闸门
//   在案结论,同 D-2' P2)本波镜像对账:组件在回合/势力回合边界与读缝同步 PONO 真值,
//   消亡计划见 SangoLegacyBridge。
//
// 组件源时点权威:digest 读、军团 AP 门槛读、探针读先同步再读(读缝同步合同;
//   SetBase 同值省略为引擎既有语义,组件 Set 为覆盖写)。战斗内数值变化
//   (ChangeTroops/ChangeMorale 的修改器事件在内核结算前置位,不是落账点)由回合/
//   势力回合边界与命令漏斗收敛——读模型一致性合同 = "回合边界 + 命令 ack 前一致"。
//
// digest 双源:SangoTurnDriver.WorldDigest 的部队行——本运行时挂载且为当前世界权威时
// 读组件源(对拍后正式源);否则读内核源(KernelTroopRows,与既有格式逐位同形)。
//
// presenter owner 迁移:部队实体携带 VisualTransform/CullState(presenter 锚定组件),
// SangoTroopMarkerRuntime 以部队实体为 owner 挂标记(消灭标记专用 owner 双实体;
//   城域标记仍是独立 owner,M3.h 在案后续片)。

using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Sango.Core;

namespace Sango.Runtime
{
    /// <summary>原生部队/军团域运行时(进程单世界:SangoTroopNativeRuntime.Active)。</summary>
    public sealed class SangoTroopNativeRuntime : IDisposable
    {
        internal const string TroopTemplateId = "sango.troop";
        internal const string CorpsTemplateId = "sango.corps";

        readonly World _world;
        readonly EntityLifecycleRuntimeServices _services;
        readonly Ludots.Core.Gameplay.GAS.TagOps _tagOps;
        readonly Dictionary<int, Entity> _troops = new();
        readonly Dictionary<int, Entity> _corps = new();
        readonly List<string> _digestRows = new();
        // 回合结算观测窗的上一拍值(士气净变/溃兵损耗探针;非持久面)。
        readonly Dictionary<int, (int Morale, int Troops)> _lastObserved = new();
        readonly Dictionary<int, int> _lastCorpsAp = new();
        Scenario _scenario;
        bool _disposed;

        public static SangoTroopNativeRuntime? Active { get; private set; }

        SangoTroopNativeRuntime(World world, EntityLifecycleRuntimeServices services, Ludots.Core.Gameplay.GAS.TagOps tagOps)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _tagOps = tagOps ?? throw new ArgumentNullException(nameof(tagOps));
            _scenario = Scenario.Cur
                ?? throw new InvalidOperationException("SangoTroopNativeRuntime requires a booted kernel (Scenario.Cur).");
            SangoTroopAttributes.EnsureRegistered();

            SubscribeKernelFaces();
            RebuildAll(_scenario);
            Active = this;
        }

        public bool IsDisposed => _disposed;

        public int TroopCount => _troops.Count;

        public int CorpsCount => _corps.Count;

        /// <summary>部队生命周期观测(测试探针):物化/解散/溃灭计数。</summary>
        public int TroopSpawnCount { get; private set; }

        public int TroopClearCount { get; private set; }

        public int TroopDestroyCount { get; private set; }

        /// <summary>权威性判定(通用合同照搬):世界被替换后、重建前,权威回到内核 PONO。</summary>
        public bool IsCurrentKernel => !_disposed && ReferenceEquals(_scenario, Scenario.Cur);

        /// <summary>引擎宿主挂载(幂等):内核世界与引擎世界未变即复用;已替换即重建。</summary>
        public static SangoTroopNativeRuntime Attach(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            Scenario scenario = Scenario.Cur
                ?? throw new InvalidOperationException("SangoTroopNativeRuntime attach requires a booted kernel (Scenario.Cur).");

            if (Active is { IsDisposed: false } existing &&
                ReferenceEquals(existing._scenario, scenario) &&
                existing._world == engine.World)
            {
                return existing;
            }

            Active?.Dispose();
            var stableIds = engine.GetService(CoreServiceKeys.PresentationStableIdAllocator)
                ?? throw new InvalidOperationException("SangoTroopNativeRuntime requires the engine PresentationStableIdAllocator service.");
            var tagOps = engine.GetService(CoreServiceKeys.TagOps) as Ludots.Core.Gameplay.GAS.TagOps
                ?? throw new InvalidOperationException("SangoTroopNativeRuntime requires the engine TagOps service.");
            return new SangoTroopNativeRuntime(
                engine.World,
                new EntityLifecycleRuntimeServices(
                    engine.World,
                    engine.MapLoader.TemplateRegistry,
                    engine.MapLoader.EntityTemplateKeys,
                    stableIds,
                    tagOps,
                    engine.GetService(CoreServiceKeys.PresenterEntityRuntime),
                    engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)),
                tagOps);
        }

        /// <summary>测试/裸世界挂载(与 SangoCityNativeRuntime.AttachBare 同款服务直构)。</summary>
        public static SangoTroopNativeRuntime AttachBare(World world, EntityLifecycleRuntimeServices services, Ludots.Core.Gameplay.GAS.TagOps tagOps)
        {
            if (Active is { IsDisposed: false })
            {
                Active.Dispose();
            }

            return new SangoTroopNativeRuntime(world, services, tagOps);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            UnsubscribeKernelFaces();
            foreach (Entity entity in _troops.Values)
            {
                if (_world.IsAlive(entity))
                {
                    _world.Destroy(entity);
                }
            }

            foreach (Entity entity in _corps.Values)
            {
                if (_world.IsAlive(entity))
                {
                    _world.Destroy(entity);
                }
            }

            _troops.Clear();
            _corps.Clear();
            _lastObserved.Clear();
            _lastCorpsAp.Clear();
            if (ReferenceEquals(Active, this))
            {
                Active = null;
            }

            _disposed = true;
        }

        // ---- 对外读取面(digest / 测试探针) ----

        /// <summary>部队域 digest 行(组件源,正式源;读缝同步后产出):id:军团:势力:x:y:兵力。</summary>
        public IReadOnlyList<string> TroopDigestRows()
        {
            if (_disposed)
            {
                throw new InvalidOperationException("SangoTroopNativeRuntime is disposed; troop digest rows require an attached runtime.");
            }

            SyncAll();
            _digestRows.Clear();
            List<KeyValuePair<int, Entity>> pairs = new(_troops);
            pairs.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (KeyValuePair<int, Entity> pair in pairs)
            {
                if (!_world.IsAlive(pair.Value))
                {
                    continue;
                }

                Entity entity = pair.Value;
                ref readonly AttributeBuffer attributes = ref _world.Get<AttributeBuffer>(entity);
                SangoTroopComposition composition = _world.Get<SangoTroopComposition>(entity);
                SangoTroopPosition position = _world.Get<SangoTroopPosition>(entity);
                _digestRows.Add(
                    $"troop {pair.Key}:" +
                    $"{composition.CorpsId}:" +
                    $"{composition.ForceId}:" +
                    $"{position.CellX}:{position.CellY}:" +
                    $"{(int)attributes.GetCurrent(SangoTroopAttributes.TroopsId)}");
            }

            return _digestRows;
        }

        /// <summary>内核源部队 digest 行(未挂载运行时的进程用;与既有 WorldDigest 部队行同格式)。</summary>
        public static List<string> KernelTroopRows(Scenario scenario)
        {
            ArgumentNullException.ThrowIfNull(scenario);
            var rows = new List<string>();
            scenario.troopsSet.ForEach(troop =>
            {
                if (troop != null && troop.IsAlive)
                {
                    rows.Add($"troop {troop.Id}:{troop.mBelongCorps?.Id ?? 0}:{troop.mBelongForce?.Id ?? 0}:{troop.x}:{troop.y}:{troop.troops}");
                }
            });
            return rows;
        }

        /// <summary>部队探针(测试/取证面):组件源字段全量。</summary>
        public readonly record struct TroopProbe(
            int TroopId,
            string Name,
            int Troops,
            int Morale,
            int Food,
            int CellX,
            int CellY,
            Vector2 PositionCm,
            in SangoTroopComposition Composition,
            in SangoTroopMission Mission,
            in SangoTroopMovement Movement,
            in SangoTroopSkillCooldowns SkillCooldowns,
            in SangoTroopCaptives Captives,
            in SangoTroopLedger Ledger);

        public List<TroopProbe> TroopSnapshot()
        {
            var probes = new List<TroopProbe>();
            if (_disposed)
            {
                return probes;
            }

            foreach (KeyValuePair<int, Entity> pair in _troops)
            {
                if (!_world.IsAlive(pair.Value))
                {
                    continue;
                }

                Entity entity = pair.Value;
                ref readonly AttributeBuffer attributes = ref _world.Get<AttributeBuffer>(entity);
                Fix64Vec2 position = _world.Get<WorldPositionCm>(entity).Value;
                probes.Add(new TroopProbe(
                    pair.Key,
                    _world.Get<Name>(entity).Value,
                    (int)attributes.GetCurrent(SangoTroopAttributes.TroopsId),
                    (int)attributes.GetCurrent(SangoTroopAttributes.MoraleId),
                    (int)attributes.GetCurrent(SangoTroopAttributes.FoodId),
                    _world.Get<SangoTroopPosition>(entity).CellX,
                    _world.Get<SangoTroopPosition>(entity).CellY,
                    new Vector2(position.X.ToFloat(), position.Y.ToFloat()),
                    _world.Get<SangoTroopComposition>(entity),
                    _world.Get<SangoTroopMission>(entity),
                    _world.Get<SangoTroopMovement>(entity),
                    _world.Get<SangoTroopSkillCooldowns>(entity),
                    _world.Get<SangoTroopCaptives>(entity),
                    _world.Get<SangoTroopLedger>(entity)));
            }

            return probes;
        }

        /// <summary>军团探针(测试/取证面):AP/jobCounter/编成/账本组件全量。</summary>
        public readonly record struct CorpsProbe(
            int CorpsId,
            string Name,
            int ActionPoint,
            int RewardJobCounter,
            in SangoCorpsMembership Membership,
            in SangoCorpsLedger Ledger);

        public List<CorpsProbe> CorpsSnapshot()
        {
            var probes = new List<CorpsProbe>();
            if (_disposed)
            {
                return probes;
            }

            foreach (KeyValuePair<int, Entity> pair in _corps)
            {
                if (!_world.IsAlive(pair.Value))
                {
                    continue;
                }

                Entity entity = pair.Value;
                probes.Add(new CorpsProbe(
                    pair.Key,
                    _world.Get<Name>(entity).Value,
                    _world.Get<SangoCorpsCommand>(entity).ActionPoint,
                    _world.Get<SangoCorpsJobCounters>(entity).RewardCounter,
                    _world.Get<SangoCorpsMembership>(entity),
                    _world.Get<SangoCorpsLedger>(entity)));
            }

            return probes;
        }

        // ---- 读面支撑(组件/属性源;挂载态实体缺席即接缝漂移,类型化抛错) ----

        internal Entity TroopEntityOrThrow(Troop troop)
        {
            if (_troops.TryGetValue(troop.Id, out Entity entity) && _world.IsAlive(entity))
            {
                return entity;
            }

            throw new InvalidOperationException(
                $"SangoTroopNativeRuntime has no entity for troop {troop.Id}; the native troop read face requires a materialized entity.");
        }

        internal Entity CorpsEntityOrThrow(Corps corps)
        {
            if (_corps.TryGetValue(corps.Id, out Entity entity) && _world.IsAlive(entity))
            {
                return entity;
            }

            throw new InvalidOperationException(
                $"SangoTroopNativeRuntime has no entity for corps {corps.Id}; the native corps read face requires a materialized entity.");
        }

        internal T CorpsComponentOrThrow<T>(Corps corps)
            where T : struct
        {
            return _world.Get<T>(CorpsEntityOrThrow(corps));
        }

        // ---- 写面落账(经 SangoTroopWriteFace/SangoCorpsWriteFace;内核 write-through 已在写面完成) ----

        /// <summary>部队任务态写面落账(SetMission 后的组件镜像)。</summary>
        public void OnNativeMission(Troop troop)
        {
            if (!IsCurrentKernel || troop == null || !_troops.TryGetValue(troop.Id, out Entity entity) || !_world.IsAlive(entity))
            {
                return;
            }

            _world.Set(entity, MissionOf(troop));
        }

        /// <summary>军团 AP 写面落账:内核扣减后的真值同步(含玩家门不扣的 0 变化,幂等)。</summary>
        public void OnNativeCorpsAp(Corps corps)
        {
            if (!IsCurrentKernel || corps == null || !_corps.TryGetValue(corps.Id, out Entity entity) || !_world.IsAlive(entity))
            {
                return;
            }

            SangoCorpsCommand command = _world.Get<SangoCorpsCommand>(entity);
            command.ActionPoint = corps.ActionPoint;
            _world.Set(entity, command);
        }

        /// <summary>军团 jobCounter 写面落账(Reward 切片)。</summary>
        public void OnNativeCorpsJobCounter(Corps corps)
        {
            if (!IsCurrentKernel || corps == null || !_corps.TryGetValue(corps.Id, out Entity entity) || !_world.IsAlive(entity))
            {
                return;
            }

            SangoCorpsJobCounters counters = _world.Get<SangoCorpsJobCounters>(entity);
            counters.RewardCounter = corps.GetJobCounter((int)CityJobType.Reward);
            _world.Set(entity, counters);
        }

        // ---- 回合/事件编排 ----

        /// <summary>每帧对账(引擎系统驱动):内核世界替换即重建。</summary>
        public void Reconcile()
        {
            if (_disposed)
            {
                return;
            }

            Scenario? current = Scenario.Cur;
            if (current == null || ReferenceEquals(_scenario, current))
            {
                return;
            }

            RebuildAll(current);
        }

        /// <summary>回合/势力回合边界全量同步(内核保留面镜像对账:耗粮/断粮/AP 发放)。</summary>
        public void SettleTurnEnd()
        {
            if (_disposed || !ReferenceEquals(_scenario, Scenario.Cur))
            {
                return;
            }

            SyncAll();
        }

        /// <summary>全量同步(幂等):内核 PONO 真值 → 组件/属性;死部队实体修剪。读缝与回合边界调用。</summary>
        public void SyncAll()
        {
            if (_disposed || !ReferenceEquals(_scenario, Scenario.Cur))
            {
                return;
            }

            List<int>? deadTroops = null;
            _scenario.troopsSet.ForEach(troop =>
            {
                if (troop == null || !troop.IsAlive)
                {
                    return;
                }

                if (_troops.TryGetValue(troop.Id, out Entity entity) && _world.IsAlive(entity))
                {
                    SyncTroopInto(entity, troop);
                }
                else if (!_troops.ContainsKey(troop.Id))
                {
                    // 事件面缺席的存活部队(如回灌世界装载序)补物化。
                    MaterializeTroop(troop);
                }
            });

            foreach (KeyValuePair<int, Entity> pair in _troops)
            {
                Troop? troop = _scenario.troopsSet.Get(pair.Key);
                if (troop == null || !troop.IsAlive)
                {
                    (deadTroops ??= new List<int>()).Add(pair.Key);
                }
            }

            if (deadTroops != null)
            {
                foreach (int troopId in deadTroops)
                {
                    RemoveTroop(troopId, countClear: false);
                }
            }

            _scenario.corpsSet.ForEach(corps =>
            {
                if (corps == null)
                {
                    return;
                }

                if (_corps.TryGetValue(corps.Id, out Entity entity) && _world.IsAlive(entity))
                {
                    SyncCorpsInto(entity, corps);
                }
                else if (!_corps.ContainsKey(corps.Id))
                {
                    MaterializeCorps(corps);
                }
            });

            List<int>? deadCorps = null;
            foreach (KeyValuePair<int, Entity> pair in _corps)
            {
                Corps? corps = _scenario.corpsSet.Get(pair.Key);
                if (corps == null)
                {
                    (deadCorps ??= new List<int>()).Add(pair.Key);
                }
            }

            if (deadCorps != null)
            {
                foreach (int corpsId in deadCorps)
                {
                    RemoveCorps(corpsId);
                }
            }
        }

        /// <summary>单部队同步(读缝增量)。</summary>
        public void SyncTroop(Troop troop)
        {
            if (_disposed || !ReferenceEquals(_scenario, Scenario.Cur) || troop == null)
            {
                return;
            }

            if (_troops.TryGetValue(troop.Id, out Entity entity) && _world.IsAlive(entity))
            {
                SyncTroopInto(entity, troop);
            }
        }

        /// <summary>单军团同步(读缝增量:AP 门槛先落账真值再读)。</summary>
        public void SyncCorps(Corps corps)
        {
            if (_disposed || !ReferenceEquals(_scenario, Scenario.Cur) || corps == null)
            {
                return;
            }

            if (_corps.TryGetValue(corps.Id, out Entity entity) && _world.IsAlive(entity))
            {
                SyncCorpsInto(entity, corps);
            }
        }

        void SyncTroopInto(Entity entity, Troop troop)
        {
            SetBase(entity, SangoTroopAttributes.TroopsId, troop.troops);
            SetBase(entity, SangoTroopAttributes.MoraleId, troop.morale);
            SetBase(entity, SangoTroopAttributes.FoodId, troop.food);

            Vector2 position = CellToCm(_scenario.Map, troop.x, troop.y);
            _world.Set(entity, new SangoTroopPosition { CellX = troop.x, CellY = troop.y });
            _world.Set(entity, WorldPositionCm.FromCmFloat(position.X, position.Y));
            if (_world.Has<VisualTransform>(entity))
            {
                VisualTransform transform = _world.Get<VisualTransform>(entity);
                transform.Position = WorldPlane2D.LogicCmToVisualMeters(position.X, position.Y, 0f);
                _world.Set(entity, transform);
            }

            _world.Set(entity, new SangoTroopComposition
            {
                LeaderId = troop.Leader?.Id ?? 0,
                Member1Id = troop.Member1?.Id ?? 0,
                Member2Id = troop.Member2?.Id ?? 0,
                LandTroopTypeId = troop.LandTroopType?.Id ?? 0,
                WaterTroopTypeId = troop.WaterTroopType?.Id ?? 0,
                ForceId = troop.mBelongForce?.Id ?? 0,
                CorpsId = troop.mBelongCorps?.Id ?? 0,
                BelongCityId = troop.mBelongCity?.Id ?? 0,
            });

            _world.Set(entity, MissionOf(troop));

            _world.Set(entity, new SangoTroopMovement
            {
                IsMoving = troop.isMoving ? (byte)1 : (byte)0,
                MoveRangeCount = troop.MoveRange?.Count ?? 0,
            });

            _world.Set(entity, CooldownsOf(troop));
            _world.Set(entity, CaptivesOf(troop));

            // 回合账本:耗粮/携粮/出征天数为内核结算观测;士气净变/断粮损耗按上一拍
            // 观测差计(同步窗净变,公式精确断言在测试面按内核输入独立复算)。
            SangoTroopLedger ledger = _world.Get<SangoTroopLedger>(entity);
            int moraleDelta = 0;
            int troopsDelta = 0;
            if (_lastObserved.TryGetValue(troop.Id, out (int Morale, int Troops) last))
            {
                moraleDelta = troop.morale - last.Morale;
                troopsDelta = troop.troops - last.Troops;
            }

            _lastObserved[troop.Id] = (troop.morale, troop.troops);
            ledger.LastFoodCost = troop.foodCost;
            ledger.LastFood = troop.food;
            ledger.LastMoraleDelta = moraleDelta;
            ledger.LastStarveDamage = troop.food <= 0 && troopsDelta < 0 ? -troopsDelta : 0;
            ledger.LiveDays = troop.liveDays;
            ledger.Starving = troop.food <= 0 ? (byte)1 : (byte)0;
            _world.Set(entity, ledger);
        }

        static SangoTroopMission MissionOf(Troop troop) => new()
        {
            MissionType = troop.missionType,
            MissionTarget = troop.missionTarget,
            MissionParams1 = troop.missionParams1,
            MissionParams2 = troop.missionParams2,
            MissionTargetCellX = troop.missionTargetCell?.x ?? 0,
            MissionTargetCellY = troop.missionTargetCell?.y ?? 0,
            HasMissionTargetCell = troop.missionTargetCell != null ? (byte)1 : (byte)0,
        };

        static SangoTroopSkillCooldowns CooldownsOf(Troop troop)
        {
            var cooldowns = default(SangoTroopSkillCooldowns);
            AppendCooldowns(ref cooldowns, troop.landSkills);
            AppendCooldowns(ref cooldowns, troop.waterSkills);
            AppendCooldowns(ref cooldowns, troop.StrategySkills);
            return cooldowns;
        }

        static void AppendCooldowns(ref SangoTroopSkillCooldowns cooldowns, List<SkillInstance>? skills)
        {
            if (skills == null)
            {
                return;
            }

            foreach (SkillInstance? skill in skills)
            {
                if (skill?.skill == null)
                {
                    continue;
                }

                if (cooldowns.Count >= SangoTroopSkillCooldowns.Capacity)
                {
                    throw new InvalidOperationException(
                        $"SangoTroopSkillCooldowns capacity ({SangoTroopSkillCooldowns.Capacity}) exceeded; refusing to truncate the cooldown roster.");
                }

                cooldowns.Entries[cooldowns.Count++] = new SangoTroopSkillCooldowns.SkillCdEntry
                {
                    SkillId = skill.skill.Id,
                    Cd = skill.CDCount,
                };
            }
        }

        static SangoTroopCaptives CaptivesOf(Troop troop)
        {
            var captives = default(SangoTroopCaptives);
            if (troop.captiveList == null)
            {
                return captives;
            }

            for (int i = 0; i < troop.captiveList.Count; i++)
            {
                Person? person = troop.captiveList.Get(i);
                if (person == null)
                {
                    continue;
                }

                if (captives.Count >= SangoTroopCaptives.Capacity)
                {
                    throw new InvalidOperationException(
                        $"SangoTroopCaptives capacity ({SangoTroopCaptives.Capacity}) exceeded; refusing to truncate the captive roster.");
                }

                captives.PersonIds[captives.Count++] = person.Id;
            }

            return captives;
        }

        void SyncCorpsInto(Entity entity, Corps corps)
        {
            var command = new SangoCorpsCommand
            {
                ForceId = corps.mBelongForce?.Id ?? 0,
                CommanderId = corps.mComander?.Id ?? 0,
                Number = corps.number,
                ActionPoint = corps.ActionPoint,
                AIPrepared = corps.AIPrepared ? (byte)1 : (byte)0,
                AIFinished = corps.AIFinished ? (byte)1 : (byte)0,
                ActionOver = corps.ActionOver ? (byte)1 : (byte)0,
            };
            _world.Set(entity, command);

            _world.Set(entity, new SangoCorpsJobCounters
            {
                RewardCounter = corps.GetJobCounter((int)CityJobType.Reward),
            });

            // 编成成员保序引用:内核 Corps.ForEachCity 同序(citySet 扫描序 + IsCity 门),
            // 聚合计数与 Corps.PrepareCityInfo 同源。
            var membership = default(SangoCorpsMembership);
            for (int i = 0; i < _scenario.citySet.Count; i++)
            {
                City? city = _scenario.citySet[i];
                if (city == null || !city.IsAlive || city.mBelongCorps != corps || !city.IsCity())
                {
                    continue;
                }

                if (membership.CityCount >= SangoCorpsMembership.Capacity)
                {
                    throw new InvalidOperationException(
                        $"SangoCorpsMembership capacity ({SangoCorpsMembership.Capacity}) exceeded; refusing to truncate the ordered corps roster.");
                }

                membership.CityIds[membership.CityCount++] = city.Id;
                membership.PersonCount += city.allPersons.Count;
                membership.GoldTotal += city.gold;
                membership.TroopsTotal += city.troops;
                membership.FoodTotal += city.food;
            }

            for (int i = 0; i < _scenario.troopsSet.Count; i++)
            {
                Troop? troop = _scenario.troopsSet[i];
                if (troop != null && troop.IsAlive && troop.mBelongCorps == corps)
                {
                    membership.TroopCount++;
                }
            }

            _world.Set(entity, membership);

            // AP 发放账本:对上一拍观测的正差(发放是回合内唯一增量;玩家门扣减不产生
            // 正差,公式精确断言在测试面按内核输入复算)。
            SangoCorpsLedger ledger = _world.Get<SangoCorpsLedger>(entity);
            if (_lastCorpsAp.TryGetValue(corps.Id, out int lastAp))
            {
                ledger.LastApGranted = Math.Max(0, corps.ActionPoint - lastAp);
            }

            _lastCorpsAp[corps.Id] = corps.ActionPoint;
            ledger.LastApTotal = corps.ActionPoint;
            _world.Set(entity, ledger);
        }

        // ---- 物化/销毁(生灭事件驱动;MaterializeTemplate Layer 0 op) ----

        void MaterializeTroop(Troop troop)
        {
            Vector2 position = CellToCm(_scenario.Map, troop.x, troop.y);
            Entity entity = EntityLifecycleAtomicOps.MaterializeTemplate(
                _services, Entity.Null, TroopTemplateId,
                Fix64Vec2.FromFloat(position.X, position.Y));
            _world.Set(entity, new Name { Value = TroopDisplayName(troop) });
            _world.Add(entity, new SangoTrooperIdentity { TroopId = troop.Id });
            _world.Add(entity, default(SangoTroopPosition));
            _world.Add(entity, default(SangoTroopComposition));
            _world.Add(entity, default(SangoTroopMission));
            _world.Add(entity, default(SangoTroopMovement));
            _world.Add(entity, default(SangoTroopSkillCooldowns));
            _world.Add(entity, default(SangoTroopCaptives));
            _world.Add(entity, default(SangoTroopLedger));
            // presenter owner 迁移:部队实体即标记 owner(VisualTransform/CullState 为
            // 锚定组件,引擎 PresenterEntityTransformSyncSystem 逐帧跟随;Y=0 仅锚点,
            // SnapToGround 采样抬高同城标)。VisualTransform 是米域,格位厘米须换算。
            _world.Add(entity, new VisualTransform
            {
                Position = WorldPlane2D.LogicCmToVisualMeters(position.X, position.Y, 0f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
            });
            _world.Add(entity, new CullState { IsVisible = true, LOD = LODLevel.High });
            _troops[troop.Id] = entity;
            TroopSpawnCount++;
            InitializeTroopAttributes(entity, troop);
            SyncTroopInto(entity, troop);
        }

        // 物化期初值直写 AttributeBuffer(与模板数据初始化同义,非变更;同
        // SangoPersonNativeRuntime.InitializePersonAttributes 的容量理由,保持同波同式)。
        void InitializeTroopAttributes(Entity entity, Troop troop)
        {
            ref AttributeBuffer attributes = ref _world.Get<AttributeBuffer>(entity);
            attributes.SetBase(SangoTroopAttributes.TroopsId, troop.troops);
            attributes.SetBase(SangoTroopAttributes.MoraleId, troop.morale);
            attributes.SetBase(SangoTroopAttributes.FoodId, troop.food);
        }

        void MaterializeCorps(Corps corps)
        {
            Entity entity = EntityLifecycleAtomicOps.MaterializeTemplate(
                _services, Entity.Null, CorpsTemplateId, Fix64Vec2.Zero);
            _world.Set(entity, new Name { Value = CorpsDisplayName(corps) });
            _world.Add(entity, new SangoCorpsIdentity { CorpsId = corps.Id });
            _world.Add(entity, default(SangoCorpsCommand));
            _world.Add(entity, default(SangoCorpsJobCounters));
            _world.Add(entity, default(SangoCorpsMembership));
            _world.Add(entity, default(SangoCorpsLedger));
            _corps[corps.Id] = entity;
            SyncCorpsInto(entity, corps);
        }

        void RemoveTroop(int troopId, bool countClear)
        {
            if (countClear)
            {
                TroopClearCount++;
            }

            _lastObserved.Remove(troopId);
            if (_troops.Remove(troopId, out Entity entity) && _world.IsAlive(entity))
            {
                _world.Destroy(entity);
            }
        }

        void RemoveCorps(int corpsId)
        {
            _lastCorpsAp.Remove(corpsId);
            if (_corps.Remove(corpsId, out Entity entity) && _world.IsAlive(entity))
            {
                _world.Destroy(entity);
            }
        }

        void RebuildAll(Scenario scenario)
        {
            foreach (Entity entity in _troops.Values)
            {
                if (_world.IsAlive(entity))
                {
                    _world.Destroy(entity);
                }
            }

            foreach (Entity entity in _corps.Values)
            {
                if (_world.IsAlive(entity))
                {
                    _world.Destroy(entity);
                }
            }

            _troops.Clear();
            _corps.Clear();
            _lastObserved.Clear();
            _lastCorpsAp.Clear();
            _scenario = scenario;
            scenario.corpsSet.ForEach(corps =>
            {
                if (corps != null)
                {
                    MaterializeCorps(corps);
                }
            });
            scenario.troopsSet.ForEach(troop =>
            {
                if (troop != null && troop.IsAlive)
                {
                    MaterializeTroop(troop);
                }
            });
        }

        void SetBase(Entity entity, int attributeId, float value)
        {
            Ludots.Core.Gameplay.GAS.AttributeMutationOps.SetBase(_world, entity, attributeId, value, _tagOps);
        }

        static string TroopDisplayName(Troop troop) => $"[sango.troop {troop.Id}] {troop.Name ?? string.Empty}";

        static string CorpsDisplayName(Corps corps) => $"[sango.corps {corps.Id}] {corps.Name ?? string.Empty}";

        // 坐标换算与镜像/城标记同轴:内核格 x=北、y=东;逻辑位取 (东, 北) 即 (X, Y),
        // 单位 cm,原点为地图中心。
        static Vector2 CellToCm(Map map, int cellX, int cellY)
        {
            float cellMeters = map.GridSize;
            float halfWorldCm = map.Width * cellMeters * 100f / 2f;
            return new Vector2(
                (cellY * cellMeters + cellMeters * 0.5f) * 100f - halfWorldCm,
                (cellX * cellMeters + cellMeters * 0.5f) * 100f - halfWorldCm);
        }

        // ---- 内核事件订阅(部队/军团结算体是虚方法链,无可替换订阅面——闸门在案结论;
        //      订阅全部为追加式:结构/摆位即时化 + 回合边界落账,陈旧世界一律 no-op) ----

        void SubscribeKernelFaces()
        {
            GameEvent.OnTroopCreated -= OnTroopCreated;
            GameEvent.OnTroopCreated += OnTroopCreated;
            GameEvent.OnTroopEnterCell -= OnTroopEnterCell;
            GameEvent.OnTroopEnterCell += OnTroopEnterCell;
            GameEvent.OnTroopClear -= OnTroopGone;
            GameEvent.OnTroopClear += OnTroopGone;
            GameEvent.OnTroopDestroyed -= OnTroopDestroyed;
            GameEvent.OnTroopDestroyed += OnTroopDestroyed;
            GameEvent.OnTroopTurnStart -= OnTroopTurnStart;
            GameEvent.OnTroopTurnStart += OnTroopTurnStart;
            GameEvent.OnForceTurnStart -= OnForceTurnStart;
            GameEvent.OnForceTurnStart += OnForceTurnStart;
            GameEvent.OnTurnEnd -= OnTurnEnd;
            GameEvent.OnTurnEnd += OnTurnEnd;
            GameEvent.OnCityFall -= OnCityFall;
            GameEvent.OnCityFall += OnCityFall;
            GameEvent.OnCorpsCreate -= OnCorpsCreate;
            GameEvent.OnCorpsCreate += OnCorpsCreate;
            GameEvent.OnCorpsDelete -= OnCorpsDelete;
            GameEvent.OnCorpsDelete += OnCorpsDelete;
            SangoCommandJournal.CommandEffectsApplied -= OnCommandEffectsApplied;
            SangoCommandJournal.CommandEffectsApplied += OnCommandEffectsApplied;
        }

        void UnsubscribeKernelFaces()
        {
            GameEvent.OnTroopCreated -= OnTroopCreated;
            GameEvent.OnTroopEnterCell -= OnTroopEnterCell;
            GameEvent.OnTroopClear -= OnTroopGone;
            GameEvent.OnTroopDestroyed -= OnTroopDestroyed;
            GameEvent.OnTroopTurnStart -= OnTroopTurnStart;
            GameEvent.OnForceTurnStart -= OnForceTurnStart;
            GameEvent.OnTurnEnd -= OnTurnEnd;
            GameEvent.OnCityFall -= OnCityFall;
            GameEvent.OnCorpsCreate -= OnCorpsCreate;
            GameEvent.OnCorpsDelete -= OnCorpsDelete;
            SangoCommandJournal.CommandEffectsApplied -= OnCommandEffectsApplied;
        }

        void OnTroopCreated(Troop troop, Scenario scenario)
        {
            if (!IsCurrentKernel || troop == null || !troop.IsAlive)
            {
                return;
            }

            if (_troops.ContainsKey(troop.Id))
            {
                return;
            }

            MaterializeTroop(troop);
        }

        // 逐步移动(每格一事件):世界位/格位/VisualTransform 即时跟随。
        void OnTroopEnterCell(Troop troop, Cell destCell, Cell lastCell)
        {
            if (!IsCurrentKernel || troop == null || destCell == null)
            {
                return;
            }

            if (!_troops.TryGetValue(troop.Id, out Entity entity) || !_world.IsAlive(entity))
            {
                return;
            }

            Vector2 position = CellToCm(_scenario.Map, destCell.x, destCell.y);
            _world.Set(entity, new SangoTroopPosition { CellX = destCell.x, CellY = destCell.y });
            _world.Set(entity, WorldPositionCm.FromCmFloat(position.X, position.Y));
            if (_world.Has<VisualTransform>(entity))
            {
                VisualTransform transform = _world.Get<VisualTransform>(entity);
                transform.Position = WorldPlane2D.LogicCmToVisualMeters(position.X, position.Y, 0f);
                _world.Set(entity, transform);
            }
        }

        // 解散共用路径(EnterCity 回城/灭国吸收):部队实体消灭。
        void OnTroopGone(Troop troop, Scenario scenario)
        {
            if (!IsCurrentKernel || troop == null)
            {
                return;
            }

            if (_troops.ContainsKey(troop.Id))
            {
                TroopClearCount++;
                RemoveTroop(troop.Id, countClear: false);
                SyncAll();
            }
        }

        // 溃灭(OnDestroy 后随 Clear 到;两事件幂等,先到先销)。
        void OnTroopDestroyed(Troop troop, SangoObject attacker, int damage, Scenario scenario)
        {
            if (!IsCurrentKernel || troop == null)
            {
                return;
            }

            if (_troops.ContainsKey(troop.Id))
            {
                TroopDestroyCount++;
                RemoveTroop(troop.Id, countClear: false);
                SyncAll();
            }
        }

        // 势力回合开始(结算体已跑完:耗粮/断粮士气/CD 推进/AP 发放/jobCounter 清零)
        // —— 该势力部队/军团的镜像对账位。
        void OnForceTurnStart(Force force, Scenario scenario)
        {
            if (!IsCurrentKernel || force == null)
            {
                return;
            }

            _scenario.troopsSet.ForEach(troop =>
            {
                if (troop != null && troop.IsAlive && troop.mBelongForce == force)
                {
                    SyncTroop(troop);
                }
            });

            _scenario.corpsSet.ForEach(corps =>
            {
                if (corps != null && corps.mBelongForce == force)
                {
                    SyncCorps(corps);
                }
            });
        }

        // 单部队回合结算尾(部队结算体末行):耗粮/士气/CD 落账。
        void OnTroopTurnStart(Troop troop, Scenario scenario) => SyncTroop(troop);

        void OnTurnEnd(Scenario scenario) => SettleTurnEnd();

        // 城陷:攻方部队入城献俘/吸收与守方军团编成变化的收敛位。
        void OnCityFall(City city, Force lastBelongForce, Troop attacker) => SettleTurnEnd();

        void OnCorpsCreate(Corps corps, Scenario scenario)
        {
            if (!IsCurrentKernel || corps == null)
            {
                return;
            }

            if (_corps.ContainsKey(corps.Id))
            {
                return;
            }

            MaterializeCorps(corps);
        }

        void OnCorpsDelete(Corps corps, Scenario scenario)
        {
            if (!IsCurrentKernel || corps == null)
            {
                return;
            }

            RemoveCorps(corps.Id);
        }

        void OnCommandEffectsApplied() => SettleTurnEnd();
    }

    /// <summary>
    /// 部队写面(任务态):内核 write-through(经内核成员,SetMission 的
    /// NeedPrepareMission 位原样触发)+ 组件镜像。未挂载原生运行时时退化为纯内核写
    /// (语义与内核逐位同)。
    /// </summary>
    public static class SangoTroopWriteFace
    {
        /// <summary>授任务(Troop.SetMission 语义)。</summary>
        public static void ApplyMission(Troop troop, MissionType missionType, int missionTarget)
        {
            ArgumentNullException.ThrowIfNull(troop);
            troop.SetMission(missionType, missionTarget);
            SangoTroopNativeRuntime.Active?.OnNativeMission(troop);
        }

        /// <summary>委任移动目标格(TroopInteractiveMoveToCell 的 OnEnter 段:missionParams1/2=目标格坐标)。</summary>
        public static void ApplyCommissionTarget(Troop troop, Cell destCell)
        {
            ArgumentNullException.ThrowIfNull(troop);
            ArgumentNullException.ThrowIfNull(destCell);
            troop.missionParams1 = destCell.x;
            troop.missionParams2 = destCell.y;
            troop.NeedPrepareMission();
            SangoTroopNativeRuntime.Active?.OnNativeMission(troop);
        }

        /// <summary>清任务(Troop.ClearMission 语义)。</summary>
        public static void ApplyClearMission(Troop troop)
        {
            ArgumentNullException.ThrowIfNull(troop);
            troop.ClearMission();
            SangoTroopNativeRuntime.Active?.OnNativeMission(troop);
        }
    }

    /// <summary>
    /// 军团读面(消 D-1' 桥 #4):AP 门槛/军团级 jobCounter 的组件源。挂载且为当前世界
    /// 权威时读组件(读缝先同步该军团);否则读内核 PONO(城原生单挂的对拍面,语义
    /// 同源)。挂载态下实体缺席即类型化抛错(接缝漂移显式失败,不静默回退)。
    /// PONO 引用仅作 id 键(城 → mBelongCorps.Id 查组件,P1 合同)。
    /// </summary>
    public static class SangoCorpsReadFace
    {
        /// <summary>军团行动力(编成门槛/内政令门槛)。</summary>
        public static int ActionPoint(City city)
        {
            ArgumentNullException.ThrowIfNull(city);
            Corps? corps = city.mBelongCorps;
            if (corps == null)
            {
                return 0;
            }

            var runtime = SangoTroopNativeRuntime.Active;
            if (runtime is { IsDisposed: false, IsCurrentKernel: true })
            {
                runtime.SyncCorps(corps);
                return runtime.CorpsComponentOrThrow<SangoCorpsCommand>(corps).ActionPoint;
            }

            return corps.ActionPoint;
        }

        /// <summary>军团级工作计数(Reward 门槛"本回合未褒奖")。</summary>
        public static int JobCounter(City city, int jobId)
        {
            ArgumentNullException.ThrowIfNull(city);
            Corps? corps = city.mBelongCorps;
            if (corps == null)
            {
                return 0;
            }

            var runtime = SangoTroopNativeRuntime.Active;
            if (runtime is { IsDisposed: false, IsCurrentKernel: true } &&
                jobId == (int)CityJobType.Reward)
            {
                runtime.SyncCorps(corps);
                return runtime.CorpsComponentOrThrow<SangoCorpsJobCounters>(corps).RewardCounter;
            }

            // 非切片面暂读内核字典(城域命令面只消费 Reward 切片;字典全量化随内核
            // 军团终局),域边界登记于 SangoLegacyBridge。
            return corps.GetJobCounter(jobId);
        }
    }

    /// <summary>
    /// 军团写面(消 D-1' 桥 #4):AP 扣减与军团级 jobCounter。内核 write-through
    /// (ReduceActionPoint 的 IsPlayer 门与 OnCorpsActionPointChange 事件位原样保持;
    /// 未挂载时退化为纯内核写)。
    /// </summary>
    public static class SangoCorpsWriteFace
    {
        /// <summary>扣行动力(Corps.ReduceActionPoint:玩家军团才实扣,事件位保持)。</summary>
        public static void ReduceActionPoint(City city, int amount)
        {
            ArgumentNullException.ThrowIfNull(city);
            Corps? corps = city.mBelongCorps;
            if (corps == null)
            {
                return;
            }

            corps.ReduceActionPoint(amount);
            SangoTroopNativeRuntime.Active?.OnNativeCorpsAp(corps);
        }

        /// <summary>军团级工作计数 +1(Corps.AddJobCounter)。</summary>
        public static void AddJobCounter(City city, int jobId)
        {
            ArgumentNullException.ThrowIfNull(city);
            Corps? corps = city.mBelongCorps;
            if (corps == null)
            {
                return;
            }

            corps.AddJobCounter(jobId);
            SangoTroopNativeRuntime.Active?.OnNativeCorpsJobCounter(corps);
        }
    }

    /// <summary>
    /// 原生部队/军团域·回合结算系统(SystemGroup.Cleanup:仿真结算后的部队/军团域
    /// 维护相位,与城/武将域结算同组):内核世界替换对账 + 回合边界全量落账(耗粮/
    /// 断粮士气/AP 发放的镜像对账)与运行时自举。headless 内核事件已先行落账,本面幂等。
    /// </summary>
    public sealed class SangoTroopSettlementSystem : ISystem<float>
    {
        readonly Func<GameEngine?> _engineSource;
        int _lastStampedTurn = -1;

        public SangoTroopSettlementSystem(Func<GameEngine?> engineSource)
        {
            _engineSource = engineSource ?? throw new ArgumentNullException(nameof(engineSource));
        }

        public void Initialize() { }

        public void BeforeUpdate(in float dt) { }

        public void Update(in float dt)
        {
            SangoTroopNativeRuntime? runtime = SangoTroopNativeRuntime.Active;
            if (runtime is not { IsDisposed: false })
            {
                GameEngine? engine = _engineSource();
                if (engine != null && Scenario.Cur != null)
                {
                    SangoTroopNativeRuntime.Attach(engine);
                }

                return;
            }

            runtime.Reconcile();
            Scenario? scenario = Scenario.Cur;
            if (scenario != null && runtime.IsCurrentKernel && scenario.Info.turnCount != _lastStampedTurn)
            {
                _lastStampedTurn = scenario.Info.turnCount;
                runtime.SettleTurnEnd();
            }
        }

        public void AfterUpdate(in float dt) { }

        public void Dispose() { }
    }
}
