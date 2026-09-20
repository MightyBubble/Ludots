using System;
using Arch.Core;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.GraphRuntime
{
    public sealed class TriggerGraphExecutionSlotStore
    {
        private readonly int[] _free;
        private readonly ulong[] _generations;
        private readonly bool[] _occupied;
        private readonly float[] _floats;
        private readonly int[] _ints;
        private readonly byte[] _bools;
        private readonly Entity[] _entities;
        private readonly Entity[] _targets;
        private readonly int[] _callStack;
        private readonly float[] _previousFloats;
        private readonly int[] _previousInts;
        private readonly byte[] _previousBools;
        private readonly Entity[] _previousEntities;
        private readonly GraphEntryPayloadTable[] _payloads;
        private readonly GraphEntryPayloadTable[] _invokeArgs;
        private int _freeCount;

        public int Capacity => _free.Length;
        public int InUseCount => Capacity - _freeCount;
        public int HighWaterMark { get; private set; }

        public TriggerGraphExecutionSlotStore(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "GameConfig.triggerGraphExecutionCapacity must be positive.");

            _free = new int[capacity];
            _generations = new ulong[capacity];
            _occupied = new bool[capacity];
            _floats = new float[checked(capacity * GraphVmLimits.MaxFloatRegisters)];
            _ints = new int[checked(capacity * GraphVmLimits.MaxIntRegisters)];
            _bools = new byte[checked(capacity * GraphVmLimits.MaxBoolRegisters)];
            _entities = new Entity[checked(capacity * GraphVmLimits.MaxEntityRegisters)];
            _targets = new Entity[checked(capacity * GraphVmLimits.MaxTargets)];
            _callStack = new int[checked(capacity * GraphVmLimits.MaxCallStackDepth)];
            _previousFloats = new float[_floats.Length];
            _previousInts = new int[_ints.Length];
            _previousBools = new byte[_bools.Length];
            _previousEntities = new Entity[_entities.Length];
            _payloads = GraphEntryPayloadTable.CreateBatch(capacity);
            _invokeArgs = GraphEntryPayloadTable.CreateBatch(capacity);
            for (int i = 0; i < capacity; i++) _free[i] = capacity - 1 - i;
            _freeCount = capacity;
        }

        internal readonly record struct Handle(int Index, ulong Generation);

        internal Handle Rent()
        {
            if (_freeCount == 0)
                throw new InvalidOperationException($"GRAPH.EXECUTION.ERR.CapacityExceeded: all {Capacity} TriggerGraph execution slots are in use.");

            int index = _free[_freeCount - 1];
            ulong generation = checked(_generations[index] + 1);
            _freeCount--;
            _generations[index] = generation;
            _occupied[index] = true;
            HighWaterMark = Math.Max(HighWaterMark, InUseCount);
            return new Handle(index, generation);
        }

        internal void Return(Handle handle)
        {
            Reset(handle);
            _occupied[handle.Index] = false;
            _free[_freeCount++] = handle.Index;
        }

        internal void Reset(Handle handle)
        {
            Floats(handle).Clear();
            Ints(handle).Clear();
            Bools(handle).Clear();
            Entities(handle).Clear();
            Targets(handle).Clear();
            CallStack(handle).Clear();
            PreviousFloats(handle).Clear();
            PreviousInts(handle).Clear();
            PreviousBools(handle).Clear();
            PreviousEntities(handle).Clear();
            EntryPayload(handle).Clear();
            InvokeArgs(handle).Clear();
        }

        internal Span<float> Floats(Handle handle) => Lane(_floats, GraphVmLimits.MaxFloatRegisters, handle);
        internal Span<int> Ints(Handle handle) => Lane(_ints, GraphVmLimits.MaxIntRegisters, handle);
        internal Span<byte> Bools(Handle handle) => Lane(_bools, GraphVmLimits.MaxBoolRegisters, handle);
        internal Span<Entity> Entities(Handle handle) => Lane(_entities, GraphVmLimits.MaxEntityRegisters, handle);
        internal Span<Entity> Targets(Handle handle) => Lane(_targets, GraphVmLimits.MaxTargets, handle);
        internal Span<int> CallStack(Handle handle) => Lane(_callStack, GraphVmLimits.MaxCallStackDepth, handle);
        internal Span<float> PreviousFloats(Handle handle) => Lane(_previousFloats, GraphVmLimits.MaxFloatRegisters, handle);
        internal Span<int> PreviousInts(Handle handle) => Lane(_previousInts, GraphVmLimits.MaxIntRegisters, handle);
        internal Span<byte> PreviousBools(Handle handle) => Lane(_previousBools, GraphVmLimits.MaxBoolRegisters, handle);
        internal Span<Entity> PreviousEntities(Handle handle) => Lane(_previousEntities, GraphVmLimits.MaxEntityRegisters, handle);
        internal GraphEntryPayloadTable EntryPayload(Handle handle) => _payloads[Validate(handle)];
        internal GraphEntryPayloadTable InvokeArgs(Handle handle) => _invokeArgs[Validate(handle)];

        private Span<T> Lane<T>(T[] values, int width, Handle handle)
            => values.AsSpan(Validate(handle) * width, width);

        private int Validate(Handle handle)
        {
            if ((uint)handle.Index >= (uint)Capacity || !_occupied[handle.Index] ||
                _generations[handle.Index] != handle.Generation)
                throw new InvalidOperationException("GRAPH.EXECUTION.ERR.StaleSlot: the execution slot is no longer owned by this run.");
            return handle.Index;
        }
    }
}
