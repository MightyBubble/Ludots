using System;
using Ludots.Adapter.LiteNetLib;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using NUnit.Framework;

namespace Ludots.Tests.NetworkingAdapter;

[TestFixture]
public sealed class DeferredNetworkRuntimePortTests
{
    [Test]
    public void Runtime_IsCreatedOnceOnFirstUse_AndDisposedByOwner()
    {
        var inner = new TrackingRuntime(NetworkProcessRole.AuthoritativeServer);
        int factoryCalls = 0;
        var deferred = new DeferredNetworkRuntimePort(
            NetworkProcessRole.AuthoritativeServer,
            () =>
            {
                factoryCalls++;
                return inner;
            });

        Assert.That(factoryCalls, Is.Zero);
        deferred.PumpTransport();
        deferred.BeforeAuthoritativeTick(1);
        deferred.AfterAuthoritativeCommit(1);
        deferred.Dispose();
        deferred.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(inner.PumpCount, Is.EqualTo(1));
            Assert.That(inner.BeforeTick, Is.EqualTo(1));
            Assert.That(inner.AfterTick, Is.EqualTo(1));
            Assert.That(inner.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void FactoryRoleMismatch_DisposesRejectedRuntimeAndFailsExplicitly()
    {
        var inner = new TrackingClientRuntime();
        var deferred = new DeferredNetworkRuntimePort(
            NetworkProcessRole.AuthoritativeServer,
            () => inner);

        Assert.That(
            deferred.PumpTransport,
            Throws.InvalidOperationException.With.Message.Contains("expected AuthoritativeServer"));
        Assert.That(inner.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void DisposeBeforeFirstUse_DoesNotCreateRuntime()
    {
        int factoryCalls = 0;
        var deferred = new DeferredReplicatedClientNetworkRuntimePort(
            () =>
            {
                factoryCalls++;
                return new TrackingClientRuntime();
            });

        deferred.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(factoryCalls, Is.Zero);
            Assert.That(deferred.PumpTransport, Throws.InstanceOf<ObjectDisposedException>());
        });
    }

    [Test]
    public void ReplicatedClientDeferred_ExposesBothInterfacesThroughOneInnerRuntime()
    {
        var inner = new TrackingClientRuntime();
        int factoryCalls = 0;
        var deferred = new DeferredReplicatedClientNetworkRuntimePort(
            () =>
            {
                factoryCalls++;
                return inner;
            });

        INetworkRuntimePort asTransport = deferred;
        IReplicatedClientFixedInputPort asFixedInput = deferred;
        IReplicatedClientNetworkRuntimePort asComposite = deferred;

        asTransport.PumpTransport();
        asTransport.PumpReplicatedClient(1f / 30f);
        FixedInputOutboxEnqueueStatus enqueued = asFixedInput.TrySubmitFixedInput(1, new byte[12]);
        FixedInputSendPulseResult pulsed = asFixedInput.TryPulseFixedInputSend();

        Assert.Multiple(() =>
        {
            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(ReferenceEquals(asTransport, asFixedInput), Is.True);
            Assert.That(ReferenceEquals(asComposite, deferred), Is.True);
            Assert.That(enqueued, Is.EqualTo(FixedInputOutboxEnqueueStatus.Enqueued));
            Assert.That(pulsed.Status, Is.EqualTo(FixedInputSendPulseStatus.Accepted));
            Assert.That(pulsed.HighestAcceptedTargetTick, Is.EqualTo(1u));
            Assert.That(inner.PumpCount, Is.EqualTo(1));
            Assert.That(inner.ClientPumpCount, Is.EqualTo(1));
            Assert.That(inner.SubmitCount, Is.EqualTo(1));
            Assert.That(inner.PulseCount, Is.EqualTo(1));
            Assert.That(asFixedInput.LastEnqueuedFixedInputTargetTick, Is.EqualTo(1u));
        });
    }

    [Test]
    public void AuthoritativeDeferred_DoesNotAdvertiseReplicatedClientContract()
    {
        var inner = new TrackingRuntime(NetworkProcessRole.AuthoritativeServer);
        var deferred = new DeferredNetworkRuntimePort(
            NetworkProcessRole.AuthoritativeServer,
            () => inner);

        deferred.PumpTransport();
        Assert.That(deferred, Is.Not.InstanceOf<IReplicatedClientNetworkRuntimePort>());
    }

    private sealed class TrackingRuntime : INetworkRuntimePort
    {
        public TrackingRuntime(NetworkProcessRole role)
        {
            Role = role;
        }

        public NetworkProcessRole Role { get; }
        public int PumpCount { get; private set; }
        public uint BeforeTick { get; private set; }
        public uint AfterTick { get; private set; }
        public int DisposeCount { get; private set; }

        public void PumpTransport() => PumpCount++;

        public void BeforeAuthoritativeTick(uint executingTick) => BeforeTick = executingTick;

        public void AfterAuthoritativeCommit(uint committedTick) => AfterTick = committedTick;

        public void PumpReplicatedClient(float frameDeltaTime)
        {
        }

        public void Dispose() => DisposeCount++;
    }

    private sealed class TrackingClientRuntime : IReplicatedClientNetworkRuntimePort
    {
        public NetworkProcessRole Role => NetworkProcessRole.ReplicatedClient;
        public ReplicatedClientConnectionState State => ReplicatedClientConnectionState.Connected;
        public SessionEpoch SessionEpoch { get; } = new(1);
        public uint FixedInputAcknowledgedCommittedTick { get; private set; }
        public ulong FixedInputAcknowledgementObservationVersion { get; private set; }
        public bool HasEnqueuedFixedInputTargetTick { get; private set; }
        public uint LastEnqueuedFixedInputTargetTick { get; private set; }
        public int PumpCount { get; private set; }
        public int ClientPumpCount { get; private set; }
        public int SubmitCount { get; private set; }
        public int PulseCount { get; private set; }
        public int DisposeCount { get; private set; }

        public void PumpTransport() => PumpCount++;

        public void BeforeAuthoritativeTick(uint executingTick)
        {
        }

        public void AfterAuthoritativeCommit(uint committedTick)
        {
        }

        public void PumpReplicatedClient(float frameDeltaTime) => ClientPumpCount++;

        public FixedInputOutboxEnqueueStatus TrySubmitFixedInput(uint targetTick, ReadOnlySpan<byte> payload)
        {
            SubmitCount++;
            HasEnqueuedFixedInputTargetTick = true;
            LastEnqueuedFixedInputTargetTick = targetTick;
            return FixedInputOutboxEnqueueStatus.Enqueued;
        }

        public FixedInputSendPulseResult TryPulseFixedInputSend()
        {
            PulseCount++;
            return new FixedInputSendPulseResult(
                FixedInputSendPulseStatus.Accepted,
                LastEnqueuedFixedInputTargetTick,
                LastEnqueuedFixedInputTargetTick,
                acceptedFrameCount: 1);
        }

        public void Dispose() => DisposeCount++;
    }
}
