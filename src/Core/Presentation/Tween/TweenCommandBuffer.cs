using System;

namespace Ludots.Core.Presentation.Tween
{
    public sealed class TweenCommandBuffer
    {
        private readonly TweenCommand[] _buffer;
        private int _count;

        public int Count => _count;
        public int Capacity => _buffer.Length;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }

        public TweenCommandBuffer(int capacity = 512)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new TweenCommand[capacity];
        }

        public bool TryAdd(in TweenCommand command)
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

        public ReadOnlySpan<TweenCommand> GetSpan() => new(_buffer, 0, _count);

        public void Clear()
        {
            _count = 0;
            DroppedSinceClear = 0;
        }
    }
}
