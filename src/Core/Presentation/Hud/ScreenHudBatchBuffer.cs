namespace Ludots.Core.Presentation.Hud
{
    /// <summary>
    /// Buffer of screen-space HUD items. Filled by WorldHudToScreenSystem; adapter draws directly.
    /// </summary>
    public sealed class ScreenHudBatchBuffer
    {
        private readonly ScreenHudItem[] _buffer;
        private int _count;

        public int Count => _count;
        public int Capacity => _buffer.Length;

        public ScreenHudBatchBuffer(int capacity = 16384)
        {
            if (capacity <= 0) throw new System.ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new ScreenHudItem[capacity];
        }

        public bool TryAdd(in ScreenHudItem item)
        {
            if (_count >= _buffer.Length)
                return false;

            _buffer[_count++] = item;
            return true;
        }

        public ReadOnlySpan<ScreenHudItem> GetSpan() => new ReadOnlySpan<ScreenHudItem>(_buffer, 0, _count);

        public void Clear()
        {
            _count = 0;
        }
    }
}
