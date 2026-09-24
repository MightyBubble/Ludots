using System;
using Ludots.Core.Networking.Configuration;

namespace Ludots.Adapter.LiteNetLib;

/// <summary>
/// Fail-closed acceptance budget checks against configured networking budgets.
/// </summary>
public sealed class AcceptanceBudgetEnforcer
{
    private readonly NetworkRuntimeConfig _config;
    private readonly NetworkAdapterMetricsSampler _metrics;
    private string? _budgetViolation;

    public AcceptanceBudgetEnforcer(NetworkRuntimeConfig config, NetworkAdapterMetricsSampler metrics)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        config.Validate();
        if (!config.AcceptanceMode)
        {
            throw new InvalidOperationException("AcceptanceBudgetEnforcer requires AcceptanceMode.");
        }
    }

    public string? BudgetViolation => _budgetViolation;

    public void EnforceAfterTickSample()
    {
        long p95 = _metrics.GetTickP95Microseconds();
        long p99 = _metrics.GetTickP99Microseconds();
        if (p95 > _config.TickP95BudgetMicroseconds)
        {
            Fail(
                $"Tick P95 {p95}us exceeds budget {_config.TickP95BudgetMicroseconds}us.");
        }

        if (p99 > _config.TickP99BudgetMicroseconds)
        {
            Fail(
                $"Tick P99 {p99}us exceeds budget {_config.TickP99BudgetMicroseconds}us.");
        }

        for (int client = 0; client < _metrics.ClientCapacity; client++)
        {
            long outboundP95 = _metrics.GetOutboundBytesPerSecondP95(client);
            if (outboundP95 > _config.MaxServerOutboundBytesPerSecondPerClient)
            {
                Fail(
                    $"Client {client} outbound P95 {outboundP95} B/s exceeds budget {_config.MaxServerOutboundBytesPerSecondPerClient} B/s.");
            }
        }
    }

    private void Fail(string message)
    {
        _budgetViolation = message;
        throw new InvalidOperationException($"AcceptanceMode budget violation: {message}");
    }
}
