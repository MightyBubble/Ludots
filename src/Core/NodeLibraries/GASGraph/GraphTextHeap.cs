using System;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// Fixed-capacity UTF-16 text bank for graph Text registers. Thread-local reuse;
    /// hot-path ops only copy within this heap (no string alloc). Nested InvokeScript /
    /// InvokeGraph push a depth frame so child slot indices do not clobber the parent.
    /// Producers Write before consumers Read; Bind/Execute do not clear the root frame
    /// (HFSM/script micro-evals must stay 0Alloc and avoid Array.Clear every tick).
    /// </summary>
    public sealed class GraphTextHeap
    {
        public const string OverflowError = "GAS.GRAPH.ERR.TextRegisterOverflow";
        public const string SlotError = "GAS.GRAPH.ERR.TextRegisterSlot";
        public const string DepthError = "GAS.GRAPH.ERR.TextHeapDepth";

        private static readonly int FrameStride =
            GraphVmLimits.MaxTextRegisters * GraphVmLimits.MaxTextCharsPerRegister;

        private static readonly int MaxFrames = GraphVmLimits.MaxInvokeDepth + 1;

        [ThreadStatic]
        private static GraphTextHeap? t_current;

        private readonly char[] _chars;
        private readonly ushort[] _lengths;
        private int _frame;

        public GraphTextHeap()
        {
            _chars = new char[MaxFrames * FrameStride];
            _lengths = new ushort[MaxFrames * GraphVmLimits.MaxTextRegisters];
        }

        public int SlotCount => GraphVmLimits.MaxTextRegisters;

        public int CharsPerSlot => GraphVmLimits.MaxTextCharsPerRegister;

        public int Frame => _frame;

        public static GraphTextHeap ForCurrentThread()
        {
            t_current ??= new GraphTextHeap();
            return t_current;
        }

        public static GraphTextHeap ForCurrentThreadCleared()
        {
            GraphTextHeap heap = ForCurrentThread();
            heap.ResetToRoot();
            return heap;
        }

        public void ResetToRoot()
        {
            _frame = 0;
            Array.Clear(_lengths, 0, _lengths.Length);
        }

        public void PushFrame()
        {
            if (_frame + 1 >= MaxFrames)
            {
                throw new InvalidOperationException(
                    $"{DepthError}: text heap frame {_frame + 1} exceeds MaxInvokeDepth+1.");
            }

            _frame++;
            int lengthOffset = _frame * GraphVmLimits.MaxTextRegisters;
            Array.Clear(_lengths, lengthOffset, GraphVmLimits.MaxTextRegisters);
        }

        public void PopFrame()
        {
            if (_frame <= 0)
            {
                throw new InvalidOperationException($"{DepthError}: cannot pop text heap root frame.");
            }

            int lengthOffset = _frame * GraphVmLimits.MaxTextRegisters;
            Array.Clear(_lengths, lengthOffset, GraphVmLimits.MaxTextRegisters);
            _frame--;
        }

        public ReadOnlySpan<char> Get(byte slot)
        {
            RequireSlot(slot);
            return _chars.AsSpan(CharOffset(slot), _lengths[LengthIndex(slot)]);
        }

        public void Write(byte slot, ReadOnlySpan<char> source)
        {
            RequireSlot(slot);
            if (source.Length > GraphVmLimits.MaxTextCharsPerRegister)
            {
                throw new InvalidOperationException(
                    $"{OverflowError}: text length {source.Length} exceeds MaxTextCharsPerRegister={GraphVmLimits.MaxTextCharsPerRegister}.");
            }

            source.CopyTo(_chars.AsSpan(CharOffset(slot), source.Length));
            _lengths[LengthIndex(slot)] = (ushort)source.Length;
        }

        public void Concat(byte dst, byte a, byte b)
        {
            RequireSlot(dst);
            RequireSlot(a);
            RequireSlot(b);
            int lenA = _lengths[LengthIndex(a)];
            int lenB = _lengths[LengthIndex(b)];
            int total = lenA + lenB;
            if (total > GraphVmLimits.MaxTextCharsPerRegister)
            {
                throw new InvalidOperationException(
                    $"{OverflowError}: concat length {total} exceeds MaxTextCharsPerRegister={GraphVmLimits.MaxTextCharsPerRegister}.");
            }

            Span<char> scratch = stackalloc char[GraphVmLimits.MaxTextCharsPerRegister];
            Get(a).CopyTo(scratch);
            Get(b).CopyTo(scratch.Slice(lenA));
            Write(dst, scratch.Slice(0, total));
        }

        private int CharOffset(byte slot)
            => (_frame * FrameStride) + (slot * GraphVmLimits.MaxTextCharsPerRegister);

        private int LengthIndex(byte slot)
            => (_frame * GraphVmLimits.MaxTextRegisters) + slot;

        private void RequireSlot(byte slot)
        {
            if (slot >= GraphVmLimits.MaxTextRegisters)
            {
                throw new InvalidOperationException(
                    $"{SlotError}: text register {slot} exceeds MaxTextRegisters={GraphVmLimits.MaxTextRegisters}.");
            }
        }
    }
}
