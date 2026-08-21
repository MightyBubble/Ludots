using System;
using System.Collections.Generic;
using Ludots.Core.Engine.Randomization;
using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.Rng;

public sealed class RngPickService
{
    private readonly IRngStreamService _streams;
    private readonly Dictionary<string, DistributionTable> _tables;
    private readonly StringIntRegistry _keyIds = new(comparer: StringComparer.Ordinal);

    public RngPickService(IRngStreamService streams, IReadOnlyList<DistributionTable> tables)
    {
        _streams = streams;
        _tables = new Dictionary<string, DistributionTable>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            if (_tables.ContainsKey(table.Id))
            {
                throw new InvalidOperationException($"Duplicate distribution id '{table.Id}'.");
            }

            _tables.Add(table.Id, table);
            _keyIds.Register(table.Id);
        }
    }

    public IReadOnlyCollection<string> DistributionIds => _tables.Keys;

    public DistributionTable GetDistribution(string distributionId)
    {
        if (_tables.TryGetValue(distributionId, out var table))
        {
            return table;
        }

        throw new InvalidOperationException(
            $"Distribution '{distributionId}' is not declared. Declare it in assets/Rng/distributions.json before use.");
    }

    public int Pick(string distributionId, float modulation = 0f)
    {
        var table = GetDistribution(distributionId);
        var stream = _streams.GetStream(table.StreamName);
        return table.Pick(stream, modulation);
    }

    public int GetDistributionKeyId(string distributionId)
    {
        GetDistribution(distributionId);
        return _keyIds.GetId(distributionId);
    }

    public int ResolveDistributionKey(string name)
    {
        var id = _keyIds.GetId(name);
        if (id == _keyIds.InvalidId)
        {
            throw new InvalidOperationException(
                $"Unknown rng distribution '{name}'. Declare it in assets/Rng/distributions.json before referencing it from a graph.");
        }

        return id;
    }

    public int PickByKeyId(int keyId, float modulation)
    {
        var distributionId = _keyIds.GetName(keyId);
        if (string.IsNullOrEmpty(distributionId))
        {
            throw new InvalidOperationException($"Unknown distribution key id {keyId}.");
        }

        return Pick(distributionId, modulation);
    }

    public RngStream GetDistributionStream(string distributionId)
    {
        return _streams.GetStream(GetDistribution(distributionId).StreamName);
    }
}
