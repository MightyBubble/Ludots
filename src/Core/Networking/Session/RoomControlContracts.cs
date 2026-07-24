using System;

namespace Ludots.Core.Networking.Session
{
    public enum NetworkRoomReadyState : byte
    {
        Unready = 0,
        Ready = 1,
    }

    public enum NetworkRoomSeatConnectionState : byte
    {
        Empty = 0,
        Connected = 1,
        AwaitingReconnect = 2,
    }

    public enum NetworkRoomPhase : byte
    {
        WaitingForPlayers = 0,
        WaitingForReady = 1,
        Countdown = 2,
        Started = 3,
    }

    public enum RoomReadyIntentApplyResult : byte
    {
        Applied = 0,
        Unchanged = 1,
        Unauthenticated = 2,
        MatchAlreadyStarted = 3,
    }

    public readonly struct NetworkRoomReadyIntent
    {
        public NetworkRoomReadyIntent(SessionEpoch sessionEpoch, NetworkRoomReadyState readyState)
        {
            if (sessionEpoch.IsEmpty)
            {
                throw new ArgumentException("Room ready intent requires a non-empty session epoch.", nameof(sessionEpoch));
            }

            if (readyState is not NetworkRoomReadyState.Unready and not NetworkRoomReadyState.Ready)
            {
                throw new ArgumentOutOfRangeException(nameof(readyState));
            }

            SessionEpoch = sessionEpoch;
            ReadyState = readyState;
        }

        public SessionEpoch SessionEpoch { get; }
        public NetworkRoomReadyState ReadyState { get; }
    }

    public readonly struct NetworkRoomSnapshotHeader
    {
        public NetworkRoomSnapshotHeader(
            SessionEpoch sessionEpoch,
            ulong revision,
            uint committedTick,
            uint countdownRemainingTicks,
            ushort seatCount,
            ushort connectedSeatCount,
            ushort readySeatCount,
            NetworkRoomPhase phase)
        {
            if (sessionEpoch.IsEmpty)
            {
                throw new ArgumentException("Room snapshot requires a non-empty session epoch.", nameof(sessionEpoch));
            }

            if (revision == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(revision));
            }

            if (seatCount == 0 || connectedSeatCount > seatCount || readySeatCount > connectedSeatCount)
            {
                throw new ArgumentException("Room snapshot seat counts are inconsistent.");
            }

            if (phase is < NetworkRoomPhase.WaitingForPlayers or > NetworkRoomPhase.Started)
            {
                throw new ArgumentOutOfRangeException(nameof(phase));
            }

            SessionEpoch = sessionEpoch;
            Revision = revision;
            CommittedTick = committedTick;
            CountdownRemainingTicks = countdownRemainingTicks;
            SeatCount = seatCount;
            ConnectedSeatCount = connectedSeatCount;
            ReadySeatCount = readySeatCount;
            Phase = phase;
        }

        public SessionEpoch SessionEpoch { get; }
        public ulong Revision { get; }
        public uint CommittedTick { get; }
        public uint CountdownRemainingTicks { get; }
        public ushort SeatCount { get; }
        public ushort ConnectedSeatCount { get; }
        public ushort ReadySeatCount { get; }
        public NetworkRoomPhase Phase { get; }
    }

    public readonly struct NetworkRoomSeatSnapshot
    {
        public NetworkRoomSeatSnapshot(
            int slot,
            NetworkRoomSeatConnectionState connectionState,
            NetworkRoomReadyState readyState,
            uint generation,
            PlayerId playerId)
        {
            if ((uint)slot > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }

            if (connectionState is < NetworkRoomSeatConnectionState.Empty or > NetworkRoomSeatConnectionState.AwaitingReconnect)
            {
                throw new ArgumentOutOfRangeException(nameof(connectionState));
            }

            if (readyState is not NetworkRoomReadyState.Unready and not NetworkRoomReadyState.Ready)
            {
                throw new ArgumentOutOfRangeException(nameof(readyState));
            }

            if (connectionState == NetworkRoomSeatConnectionState.Empty)
            {
                if (generation != 0 || playerId.Value != 0 || readyState != NetworkRoomReadyState.Unready)
                {
                    throw new ArgumentException("An empty room seat cannot carry identity or ready state.");
                }
            }
            else if (generation == 0 || playerId.Value <= 0)
            {
                throw new ArgumentException("An occupied room seat requires a valid server-assigned identity.");
            }

            if (connectionState != NetworkRoomSeatConnectionState.Connected && readyState != NetworkRoomReadyState.Unready)
            {
                throw new ArgumentException("Only a connected room seat may be ready.");
            }

            Slot = slot;
            ConnectionState = connectionState;
            ReadyState = readyState;
            Generation = generation;
            PlayerId = playerId;
        }

        public int Slot { get; }
        public NetworkRoomSeatConnectionState ConnectionState { get; }
        public NetworkRoomReadyState ReadyState { get; }
        public uint Generation { get; }
        public PlayerId PlayerId { get; }
    }
}
