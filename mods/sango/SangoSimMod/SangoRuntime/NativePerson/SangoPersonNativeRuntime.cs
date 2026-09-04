// D-2' 武将域原生实现运行时:物化 sango.person 实体群(850,上限 851 语义)、
// 内核回合/命令/武将事件面的组件落账、武将写面(训练功勋经验/褒奖忠诚/任务态,
// 组件写 + 内核 write-through)、武将读面(俸给/收获因子/命令门槛的组件源)、
// digest 武将行双源。
//
// 对拍合同(本波验收核心,同 D-1' 城域):
//   老内核跑一遍(本运行时不挂,digest 武将行=内核源)与原生武将域跑一遍(挂载,
//   组件源)同种子同命令流,全量 digest 逐位相等。等价性由两段证明构成——
//   1) 跨运行 write-through:原生写面把结果同时写内核 Person PONO(merit/GainExp/
//      ActionOver 经内核成员与 setter,事件位不变)——原生写错一位即内核偏移即失配;
//   2) 账本计值:SangoPersonLedger(功勋/经验差/忠诚差)为原生写面计量,测试按
//      内核公式独立复算断言。
//   内核保留面(换季掉忠 Force.OnSeasonStart、登场 Person.OnTurnStart、俘虏逃逸
//      City/Troop.OnForceTurnEnd 为虚方法链,无事件订阅面可交换——见闸门在案结论)
//   本波镜像对账:组件在回合边界与读缝同步 PONO 真值,消亡计划见 SangoLegacyBridge。
//
// 组件源时点权威:digest 读、俸给读、收获因子读、命令结算读四个读缝先同步再读
// (SyncAll 幂等:SetBase 同值省略为引擎既有语义,组件 Set 为覆盖写)。回合/日界
// 与命令漏斗订阅做常规刷新;武将生命周期六事件做单人增量同步。
//
// digest 双源:SangoTurnDriver.WorldDigest 的武将行——本运行时挂载且为当前世界
// 权威时读组件源(对拍后正式源);否则读内核源(KernelPersonRows,与既有格式逐位同形)。

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
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using Sango.Core;

namespace Sango.Runtime
{
    /// <summary>原生武将域运行时(进程单世界:SangoPersonNativeRuntime.Active)。</summary>
    public sealed class SangoPersonNativeRuntime : IDisposable
    {
        internal const string PersonTemplateId = "sango.person";

        /// <summary>内核 personSet 容量合同:id 0 空位 + 850 实存武将,越界即内核外武将。</summary>
        public const int PersonIdLimit = 851;

        readonly World _world;
        readonly EntityLifecycleRuntimeServices _services;
        readonly TagOps _tagOps;
        readonly Dictionary<int, Entity> _persons = new();
        readonly List<string> _digestRows = new();
        readonly int[] _stateCounts = new int[(int)PersonStateType.Max];
        Scenario _scenario;
        bool _disposed;

        public static SangoPersonNativeRuntime? Active { get; private set; }

        /// <summary>引擎世界(读面内部使用;不外露写路径)。</summary>
        internal World World => _world;

        SangoPersonNativeRuntime(World world, EntityLifecycleRuntimeServices services, Ludots.Core.Gameplay.GAS.TagOps tagOps)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _tagOps = tagOps ?? throw new ArgumentNullException(nameof(tagOps));
            _scenario = Scenario.Cur
                ?? throw new InvalidOperationException("SangoPersonNativeRuntime requires a booted kernel (Scenario.Cur).");
            SangoPersonAttributes.EnsureRegistered();

            SubscribeKernelFaces();
            RebuildAll(_scenario);
            Active = this;
        }

        public bool IsDisposed => _disposed;

        public int PersonCount => _persons.Count;

        /// <summary>换季掉忠观测(内核保留面):最近一次同步观测到的忠诚总降量(测试探针)。</summary>
        public int CaptureCount { get; private set; }

        public int EscapeCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public int ExecuteCount { get; private set; }

        /// <summary>权威性判定(通用合同照搬):世界被替换后、重建前,武将权威回到内核 PONO。</summary>
        public bool IsCurrentKernel => !_disposed && ReferenceEquals(_scenario, Scenario.Cur);

