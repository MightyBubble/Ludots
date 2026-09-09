using System;
using System.Runtime.CompilerServices;
using Arch.Core;

namespace Ludots.Core.Gameplay.GAS;

internal sealed class TransactionEntityIndex
{
    private readonly Entity[] _keys;
    private readonly int[] _values;
    private readonly int[] _stamps;
    private readonly int _mask;
    private int _stamp = 1;

    internal TransactionEntityIndex(int maximumEntries)
    {
        if (maximumEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        }

        int capacity = NextPowerOfTwo(maximumEntries);
        _keys = new Entity[capacity];
        _values = new int[capacity];
        _stamps = new int[capacity];
        _mask = capacity - 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGet(Entity key, out int value)
    {
        int slot = Hash(key) & _mask;
        for (int probes = 0; probes < _keys.Length; probes++)
        {
            if (_stamps[slot] != _stamp)
            {
                value = default;
                return false;
            }

            if (_keys[slot] == key)
            {
                value = _values[slot];
                return true;
            }

            slot = (slot + 1) & _mask;
        }

        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool Contains(Entity key) => TryGet(key, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Add(Entity key, int value)
    {
        int slot = Hash(key) & _mask;
        for (int probes = 0; probes < _keys.Length; probes++)
        {
            if (_stamps[slot] != _stamp)
            {
                _stamps[slot] = _stamp;
                _keys[slot] = key;
                _values[slot] = value;
                return;
            }

            if (_keys[slot] == key)
            {
                throw new InvalidOperationException("GAS.EFFECT_TRANSACTION.ERR.DuplicateEntityIndex");
            }

            slot = (slot + 1) & _mask;
        }

        throw new InvalidOperationException("GAS.EFFECT_TRANSACTION.ERR.EntityIndexCapacityExceeded");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Set(Entity key, int value)
    {
        int slot = Hash(key) & _mask;
        for (int probes = 0; probes < _keys.Length; probes++)
        {
            if (_stamps[slot] != _stamp)
            {
                _stamps[slot] = _stamp;
                _keys[slot] = key;
                _values[slot] = value;
                return;
            }

            if (_keys[slot] == key)
            {
                _values[slot] = value;
                return;
            }

            slot = (slot + 1) & _mask;
        }

        throw new InvalidOperationException("GAS.EFFECT_TRANSACTION.ERR.EntityIndexCapacityExceeded");
    }

    internal void Reset()
    {
        if (_stamp == int.MaxValue)
        {
            Array.Clear(_stamps, 0, _stamps.Length);
            _stamp = 1;
            return;
        }

        _stamp++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Hash(Entity key)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)key.Id) * 16777619u;
            hash = (hash ^ (uint)key.WorldId) * 16777619u;
            hash = (hash ^ (uint)key.Version) * 16777619u;
            return (int)(hash * 0x9E3779B1u);
        }
    }

    private static int NextPowerOfTwo(int value)
    {
        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        return checked(value + 1);
    }
}

internal sealed class TransactionEntityOwnerIndex
{
    private readonly Entity[] _entities;
    private readonly int[] _owners;
    private readonly int[] _values;
    private readonly int[] _stamps;
    private readonly int _mask;
    private int _stamp = 1;

    internal TransactionEntityOwnerIndex(int maximumEntries)
    {
        if (maximumEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        }

        int capacity = NextPowerOfTwo(maximumEntries);
        _entities = new Entity[capacity];
        _owners = new int[capacity];
        _values = new int[capacity];
        _stamps = new int[capacity];
        _mask = capacity - 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool Contains(Entity entity, int ownerEffectId) => TryGet(entity, ownerEffectId, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGet(Entity entity, int ownerEffectId, out int value)
    {
        int slot = Hash(entity, ownerEffectId) & _mask;
        for (int probes = 0; probes < _entities.Length; probes++)
        {
            if (_stamps[slot] != _stamp)
            {
                value = default;
                return false;
            }

            if (_entities[slot] == entity && _owners[slot] == ownerEffectId)
            {
                value = _values[slot];
                return true;
            }

            slot = (slot + 1) & _mask;
        }

        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Add(Entity entity, int ownerEffectId, int value)
    {
        int slot = Hash(entity, ownerEffectId) & _mask;
        for (int probes = 0; probes < _entities.Length; probes++)
        {
            if (_stamps[slot] != _stamp)
            {
                _stamps[slot] = _stamp;
                _entities[slot] = entity;
                _owners[slot] = ownerEffectId;
                _values[slot] = value;
                return;
            }

            if (_entities[slot] == entity && _owners[slot] == ownerEffectId)
            {
                throw new InvalidOperationException("GAS.EFFECT_TRANSACTION.ERR.DuplicateEntityOwnerIndex");
            }

            slot = (slot + 1) & _mask;
        }

        throw new InvalidOperationException("GAS.EFFECT_TRANSACTION.ERR.EntityOwnerIndexCapacityExceeded");
    }

    internal void Reset()
    {
        if (_stamp == int.MaxValue)
        {
            Array.Clear(_stamps, 0, _stamps.Length);
            _stamp = 1;
            return;
        }

        _stamp++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Hash(Entity entity, int ownerEffectId)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)entity.Id) * 16777619u;
            hash = (hash ^ (uint)entity.WorldId) * 16777619u;
            hash = (hash ^ (uint)entity.Version) * 16777619u;
            hash = (hash ^ (uint)ownerEffectId) * 16777619u;
            return (int)(hash * 0x9E3779B1u);
        }
    }

    private static int NextPowerOfTwo(int value)
    {
        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        return checked(value + 1);
    }
}
