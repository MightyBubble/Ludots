using System;
using System.Diagnostics;
using Ludots.Core.Networking.Runtime;

namespace Ludots.Adapter.LiteNetLib;

/// <summary>
/// Feeds acceptance metrics / proof writing from the network runtime pump path.
/// Authoritative full-tick duration should be reported by the host via
/// <see cref="NetworkAcceptanceProofService.ObserveFullTickMicroseconds"/> around <c>GameEngine.Tick</c>.
/// Replicated clients sample PumpTransport→PumpReplicatedClient wall time here.
/// </summary>
public sealed class InstrumentedNetworkRuntimePort : INetworkRuntimePort
{
    private readonly INetworkRuntimePort _inner;
    private readonly NetworkAcceptanceProofService? _proof;
    private readonly NetworkAdapterMetricsSampler? _metrics;
    private readonly AcceptanceBudgetEnforcer? _enforcer;
    private long _frameStartTimestamp;
    private bool _disposed;

    public InstrumentedNetworkRuntimePort(
        INetworkRuntimePort inner,
        NetworkAcceptanceProofService? proof = null,
        NetworkAdapterMetricsSampler? metrics = null,
        AcceptanceBudgetEnforcer? enforcer = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _proof = proof;
        _metrics = metrics ?? proof?.Metrics;
        _enforcer = enforcer ?? proof?.Enforcer;
        if (_metrics == null && (_proof != null || _enforcer != null))
        {
            throw new ArgumentException("Metrics sampler is required when proof or acceptance enforcement is enabled.");
        }
    }

    public NetworkProcessRole Role => _inner.Role;

    public void PumpTransport()
    {
        ThrowIfDisposed();
        _frameStartTimestamp = Stopwatch.GetTimestamp();
        _metrics?.AdvanceWallClock();
        _inner.PumpTransport();
        _proof?.PumpProof();
    }

    public void BeforeAuthoritativeTick(uint executingTick)
    {
        ThrowIfDisposed();
        _inner.BeforeAuthoritativeTick(executingTick);
    }

    public void AfterAuthoritativeCommit(uint committedTick)
    {
        ThrowIfDisposed();
        _inner.AfterAuthoritativeCommit(committedTick);
        _proof?.ObserveSnapshot();
        _proof?.PumpProof();
    }

    public void PumpReplicatedClient(float frameDeltaTime)
    {
        ThrowIfDisposed();
        _inner.PumpReplicatedClient(frameDeltaTime);
        if (_metrics != null && _frameStartTimestamp != 0)
        {
            ulong microseconds = ToMicroseconds(Stopwatch.GetTimestamp() - _frameStartTimestamp);
            _metrics.RecordTickMicroseconds(microseconds);
            _enforcer?.EnforceAfterTickSample();
            _proof?.PumpProof();
            _frameStartTimestamp = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _inner.Dispose();
    }

    private static ulong ToMicroseconds(long timestampDelta)
    {
        double microseconds = timestampDelta * 1_000_000.0 / Stopwatch.Frequency;
        if (microseconds <= 0)
        {
            return 0;
        }

        return (ulong)microseconds;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(InstrumentedNetworkRuntimePort));
    }
}
