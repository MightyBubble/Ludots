using System;
using System.Collections.Generic;
using Arch.Core;
using ArchWorld = Arch.Core.World;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// CrowdSimulation 单位存储(ECS 实体,不另造 SoA):每个单位是一个 Arch 实体,
/// 带 CrowdSimulationAgent(profile)、CrowdSimulationUnitState(仿真身份)、
/// WorldPositionCm、PlayerOwner。管理层只持三件东西:稠密序(创建序)、
/// 槽位代数组与空闲槽池——与参考实现 units 的 18 位槽位 + 14 位代句柄同构,
/// 句柄稳定且回放可复现。
/// </summary>
public sealed class CrowdSimUnits
{
    public const int SlotBits = 18, SlotMask = (1 << SlotBits) - 1, GenMask = (1 << 14) - 1;

    private readonly ArchWorld _world;
    private readonly CrowdSimPresentationWiring? _presentation;
    private readonly List<Entity> _dense = new();
    private readonly List<int> _slotOfDense = new();
    private readonly int[] _sparse;
    private readonly ushort[] _gen;
    private readonly int[] _freeSlots;
    private int _freeTop;

    public CrowdSimUnits(ArchWorld world, int capacity, CrowdSimPresentationWiring? presentation = null)
    {
        if (capacity > SlotMask + 1) throw new ArgumentOutOfRangeException(nameof(capacity), "单位容量超过句柄槽位上限。");
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _presentation = presentation;
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

    /// <summary>稠密下标 → 体型 id(运动参数解析用;不写存储,从组件读)。</summary>
    public string ProfileIdAt(int dense) => _world.Get<CrowdSimulationAgent>(_dense[dense]).ProfileId;

    /// <summary>生成一个单位;满容量返回 -1(D54:失败不留痕迹)。
    /// 接线存在时同时挂上呈现投影所需的事实(稳定 id / 模板键 / 朝向),
    /// 之后由 PresentationEntityLifecycleSystem 数据驱动地建 presenter——生成路径本身不变。</summary>
    public int Add(Fix64 x, Fix64 y, string profileId, int groupId, int playerId, Fix64 radiusCm)
    {
        if (_dense.Count >= Capacity) return -1;
        int slot = _freeSlots[--_freeTop];
        int dense = _dense.Count;
        _sparse[slot] = dense;
        Entity entity;
        if (_presentation != null)
        {
            _presentation.TemplateKeyByProfileId.TryGetValue(profileId, out int templateKeyId);
            var posCm = new Fix64Vec2(x, y);
            var blackboard = new Ludots.Core.Gameplay.GAS.Components.BlackboardFloatBuffer();
            blackboard.Set(_presentation.RadiusMetersBlackboardKeyId, (float)radiusCm.ToDouble() / 100f);
            entity = _world.Create(
                new CrowdSimulationAgent { ProfileId = profileId },
                new CrowdSimulationUnitState { Slot = slot, GroupId = groupId, State = 0, Mode = 0, Level = 0, Order = 0 },
                new CrowdSimulationKinematics(),
                new WorldPositionCm { Value = posCm },
                // WorldToVisualSyncSystem 的查询要求 PreviousWorldPositionCm 同帧就位
                // (生成管线预置件同款),否则 VisualTransform 永远停在默认值、presenter 沉在原点。
                new PreviousWorldPositionCm { Value = posCm },
                new PlayerOwner { PlayerId = playerId },
                new Ludots.Core.Presentation.Components.PresentationStableId { Value = _presentation.StableIds.Allocate() },
                new EntityTemplateKeyRef { TemplateKeyId = templateKeyId },
                new FacingDirection { AngleRad = 0f },
                // WorldToVisualSyncSystem 的读取端:VisualTransform 是它的输出槽,
                // CullState 是相机剔除的状态槽——两者都是生成管线预置件的同款形状。
                Ludots.Core.Presentation.Components.VisualTransform.Default,
                new Ludots.Core.Presentation.Components.CullState { IsVisible = false, LOD = Ludots.Platform.Abstractions.LODLevel.Low },
                // 地形高度采样的门控标记(模板 `{}` 空对象的等价物):没有它 TerrainHeightSyncSystem
                // 不写 Y,单位会埋在海平面以下的地表里。
                new Ludots.Core.Presentation.Components.ContinuousHeightmapSampleState(),
                blackboard);
        }
        else
        {
            entity = _world.Create(
                new CrowdSimulationAgent { ProfileId = profileId },
                new CrowdSimulationUnitState { Slot = slot, GroupId = groupId, State = 0, Mode = 0, Level = 0, Order = 0 },
                new CrowdSimulationKinematics(),
                new WorldPositionCm { Value = new Fix64Vec2(x, y) },
                new PlayerOwner { PlayerId = playerId });
        }

        _dense.Add(entity);
        _slotOfDense.Add(slot);
        return dense;
    }

    /// <summary>清空(代 bump:旧句柄全部失效;槽池回到初始 LIFO 序,回放可复现)。</summary>
    public void Clear()
    {
        // 先摘选中集合再毁实体:集合成员是实体引用,销毁后留着只会指到死实体。
        _presentation?.Collections.Replace(
            _presentation.SelectionOwner,
            _presentation.SelectedCollectionKeyId,
            SelectionMirror.Descriptor,
            ReadOnlySpan<Entity>.Empty);
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
