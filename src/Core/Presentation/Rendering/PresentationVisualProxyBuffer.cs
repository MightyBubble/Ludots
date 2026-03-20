using System;

namespace Ludots.Core.Presentation.Rendering
{
    public sealed class PresentationVisualProxyBuffer
    {
        private readonly PresentationVisualProxy[] _buffer;
        private int _count;

        public int Count => _count;
        public int Capacity => _buffer.Length;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }

        public PresentationVisualProxyBuffer(int capacity = 8192)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new PresentationVisualProxy[capacity];
        }

        public bool TryAdd(in PresentationVisualProxy proxy)
        {
            if (_count >= _buffer.Length)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            _buffer[_count++] = proxy;
            return true;
        }

        public ReadOnlySpan<PresentationVisualProxy> GetSpan() => new ReadOnlySpan<PresentationVisualProxy>(_buffer, 0, _count);

        public void Clear()
        {
            _count = 0;
            DroppedSinceClear = 0;
        }
    }
}
