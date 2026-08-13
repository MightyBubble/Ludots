using System;

namespace Ludots.Core.Presentation.Presenters
{
    public sealed class PresenterCommandBuffer
    {
        private readonly PresenterCommand[] _buffer;
        private int _count;

        public int Count => _count;
        public int Capacity => _buffer.Length;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }

        public PresenterCommandBuffer(int capacity = 8192)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _buffer = new PresenterCommand[capacity];
        }

        public bool TryAdd(in PresenterCommand command)
        {
            if (_count >= _buffer.Length)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            _buffer[_count++] = command;
            return true;
        }

        public ReadOnlySpan<PresenterCommand> GetSpan() => new(_buffer, 0, _count);

        public void Clear()
        {
            _count = 0;
            DroppedSinceClear = 0;
        }
    }
}
