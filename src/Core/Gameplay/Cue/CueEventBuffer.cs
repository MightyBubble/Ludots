using System;

namespace Ludots.Core.Gameplay.Cue
{
    public sealed class CueEventBuffer
    {
        private readonly CueDescriptor[] _buffer;
        private int _count;

        public int Count => _count;
        public int Capacity => _buffer.Length;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }

        public CueEventBuffer(int capacity = 1024)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new CueDescriptor[capacity];
        }

        public bool TryAdd(in CueDescriptor cue)
        {
            if (_count >= _buffer.Length)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            _buffer[_count++] = cue;
            return true;
        }

        public ReadOnlySpan<CueDescriptor> GetSpan() => new ReadOnlySpan<CueDescriptor>(_buffer, 0, _count);

        public void Clear()
        {
            _count = 0;
            DroppedSinceClear = 0;
        }
    }
}
