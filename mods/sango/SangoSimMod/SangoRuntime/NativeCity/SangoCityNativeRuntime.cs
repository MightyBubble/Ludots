// D-1' 城域原生实现运行时:物化 sango.city 实体群、内核事件面交换(经济/AI 决策)、
// 回合/命令/城陷结算编排、digest 城域行组件源。
//
// 对拍合同(本波验收核心):
//   老内核跑一遍(本运行时不挂)与原生城域跑一遍(挂载)同种子同命令流,城域 digest
//   行逐位相等。等价性由两段证明构成——
//   1) 跨运行 write-through:原生收入/收获在内核同位取随机(共享 GameRandom 流)并把
//      结果写回内核 PONO(桥 #7);原生公式错一位 → run B 的内核世界偏移 → digest 失配。
//   2) 账本计值:SangoCityEconomy.LastIncomeGold/LastSalaryGold/LastHarvestFood/
//      LastFoodCost 为原生结算面的逐城计量,测试按内核公式独立复算断言。
//   内核保留面(combat 耐久/OnFall 内部/人口增长 draw/名单流动)本波镜像对账
//   (SettleTurnEnd 全量同步 PONO→组件),消亡计划见 SangoLegacyBridge 登记表。
//
// digest 双源:SangoTurnDriver.WorldDigest 的城域行——本运行时挂载时读组件源
// (对拍后正式源);未挂载读内核源(SangoCityNativeRuntime.KernelCityRows)。

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using Sango;
using Sango.Core;

namespace Sango.Runtime
{
    /// <summary>原生城域运行时(进程单世界:SangoCityNativeRuntime.Active)。</summary>
    public sealed class SangoCityNativeRuntime : IDisposable
    {
        internal const string CityTemplateId = "sango.city";

        readonly World _world;
        readonly EntityLifecycleRuntimeServices _services;
        readonly TagOps _tagOps;
        readonly Dictionary<int, Entity> _cities = new();
        readonly List<string> _digestRows = new();
        Scenario _scenario;
        bool _disposed;

        // 事件面交换态(Attach 建立,Dispose 换回;见 SangoCityEventSwap)。
        Delegate? _kernelIncome;
        Delegate? _nativeIncome;
        Delegate? _kernelSeason;
        Delegate? _nativeSeason;
        Delegate? _kernelClassicsHarvest;
        Delegate? _nativeHarvestCalc;
        Delegate? _kernelBuildingHarvest;
        Delegate? _kernelAIPrepare;
        Delegate? _nativeAIPrepare;
        int[][]? _classicsBuildingTemplate;

        public static SangoCityNativeRuntime? Active { get; private set; }

        SangoCityNativeRuntime(World world, EntityLifecycleRuntimeServices services, TagOps tagOps)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _tagOps = tagOps ?? throw new ArgumentNullException(nameof(tagOps));
            _scenario = Scenario.Cur
                ?? throw new InvalidOperationException("SangoCityNativeRuntime requires a booted kernel (Scenario.Cur).");
            SangoCityAttributes.EnsureRegistered();

            SubscribeKernelFaces();
            SwapKernelFaces();
            RebuildAll(_scenario);
            Active = this;
        }

        public bool IsDisposed => _disposed;

        public int CityCount => _cities.Count;

        /// <summary>
        /// 权威性判定:本运行时是否仍是当前内核世界的城域权威(世界被替换后、重建前,
        /// 城域权威回到内核 PONO——digest 不得读一个属于旧世界的组件集)。
        /// </summary>
        public bool IsCurrentKernel => !_disposed && ReferenceEquals(_scenario, Scenario.Cur);

