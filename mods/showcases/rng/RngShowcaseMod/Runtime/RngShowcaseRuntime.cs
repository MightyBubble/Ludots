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
/// Deterministic pick loop over a Core distribution: counters, knobs, and a
/// snapshot-replay proof segment. All randomness flows through the named stream,
/// so any recorded segment can be restored and redrawn identically.
/// </summary>
public sealed class RngShowcaseRuntime
{
    public const int SegmentLength = 50;

    private readonly RngPickService _picks;
    private readonly int[] _actualCounts = new int[8];
    private readonly int[] _segmentPicks = new int[SegmentLength];
    private RngStreamSnapshot _segmentSnapshot;
    private bool _hasSegment;

    public RngShowcaseRuntime(RngPickService picks)
    {
        _picks = picks ?? throw new ArgumentNullException(nameof(picks));
        DistributionId = FirstDistributionId() ?? throw new InvalidOperationException(
            "RngShowcaseMod requires at least one distribution in Rng/distributions.json.");
    }

    public string DistributionId { get; private set; }
    public int ModulationPermille { get; private set; }
    public int BurstSize { get; private set; } = 10;
    public int IntervalTicks { get; private set; } = 30;
    public bool AutoRun { get; private set; } = true;
    public long TotalPicks { get; private set; }
    public int TickCounter { get; private set; }

    private string? FirstDistributionId()
    {
        return _picks.DistributionIds.OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();
    }

    public void Tick()
    {
        TickCounter++;
        if (AutoRun && TickCounter % IntervalTicks == 0)
        {
            DrawBurst(BurstSize);
        }
    }

    public int[] DrawBurst(int count)
    {
        var modulation = ModulationPermille / 1000f;
        var results = new int[count];
        for (var i = 0; i < count; i++)
        {
            results[i] = _picks.Pick(DistributionId, modulation);
            RecordPick(results[i]);
        }

        return results;
    }

    private void RecordPick(int entryIndex)
    {
        if (entryIndex >= 0 && entryIndex < _actualCounts.Length)
        {
            _actualCounts[entryIndex]++;
        }

        TotalPicks++;
    }

    public JsonObject SetKnobs(
        string? distributionId = null,
        int? modulationPermille = null,
        int? burstSize = null,
        int? intervalTicks = null,
        bool? autoRun = null)
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

        return BuildState();
    }

    public JsonObject VerifyReplay()
    {
        var stream = _picks.GetDistributionStream(DistributionId);
        var modulation = ModulationPermille / 1000f;

        var segmentStart = _hasSegment ? _segmentSnapshot : stream.CaptureSnapshot();
        if (!_hasSegment)
        {
            _segmentSnapshot = segmentStart;
            _hasSegment = true;
            for (var i = 0; i < SegmentLength; i++)
            {
                _segmentPicks[i] = _picks.Pick(DistributionId, modulation);
                RecordPick(_segmentPicks[i]);
            }
        }

        stream.RestoreSnapshot(in _segmentSnapshot);
        var replayed = new int[SegmentLength];
        for (var i = 0; i < SegmentLength; i++)
        {
            replayed[i] = _picks.Pick(DistributionId, modulation);
        }

        var matched = replayed.SequenceEqual(_segmentPicks);
        return new JsonObject
        {
            ["distribution"] = DistributionId,
            ["segmentLength"] = SegmentLength,
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
        var entries = new JsonArray();
        var actualTotal = 0L;
        for (var i = 0; i < table.EntryCount && i < _actualCounts.Length; i++)
        {
            actualTotal += _actualCounts[i];
        }

        for (var i = 0; i < table.EntryCount && i < _actualCounts.Length; i++)
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
                ["actual"] = _actualCounts[i],
                ["actualPct"] = actualTotal > 0 ? Math.Round(_actualCounts[i] * 100.0 / actualTotal, 2) : 0,
                ["expectedPct"] = Math.Round(expected * 100, 2),
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
            ["totalPicks"] = TotalPicks,
            ["tick"] = TickCounter,
            ["stream"] = new JsonObject
            {
                ["name"] = stream.StreamId,
                ["position"] = stream.Position,
            },
            ["entries"] = entries,
        };
    }
}
