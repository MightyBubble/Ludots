using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Networking.Runtime;

namespace Ludots.Adapter.LiteNetLib;

internal sealed class DeferredNetworkRuntimeComposition
{
    public DeferredNetworkRuntimeComposition(
        INetworkRuntimePort runtime,
        INetworkFaultInjectionMetricsPort faultInjectionMetrics,
        Action publishServices)
    {
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        FaultInjectionMetrics = faultInjectionMetrics ??
            throw new ArgumentNullException(nameof(faultInjectionMetrics));
        PublishServices = publishServices ?? throw new ArgumentNullException(nameof(publishServices));
    }

    public INetworkRuntimePort Runtime { get; }
    public INetworkFaultInjectionMetricsPort FaultInjectionMetrics { get; }
    public Action PublishServices { get; }
}

internal sealed class DeferredNetworkRuntimePort :
    INetworkRuntimePort,
    IReplicatedClientRuntimeStatus,
    IPresentationInterpolationSource,
    INetworkFaultInjectionMetricsPort
{
    private readonly Func<DeferredNetworkRuntimeComposition> _factory;
    private INetworkRuntimePort? _runtime;
    private INetworkFaultInjectionMetricsPort? _faultInjectionMetrics;
    private Exception? _activationFailure;
    private ActivationState _state;
    private bool _disposed;

    public DeferredNetworkRuntimePort(
        NetworkProcessRole role,
        Func<DeferredNetworkRuntimeComposition> factory)
    {
        if (role == NetworkProcessRole.Standalone)
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        Role = role;
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public NetworkProcessRole Role { get; }

    public ReplicatedClientConnectionState ConnectionState => GetClientStatus().ConnectionState;

    public bool HasEstablishedSession => GetClientStatus().HasEstablishedSession;

    public bool IsAwaitingFullSnapshot => GetClientStatus().IsAwaitingFullSnapshot;

    public bool IsFaulted => GetClientStatus().IsFaulted;

    public uint LastCommittedTick => GetClientStatus().LastCommittedTick;

    public float ReconnectWindowRemainingSeconds => GetClientStatus().ReconnectWindowRemainingSeconds;

    public int RoundTripTimeMilliseconds => GetClientStatus().RoundTripTimeMilliseconds;

    public float InterpolationAlpha => GetClientInterpolationSource().InterpolationAlpha;

    public NetworkFaultInjectionObservationSnapshot Capture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_state != ActivationState.Activated || _faultInjectionMetrics == null)
        {
            throw new InvalidOperationException(
                "Network fault injection metrics are unavailable until the network runtime is activated.");
        }

        NetworkFaultInjectionObservationSnapshot snapshot = _faultInjectionMetrics.Capture();
        if (snapshot.Role != Role)
        {
            throw new InvalidOperationException(
                $"Network fault injection metrics role {snapshot.Role} does not match runtime role {Role}.");
        }

        return snapshot;
    }

    public void Activate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_state == ActivationState.Activated)
        {
            return;
        }

        if (_state == ActivationState.Activating)
        {
            throw new InvalidOperationException("Network runtime activation cannot re-enter itself.");
        }

        if (_state == ActivationState.Faulted)
        {
            throw new InvalidOperationException(
                "Network runtime activation previously failed and cannot retry frozen schema composition.",
                _activationFailure);
        }

        _state = ActivationState.Activating;
        INetworkRuntimePort? runtime = null;
        try
        {
            DeferredNetworkRuntimeComposition composition = _factory() ??
                throw new InvalidOperationException("Network runtime composition factory returned null.");
            runtime = composition.Runtime;
            if (runtime.Role != Role)
            {
                throw new InvalidOperationException(
                    $"Network runtime factory returned role {runtime.Role}, expected {Role}.");
            }

            runtime.Activate();
            composition.PublishServices();
            _runtime = runtime;
            _faultInjectionMetrics = composition.FaultInjectionMetrics;
            _state = ActivationState.Activated;
        }
        catch (Exception exception)
        {
            runtime?.Dispose();
            _activationFailure = exception;
            _state = ActivationState.Faulted;
            throw;
        }
    }

    public void PumpTransport() => GetActivatedRuntime().PumpTransport();

    public void BeforeAuthoritativeTick(uint executingTick) =>
        GetActivatedRuntime().BeforeAuthoritativeTick(executingTick);

    public void AfterAuthoritativeCommit(uint committedTick) =>
        GetActivatedRuntime().AfterAuthoritativeCommit(committedTick);

    public void PumpReplicatedClient(float frameDeltaTime) =>
        GetActivatedRuntime().PumpReplicatedClient(frameDeltaTime);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runtime?.Dispose();
        _runtime = null;
        _faultInjectionMetrics = null;
        _state = ActivationState.Disposed;
    }

    private INetworkRuntimePort GetActivatedRuntime()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _runtime ??
            throw new InvalidOperationException(
                "Network runtime has not been activated after GameStart schema registration.");
    }

    private IReplicatedClientRuntimeStatus GetClientStatus()
    {
        if (Role != NetworkProcessRole.ReplicatedClient)
        {
            throw new InvalidOperationException("Only a replicated-client runtime exposes client connection status.");
        }

        return GetActivatedRuntime() as IReplicatedClientRuntimeStatus ??
            throw new InvalidOperationException("Replicated-client runtime does not expose the required client status contract.");
    }

    private IPresentationInterpolationSource GetClientInterpolationSource()
    {
        if (Role != NetworkProcessRole.ReplicatedClient)
        {
            throw new InvalidOperationException("Only a replicated-client runtime exposes presentation interpolation.");
        }

        return GetActivatedRuntime() as IPresentationInterpolationSource ??
            throw new InvalidOperationException("Replicated-client runtime does not expose presentation interpolation.");
    }

    private enum ActivationState : byte
    {
        Installed = 0,
        Activating = 1,
        Activated = 2,
        Faulted = 3,
        Disposed = 4,
    }
}
