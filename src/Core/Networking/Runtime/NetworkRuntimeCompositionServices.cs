using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Transport;

namespace Ludots.Core.Networking.Runtime
{
    public sealed class AuthoritativeSeatControllerRegistry : IAuthoritativeSeatControllerResolver
    {
        private readonly World _world;
        private readonly Entity[] _controllers;

        public AuthoritativeSeatControllerRegistry(
            World world,
            PlayerEntityLookup players,
            int seatCapacity)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            ArgumentNullException.ThrowIfNull(players);
            if (seatCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seatCapacity));
            }

            _controllers = new Entity[seatCapacity];
            for (int seat = 0; seat < seatCapacity; seat++)
            {
                int playerId = seat + 1;
                if (!players.TryGet(playerId, out Entity controller) ||
                    !world.IsAlive(controller) ||
                    !world.TryGet(controller, out PlayerIdentity identity) ||
                    identity.PlayerId != playerId)
                {
                    throw new InvalidOperationException(
                        $"Authoritative map requires one live PlayerIdentity representative for player {playerId}.");
                }

                _controllers[seat] = controller;
            }

            if (players.Count != seatCapacity)
            {
                throw new InvalidOperationException(
                    $"Authoritative map player representative count {players.Count} does not match networking capacity {seatCapacity}.");
            }
        }

        public int SeatCapacity => _controllers.Length;

        public bool TryResolveController(in SessionSeatBinding seat, out Entity controller)
        {
            int slot = seat.Slot;
            if (!seat.IsValid ||
                (uint)slot >= (uint)_controllers.Length ||
                seat.PlayerId.Value != slot + 1 ||
                !_world.IsAlive(_controllers[slot]))
            {
                controller = Entity.Null;
                return false;
            }

            controller = _controllers[slot];
            return true;
        }

        public bool TryGetController(int seatSlot, out Entity controller)
        {
            if ((uint)seatSlot >= (uint)_controllers.Length ||
                !_world.IsAlive(_controllers[seatSlot]))
            {
                controller = Entity.Null;
                return false;
            }

            controller = _controllers[seatSlot];
            return true;
        }
    }

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
        private readonly NetworkRoomSeatSnapshot[] _roomSeats;

        public NetworkRuntimeStateObserver(int seatCapacity)
        {
            if (seatCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seatCapacity));
            }

            _seatStates = new NetworkSeatConnectionState[seatCapacity];
            _seatGenerations = new uint[seatCapacity];
            _seatPlayerIds = new int[seatCapacity];
            _roomSeats = new NetworkRoomSeatSnapshot[seatCapacity];
        }

        public int SeatCapacity => _seatStates.Length;
        public int FaultCount { get; private set; }
        public NetworkRuntimeFault LastFault { get; private set; }
        public SessionHandshakeResponse LastClientHandshake { get; private set; }
        public NetworkCommandAdmissionOutcome LastClientAdmission { get; private set; }
        public NetworkResyncRequired LastClientResync { get; private set; }
        public bool HasRoomSnapshot { get; private set; }
        public NetworkRoomSnapshotHeader LastRoomSnapshot { get; private set; }

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

        public void OnServerRoomSnapshot(
            in NetworkRoomSnapshotHeader snapshot,
            ReadOnlySpan<NetworkRoomSeatSnapshot> seats) =>
            StoreRoomSnapshot(in snapshot, seats);

        public void OnClientHandshake(in SessionHandshakeResponse response) => LastClientHandshake = response;

        public void OnClientAdmission(in NetworkCommandAdmissionOutcome outcome) => LastClientAdmission = outcome;

        public void OnClientResyncRequired(in NetworkResyncRequired message) => LastClientResync = message;

        public void OnClientRoomSnapshot(
            in NetworkRoomSnapshotHeader snapshot,
            ReadOnlySpan<NetworkRoomSeatSnapshot> seats) =>
            StoreRoomSnapshot(in snapshot, seats);

        public bool TryCopyRoomSeats(Span<NetworkRoomSeatSnapshot> destination, out int seatCount)
        {
            seatCount = HasRoomSnapshot ? LastRoomSnapshot.SeatCount : 0;
            if (destination.Length < seatCount)
            {
                return false;
            }

            _roomSeats.AsSpan(0, seatCount).CopyTo(destination);
            return HasRoomSnapshot;
        }

        private void StoreRoomSnapshot(
            in NetworkRoomSnapshotHeader snapshot,
            ReadOnlySpan<NetworkRoomSeatSnapshot> seats)
        {
            if (snapshot.SeatCount != seats.Length || seats.Length > _roomSeats.Length)
            {
                throw new InvalidOperationException("Room snapshot exceeds the observer's configured seat capacity.");
            }

            seats.CopyTo(_roomSeats);
            if (seats.Length < _roomSeats.Length)
            {
                Array.Clear(_roomSeats, seats.Length, _roomSeats.Length - seats.Length);
            }

            LastRoomSnapshot = snapshot;
            HasRoomSnapshot = true;
        }

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

    public sealed class AuthoritativeReplicationSeatRuntimeFactory
    {
        private readonly World _world;
        private readonly NetworkEntityTable _entities;
        private readonly KnowledgeProjectionStore _knowledge;
        private readonly AuthoritativeSeatControllerRegistry _controllers;
        private readonly ReplicationSchemaProjectorRegistry _projectors;
        private readonly NetworkRuntimeConfig _config;

        public AuthoritativeReplicationSeatRuntimeFactory(
            World world,
            NetworkEntityTable entities,
            KnowledgeProjectionStore knowledge,
            AuthoritativeSeatControllerRegistry controllers,
            ReplicationSchemaProjectorRegistry projectors,
            NetworkRuntimeConfig config)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _entities = entities ?? throw new ArgumentNullException(nameof(entities));
            _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
            _controllers = controllers ?? throw new ArgumentNullException(nameof(controllers));
            _projectors = projectors ?? throw new ArgumentNullException(nameof(projectors));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            config.Validate();

            if (!projectors.IsFrozen)
            {
                throw new InvalidOperationException("Replication projector registry must be frozen before seat runtime composition.");
            }

            if (entities.Capacity != config.NetworkEntityCapacity ||
                controllers.SeatCapacity != config.PlayerCapacity)
            {
                throw new InvalidOperationException("Replication composition capacities do not match the networking profile.");
            }
        }

        public AuthoritativeReplicationSeatRuntime[] CreateAll()
        {
            var seats = new AuthoritativeReplicationSeatRuntime[_config.PlayerCapacity];
            for (int seat = 0; seat < seats.Length; seat++)
            {
                if (!_controllers.TryGetController(seat, out Entity viewer))
                {
                    throw new InvalidOperationException($"Authoritative replication viewer for seat {seat} is unavailable.");
                }

                var disclosureLog = new ReplicationDisclosureChangeLog(_config.DisclosureChangeLogCapacity);
                seats[seat] = new AuthoritativeReplicationSeatRuntime(
                    seat,
                    new PlayerId(seat + 1),
                    new AuthoritativeWorldReplicationBridge(
                        _world,
                        _entities,
                        _knowledge,
                        viewer,
                        _projectors,
                        _config.NetworkEntityCapacity),
                    new AuthoritativeReplicationChannel(
                        _config.NetworkEntityCapacity,
                        _config.BaselineCapacity,
                        disclosureLog),
                    disclosureLog,
                    new ReplicationProjectionBuffer(_config.NetworkEntityCapacity),
                    new ReplicationPacketBuffer(_config.ReplicationPacketEntityCapacity));
            }

            return seats;
        }
    }

    public sealed class ClientReplicationBridgeFactory : IClientReplicationBridgeFactory
    {
        private readonly World _world;
        private readonly int _entityCapacity;
        private readonly ClientReplicationSchemaApplierRegistry _appliers;

        public ClientReplicationBridgeFactory(
            World world,
            int entityCapacity,
            ClientReplicationSchemaApplierRegistry appliers)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            if (entityCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityCapacity));
            }

            _appliers = appliers ?? throw new ArgumentNullException(nameof(appliers));
            if (!appliers.IsFrozen)
            {
                throw new InvalidOperationException("Client replication applier registry must be frozen before bridge composition.");
            }

            _entityCapacity = entityCapacity;
        }

        public ClientWorldReplicationBridge Create(ulong sessionEpoch)
        {
            if (sessionEpoch == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sessionEpoch));
            }

            return new ClientWorldReplicationBridge(_world, _entityCapacity, sessionEpoch, _appliers);
        }
    }
}
