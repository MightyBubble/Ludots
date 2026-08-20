using System;
using System.Collections.Generic;
using Ludots.Core.Engine.Randomization;

namespace Ludots.Core.Gameplay.Rng;

public sealed class RngPickService
{
    private readonly IRngStreamService _streams;
    private readonly Dictionary<string, DistributionTable> _tables;
    private readonly Dictionary<int, string> _tablesByKeyId = new();

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
            _tablesByKeyId.Add(_tablesByKeyId.Count + 1, table.Id);
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
        var table = GetDistribution(distributionId);
        foreach (var pair in _tablesByKeyId)
        {
            if (_tables[pair.Value] == table) return pair.Key;
        }

        throw new InvalidOperationException($"Distribution '{distributionId}' has no key id.");
    }

    public int PickByKeyId(int keyId, float modulation)
    {
        if (!_tablesByKeyId.TryGetValue(keyId, out var distributionId))
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
