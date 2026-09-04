// M3 末 ECS 溶解第一片:内核读模型 → Arch ECS 镜像实体(SangoEntityMirror)。
// D-1' 起城面升级为原生城实体(sango.city 模板 + GAS 属性 + 保序组件,
// SangoCityNativeRuntime 持有);D-2' 起武将面升级为原生武将实体(sango.person 模板 +
// GAS 属性 + 状态/归属组件,SangoPersonNativeRuntime 持有);D-3' 起部队面升级为原生
// 部队实体(sango.troop 模板 + GAS 属性 + 任务/CD/路径组件,SangoTroopNativeRuntime
// 持有)。原生运行时挂载时镜像城/武将/部队分支全部退役(消灭双实体;镜像仅在全原生
// 未挂载的裸内核跑(预言机对拍)中物化,供读模型纯净性双跑验收)。
// 合同:内核(Scenario.Cur 对象池)是唯一真相源,本文件只读内核、只写引擎世界;
// 镜像开关(注入与否)不影响 WorldDigest(读模型纯净性由 SangoEntityMirrorTests 双跑验收)。
// 物化走 Layer 0 原子 op EntityLifecycleAtomicOps.MaterializeTemplate(模板
// sango.mirror.{city|person|troop},assets/Entities/templates.json,经引擎既有模板合并链);
// 镜像实体无 PresentationStableId(MaterializeTemplate 语义即剥离),despawn 走引擎
// 非表现实体分支 world.Destroy(与 RollbackMaterializedTarget 同构),不进表现生命周期流。
// 读取面与 SangoWorldTopics 同源(gold/food/population/allPersons/loyalty/统武智政/
// state/troops/morale/missionType/mBelongForce/mFlag.color),不新开读取路径。
//
// 同步时机:
//   回合边界 + 命令生效后 —— 订阅 SangoCommandJournal.CommandEffectsApplied(命令成功
//   落账的唯 一漏斗;step 命令覆盖 SangoTurnDriver.AdvanceTurn 的一切调用方,故引擎
//   TurnAdvanced 链的刷新也由此承载,且发生在命令 ack 返回之前);
//   结构/摆位/归属 —— 订阅内核既有事件:OnTroopCreated/OnTroopEnterCell/OnTroopClear/
//   OnTroopDestroyed(部队生灭与逐格移动)、OnCityFall(城陷只改 ForceRef,白城不删)、
//   OnPersonCaptured/OnPersonRelease/OnPersonExecute/OnPersonEscape/OnPersonChangeBelongCity
//   (俘获/释放/处决/逃跑/转城的状态变更)。
//   内核无事件面的静默变化(忠诚漂移、非处决死亡等)由回合边界全量刷新覆盖——读模型的
//   一致性合同是"回合边界 + 命令 ack 前一致",事件订阅只把任务列明的生命周期做即时化。
//   部队镜像随 OnTroopEnterCell 逐格移动;随军武将的位置随刷新点对齐(不逐格跟随)。
//
// 内核世界替换(selectPlayerForce 重装/读档回灌):SangoEntityMirrorSystem 每帧对账
// Scenario.Cur 引用,替换即全量重建(镜像实体全销毁重摆)。

using System;
using EngineLog = Ludots.Core.Diagnostics.Log;
using EngineLogChannel = Ludots.Core.Diagnostics.LogChannel;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using Sango.Core;

namespace Sango.Runtime
{
    public enum SangoMirrorKind : byte
    {
        City = 1,
        Person = 2,
        Troop = 3,
    }

    /// <summary>镜像身份:内核对象 id + 类型(内核 id 城/武将/部队各自 1..N 序号,按 Kind 区分)。</summary>
    public struct SangoIdentity
    {
        public SangoMirrorKind Kind;
        public int KernelId;
    }

