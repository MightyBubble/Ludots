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
    /// Fixed 8-slot category digest table for handshake wire payloads (value-type, no heap).
    /// </summary>
    public readonly struct ContentCategoryDigestTable : IEquatable<ContentCategoryDigestTable>
    {
        private readonly ContentFingerprint _c0;
        private readonly ContentFingerprint _c1;
        private readonly ContentFingerprint _c2;
        private readonly ContentFingerprint _c3;
        private readonly ContentFingerprint _c4;
        private readonly ContentFingerprint _c5;
        private readonly ContentFingerprint _c6;
        private readonly ContentFingerprint _c7;

        public ContentCategoryDigestTable(ReadOnlySpan<ContentFingerprint> digests)
        {
            if (digests.Length != ContentIdentityManifest.CategoryCount)
            {
                throw new ArgumentException(
                    $"Category digest table requires exactly {ContentIdentityManifest.CategoryCount} digests.",
                    nameof(digests));
            }

            for (int i = 0; i < digests.Length; i++)
            {
                if (digests[i].IsEmpty)
                {
                    throw new ArgumentException("Category digests must be non-empty.", nameof(digests));
                }
            }

            _c0 = digests[0];
            _c1 = digests[1];
            _c2 = digests[2];
            _c3 = digests[3];
            _c4 = digests[4];
            _c5 = digests[5];
            _c6 = digests[6];
            _c7 = digests[7];
        }

        public bool IsEmpty =>
            _c0.IsEmpty && _c1.IsEmpty && _c2.IsEmpty && _c3.IsEmpty &&
            _c4.IsEmpty && _c5.IsEmpty && _c6.IsEmpty && _c7.IsEmpty;

        public ContentFingerprint this[int index] => index switch
        {
            0 => _c0,
            1 => _c1,
            2 => _c2,
            3 => _c3,
            4 => _c4,
            5 => _c5,
            6 => _c6,
            7 => _c7,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        public void CopyTo(Span<ContentFingerprint> destination)
        {
            if (destination.Length < ContentIdentityManifest.CategoryCount)
            {
                throw new ArgumentException("Destination is too small for category digests.", nameof(destination));
            }

            destination[0] = _c0;
            destination[1] = _c1;
            destination[2] = _c2;
            destination[3] = _c3;
            destination[4] = _c4;
            destination[5] = _c5;
            destination[6] = _c6;
            destination[7] = _c7;
        }

        public bool Equals(ContentCategoryDigestTable other) =>
            _c0 == other._c0 &&
            _c1 == other._c1 &&
            _c2 == other._c2 &&
            _c3 == other._c3 &&
            _c4 == other._c4 &&
            _c5 == other._c5 &&
            _c6 == other._c6 &&
            _c7 == other._c7;

        public override bool Equals(object? obj) => obj is ContentCategoryDigestTable other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                HashCode.Combine(_c0, _c1, _c2, _c3),
                HashCode.Combine(_c4, _c5, _c6, _c7));
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
            SessionEpoch sessionEpoch = default,
            ContentCategoryDigestTable categoryDigests = default)
        {
            ProtocolVersion = protocolVersion;
            ContentFingerprint = contentFingerprint;
            ReconnectToken = reconnectToken;
            SessionEpoch = sessionEpoch;
            CategoryDigests = categoryDigests;
        }

        public ProtocolVersion ProtocolVersion { get; }

        public ContentFingerprint ContentFingerprint { get; }

        public ReconnectToken ReconnectToken { get; }

        public SessionEpoch SessionEpoch { get; }

        /// <summary>
        /// Optional per-category digests. Empty means aggregate-only comparison on the server.
        /// </summary>
        public ContentCategoryDigestTable CategoryDigests { get; }

        public bool IsWellFormed
        {
            get
            {
                if (!ProtocolVersion.IsWellFormed)
                {
                    return false;
                }

                if (CategoryDigests.IsEmpty)
                {
                    return true;
                }

                for (int i = 0; i < ContentIdentityManifest.CategoryCount; i++)
                {
                    if (CategoryDigests[i].IsEmpty)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
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
            ContentMismatchDetail mismatchDetail)
        {
            Accepted = accepted;
            RejectReason = rejectReason;
            Seat = seat;
            ReconnectToken = reconnectToken;
            ProtocolVersion = protocolVersion;
            ContentFingerprint = contentFingerprint;
            SessionEpoch = sessionEpoch;
            MismatchDetail = mismatchDetail;
        }

        public bool Accepted { get; }

        public HandshakeRejectReason RejectReason { get; }

        public SessionSeatBinding Seat { get; }

        public PlayerId PlayerId => Seat.PlayerId;

        public ReconnectToken ReconnectToken { get; }

        public ProtocolVersion ProtocolVersion { get; }

        public ContentFingerprint ContentFingerprint { get; }

        public SessionEpoch SessionEpoch { get; }

        /// <summary>
        /// Populated when <see cref="RejectReason"/> is <see cref="HandshakeRejectReason.ContentMismatch"/>.
        /// </summary>
        public ContentMismatchDetail MismatchDetail { get; }

        public static SessionHandshakeResponse Accept(
            in SessionSeatBinding seat,
            ReconnectToken reconnectToken,
            ProtocolVersion protocolVersion,
            ContentFingerprint contentFingerprint,
            SessionEpoch sessionEpoch)
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

            return new SessionHandshakeResponse(
                accepted: true,
                HandshakeRejectReason.None,
                in seat,
                reconnectToken,
                protocolVersion,
                contentFingerprint,
                sessionEpoch,
                default);
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
                default);
        }

        public static SessionHandshakeResponse Reject(
            HandshakeRejectReason reason,
            ProtocolVersion protocolVersion,
            ContentFingerprint contentFingerprint,
            SessionEpoch sessionEpoch,
            ContentMismatchDetail mismatchDetail)
        {
            if (reason != HandshakeRejectReason.ContentMismatch)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(reason),
                    "Mismatch detail requires ContentMismatch reject reason.");
            }

            if (mismatchDetail.Category == ContentIdentityCategory.None)
            {
                throw new ArgumentException(
                    "Content mismatch detail requires a non-None category.",
                    nameof(mismatchDetail));
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
                mismatchDetail);
        }
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
