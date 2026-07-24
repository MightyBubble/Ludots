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
            PlayerId playerId,
            ReconnectToken reconnectToken,
            ProtocolVersion protocolVersion,
            ContentFingerprint contentFingerprint,
            SessionEpoch sessionEpoch)
        {
            Accepted = accepted;
            RejectReason = rejectReason;
            PlayerId = playerId;
            ReconnectToken = reconnectToken;
            ProtocolVersion = protocolVersion;
            ContentFingerprint = contentFingerprint;
            SessionEpoch = sessionEpoch;
        }

        public bool Accepted { get; }

        public HandshakeRejectReason RejectReason { get; }

        public PlayerId PlayerId { get; }

        public ReconnectToken ReconnectToken { get; }

        public ProtocolVersion ProtocolVersion { get; }

        public ContentFingerprint ContentFingerprint { get; }

        public SessionEpoch SessionEpoch { get; }

        public static SessionHandshakeResponse Accept(
            PlayerId playerId,
            ReconnectToken reconnectToken,
            ProtocolVersion protocolVersion,
            ContentFingerprint contentFingerprint,
            SessionEpoch sessionEpoch)
        {
            if (reconnectToken.IsEmpty)
            {
                throw new ArgumentException("Accepted handshake must issue a non-empty reconnect token.", nameof(reconnectToken));
            }

            if (sessionEpoch.IsEmpty)
            {
                throw new ArgumentException("Accepted handshake must include a non-empty session epoch.", nameof(sessionEpoch));
            }

            return new SessionHandshakeResponse(
                accepted: true,
                HandshakeRejectReason.None,
                playerId,
                reconnectToken,
                protocolVersion,
                contentFingerprint,
                sessionEpoch);
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

            return new SessionHandshakeResponse(
                accepted: false,
                reason,
                playerId: default,
                ReconnectToken.Empty,
                protocolVersion,
                contentFingerprint,
                sessionEpoch);
        }
    }
}
