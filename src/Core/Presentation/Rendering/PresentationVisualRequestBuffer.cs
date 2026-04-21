using System;

namespace Ludots.Core.Presentation.Rendering
{
    public sealed class PresentationVisualRequestBuffer
    {
        private readonly PresentationVisualRequest[] _buffer;
        private int _count;

        public PresentationVisualRequestBuffer(int capacity = 4096)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new PresentationVisualRequest[capacity];
        }

        public int Count => _count;

        public int Capacity => _buffer.Length;

        public int DroppedSinceClear { get; private set; }

        public int DroppedTotal { get; private set; }

        public bool TryAdd(in PresentationVisualRequest request)
        {
            if (_count >= _buffer.Length)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            _buffer[_count++] = request;
            return true;
        }

        public ReadOnlySpan<PresentationVisualRequest> GetSpan()
            => new ReadOnlySpan<PresentationVisualRequest>(_buffer, 0, _count);

        public void Clear()
        {
            _count = 0;
            DroppedSinceClear = 0;
        }
    }
}
