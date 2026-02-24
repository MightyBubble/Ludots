using System;

namespace Ludots.Core.Presentation.Events
{
    public sealed class PresentationEventStream
    {
        private readonly PresentationEvent[] _buffer;
        private int _count;

        public int Count => _count;
        public int Capacity => _buffer.Length;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }

        public PresentationEventStream(int capacity = 8192)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new PresentationEvent[capacity];
        }

        public bool TryAdd(in PresentationEvent evt)
        {
            if (_count >= _buffer.Length)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            _buffer[_count++] = evt;
            return true;
        }

        public ReadOnlySpan<PresentationEvent> GetSpan() => new ReadOnlySpan<PresentationEvent>(_buffer, 0, _count);

        public void Clear()
        {
            _count = 0;
            DroppedSinceClear = 0;
        }
    }
}
