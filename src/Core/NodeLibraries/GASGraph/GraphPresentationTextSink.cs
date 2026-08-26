using System;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public enum GraphPresentationTextSurface : byte
    {
        Subtitle = 0,
        Dialogue = 1,
    }

    /// <summary>
    /// Fixed ring that graph SinkPresentationText writes into. Dialogue / subtitle hosts
    /// drain this ring; capacity overflow fails closed.
    /// </summary>
    public sealed class GraphPresentationTextSink
    {
        public const string UnavailableError = "GAS.GRAPH.ERR.PresentationTextSinkUnavailable";
        public const string OverflowError = "GAS.GRAPH.ERR.PresentationTextSinkOverflow";
        public const string SurfaceError = "GAS.GRAPH.ERR.PresentationTextSurface";

        public const int Capacity = 8;
        public const int MaxChars = GraphVmLimits.MaxTextCharsPerRegister;

        private readonly char[] _chars = new char[Capacity * MaxChars];
        private readonly ushort[] _lengths = new ushort[Capacity];
        private readonly GraphPresentationTextSurface[] _surfaces = new GraphPresentationTextSurface[Capacity];
        private int _count;
        private int _head;

        public int Count => _count;

        public void Clear()
        {
            _count = 0;
            _head = 0;
            Array.Clear(_lengths, 0, _lengths.Length);
        }

        public void Push(GraphPresentationTextSurface surface, ReadOnlySpan<char> text)
        {
            if (surface is not (GraphPresentationTextSurface.Subtitle or GraphPresentationTextSurface.Dialogue))
            {
                throw new InvalidOperationException($"{SurfaceError}: unsupported surface '{surface}'.");
            }

            if (text.Length > MaxChars)
            {
                throw new InvalidOperationException(
                    $"{OverflowError}: text length {text.Length} exceeds sink MaxChars={MaxChars}.");
            }

            if (_count >= Capacity)
            {
                throw new InvalidOperationException(
                    $"{OverflowError}: presentation text sink is full (Capacity={Capacity}).");
            }

            int index = (_head + _count) % Capacity;
            text.CopyTo(_chars.AsSpan(index * MaxChars, text.Length));
            _lengths[index] = (ushort)text.Length;
            _surfaces[index] = surface;
            _count++;
        }

        public bool TryPeek(out GraphPresentationTextSurface surface, out ReadOnlySpan<char> text)
        {
            if (_count == 0)
            {
                surface = default;
                text = default;
                return false;
            }

            surface = _surfaces[_head];
            text = _chars.AsSpan(_head * MaxChars, _lengths[_head]);
            return true;
        }

        public bool TryDequeue(out GraphPresentationTextSurface surface, out string text)
        {
            if (!TryPeek(out surface, out ReadOnlySpan<char> span))
            {
                text = string.Empty;
                return false;
            }

            text = span.ToString();
            _head = (_head + 1) % Capacity;
            _count--;
            return true;
        }
    }
}
