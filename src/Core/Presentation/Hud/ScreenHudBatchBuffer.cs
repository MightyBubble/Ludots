namespace Ludots.Core.Presentation.Hud
{
    /// <summary>
    /// Buffer of screen-space HUD items. Filled by WorldHudToScreenSystem; adapter draws directly.
    /// </summary>
    public sealed class ScreenHudBatchBuffer
    {
        private readonly ScreenHudBarItem[] _bars;
        private readonly ScreenHudTextItem[] _texts;
        private readonly ScreenHudItem[] _flattened;
        private int _barCount;
        private int _textCount;
        private int _count;
        private bool _flattenedDirty;

        public int Count => _count;
        public int Capacity => _flattened.Length;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }
        public int BarCount => _barCount;
        public int TextCount => _textCount;

        public ScreenHudBatchBuffer(int capacity = 65536)
        {
            if (capacity <= 0) throw new System.ArgumentOutOfRangeException(nameof(capacity));
            _bars = new ScreenHudBarItem[capacity];
            _texts = new ScreenHudTextItem[capacity];
            _flattened = new ScreenHudItem[capacity];
        }

        public bool TryAdd(in ScreenHudItem item)
        {
            if (_count >= _flattened.Length)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            switch (item.Kind)
            {
                case WorldHudItemKind.Bar:
                    _bars[_barCount++] = new ScreenHudBarItem
                    {
                        StableId = item.StableId,
                        DirtySerial = item.DirtySerial,
                        ScreenX = item.ScreenX,
                        ScreenY = item.ScreenY,
                        Color0 = item.Color0,
                        Color1 = item.Color1,
                        Width = item.Width,
                        Height = item.Height,
                        Value0 = item.Value0,
                    };
                    break;

                case WorldHudItemKind.Text:
                    _texts[_textCount++] = new ScreenHudTextItem
                    {
                        StableId = item.StableId,
                        DirtySerial = item.DirtySerial,
                        ScreenX = item.ScreenX,
                        ScreenY = item.ScreenY,
                        Color0 = item.Color0,
                        Value0 = item.Value0,
                        Value1 = item.Value1,
                        Id0 = item.Id0,
                        Id1 = item.Id1,
                        FontSize = item.FontSize,
                        Text = item.Text,
                    };
                    break;

                default:
                    return true;
            }

            _count++;
            _flattenedDirty = true;
            return true;
        }

        public bool TryAddBar(in ScreenHudBarItem item)
        {
            if (_count >= _flattened.Length)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            _bars[_barCount++] = item;
            _count++;
            _flattenedDirty = true;
            return true;
        }

        public bool TryAddText(in ScreenHudTextItem item)
        {
            if (_count >= _flattened.Length)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            _texts[_textCount++] = item;
            _count++;
            _flattenedDirty = true;
            return true;
        }

        public ReadOnlySpan<ScreenHudBarItem> GetBarSpan() => new(_bars, 0, _barCount);

        public ReadOnlySpan<ScreenHudTextItem> GetTextSpan() => new(_texts, 0, _textCount);

        public ReadOnlySpan<ScreenHudItem> GetSpan()
        {
            if (_flattenedDirty)
            {
                RebuildFlattened();
            }

            return new ReadOnlySpan<ScreenHudItem>(_flattened, 0, _count);
        }

        public void Clear()
        {
            _barCount = 0;
            _textCount = 0;
            _count = 0;
            DroppedSinceClear = 0;
            _flattenedDirty = false;
        }

        private void RebuildFlattened()
        {
            int offset = 0;
            for (int i = 0; i < _barCount; i++)
            {
                ref readonly ScreenHudBarItem item = ref _bars[i];
                _flattened[offset++] = new ScreenHudItem
                {
                    StableId = item.StableId,
                    DirtySerial = item.DirtySerial,
                    Kind = WorldHudItemKind.Bar,
                    ScreenX = item.ScreenX,
                    ScreenY = item.ScreenY,
                    Color0 = item.Color0,
                    Color1 = item.Color1,
                    Width = item.Width,
                    Height = item.Height,
                    Value0 = item.Value0,
                };
            }

            for (int i = 0; i < _textCount; i++)
            {
                ref readonly ScreenHudTextItem item = ref _texts[i];
                _flattened[offset++] = new ScreenHudItem
                {
                    StableId = item.StableId,
                    DirtySerial = item.DirtySerial,
                    Kind = WorldHudItemKind.Text,
                    ScreenX = item.ScreenX,
                    ScreenY = item.ScreenY,
                    Color0 = item.Color0,
                    Value0 = item.Value0,
                    Value1 = item.Value1,
                    Id0 = item.Id0,
                    Id1 = item.Id1,
                    FontSize = item.FontSize,
                    Text = item.Text,
                };
            }

            _flattenedDirty = false;
        }
    }
}