    /// <summary>势力引用:内核势力 id + 旗帜色(白城/无势力 = id 0 + 灰)。</summary>
    public struct SangoForceRef
    {
        public int ForceId;
        public Vector4 Color;
    }

    /// <summary>
    /// 内核数值快照(读模型)。按 Kind 取对应字段组:城金/粮/人口/耐久/武将数;武将忠诚/
    /// 统武智政/状态;部队兵力/士气/任务标签。字段源与 SangoWorldTopics 投影同批。
    /// </summary>
    public struct SangoStatsSnapshot
    {
        // city
        public int Gold;
        public int Food;
        public int Population;
        public int Durability;
        public int PersonCount;

        // person
        public int Loyalty;
        public int Command;
        public int Strength;
        public int Intelligence;
        public int Politics;
        public int State;

        // troop
        public int Troops;
        public int Morale;
        public int MissionTypeId;
        public string MissionLabel;
    }

    /// <summary>镜像实体只读探针(测试/取证面,不参与引擎运行时)。</summary>
    public readonly record struct SangoMirrorProbe(
        SangoMirrorKind Kind,
        int KernelId,
        string Name,
        Vector2 PositionCm,
        SangoForceRef Force,
        SangoStatsSnapshot Stats);

    public sealed class SangoEntityMirrorRuntime : IDisposable
    {
        internal const string CityTemplateId = "sango.mirror.city";
        internal const string PersonTemplateId = "sango.mirror.person";
        internal const string TroopTemplateId = "sango.mirror.troop";

        static readonly EngineLogChannel MirrorChannel = EngineLog.GetOrCreateModChannel("SangoSimMod");

        static readonly Vector4 UnownedColor = new(0.55f, 0.55f, 0.55f, 1f);

        readonly World _world;
        readonly EntityLifecycleRuntimeServices _services;
        readonly Dictionary<int, Entity> _cities = new();
        readonly Dictionary<int, Entity> _persons = new();
        readonly Dictionary<int, Entity> _troops = new();
        Scenario _scenario;
        bool _disposed;

        /// <summary>当前挂载的镜像运行时(内核单世界,进程内至多一个;引擎宿主由 SangoSimModEntry 维护)。</summary>
        public static SangoEntityMirrorRuntime? Active { get; private set; }

        public SangoEntityMirrorRuntime(World world, EntityLifecycleRuntimeServices services)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _scenario = Scenario.Cur
                ?? throw new InvalidOperationException("SangoEntityMirror requires a booted kernel (Scenario.Cur).");

            Subscribe();
            RebuildAll(_scenario);
            Active = this;
        }

        public bool IsDisposed => _disposed;

        public int CityCount => _cities.Count;
        public int PersonCount => _persons.Count;
        public int TroopCount => _troops.Count;

        /// <summary>
        /// 引擎宿主挂载(幂等):内核世界与引擎世界未变即复用现役镜像;已替换即销毁重建。
        /// 需要引擎核心服务齐备(模板注册表/稳定 id/TagOps),缺席类型化抛错,不代建。
        /// </summary>
        public static SangoEntityMirrorRuntime Attach(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            Scenario scenario = Scenario.Cur
                ?? throw new InvalidOperationException("SangoEntityMirror attach requires a booted kernel (Scenario.Cur).");

            if (Active is { IsDisposed: false } existing &&
                ReferenceEquals(existing._scenario, scenario) &&
                existing._world == engine.World)
            {
                return existing;
            }

            Active?.Dispose();
            return new SangoEntityMirrorRuntime(engine.World, BuildEngineServices(engine));
        }

