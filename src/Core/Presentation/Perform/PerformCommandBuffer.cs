using System;

namespace Ludots.Core.Presentation.Perform
{
    /// <summary>
    /// Domain command buffer for perform orchestration.
    /// Unlike PresentationCommandBuffer, this buffer belongs to the performer domain,
    /// not the broader presentation transport layer.
    /// </summary>
    public sealed class PerformCommandBuffer
    {
        private readonly PerformCommand[] _buffer;
        private int _count;

        public int Count => _count;
        public int Capacity => _buffer.Length;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }

        public PerformCommandBuffer(int capacity = 8192)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new PerformCommand[capacity];
        }

        public bool TryAdd(in PerformCommand command)
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

        public ReadOnlySpan<PerformCommand> GetSpan() => new ReadOnlySpan<PerformCommand>(_buffer, 0, _count);

        public void Clear()
        {
            _count = 0;
            DroppedSinceClear = 0;
        }
    }
}
