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
            HandshakePending = 3,
        }

        private readonly SessionEpoch _sessionEpoch;
        private readonly ProtocolVersion _requiredProtocolVersion;
        private readonly ContentFingerprint _requiredContentFingerprint;
        private readonly uint _reconnectWindowTicks;
        private readonly uint _readyCountdownTicks;

        private readonly SeatState[] _states;
        private readonly int[] _connectionValues;
        private readonly int[] _playerValues;
        private readonly uint[] _seatGenerations;
        private readonly ulong[] _tokenLow;
        private readonly ulong[] _tokenHigh;
        private readonly uint[] _disconnectTicks;
        private readonly bool[] _roomReady;
        private readonly bool[] _pendingReconnect;
        private readonly ulong[] _pendingTokenLow;
        private readonly ulong[] _pendingTokenHigh;
        private readonly uint[] _pendingTicks;

        private NetworkRoomPhase _roomPhase;
        private ulong _roomRevision;
        private uint _roomCommittedTick;
        private uint _roomCountdownStartTick;
        private uint _roomCountdownRemainingTicks;
        private bool _hasRoomCommittedTick;

        public AuthoritativeSessionRegistry(
            int seatCapacity,
            SessionEpoch sessionEpoch,
            ProtocolVersion requiredProtocolVersion,
            ContentFingerprint requiredContentFingerprint,
            uint reconnectWindowTicks,
            uint readyCountdownTicks)
        {
            if (seatCapacity <= 0 || seatCapacity > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(seatCapacity), "Seat capacity must fit the room snapshot wire contract.");
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

            if (readyCountdownTicks == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(readyCountdownTicks), "Ready countdown must be positive.");
            }

            _sessionEpoch = sessionEpoch;
            _requiredProtocolVersion = requiredProtocolVersion;
            _requiredContentFingerprint = requiredContentFingerprint;
            _reconnectWindowTicks = reconnectWindowTicks;
            _readyCountdownTicks = readyCountdownTicks;

            _states = new SeatState[seatCapacity];
            _connectionValues = new int[seatCapacity];
            _playerValues = new int[seatCapacity];
            _seatGenerations = new uint[seatCapacity];
            _tokenLow = new ulong[seatCapacity];
            _tokenHigh = new ulong[seatCapacity];
            _disconnectTicks = new uint[seatCapacity];
            _roomReady = new bool[seatCapacity];
            _pendingReconnect = new bool[seatCapacity];
            _pendingTokenLow = new ulong[seatCapacity];
            _pendingTokenHigh = new ulong[seatCapacity];
            _pendingTicks = new uint[seatCapacity];
            _roomPhase = NetworkRoomPhase.WaitingForPlayers;
            _roomRevision = 1;

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

        public uint ReadyCountdownTicks => _readyCountdownTicks;

        public ulong RoomRevision => _roomRevision;

        public NetworkRoomPhase RoomPhase => _roomPhase;

        public uint RoomCountdownRemainingTicks => _roomCountdownRemainingTicks;

        public bool TryHandshake(
            ConnectionId connectionId,
            in SessionHandshakeRequest request,
            uint currentTick,
            out SessionHandshakeResponse response)
        {
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

            if (FindPendingSeatByConnection(connectionId.Value, out int pendingSeat))
            {
                if (!PendingTokenMatches(pendingSeat, request.ReconnectToken))
                {
                    response = Reject(HandshakeRejectReason.StaleOrInvalidReconnectToken);
                    return false;
                }

                response = BuildPendingResponse(pendingSeat);
                return true;
            }

            if (request.ReconnectToken.IsEmpty)
            {
                if (!request.SessionEpoch.IsEmpty && request.SessionEpoch != _sessionEpoch)
                {
                    response = Reject(HandshakeRejectReason.SessionEpochMismatch);
                    return false;
                }

                return TryInitialJoin(connectionId, currentTick, out response);
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
            if (FindPendingSeatByConnection(connectionId.Value, out int pendingSeat))
            {
                _connectionValues[pendingSeat] = 0;
                return true;
            }

            if (!FindConnectedSeat(connectionId.Value, out int seat))
            {
                return false;
            }

            _states[seat] = SeatState.AwaitingReconnect;
            _connectionValues[seat] = 0;
            _disconnectTicks[seat] = currentTick;
            _roomReady[seat] = false;
            RefreshRoomPhase();
            MarkRoomChanged();
            return true;
        }

        public bool TryConfirmHandshake(
            ConnectionId connectionId,
            in SessionHandshakeConfirmation confirmation,
            out SessionSeatBinding binding,
            out bool reconnect)
        {
            binding = default;
            reconnect = false;
            int seat = confirmation.SeatSlot;
            if (!confirmation.IsWellFormed ||
                confirmation.SessionEpoch != _sessionEpoch ||
                (uint)seat >= (uint)_states.Length ||
                _states[seat] != SeatState.HandshakePending ||
                _connectionValues[seat] != connectionId.Value ||
                _seatGenerations[seat] != confirmation.SeatGeneration ||
                _pendingTokenLow[seat] != confirmation.ReconnectToken.Low ||
                _pendingTokenHigh[seat] != confirmation.ReconnectToken.High)
            {
                return false;
            }

            reconnect = _pendingReconnect[seat];
            _states[seat] = SeatState.Connected;
            _tokenLow[seat] = _pendingTokenLow[seat];
            _tokenHigh[seat] = _pendingTokenHigh[seat];
            _disconnectTicks[seat] = 0;
            _roomReady[seat] = false;
            ClearPendingHandshake(seat);
            RefreshRoomPhase();
            MarkRoomChanged();
            binding = GetSeatBinding(seat);
            return true;
        }

        public RoomReadyIntentApplyResult ApplyRoomReadyIntent(
            ConnectionId connectionId,
            NetworkRoomReadyState readyState,
            uint committedTick)
        {
            if (readyState is not NetworkRoomReadyState.Unready and not NetworkRoomReadyState.Ready)
            {
                throw new ArgumentOutOfRangeException(nameof(readyState));
            }

            if (!FindConnectedSeat(connectionId.Value, out int seat))
            {
                return RoomReadyIntentApplyResult.Unauthenticated;
            }

            ObserveCommittedTick(committedTick);
            if (_roomPhase == NetworkRoomPhase.Started)
            {
                return RoomReadyIntentApplyResult.MatchAlreadyStarted;
            }

            bool ready = readyState == NetworkRoomReadyState.Ready;
            if (_roomReady[seat] == ready)
            {
                return RoomReadyIntentApplyResult.Unchanged;
            }

            _roomReady[seat] = ready;
            RefreshRoomPhase();
            MarkRoomChanged();
            return RoomReadyIntentApplyResult.Applied;
        }

        public bool AdvanceRoomCountdown(uint committedTick)
        {
            ObserveCommittedTick(committedTick);
            if (_roomPhase != NetworkRoomPhase.Countdown)
            {
                return false;
            }

            uint elapsed = unchecked(committedTick - _roomCountdownStartTick);
            uint remaining = elapsed >= _readyCountdownTicks
                ? 0
                : _readyCountdownTicks - elapsed;
            if (remaining == _roomCountdownRemainingTicks)
            {
                return false;
            }

            _roomCountdownRemainingTicks = remaining;
            if (remaining == 0)
            {
                _roomPhase = NetworkRoomPhase.Started;
            }

            MarkRoomChanged();
            return true;
        }

        public bool TryCopyRoomSnapshot(
            Span<NetworkRoomSeatSnapshot> seats,
            out NetworkRoomSnapshotHeader header,
            out int seatCount)
        {
            seatCount = _states.Length;
            if (seats.Length < seatCount)
            {
                header = default;
                return false;
            }

            int connectedCount = 0;
            int readyCount = 0;
            for (int i = 0; i < _states.Length; i++)
            {
                NetworkRoomSeatConnectionState connectionState = _states[i] switch
                {
                    SeatState.Empty => NetworkRoomSeatConnectionState.Empty,
                    SeatState.Connected => NetworkRoomSeatConnectionState.Connected,
                    SeatState.AwaitingReconnect => NetworkRoomSeatConnectionState.AwaitingReconnect,
                    SeatState.HandshakePending => _pendingReconnect[i]
                        ? NetworkRoomSeatConnectionState.AwaitingReconnect
                        : NetworkRoomSeatConnectionState.Empty,
                    _ => throw new InvalidOperationException("Authoritative room contains an unknown seat state."),
                };
                NetworkRoomReadyState readyState = _roomReady[i]
                    ? NetworkRoomReadyState.Ready
                    : NetworkRoomReadyState.Unready;
                bool occupied = _states[i] != SeatState.Empty &&
                    (_states[i] != SeatState.HandshakePending || _pendingReconnect[i]);
                seats[i] = new NetworkRoomSeatSnapshot(
                    i,
                    connectionState,
                    readyState,
                    occupied ? _seatGenerations[i] : 0,
                    occupied ? new PlayerId(_playerValues[i]) : default);
                connectedCount += _states[i] == SeatState.Connected ? 1 : 0;
                readyCount += _roomReady[i] ? 1 : 0;
            }

            header = new NetworkRoomSnapshotHeader(
                _sessionEpoch,
                _roomRevision,
                _roomCommittedTick,
                _roomCountdownRemainingTicks,
                checked((ushort)_states.Length),
                checked((ushort)connectedCount),
                checked((ushort)readyCount),
                _roomPhase);
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

        public bool TryGetSeatBinding(ConnectionId connectionId, out SessionSeatBinding binding)
        {
            if (!FindConnectedSeat(connectionId.Value, out int seat))
            {
                binding = default;
                return false;
            }

            binding = GetSeatBinding(seat);
            return true;
        }

        /// <summary>
        /// Atomically expires reconnect windows and returns every released seat so callers can
        /// clear command, replication, and disclosure state before the slot is reused.
        /// </summary>
        public bool TryExpireAwaitingSeats(
            uint currentTick,
            Span<SessionSeatBinding> expiredSeats,
            out int expiredCount)
        {
            ExpirePendingHandshakes(currentTick);
            expiredCount = 0;
            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i] == SeatState.AwaitingReconnect && IsReconnectExpired(i, currentTick))
                {
                    expiredCount++;
                }
            }

            if (expiredSeats.Length < expiredCount)
            {
                return false;
            }

            int writeIndex = 0;
            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i] != SeatState.AwaitingReconnect || !IsReconnectExpired(i, currentTick))
                {
                    continue;
                }

                expiredSeats[writeIndex++] = GetSeatBinding(i);
                ClearSeat(i);
            }

            return true;
        }

        private bool TryInitialJoin(
            ConnectionId connectionId,
            uint currentTick,
            out SessionHandshakeResponse response)
        {
            if (_roomPhase == NetworkRoomPhase.Started)
            {
                response = Reject(HandshakeRejectReason.MatchAlreadyStarted);
                return false;
            }

            if (!TryFindEmptySeat(out int seat) && !TryFindAbandonedInitialHandshake(out seat))
            {
                response = Reject(HandshakeRejectReason.SessionFull);
                return false;
            }

            if (_states[seat] == SeatState.HandshakePending)
            {
                ClearSeat(seat);
            }

            ReconnectToken token = IssueToken();
            _seatGenerations[seat] = NextGeneration(_seatGenerations[seat]);
            _states[seat] = SeatState.HandshakePending;
            _connectionValues[seat] = connectionId.Value;
            _pendingReconnect[seat] = false;
            _pendingTokenLow[seat] = token.Low;
            _pendingTokenHigh[seat] = token.High;
            _pendingTicks[seat] = currentTick;
            _disconnectTicks[seat] = 0;
            _roomReady[seat] = false;
            response = BuildPendingResponse(seat);
            return true;
        }

        private bool TryReconnect(
            ConnectionId connectionId,
            ReconnectToken token,
            uint currentTick,
            out SessionHandshakeResponse response)
        {
            if (FindPendingSeatByToken(token, out int pendingSeat))
            {
                if (IsPendingExpired(pendingSeat, currentTick))
                {
                    ExpirePendingHandshake(pendingSeat);
                }
                else
                {
                    _connectionValues[pendingSeat] = connectionId.Value;
                    response = BuildPendingResponse(pendingSeat);
                    return true;
                }
            }

            if (!FindSeatByToken(token, out int seat) ||
                _states[seat] != SeatState.AwaitingReconnect ||
                IsReconnectExpired(seat, currentTick))
            {
                response = Reject(HandshakeRejectReason.StaleOrInvalidReconnectToken);
                return false;
            }

            ReconnectToken rotated = IssueToken();
            _states[seat] = SeatState.HandshakePending;
            _connectionValues[seat] = connectionId.Value;
            _pendingReconnect[seat] = true;
            _pendingTokenLow[seat] = rotated.Low;
            _pendingTokenHigh[seat] = rotated.High;
            _pendingTicks[seat] = currentTick;
            _roomReady[seat] = false;
            response = BuildPendingResponse(seat);
            return true;
        }

        private SessionHandshakeResponse BuildPendingResponse(int seat) =>
            SessionHandshakeResponse.Accept(
                GetSeatBinding(seat),
                new ReconnectToken(_pendingTokenLow[seat], _pendingTokenHigh[seat]),
                _requiredProtocolVersion,
                _requiredContentFingerprint,
                _sessionEpoch,
                nextClientBatchSequence: 1);

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

        private bool TryFindAbandonedInitialHandshake(out int seat)
        {
            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i] == SeatState.HandshakePending &&
                    !_pendingReconnect[i] &&
                    _connectionValues[i] == 0)
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

        private bool FindPendingSeatByConnection(int connectionValue, out int seat)
        {
            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i] == SeatState.HandshakePending &&
                    _connectionValues[i] == connectionValue)
                {
                    seat = i;
                    return true;
                }
            }

            seat = -1;
            return false;
        }

        private bool FindPendingSeatByToken(ReconnectToken token, out int seat)
        {
            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i] == SeatState.HandshakePending && PendingTokenMatches(i, token))
                {
                    seat = i;
                    return true;
                }
            }

            seat = -1;
            return false;
        }

        private bool PendingTokenMatches(int seat, ReconnectToken token) =>
            (_pendingTokenLow[seat] == token.Low && _pendingTokenHigh[seat] == token.High) ||
            (_pendingReconnect[seat] && _tokenLow[seat] == token.Low && _tokenHigh[seat] == token.High) ||
            (!_pendingReconnect[seat] && token.IsEmpty);

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

        private bool IsPendingExpired(int seat, uint currentTick)
        {
            uint originTick = _pendingReconnect[seat] ? _disconnectTicks[seat] : _pendingTicks[seat];
            return unchecked(currentTick - originTick) > _reconnectWindowTicks;
        }

        private void ExpirePendingHandshakes(uint currentTick)
        {
            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i] == SeatState.HandshakePending && IsPendingExpired(i, currentTick))
                {
                    ExpirePendingHandshake(i);
                }
            }
        }

        private void ExpirePendingHandshake(int seat)
        {
            if (_pendingReconnect[seat])
            {
                _states[seat] = SeatState.AwaitingReconnect;
                _connectionValues[seat] = 0;
                ClearPendingHandshake(seat);
                return;
            }

            ClearSeat(seat);
        }

        private void ClearPendingHandshake(int seat)
        {
            _pendingReconnect[seat] = false;
            _pendingTokenLow[seat] = 0;
            _pendingTokenHigh[seat] = 0;
            _pendingTicks[seat] = 0;
        }

        private void ClearSeat(int seat)
        {
            _states[seat] = SeatState.Empty;
            _connectionValues[seat] = 0;
            _tokenLow[seat] = 0;
            _tokenHigh[seat] = 0;
            _disconnectTicks[seat] = 0;
            _roomReady[seat] = false;
            ClearPendingHandshake(seat);
            RefreshRoomPhase();
            MarkRoomChanged();
        }

        private void ObserveCommittedTick(uint committedTick)
        {
            if (_hasRoomCommittedTick && committedTick < _roomCommittedTick)
            {
                throw new InvalidOperationException(
                    $"Room committed tick regressed from {_roomCommittedTick} to {committedTick}.");
            }

            _roomCommittedTick = committedTick;
            _hasRoomCommittedTick = true;
        }

        private void RefreshRoomPhase()
        {
            if (_roomPhase == NetworkRoomPhase.Started)
            {
                return;
            }

            int connectedCount = 0;
            int readyCount = 0;
            for (int i = 0; i < _states.Length; i++)
            {
                connectedCount += _states[i] == SeatState.Connected ? 1 : 0;
                readyCount += _roomReady[i] ? 1 : 0;
            }

            if (connectedCount < _states.Length)
            {
                _roomPhase = NetworkRoomPhase.WaitingForPlayers;
                _roomCountdownRemainingTicks = 0;
                _roomCountdownStartTick = 0;
                return;
            }

            if (readyCount < _states.Length)
            {
                _roomPhase = NetworkRoomPhase.WaitingForReady;
                _roomCountdownRemainingTicks = 0;
                _roomCountdownStartTick = 0;
                return;
            }

            _roomPhase = NetworkRoomPhase.Countdown;
            _roomCountdownStartTick = _roomCommittedTick;
            _roomCountdownRemainingTicks = _readyCountdownTicks;
        }

        private void MarkRoomChanged() => _roomRevision = checked(_roomRevision + 1);

        private SessionSeatBinding GetSeatBinding(int seat) =>
            new(seat, _seatGenerations[seat], new PlayerId(_playerValues[seat]));

        private static uint NextGeneration(uint current) => current == uint.MaxValue ? 1u : current + 1u;

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

                if (FindSeatByToken(token, out _) || FindPendingSeatByToken(token, out _))
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