        static EntityLifecycleRuntimeServices BuildEngineServices(GameEngine engine)
        {
            var stableIds = engine.GetService(CoreServiceKeys.PresentationStableIdAllocator)
                ?? throw new InvalidOperationException("SangoEntityMirror requires the engine PresentationStableIdAllocator service.");
            var tagOps = engine.GetService(CoreServiceKeys.TagOps)
                ?? throw new InvalidOperationException("SangoEntityMirror requires the engine TagOps service.");
            return new EntityLifecycleRuntimeServices(
                engine.World,
                engine.MapLoader.TemplateRegistry,
                engine.MapLoader.EntityTemplateKeys,
                stableIds,
                tagOps,
                engine.GetService(CoreServiceKeys.PresenterEntityRuntime),
                engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry));
        }

        /// <summary>每帧对账(Cleanup 相位):内核世界被替换即全量重建;否则不动。</summary>
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

        /// <summary>全量数值刷新(回合边界 + 命令生效后;也覆盖事件未及的静默变化)。</summary>
        public void RefreshAllStats()
        {
            if (_disposed || !ReferenceEquals(_scenario, Scenario.Cur))
            {
                return;
            }

            // D-1':城数值面归原生城实体(GAS 属性),镜像只保武将/部队刷新。
            // D-2':武将面归原生武将实体(sango.person 模板 + GAS 属性 + 组件),
            // 镜像武将分支随原生武将运行时挂载退役(消灭双实体,城域先例)。
            // D-3':部队面归原生部队实体(sango.troop 模板 + GAS 属性 + 组件,
            // SangoTroopNativeRuntime),镜像部队分支随其挂载退役。
            bool nativeCities = SangoCityNativeRuntime.Active is { IsDisposed: false };
            bool nativePersons = SangoPersonNativeRuntime.Active is { IsDisposed: false };
            bool nativeTroops = SangoTroopNativeRuntime.Active is { IsDisposed: false };
            if (!nativeCities)
            {
                foreach (KeyValuePair<int, Entity> pair in _cities)
                {
                    City? city = _scenario.citySet.Get(pair.Key);
                    if (city == null || !_world.IsAlive(pair.Value))
                    {
                        continue;
                    }

                    ApplyCitySnapshot(_world, pair.Value, city);
                }
            }

            if (!nativePersons)
            {
                foreach (KeyValuePair<int, Entity> pair in _persons)
                {
                    Person? person = _scenario.personSet.Get(pair.Key);
                    if (person == null || !_world.IsAlive(pair.Value))
                    {
                        continue;
                    }

                    ApplyPersonSnapshot(_world, pair.Value, person, PersonPositionCm(_scenario, person));
                }
            }

            if (nativeTroops)
            {
                return;
            }

            List<int>? deadTroops = null;
            foreach (KeyValuePair<int, Entity> pair in _troops)
            {
                Troop? troop = _scenario.troopsSet.Get(pair.Key);
                if (troop == null || !troop.IsAlive)
                {
                    (deadTroops ??= new List<int>()).Add(pair.Key);
                    continue;
                }

                if (!_world.IsAlive(pair.Value))
                {
                    continue;
                }

                ApplyTroopSnapshot(_world, pair.Value, troop, CellToCm(_scenario.Map, troop.x, troop.y));
            }

            if (deadTroops != null)
            {
                foreach (int troopId in deadTroops)
                {
                    RemoveMirror(_troops, troopId);
                }
            }
        }

        public List<SangoMirrorProbe> Snapshot(SangoMirrorKind kind)
        {
            var probes = new List<SangoMirrorProbe>();
            if (_disposed)
            {
                return probes;
            }

            Dictionary<int, Entity> table = kind switch
            {
                SangoMirrorKind.City => _cities,
                SangoMirrorKind.Person => _persons,
                _ => _troops,
            };
            foreach (KeyValuePair<int, Entity> pair in table)
            {
                if (!_world.IsAlive(pair.Value))
                {
                    continue;
                }

                probes.Add(new SangoMirrorProbe(
                    kind,
                    pair.Key,
                    _world.Get<Name>(pair.Value).Value,
                    PositionOf(pair.Value),
                    _world.Get<SangoForceRef>(pair.Value),
                    _world.Get<SangoStatsSnapshot>(pair.Value)));
            }

            return probes;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Unsubscribe();
            DestroyAllMirrorEntities();
            if (ReferenceEquals(Active, this))
            {
                Active = null;
            }

            _disposed = true;
        }

        // ---- 全量重建 ----

        void RebuildAll(Scenario scenario)
        {
            DestroyAllMirrorEntities();
            _cities.Clear();
            _persons.Clear();
            _troops.Clear();
            _scenario = scenario;

            // D-1':城面升级为原生城实体(sango.city 模板 + GAS 属性 + 保序组件,
            // SangoCityNativeRuntime 持有);原生运行时挂载时镜像不再重复建城实体。
            // D-2':武将面同理归 sango.person 原生实体,镜像武将分支随其挂载退役。
            // D-3':部队面同理归 sango.troop 原生实体,镜像部队分支随其挂载退役。
            bool nativeCities = SangoCityNativeRuntime.Active is { IsDisposed: false };
            if (!nativeCities)
            {
                scenario.citySet.ForEach(city =>
                {
                    if (city != null)
                    {
                        _cities[city.Id] = SpawnMirror(SangoMirrorKind.City, city.Id, CityDisplayName(city), CellToCm(scenario.Map, city.x, city.y));
                    }
                });
            }

            bool nativePersons = SangoPersonNativeRuntime.Active is { IsDisposed: false };
            if (!nativePersons)
            {
                scenario.personSet.ForEach(person =>
                {
                    if (person != null)
                    {
                        _persons[person.Id] = SpawnMirror(SangoMirrorKind.Person, person.Id, PersonDisplayName(person), PersonPositionCm(scenario, person));
                    }
                });
            }

            // D-3':部队面归原生部队实体时镜像不再重复建部队实体。
            if (SangoTroopNativeRuntime.Active is not { IsDisposed: false })
            {
                scenario.troopsSet.ForEach(troop =>
                {
                    if (troop != null && troop.IsAlive)
                    {
                        _troops[troop.Id] = SpawnMirror(SangoMirrorKind.Troop, troop.Id, TroopDisplayName(troop), CellToCm(scenario.Map, troop.x, troop.y));
                    }
                });
            }

            RefreshAllStats();
            EngineLog.Info(MirrorChannel, $"[SangoEntityMirror] rebuilt for scenario turn {scenario.Info.turnCount}: {_cities.Count} cities, {_persons.Count} persons, {_troops.Count} troops");
        }

        void DestroyAllMirrorEntities()
        {
            DestroyTable(_world, _cities);
            DestroyTable(_world, _persons);
            DestroyTable(_world, _troops);

            static void DestroyTable(World world, Dictionary<int, Entity> table)
            {
                foreach (Entity entity in table.Values)
                {
                    if (world.IsAlive(entity))
                    {
                        world.Destroy(entity);
                    }
                }
            }
        }

        // ---- 内核事件面(结构/摆位/归属即时化) ----

        void Subscribe()
        {
            GameEvent.OnTroopCreated -= OnTroopCreated;
            GameEvent.OnTroopCreated += OnTroopCreated;
            GameEvent.OnTroopEnterCell -= OnTroopEnterCell;
            GameEvent.OnTroopEnterCell += OnTroopEnterCell;
            GameEvent.OnTroopClear -= OnTroopGone;
            GameEvent.OnTroopClear += OnTroopGone;
            GameEvent.OnTroopDestroyed -= OnTroopDestroyed;
            GameEvent.OnTroopDestroyed += OnTroopDestroyed;
            GameEvent.OnCityFall -= OnCityFall;
            GameEvent.OnCityFall += OnCityFall;
            GameEvent.OnPersonCaptured -= OnPersonCaptured;
            GameEvent.OnPersonCaptured += OnPersonCaptured;
            GameEvent.OnPersonRelease -= OnPersonReleasedOrExecuted;
            GameEvent.OnPersonRelease += OnPersonReleasedOrExecuted;
            GameEvent.OnPersonExecute -= OnPersonReleasedOrExecuted;
            GameEvent.OnPersonExecute += OnPersonReleasedOrExecuted;
            GameEvent.OnPersonEscape -= OnPersonEscaped;
            GameEvent.OnPersonEscape += OnPersonEscaped;
            GameEvent.OnPersonChangeBelongCity -= OnPersonChangedCity;
            GameEvent.OnPersonChangeBelongCity += OnPersonChangedCity;
            SangoCommandJournal.CommandEffectsApplied -= OnCommandEffectsApplied;
            SangoCommandJournal.CommandEffectsApplied += OnCommandEffectsApplied;
        }

        void Unsubscribe()
        {
            GameEvent.OnTroopCreated -= OnTroopCreated;
            GameEvent.OnTroopEnterCell -= OnTroopEnterCell;
            GameEvent.OnTroopClear -= OnTroopGone;
            GameEvent.OnTroopDestroyed -= OnTroopDestroyed;
            GameEvent.OnCityFall -= OnCityFall;
            GameEvent.OnPersonCaptured -= OnPersonCaptured;
            GameEvent.OnPersonRelease -= OnPersonReleasedOrExecuted;
            GameEvent.OnPersonExecute -= OnPersonReleasedOrExecuted;
            GameEvent.OnPersonEscape -= OnPersonEscaped;
            GameEvent.OnPersonChangeBelongCity -= OnPersonChangedCity;
            SangoCommandJournal.CommandEffectsApplied -= OnCommandEffectsApplied;
        }

        bool HandlesCurrentWorld(Scenario scenario) => !_disposed && ReferenceEquals(_scenario, scenario);

        void OnTroopCreated(Troop troop, Scenario scenario)
        {
            // D-3':部队面归原生部队实体时镜像无部队分支(无实体可建)。
            if (SangoTroopNativeRuntime.Active is { IsDisposed: false } || !HandlesCurrentWorld(scenario) || troop == null || !troop.IsAlive)
            {
                return;
            }

            if (_troops.ContainsKey(troop.Id))
            {
                return;
            }

            _troops[troop.Id] = SpawnMirror(SangoMirrorKind.Troop, troop.Id, TroopDisplayName(troop), CellToCm(scenario.Map, troop.x, troop.y));
            ApplyTroopSnapshot(_world, _troops[troop.Id], troop, CellToCm(scenario.Map, troop.x, troop.y));
        }

        void OnTroopEnterCell(Troop troop, Cell destCell, Cell lastCell)
        {
            if (SangoTroopNativeRuntime.Active is { IsDisposed: false })
            {
                return;
            }

            if (!HandlesCurrentKernel() || troop == null || !_troops.TryGetValue(troop.Id, out Entity entity) || !_world.IsAlive(entity))
            {
                return;
            }

            Vector2 position = CellToCm(_scenario.Map, destCell.x, destCell.y);
            _world.Set(entity, WorldPositionCm.FromCmFloat(position.X, position.Y));
        }

        void OnTroopGone(Troop troop, Scenario scenario)
        {
            if (!HandlesCurrentWorld(scenario) || troop == null)
            {
                return;
            }

            RemoveMirror(_troops, troop.Id);
            RefreshAllStats();
        }

        void OnTroopDestroyed(Troop troop, SangoObject attacker, int damage, Scenario scenario)
        {
            if (!HandlesCurrentWorld(scenario) || troop == null)
            {
                return;
            }

            RemoveMirror(_troops, troop.Id);
            RefreshAllStats();
        }

        void OnCityFall(City city, Force lastBelongForce, Troop attacker)
        {
            if (!HandlesCurrentKernel() || city == null)
            {
                return;
            }

            // 城陷语义:归属变更改 ForceRef(白城不删);俘虏解救等连带状态由全量刷新收敛。
            // D-1':城面归原生城实体时本段空转(原生运行时自订 OnCityFall)。
            if (_cities.TryGetValue(city.Id, out Entity entity) && _world.IsAlive(entity))
            {
                _world.Set(entity, ForceRefOf(city.mBelongForce));
            }

            RefreshAllStats();
        }

        void OnPersonCaptured(Person person, Troop captorTroop) => RefreshPersonIfCurrent(person);

        void OnPersonReleasedOrExecuted(Person person, Force actingForce) => RefreshPersonIfCurrent(person);

        void OnPersonEscaped(Person person, SangoObject source) => RefreshPersonIfCurrent(person);

        void OnPersonChangedCity(Person person, City fromCity, City toCity) => RefreshPersonIfCurrent(person);

        void OnCommandEffectsApplied()
        {
            RefreshAllStats();
        }

        bool HandlesCurrentKernel() => !_disposed && ReferenceEquals(_scenario, Scenario.Cur);

        void RefreshPersonIfCurrent(Person person)
        {
            if (!HandlesCurrentKernel() || person == null)
            {
                return;
            }

            RefreshPerson(person);
        }

        void RefreshPerson(Person person)
        {
            if (!ReferenceEquals(_scenario, Scenario.Cur))
            {
                return;
            }

            // D-2':原生武将运行时挂载时镜像武将分支已退役(无实体可刷)。
            if (SangoPersonNativeRuntime.Active is { IsDisposed: false })
            {
                return;
            }

            if (!_persons.TryGetValue(person.Id, out Entity entity) || !_world.IsAlive(entity))
            {
                return;
            }

            _world.Set(entity, new Name { Value = PersonDisplayName(person) });
            ApplyPersonSnapshot(_world, entity, person, PersonPositionCm(_scenario, person));
        }

        // ---- 物化与快照 ----

        Entity SpawnMirror(SangoMirrorKind kind, int kernelId, string name, Vector2 positionCm)
        {
            string templateId = kind switch
            {
                SangoMirrorKind.City => CityTemplateId,
                SangoMirrorKind.Person => PersonTemplateId,
                _ => TroopTemplateId,
            };
            Entity entity = EntityLifecycleAtomicOps.MaterializeTemplate(
                _services, Entity.Null, templateId, Fix64Vec2.FromFloat(positionCm.X, positionCm.Y));
            _world.Set(entity, new Name { Value = name });
            _world.Add(entity, new SangoIdentity { Kind = kind, KernelId = kernelId });
            _world.Add(entity, default(SangoForceRef));
            _world.Add(entity, default(SangoStatsSnapshot));
            return entity;
        }

        static void ApplyCitySnapshot(World world, Entity entity, City city)
        {
            world.Set(entity, ForceRefOf(city.mBelongForce));
            world.Set(entity, new SangoStatsSnapshot
            {
                Gold = city.gold,
                Food = city.food,
                Population = city.population,
                Durability = city.durability,
                PersonCount = city.allPersons.Count,
            });
        }

        static void ApplyPersonSnapshot(World world, Entity entity, Person person, Vector2 positionCm)
        {
            world.Set(entity, ForceRefOf(person.mBelongForce));
            world.Set(entity, WorldPositionCm.FromCmFloat(positionCm.X, positionCm.Y));
            world.Set(entity, new SangoStatsSnapshot
            {
                Loyalty = person.loyalty,
                Command = person.Command,
                Strength = person.Strength,
                Intelligence = person.Intelligence,
                Politics = person.Politics,
                State = person.state,
            });
        }

        static void ApplyTroopSnapshot(World world, Entity entity, Troop troop, Vector2 positionCm)
        {
            world.Set(entity, ForceRefOf(troop.mBelongForce));
            world.Set(entity, WorldPositionCm.FromCmFloat(positionCm.X, positionCm.Y));
            int missionTypeId = troop.missionType;
            world.Set(entity, new SangoStatsSnapshot
            {
                Troops = troop.troops,
                Morale = troop.morale,
                MissionTypeId = missionTypeId,
                MissionLabel = missionTypeId == 0 ? "None" : ((MissionType)missionTypeId).ToString(),
            });
        }

        static SangoForceRef ForceRefOf(Force? force)
        {
            if (force?.mFlag == null)
            {
                return new SangoForceRef { ForceId = force?.Id ?? 0, Color = UnownedColor };
            }

            var flagColor = force.mFlag.color;
            return new SangoForceRef
            {
                ForceId = force.Id,
                Color = new Vector4(flagColor.r, flagColor.g, flagColor.b, 1f),
            };
        }

        void RemoveMirror(Dictionary<int, Entity> table, int kernelId)
        {
            if (table.Remove(kernelId, out Entity entity) && _world.IsAlive(entity))
            {
                _world.Destroy(entity);
            }
        }

        Vector2 PositionOf(Entity entity)
        {
            Fix64Vec2 value = _world.Get<WorldPositionCm>(entity).Value;
            return new Vector2(value.X.ToFloat(), value.Y.ToFloat());
        }

        static string CityDisplayName(City city) => $"[sango.city {city.Id}] {city.Name ?? string.Empty}";

        static string PersonDisplayName(Person person) => $"[sango.person {person.Id}] {person.Name ?? string.Empty}";

        static string TroopDisplayName(Troop troop) => $"[sango.troop {troop.Id}] {troop.Name ?? string.Empty}";

        // 坐标换算与 SangoCityMarkers/SangoTroopMarkers 同轴:内核格 x=北、y=东;镜像 2D
        // 逻辑位取 (东, 北) 即 (X, Y),单位 cm,原点为地图中心。
        static Vector2 CellToCm(Map map, int cellX, int cellY)
        {
            float cellMeters = map.GridSize;
            float halfWorldCm = map.Width * cellMeters * 100f / 2f;
            return new Vector2(
                (cellY * cellMeters + cellMeters * 0.5f) * 100f - halfWorldCm,
                (cellX * cellMeters + cellMeters * 0.5f) * 100f - halfWorldCm);
        }

        static Vector2 PersonPositionCm(Scenario scenario, Person person)
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

            return Vector2.Zero;
        }
    }

    /// <summary>
    /// 镜像每帧守卫(Cleanup 相位):内核世界替换对账重建;引擎事件尚未送达引擎实例的
    /// 宿主路径下,内核已启动即自举挂载。系统注册走 ISystemRegistrar 正式面
    /// (SangoSimModEntry.OnLoad,SystemGroup.Cleanup——仿真结算后的读模型维护相位)。
    /// </summary>
    public sealed class SangoEntityMirrorSystem : ISystem<float>
    {
        readonly Func<GameEngine?> _engineSource;

        public SangoEntityMirrorSystem(Func<GameEngine?> engineSource)
        {
            _engineSource = engineSource ?? throw new ArgumentNullException(nameof(engineSource));
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (SangoEntityMirrorRuntime.Active is { IsDisposed: false } mirror)
            {
                mirror.Reconcile();
                return;
            }

            GameEngine? engine = _engineSource();
            if (engine != null && Sango.Core.Scenario.Cur != null)
            {
                // D-1':城面原生运行时先行(城模板升级为 sango.city,镜像城分支随其挂载退役);
                // D-2':武将面原生运行时同理先行(sango.person,镜像武将分支随其挂载退役);
                // D-3':部队/军团面原生运行时同理先行(sango.troop/sango.corps,镜像部队分支随其挂载退役)。
                SangoCityNativeRuntime.Attach(engine);
                SangoPersonNativeRuntime.Attach(engine);
                SangoTroopNativeRuntime.Attach(engine);
                SangoEntityMirrorRuntime.Attach(engine);
            }
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }
    }
}
