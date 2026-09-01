using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Transport;
using Ludots.Core.Spatial;

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
        private const int AdmissionProgressStageCapacity = 6;

        private readonly NetworkSeatConnectionState[] _seatStates;
        private readonly uint[] _seatGenerations;
        private readonly int[] _seatPlayerIds;
        private readonly NetworkRoomSeatSnapshot[] _roomSeats;
        private readonly ulong[] _clientAdmissionSequences;
        private readonly NetworkCommandAdmissionOutcome[] _clientAdmissionSummaries;
        private readonly NetworkCommandAdmissionOutcome[] _clientActorAdmissions;
        private readonly NetworkCommandAdmissionOutcome[] _clientAdmissionProgress;
        private readonly bool[] _clientAdmissionActive;
        private readonly bool[] _clientActorAdmissionActive;
        private readonly byte[] _clientAdmissionProgressCounts;
        private readonly int _maxActorsPerCommandBatch;
        private ReplicatedClientCommandStreamIdentity _clientAdmissionIdentity;
        private int _clientAdmissionWriteIndex;

        public NetworkRuntimeStateObserver(
            int seatCapacity,
            int clientAdmissionHistoryCapacity,
            int maxActorsPerCommandBatch)
        {
            if (seatCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seatCapacity));
            }

            if (clientAdmissionHistoryCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(clientAdmissionHistoryCapacity));
            }

            if (maxActorsPerCommandBatch <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxActorsPerCommandBatch));
            }

            _seatStates = new NetworkSeatConnectionState[seatCapacity];
            _seatGenerations = new uint[seatCapacity];
            _seatPlayerIds = new int[seatCapacity];
            _roomSeats = new NetworkRoomSeatSnapshot[seatCapacity];
            _clientAdmissionSequences = new ulong[clientAdmissionHistoryCapacity];
            _clientAdmissionSummaries = new NetworkCommandAdmissionOutcome[clientAdmissionHistoryCapacity];
            _clientAdmissionActive = new bool[clientAdmissionHistoryCapacity];
            _maxActorsPerCommandBatch = maxActorsPerCommandBatch;
            int actorAdmissionCapacity = checked(clientAdmissionHistoryCapacity * maxActorsPerCommandBatch);
            _clientActorAdmissions = new NetworkCommandAdmissionOutcome[actorAdmissionCapacity];
            _clientActorAdmissionActive = new bool[actorAdmissionCapacity];
            _clientAdmissionProgress = new NetworkCommandAdmissionOutcome[
                checked(clientAdmissionHistoryCapacity * AdmissionProgressStageCapacity)];
            _clientAdmissionProgressCounts = new byte[clientAdmissionHistoryCapacity];
        }

        public int SeatCapacity => _seatStates.Length;
        public int ClientAdmissionProgressCapacityPerBatch => AdmissionProgressStageCapacity;
        public int FaultCount { get; private set; }
        public NetworkRuntimeFault LastFault { get; private set; }
        public SessionHandshakeResponse LastClientHandshake { get; private set; }
        public NetworkCommandAdmissionOutcome LastClientAdmission { get; private set; }
        public ulong ClientAdmissionRevision { get; private set; }
        public ulong ClientAdmissionHistoryEvictionCount { get; private set; }
        public ulong ClientAdmissionHistoryMissCount { get; private set; }
        public NetworkResyncRequired LastClientResync { get; private set; }
        public bool HasRoomSnapshot { get; private set; }
        public NetworkRoomSnapshotHeader LastRoomSnapshot { get; private set; }

        public int ConnectedSeatCount
        {
            get
            {
                if (HasRoomSnapshot)
                {
                    int roomConnectedCount = 0;
                    int seatCount = LastRoomSnapshot.SeatCount;
                    for (int i = 0; i < seatCount; i++)
                    {
                        if (_roomSeats[i].ConnectionState == NetworkRoomSeatConnectionState.Connected)
                        {
                            roomConnectedCount++;
                        }
                    }
                    return roomConnectedCount;
                }

                int runtimeConnectedCount = 0;
                for (int i = 0; i < _seatStates.Length; i++)
                {
                    if (_seatStates[i] == NetworkSeatConnectionState.Connected)
                    {
                        runtimeConnectedCount++;
                    }
                }
                return runtimeConnectedCount;
            }
        }

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

        public void OnClientHandshake(in SessionHandshakeResponse response)
        {
            bool identityChanged;
            if (response.Accepted)
            {
                var identity = new ReplicatedClientCommandStreamIdentity(
                    response.SessionEpoch,
                    response.Seat.Slot,
                    response.Seat.Generation);
                identityChanged = _clientAdmissionIdentity != identity;
                _clientAdmissionIdentity = identity;
            }
            else
            {
                identityChanged = _clientAdmissionIdentity.IsValid &&
                    _clientAdmissionIdentity.SessionEpoch != response.SessionEpoch;
                if (identityChanged)
                {
                    _clientAdmissionIdentity = default;
                }
            }

            if (identityChanged)
            {
                ResetClientAdmissionSession();
            }

            LastClientHandshake = response;
        }

        public void OnClientAdmission(in NetworkCommandAdmissionOutcome outcome)
        {
            if (outcome.ClientBatchSequence == 0 ||
                outcome.ActorCount <= 0 ||
                outcome.ActorCount > _maxActorsPerCommandBatch ||
                (outcome.Stage == NetworkCommandAdmissionStage.EntityIntake &&
                    outcome.AdmissionBatchIndex >= outcome.ActorCount))
            {
                throw new InvalidOperationException("Client admission outcome exceeds the observer's configured command shape.");
            }

            int batchIndex = FindClientAdmissionBatch(outcome.ClientBatchSequence);
            if (batchIndex < 0)
            {
                if (ClientAdmissionRevision != 0 &&
                    outcome.ClientBatchSequence < LastClientAdmission.ClientBatchSequence)
                {
                    ClientAdmissionHistoryMissCount = checked(ClientAdmissionHistoryMissCount + 1);
                    return;
                }

                batchIndex = AllocateClientAdmissionBatch(outcome.ClientBatchSequence);
            }

            bool changed = false;
            if (outcome.Stage == NetworkCommandAdmissionStage.EntityIntake)
            {
                int actorIndex = GetClientActorAdmissionIndex(batchIndex, outcome.AdmissionBatchIndex);
                if (!_clientActorAdmissionActive[actorIndex] ||
                    ShouldAdvance(in _clientActorAdmissions[actorIndex], in outcome))
                {
                    _clientActorAdmissions[actorIndex] = outcome;
                    _clientActorAdmissionActive[actorIndex] = true;
                    changed = true;
                }
            }

            bool summaryChanged = !_clientAdmissionActive[batchIndex] ||
                ShouldAdvance(in _clientAdmissionSummaries[batchIndex], in outcome);
            if (summaryChanged)
            {
                _clientAdmissionSummaries[batchIndex] = outcome;
                _clientAdmissionActive[batchIndex] = true;
                changed = true;
                AppendClientAdmissionProgress(batchIndex, in outcome);
            }

            if (!changed)
            {
                return;
            }

            ClientAdmissionRevision = checked(ClientAdmissionRevision + 1);
            if (LastClientAdmission.ClientBatchSequence == 0 ||
                outcome.ClientBatchSequence >= LastClientAdmission.ClientBatchSequence)
            {
                LastClientAdmission = _clientAdmissionSummaries[batchIndex];
            }
        }

        public bool TryGetClientAdmission(
            ulong clientBatchSequence,
            out NetworkCommandAdmissionOutcome outcome)
        {
            int batchIndex = FindClientAdmissionBatch(clientBatchSequence);
            if (batchIndex < 0 || !_clientAdmissionActive[batchIndex])
            {
                outcome = default;
                return false;
            }

            outcome = _clientAdmissionSummaries[batchIndex];
            return true;
        }

        public bool TryGetClientActorAdmission(
            ulong clientBatchSequence,
            ushort admissionBatchIndex,
            out NetworkCommandAdmissionOutcome outcome)
        {
            if (admissionBatchIndex >= _maxActorsPerCommandBatch)
            {
                throw new ArgumentOutOfRangeException(nameof(admissionBatchIndex));
            }

            int batchIndex = FindClientAdmissionBatch(clientBatchSequence);
            if (batchIndex < 0)
            {
                outcome = default;
                return false;
            }

            int actorIndex = GetClientActorAdmissionIndex(batchIndex, admissionBatchIndex);
            if (!_clientActorAdmissionActive[actorIndex])
            {
                outcome = default;
                return false;
            }

            outcome = _clientActorAdmissions[actorIndex];
            return true;
        }

        public bool TryCopyClientAdmissionProgress(
            ulong clientBatchSequence,
            Span<NetworkCommandAdmissionOutcome> destination,
            out int progressCount)
        {
            int batchIndex = FindClientAdmissionBatch(clientBatchSequence);
            if (batchIndex < 0)
            {
                progressCount = 0;
                return false;
            }

            progressCount = _clientAdmissionProgressCounts[batchIndex];
            if (destination.Length < progressCount)
            {
                return false;
            }

            int progressOffset = batchIndex * AdmissionProgressStageCapacity;
            _clientAdmissionProgress.AsSpan(progressOffset, progressCount).CopyTo(destination);
            return true;
        }

        private static int AdmissionProgressRank(in NetworkCommandAdmissionOutcome outcome)
        {
            if (NetworkCommandAdmissionCodeSemantics.IsRejection(outcome.Result))
            {
                return 5;
            }

            return outcome.Stage switch
            {
                NetworkCommandAdmissionStage.NetworkIntake => 0,
                NetworkCommandAdmissionStage.GlobalIntake => 1,
                NetworkCommandAdmissionStage.EntityIntake => outcome.Result switch
                {
                    NetworkCommandAdmissionCode.Queued => 2,
                    NetworkCommandAdmissionCode.Pending => 3,
                    NetworkCommandAdmissionCode.Activated => 4,
                    _ => throw new InvalidOperationException(
                        $"Non-rejected EntityIntake cannot carry admission code {outcome.Result}."),
                },
                NetworkCommandAdmissionStage.Terminal => 6,
                _ => throw new InvalidOperationException(
                    $"Unknown network command admission stage {outcome.Stage}."),
            };
        }

        private static bool IsAdmissionRejection(NetworkCommandAdmissionCode code) =>
            NetworkCommandAdmissionCodeSemantics.IsRejection(code);

        private static bool SameAdmission(
            in NetworkCommandAdmissionOutcome left,
            in NetworkCommandAdmissionOutcome right) =>
            left.ClientBatchSequence == right.ClientBatchSequence &&
            left.AdmissionBatchId == right.AdmissionBatchId &&
            left.AdmissionBatchIndex == right.AdmissionBatchIndex &&
            left.Stage == right.Stage &&
            left.Result == right.Result;

        private static bool ShouldAdvance(
            in NetworkCommandAdmissionOutcome current,
            in NetworkCommandAdmissionOutcome incoming)
        {
            int incomingRank = AdmissionProgressRank(in incoming);
            int currentRank = AdmissionProgressRank(in current);
            return incomingRank > currentRank ||
                (incomingRank == currentRank && !SameAdmission(in incoming, in current));
        }

        private int FindClientAdmissionBatch(ulong clientBatchSequence)
        {
            for (int i = 0; i < _clientAdmissionSequences.Length; i++)
            {
                if (_clientAdmissionActive[i] && _clientAdmissionSequences[i] == clientBatchSequence)
                {
                    return i;
                }
            }

            return -1;
        }

        private int AllocateClientAdmissionBatch(ulong clientBatchSequence)
        {
            int batchIndex = _clientAdmissionWriteIndex;
            if (_clientAdmissionActive[batchIndex])
            {
                ClientAdmissionHistoryEvictionCount = checked(ClientAdmissionHistoryEvictionCount + 1);
            }

            _clientAdmissionSequences[batchIndex] = clientBatchSequence;
            _clientAdmissionSummaries[batchIndex] = default;
            _clientAdmissionActive[batchIndex] = false;
            int actorOffset = batchIndex * _maxActorsPerCommandBatch;
            Array.Clear(_clientActorAdmissions, actorOffset, _maxActorsPerCommandBatch);
            Array.Clear(_clientActorAdmissionActive, actorOffset, _maxActorsPerCommandBatch);
            int progressOffset = batchIndex * AdmissionProgressStageCapacity;
            Array.Clear(_clientAdmissionProgress, progressOffset, AdmissionProgressStageCapacity);
            _clientAdmissionProgressCounts[batchIndex] = 0;
            _clientAdmissionWriteIndex = (batchIndex + 1) % _clientAdmissionSequences.Length;
            return batchIndex;
        }

        private void AppendClientAdmissionProgress(
            int batchIndex,
            in NetworkCommandAdmissionOutcome outcome)
        {
            int progressRank = AdmissionProgressRank(in outcome);
            int progressCount = _clientAdmissionProgressCounts[batchIndex];
            if (progressCount > 0)
            {
                int lastIndex = checked(
                    (batchIndex * AdmissionProgressStageCapacity) + progressCount - 1);
                if (AdmissionProgressRank(in _clientAdmissionProgress[lastIndex]) >= progressRank)
                {
                    return;
                }
            }

            if (progressCount >= AdmissionProgressStageCapacity)
            {
                throw new InvalidOperationException(
                    "Client admission progress exceeded the fixed semantic stage capacity.");
            }

            int progressIndex = checked(
                (batchIndex * AdmissionProgressStageCapacity) + progressCount);
            _clientAdmissionProgress[progressIndex] = outcome;
            _clientAdmissionProgressCounts[batchIndex] = checked((byte)(progressCount + 1));
        }

        private void ResetClientAdmissionSession()
        {
            Array.Clear(_clientAdmissionSequences, 0, _clientAdmissionSequences.Length);
            Array.Clear(_clientAdmissionSummaries, 0, _clientAdmissionSummaries.Length);
            Array.Clear(_clientActorAdmissions, 0, _clientActorAdmissions.Length);
            Array.Clear(_clientAdmissionProgress, 0, _clientAdmissionProgress.Length);
            Array.Clear(_clientAdmissionActive, 0, _clientAdmissionActive.Length);
            Array.Clear(_clientActorAdmissionActive, 0, _clientActorAdmissionActive.Length);
            Array.Clear(_clientAdmissionProgressCounts, 0, _clientAdmissionProgressCounts.Length);
            _clientAdmissionWriteIndex = 0;
            LastClientAdmission = default;
            ClientAdmissionRevision = 0;
            ClientAdmissionHistoryEvictionCount = 0;
            ClientAdmissionHistoryMissCount = 0;
        }

        private int GetClientActorAdmissionIndex(int batchIndex, int admissionBatchIndex) =>
            checked((batchIndex * _maxActorsPerCommandBatch) + admissionBatchIndex);

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
        private readonly ISpatialPartitionMembership _spatialMembership;
        private readonly KnowledgeProjectionStore _knowledge;
        private readonly Func<Entity> _viewerResolver;

        public ClientReplicationBridgeFactory(
            World world,
            int entityCapacity,
            ClientReplicationSchemaApplierRegistry appliers,
            ISpatialPartitionMembership spatialMembership,
            KnowledgeProjectionStore knowledge,
            Func<Entity> viewerResolver)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            if (entityCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityCapacity));
            }

            _appliers = appliers ?? throw new ArgumentNullException(nameof(appliers));
            _spatialMembership = spatialMembership ?? throw new ArgumentNullException(nameof(spatialMembership));
            _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
            _viewerResolver = viewerResolver ?? throw new ArgumentNullException(nameof(viewerResolver));
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

            Entity viewer = _viewerResolver();
            if (viewer == Entity.Null || !_world.IsAlive(viewer))
            {
                throw new InvalidOperationException("Client replication bridge requires a live local player representative after handshake.");
            }

            return new ClientWorldReplicationBridge(
                _world,
                _entityCapacity,
                sessionEpoch,
                _appliers,
                _spatialMembership,
                _knowledge,
                viewer);
        }
    }
}