        /// <summary>引擎宿主挂载(幂等):内核世界与引擎世界未变即复用;已替换即重建。</summary>
        public static SangoCityNativeRuntime Attach(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            Scenario scenario = Scenario.Cur
                ?? throw new InvalidOperationException("SangoCityNativeRuntime attach requires a booted kernel (Scenario.Cur).");

            if (Active is { IsDisposed: false } existing &&
                ReferenceEquals(existing._scenario, scenario) &&
                existing._world == engine.World)
            {
                return existing;
            }

            Active?.Dispose();
            var stableIds = engine.GetService(CoreServiceKeys.PresentationStableIdAllocator)
                ?? throw new InvalidOperationException("SangoCityNativeRuntime requires the engine PresentationStableIdAllocator service.");
            var tagOps = engine.GetService(CoreServiceKeys.TagOps) as TagOps
                ?? throw new InvalidOperationException("SangoCityNativeRuntime requires the engine TagOps service.");
            return new SangoCityNativeRuntime(
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

        /// <summary>测试/裸世界挂载(与 SangoEntityMirrorTests 同款服务直构)。</summary>
        public static SangoCityNativeRuntime AttachBare(World world, EntityLifecycleRuntimeServices services, TagOps tagOps)
        {
            if (Active is { IsDisposed: false })
            {
                Active.Dispose();
            }

            return new SangoCityNativeRuntime(world, services, tagOps);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            RestoreKernelFaces();
            UnsubscribeKernelFaces();
            foreach (Entity entity in _cities.Values)
            {
                if (_world.IsAlive(entity))
                {
                    _world.Destroy(entity);
                }
            }

            _cities.Clear();
            if (ReferenceEquals(Active, this))
            {
                Active = null;
            }

            _disposed = true;
        }

        // ---- 对外读取面(digest / 测试探针) ----

        /// <summary>城域 digest 行(组件源,正式源):id:金:粮:人口:耐久:归属:名单指纹。</summary>
        public IReadOnlyList<string> CityDigestRows()
        {
            if (_disposed)
            {
                throw new InvalidOperationException("SangoCityNativeRuntime is disposed; city digest rows require an attached runtime.");
            }

            _digestRows.Clear();
            List<KeyValuePair<int, Entity>> pairs = new(_cities);
            pairs.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (KeyValuePair<int, Entity> pair in pairs)
            {
                if (!_world.IsAlive(pair.Value))
                {
                    continue;
                }

                Entity entity = pair.Value;
                ref readonly AttributeBuffer attributes = ref _world.Get<AttributeBuffer>(entity);
                SangoForceRef force = _world.Get<SangoForceRef>(entity);
                _digestRows.Add(
                    $"city {pair.Key}:" +
                    $"{(int)attributes.GetCurrent(SangoCityAttributes.GoldId)}:" +
                    $"{(int)attributes.GetCurrent(SangoCityAttributes.FoodId)}:" +
                    $"{(int)attributes.GetCurrent(SangoCityAttributes.PopulationId)}:" +
                    $"{(int)attributes.GetCurrent(SangoCityAttributes.DurabilityId)}:" +
                    $"{force.ForceId}:" +
                    $"{SangoCityRoster.RosterFingerprint(_world.Get<SangoCityRoster>(entity))}");
            }

            return _digestRows;
        }

        /// <summary>内核源城域 digest 行(未挂载运行时的进程用;与组件源同格式,对拍双源)。</summary>
        public static List<string> KernelCityRows(Scenario scenario)
        {
            ArgumentNullException.ThrowIfNull(scenario);
            var rows = new List<string>();
            scenario.citySet.ForEach(city =>
            {
                if (city == null)
                {
                    return;
                }

                rows.Add(
                    $"city {city.Id}:{city.gold}:{city.food}:{city.population}:{city.durability}:" +
                    $"{city.mBelongForce?.Id ?? 0}:{KernelRosterFingerprint(city)}");
            });
            return rows;
        }

        /// <summary>城探针(测试/取证面):组件源字段 + 摆位;PersonCount/Morale 为内核保留面读数(桥 #6/#8)。</summary>
        public readonly record struct CityProbe(
            int CityId,
            string Name,
            int Gold,
            int Food,
            int Population,
            int Durability,
            int ForceId,
            int Morale,
            int PersonCount,
            System.Numerics.Vector2 PositionCm,
            in SangoCityRoster Roster,
            in SangoCityEconomy Economy);

        public List<CityProbe> Snapshot()
        {
            var probes = new List<CityProbe>();
            if (_disposed)
            {
                return probes;
            }

            foreach (KeyValuePair<int, Entity> pair in _cities)
            {
                if (!_world.IsAlive(pair.Value))
                {
                    continue;
                }

                Entity entity = pair.Value;
                ref readonly AttributeBuffer attributes = ref _world.Get<AttributeBuffer>(entity);
                Fix64Vec2 position = _world.Get<WorldPositionCm>(entity).Value;
                City? city = _scenario.citySet.Get(pair.Key);
                probes.Add(new CityProbe(
                    pair.Key,
                    _world.Get<Name>(entity).Value,
                    (int)attributes.GetCurrent(SangoCityAttributes.GoldId),
                    (int)attributes.GetCurrent(SangoCityAttributes.FoodId),
                    (int)attributes.GetCurrent(SangoCityAttributes.PopulationId),
                    (int)attributes.GetCurrent(SangoCityAttributes.DurabilityId),
                    _world.Get<SangoForceRef>(entity).ForceId,
                    city?.morale ?? 0,
                    city?.allPersons.Count ?? 0,
                    new System.Numerics.Vector2(position.X.ToFloat(), position.Y.ToFloat()),
                    _world.Get<SangoCityRoster>(entity),
                    _world.Get<SangoCityEconomy>(entity)));
            }

            return probes;
        }

        // ---- 命令面:原生执行路径(SangoCityOps 在挂载态路由至此) ----

        /// <summary>原生 job 结算:写 PONO(桥)后同步组件。门槛在 SangoCityOps(双路共用)。
        /// D-2':结算前先刷武将组件源(读缝合同——训练能力/忠诚/状态读组件,须先落账真值)。</summary>
        public void ExecuteJob(City city, string type, Person[] persons, Person? target)
        {
            ArgumentNullException.ThrowIfNull(city);
            if (!_cities.TryGetValue(city.Id, out Entity entity) || !_world.IsAlive(entity))
            {
                throw new InvalidOperationException($"SangoCityNativeRuntime has no entity for city {city.Id}; cannot execute a native job.");
            }

            SangoPersonNativeRuntime.Active?.SettleTurnEnd();

            switch (type)
            {
                case "train":
                    SangoCityJobOps.TrainTroops(city, persons);
                    break;
                case "search":
                    SangoCityJobOps.Searching(city, persons);
                    break;
                case "reward":
                    SangoCityJobOps.RewardPersons(city, persons);
                    break;
                case "recruit":
                    SangoCityJobOps.RecruitPerson(city, persons[0], target!);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown native city job type '{type}'.");
            }

            SyncCity(entity, city);
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

        /// <summary>回合边界全量同步(内核保留面镜像对账 + 属性权威值落账)。</summary>
        public void SettleTurnEnd()
        {
            if (_disposed || !ReferenceEquals(_scenario, Scenario.Cur))
            {
                return;
            }

            _scenario.citySet.ForEach(city =>
            {
                if (city != null && _cities.TryGetValue(city.Id, out Entity entity) && _world.IsAlive(entity))
                {
                    SyncCity(entity, city);
                }
            });
        }

        void RebuildAll(Scenario scenario)
        {
            foreach (Entity entity in _cities.Values)
            {
                if (_world.IsAlive(entity))
                {
                    _world.Destroy(entity);
                }
            }

            _cities.Clear();
            _scenario = scenario;
            scenario.citySet.ForEach(city =>
            {
                if (city == null)
                {
                    return;
                }

                Entity entity = EntityLifecycleAtomicOps.MaterializeTemplate(
                    _services, Entity.Null, CityTemplateId,
                    Fix64Vec2.FromFloat(CityPositionCm(scenario, city).X, CityPositionCm(scenario, city).Y));
                _world.Set(entity, new Name { Value = CityDisplayName(city) });
                _world.Add(entity, new SangoCityIdentity { CityId = city.Id });
                _world.Add(entity, default(SangoForceRef));
                _world.Add(entity, default(SangoCityRoster));
                _world.Add(entity, default(SangoCityJobState));
                _world.Add(entity, default(SangoCityEconomy));
                _world.Add(entity, default(SangoCityAIPlan));
                _cities[city.Id] = entity;
                SyncCity(entity, city);
            });
        }

        /// <summary>单城全量同步:PONO→组件(值/名单/job 位)与属性写入。</summary>
        void SyncCity(Entity entity, City city)
        {
            SetBase(entity, SangoCityAttributes.GoldId, city.gold);
            SetBase(entity, SangoCityAttributes.FoodId, city.food);
            SetBase(entity, SangoCityAttributes.PopulationId, city.population);
            SetBase(entity, SangoCityAttributes.DurabilityId, city.durability);
            _world.Set(entity, ForceRefOf(city));

            SangoCityRoster roster = default;
            FillRoster(ref roster.WildPersonIds, ref roster.WildCount, city.wildPersons);
            FillRoster(ref roster.CaptivePersonIds, ref roster.CaptiveCount, city.captiveList);
            FillRoster(ref roster.InvisiblePersonIds, ref roster.InvisibleCount, city.invisiblePersons);
            FillRoster(ref roster.BuildingIds, ref roster.BuildingCount, city.allBuildings);
            _world.Set(entity, roster);

            var jobState = new SangoCityJobState
            {
                TrainCounter = city.GetJobCounter((int)CityJobType.TrainTroops),
                SearchingCounter = city.GetJobCounter((int)CityJobType.Searching),
                RecruitCounter = city.GetJobCounter((int)CityJobType.RecruitPerson),
                RewardCounter = SangoLegacyBridge.CorpsJobCounter(city, (int)CityJobType.Reward),
                AIPrepared = city.AIPrepared ? (byte)1 : (byte)0,
                AIFinished = city.AIFinished ? (byte)1 : (byte)0,
                ActionOver = city.ActionOver ? (byte)1 : (byte)0,
            };
            _world.Set(entity, jobState);

            SangoCityEconomy economy = _world.Get<SangoCityEconomy>(entity);
            economy.TotalGainFood = city.totalGainFood;
            economy.TotalGainGold = city.totalGainGold;
            economy.PopulationIncreaseFactor = city.population_increase_factor;
            _world.Set(entity, economy);
        }

        static void FillRoster(ref SangoCityIdList target, ref int count, List<Person> persons)
        {
            count = 0;
            if (persons == null)
            {
                return;
            }

            foreach (Person person in persons)
            {
                if (person == null)
                {
                    continue;
                }

                if (count >= SangoCityIdList.Capacity)
                {
                    throw new InvalidOperationException(
                        $"SangoCityRoster capacity ({SangoCityIdList.Capacity}) exceeded; refusing to truncate an ordered roster.");
                }

                target[count++] = person.Id;
            }
        }

        static void FillRoster(ref SangoCityIdList target, ref int count, SangoObjectList<Person> persons)
        {
            count = 0;
            if (persons == null)
            {
                return;
            }

            for (int i = 0; i < persons.Count; i++)
            {
                Person? person = persons.Get(i);
                if (person == null)
                {
                    continue;
                }

                if (count >= SangoCityIdList.Capacity)
                {
                    throw new InvalidOperationException(
                        $"SangoCityRoster capacity ({SangoCityIdList.Capacity}) exceeded; refusing to truncate an ordered roster.");
                }

                target[count++] = person.Id;
            }
        }

        static void FillRoster(ref SangoCityIdList target, ref int count, SangoObjectList<Sango.Core.Building> buildings)
        {
            count = 0;
            if (buildings == null)
            {
                return;
            }

            for (int i = 0; i < buildings.Count; i++)
            {
                Sango.Core.Building? building = buildings.Get(i);
                if (building == null)
                {
                    continue;
                }

                if (count >= SangoCityIdList.Capacity)
                {
                    throw new InvalidOperationException(
                        $"SangoCityRoster capacity ({SangoCityIdList.Capacity}) exceeded; refusing to truncate an ordered roster.");
                }

                target[count++] = building.Id;
            }
        }

        static int KernelRosterFingerprint(City city)
        {
            SangoCityRoster roster = default;
            FillRoster(ref roster.WildPersonIds, ref roster.WildCount, city.wildPersons);
            FillRoster(ref roster.CaptivePersonIds, ref roster.CaptiveCount, city.captiveList);
            FillRoster(ref roster.InvisiblePersonIds, ref roster.InvisibleCount, city.invisiblePersons);
            FillRoster(ref roster.BuildingIds, ref roster.BuildingCount, city.allBuildings);
            return SangoCityRoster.RosterFingerprint(roster);
        }

        void SetBase(Entity entity, int attributeId, float value)
        {
            AttributeMutationOps.SetBase(_world, entity, attributeId, value, _tagOps);
        }

        static SangoForceRef ForceRefOf(City city)
        {
            Force? force = city.mBelongForce;
            if (force?.mFlag == null)
            {
                return new SangoForceRef { ForceId = force?.Id ?? 0, Color = new Vector4(0.55f, 0.55f, 0.55f, 1f) };
            }

            var color = force.mFlag.color;
            return new SangoForceRef { ForceId = force.Id, Color = new Vector4(color.r, color.g, color.b, 1f) };
        }

        static string CityDisplayName(City city) => $"[sango.city {city.Id}] {city.Name ?? string.Empty}";

        static System.Numerics.Vector2 CityPositionCm(Scenario scenario, City city)
        {
            float cellMeters = scenario.Map.GridSize;
            float halfWorldCm = scenario.Map.Width * cellMeters * 100f / 2f;
            return new System.Numerics.Vector2(
                (city.y * cellMeters + cellMeters * 0.5f) * 100f - halfWorldCm,
                (city.x * cellMeters + cellMeters * 0.5f) * 100f - halfWorldCm);
        }

        // ---- 内核事件订阅(城陷/军粮账本/回合边界/命令落账) ----

        void SubscribeKernelFaces()
        {
            GameEvent.OnCityFall -= OnCityFall;
            GameEvent.OnCityFall += OnCityFall;
            GameEvent.OnCityTurnStart -= OnCityTurnStart;
            GameEvent.OnCityTurnStart += OnCityTurnStart;
            GameEvent.OnTurnEnd -= OnTurnEnd;
            GameEvent.OnTurnEnd += OnTurnEnd;
            SangoCommandJournal.CommandEffectsApplied -= OnCommandEffectsApplied;
            SangoCommandJournal.CommandEffectsApplied += OnCommandEffectsApplied;
        }

        void UnsubscribeKernelFaces()
        {
            GameEvent.OnCityFall -= OnCityFall;
            GameEvent.OnCityTurnStart -= OnCityTurnStart;
            GameEvent.OnTurnEnd -= OnTurnEnd;
            SangoCommandJournal.CommandEffectsApplied -= OnCommandEffectsApplied;
        }

        void OnCityFall(City city, Force lastBelongForce, Troop attacker)
        {
            if (_disposed || !ReferenceEquals(_scenario, Scenario.Cur) || city == null)
            {
                return;
            }

            // 城陷组件转移:归属翻转/名单再分布/值衰减由内核保留面完成(桥登记),
            // 原生面在此把转移结果落账进组件并计数(原生"城陷面"的可对账产物)。
            if (_cities.TryGetValue(city.Id, out Entity entity) && _world.IsAlive(entity))
            {
                SangoCityEconomy economy = _world.Get<SangoCityEconomy>(entity);
                economy.FallCount++;
                _world.Set(entity, economy);
                SyncCity(entity, city);
            }
        }

        void OnCityTurnStart(City city, Scenario scenario)
        {
            if (_disposed || !ReferenceEquals(_scenario, Scenario.Cur) || city == null)
            {
                return;
            }

            // 军粮账本(City.FoodCost 公式 port;饥荒分支是内核保留面——food 归零即打哨兵)。
            if (_cities.TryGetValue(city.Id, out Entity entity) && _world.IsAlive(entity))
            {
                SangoCityEconomy economy = _world.Get<SangoCityEconomy>(entity);
                economy.LastFoodCost = city.food == 0 ? -1 : SangoCityFormulas.MilitaryFoodCost(city);
                _world.Set(entity, economy);
            }
        }

        void OnTurnEnd(Scenario scenario)
        {
            SettleTurnEnd();
        }

        void OnCommandEffectsApplied()
        {
            if (_disposed || !ReferenceEquals(_scenario, Scenario.Cur))
            {
                return;
            }

            _scenario.citySet.ForEach(city =>
            {
                if (city != null && _cities.TryGetValue(city.Id, out Entity entity) && _world.IsAlive(entity))
                {
                    SyncCity(entity, city);
                }
            });
        }

        // ---- 内核事件面交换(经济/AI 决策;见 SangoCityEventSwap 与闸门自审) ----

        void SwapKernelFaces()
        {
            object classics = GameSystem.GetSystem<ClassicsCityWorking>()
                ?? throw new InvalidOperationException("Kernel system ClassicsCityWorking is not registered; the economy seam is unavailable.");
            object buildingWorking = GameSystem.GetSystem<BuildingWorking>()
                ?? throw new InvalidOperationException("Kernel system BuildingWorking is not registered; the harvest seam is unavailable.");
            _classicsBuildingTemplate = (int[][])SangoCityEventSwap.ReadKernelStaticField(
                typeof(ClassicsCityWorking), "CityBuildingTemplate");

            Type monthType = typeof(EventBase.EventDelegate<City, Scenario>);
            Type harvestType = typeof(EventBase.EventDelegate<City>);
            Type self = typeof(SangoCityNativeRuntime);
            const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

            _nativeIncome = self.GetMethod(nameof(OnNativeCityMonthStart), PrivateInstance)!.CreateDelegate(monthType, this);
            _kernelIncome = SangoCityEventSwap.CreateKernelHandler<EventBase.EventDelegate<City, Scenario>>(classics, "OnCityMonthStart");
            GameEvent.OnCityMonthStart = (EventBase.EventDelegate<City, Scenario>)SangoCityEventSwap.SwapInto(
                GameEvent.OnCityMonthStart, _kernelIncome, _nativeIncome);

            _nativeSeason = self.GetMethod(nameof(OnNativeCitySeasonStart), PrivateInstance)!.CreateDelegate(monthType, this);
            _kernelSeason = SangoCityEventSwap.CreateKernelHandler<EventBase.EventDelegate<City, Scenario>>(classics, "OnCitySeasonStart");
            GameEvent.OnCitySeasonStart = (EventBase.EventDelegate<City, Scenario>)SangoCityEventSwap.SwapInto(
                GameEvent.OnCitySeasonStart, _kernelSeason, _nativeSeason);

            _nativeHarvestCalc = self.GetMethod(nameof(OnNativeCalculateHarvest), PrivateInstance)!.CreateDelegate(harvestType, this);
            _kernelClassicsHarvest = SangoCityEventSwap.CreateKernelHandler<EventBase.EventDelegate<City>>(classics, "OnCityCalculateHarvest");
            _kernelBuildingHarvest = SangoCityEventSwap.CreateKernelHandler<EventBase.EventDelegate<City>>(buildingWorking, "OnCityCalculateHarvest");
            Delegate swappedClassics = SangoCityEventSwap.SwapInto(
                GameEvent.OnCityCalculateHarvest, _kernelClassicsHarvest, _nativeHarvestCalc);
            // 复合收获链:BuildingWorking 的覆盖段同样换下(原生 CompositeCalculateHarvest 已含两段;
            // 非当前世界时在 handler 内按原序还原两段)。
            GameEvent.OnCityCalculateHarvest = (EventBase.EventDelegate<City>)SangoCityEventSwap.RemoveFrom(
                swappedClassics, _kernelBuildingHarvest)!;

            _nativeAIPrepare = self.GetMethod(nameof(OnNativeCityAIPrepare), PrivateInstance)!.CreateDelegate(monthType, this);
            _kernelAIPrepare = SangoCityEventSwap.CreateKernelHandler<EventBase.EventDelegate<City, Scenario>>(classics, "OnCityAIPrepare");
            GameEvent.OnCityAIPrepare = (EventBase.EventDelegate<City, Scenario>)SangoCityEventSwap.SwapInto(
                GameEvent.OnCityAIPrepare, _kernelAIPrepare, _nativeAIPrepare);
        }

        void RestoreKernelFaces()
        {
            if (_kernelIncome != null)
            {
                GameEvent.OnCityMonthStart = (EventBase.EventDelegate<City, Scenario>)SangoCityEventSwap.SwapBack(
                    GameEvent.OnCityMonthStart, _kernelIncome, _nativeIncome!);
            }

            if (_kernelSeason != null)
            {
                GameEvent.OnCitySeasonStart = (EventBase.EventDelegate<City, Scenario>)SangoCityEventSwap.SwapBack(
                    GameEvent.OnCitySeasonStart, _kernelSeason, _nativeSeason!);
            }

            if (_kernelClassicsHarvest != null)
            {
                Delegate current = SangoCityEventSwap.SwapBack(
                    GameEvent.OnCityCalculateHarvest, _kernelClassicsHarvest, _nativeHarvestCalc!);
                GameEvent.OnCityCalculateHarvest = (EventBase.EventDelegate<City>)
                    (current == null ? _kernelBuildingHarvest : SangoCityEventSwap.Combine(current, _kernelBuildingHarvest!))!;
            }

            if (_kernelAIPrepare != null)
            {
                GameEvent.OnCityAIPrepare = (EventBase.EventDelegate<City, Scenario>)SangoCityEventSwap.SwapBack(
                    GameEvent.OnCityAIPrepare, _kernelAIPrepare, _nativeAIPrepare!);
            }
        }

        // ---- 原生结算 handler(经事件面在内核同位调用) ----

        void OnNativeCityMonthStart(City city, Scenario scenario)
        {
            if (!ReferenceEquals(_scenario, Scenario.Cur))
            {
                // 本运行时已非当前世界的权威(世界替换后、重建前):让位给被换下的内核
                // handler——任何世界的内核经济链不断流。
                _kernelIncome?.DynamicInvoke(city, scenario);
                return;
            }

            if (_disposed || city == null || city.mBelongCorps == null)
            {
                return;
            }

            if (!_cities.TryGetValue(city.Id, out Entity entity) || !_world.IsAlive(entity))
            {
                return;
            }

            SangoCityEconomy economy = _world.Get<SangoCityEconomy>(entity);
            (int income, int drawn) = SangoCityFormulas.MonthlyIncomeGold(city, economy.TotalGainGold);
            ApplyGold(city, entity, income);
            economy.LastIncomeGold = income;
            economy.LastIncomeDrawGold = drawn;
            economy.LastIncomeBaseGold = economy.TotalGainGold;
            // 俸给账本(D-2' 消桥 #1/#2):与内核随后的 GoldCost 同输入(收入不改变名单),
            // 确定性同值;武将读面走组件源(读缝同步见 SalaryGoldCost)。
            economy.LastSalaryGold = SangoCityFormulas.SalaryGoldCost(city);
            _world.Set(entity, economy);
        }

        void OnNativeCitySeasonStart(City city, Scenario scenario)
        {
            if (!ReferenceEquals(_scenario, Scenario.Cur))
            {
                _kernelSeason?.DynamicInvoke(city, scenario);
                return;
            }

            if (_disposed || city == null || city.mBelongCorps == null)
            {
                return;
            }

            if (!_cities.TryGetValue(city.Id, out Entity entity) || !_world.IsAlive(entity))
            {
                return;
            }

            SangoCityEconomy economy = _world.Get<SangoCityEconomy>(entity);
            (int harvest, int drawnFood) = SangoCityFormulas.SeasonalHarvestFood(city, economy.TotalGainFood);
            ApplyFood(city, entity, harvest);
            economy.LastHarvestFood = harvest;
            economy.LastHarvestDrawFood = drawnFood;
            economy.LastHarvestBaseFood = economy.TotalGainFood;
            _world.Set(entity, economy);
        }

        void OnNativeCalculateHarvest(City city)
        {
            if (!ReferenceEquals(_scenario, Scenario.Cur))
            {
                // 复合收获链还原:先内核 Classics 段,再内核 BuildingWorking 覆盖段。
                _kernelClassicsHarvest?.DynamicInvoke(city);
                _kernelBuildingHarvest?.DynamicInvoke(city);
                return;
            }

            if (_disposed || city == null)
            {
                return;
            }

            // 收获因子读缝(D-2' 消桥 #2):太守/工作武将五维读组件属性源,先落账真值。
            SangoPersonNativeRuntime.Active?.SettleTurnEnd();
            (int totalFood, int totalGold, float factor) = SangoCityFormulas.CompositeCalculateHarvest(city);
            if (_cities.TryGetValue(city.Id, out Entity entity) && _world.IsAlive(entity))
            {
                SangoCityEconomy economy = _world.Get<SangoCityEconomy>(entity);
                economy.TotalGainFood = totalFood;
                economy.TotalGainGold = totalGold;
                economy.PopulationIncreaseFactor = factor;
                _world.Set(entity, economy);
            }
        }

        void OnNativeCityAIPrepare(City city, Scenario scenario)
        {
            if (!ReferenceEquals(_scenario, Scenario.Cur))
            {
                _kernelAIPrepare?.DynamicInvoke(city, scenario);
                return;
            }

            if (_disposed || city == null)
            {
                return;
            }

            // 决策面原生 port(ClassicsCityWorking.OnCityAIPrepare 逐行);
            // 执行面仍内核 CityAI 函数(桥登记 #13)。
            CityAI.CityBuildingTemplate = _classicsBuildingTemplate!;
            List<Func<City, Scenario, bool>> commandList = city.AICommandList;
            var plan = default(SangoCityAIPlan);
            plan.BorderCity = city.IsBorderCity ? (byte)1 : (byte)0;
            plan.AIPreparedThisTurn = 1;
            if (city.IsBorderCity)
            {
                Append(commandList, ref plan, CityAI.AIRewardPerson, SangoCityAICommandKind.RewardPerson);
                Append(commandList, ref plan, CityAI.AISearching, SangoCityAICommandKind.Searching);
                Append(commandList, ref plan, CityAI.AIRecruitPerson, SangoCityAICommandKind.RecruitPerson);
                Append(commandList, ref plan, CityAI.AIAttack, SangoCityAICommandKind.Attack);
                Append(commandList, ref plan, CityAI.AITradeFood, SangoCityAICommandKind.TradeFood);
                Append(commandList, ref plan, CityAI.AISecurity, SangoCityAICommandKind.Security);
                Append(commandList, ref plan, CityAI.AITrainTroop, SangoCityAICommandKind.TrainTroop);
                if (city.troops < 20000)
                {
                    Append(commandList, ref plan, CityAI.AIRecruitTroop, SangoCityAICommandKind.RecruitTroop);
                    Append(commandList, ref plan, CityAI.AIIntrior, SangoCityAICommandKind.Interior);
                }
                else
                {
                    if (scenario.Info.day == 10)
                    {
                        Append(commandList, ref plan, CityAI.AIRecruitTroop, SangoCityAICommandKind.RecruitTroop);
                        Append(commandList, ref plan, CityAI.AICreateItems, SangoCityAICommandKind.CreateItems);
                        Append(commandList, ref plan, CityAI.AIIntrior, SangoCityAICommandKind.Interior);
                    }
                    else if (scenario.Info.day == 20)
                    {
                        Append(commandList, ref plan, CityAI.AIIntrior, SangoCityAICommandKind.Interior);
                        Append(commandList, ref plan, CityAI.AIRecruitTroop, SangoCityAICommandKind.RecruitTroop);
                        Append(commandList, ref plan, CityAI.AICreateItems, SangoCityAICommandKind.CreateItems);
                    }
                    else
                    {
                        Append(commandList, ref plan, CityAI.AICreateItems, SangoCityAICommandKind.CreateItems);
                        Append(commandList, ref plan, CityAI.AIRecruitTroop, SangoCityAICommandKind.RecruitTroop);
                        Append(commandList, ref plan, CityAI.AIIntrior, SangoCityAICommandKind.Interior);
                    }
                }

                Append(commandList, ref plan, CityAI.AITradeFood, SangoCityAICommandKind.TradeFood);
            }
            else
            {
                Append(commandList, ref plan, CityAI.AIRewardPerson, SangoCityAICommandKind.RewardPerson);
                Append(commandList, ref plan, CityAI.AISearching, SangoCityAICommandKind.Searching);
                Append(commandList, ref plan, CityAI.AIRecruitPerson, SangoCityAICommandKind.RecruitPerson);
                Append(commandList, ref plan, CityAI.AITransfrom, SangoCityAICommandKind.Transform);
                Append(commandList, ref plan, CityAI.AISecurity, SangoCityAICommandKind.Security);
                Append(commandList, ref plan, CityAI.AITradeFood, SangoCityAICommandKind.TradeFood);
                Append(commandList, ref plan, CityAI.AITrainTroop, SangoCityAICommandKind.TrainTroop);
                Append(commandList, ref plan, CityAI.AICreateItems, SangoCityAICommandKind.CreateItems);
                Append(commandList, ref plan, CityAI.AIRecruitTroop, SangoCityAICommandKind.RecruitTroop);
                Append(commandList, ref plan, CityAI.AIIntrior, SangoCityAICommandKind.Interior);
                Append(commandList, ref plan, CityAI.AIAttack, SangoCityAICommandKind.Attack);
            }

            if (_cities.TryGetValue(city.Id, out Entity entity) && _world.IsAlive(entity))
            {
                _world.Set(entity, plan);
            }
        }

        static void Append(List<Func<City, Scenario, bool>> commandList, ref SangoCityAIPlan plan,
            Func<City, Scenario, bool> command, SangoCityAICommandKind kind)
        {
            if (plan.Count >= SangoCityAIPlan.Capacity)
            {
                throw new InvalidOperationException(
                    $"SangoCityAIPlan capacity ({SangoCityAIPlan.Capacity}) exceeded; refusing to truncate the AI command plan.");
            }

            commandList.Add(command);
            plan.Kinds[plan.Count++] = (byte)kind;
        }

        // ---- 值应用(write-through:内核 PONO 与组件双写,公式 port 自带钳制语义) ----

        void ApplyGold(City city, Entity entity, int delta)
        {
            int gold = city.gold + delta;
            if (gold > city.GoldLimit)
            {
                gold = city.GoldLimit;
            }
            else if (gold < 0)
            {
                gold = 0;
            }

            city.gold = gold;
            SetBase(entity, SangoCityAttributes.GoldId, gold);
        }

        void ApplyFood(City city, Entity entity, int delta)
        {
            int food = city.food + delta;
            if (food > city.FoodLimit)
            {
                food = city.FoodLimit;
            }
            else if (food < 0)
            {
                food = 0;
            }

            city.food = food;
            SetBase(entity, SangoCityAttributes.FoodId, food);
        }
    }

    /// <summary>
    /// 原生城域·回合结算系统(SystemGroup.Cleanup:仿真结算后的城域维护相位,与
    /// SangoEntityMirrorSystem 同组):内核世界替换对账 + 回合边界结算的引擎侧
    /// 驱动面(headless 内核事件已先行结算,本面幂等)。
    /// </summary>
    public sealed class SangoCitySettlementSystem : ISystem<float>
    {
        readonly Func<GameEngine?> _engineSource;

        public SangoCitySettlementSystem(Func<GameEngine?> engineSource)
        {
            _engineSource = engineSource ?? throw new ArgumentNullException(nameof(engineSource));
        }

        public void Initialize() { }

        public void BeforeUpdate(in float dt) { }

        public void Update(in float dt)
        {
            SangoCityNativeRuntime? runtime = SangoCityNativeRuntime.Active;
            if (runtime is { IsDisposed: false })
            {
                runtime.Reconcile();
                runtime.SettleTurnEnd();
                return;
            }

            GameEngine? engine = _engineSource();
            if (engine != null && Scenario.Cur != null)
            {
                SangoCityNativeRuntime.Attach(engine);
            }
        }

        public void AfterUpdate(in float dt) { }

        public void Dispose() { }
    }

    /// <summary>
    /// 原生城域·城 AI 系统(SystemGroup.InputCollection:引擎 AI 相位,
    /// UtilityAiThinkScheduleSystem 同组):AI 计划组件的回合位维护与运行时自举。
    /// 决策面在 OnCityAIPrepare 事件位原生执行(见 SangoCityNativeRuntime)。
    /// </summary>
    public sealed class SangoCityAISystem : ISystem<float>
    {
        readonly Func<GameEngine?> _engineSource;
        int _lastStampedTurn = -1;

        public SangoCityAISystem(Func<GameEngine?> engineSource)
        {
            _engineSource = engineSource ?? throw new ArgumentNullException(nameof(engineSource));
        }

        public void Initialize() { }

        public void BeforeUpdate(in float dt) { }

        public void Update(in float dt)
        {
            SangoCityNativeRuntime? runtime = SangoCityNativeRuntime.Active;
            if (runtime is not { IsDisposed: false })
            {
                GameEngine? engine = _engineSource();
                if (engine != null && Scenario.Cur != null)
                {
                    SangoCityNativeRuntime.Attach(engine);
                }

                return;
            }

            Scenario? scenario = Scenario.Cur;
            if (scenario != null && scenario.Info.turnCount != _lastStampedTurn)
            {
                _lastStampedTurn = scenario.Info.turnCount;
                runtime.SettleTurnEnd();
            }
        }

        public void AfterUpdate(in float dt) { }

        public void Dispose() { }
    }
}