        /// <summary>引擎宿主挂载(幂等):内核世界与引擎世界未变即复用;已替换即重建。</summary>
        public static SangoPersonNativeRuntime Attach(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            Scenario scenario = Scenario.Cur
                ?? throw new InvalidOperationException("SangoPersonNativeRuntime attach requires a booted kernel (Scenario.Cur).");

            if (Active is { IsDisposed: false } existing &&
                ReferenceEquals(existing._scenario, scenario) &&
                existing._world == engine.World)
            {
                return existing;
            }

            Active?.Dispose();
            var stableIds = engine.GetService(CoreServiceKeys.PresentationStableIdAllocator)
                ?? throw new InvalidOperationException("SangoPersonNativeRuntime requires the engine PresentationStableIdAllocator service.");
            var tagOps = engine.GetService(CoreServiceKeys.TagOps) as Ludots.Core.Gameplay.GAS.TagOps
                ?? throw new InvalidOperationException("SangoPersonNativeRuntime requires the engine TagOps service.");
            return new SangoPersonNativeRuntime(
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
        public static SangoPersonNativeRuntime AttachBare(World world, EntityLifecycleRuntimeServices services, Ludots.Core.Gameplay.GAS.TagOps tagOps)
        {
            if (Active is { IsDisposed: false })
            {
                Active.Dispose();
            }

            return new SangoPersonNativeRuntime(world, services, tagOps);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            UnsubscribeKernelFaces();
            foreach (Entity entity in _persons.Values)
            {
                if (_world.IsAlive(entity))
                {
                    _world.Destroy(entity);
                }
            }

            _persons.Clear();
            if (ReferenceEquals(Active, this))
            {
                Active = null;
            }

            _disposed = true;
        }

        // ---- 对外读取面(digest / 测试探针) ----

        /// <summary>武将域 digest 行(组件源,正式源;读缝同步后产出):id:忠诚:势力:state。</summary>
        public IReadOnlyList<string> PersonDigestRows()
        {
            if (_disposed)
            {
                throw new InvalidOperationException("SangoPersonNativeRuntime is disposed; person digest rows require an attached runtime.");
            }

            SyncAll();
            _digestRows.Clear();
            List<KeyValuePair<int, Entity>> pairs = new(_persons);
            pairs.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (KeyValuePair<int, Entity> pair in pairs)
            {
                if (!_world.IsAlive(pair.Value))
                {
                    continue;
                }

                Entity entity = pair.Value;
                ref readonly AttributeBuffer attributes = ref _world.Get<AttributeBuffer>(entity);
                SangoPersonMembership membership = _world.Get<SangoPersonMembership>(entity);
                SangoPersonStatus status = _world.Get<SangoPersonStatus>(entity);
                _digestRows.Add(
                    $"person {pair.Key}:" +
                    $"{(int)attributes.GetCurrent(SangoPersonAttributes.LoyaltyId)}:" +
                    $"{membership.BelongForceId}:" +
                    $"{status.State}");
            }

            return _digestRows;
        }

        /// <summary>内核源武将 digest 行(未挂载运行时的进程用;与既有 WorldDigest 武将行同格式)。</summary>
        public static List<string> KernelPersonRows(Scenario scenario)
        {
            ArgumentNullException.ThrowIfNull(scenario);
            var rows = new List<string>();
            scenario.personSet.ForEach(person =>
            {
                if (person != null)
                {
                    rows.Add($"person {person.Id}:{person.loyalty}:{person.mBelongForce?.Id ?? 0}:{person.state}");
                }
            });
            return rows;
        }

        /// <summary>武将探针(测试/取证面):组件源字段全量 + 状态直方图。</summary>
        public readonly record struct PersonProbe(
            int PersonId,
            string Name,
            int Loyalty,
            int Command,
            int Strength,
            int Intelligence,
            int Politics,
            int State,
            bool ActionOver,
            int Merit,
            int Exp,
            int LevelId,
            int OfficialCost,
            SangoPersonMembership Membership,
            SangoPersonMission Mission,
            SangoPersonLedger Ledger,
            Vector2 PositionCm);

        public List<PersonProbe> Snapshot()
        {
            var probes = new List<PersonProbe>();
            if (_disposed)
            {
                return probes;
            }

            foreach (KeyValuePair<int, Entity> pair in _persons)
            {
                if (!_world.IsAlive(pair.Value))
                {
                    continue;
                }

                Entity entity = pair.Value;
                ref readonly AttributeBuffer attributes = ref _world.Get<AttributeBuffer>(entity);
                Fix64Vec2 position = _world.Get<WorldPositionCm>(entity).Value;
                probes.Add(new PersonProbe(
                    pair.Key,
                    _world.Get<Name>(entity).Value,
                    (int)attributes.GetCurrent(SangoPersonAttributes.LoyaltyId),
                    (int)attributes.GetCurrent(SangoPersonAttributes.CommandId),
                    (int)attributes.GetCurrent(SangoPersonAttributes.StrengthId),
                    (int)attributes.GetCurrent(SangoPersonAttributes.IntelligenceId),
                    (int)attributes.GetCurrent(SangoPersonAttributes.PoliticsId),
                    _world.Get<SangoPersonStatus>(entity).State,
                    _world.Get<SangoPersonStatus>(entity).ActionOver != 0,
                    _world.Get<SangoPersonCareer>(entity).Merit,
                    _world.Get<SangoPersonCareer>(entity).Exp,
                    _world.Get<SangoPersonCareer>(entity).LevelId,
                    _world.Get<SangoPersonCareer>(entity).OfficialCost,
                    _world.Get<SangoPersonMembership>(entity),
                    _world.Get<SangoPersonMission>(entity),
                    _world.Get<SangoPersonLedger>(entity),
                    new Vector2(position.X.ToFloat(), position.Y.ToFloat())));
            }

            return probes;
        }

        /// <summary>状态直方图(PersonStateType 序;同步时维护,结算观测探针)。</summary>
        public int[] StateCounts()
        {
            var counts = new int[(int)PersonStateType.Max];
            Array.Copy(_stateCounts, counts, counts.Length);
            return counts;
        }

        // ---- 读面:俸给组件源(消 D-1' 桥 #1/#2) ----

        /// <summary>
        /// 俸给(City.GoldCost 语义的组件源):官员俸 = 驻城武将(state ∈ 官职/普通面,
        /// 与内核 city.allPersons 成员同集)的 OfficialCost 之和;俘虏羁押费 = 城俘
        /// (无部队、所在城=本城)每人 100。读缝同步后产出,与内核公式逐位对照由测试断言。
        /// </summary>
        public int SalaryGoldCostFromComponents(City city)
        {
            ArgumentNullException.ThrowIfNull(city);
            SyncAll();
            int goldCost = 0;
            foreach (KeyValuePair<int, Entity> pair in _persons)
            {
                if (!_world.IsAlive(pair.Value))
                {
                    continue;
                }

                Entity entity = pair.Value;
                SangoPersonMembership membership = _world.Get<SangoPersonMembership>(entity);
                SangoPersonStatus status = _world.Get<SangoPersonStatus>(entity);
                if (membership.BelongCityId == city.Id && IsAllPersonsMember(status.State))
                {
                    goldCost += _world.Get<SangoPersonCareer>(entity).OfficialCost;
                }
                else if (status.State == (byte)PersonStateType.Prisoner &&
                         membership.BelongTroopId == 0 &&
                         membership.CurrentCityId == city.Id)
                {
                    goldCost += 100;
                }
            }

            return goldCost;
        }

        static bool IsAllPersonsMember(byte state) =>
            state == (byte)PersonStateType.Governor ||
            state == (byte)PersonStateType.Commander ||
            state == (byte)PersonStateType.Leader ||
            state == (byte)PersonStateType.Normal;

        // ---- 读面支撑(组件/属性源;挂载态实体缺席即接缝漂移,类型化抛错) ----

        internal Entity PersonEntityOrThrow(Person person)
        {
            if (_persons.TryGetValue(person.Id, out Entity entity) && _world.IsAlive(entity))
            {
                return entity;
            }

            throw new InvalidOperationException(
                $"SangoPersonNativeRuntime has no entity for person {person.Id}; the native person read face requires a materialized entity.");
        }

        internal ref readonly AttributeBuffer PersonAttributeBufferOrThrow(Person person) =>
            ref _world.Get<AttributeBuffer>(PersonEntityOrThrow(person));

        internal T PersonComponentOrThrow<T>(Person person)
            where T : struct
        {
            return _world.Get<T>(PersonEntityOrThrow(person));
        }

        // ---- 写面落账(经 SangoPersonWriteFace;内核 write-through 已在写面完成) ----

        /// <summary>训练写面落账:merit/Exp/LevelId 组件 + 账本(功勋增量/经验观测差)。</summary>
        public void OnNativeTrainGain(Person person, int meritGain, int expDelta)
        {
            if (!IsCurrentKernel || person == null || !_persons.TryGetValue(person.Id, out Entity entity) || !_world.IsAlive(entity))
            {
                return;
            }

            SangoPersonCareer career = _world.Get<SangoPersonCareer>(entity);
            career.Merit = person.merit;
            career.Exp = person.Exp;
            career.LevelId = person.Level?.Id ?? 0;
            _world.Set(entity, career);
            SangoPersonLedger ledger = _world.Get<SangoPersonLedger>(entity);
            ledger.LastMeritGain = meritGain;
            ledger.LastExpDelta = expDelta;
            ledger.TrainCount++;
            _world.Set(entity, ledger);
        }

        /// <summary>褒奖写面落账:忠诚属性 + 账本(忠诚增量)。</summary>
        public void OnNativeRewardLoyalty(Person person, int delta)
        {
            if (!IsCurrentKernel || person == null || !_persons.TryGetValue(person.Id, out Entity entity) || !_world.IsAlive(entity))
            {
                return;
            }

            SetBase(entity, SangoPersonAttributes.LoyaltyId, person.loyalty);
            SangoPersonLedger ledger = _world.Get<SangoPersonLedger>(entity);
            ledger.LastLoyaltyDelta = delta;
            ledger.RewardCount++;
            _world.Set(entity, ledger);
        }

        /// <summary>任务态写面落账(SetMission 后的组件镜像)。</summary>
        public void OnNativeMission(Person person)
        {
            if (!IsCurrentKernel || person == null || !_persons.TryGetValue(person.Id, out Entity entity) || !_world.IsAlive(entity))
            {
                return;
            }

            _world.Set(entity, MissionOf(person));
            SangoPersonLedger ledger = _world.Get<SangoPersonLedger>(entity);
            ledger.MissionCount++;
            _world.Set(entity, ledger);
        }

        /// <summary>行动位写面落账。</summary>
        public void OnNativeActionOver(Person person, bool value)
        {
            if (!IsCurrentKernel || person == null || !_persons.TryGetValue(person.Id, out Entity entity) || !_world.IsAlive(entity))
            {
                return;
            }

            SangoPersonStatus status = _world.Get<SangoPersonStatus>(entity);
            status.ActionOver = value ? (byte)1 : (byte)0;
            _world.Set(entity, status);
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

        /// <summary>回合边界全量同步(内核保留面镜像对账:掉忠/登场/逃逸/归属流动)。</summary>
        public void SettleTurnEnd()
        {
            if (_disposed || !ReferenceEquals(_scenario, Scenario.Cur))
            {
                return;
            }

            SyncAll();
        }

        /// <summary>
        /// 单人同步(生命周期事件增量):状态/归属/任务/履历/属性按 PONO 真值落账。
        /// </summary>
        public void SyncPerson(Person person)
        {
            if (_disposed || !ReferenceEquals(_scenario, Scenario.Cur) || person == null)
            {
                return;
            }

            if (_persons.TryGetValue(person.Id, out Entity entity) && _world.IsAlive(entity))
            {
                SyncPersonInto(entity, person);
            }
        }

        /// <summary>全量同步(幂等):全部武将 PONO 真值 → 组件/属性;读缝与回合边界调用。</summary>
        public void SyncAll()
        {
            if (_disposed || !ReferenceEquals(_scenario, Scenario.Cur))
            {
                return;
            }

            Array.Clear(_stateCounts);
            _scenario.personSet.ForEach(person =>
            {
                if (person == null)
                {
                    return;
                }

                if (_persons.TryGetValue(person.Id, out Entity entity) && _world.IsAlive(entity))
                {
                    SyncPersonInto(entity, person);
                }

                _stateCounts[person.state]++; // PersonStateType.Max 之外无取值;越界即内核合同破坏
            });
        }

        void SyncPersonInto(Entity entity, Person person)
        {
            SetBase(entity, SangoPersonAttributes.CommandId, person.Command);
            SetBase(entity, SangoPersonAttributes.StrengthId, person.Strength);
            SetBase(entity, SangoPersonAttributes.IntelligenceId, person.Intelligence);
            SetBase(entity, SangoPersonAttributes.PoliticsId, person.Politics);
            SetBase(entity, SangoPersonAttributes.LoyaltyId, person.loyalty);

            _world.Set(entity, new SangoPersonMembership
            {
                BelongForceId = person.mBelongForce?.Id ?? 0,
                BelongCorpsId = person.mBelongCorps?.Id ?? 0,
                BelongCityId = person.mBelongCity?.Id ?? 0,
                CurrentCityId = person.mCurrentCity?.Id ?? 0,
                BelongTroopId = person.mTroop?.Id ?? 0,
            });

            _world.Set(entity, new SangoPersonStatus
            {
                State = (byte)person.state,
                ActionOver = person.ActionOver ? (byte)1 : (byte)0,
                StayTurnCount = person.stayTurnCount,
                WildTurnCount = person.wildTurnCount,
            });

            _world.Set(entity, new SangoPersonCareer
            {
                Merit = person.merit,
                Exp = person.Exp,
                LevelId = person.Level?.Id ?? 0,
                OfficialId = person.Official?.Id ?? 0,
                OfficialCost = person.Official?.cost ?? 0,
            });

            _world.Set(entity, MissionOf(person));

            System.Numerics.Vector2 position = PersonPositionCm(_scenario, person);
            _world.Set(entity, WorldPositionCm.FromCmFloat(position.X, position.Y));
        }

        static SangoPersonMission MissionOf(Person person) => new()
        {
            MissionType = person.missionType,
            MissionTarget = person.missionTarget,
            MissionCounter = person.missionCounter,
            MissionParams1 = person.missionParams1,
            MissionParams2 = person.missionParams2,
            MissionParams3 = person.missionParams3,
            MissionParams4 = person.missionParams4,
        };

        void RebuildAll(Scenario scenario)
        {
            foreach (Entity entity in _persons.Values)
            {
                if (_world.IsAlive(entity))
                {
                    _world.Destroy(entity);
                }
            }

            _persons.Clear();
            _scenario = scenario;
            scenario.personSet.ForEach(person =>
            {
                if (person == null)
                {
                    return;
                }

                if (person.Id <= 0 || person.Id >= PersonIdLimit)
                {
                    throw new InvalidOperationException(
                        $"Kernel person id {person.Id} is outside the personSet capacity contract (1..{PersonIdLimit - 1}); " +
                        "the native person domain refuses to materialize out-of-capacity ids.");
                }

                System.Numerics.Vector2 position = PersonPositionCm(scenario, person);
                Entity entity = EntityLifecycleAtomicOps.MaterializeTemplate(
                    _services, Entity.Null, PersonTemplateId,
                    Fix64Vec2.FromFloat(position.X, position.Y));
                _world.Set(entity, new Name { Value = PersonDisplayName(person) });
                _world.Add(entity, new SangoPersonIdentity { PersonId = person.Id });
                _world.Add(entity, default(SangoPersonMembership));
                _world.Add(entity, default(SangoPersonStatus));
                _world.Add(entity, default(SangoPersonCareer));
                _world.Add(entity, default(SangoPersonMission));
                _world.Add(entity, default(SangoPersonLedger));
                _persons[person.Id] = entity;
                InitializePersonAttributes(entity, person);
                SyncPersonInto(entity, person);
            });
        }

        // 物化期初值直写 AttributeBuffer(与模板数据初始化同义,非变更):850 武将 ×5 属性
        // 的首拍批量变更会超出引擎 AttributeChanged 延迟队列容量(1024,GAS.
        // DEFERRED_TRIGGER.ERR.CapacityExceeded——D-3' 运行时启动实测),后续值变化仍走
        // AttributeMutationOps.SetBase 正式通道(同值幂等,常态逐回合变更量远低于容量)。
        void InitializePersonAttributes(Entity entity, Person person)
        {
            ref AttributeBuffer attributes = ref _world.Get<AttributeBuffer>(entity);
            attributes.SetBase(SangoPersonAttributes.CommandId, person.Command);
            attributes.SetBase(SangoPersonAttributes.StrengthId, person.Strength);
            attributes.SetBase(SangoPersonAttributes.IntelligenceId, person.Intelligence);
            attributes.SetBase(SangoPersonAttributes.PoliticsId, person.Politics);
            attributes.SetBase(SangoPersonAttributes.LoyaltyId, person.loyalty);
        }

        void SetBase(Entity entity, int attributeId, float value)
        {
            Ludots.Core.Gameplay.GAS.AttributeMutationOps.SetBase(_world, entity, attributeId, value, _tagOps);
        }

        static string PersonDisplayName(Person person) => $"[sango.person {person.Id}] {person.Name ?? string.Empty}";

        // 坐标换算与镜像/城标记同轴:内核格 x=北、y=东;随军取部队格,否则取所在/所属城。
        static System.Numerics.Vector2 PersonPositionCm(Scenario scenario, Person person)
        {
            if (person.mTroop?.cell != null)
            {
                return CellToCm(scenario.Map, person.mTroop.cell.x, person.mTroop.cell.y);
            }

            City? anchor = person.mCurrentCity ?? person.mBelongCity;
            if (anchor != null)
            {
                return CellToCm(scenario.Map, anchor.x, anchor.y);
            }

            return System.Numerics.Vector2.Zero;
        }

        static System.Numerics.Vector2 CellToCm(Map map, int cellX, int cellY)
        {
            float cellMeters = map.GridSize;
            float halfWorldCm = map.Width * cellMeters * 100f / 2f;
            return new System.Numerics.Vector2(
                (cellY * cellMeters + cellMeters * 0.5f) * 100f - halfWorldCm,
                (cellX * cellMeters + cellMeters * 0.5f) * 100f - halfWorldCm);
        }

        // ---- 内核事件订阅(全为追加式:内核武将结算体是虚方法链,无可替换订阅面) ----

        void SubscribeKernelFaces()
        {
            GameEvent.OnTurnStart -= OnTurnStart;
            GameEvent.OnTurnStart += OnTurnStart;
            GameEvent.OnTurnEnd -= OnTurnEnd;
            GameEvent.OnTurnEnd += OnTurnEnd;
            GameEvent.OnDayUpdate -= OnDayUpdate;
            GameEvent.OnDayUpdate += OnDayUpdate;
            GameEvent.OnCityFall -= OnCityFall;
            GameEvent.OnCityFall += OnCityFall;
            GameEvent.OnPersonCaptured -= OnPersonCaptured;
            GameEvent.OnPersonCaptured += OnPersonCaptured;
            GameEvent.OnPersonRelease -= OnPersonReleased;
            GameEvent.OnPersonRelease += OnPersonReleased;
            GameEvent.OnPersonExecute -= OnPersonExecuted;
            GameEvent.OnPersonExecute += OnPersonExecuted;
            GameEvent.OnPersonEscape -= OnPersonEscaped;
            GameEvent.OnPersonEscape += OnPersonEscaped;
            GameEvent.OnPersonChangeBelongCity -= OnPersonChangedBelongCity;
            GameEvent.OnPersonChangeBelongCity += OnPersonChangedBelongCity;
            GameEvent.OnPersonChangCurrentCity -= OnPersonChangedCurrentCity;
            GameEvent.OnPersonChangCurrentCity += OnPersonChangedCurrentCity;
            SangoCommandJournal.CommandEffectsApplied -= OnCommandEffectsApplied;
            SangoCommandJournal.CommandEffectsApplied += OnCommandEffectsApplied;
        }

        void UnsubscribeKernelFaces()
        {
            GameEvent.OnTurnStart -= OnTurnStart;
            GameEvent.OnTurnEnd -= OnTurnEnd;
            GameEvent.OnDayUpdate -= OnDayUpdate;
            GameEvent.OnCityFall -= OnCityFall;
            GameEvent.OnPersonCaptured -= OnPersonCaptured;
            GameEvent.OnPersonRelease -= OnPersonReleased;
            GameEvent.OnPersonExecute -= OnPersonExecuted;
            GameEvent.OnPersonEscape -= OnPersonEscaped;
            GameEvent.OnPersonChangeBelongCity -= OnPersonChangedBelongCity;
            GameEvent.OnPersonChangCurrentCity -= OnPersonChangedCurrentCity;
            SangoCommandJournal.CommandEffectsApplied -= OnCommandEffectsApplied;
        }

        // 回合边界:登场(Person.OnTurnStart 链后)/ 回合末(在野移动与任务链后)/
        // 日界(IncreaseDate 末:换季掉忠后)——内核保留面的镜像对账位。
        void OnTurnStart(Scenario scenario) => SettleTurnEnd();
        void OnTurnEnd(Scenario scenario) => SettleTurnEnd();
        void OnDayUpdate(Scenario scenario) => SettleTurnEnd();

        void OnCityFall(City city, Force lastBelongForce, Troop attacker) => SettleTurnEnd();

        void OnPersonCaptured(Person person, Troop captorTroop)
        {
            if (!IsCurrentKernel)
            {
                return;
            }

            CaptureCount++;
            SyncPerson(person);
        }

        void OnPersonReleased(Person person, Force actingForce)
        {
            if (!IsCurrentKernel)
            {
                return;
            }

            ReleaseCount++;
            SyncPerson(person);
        }

        void OnPersonExecuted(Person person, Force actingForce)
        {
            if (!IsCurrentKernel)
            {
                return;
            }

            ExecuteCount++;
            SyncPerson(person);
        }

        void OnPersonEscaped(Person person, SangoObject source)
        {
            if (!IsCurrentKernel)
            {
                return;
            }

            EscapeCount++;
            SyncPerson(person);
        }

        void OnPersonChangedBelongCity(Person person, City fromCity, City toCity) => SyncPerson(person);

        void OnPersonChangedCurrentCity(Person person, City fromCity, City toCity) => SyncPerson(person);

        void OnCommandEffectsApplied() => SettleTurnEnd();
    }

    /// <summary>
    /// 武将写面(消 D-1' 桥 #3):城域 job 结算体的武将写入段。语句序保持内核原序
    /// (merit → GainExp → 名单移除(城侧) → ActionOver);内核副作用位(OnPersonLevelUp
    /// 升级链、OnPersonActionOver 事件)经内核成员与 setter 原样触发,组件与账本由
    /// 原生运行时落账(未挂载时退化为纯内核写,语义与内核 Job* 逐位同)。
    /// </summary>
    public static class SangoPersonWriteFace
    {
        /// <summary>训练功勋/经验(City.JobTrainTroops 写入段:merit += gain; GainExp(gain))。</summary>
        public static void ApplyTrainGain(Person person, int meritGain)
        {
            ArgumentNullException.ThrowIfNull(person);
            person.merit += meritGain;
            int expBefore = person.Exp;
            person.GainExp(meritGain);
            SangoPersonNativeRuntime.Active?.OnNativeTrainGain(person, meritGain, person.Exp - expBefore);
        }

        /// <summary>褒奖忠诚(City.JobRewardPersons 写入段:loyalty += delta)。</summary>
        public static void ApplyRewardLoyalty(Person person, int delta)
        {
            ArgumentNullException.ThrowIfNull(person);
            person.loyalty += delta;
            SangoPersonNativeRuntime.Active?.OnNativeRewardLoyalty(person, delta);
        }

        /// <summary>任务态(City.JobRecruitPerson 写入段:SetMission 四参面)。</summary>
        public static void ApplyMission(Person person, MissionType missionType, SangoObject missionTarget, int missionCounter, int missionParams1)
        {
            ArgumentNullException.ThrowIfNull(person);
            person.SetMission(missionType, missionTarget, missionCounter, missionParams1);
            SangoPersonNativeRuntime.Active?.OnNativeMission(person);
        }

        /// <summary>行动结束位(经内核 setter,保 OnPersonActionOver 事件位)。</summary>
        public static void SetActionOver(Person person, bool value)
        {
            ArgumentNullException.ThrowIfNull(person);
            person.ActionOver = value;
            SangoPersonNativeRuntime.Active?.OnNativeActionOver(person, value);
        }
    }

    /// <summary>
    /// 武将读面(消 D-1' 桥 #1/#2):原生系统/命令门槛的武将值读取。挂载且为当前世界
    /// 权威时读组件/属性源;否则读内核 PONO(城原生单挂的对拍面,语义同源)。
    /// 挂载态下实体缺席即类型化抛错(接缝漂移显式失败,不静默回退)。
    /// </summary>
    public static class SangoPersonReadFace
    {
        /// <summary>忠诚(褒奖门槛/城 AI 决策输入)。</summary>
        public static int Loyalty(Person person)
        {
            ArgumentNullException.ThrowIfNull(person);
            var runtime = SangoPersonNativeRuntime.Active;
            if (runtime is { IsDisposed: false, IsCurrentKernel: true })
            {
                return (int)runtime.PersonAttributeBufferOrThrow(person).GetCurrent(SangoPersonAttributes.LoyaltyId);
            }

            return person.loyalty;
        }

        /// <summary>身分 state(在野/俘虏/隐形/死亡/官职;招揽目标门槛)。</summary>
        public static int State(Person person)
        {
            ArgumentNullException.ThrowIfNull(person);
            var runtime = SangoPersonNativeRuntime.Active;
            if (runtime is { IsDisposed: false, IsCurrentKernel: true })
            {
                return runtime.PersonComponentOrThrow<SangoPersonStatus>(person).State;
            }

            return person.state;
        }

        /// <summary>是否在部队(褒奖门槛;值源=活引用 mTroop)。</summary>
        public static bool HasTroop(Person person)
        {
            ArgumentNullException.ThrowIfNull(person);
            var runtime = SangoPersonNativeRuntime.Active;
            if (runtime is { IsDisposed: false, IsCurrentKernel: true })
            {
                return runtime.PersonComponentOrThrow<SangoPersonMembership>(person).BelongTroopId != 0;
            }

            return person.mTroop != null;
        }

        /// <summary>是否空闲(Person.IsFree 语义:无部队/无任务/非俘虏/非死亡)。</summary>
        public static bool IsFree(Person person)
        {
            ArgumentNullException.ThrowIfNull(person);
            var runtime = SangoPersonNativeRuntime.Active;
            if (runtime is { IsDisposed: false, IsCurrentKernel: true })
            {
                SangoPersonMembership membership = runtime.PersonComponentOrThrow<SangoPersonMembership>(person);
                SangoPersonStatus status = runtime.PersonComponentOrThrow<SangoPersonStatus>(person);
                SangoPersonMission mission = runtime.PersonComponentOrThrow<SangoPersonMission>(person);
                return membership.BelongTroopId == 0 &&
                       mission.MissionType == (int)MissionType.None &&
                       status.State != (byte)PersonStateType.Prisoner &&
                       status.State != (byte)PersonStateType.Dead;
            }

            return person.IsFree;
        }

        /// <summary>是否已行动(建筑工人筛选面)。</summary>
        public static bool ActionOver(Person person)
        {
            ArgumentNullException.ThrowIfNull(person);
            var runtime = SangoPersonNativeRuntime.Active;
            if (runtime is { IsDisposed: false, IsCurrentKernel: true })
            {
                return runtime.PersonComponentOrThrow<SangoPersonStatus>(person).ActionOver != 0;
            }

            return person.ActionOver;
        }

        /// <summary>五维属性计算值(收获因子/训练能力;AttributeType 0..4 = 统武智政魅)。</summary>
        public static int GetAttribute(Person person, int attributeType)
        {
            ArgumentNullException.ThrowIfNull(person);
            var runtime = SangoPersonNativeRuntime.Active;
            if (runtime is { IsDisposed: false, IsCurrentKernel: true })
            {
                ref readonly AttributeBuffer attributes = ref runtime.PersonAttributeBufferOrThrow(person);
                return attributeType switch
                {
                    0 => (int)attributes.GetCurrent(SangoPersonAttributes.CommandId),
                    1 => (int)attributes.GetCurrent(SangoPersonAttributes.StrengthId),
                    2 => (int)attributes.GetCurrent(SangoPersonAttributes.IntelligenceId),
                    3 => (int)attributes.GetCurrent(SangoPersonAttributes.PoliticsId),
                    // 魅力不属本波属性面(任务范围=统/武/智/政/忠诚);原生读者暂读内核计算值,
                    // 域边界登记于 SangoLegacyBridge 武将段。
                    4 => person.Glamour,
                    _ => 0,
                };
            }

            return person.GetAttribute(attributeType);
        }
    }

    /// <summary>
    /// 原生武将域·回合结算系统(SystemGroup.Cleanup:仿真结算后的武将域维护相位,
    /// 与城域结算同组):内核世界替换对账 + 回合边界全量落账(掉忠/登场/逃役/归属
    /// 流动的镜像对账)与运行时自举。headless 内核事件已先行落账,本面幂等。
    /// </summary>
    public sealed class SangoPersonSettlementSystem : ISystem<float>
    {
        readonly Func<GameEngine?> _engineSource;
        int _lastStampedTurn = -1;

        public SangoPersonSettlementSystem(Func<GameEngine?> engineSource)
        {
            _engineSource = engineSource ?? throw new ArgumentNullException(nameof(engineSource));
        }

        public void Initialize() { }

        public void BeforeUpdate(in float dt) { }

        public void Update(in float dt)
        {
            SangoPersonNativeRuntime? runtime = SangoPersonNativeRuntime.Active;
            if (runtime is not { IsDisposed: false })
            {
                GameEngine? engine = _engineSource();
                if (engine != null && Scenario.Cur != null)
                {
                    SangoPersonNativeRuntime.Attach(engine);
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
