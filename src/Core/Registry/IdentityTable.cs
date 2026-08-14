using System;
using System.Collections.Generic;

namespace Ludots.Core.Registry;

public enum FrozenRegisterBehavior
{
    ThrowAlways,
    ReturnExisting,
}

public sealed class IdentityTable
{
    private readonly Dictionary<string, int> _nameToId;
    private readonly Dictionary<int, string> _idToName;
    private readonly string _tableName;
    private readonly int _maxExclusive;
    private readonly int _startId;
    private readonly FrozenRegisterBehavior _frozenRegister;
    private int _nextId;
    private bool _frozen;

    public IdentityTable(
        string tableName,
        int maxExclusive,
        int startId = 1,
        int invalidId = 0,
        StringComparer? comparer = null,
        FrozenRegisterBehavior frozenRegister = FrozenRegisterBehavior.ThrowAlways)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException("Table name is required.", nameof(tableName));
        }

        if (maxExclusive <= startId)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        }

        _tableName = tableName;
        _maxExclusive = maxExclusive;
        _startId = startId;
        InvalidId = invalidId;
        _nextId = startId;
        _frozenRegister = frozenRegister;
        _nameToId = new Dictionary<string, int>(comparer ?? StringComparer.OrdinalIgnoreCase);
        _idToName = new Dictionary<int, string>();
    }

    public int InvalidId { get; }
    public bool IsFrozen => _frozen;
    public int Count => _nameToId.Count;

    public void Freeze() => _frozen = true;

    public int Register(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException($"{_tableName} name cannot be null or whitespace.", nameof(name));
        }

        if (_frozen)
        {
            if (_frozenRegister == FrozenRegisterBehavior.ReturnExisting &&
                _nameToId.TryGetValue(name, out int existingWhenFrozen))
            {
                return existingWhenFrozen;
            }

            throw new InvalidOperationException($"{_tableName} is frozen. Cannot register '{name}'.");
        }

        if (_nameToId.TryGetValue(name, out int existing))
        {
            return existing;
        }

        if (_nextId >= _maxExclusive)
        {
            throw new InvalidOperationException(
                $"{_tableName} supports ids {_startId}..{_maxExclusive - 1}; capacity exhausted.");
        }

        int id = _nextId++;
        _nameToId[name] = id;
        _idToName[id] = name;
        return id;
    }

    public int GetId(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return InvalidId;
        }

        return _nameToId.TryGetValue(name, out int id) ? id : InvalidId;
    }

    public string GetName(int id)
    {
        return _idToName.TryGetValue(id, out string? name) ? name : string.Empty;
    }

    public bool ContainsId(int id) => _idToName.ContainsKey(id);

    public RegistryMapping[] SnapshotMappings()
    {
        return RegistryMappingSnapshot.FromNameToId(_nameToId);
    }
}
