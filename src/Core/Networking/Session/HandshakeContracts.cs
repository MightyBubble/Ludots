using System;

namespace Ludots.Core.Networking.Session
{
    public readonly struct ProtocolVersion : IEquatable<ProtocolVersion>
    {
        public ProtocolVersion(ushort major, ushort minor)
        {
            Major = major;
            Minor = minor;
        }

        public ushort Major { get; }

        public ushort Minor { get; }

        public bool IsWellFormed => Major > 0;

        public bool Equals(ProtocolVersion other) => Major == other.Major && Minor == other.Minor;

        public override bool Equals(object? obj) => obj is ProtocolVersion other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Major, Minor);

        public static bool operator ==(ProtocolVersion left, ProtocolVersion right) => left.Equals(right);

        public static bool operator !=(ProtocolVersion left, ProtocolVersion right) => !left.Equals(right);

        public override string ToString() => $"{Major}.{Minor}";
    }

    /// <summary>
    /// Server session generation. Empty is allowed on initial join requests only.
    /// </summary>
    public readonly struct SessionEpoch : IEquatable<SessionEpoch>
    {
        public static SessionEpoch Empty => default;

        public SessionEpoch(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }

        public bool IsEmpty => Value == 0;

        public bool Equals(SessionEpoch other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is SessionEpoch other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public static bool operator ==(SessionEpoch left, SessionEpoch right) => left.Equals(right);

        public static bool operator !=(SessionEpoch left, SessionEpoch right) => !left.Equals(right);

        public override string ToString() => Value.ToString();
    }

    /// <summary>
    /// Server-derived player seat identity. Never accepted from client handshake payloads.
    /// </summary>
    public readonly struct PlayerId : IEquatable<PlayerId>
    {
        public PlayerId(int value)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Player id must be positive.");
            }

            Value = value;
        }

        public int Value { get; }

        public bool Equals(PlayerId other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is PlayerId other && Equals(other);

        public override int GetHashCode() => Value;

        public static bool operator ==(PlayerId left, PlayerId right) => left.Equals(right);

        public static bool operator !=(PlayerId left, PlayerId right) => !left.Equals(right);
    }

    public readonly struct ReconnectToken : IEquatable<ReconnectToken>
    {
        public static ReconnectToken Empty => default;

        public ReconnectToken(ulong low, ulong high)
        {
            Low = low;
            High = high;
        }

        public ulong Low { get; }

        public ulong High { get; }

        public bool IsEmpty => Low == 0 && High == 0;

        public bool Equals(ReconnectToken other) => Low == other.Low && High == other.High;

        public override bool Equals(object? obj) => obj is ReconnectToken other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Low, High);

        public static bool operator ==(ReconnectToken left, ReconnectToken right) => left.Equals(right);

        public static bool operator !=(ReconnectToken left, ReconnectToken right) => !left.Equals(right);
    }

    public enum HandshakeRejectReason : byte
    {
        None = 0,
        ProtocolMismatch = 1,
        ContentMismatch = 2,
        SessionFull = 3,
        StaleOrInvalidReconnectToken = 4,
        MalformedRequest = 5,
        SessionEpochMismatch = 6,
        MatchAlreadyStarted = 7,
    }

    /// <summary>
    /// Client handshake payload. Intentionally omits player identity; seats are server-assigned.
    /// </summary>
    public readonly struct SessionHandshakeRequest
    {
        public SessionHandshakeRequest(
            ProtocolVersion protocolVersion,
            ContentFingerprint contentFingerprint,
            ReconnectToken reconnectToken = default,
            SessionEpoch sessionEpoch = default)
        {
            ProtocolVersion = protocolVersion;
            ContentFingerprint = contentFingerprint;
            ReconnectToken = reconnectToken;
            SessionEpoch = sessionEpoch;
        }

        public ProtocolVersion ProtocolVersion { get; }

        public ContentFingerprint ContentFingerprint { get; }

        public ReconnectToken ReconnectToken { get; }

        public SessionEpoch SessionEpoch { get; }

        public bool IsWellFormed => ProtocolVersion.IsWellFormed;
    }

    public readonly struct SessionHandshakeResponse
    {
        private SessionHandshakeResponse(
            bool accepted,
            HandshakeRejectReason rejectReason,
            in SessionSeatBinding seat,
            ReconnectToken reconnectToken,
            ProtocolVersion protocolVersion,
            ContentFingerprint contentFingerprint,
            SessionEpoch sessionEpoch,
            ulong nextClientBatchSequence)
        {
            Accepted = accepted;
            RejectReason = rejectReason;
            Seat = seat;
            ReconnectToken = reconnectToken;
            ProtocolVersion = protocolVersion;
            ContentFingerprint = contentFingerprint;
            SessionEpoch = sessionEpoch;
            NextClientBatchSequence = nextClientBatchSequence;
        }

        public bool Accepted { get; }

        public HandshakeRejectReason RejectReason { get; }

        public SessionSeatBinding Seat { get; }

        public PlayerId PlayerId => Seat.PlayerId;

        public ReconnectToken ReconnectToken { get; }

        public ProtocolVersion ProtocolVersion { get; }

        public ContentFingerprint ContentFingerprint { get; }

        public SessionEpoch SessionEpoch { get; }

        public ulong NextClientBatchSequence { get; }

        public static SessionHandshakeResponse Accept(
            in SessionSeatBinding seat,
            ReconnectToken reconnectToken,
            ProtocolVersion protocolVersion,
            ContentFingerprint contentFingerprint,
            SessionEpoch sessionEpoch,
            ulong nextClientBatchSequence)
        {
            if (!seat.IsValid)
            {
                throw new ArgumentException("Accepted handshake requires a valid authoritative seat.", nameof(seat));
            }

            if (reconnectToken.IsEmpty)
            {
                throw new ArgumentException("Accepted handshake must issue a non-empty reconnect token.", nameof(reconnectToken));
            }

            if (sessionEpoch.IsEmpty)
            {
                throw new ArgumentException("Accepted handshake must include a non-empty session epoch.", nameof(sessionEpoch));
            }

            if (nextClientBatchSequence == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(nextClientBatchSequence),
                    "Accepted handshake must include the authoritative next client command sequence.");
            }

            return new SessionHandshakeResponse(
                accepted: true,
                HandshakeRejectReason.None,
                in seat,
                reconnectToken,
                protocolVersion,
                contentFingerprint,
                sessionEpoch,
                nextClientBatchSequence);
        }

        public static SessionHandshakeResponse Reject(
            HandshakeRejectReason reason,
            ProtocolVersion protocolVersion,
            ContentFingerprint contentFingerprint,
            SessionEpoch sessionEpoch)
        {
            if (reason == HandshakeRejectReason.None)
            {
                throw new ArgumentOutOfRangeException(nameof(reason), "Reject reason must be explicit.");
            }

            SessionSeatBinding seat = default;
            return new SessionHandshakeResponse(
                accepted: false,
                reason,
                in seat,
                ReconnectToken.Empty,
                protocolVersion,
                contentFingerprint,
                sessionEpoch,
                nextClientBatchSequence: 0);
        }
    }

    /// <summary>
    /// Commits a prepared handshake only after the client has durably stored the candidate token.
    /// Player identity remains server-derived and is intentionally absent.
    /// </summary>
    public readonly struct SessionHandshakeConfirmation
    {
        public SessionHandshakeConfirmation(
            SessionEpoch sessionEpoch,
            int seatSlot,
            uint seatGeneration,
            ReconnectToken reconnectToken)
        {
            SessionEpoch = sessionEpoch;
            SeatSlot = seatSlot;
            SeatGeneration = seatGeneration;
            ReconnectToken = reconnectToken;
        }

        public SessionEpoch SessionEpoch { get; }

        public int SeatSlot { get; }

        public uint SeatGeneration { get; }

        public ReconnectToken ReconnectToken { get; }

        public bool IsWellFormed =>
            !SessionEpoch.IsEmpty &&
            SeatSlot >= 0 &&
            SeatGeneration != 0 &&
            !ReconnectToken.IsEmpty;
    }

    public readonly struct SessionSeatBinding : IEquatable<SessionSeatBinding>
    {
        public SessionSeatBinding(int slot, uint generation, PlayerId playerId)
        {
            Slot = slot;
            Generation = generation;
            PlayerId = playerId;
        }

        public int Slot { get; }

        public uint Generation { get; }

        public PlayerId PlayerId { get; }

        public bool IsValid => Slot >= 0 && Generation != 0 && PlayerId.Value > 0;

        public bool Equals(SessionSeatBinding other) =>
            Slot == other.Slot &&
            Generation == other.Generation &&
            PlayerId == other.PlayerId;

        public override bool Equals(object? obj) => obj is SessionSeatBinding other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Slot, Generation, PlayerId);

        public static bool operator ==(SessionSeatBinding left, SessionSeatBinding right) => left.Equals(right);

        public static bool operator !=(SessionSeatBinding left, SessionSeatBinding right) => !left.Equals(right);
    }
}
