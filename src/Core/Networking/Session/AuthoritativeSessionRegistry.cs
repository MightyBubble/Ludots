using System;
using System.Security.Cryptography;
using Ludots.Core.Networking.Transport;

namespace Ludots.Core.Networking.Session
{
    /// <summary>
    /// Fixed-capacity authoritative seat table. Connection-to-player binding is server-derived.
    /// Steady-state paths use preallocated arrays only (no Dictionary/List growth).
    /// </summary>
    public sealed class AuthoritativeSessionRegistry
    {
        private const int TokenIssueMaxAttempts = 16;

        private enum SeatState : byte
        {
            Empty = 0,
            Connected = 1,
            AwaitingReconnect = 2,
        }

        private readonly SessionEpoch _sessionEpoch;
        private readonly ProtocolVersion _requiredProtocolVersion;
        private readonly ContentFingerprint _requiredContentFingerprint;
        private readonly uint _reconnectWindowTicks;

        private readonly SeatState[] _states;
        private readonly int[] _connectionValues;
        private readonly int[] _playerValues;
        private readonly ulong[] _tokenLow;
        private readonly ulong[] _tokenHigh;
        private readonly uint[] _disconnectTicks;

        public AuthoritativeSessionRegistry(
            int seatCapacity,
            SessionEpoch sessionEpoch,
            ProtocolVersion requiredProtocolVersion,
            ContentFingerprint requiredContentFingerprint,
            uint reconnectWindowTicks)
        {
            if (seatCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seatCapacity), "Seat capacity must be positive.");
            }

            if (sessionEpoch.IsEmpty)
            {
                throw new ArgumentException("Session epoch must be non-empty.", nameof(sessionEpoch));
            }

            if (!requiredProtocolVersion.IsWellFormed)
            {
                throw new ArgumentException("Required protocol version must be well-formed.", nameof(requiredProtocolVersion));
            }

            if (requiredContentFingerprint.IsEmpty)
            {
                throw new ArgumentException("Required content fingerprint must be non-empty.", nameof(requiredContentFingerprint));
            }

            _sessionEpoch = sessionEpoch;
            _requiredProtocolVersion = requiredProtocolVersion;
            _requiredContentFingerprint = requiredContentFingerprint;
            _reconnectWindowTicks = reconnectWindowTicks;

            _states = new SeatState[seatCapacity];
            _connectionValues = new int[seatCapacity];
            _playerValues = new int[seatCapacity];
            _tokenLow = new ulong[seatCapacity];
            _tokenHigh = new ulong[seatCapacity];
            _disconnectTicks = new uint[seatCapacity];

