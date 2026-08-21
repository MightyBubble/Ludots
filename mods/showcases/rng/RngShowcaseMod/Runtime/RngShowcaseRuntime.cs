using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.Engine.Randomization;
using Ludots.Core.Gameplay.Rng;
using Ludots.Core.Scripting;

namespace RngShowcaseMod.Runtime;

public static class RngShowcaseServiceKeys
{
    public static readonly ServiceKey<RngShowcaseRuntime> Runtime = new("RngShowcaseMod.Runtime");
}

/// <summary>
/// Deterministic pick loop over a Core distribution: per-distribution counters,
/// knobs, and a snapshot-replay proof that always covers the most recent burst.
/// All randomness flows through the named stream, so any recorded segment can be
/// restored and redrawn identically.
/// </summary>
public sealed class RngShowcaseRuntime
{
    public const int SegmentLengthCap = 50;

    private readonly RngPickService _picks;
    private readonly Dictionary<string, int[]> _countsByDistribution = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _totalsByDistribution = new(StringComparer.Ordinal);
    private int[] _segmentPicks = Array.Empty<int>();
    private RngStreamSnapshot _segmentSnapshot;
    private bool _hasSegment;

    public RngShowcaseRuntime(RngPickService picks)
    {
        _picks = picks ?? throw new ArgumentNullException(nameof(picks));
        DistributionId = FeaturedDistributionId() ?? throw new InvalidOperationException(
            "RngShowcaseMod requires at least one distribution in Rng/distributions.json.");
    }

    public string DistributionId { get; private set; }
    public int ModulationPermille { get; private set; }
    public int BurstSize { get; private set; } = 10;
    public int IntervalTicks { get; private set; } = 30;
    public bool AutoRun { get; private set; } = true;
    public long SimTicks { get; private set; }

    public long TotalPicks => _totalsByDistribution.TryGetValue(DistributionId, out var total) ? total : 0;

    private string? FeaturedDistributionId()
    {
        var ordered = _picks.DistributionIds.OrderBy(id => id, StringComparer.Ordinal).ToList();
        return ordered.FirstOrDefault(id => id.EndsWith(".loot", StringComparison.Ordinal)) ?? ordered.FirstOrDefault();
    }

    private int[] CountsFor(string distributionId)
    {
        if (!_countsByDistribution.TryGetValue(distributionId, out var counts))
        {
            counts = new int[_picks.GetDistribution(distributionId).EntryCount];
            _countsByDistribution.Add(distributionId, counts);
        }

        return counts;
    }

    public void Tick()
    {
        SimTicks++;
        if (AutoRun && SimTicks % IntervalTicks == 0)
        {
            DrawBurst(BurstSize);
        }
    }

    public int[] DrawBurst(int count)
    {
        count = Math.Clamp(count, 1, 1000);
        var modulation = ModulationPermille / 1000f;
        var stream = _picks.GetDistributionStream(DistributionId);
        var snapshot = stream.CaptureSnapshot();

        var counts = CountsFor(DistributionId);
        var total = TotalPicks;
        var results = new int[count];
        for (var i = 0; i < count; i++)
        {
            results[i] = _picks.Pick(DistributionId, modulation);
            if (results[i] >= 0 && results[i] < counts.Length)
            {
                counts[results[i]]++;
            }

            total++;
        }

        _totalsByDistribution[DistributionId] = total;
        _segmentSnapshot = snapshot;
        _segmentPicks = results;
        _hasSegment = true;
        return results;
    }

