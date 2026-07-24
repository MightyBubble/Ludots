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

    public sealed class AuthoritativeReplicationSeatRuntimeFactory : IAuthoritativeReplicationSeatRuntimeFactory
    {
        private readonly World _world;
        private readonly NetworkEntityTable _entities;
        private readonly KnowledgeProjectionStore _knowledge;
        private readonly ReplicationSchemaProjectorRegistry _projectors;
        private readonly int _baselineCapacity;
        private readonly int _disclosureChangeLogCapacity;
        private readonly bool[] _leased;
        private readonly uint[] _generations;
        private readonly int[] _playerIds;
        private readonly AuthoritativeReplicationSeatRuntime?[] _runtimes;

        public AuthoritativeReplicationSeatRuntimeFactory(
            World world,
            NetworkEntityTable entities,
            KnowledgeProjectionStore knowledge,
            ReplicationSchemaProjectorRegistry projectors,
            NetworkRuntimeConfig config)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _entities = entities ?? throw new ArgumentNullException(nameof(entities));
            _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
            _projectors = projectors ?? throw new ArgumentNullException(nameof(projectors));
            ArgumentNullException.ThrowIfNull(config);
            config.Validate();

            if (!projectors.IsFrozen)
            {
                throw new InvalidOperationException(
                    "Replication projector registry must be frozen before seat runtime composition.");
            }

            if (entities.Capacity != config.GlobalNetworkEntityCapacity)
            {
                throw new InvalidOperationException(
                    "Network entity table capacity does not match the networking profile.");
            }

            SeatCapacity = config.PlayerCapacity;
            GlobalEntityCapacity = config.GlobalNetworkEntityCapacity;
            ReplicationEntityCapacityPerSeat = config.ReplicationEntityCapacityPerSeat;
            _baselineCapacity = config.BaselineCapacity;
            _disclosureChangeLogCapacity = config.DisclosureChangeLogCapacity;
            _leased = new bool[SeatCapacity];
            _generations = new uint[SeatCapacity];
            _playerIds = new int[SeatCapacity];
            _runtimes = new AuthoritativeReplicationSeatRuntime?[SeatCapacity];
        }

        public int SeatCapacity { get; }

        public int GlobalEntityCapacity { get; }

        public int ReplicationEntityCapacityPerSeat { get; }

        public bool TryAcquire(
            in SessionSeatBinding seat,
            Entity viewer,
            out AuthoritativeReplicationSeatRuntime? runtime)
        {
            runtime = null;
            int slot = seat.Slot;
            if (!seat.IsValid ||
                (uint)slot >= (uint)SeatCapacity ||
                seat.PlayerId.Value != slot + 1 ||
                viewer == Entity.Null ||
                !_world.IsAlive(viewer) ||
                _leased[slot])
            {
                return false;
            }

            var disclosureLog = new ReplicationDisclosureChangeLog(_disclosureChangeLogCapacity);
            runtime = new AuthoritativeReplicationSeatRuntime(
                new AuthoritativeWorldReplicationBridge(
                    _world,
                    _entities,
                    _knowledge,
                    viewer,
                    _projectors,
                    ReplicationEntityCapacityPerSeat),
                new AuthoritativeReplicationChannel(
                    _entities,
                    ReplicationEntityCapacityPerSeat,
                    _baselineCapacity,
                    disclosureLog),
                new ReplicationProjectionBuffer(ReplicationEntityCapacityPerSeat),
                new ReplicationPacketBuffer(ReplicationEntityCapacityPerSeat));

            _leased[slot] = true;
            _generations[slot] = seat.Generation;
            _playerIds[slot] = seat.PlayerId.Value;
            _runtimes[slot] = runtime;
            return true;
        }

        public bool TryRelease(
            in SessionSeatBinding seat,
            AuthoritativeReplicationSeatRuntime runtime)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            int slot = seat.Slot;
            if (!seat.IsValid ||
                (uint)slot >= (uint)SeatCapacity ||
                !_leased[slot] ||
                _generations[slot] != seat.Generation ||
                _playerIds[slot] != seat.PlayerId.Value ||
                !ReferenceEquals(_runtimes[slot], runtime))
            {
                return false;
            }

            _leased[slot] = false;
            _generations[slot] = 0;
            _playerIds[slot] = 0;
            _runtimes[slot] = null;
            return true;
        }
    }

    public sealed class ClientReplicationBridgeFactory : IClientReplicationBridgeFactory
    {
        private readonly World _world;
        private readonly ClientReplicationSchemaApplierRegistry _appliers;

        public ClientReplicationBridgeFactory(
            World world,
            int globalEntityCapacity,
            ClientReplicationSchemaApplierRegistry appliers)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            if (globalEntityCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(globalEntityCapacity));
            }

            _appliers = appliers ?? throw new ArgumentNullException(nameof(appliers));
            if (!appliers.IsFrozen)
            {
                throw new InvalidOperationException(
                    "Client replication applier registry must be frozen before bridge composition.");
            }

            GlobalEntityCapacity = globalEntityCapacity;
        }

        public int GlobalEntityCapacity { get; }

        public ClientWorldReplicationBridge Create(ulong sessionEpoch)
        {
            if (sessionEpoch == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sessionEpoch));
            }

            return new ClientWorldReplicationBridge(
                _world,
                GlobalEntityCapacity,
                sessionEpoch,
                _appliers);
        }
    }
}
