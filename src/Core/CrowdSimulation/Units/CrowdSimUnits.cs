using System;
using System.Collections.Generic;
using Arch.Core;
using ArchWorld = Arch.Core.World;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>一次单位生成的实例输入:出生格算出的位置 + 配置解析出的默认仿真参数(模板缺省字段的回落值)。</summary>
public readonly record struct CrowdUnitSpawnRequest(
    Fix64 XCm,
    Fix64 YCm,
    int UnitType,
    int RadiusClass,
    int GroupId,
    int PlayerId,
    string ProfileId,
    Fix64 RadiusCm,
    Fix64 PersonalRadiusCm,
    Fix64 SpeedCmPerSecond,
    Fix64 PushPriority);

/// <summary>
/// CrowdSimulation 单位存储(ECS 实体,不另造 SoA):每个单位是一个 Arch 实体,
/// 带 CrowdSimulationAgent(模板写真 + 配置补全的仿真参数)、CrowdSimulationUnitState(仿真身份)、
/// WorldPositionCm、PlayerOwner。管理层只持三件东西:稠密序(创建序)、
/// 槽位代数组与空闲槽池——与参考实现 units 的 18 位槽位 + 14 位代句柄同构,
/// 句柄稳定且回放可复现。
/// 单位实体走引擎模板生成管线(EntityBuilder.UseTemplate→Build,RuntimeEntitySpawnSystem
/// 同款同步路径)实例化;出生算法(盐流/搜索/散布)留在内核,生成后钩子只补逐实例事实
/// (位置/玩家/仿真身份/呈现预置件)并把稠密序与句柄登记进来。
/// </summary>
public sealed class CrowdSimUnits
{
    public const int SlotBits = 18, SlotMask = (1 << SlotBits) - 1, GenMask = (1 << 14) - 1;

    private readonly ArchWorld _world;
    private readonly CrowdSimPresentationWiring _presentation;
    private readonly List<Entity> _dense = new();
    private readonly List<int> _slotOfDense = new();
    private readonly int[] _sparse;
    private readonly ushort[] _gen;
    private readonly int[] _freeSlots;
    private int _freeTop;
    private readonly Dictionary<string, EntityTemplate> _templateCache = new(StringComparer.Ordinal);
    private EntityBuilder? _builder;

    public CrowdSimUnits(ArchWorld world, int capacity, CrowdSimPresentationWiring presentation)
    {
        if (capacity > SlotMask + 1) throw new ArgumentOutOfRangeException(nameof(capacity), "单位容量超过句柄槽位上限。");
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        Capacity = capacity;
        _sparse = new int[capacity];
        _gen = new ushort[capacity];
        _freeSlots = new int[capacity];
        Clear();
    }

    public int Capacity { get; }
    public int Count => _dense.Count;
    public Entity EntityAt(int dense) => _dense[dense];
    public int SlotAt(int dense) => _slotOfDense[dense];
    public ushort GenOf(int slot) => _gen[slot];
    public uint HandleAt(int dense) => (uint)(((_gen[_slotOfDense[dense]] & GenMask) << SlotBits) | _slotOfDense[dense]);
    public int FreeTop => _freeTop;

    /// <summary>稠密下标 → 补全后的个人(避让/碰撞)半径(内核热路径读组件,不走字符串档案查表)。</summary>
    public Fix64 PersonalRadiusCmAt(int dense) => _world.Get<CrowdSimulationAgent>(_dense[dense]).ResolvedPersonalRadiusCm;

    /// <summary>生成一个单位;满容量返回 -1(失败不留痕迹)。
    /// 实体由模板生成管线实例化(模板写真组件 + Mod 作者附加组件都落到实体),再由钩子
    /// 补齐逐实例组件——单位创建只有这一条生产路径,引擎与无头装配同一管线。</summary>
    public int Add(in CrowdUnitSpawnRequest unit)
    {
        if (_dense.Count >= Capacity) return -1;
        int slot = _freeSlots[--_freeTop];
        int dense = _dense.Count;
        _sparse[slot] = dense;
        Entity entity = AddViaTemplate(in unit, slot);
        _dense.Add(entity);
        _slotOfDense.Add(slot);
        return dense;
    }

