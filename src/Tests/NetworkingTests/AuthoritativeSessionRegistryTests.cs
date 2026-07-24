using System.Reflection;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Transport;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class AuthoritativeSessionRegistryTests
{
    private static readonly ProtocolVersion Protocol = new(1, 0);
    private static readonly ContentFingerprint Content = ContentFingerprintBuilder.FromCanonicalBytes("rts_duel_v1"u8);
    private static readonly SessionEpoch Epoch = new(1);

    [Test]
    public void TwoConnections_ReceiveDistinctPlayerSeats()
    {
        var registry = CreateRegistry(seatCapacity: 2);

        Assert.That(registry.TryHandshake(new ConnectionId(10), JoinRequest(), currentTick: 1, out SessionHandshakeResponse first), Is.True);
        Assert.That(registry.TryHandshake(new ConnectionId(20), JoinRequest(), currentTick: 1, out SessionHandshakeResponse second), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(first.Accepted, Is.True);
            Assert.That(second.Accepted, Is.True);
            Assert.That(first.PlayerId, Is.Not.EqualTo(second.PlayerId));
            Assert.That(first.SessionEpoch, Is.EqualTo(Epoch));
            Assert.That(second.SessionEpoch, Is.EqualTo(Epoch));
            Assert.That(registry.TryGetPlayerId(new ConnectionId(10), out PlayerId playerA), Is.True);
            Assert.That(registry.TryGetPlayerId(new ConnectionId(20), out PlayerId playerB), Is.True);
            Assert.That(playerA, Is.EqualTo(first.PlayerId));
            Assert.That(playerB, Is.EqualTo(second.PlayerId));
            Assert.That(first.Seat.IsValid, Is.True);
            Assert.That(second.Seat.IsValid, Is.True);
            Assert.That(first.Seat, Is.Not.EqualTo(second.Seat));
        });
    }

    [Test]
    public void HandshakeRequest_CannotSpoofPlayerIdentityByConstruction()
    {
        PropertyInfo[] properties = typeof(SessionHandshakeRequest).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        Assert.That(properties.Any(p => p.Name.Contains("Player", StringComparison.Ordinal)), Is.False);

        FieldInfo[] fields = typeof(SessionHandshakeRequest).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(fields.Any(f => f.Name.Contains("Player", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public void ProtocolAndContentMismatch_AreExplicitRejects()
    {
        var registry = CreateRegistry(seatCapacity: 2);

        Assert.That(
            registry.TryHandshake(
                new ConnectionId(1),
                new SessionHandshakeRequest(new ProtocolVersion(2, 0), Content),
                currentTick: 1,
                out SessionHandshakeResponse protocolReject),
            Is.False);
        Assert.That(protocolReject.RejectReason, Is.EqualTo(HandshakeRejectReason.ProtocolMismatch));

        Assert.That(
            registry.TryHandshake(
                new ConnectionId(2),
                new SessionHandshakeRequest(Protocol, ContentFingerprintBuilder.FromCanonicalBytes("other"u8)),
                currentTick: 1,
                out SessionHandshakeResponse contentReject),
            Is.False);
        Assert.That(contentReject.RejectReason, Is.EqualTo(HandshakeRejectReason.ContentMismatch));
    }

    [Test]
    public void FullSession_RejectsAdditionalJoin()
    {
        var registry = CreateRegistry(seatCapacity: 1);
        Assert.That(registry.TryHandshake(new ConnectionId(1), JoinRequest(), currentTick: 1, out _), Is.True);

        Assert.That(registry.TryHandshake(new ConnectionId(2), JoinRequest(), currentTick: 2, out SessionHandshakeResponse full), Is.False);
        Assert.That(full.RejectReason, Is.EqualTo(HandshakeRejectReason.SessionFull));
    }

    [Test]
    public void Reconnect_PreservesPlayerIdentity_AndRotatesToken()
    {
        var registry = CreateRegistry(seatCapacity: 2, reconnectWindowTicks: 30);
        Assert.That(registry.TryHandshake(new ConnectionId(1), JoinRequest(), currentTick: 10, out SessionHandshakeResponse join), Is.True);
        ReconnectToken originalToken = join.ReconnectToken;
        PlayerId originalPlayer = join.PlayerId;

        Assert.That(originalToken.IsEmpty, Is.False);
        Assert.That(join.SessionEpoch, Is.EqualTo(Epoch));

        Assert.That(registry.TryDisconnect(new ConnectionId(1), currentTick: 11), Is.True);
        Assert.That(registry.TryGetPlayerId(new ConnectionId(1), out _), Is.False);

        Assert.That(
            registry.TryHandshake(
                new ConnectionId(99),
                ReconnectRequest(originalToken, join.SessionEpoch),
                currentTick: 20,
                out SessionHandshakeResponse reconnect),
            Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(reconnect.PlayerId, Is.EqualTo(originalPlayer));
            Assert.That(reconnect.Seat, Is.EqualTo(join.Seat));
            Assert.That(reconnect.ReconnectToken, Is.Not.EqualTo(originalToken));
            Assert.That(reconnect.ReconnectToken.IsEmpty, Is.False);
            Assert.That(reconnect.SessionEpoch, Is.EqualTo(Epoch));
            Assert.That(registry.TryGetPlayerId(new ConnectionId(99), out PlayerId rebound), Is.True);
            Assert.That(rebound, Is.EqualTo(originalPlayer));
        });

        Assert.That(
            registry.TryHandshake(
                new ConnectionId(100),
                ReconnectRequest(originalToken, join.SessionEpoch),
                currentTick: 21,
                out SessionHandshakeResponse staleReplay),
            Is.False);
        Assert.That(staleReplay.RejectReason, Is.EqualTo(HandshakeRejectReason.StaleOrInvalidReconnectToken));
    }

    [Test]
    public void SuccessfulReconnect_RotatesToken_AndRejectsPriorToken()
    {
        var registry = CreateRegistry(seatCapacity: 1, reconnectWindowTicks: 50);
        Assert.That(registry.TryHandshake(new ConnectionId(1), JoinRequest(), currentTick: 1, out SessionHandshakeResponse join), Is.True);
        Assert.That(registry.TryDisconnect(new ConnectionId(1), currentTick: 2), Is.True);

        Assert.That(
            registry.TryHandshake(
                new ConnectionId(2),
                ReconnectRequest(join.ReconnectToken, join.SessionEpoch),
                currentTick: 3,
                out SessionHandshakeResponse firstReconnect),
            Is.True);
        Assert.That(firstReconnect.ReconnectToken, Is.Not.EqualTo(join.ReconnectToken));

        Assert.That(registry.TryDisconnect(new ConnectionId(2), currentTick: 4), Is.True);
        Assert.That(
            registry.TryHandshake(
                new ConnectionId(3),
                ReconnectRequest(join.ReconnectToken, join.SessionEpoch),
                currentTick: 5,
                out SessionHandshakeResponse priorTokenReject),
            Is.False);
        Assert.That(priorTokenReject.RejectReason, Is.EqualTo(HandshakeRejectReason.StaleOrInvalidReconnectToken));

        Assert.That(
            registry.TryHandshake(
                new ConnectionId(3),
                ReconnectRequest(firstReconnect.ReconnectToken, firstReconnect.SessionEpoch),
                currentTick: 5,
                out SessionHandshakeResponse secondReconnect),
            Is.True);
        Assert.That(secondReconnect.PlayerId, Is.EqualTo(join.PlayerId));
        Assert.That(secondReconnect.ReconnectToken, Is.Not.EqualTo(firstReconnect.ReconnectToken));
    }

    [Test]
    public void ExpiredReconnect_RejectsAndFreesSeat()
    {
        var registry = CreateRegistry(seatCapacity: 1, reconnectWindowTicks: 5);
        Assert.That(registry.TryHandshake(new ConnectionId(1), JoinRequest(), currentTick: 1, out SessionHandshakeResponse join), Is.True);
        Assert.That(registry.TryDisconnect(new ConnectionId(1), currentTick: 2), Is.True);

        Span<SessionSeatBinding> expiredSeats = stackalloc SessionSeatBinding[1];
        Assert.That(registry.TryExpireAwaitingSeats(8, expiredSeats, out int expiredCount), Is.True);
        Assert.That(expiredCount, Is.EqualTo(1));
        Assert.That(expiredSeats[0], Is.EqualTo(join.Seat));

        Assert.That(
            registry.TryHandshake(
                new ConnectionId(2),
                ReconnectRequest(join.ReconnectToken, join.SessionEpoch),
                currentTick: 8,
                out SessionHandshakeResponse expired),
            Is.False);
        Assert.That(expired.RejectReason, Is.EqualTo(HandshakeRejectReason.StaleOrInvalidReconnectToken));

        Assert.That(registry.TryHandshake(new ConnectionId(3), JoinRequest(), currentTick: 9, out SessionHandshakeResponse rejoin), Is.True);
        Assert.That(rejoin.Accepted, Is.True);
    }

    [Test]
    public void CrossEpochStaleToken_RejectsWithSessionEpochMismatch_BeforeTokenLookup()
    {
        var priorRegistry = CreateRegistry(seatCapacity: 1, sessionEpoch: new SessionEpoch(11));
        Assert.That(priorRegistry.TryHandshake(new ConnectionId(1), JoinRequest(), currentTick: 1, out SessionHandshakeResponse priorJoin), Is.True);
        Assert.That(priorRegistry.TryDisconnect(new ConnectionId(1), currentTick: 2), Is.True);

        var liveRegistry = CreateRegistry(seatCapacity: 1, sessionEpoch: new SessionEpoch(22));
        Assert.That(liveRegistry.TryHandshake(new ConnectionId(7), JoinRequest(), currentTick: 3, out _), Is.True);
        Assert.That(liveRegistry.TryDisconnect(new ConnectionId(7), currentTick: 4), Is.True);

        Assert.That(
            liveRegistry.TryHandshake(
                new ConnectionId(8),
                ReconnectRequest(priorJoin.ReconnectToken, priorJoin.SessionEpoch),
                currentTick: 5,
                out SessionHandshakeResponse epochReject),
            Is.False);
        Assert.That(epochReject.RejectReason, Is.EqualTo(HandshakeRejectReason.SessionEpochMismatch));
        Assert.That(epochReject.SessionEpoch, Is.EqualTo(new SessionEpoch(22)));
    }

    [Test]
    public void EmptyContentFingerprint_IsRejectedByConstructor()
    {
        Assert.That(
            () => new AuthoritativeSessionRegistry(2, Epoch, Protocol, ContentFingerprint.Empty, 30),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("requiredContentFingerprint"));
    }

    [Test]
    public void EmptySessionEpoch_IsRejectedByConstructor()
    {
        Assert.That(
            () => new AuthoritativeSessionRegistry(2, SessionEpoch.Empty, Protocol, Content, 30),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("sessionEpoch"));
    }

    [Test]
    public void TickWrap_AllowsReconnectWithinConfiguredWindow()
    {
        var registry = CreateRegistry(seatCapacity: 1, reconnectWindowTicks: 30);
        Assert.That(registry.TryHandshake(new ConnectionId(1), JoinRequest(), currentTick: uint.MaxValue - 5, out SessionHandshakeResponse join), Is.True);
        Assert.That(registry.TryDisconnect(new ConnectionId(1), currentTick: uint.MaxValue - 2), Is.True);

        Assert.That(
            registry.TryHandshake(
                new ConnectionId(2),
                ReconnectRequest(join.ReconnectToken, join.SessionEpoch),
                currentTick: 10,
                out SessionHandshakeResponse reconnect),
            Is.True);
        Assert.That(reconnect.PlayerId, Is.EqualTo(join.PlayerId));
        Assert.That(reconnect.SessionEpoch, Is.EqualTo(Epoch));
    }

    [Test]
    public void TickWrap_RejectsReconnectAfterConfiguredWindow()
    {
        var registry = CreateRegistry(seatCapacity: 1, reconnectWindowTicks: 5);
        Assert.That(registry.TryHandshake(new ConnectionId(1), JoinRequest(), currentTick: uint.MaxValue - 2, out SessionHandshakeResponse join), Is.True);
        Assert.That(registry.TryDisconnect(new ConnectionId(1), currentTick: uint.MaxValue - 1), Is.True);

        Span<SessionSeatBinding> expiredSeats = stackalloc SessionSeatBinding[1];
        Assert.That(registry.TryExpireAwaitingSeats(5, expiredSeats, out int expiredCount), Is.True);
        Assert.That(expiredCount, Is.EqualTo(1));
        Assert.That(expiredSeats[0], Is.EqualTo(join.Seat));

        Assert.That(
            registry.TryHandshake(
                new ConnectionId(2),
                ReconnectRequest(join.ReconnectToken, join.SessionEpoch),
                currentTick: 5,
                out SessionHandshakeResponse expired),
            Is.False);
        Assert.That(expired.RejectReason, Is.EqualTo(HandshakeRejectReason.StaleOrInvalidReconnectToken));

        Assert.That(registry.TryHandshake(new ConnectionId(3), JoinRequest(), currentTick: 6, out _), Is.True);
    }

    [Test]
    public void ExpirationOutputCapacity_IsAtomicAndReportsRequiredCount()
    {
        var registry = CreateRegistry(seatCapacity: 2, reconnectWindowTicks: 5);
        Assert.That(registry.TryHandshake(new ConnectionId(1), JoinRequest(), currentTick: 1, out SessionHandshakeResponse first), Is.True);
        Assert.That(registry.TryHandshake(new ConnectionId(2), JoinRequest(), currentTick: 1, out SessionHandshakeResponse second), Is.True);
        Assert.That(registry.TryDisconnect(new ConnectionId(1), currentTick: 2), Is.True);
        Assert.That(registry.TryDisconnect(new ConnectionId(2), currentTick: 2), Is.True);

        Span<SessionSeatBinding> tooSmall = stackalloc SessionSeatBinding[1];
        Assert.That(registry.TryExpireAwaitingSeats(8, tooSmall, out int required), Is.False);
        Assert.That(required, Is.EqualTo(2));

        Span<SessionSeatBinding> released = stackalloc SessionSeatBinding[2];
        Assert.That(registry.TryExpireAwaitingSeats(8, released, out int releasedCount), Is.True);
        Assert.That(releasedCount, Is.EqualTo(2));
        Assert.That(released[0], Is.EqualTo(first.Seat));
        Assert.That(released[1], Is.EqualTo(second.Seat));
    }

    [Test]
    public void SteadyState_DisconnectReconnectLookup_AllocatesZeroBytesAfterWarmup()
    {
        var registry = CreateRegistry(seatCapacity: 2, reconnectWindowTicks: 100);
        var connectionA = new ConnectionId(1);
        var connectionB = new ConnectionId(2);

        Assert.That(registry.TryHandshake(connectionA, JoinRequest(), currentTick: 1, out SessionHandshakeResponse joinA), Is.True);
        Assert.That(registry.TryHandshake(connectionB, JoinRequest(), currentTick: 1, out _), Is.True);

        // Warm up JIT and any one-time paths.
        for (uint tick = 2; tick < 32; tick++)
        {
            Assert.That(registry.TryDisconnect(connectionA, tick), Is.True);
            Assert.That(
                registry.TryHandshake(
                    connectionA,
                    ReconnectRequest(joinA.ReconnectToken, joinA.SessionEpoch),
                    tick,
                    out joinA),
                Is.True);
            Assert.That(registry.TryGetPlayerId(connectionA, out _), Is.True);
            Assert.That(registry.TryGetPlayerId(connectionB, out _), Is.True);
        }

        bool ok = true;
        GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (uint tick = 100; tick < 1100; tick++)
        {
            ok &= registry.TryDisconnect(connectionA, tick);
            ok &= registry.TryHandshake(
                connectionA,
                ReconnectRequest(joinA.ReconnectToken, joinA.SessionEpoch),
                tick,
                out joinA);
            ok &= registry.TryGetPlayerId(connectionA, out _);
            ok &= registry.TryGetPlayerId(connectionB, out _);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(ok, Is.True);
        Assert.That(allocated, Is.EqualTo(0), $"Expected 0 B allocation, observed {allocated} B.");
    }

    [Test]
    public void ExhaustedSeatGeneration_RetiresSeat_AndAdmissionMovesOrReportsSessionFull()
    {
        var registry = CreateRegistry(seatCapacity: 2, reconnectWindowTicks: 5);
        Assert.That(registry.TryHandshake(new ConnectionId(1), JoinRequest(), currentTick: 1, out SessionHandshakeResponse first), Is.True);
        Assert.That(first.Seat.Slot, Is.EqualTo(0));
        Assert.That(first.Seat.Generation, Is.EqualTo(1u));

        SeedSeatGeneration(registry, seat: 0, generation: uint.MaxValue);
        Assert.That(registry.TryDisconnect(new ConnectionId(1), currentTick: 2), Is.True);
        Span<SessionSeatBinding> expired = stackalloc SessionSeatBinding[2];
        Assert.That(registry.TryExpireAwaitingSeats(8, expired, out int expiredCount), Is.True);
        Assert.That(expiredCount, Is.EqualTo(1));
        Assert.That(expired[0].Generation, Is.EqualTo(uint.MaxValue));

        // Retired seat 0 is skipped; seat 1 is still usable.
        Assert.That(registry.TryHandshake(new ConnectionId(2), JoinRequest(), currentTick: 9, out SessionHandshakeResponse second), Is.True);
        Assert.That(second.Seat.Slot, Is.EqualTo(1));
        Assert.That(second.Seat.Generation, Is.EqualTo(1u));

        SeedSeatGeneration(registry, seat: 1, generation: uint.MaxValue);
        Assert.That(registry.TryDisconnect(new ConnectionId(2), currentTick: 10), Is.True);
        Assert.That(registry.TryExpireAwaitingSeats(16, expired, out expiredCount), Is.True);
        Assert.That(expiredCount, Is.EqualTo(1));

        Assert.That(
            registry.TryHandshake(new ConnectionId(3), JoinRequest(), currentTick: 17, out SessionHandshakeResponse full),
            Is.False);
        Assert.That(full.RejectReason, Is.EqualTo(HandshakeRejectReason.SessionFull));
    }

    [Test]
    public void NextGeneration_NeverWrapsMaxValueToOne()
    {
        MethodInfo? nextGeneration = typeof(AuthoritativeSessionRegistry).GetMethod(
            "NextGeneration",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(nextGeneration, Is.Not.Null);
        TargetInvocationException? ex = Assert.Throws<TargetInvocationException>(() =>
            nextGeneration!.Invoke(null, new object[] { uint.MaxValue }));
        Assert.That(ex!.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(nextGeneration!.Invoke(null, new object[] { uint.MaxValue - 1u }), Is.EqualTo(uint.MaxValue));
    }

    private static void SeedSeatGeneration(AuthoritativeSessionRegistry registry, int seat, uint generation)
    {
        FieldInfo generations = typeof(AuthoritativeSessionRegistry).GetField(
            "_seatGenerations",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing _seatGenerations field.");
        var values = (uint[])generations.GetValue(registry)!;
        values[seat] = generation;
    }

    private static AuthoritativeSessionRegistry CreateRegistry(
        int seatCapacity,
        uint reconnectWindowTicks = 30,
        SessionEpoch? sessionEpoch = null) =>
        new(seatCapacity, sessionEpoch ?? Epoch, Protocol, Content, reconnectWindowTicks);

    private static SessionHandshakeRequest JoinRequest() => new(Protocol, Content);

    private static SessionHandshakeRequest ReconnectRequest(ReconnectToken token, SessionEpoch epoch) =>
        new(Protocol, Content, token, epoch);
}