    public JsonObject SetKnobs(
        string? distributionId = null,
        int? modulationPermille = null,
        int? burstSize = null,
        int? intervalTicks = null,
        bool? autoRun = null,
        bool resetStats = false)
    {
        if (distributionId != null)
        {
            _picks.GetDistribution(distributionId);
            DistributionId = distributionId;
        }

        if (modulationPermille.HasValue)
        {
            ModulationPermille = Math.Clamp(modulationPermille.Value, -1000, 1000);
        }

        if (burstSize.HasValue)
        {
            BurstSize = Math.Clamp(burstSize.Value, 1, 1000);
        }

        if (intervalTicks.HasValue)
        {
            IntervalTicks = Math.Clamp(intervalTicks.Value, 1, 600);
        }

        if (autoRun.HasValue)
        {
            AutoRun = autoRun.Value;
        }

        if (resetStats)
        {
            Array.Clear(CountsFor(DistributionId));
            _totalsByDistribution[DistributionId] = 0;
            _hasSegment = false;
            _segmentPicks = Array.Empty<int>();
        }

        return BuildState();
    }

    public JsonObject VerifyReplay()
    {
        var stream = _picks.GetDistributionStream(DistributionId);
        var modulation = ModulationPermille / 1000f;

        if (!_hasSegment)
        {
            _segmentPicks = DrawBurst(SegmentLengthCap);
        }

        stream.RestoreSnapshot(in _segmentSnapshot);
        var replayed = new int[_segmentPicks.Length];
        for (var i = 0; i < replayed.Length; i++)
        {
            replayed[i] = _picks.Pick(DistributionId, modulation);
        }

        var matched = replayed.SequenceEqual(_segmentPicks);
        return new JsonObject
        {
            ["distribution"] = DistributionId,
            ["segmentLength"] = _segmentPicks.Length,
            ["matched"] = matched,
            ["original"] = new JsonArray(_segmentPicks.Select(p => (JsonNode)p!).ToArray()),
            ["replayed"] = new JsonArray(replayed.Select(p => (JsonNode)p!).ToArray()),
            ["streamPosition"] = stream.Position,
        };
    }

    public JsonObject BuildState()
    {
        var table = _picks.GetDistribution(DistributionId);
        var modulation = ModulationPermille / 1000f;
        var counts = CountsFor(DistributionId);
        var entries = new JsonArray();
        var actualTotal = 0L;
        var pickableTotal = 0f;
        for (var i = 0; i < table.EntryCount && i < counts.Length; i++)
        {
            actualTotal += counts[i];
            if (table.GetEntry(i).Enabled)
            {
                pickableTotal += table.GetEffectiveShare(i, modulation);
            }
        }

        for (var i = 0; i < table.EntryCount && i < counts.Length; i++)
        {
            var entry = table.GetEntry(i);
            var expected = table.GetEffectiveShare(i, modulation);
            entries.Add(new JsonObject
            {
                ["index"] = i,
                ["id"] = entry.Id,
                ["weight"] = entry.Weight,
                ["locked"] = entry.Locked,
                ["enabled"] = entry.Enabled,
                ["baseShare"] = Math.Round(table.GetBaseShare(i), 5),
                ["effectiveShare"] = Math.Round(expected, 5),
                ["actual"] = counts[i],
                ["actualPct"] = actualTotal > 0 ? Math.Round(counts[i] * 100.0 / actualTotal, 2) : 0,
                ["expectedPct"] = entry.Enabled && pickableTotal > 0 ? Math.Round(expected / pickableTotal * 100, 2) : 0,
            });
        }

        var stream = _picks.GetDistributionStream(DistributionId);
        return new JsonObject
        {
            ["distribution"] = DistributionId,
            ["availableDistributions"] = new JsonArray(_picks.DistributionIds.OrderBy(id => id, StringComparer.Ordinal).Select(id => (JsonNode)id!).ToArray()),
            ["modulationPermille"] = ModulationPermille,
            ["burstSize"] = BurstSize,
            ["intervalTicks"] = IntervalTicks,
            ["autoRun"] = AutoRun,
            ["totalPicks"] = actualTotal,
            ["simTick"] = SimTicks,
            ["stream"] = new JsonObject
            {
                ["name"] = stream.StreamId,
                ["position"] = stream.Position,
            },
            ["entries"] = entries,
        };
    }
}
