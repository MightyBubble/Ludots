using System;
using Ludots.Core.Engine.Randomization;

namespace Ludots.Core.Gameplay.Rng;

public sealed class DistributionTable
{
    private readonly DistributionEntry[] _entries;
    private readonly float[] _shares;
    private readonly float _lockedShareAnchor;

    public DistributionTable(string id, string streamName, DistributionEntryConfig[] config)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("Distribution id is required.");
        }

        if (config == null || config.Length == 0)
        {
            throw new InvalidOperationException($"Distribution '{id}' must declare at least one entry.");
        }

        Id = id;
        StreamName = streamName;
        _entries = new DistributionEntry[config.Length];
        _shares = new float[config.Length];

        var totalWeight = 0;
        var lockedWeight = 0;
        for (var i = 0; i < config.Length; i++)
        {
            var entry = config[i];
            if (entry.Weight < 0)
            {
                throw new InvalidOperationException(
                    $"Distribution '{id}' entry '{entry.Id}' has negative weight {entry.Weight}.");
            }

            if (entry.Modulation != null && entry.Modulation.MinPermille > entry.Modulation.MaxPermille)
            {
                throw new InvalidOperationException(
                    $"Distribution '{id}' entry '{entry.Id}' modulation min ({entry.Modulation.MinPermille}) exceeds max ({entry.Modulation.MaxPermille}).");
            }

            _entries[i] = new DistributionEntry(entry.Id, entry.Weight, entry.Enabled, entry.Locked, entry.Modulation);
            totalWeight += entry.Weight;
            if (entry.Locked)
            {
                lockedWeight += entry.Weight;
            }
        }

        if (totalWeight <= 0)
        {
            throw new InvalidOperationException($"Distribution '{id}' total weight must be positive.");
        }

        _lockedShareAnchor = lockedWeight / (float)totalWeight;
        RecalculateShares();
    }

    public string Id { get; }

    public string StreamName { get; }

    public int EntryCount => _entries.Length;

    public DistributionEntry GetEntry(int index) => _entries[index];

    public float GetBaseShare(int index) => _shares[index];

    public float LockedShareAnchor => _lockedShareAnchor;

    public float GetEffectiveShare(int index, float modulation)
    {
        var entry = _entries[index];
        if (entry.Modulation is not { } modulationConfig)
        {
            return _shares[index];
        }

        var influence = modulation;
        if (float.IsNaN(influence) || float.IsInfinity(influence))
        {
            influence = 0f;
        }

        if (modulationConfig.Invert)
        {
            influence = -influence;
        }

        var minFactor = modulationConfig.MinPermille / 1000f;
        var maxFactor = modulationConfig.MaxPermille / 1000f;
        var factor = influence >= 0f
            ? 1f + (maxFactor - 1f) * influence
            : 1f + (minFactor - 1f) * -influence;

        return _shares[index] * Math.Clamp(factor, Math.Min(1f, minFactor), Math.Max(1f, maxFactor));
    }

    public int Pick(RngStream stream, float modulation)
    {
        var total = 0f;
        Span<float> effective = stackalloc float[_entries.Length];
        for (var i = 0; i < _entries.Length; i++)
        {
            effective[i] = _entries[i].Enabled ? GetEffectiveShare(i, modulation) : 0f;
            total += effective[i];
        }

        if (total <= 0f)
        {
            throw new InvalidOperationException(
                $"Distribution '{Id}' has no pickable entry (all disabled or zero share) at modulation {modulation}.");
        }

        var roll = stream.NextFloat01() * total;
        var cumulative = 0f;
        for (var i = 0; i < effective.Length; i++)
        {
            cumulative += effective[i];
            if (roll < cumulative)
            {
                return i;
            }
        }

        return effective.Length - 1;
    }

    public bool TrySetWeight(int index, int weight)
    {
        if (index < 0 || index >= _entries.Length)
        {
            return false;
        }

        if (weight < 0)
        {
            throw new InvalidOperationException($"Weight must not be negative, got {weight}.");
        }

        var entry = _entries[index];
        if (entry.Locked)
        {
            throw new InvalidOperationException(
                $"Entry '{entry.Id}' in distribution '{Id}' is locked; its share is frozen.");
        }

        if (weight == 0 && CountPositiveUnlockedExcluding(index) == 0)
        {
            throw new InvalidOperationException(
                $"Cannot zero out weight of '{entry.Id}': it is the only unlocked entry with positive weight in distribution '{Id}', leaving nothing to renormalize.");
        }

        _entries[index] = entry.WithWeight(weight);
        RecalculateShares();
        return true;
    }

    private int CountPositiveUnlockedExcluding(int index)
    {
        var count = 0;
        for (var i = 0; i < _entries.Length; i++)
        {
            if (i != index && !_entries[i].Locked && _entries[i].Weight > 0)
            {
                count++;
            }
        }

        return count;
    }

    private void RecalculateShares()
    {
        var unlockedWeight = 0;
        for (var i = 0; i < _entries.Length; i++)
        {
            if (!_entries[i].Locked)
            {
                unlockedWeight += _entries[i].Weight;
            }
        }

        if (unlockedWeight <= 0)
        {
            throw new InvalidOperationException(
                $"Distribution '{Id}' has no unlocked positive weight to normalize; unlock at least one entry.");
        }

        var unlockedBudget = Math.Clamp(1f - _lockedShareAnchor, 0f, 1f);
        for (var i = 0; i < _entries.Length; i++)
        {
            var share = _entries[i].Locked
                ? _lockedShareAnchor * (_entries[i].Weight / (float)(_lockedShareAnchor > 0f ? LockedTotalWeight() : 1))
                : _entries[i].Weight / (float)unlockedWeight * unlockedBudget;

            _shares[i] = float.IsNaN(share) || share < 1e-7f ? 0f : share;
        }
    }

    private int LockedTotalWeight()
    {
        var locked = 0;
        for (var i = 0; i < _entries.Length; i++)
        {
            if (_entries[i].Locked)
            {
                locked += _entries[i].Weight;
            }
        }

        return locked;
    }
}

public sealed record DistributionEntry(
    string Id,
    int Weight,
    bool Enabled,
    bool Locked,
    DistributionModulationConfig? Modulation)
{
    public DistributionEntry WithWeight(int weight) => this with { Weight = weight };
}
