using System;
using System.Collections.Generic;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace RngCapabilityMod.Rng;

public sealed class RngGraphOpContext
{
    private readonly Dictionary<string, int> _distributionOpIds = new(StringComparer.Ordinal);
    private RngPickService? _service;

    public RngPickService? Service => _service;

    public void InstallService(RngPickService service)
    {
        _service = service;
        _distributionOpIds.Clear();
        foreach (var id in service.DistributionIds)
        {
            _distributionOpIds.Add(id, _distributionOpIds.Count + 1);
        }
    }

    public int GetDistributionOpId(string distributionId)
    {
        if (_distributionOpIds.TryGetValue(distributionId, out var opId))
        {
            return opId;
        }

        throw new InvalidOperationException(
            $"Distribution '{distributionId}' has no graph-op id; the service must be installed first.");
    }

    public bool TryGetDistributionByOpId(int opId, out string distributionId)
    {
        foreach (var pair in _distributionOpIds)
        {
            if (pair.Value == opId)
            {
                distributionId = pair.Key;
                return true;
            }
        }

        distributionId = string.Empty;
        return false;
    }
}

public static class WeightedPickGraphOp
{
    public const int NoPickResult = -1;

    public static GasGraphOpHandler CreateHandler(RngGraphOpContext opContext)
    {
        return (ref GraphExecutionState state, in GraphInstruction ins, ref int pc) =>
        {
            var modulationPermille = state.I[ins.A];
            state.I[ins.Dst] = Execute(opContext, ins.Imm, modulationPermille / 1000f);
        };
    }

    public static int Execute(RngGraphOpContext opContext, int distributionOpId, float modulation)
    {
        var service = opContext.Service
            ?? throw new InvalidOperationException(
                "RngCapabilityMod pick service is not installed; distributions must load before graph execution.");

        if (!opContext.TryGetDistributionByOpId(distributionOpId, out var distributionId))
        {
            throw new InvalidOperationException(
                $"Unknown distribution op id {distributionOpId}; it was not interned at service install.");
        }

        return service.Pick(distributionId, modulation);
    }
}
