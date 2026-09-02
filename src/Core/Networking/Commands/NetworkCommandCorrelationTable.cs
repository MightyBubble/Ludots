using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Networking.Commands
{
    public readonly struct NetworkCommandCorrelationContext
    {
        public NetworkCommandCorrelationContext(
            int admissionBatchId,
            int seatSlot,
            uint seatGeneration,
            int playerId,
            ulong clientBatchSequence,
            int targetTick,
            int actorCount,
            bool deliver,
            ushort terminalCount)
        {
            AdmissionBatchId = admissionBatchId;
            SeatSlot = seatSlot;
            SeatGeneration = seatGeneration;
            PlayerId = playerId;
            ClientBatchSequence = clientBatchSequence;
            TargetTick = targetTick;
            ActorCount = actorCount;
            Deliver = deliver;
            TerminalCount = terminalCount;
        }

        public int AdmissionBatchId { get; }
        public int SeatSlot { get; }
        public uint SeatGeneration { get; }
        public int PlayerId { get; }
        public ulong ClientBatchSequence { get; }
        public int TargetTick { get; }
        public int ActorCount { get; }
        public bool Deliver { get; }
        public ushort TerminalCount { get; }
    }

    internal sealed class NetworkCommandCorrelationTable
    {
        private readonly bool[] _active;
        private readonly bool[] _deliver;
        private readonly int[] _admissionBatchIds;
        private readonly int[] _rowOrderIds;
        private readonly int[] _seatSlots;
        private readonly uint[] _seatGenerations;
        private readonly int[] _playerIds;
        private readonly ulong[] _sequences;
        private readonly int[] _targetTicks;
        private readonly int[] _actorCounts;
        private readonly ushort[] _terminalCounts;
        private readonly Entity[] _actors;
        private readonly ushort[] _batchIndices;
        private readonly byte[] _rowStates;
        private readonly int _maxActorsPerBatch;
        private int _searchStart;

        public NetworkCommandCorrelationTable(int capacity, int maxActorsPerBatch)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Correlation capacity must be positive.");
            }

            if (maxActorsPerBatch <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxActorsPerBatch));
            }

            _maxActorsPerBatch = maxActorsPerBatch;
            _active = new bool[capacity];
            _deliver = new bool[capacity];
            _admissionBatchIds = new int[capacity];
            _rowOrderIds = new int[checked(capacity * maxActorsPerBatch)];
            _seatSlots = new int[capacity];
            _seatGenerations = new uint[capacity];
            _playerIds = new int[capacity];
            _sequences = new ulong[capacity];
            _targetTicks = new int[capacity];
            _actorCounts = new int[capacity];
            _terminalCounts = new ushort[capacity];
            _actors = new Entity[checked(capacity * maxActorsPerBatch)];
            _batchIndices = new ushort[_actors.Length];
            _rowStates = new byte[_actors.Length];
        }

        public int Capacity => _active.Length;

        public void EnsureCanRegisterBatch(int actorCount)
        {
            ValidateActorCount(actorCount);
            for (int i = 0; i < _active.Length; i++)
            {
                if (!_active[i])
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                $"Network command correlation capacity {Capacity} is exhausted.");
        }

        public void RegisterBatch(
            in NetworkCommandSeat seat,
            ulong clientBatchSequence,
            int targetTick,
            ReadOnlySpan<Order> orders)
        {
            if (orders.IsEmpty)
            {
                throw new InvalidOperationException("Network command correlation requires a non-empty shared batch.");
            }

            ValidateActorCount(orders.Length);

            int admissionBatchId = orders[0].AdmissionBatchId;
            if (admissionBatchId <= 0)
            {
                throw new InvalidOperationException(
                    "Network command correlation requires a positive admission batch id.");
            }

            for (int i = 0; i < orders.Length; i++)
            {
                ref readonly Order order = ref orders[i];
                if (order.OrderId <= 0 ||
                    order.AdmissionBatchId != admissionBatchId ||
                    order.AdmissionBatchSize != orders.Length ||
                    order.AdmissionBatchIndex != i ||
                    order.PlayerId != seat.PlayerId ||
                    order.Actor == Entity.Null)
                {
                    throw new InvalidOperationException(
                        $"Network command correlation batch {admissionBatchId} has invalid row identity at index {i}.");
                }
            }

            int existing = FindByAdmissionBatchId(admissionBatchId);
            if (existing >= 0)
            {
                ValidateExisting(existing, in seat, clientBatchSequence, orders.Length);
                return;
            }

            int slot = FindFree();
            if (slot < 0)
            {
                throw new InvalidOperationException(
                    $"Network command correlation capacity {Capacity} is exhausted for admission batch {admissionBatchId}.");
            }

            _active[slot] = true;
            _deliver[slot] = true;
            _admissionBatchIds[slot] = admissionBatchId;
            _seatSlots[slot] = seat.Slot;
            _seatGenerations[slot] = seat.Generation;
            _playerIds[slot] = seat.PlayerId;
            _sequences[slot] = clientBatchSequence;
            _targetTicks[slot] = targetTick;
            _actorCounts[slot] = orders.Length;
            _terminalCounts[slot] = 0;
            int rowOffset = slot * _maxActorsPerBatch;
            Array.Clear(_rowStates, rowOffset, _maxActorsPerBatch);
            for (int i = 0; i < orders.Length; i++)
            {
                int row = rowOffset + i;
                _actors[row] = orders[i].Actor;
                _batchIndices[row] = orders[i].AdmissionBatchIndex;
                _rowOrderIds[row] = orders[i].OrderId;
            }
        }

        public bool TryFindByAdmissionBatchId(int admissionBatchId, out int tableIndex, out NetworkCommandCorrelationContext context)
        {
            tableIndex = FindByAdmissionBatchId(admissionBatchId);
            if (tableIndex < 0)
            {
                context = default;
                return false;
            }

            context = CreateContext(tableIndex);
            return true;
        }

        public bool TryFindByOrderIdAndBatchIndex(int orderId, ushort admissionBatchIndex, out int tableIndex, out NetworkCommandCorrelationContext context)
        {
            for (int i = 0; i < _active.Length; i++)
            {
                if (!_active[i])
                {
                    continue;
                }

                if (admissionBatchIndex >= _actorCounts[i])
                {
                    continue;
                }

                int row = (i * _maxActorsPerBatch) + admissionBatchIndex;
                if (_batchIndices[row] != admissionBatchIndex || _rowOrderIds[row] != orderId)
                {
                    continue;
                }

                tableIndex = i;
                context = CreateContext(i);
                return true;
            }

            tableIndex = -1;
            context = default;
            return false;
        }

        public bool TryFindByOrderIdAndActor(int orderId, Entity actor, out int tableIndex, out ushort admissionBatchIndex, out NetworkCommandCorrelationContext context)
        {
            for (int i = 0; i < _active.Length; i++)
            {
                if (!_active[i])
                {
                    continue;
                }

                int rowOffset = i * _maxActorsPerBatch;
                for (int row = 0; row < _actorCounts[i]; row++)
                {
                    int index = rowOffset + row;
                    if (_rowOrderIds[index] == orderId && _actors[index] == actor)
                    {
                        tableIndex = i;
                        admissionBatchIndex = _batchIndices[index];
                        context = CreateContext(i);
                        return true;
                    }
                }
            }

            tableIndex = -1;
            admissionBatchIndex = 0;
            context = default;
            return false;
        }

        public byte GetRowState(int tableIndex, int admissionBatchIndex)
        {
            ValidateRow(tableIndex, admissionBatchIndex);
            return _rowStates[checked((tableIndex * _maxActorsPerBatch) + admissionBatchIndex)];
        }

        public void SetRowState(int tableIndex, int admissionBatchIndex, byte state)
        {
            ValidateRow(tableIndex, admissionBatchIndex);
            if (state is not 1 and not 2)
            {
                throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown correlation row state.");
            }

            _rowStates[checked((tableIndex * _maxActorsPerBatch) + admissionBatchIndex)] = state;
        }

        public void IncrementTerminalCount(int tableIndex)
        {
            ValidateTableIndex(tableIndex);
            if (_terminalCounts[tableIndex] >= _actorCounts[tableIndex])
            {
                throw new InvalidOperationException("Network command correlation terminal count exceeded its actor count.");
            }

            _terminalCounts[tableIndex]++;
        }

        public ushort GetTerminalCount(int tableIndex) => _terminalCounts[tableIndex];

        public bool GetDeliver(int tableIndex) => _deliver[tableIndex];

        public void Clear(int tableIndex)
        {
            ValidateTableIndex(tableIndex);
            _active[tableIndex] = false;
            _deliver[tableIndex] = false;
            _admissionBatchIds[tableIndex] = 0;
            _actorCounts[tableIndex] = 0;
            _terminalCounts[tableIndex] = 0;
            Array.Clear(_rowOrderIds, tableIndex * _maxActorsPerBatch, _maxActorsPerBatch);
            Array.Clear(_rowStates, tableIndex * _maxActorsPerBatch, _maxActorsPerBatch);
        }

        public void AbandonDeliveryForSeat(int seatSlot, uint seatGeneration)
        {
            for (int i = 0; i < _active.Length; i++)
            {
                if (_active[i] &&
                    _seatSlots[i] == seatSlot &&
                    _seatGenerations[i] == seatGeneration)
                {
                    _deliver[i] = false;
                }
            }
        }

        private NetworkCommandCorrelationContext CreateContext(int tableIndex) =>
            new(
                _admissionBatchIds[tableIndex],
                _seatSlots[tableIndex],
                _seatGenerations[tableIndex],
                _playerIds[tableIndex],
                _sequences[tableIndex],
                _targetTicks[tableIndex],
                _actorCounts[tableIndex],
                _deliver[tableIndex],
                _terminalCounts[tableIndex]);

        private void ValidateExisting(
            int tableIndex,
            in NetworkCommandSeat seat,
            ulong clientBatchSequence,
            int actorCount)
        {
            if (_seatSlots[tableIndex] != seat.Slot ||
                _seatGenerations[tableIndex] != seat.Generation ||
                _sequences[tableIndex] != clientBatchSequence ||
                _actorCounts[tableIndex] != actorCount)
            {
                throw new InvalidOperationException("Network command correlation identity mismatch.");
            }
        }

        private int FindByAdmissionBatchId(int admissionBatchId)
        {
            for (int i = 0; i < _active.Length; i++)
            {
                if (_active[i] && _admissionBatchIds[i] == admissionBatchId)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindFree()
        {
            for (int offset = 0; offset < _active.Length; offset++)
            {
                int index = (_searchStart + offset) % _active.Length;
                if (_active[index])
                {
                    continue;
                }

                _searchStart = (index + 1) % _active.Length;
                return index;
            }

            return -1;
        }

        private void ValidateActorCount(int actorCount)
        {
            if (actorCount <= 0 || actorCount > _maxActorsPerBatch)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(actorCount),
                    actorCount,
                    $"Correlation actor count must be between 1 and {_maxActorsPerBatch}.");
            }
        }

        private void ValidateTableIndex(int tableIndex)
        {
            if ((uint)tableIndex >= (uint)_active.Length || !_active[tableIndex])
            {
                throw new ArgumentOutOfRangeException(nameof(tableIndex), tableIndex, "Correlation slot is not active.");
            }
        }

        private void ValidateRow(int tableIndex, int admissionBatchIndex)
        {
            ValidateTableIndex(tableIndex);
            if ((uint)admissionBatchIndex >= (uint)_actorCounts[tableIndex])
            {
                throw new ArgumentOutOfRangeException(
                    nameof(admissionBatchIndex),
                    admissionBatchIndex,
                    "Correlation row is outside the batch actor count.");
            }
        }
    }
}