            for (int i = 0; i < seatCapacity; i++)
            {
                _playerValues[i] = i + 1;
            }
        }

        public int SeatCapacity => _states.Length;

        public SessionEpoch SessionEpoch => _sessionEpoch;

        public ProtocolVersion RequiredProtocolVersion => _requiredProtocolVersion;

        public ContentFingerprint RequiredContentFingerprint => _requiredContentFingerprint;

        public uint ReconnectWindowTicks => _reconnectWindowTicks;

        public bool TryHandshake(
            ConnectionId connectionId,
            in SessionHandshakeRequest request,
            uint currentTick,
            out SessionHandshakeResponse response)
        {
            ExpireAwaitingSeats(currentTick);

            if (!request.IsWellFormed)
            {
                response = Reject(HandshakeRejectReason.MalformedRequest);
                return false;
            }

            if (request.ProtocolVersion != _requiredProtocolVersion)
            {
                response = Reject(HandshakeRejectReason.ProtocolMismatch);
                return false;
            }

            if (request.ContentFingerprint != _requiredContentFingerprint)
            {
                response = Reject(HandshakeRejectReason.ContentMismatch);
                return false;
            }

            if (FindConnectedSeat(connectionId.Value, out _))
            {
                response = Reject(HandshakeRejectReason.MalformedRequest);
                return false;
            }

            if (request.ReconnectToken.IsEmpty)
            {
                if (!request.SessionEpoch.IsEmpty && request.SessionEpoch != _sessionEpoch)
                {
                    response = Reject(HandshakeRejectReason.SessionEpochMismatch);
                    return false;
                }

                return TryInitialJoin(connectionId, out response);
            }

            if (request.SessionEpoch != _sessionEpoch)
            {
                response = Reject(HandshakeRejectReason.SessionEpochMismatch);
                return false;
            }

            return TryReconnect(connectionId, request.ReconnectToken, currentTick, out response);
        }

        public bool TryDisconnect(ConnectionId connectionId, uint currentTick)
        {
            ExpireAwaitingSeats(currentTick);

            if (!FindConnectedSeat(connectionId.Value, out int seat))
            {
                return false;
            }

            _states[seat] = SeatState.AwaitingReconnect;
            _connectionValues[seat] = 0;
            _disconnectTicks[seat] = currentTick;
            return true;
        }

        public bool TryGetPlayerId(ConnectionId connectionId, out PlayerId playerId)
        {
            if (!FindConnectedSeat(connectionId.Value, out int seat))
            {
                playerId = default;
                return false;
            }

            playerId = new PlayerId(_playerValues[seat]);
            return true;
        }

        public void ExpireAwaitingSeats(uint currentTick)
        {
            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i] != SeatState.AwaitingReconnect)
                {
                    continue;
                }

                if (IsReconnectExpired(i, currentTick))
                {
                    ClearSeat(i);
                }
            }
        }

        private bool TryInitialJoin(ConnectionId connectionId, out SessionHandshakeResponse response)
        {
            if (!TryFindEmptySeat(out int seat))
            {
                response = Reject(HandshakeRejectReason.SessionFull);
                return false;
            }

            ReconnectToken token = IssueToken();
            _states[seat] = SeatState.Connected;
            _connectionValues[seat] = connectionId.Value;
            _tokenLow[seat] = token.Low;
            _tokenHigh[seat] = token.High;
            _disconnectTicks[seat] = 0;

            response = SessionHandshakeResponse.Accept(
                new PlayerId(_playerValues[seat]),
                token,
                _requiredProtocolVersion,
                _requiredContentFingerprint,
                _sessionEpoch);
            return true;
        }

        private bool TryReconnect(
            ConnectionId connectionId,
            ReconnectToken token,
            uint currentTick,
            out SessionHandshakeResponse response)
        {
            if (!FindSeatByToken(token, out int seat) ||
                _states[seat] != SeatState.AwaitingReconnect ||
                IsReconnectExpired(seat, currentTick))
            {
                if (FindSeatByToken(token, out int staleSeat) &&
                    _states[staleSeat] == SeatState.AwaitingReconnect &&
                    IsReconnectExpired(staleSeat, currentTick))
                {
                    ClearSeat(staleSeat);
                }

                response = Reject(HandshakeRejectReason.StaleOrInvalidReconnectToken);
                return false;
            }

            ReconnectToken rotated = IssueToken();
            _states[seat] = SeatState.Connected;
            _connectionValues[seat] = connectionId.Value;
            _tokenLow[seat] = rotated.Low;
            _tokenHigh[seat] = rotated.High;
            _disconnectTicks[seat] = 0;

            response = SessionHandshakeResponse.Accept(
                new PlayerId(_playerValues[seat]),
                rotated,
                _requiredProtocolVersion,
                _requiredContentFingerprint,
                _sessionEpoch);
            return true;
        }

        private SessionHandshakeResponse Reject(HandshakeRejectReason reason) =>
            SessionHandshakeResponse.Reject(
                reason,
                _requiredProtocolVersion,
                _requiredContentFingerprint,
                _sessionEpoch);

        private bool TryFindEmptySeat(out int seat)
        {
            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i] == SeatState.Empty)
                {
                    seat = i;
                    return true;
                }
            }

            seat = -1;
            return false;
        }

        private bool FindConnectedSeat(int connectionValue, out int seat)
        {
            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i] == SeatState.Connected && _connectionValues[i] == connectionValue)
                {
                    seat = i;
                    return true;
                }
            }

            seat = -1;
            return false;
        }

        private bool FindSeatByToken(ReconnectToken token, out int seat)
        {
            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i] == SeatState.Empty)
                {
                    continue;
                }

                if (_tokenLow[i] == token.Low && _tokenHigh[i] == token.High)
                {
                    seat = i;
                    return true;
                }
            }

            seat = -1;
            return false;
        }

        private bool IsReconnectExpired(int seat, uint currentTick)
        {
            uint elapsed = unchecked(currentTick - _disconnectTicks[seat]);
            return elapsed > _reconnectWindowTicks;
        }

        private void ClearSeat(int seat)
        {
            _states[seat] = SeatState.Empty;
            _connectionValues[seat] = 0;
            _tokenLow[seat] = 0;
            _tokenHigh[seat] = 0;
            _disconnectTicks[seat] = 0;
        }

        private ReconnectToken IssueToken()
        {
            Span<byte> bytes = stackalloc byte[16];
            for (int attempt = 0; attempt < TokenIssueMaxAttempts; attempt++)
            {
                RandomNumberGenerator.Fill(bytes);
                ulong low = BitConverter.ToUInt64(bytes);
                ulong high = BitConverter.ToUInt64(bytes.Slice(8));
                var token = new ReconnectToken(low, high);
                if (token.IsEmpty)
                {
                    continue;
                }

                if (FindSeatByToken(token, out _))
                {
                    continue;
                }

                return token;
            }

            throw new InvalidOperationException(
                $"Failed to issue a unique non-empty reconnect token within {TokenIssueMaxAttempts} attempts.");
        }
    }
}
