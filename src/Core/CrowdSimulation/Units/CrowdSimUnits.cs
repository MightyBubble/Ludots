using System;
using System.Collections.Generic;
using Arch.Core;
using ArchWorld = Arch.Core.World;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Gameplay.Components;
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
    private readonly List<Entity> _dense = new();
    private readonly List<int> _slotOfDense = new();
    private readonly int[] _sparse;
    private readonly ushort[] _gen;
    private readonly int[] _freeSlots;
    private int _freeTop;

    public CrowdSimUnits(ArchWorld world, int capacity)
    {
        if (capacity > SlotMask + 1) throw new ArgumentOutOfRangeException(nameof(capacity), "单位容量超过句柄槽位上限。");
        _world = world ?? throw new ArgumentNullException(nameof(world));
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

    /// <summary>生成一个单位;满容量返回 -1(D54:失败不留痕迹)。</summary>
    public int Add(Fix64 x, Fix64 y, string profileId, int groupId, int playerId)
    {
        if (_dense.Count >= Capacity) return -1;
        int slot = _freeSlots[--_freeTop];
        int dense = _dense.Count;
        _sparse[slot] = dense;
        var entity = _world.Create(
            new CrowdSimulationAgent { ProfileId = profileId },
            new CrowdSimulationUnitState { Slot = slot, GroupId = groupId, State = 0, Mode = 0, Level = 0, Order = 0 },
            new WorldPositionCm { Value = new Fix64Vec2(x, y) },
            new PlayerOwner { PlayerId = playerId });
        _dense.Add(entity);
        _slotOfDense.Add(slot);
        return dense;
    }

    /// <summary>清空(代 bump:旧句柄全部失效;槽池回到初始 LIFO 序,回放可复现)。</summary>
    public void Clear()
    {
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
