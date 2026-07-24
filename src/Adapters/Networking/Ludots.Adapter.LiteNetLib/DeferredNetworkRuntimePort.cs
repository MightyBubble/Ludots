using System;
using Ludots.Core.Networking.Runtime;

namespace Ludots.Adapter.LiteNetLib;

/// <summary>
/// Lazily materializes the owned network runtime on first use.
/// </summary>
internal class DeferredNetworkRuntimePort : INetworkRuntimePort
{
    private readonly Func<INetworkRuntimePort> _factory;
    private INetworkRuntimePort? _runtime;
    private bool _disposed;

    public DeferredNetworkRuntimePort(NetworkProcessRole role, Func<INetworkRuntimePort> factory)
    {
        if (role == NetworkProcessRole.Standalone)
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        Role = role;
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public NetworkProcessRole Role { get; }

    public void PumpTransport() => GetRuntime().PumpTransport();

    public void BeforeAuthoritativeTick(uint executingTick) =>
        GetRuntime().BeforeAuthoritativeTick(executingTick);

    public void AfterAuthoritativeCommit(uint committedTick) =>
        GetRuntime().AfterAuthoritativeCommit(committedTick);

    public void PumpReplicatedClient(float frameDeltaTime) =>
        GetRuntime().PumpReplicatedClient(frameDeltaTime);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runtime?.Dispose();
        _runtime = null;
        OnRuntimeCleared();
    }

    protected INetworkRuntimePort GetRuntime()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_runtime != null)
        {
            return _runtime;
        }

        INetworkRuntimePort runtime = _factory() ??
            throw new InvalidOperationException("Network runtime factory returned null.");
        if (runtime.Role != Role)
        {
            runtime.Dispose();
            throw new InvalidOperationException(
                $"Network runtime factory returned role {runtime.Role}, expected {Role}.");
        }

        try
        {
            ValidateRuntime(runtime);
        }
        catch
        {
            runtime.Dispose();
            throw;
        }

        _runtime = runtime;
        return runtime;
    }

    protected virtual void ValidateRuntime(INetworkRuntimePort runtime)
    {
    }

    protected virtual void OnRuntimeCleared()
    {
    }
}