    private Entity AddViaTemplate(in CrowdUnitSpawnRequest unit, int slot)
    {
        var wiring = _presentation;
        (string templateId, int templateKeyId) = wiring.TemplatesByUnitTypeRadius[unit.UnitType][unit.RadiusClass];
        ResolveTemplate(templateId);
        _builder ??= new EntityBuilder(_world, _templateCache);
        var posCm = new Fix64Vec2(unit.XCm, unit.YCm);

        // 模板组件经 ComponentRegistry 数据驱动落到实体(模板写真 + Mod 作者附加组件);
        // 逐实例事实在生成后钩子补位(RuntimeEntitySpawnSystem.SpawnTemplate 同款形状)。
        Entity entity = _builder!.UseTemplate(templateId)
            .WithEntityContext($"CrowdSimulation unit '{templateId}'")
            .Build();

        CompleteAgent(entity, in unit);
        SetOrAdd(entity, new CrowdSimulationUnitState { Slot = slot, GroupId = unit.GroupId, State = 0, Mode = 0, Level = 0, Order = 0 });
        SetOrAdd(entity, new CrowdSimulationKinematics());
        SetOrAdd(entity, new WorldPositionCm { Value = posCm });
        // WorldToVisualSyncSystem 的查询要求 PreviousWorldPositionCm 同帧就位,
        // 否则 VisualTransform 永远停在默认值、presenter 沉在原点。
        SetOrAdd(entity, new PreviousWorldPositionCm { Value = posCm });
        SetOrAdd(entity, new PlayerOwner { PlayerId = unit.PlayerId });
        if (!_world.Has<Ludots.Core.Presentation.Components.PresentationStableId>(entity))
        {
            _world.Add(entity, new Ludots.Core.Presentation.Components.PresentationStableId { Value = wiring.StableIds.Allocate() });
        }

        SetOrAdd(entity, new EntityTemplateKeyRef { TemplateKeyId = templateKeyId });
        SetOrAdd(entity, new FacingDirection { AngleRad = 0f });
        SetOrAdd(entity, Ludots.Core.Presentation.Components.VisualTransform.Default);
        SetOrAdd(entity, new Ludots.Core.Presentation.Components.CullState { IsVisible = false, LOD = Ludots.Platform.Abstractions.LODLevel.Low });
        // 地形高度采样的门控标记:没有它 TerrainHeightSyncSystem 不写 Y,单位埋在海平面以下的地表里。
        SetOrAdd(entity, new Ludots.Core.Presentation.Components.ContinuousHeightmapSampleState());
        SetRadiusBlackboard(entity, in unit);
        return entity;
    }

    /// <summary>模板缺省的仿真参数按配置默认值补全(配置分层:unitTypes/agentTypes 是默认值层);
    /// 补全后内核只读本组件,不再做字符串档案查表。</summary>
    private void CompleteAgent(Entity entity, in CrowdUnitSpawnRequest unit)
    {
        var agent = _world.Get<CrowdSimulationAgent>(entity);
        agent.RadiusClassCm ??= (int)unit.RadiusCm.ToInt();
        agent.SpeedCmPerSecond ??= unit.SpeedCmPerSecond;
        agent.RadiusCm ??= unit.RadiusCm;
        agent.PersonalRadiusCm ??= unit.PersonalRadiusCm;
        agent.PushPriority ??= unit.PushPriority;
        _world.Set(entity, agent);
    }

    private void SetRadiusBlackboard(Entity entity, in CrowdUnitSpawnRequest unit)
    {
        float radiusMeters = (float)unit.RadiusCm.ToDouble() / 100f;
        int keyId = _presentation.RadiusMetersBlackboardKeyId;
        if (_world.Has<BlackboardFloatBuffer>(entity))
        {
            ref var buffer = ref _world.Get<BlackboardFloatBuffer>(entity);
            buffer.Set(keyId, radiusMeters);
        }
        else
        {
            var buffer = new BlackboardFloatBuffer();
            buffer.Set(keyId, radiusMeters);
            _world.Add(entity, buffer);
        }
    }

    private void SetOrAdd<T>(Entity entity, T component) where T : struct
    {
        if (_world.Has<T>(entity)) _world.Set(entity, component);
        else _world.Add(entity, component);
    }

    private void ResolveTemplate(string templateId)
    {
        if (_templateCache.ContainsKey(templateId)) return;
        var template = _presentation.TemplateRegistry.Get(templateId)
            ?? throw new InvalidOperationException(
                $"CrowdSimulation 单位模板 \"{templateId}\" 不存在(应由 Mod 的 Entities/templates.json 提供)。");
        _templateCache[templateId] = template;
    }

    /// <summary>清空(代 bump:旧句柄全部失效;槽池回到初始 LIFO 序,回放可复现)。</summary>
    public void Clear()
    {
        // 先摘选中集合再毁实体:集合成员是实体引用,销毁后留着只会指到死实体。
        // 空选中属主(回放派生接线)跳过镜像——活集合仓不可被回放触碰。
        if (_presentation.SelectionOwner != Entity.Null)
        {
            _presentation.Collections.Replace(
                _presentation.SelectionOwner,
                _presentation.SelectedCollectionKeyId,
                SelectionMirror.Descriptor,
                ReadOnlySpan<Entity>.Empty);
        }
        for (int i = 0; i < _dense.Count; i++)
        {
            _world.Destroy(_dense[i]);
            _gen[_slotOfDense[i]] = (ushort)((_gen[_slotOfDense[i]] + 1) & GenMask);
        }

        _dense.Clear();
        _slotOfDense.Clear();
        Array.Fill(_sparse, -1);
        for (int k = 0; k < Capacity; k++) _freeSlots[k] = Capacity - 1 - k;
        _freeTop = Capacity;
    }

    /// <summary>逐单位读位置与身份的视图(校验 / 遍历用,不复制)。</summary>
    public readonly struct UnitView
    {
        public required Entity Entity { get; init; }
        public required int Dense { get; init; }
        public required uint Handle { get; init; }
    }
}
