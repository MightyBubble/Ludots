using Arch.Core;

namespace Ludots.Core.GraphRuntime
{
    public enum GraphDebugTraceMode : byte
    {
        Disabled = 0,
        Node = 1,
        NodeAndPins = 2,
    }

    public enum GraphDebugTraceEvent : byte
    {
        NodeEnter = 0,
        NodeExit = 1,
        Suspended = 2,
        Halted = 3,
        PinInt = 4,
        PinFloat = 5,
        PinBool = 6,
        PinEntity = 7,
        BlackboardInt = 8,
        BlackboardFloat = 9,
        BlackboardEntity = 10,
    }

    public readonly struct GraphDebugTraceRecord
    {
        public GraphDebugTraceRecord(
            long sequence,
            int graphId,
            GraphDebugTraceEvent eventKind,
            int sourcePc,
            int cursorPc,
            int steps,
            int registerIndex,
            int intValue,
            float floatValue,
            Entity entityValue)
        {
            Sequence = sequence;
            GraphId = graphId;
            EventKind = eventKind;
            SourcePc = sourcePc;
            CursorPc = cursorPc;
            Steps = steps;
            RegisterIndex = registerIndex;
            IntValue = intValue;
            FloatValue = floatValue;
            EntityValue = entityValue;
        }

        public long Sequence { get; }
        public int GraphId { get; }
        public GraphDebugTraceEvent EventKind { get; }
        public int SourcePc { get; }
        public int CursorPc { get; }
        public int Steps { get; }
        public int RegisterIndex { get; }
        public int IntValue { get; }
        public float FloatValue { get; }
        public Entity EntityValue { get; }
    }

    /// <summary>
    /// Fixed-capacity, opt-in execution trace for one mounted graph entry.
    /// The producer never allocates or formats data; consumers drain by sequence.
    /// Nested InvokeScript shares the ring and attributes each record with its graph id.
    /// NodeExit is reserved and not emitted by the current producer contract.
    /// </summary>
    public sealed class GraphDebugTrace
    {
        public const int DefaultCapacity = 2048;

        private readonly GraphDebugTraceRecord[] _records;
        private int _head;
        private int _count;
        private long _nextSequence;
        private long _dropped;

        public GraphDebugTrace(int capacity = DefaultCapacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _records = new GraphDebugTraceRecord[capacity];
        }

        public GraphDebugTraceMode Mode { get; private set; }
        public long DroppedCount => _dropped;
        public long LatestSequence => _nextSequence;
        public int Capacity => _records.Length;

        public void Configure(GraphDebugTraceMode mode)
        {
            if (!Enum.IsDefined(mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            Mode = mode;
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
            _nextSequence = 0;
            _dropped = 0;
        }

        public void RecordNode(int graphId, int sourcePc, int cursorPc, int steps, GraphDebugTraceEvent eventKind)
        {
            if (Mode == GraphDebugTraceMode.Disabled)
            {
                return;
            }

            Write(graphId, eventKind, sourcePc, cursorPc, steps, -1, 0, 0f, default);
        }

        public void RecordIntPin(int graphId, int sourcePc, int registerIndex, int value, int cursorPc, int steps)
        {
            if (Mode != GraphDebugTraceMode.NodeAndPins)
            {
                return;
            }

            Write(graphId, GraphDebugTraceEvent.PinInt, sourcePc, cursorPc, steps, registerIndex, value, 0f, default);
        }

        public void RecordFloatPin(int graphId, int sourcePc, int registerIndex, float value, int cursorPc, int steps)
        {
            if (Mode != GraphDebugTraceMode.NodeAndPins)
            {
                return;
            }

            Write(graphId, GraphDebugTraceEvent.PinFloat, sourcePc, cursorPc, steps, registerIndex, 0, value, default);
        }

        public void RecordBoolPin(int graphId, int sourcePc, int registerIndex, bool value, int cursorPc, int steps)
        {
            if (Mode != GraphDebugTraceMode.NodeAndPins)
            {
                return;
            }

            Write(graphId, GraphDebugTraceEvent.PinBool, sourcePc, cursorPc, steps, registerIndex, value ? 1 : 0, 0f, default);
        }

        public void RecordEntityPin(int graphId, int sourcePc, int registerIndex, Entity value, int cursorPc, int steps)
        {
            if (Mode != GraphDebugTraceMode.NodeAndPins)
            {
                return;
            }

            Write(graphId, GraphDebugTraceEvent.PinEntity, sourcePc, cursorPc, steps, registerIndex, 0, 0f, value);
        }

        public void RecordBlackboardInt(int graphId, int sourcePc, int keyId, int value, int cursorPc, int steps)
        {
            if (Mode != GraphDebugTraceMode.NodeAndPins) return;
            Write(graphId, GraphDebugTraceEvent.BlackboardInt, sourcePc, cursorPc, steps, keyId, value, 0f, default);
        }

        public void RecordBlackboardFloat(int graphId, int sourcePc, int keyId, float value, int cursorPc, int steps)
        {
            if (Mode != GraphDebugTraceMode.NodeAndPins) return;
            Write(graphId, GraphDebugTraceEvent.BlackboardFloat, sourcePc, cursorPc, steps, keyId, 0, value, default);
        }

        public void RecordBlackboardEntity(int graphId, int sourcePc, int keyId, Entity value, int cursorPc, int steps)
        {
            if (Mode != GraphDebugTraceMode.NodeAndPins) return;
            Write(graphId, GraphDebugTraceEvent.BlackboardEntity, sourcePc, cursorPc, steps, keyId, 0, 0f, value);
        }

        public int ReadSince(long since, Span<GraphDebugTraceRecord> destination, out long oldestSequence)
        {
            oldestSequence = _count == 0 ? _nextSequence + 1 : _records[_head].Sequence;
            int copied = 0;
            for (int i = 0; i < _count && copied < destination.Length; i++)
            {
                GraphDebugTraceRecord record = _records[(_head + i) % _records.Length];
                if (record.Sequence <= since)
                {
                    continue;
                }

                destination[copied++] = record;
            }

            return copied;
        }

        private void Write(
            int graphId,
            GraphDebugTraceEvent eventKind,
            int sourcePc,
            int cursorPc,
            int steps,
            int registerIndex,
            int intValue,
            float floatValue,
            Entity entityValue)
        {
            long sequence = ++_nextSequence;
            int index;
            if (_count < _records.Length)
            {
                index = (_head + _count) % _records.Length;
                _count++;
            }
            else
            {
                index = _head;
                _head = (_head + 1) % _records.Length;
                _dropped++;
            }

            _records[index] = new GraphDebugTraceRecord(
                sequence,
                graphId,
                eventKind,
                sourcePc,
                cursorPc,
                steps,
                registerIndex,
                intValue,
                floatValue,
                entityValue);
        }
    }
}
