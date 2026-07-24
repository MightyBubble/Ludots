using Arch.Core;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Transport;

namespace Ludots.Core.Networking.Runtime
{
    public enum NetworkSeatConnectionState : byte
    {
        Empty = 0,
        Connected = 1,
        AwaitingReconnect = 2,
    }

    public sealed class NetworkRuntimeStateObserver : INetworkRuntimeObserver
    {
        private readonly NetworkSeatConnectionState[] _seatStates;
        private readonly uint[] _seatGenerations;
        private readonly int[] _seatPlayerIds;

        public NetworkRuntimeStateObserver(int seatCapacity)
        {
            if (seatCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seatCapacity));
            }

            _seatStates = new NetworkSeatConnectionState[seatCapacity];
            _seatGenerations = new uint[seatCapacity];
            _seatPlayerIds = new int[seatCapacity];
        }

        public int SeatCapacity => _seatStates.Length;
        public int FaultCount { get; private set; }
        public NetworkRuntimeFault LastFault { get; private set; }
        public SessionHandshakeResponse LastClientHandshake { get; private set; }
        public NetworkCommandAdmissionOutcome LastClientAdmission { get; private set; }
        public NetworkResyncRequired LastClientResync { get; private set; }

        public NetworkSeatConnectionState GetSeatState(int seatSlot)
        {
            ValidateSeatSlot(seatSlot);
            return _seatStates[seatSlot];
        }

        public bool TryGetSeatBinding(int seatSlot, out SessionSeatBinding binding)
        {
            ValidateSeatSlot(seatSlot);
            if (_seatStates[seatSlot] == NetworkSeatConnectionState.Empty)
            {
                binding = default;
                return false;
            }

            binding = new SessionSeatBinding(
                seatSlot,
                _seatGenerations[seatSlot],
                new PlayerId(_seatPlayerIds[seatSlot]));
            return true;
        }

        public void OnFault(in NetworkRuntimeFault fault)
        {
            FaultCount++;
            LastFault = fault;
        }

        public void OnServerSeatConnected(in SessionSeatBinding seat, bool reconnected)
        {
            ValidateBinding(in seat);
            _seatStates[seat.Slot] = NetworkSeatConnectionState.Connected;
            _seatGenerations[seat.Slot] = seat.Generation;
            _seatPlayerIds[seat.Slot] = seat.PlayerId.Value;
        }

        public void OnServerSeatDisconnected(in SessionSeatBinding seat, TransportDisconnectReason reason)
        {
            ValidateCurrentBinding(in seat);
            _seatStates[seat.Slot] = NetworkSeatConnectionState.AwaitingReconnect;
        }

        public void OnServerSeatReleased(in SessionSeatBinding seat)
        {
            ValidateCurrentBinding(in seat);
            _seatStates[seat.Slot] = NetworkSeatConnectionState.Empty;
            _seatGenerations[seat.Slot] = 0;
            _seatPlayerIds[seat.Slot] = 0;
        }

        public void OnClientHandshake(in SessionHandshakeResponse response) => LastClientHandshake = response;

        public void OnClientAdmission(in NetworkCommandAdmissionOutcome outcome) => LastClientAdmission = outcome;

        public void OnClientResyncRequired(in NetworkResyncRequired message) => LastClientResync = message;

        private void ValidateBinding(in SessionSeatBinding binding)
        {
            if (!binding.IsValid ||
                (uint)binding.Slot >= (uint)_seatStates.Length ||
                binding.PlayerId.Value != binding.Slot + 1)
            {
                throw new InvalidOperationException("Network runtime reported an invalid authoritative seat binding.");
            }
        }

        private void ValidateCurrentBinding(in SessionSeatBinding binding)
        {
            ValidateBinding(in binding);
            if (_seatStates[binding.Slot] == NetworkSeatConnectionState.Empty ||
                _seatGenerations[binding.Slot] != binding.Generation ||
                _seatPlayerIds[binding.Slot] != binding.PlayerId.Value)
            {
                throw new InvalidOperationException("Network runtime reported a stale authoritative seat event.");
            }
        }

        private void ValidateSeatSlot(int seatSlot)
        {
            if ((uint)seatSlot >= (uint)_seatStates.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(seatSlot));
            }
        }
    }

    public sealed class NetworkRuntimeObserverFanout : INetworkRuntimeObserver
    {
        public NetworkRuntimeObserverFanout(
            NetworkRuntimeStateObserver stateObserver,
            INetworkRuntimeObserver bridge)
        {
            StateObserver = stateObserver ?? throw new ArgumentNullException(nameof(stateObserver));
            Bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            if (ReferenceEquals(stateObserver, bridge))
            {
                throw new ArgumentException(
                    "Network runtime observer bridge must be distinct from the state observer.",
                    nameof(bridge));
            }
        }

        public NetworkRuntimeStateObserver StateObserver { get; }

        public INetworkRuntimeObserver Bridge { get; }

        public void OnFault(in NetworkRuntimeFault fault)
        {
            Bridge.OnFault(in fault);
            StateObserver.OnFault(in fault);
        }

        public void OnServerSeatConnected(in SessionSeatBinding seat, bool reconnected)
        {
            Bridge.OnServerSeatConnected(in seat, reconnected);
            StateObserver.OnServerSeatConnected(in seat, reconnected);
        }

        public void OnServerSeatDisconnected(in SessionSeatBinding seat, TransportDisconnectReason reason)
        {
            Bridge.OnServerSeatDisconnected(in seat, reason);
            StateObserver.OnServerSeatDisconnected(in seat, reason);
        }

        public void OnServerSeatReleased(in SessionSeatBinding seat)
        {
            Bridge.OnServerSeatReleased(in seat);
            StateObserver.OnServerSeatReleased(in seat);
        }

        public void OnClientHandshake(in SessionHandshakeResponse response)
        {
            Bridge.OnClientHandshake(in response);
            StateObserver.OnClientHandshake(in response);
        }

        public void OnClientAdmission(in NetworkCommandAdmissionOutcome outcome)
        {
            Bridge.OnClientAdmission(in outcome);
            StateObserver.OnClientAdmission(in outcome);
        }

        public void OnClientResyncRequired(in NetworkResyncRequired message)
        {
            Bridge.OnClientResyncRequired(in message);
            StateObserver.OnClientResyncRequired(in message);
        }
    }

    public sealed class ClientReplicationBridgeFactory : IClientReplicationBridgeFactory
    {
        private readonly World _world;
        private readonly ClientReplicationSchemaApplierRegistry _appliers;

        public ClientReplicationBridgeFactory(
            World world,
            int globalEntityCapacity,
            int replicationEntityCapacityPerSeat,
            ClientReplicationSchemaApplierRegistry appliers)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            if (globalEntityCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(globalEntityCapacity));
            }

            if (replicationEntityCapacityPerSeat <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(replicationEntityCapacityPerSeat));
            }

            if (replicationEntityCapacityPerSeat > globalEntityCapacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(replicationEntityCapacityPerSeat),
                    "Per-seat replication capacity cannot exceed global entity capacity.");
            }

            _appliers = appliers ?? throw new ArgumentNullException(nameof(appliers));
            if (!appliers.IsFrozen)
            {
                throw new InvalidOperationException(
                    "Client replication applier registry must be frozen before bridge composition.");
            }

            GlobalEntityCapacity = globalEntityCapacity;
            ReplicationEntityCapacityPerSeat = replicationEntityCapacityPerSeat;
        }

        public int GlobalEntityCapacity { get; }

        public int ReplicationEntityCapacityPerSeat { get; }

        public ClientWorldReplicationBridge Create(ulong sessionEpoch)
        {
            if (sessionEpoch == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sessionEpoch));
            }

            return new ClientWorldReplicationBridge(
                _world,
                GlobalEntityCapacity,
                ReplicationEntityCapacityPerSeat,
                sessionEpoch,
                _appliers);
        }
    }
}
