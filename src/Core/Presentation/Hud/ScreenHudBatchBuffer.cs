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
        private readonly ScreenHudBarItem[] _dirtyBars;
        private readonly ScreenHudTextItem[] _dirtyTexts;
        private readonly ScreenHudBarItem[] _positionOnlyBars;
        private readonly ScreenHudTextItem[] _positionOnlyTexts;
        private readonly int[] _removedStableIds;
        private const int PositionOnlySparseCopyLimit = 1024;
        private int _positionOnlyBarStart = int.MaxValue;
        private int _positionOnlyBarEnd = -1;
        private int _positionOnlyTextStart = int.MaxValue;
        private int _positionOnlyTextEnd = -1;
        private bool _positionOnlyBarRangeOnly;
        private bool _positionOnlyTextRangeOnly;
        private readonly int[] _barProjectedBuildStamps;
        private readonly int[] _textProjectedBuildStamps;
        private readonly System.Collections.Generic.Dictionary<int, int> _barIndexByStableId;
        private readonly System.Collections.Generic.Dictionary<int, int> _textIndexByStableId;
        private int _barCount;
        private int _textCount;
        private int _count;
        private int _dirtyBarCount;
        private int _dirtyTextCount;
        private int _positionOnlyBarCount;
        private int _positionOnlyTextCount;
        private int _removedStableIdCount;
        private bool _flattenedDirty;
        private bool _bulkProjectedBuildActive;
        private bool _bulkProjectedBuildChanged;
        private bool _projectedBuildRetained;
        private bool _projectedBuildMembershipChanged;
        private bool _stableIndexValid = true;
        private int _projectedBuildStamp;

        public int Count => _count;
        public int Capacity => _flattened.Length;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }
        public int BarCount => _barCount;
        public int TextCount => _textCount;
        public int ContentRevision { get; private set; }
        public bool RequiresFullRebuild { get; private set; }
        public bool HasPositionOnlyBarRange => _positionOnlyBarStart <= _positionOnlyBarEnd;
        public bool HasPositionOnlyTextRange => _positionOnlyTextStart <= _positionOnlyTextEnd;
        public int PositionOnlyBarStart => HasPositionOnlyBarRange ? _positionOnlyBarStart : 0;
        public int PositionOnlyBarCount => HasPositionOnlyBarRange ? (_positionOnlyBarEnd - _positionOnlyBarStart + 1) : 0;
        public int PositionOnlyTextStart => HasPositionOnlyTextRange ? _positionOnlyTextStart : 0;
        public int PositionOnlyTextCount => HasPositionOnlyTextRange ? (_positionOnlyTextEnd - _positionOnlyTextStart + 1) : 0;
        public bool PositionOnlyBarRangeOnly => _positionOnlyBarRangeOnly;
        public bool PositionOnlyTextRangeOnly => _positionOnlyTextRangeOnly;

        public ScreenHudBatchBuffer(int capacity = 65536)
        {
            if (capacity <= 0) throw new System.ArgumentOutOfRangeException(nameof(capacity));
            _bars = new ScreenHudBarItem[capacity];
            _texts = new ScreenHudTextItem[capacity];
            _flattened = new ScreenHudItem[capacity];
            _dirtyBars = new ScreenHudBarItem[capacity];
            _dirtyTexts = new ScreenHudTextItem[capacity];
            _positionOnlyBars = new ScreenHudBarItem[capacity];
            _positionOnlyTexts = new ScreenHudTextItem[capacity];
            _removedStableIds = new int[capacity];
            _barProjectedBuildStamps = new int[capacity];
            _textProjectedBuildStamps = new int[capacity];
            _barIndexByStableId = new System.Collections.Generic.Dictionary<int, int>(capacity);
            _textIndexByStableId = new System.Collections.Generic.Dictionary<int, int>(capacity);
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
                    ScreenHudBarItem bar = new()
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
                    _bars[_barCount++] = bar;
                    if (bar.StableId > 0)
                    {
                        SetStableIndex(_barIndexByStableId, bar.StableId, _barCount - 1);
                    }

                    break;

                case WorldHudItemKind.Text:
                    ScreenHudTextItem text = new()
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
                    _texts[_textCount++] = text;
                    if (text.StableId > 0)
                    {
                        SetStableIndex(_textIndexByStableId, text.StableId, _textCount - 1);
                    }

                    break;

                default:
                    return true;
            }

            _count++;
            _flattenedDirty = true;
            ContentRevision++;
            return true;
        }

        public bool TryAddBar(in ScreenHudBarItem item)
        {
            return TryAddBar(in item, trackStableIndex: true, trackDelta: true);
        }

        public bool TryAddProjectedBar(in ScreenHudBarItem item)
        {
            if (_bulkProjectedBuildActive && !_projectedBuildRetained)
            {
                return TryAddBar(in item, trackStableIndex: false, trackDelta: false, bumpRevision: false, markProjected: false);
            }

            return TryAddBar(in item, trackStableIndex: true, trackDelta: true, bumpRevision: !_bulkProjectedBuildActive, markProjected: true);
        }

        public bool TryUpsertProjectedBar(in ScreenHudBarItem item)
        {
            return TryUpsertBar(in item, bumpRevision: !_bulkProjectedBuildActive, markProjected: true);
        }

        public bool TryUpsertProjectedBar(in ScreenHudBarItem item, int preferredIndex)
        {
            return TryUpsertBar(in item, bumpRevision: !_bulkProjectedBuildActive, markProjected: true, preferredIndex);
        }

        public bool TryUpsertProjectedBarPosition(
            int preferredIndex,
            int stableId,
            int dirtySerial,
            float screenX,
            float screenY)
        {
            if (stableId > 0 && TryGetBarStableIndex(stableId, preferredIndex, out int index))
            {
                _barProjectedBuildStamps[index] = _projectedBuildStamp;
                ref ScreenHudBarItem current = ref _bars[index];
                if (current.DirtySerial == dirtySerial)
                {
                    if (current.ScreenX == screenX && current.ScreenY == screenY)
                    {
                        return true;
                    }

                    current.ScreenX = screenX;
                    current.ScreenY = screenY;
                    AddPositionOnlyBar(index, in current);
                    _flattenedDirty = true;
                    _bulkProjectedBuildChanged = _bulkProjectedBuildActive;
                    if (!_bulkProjectedBuildActive)
                    {
                        ContentRevision++;
                    }

                    return true;
                }
            }

            if (stableId > 0 && !_stableIndexValid)
            {
                EnsureStableIndex();
                if (TryGetBarStableIndex(stableId, preferredIndex: -1, out int resolvedIndex))
                {
                    _barProjectedBuildStamps[resolvedIndex] = _projectedBuildStamp;
                    ref ScreenHudBarItem current = ref _bars[resolvedIndex];
                    if (current.DirtySerial == dirtySerial)
                    {
                        if (current.ScreenX == screenX && current.ScreenY == screenY)
                        {
                            return true;
                        }

                        current.ScreenX = screenX;
                        current.ScreenY = screenY;
                        AddPositionOnlyBar(resolvedIndex, in current);
                        _flattenedDirty = true;
                        _bulkProjectedBuildChanged = _bulkProjectedBuildActive;
                        if (!_bulkProjectedBuildActive)
                        {
                            ContentRevision++;
                        }

                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryAddBar(in ScreenHudBarItem item, bool trackStableIndex, bool trackDelta)
        {
            return TryAddBar(in item, trackStableIndex, trackDelta, bumpRevision: true);
        }

        private bool TryAddBar(in ScreenHudBarItem item, bool trackStableIndex, bool trackDelta, bool bumpRevision)
        {
            return TryAddBar(in item, trackStableIndex, trackDelta, bumpRevision, markProjected: false);
        }

        private bool TryAddBar(in ScreenHudBarItem item, bool trackStableIndex, bool trackDelta, bool bumpRevision, bool markProjected)
        {
            if (_count >= _flattened.Length)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            int index = _barCount++;
            _bars[index] = item;
            if (_bulkProjectedBuildActive && _projectedBuildRetained)
            {
                _projectedBuildMembershipChanged = true;
            }

            if (trackStableIndex && item.StableId > 0)
            {
                EnsureStableIndex();
                SetStableIndex(_barIndexByStableId, item.StableId, index);
            }

            if (markProjected)
            {
                _barProjectedBuildStamps[index] = _projectedBuildStamp;
            }

            if (trackDelta)
            {
                AddDirtyBar(in item);
            }

            _count++;
            _flattenedDirty = true;
            if (bumpRevision)
            {
                ContentRevision++;
            }
            else
            {
                _bulkProjectedBuildChanged = true;
            }

            return true;
        }

        public bool TryAddText(in ScreenHudTextItem item)
        {
            return TryAddText(in item, trackStableIndex: true, trackDelta: true);
        }

        public bool TryAddProjectedText(in ScreenHudTextItem item)
        {
            if (_bulkProjectedBuildActive && !_projectedBuildRetained)
            {
                return TryAddText(in item, trackStableIndex: false, trackDelta: false, bumpRevision: false, markProjected: false);
            }

            return TryAddText(in item, trackStableIndex: true, trackDelta: true, bumpRevision: !_bulkProjectedBuildActive, markProjected: true);
        }

        public bool TryUpsertProjectedText(in ScreenHudTextItem item)
        {
            return TryUpsertText(in item, bumpRevision: !_bulkProjectedBuildActive, markProjected: true);
        }

        public bool TryUpsertProjectedText(in ScreenHudTextItem item, int preferredIndex)
        {
            return TryUpsertText(in item, bumpRevision: !_bulkProjectedBuildActive, markProjected: true, preferredIndex);
        }

        public bool TryUpsertProjectedTextPosition(
            int preferredIndex,
            int stableId,
            int dirtySerial,
            float screenX,
            float screenY)
        {
            if (stableId > 0 && TryGetTextStableIndex(stableId, preferredIndex, out int index))
            {
                _textProjectedBuildStamps[index] = _projectedBuildStamp;
                ref ScreenHudTextItem current = ref _texts[index];
                if (current.DirtySerial == dirtySerial)
                {
                    if (current.ScreenX == screenX && current.ScreenY == screenY)
                    {
                        return true;
                    }

                    current.ScreenX = screenX;
                    current.ScreenY = screenY;
                    AddPositionOnlyText(index, in current);
                    _flattenedDirty = true;
                    _bulkProjectedBuildChanged = _bulkProjectedBuildActive;
                    if (!_bulkProjectedBuildActive)
                    {
                        ContentRevision++;
                    }

                    return true;
                }
            }

            if (stableId > 0 && !_stableIndexValid)
            {
                EnsureStableIndex();
                if (TryGetTextStableIndex(stableId, preferredIndex: -1, out int resolvedIndex))
                {
                    _textProjectedBuildStamps[resolvedIndex] = _projectedBuildStamp;
                    ref ScreenHudTextItem current = ref _texts[resolvedIndex];
                    if (current.DirtySerial == dirtySerial)
                    {
                        if (current.ScreenX == screenX && current.ScreenY == screenY)
                        {
                            return true;
                        }

                        current.ScreenX = screenX;
                        current.ScreenY = screenY;
                        AddPositionOnlyText(resolvedIndex, in current);
                        _flattenedDirty = true;
                        _bulkProjectedBuildChanged = _bulkProjectedBuildActive;
                        if (!_bulkProjectedBuildActive)
                        {
                            ContentRevision++;
                        }

                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryAddText(in ScreenHudTextItem item, bool trackStableIndex, bool trackDelta)
        {
            return TryAddText(in item, trackStableIndex, trackDelta, bumpRevision: true);
        }

        private bool TryAddText(in ScreenHudTextItem item, bool trackStableIndex, bool trackDelta, bool bumpRevision)
        {
            return TryAddText(in item, trackStableIndex, trackDelta, bumpRevision, markProjected: false);
        }

        private bool TryAddText(in ScreenHudTextItem item, bool trackStableIndex, bool trackDelta, bool bumpRevision, bool markProjected)
        {
            if (_count >= _flattened.Length)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            int index = _textCount++;
            _texts[index] = item;
            if (_bulkProjectedBuildActive && _projectedBuildRetained)
            {
                _projectedBuildMembershipChanged = true;
            }

            if (trackStableIndex && item.StableId > 0)
            {
                EnsureStableIndex();
                SetStableIndex(_textIndexByStableId, item.StableId, index);
            }

            if (markProjected)
            {
                _textProjectedBuildStamps[index] = _projectedBuildStamp;
            }

            if (trackDelta)
            {
                AddDirtyText(in item);
            }

            _count++;
            _flattenedDirty = true;
            if (bumpRevision)
            {
                ContentRevision++;
            }
            else
            {
                _bulkProjectedBuildChanged = true;
            }

            return true;
        }

        public bool TryUpsertBar(in ScreenHudBarItem item)
        {
            return TryUpsertBar(in item, bumpRevision: true);
        }

        private bool TryUpsertBar(in ScreenHudBarItem item, bool bumpRevision)
        {
            return TryUpsertBar(in item, bumpRevision, markProjected: false);
        }

        private bool TryUpsertBar(in ScreenHudBarItem item, bool bumpRevision, bool markProjected)
        {
            return TryUpsertBar(in item, bumpRevision, markProjected, preferredIndex: -1);
        }

        private bool TryUpsertBar(in ScreenHudBarItem item, bool bumpRevision, bool markProjected, int preferredIndex)
        {
            EnsureStableIndex();
            if (item.StableId > 0 && TryGetBarStableIndex(item.StableId, preferredIndex, out int index))
            {
                if (markProjected)
                {
                    _barProjectedBuildStamps[index] = _projectedBuildStamp;
                }

                ref ScreenHudBarItem current = ref _bars[index];
                if (BarContentEquals(in current, in item))
                {
                    if (current.ScreenX == item.ScreenX && current.ScreenY == item.ScreenY)
                    {
                        return true;
                    }

                    current.ScreenX = item.ScreenX;
                    current.ScreenY = item.ScreenY;
                    AddPositionOnlyBar(index, in current);
                    _flattenedDirty = true;
                    if (bumpRevision)
                    {
                        ContentRevision++;
                    }
                    else
                    {
                        _bulkProjectedBuildChanged = true;
                    }

                    return true;
                }

                if (BarEquals(in _bars[index], in item))
                {
                    return true;
                }

                _bars[index] = item;
                AddDirtyBar(in item);

                _flattenedDirty = true;
                if (bumpRevision)
                {
                    ContentRevision++;
                }
                else
                {
                    _bulkProjectedBuildChanged = true;
                }
                return true;
            }

            return TryAddBar(in item, trackStableIndex: true, trackDelta: true, bumpRevision, markProjected);
        }

        public bool TryUpsertText(in ScreenHudTextItem item)
        {
            return TryUpsertText(in item, bumpRevision: true);
        }

        private bool TryUpsertText(in ScreenHudTextItem item, bool bumpRevision)
        {
            return TryUpsertText(in item, bumpRevision, markProjected: false);
        }

        private bool TryUpsertText(in ScreenHudTextItem item, bool bumpRevision, bool markProjected)
        {
            return TryUpsertText(in item, bumpRevision, markProjected, preferredIndex: -1);
        }

        private bool TryUpsertText(in ScreenHudTextItem item, bool bumpRevision, bool markProjected, int preferredIndex)
        {
            EnsureStableIndex();
            if (item.StableId > 0 && TryGetTextStableIndex(item.StableId, preferredIndex, out int index))
            {
                if (markProjected)
                {
                    _textProjectedBuildStamps[index] = _projectedBuildStamp;
                }

                ref ScreenHudTextItem current = ref _texts[index];
                if (TextContentEquals(in current, in item))
                {
                    if (current.ScreenX == item.ScreenX && current.ScreenY == item.ScreenY)
                    {
                        return true;
                    }

                    current.ScreenX = item.ScreenX;
                    current.ScreenY = item.ScreenY;
                    AddPositionOnlyText(index, in current);
                    _flattenedDirty = true;
                    if (bumpRevision)
                    {
                        ContentRevision++;
                    }
                    else
                    {
                        _bulkProjectedBuildChanged = true;
                    }

                    return true;
                }

                if (TextEquals(in _texts[index], in item))
                {
                    return true;
                }

                _texts[index] = item;
                AddDirtyText(in item);

                _flattenedDirty = true;
                if (bumpRevision)
                {
                    ContentRevision++;
                }
                else
                {
                    _bulkProjectedBuildChanged = true;
                }
                return true;
            }

            return TryAddText(in item, trackStableIndex: true, trackDelta: true, bumpRevision, markProjected);
        }

        public void Remove(int stableId)
        {
            Remove(stableId, bumpRevision: true);
        }

        public void RemoveProjected(int stableId)
        {
            Remove(stableId, bumpRevision: !_bulkProjectedBuildActive);
        }

        private void Remove(int stableId, bool bumpRevision)
        {
            if (stableId <= 0)
            {
                return;
            }

            bool removed = RemoveBar(stableId);
            removed |= RemoveText(stableId);
            if (removed)
            {
                AddRemovedStableId(stableId);
                _count = _barCount + _textCount;
                _flattenedDirty = true;
                if (bumpRevision)
                {
                    ContentRevision++;
                }
                else
                {
                    _bulkProjectedBuildChanged = true;
                }
            }
        }

        public ReadOnlySpan<ScreenHudBarItem> GetBarSpan() => new(_bars, 0, _barCount);

        public ReadOnlySpan<ScreenHudTextItem> GetTextSpan() => new(_texts, 0, _textCount);

        public ReadOnlySpan<ScreenHudBarItem> GetDirtyBarSpan() => new(_dirtyBars, 0, _dirtyBarCount);

        public ReadOnlySpan<ScreenHudTextItem> GetDirtyTextSpan() => new(_dirtyTexts, 0, _dirtyTextCount);

        public ReadOnlySpan<ScreenHudBarItem> GetPositionOnlyBarSpan() =>
            _positionOnlyBarRangeOnly ? ReadOnlySpan<ScreenHudBarItem>.Empty : new(_positionOnlyBars, 0, _positionOnlyBarCount);

        public ReadOnlySpan<ScreenHudTextItem> GetPositionOnlyTextSpan() =>
            _positionOnlyTextRangeOnly ? ReadOnlySpan<ScreenHudTextItem>.Empty : new(_positionOnlyTexts, 0, _positionOnlyTextCount);

        public ReadOnlySpan<int> GetRemovedStableIdSpan() => new(_removedStableIds, 0, _removedStableIdCount);

        public bool TryApplyWorldContentDelta(in WorldHudItem item)
        {
            EnsureStableIndex();
            if (item.Kind == WorldHudItemKind.Bar)
            {
                if (item.StableId <= 0 || !TryGetStableIndex(_barIndexByStableId, item.StableId, _barCount, out int index))
                {
                    return true;
                }

                ref ScreenHudBarItem current = ref _bars[index];
                return TryUpsertBar(new ScreenHudBarItem
                {
                    StableId = item.StableId,
                    DirtySerial = item.DirtySerial,
                    ScreenX = current.ScreenX,
                    ScreenY = current.ScreenY,
                    Color0 = item.Color0,
                    Color1 = item.Color1,
                    Width = item.Width,
                    Height = item.Height,
                    Value0 = item.Value0,
                });
            }

            if (item.Kind == WorldHudItemKind.Text)
            {
                if (item.StableId <= 0 || !TryGetStableIndex(_textIndexByStableId, item.StableId, _textCount, out int index))
                {
                    return true;
                }

                ref ScreenHudTextItem current = ref _texts[index];
                return TryUpsertText(new ScreenHudTextItem
                {
                    StableId = item.StableId,
                    DirtySerial = item.DirtySerial,
                    ScreenX = current.ScreenX,
                    ScreenY = current.ScreenY,
                    Color0 = item.Color0,
                    Value0 = item.Value0,
                    Value1 = item.Value1,
                    Id0 = item.Id0,
                    Id1 = item.Id1,
                    FontSize = item.FontSize,
                    Text = item.Text,
                });
            }

            return true;
        }

        public void ClearDeltas()
        {
            _dirtyBarCount = 0;
            _dirtyTextCount = 0;
            _positionOnlyBarCount = 0;
            _positionOnlyTextCount = 0;
            _removedStableIdCount = 0;
            _positionOnlyBarStart = int.MaxValue;
            _positionOnlyBarEnd = -1;
            _positionOnlyTextStart = int.MaxValue;
            _positionOnlyTextEnd = -1;
            _positionOnlyBarRangeOnly = false;
            _positionOnlyTextRangeOnly = false;
            RequiresFullRebuild = false;
        }

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
            _barIndexByStableId.Clear();
            _textIndexByStableId.Clear();
            _stableIndexValid = true;
            _dirtyBarCount = 0;
            _dirtyTextCount = 0;
            _positionOnlyBarCount = 0;
            _positionOnlyTextCount = 0;
            _removedStableIdCount = 0;
            _positionOnlyBarStart = int.MaxValue;
            _positionOnlyBarEnd = -1;
            _positionOnlyTextStart = int.MaxValue;
            _positionOnlyTextEnd = -1;
            _positionOnlyBarRangeOnly = false;
            _positionOnlyTextRangeOnly = false;
            RequiresFullRebuild = true;
            ContentRevision++;
        }

        public void BeginProjectedBuild()
        {
            BeginProjectedBuild(retained: true);
        }

        public void BeginProjectedBuild(bool retained)
        {
            _bulkProjectedBuildActive = true;
            _bulkProjectedBuildChanged = false;
            _projectedBuildRetained = retained;
            _projectedBuildMembershipChanged = false;
            if (!retained)
            {
                _barCount = 0;
                _textCount = 0;
                _count = 0;
                DroppedSinceClear = 0;
                _dirtyBarCount = 0;
                _dirtyTextCount = 0;
                _positionOnlyBarCount = 0;
                _positionOnlyTextCount = 0;
                _removedStableIdCount = 0;
                _positionOnlyBarStart = int.MaxValue;
                _positionOnlyBarEnd = -1;
                _positionOnlyTextStart = int.MaxValue;
                _positionOnlyTextEnd = -1;
                _positionOnlyBarRangeOnly = false;
                _positionOnlyTextRangeOnly = false;
                _flattenedDirty = false;
                _stableIndexValid = false;
                return;
            }

            _projectedBuildStamp++;
            if (_projectedBuildStamp == int.MaxValue)
            {
                Array.Clear(_barProjectedBuildStamps, 0, _barProjectedBuildStamps.Length);
                Array.Clear(_textProjectedBuildStamps, 0, _textProjectedBuildStamps.Length);
                _projectedBuildStamp = 1;
            }
        }

        public void EndProjectedBuild()
        {
            EndProjectedBuild(removeUnseenProjectedItems: false);
        }

        public void EndProjectedBuild(bool removeUnseenProjectedItems)
        {
            EndProjectedBuild(removeUnseenProjectedItems, projectedBarCount: -1, projectedTextCount: -1);
        }

        public void EndProjectedBuild(bool removeUnseenProjectedItems, int projectedBarCount, int projectedTextCount)
        {
            if (!_bulkProjectedBuildActive)
            {
                return;
            }

            if (!_projectedBuildRetained)
            {
                _bulkProjectedBuildActive = false;
                _projectedBuildRetained = true;
                RequiresFullRebuild = true;
                ContentRevision++;
                return;
            }

            if (removeUnseenProjectedItems)
            {
                bool sawEveryExistingItem =
                    projectedBarCount == _barCount &&
                    projectedTextCount == _textCount &&
                    !_projectedBuildMembershipChanged;
                if (!sawEveryExistingItem)
                {
                    RemoveUnseenProjectedItems();
                }
            }

            _bulkProjectedBuildActive = false;
            if (_bulkProjectedBuildChanged)
            {
                ContentRevision++;
            }
        }

        private void RemoveUnseenProjectedItems()
        {
            CompactProjectedBars();
            CompactProjectedTexts();

            _count = _barCount + _textCount;
            if (_bulkProjectedBuildChanged)
            {
                _flattenedDirty = true;
            }
        }

        private void CompactProjectedBars()
        {
            int firstUnseen = -1;
            for (int i = 0; i < _barCount; i++)
            {
                if (_barProjectedBuildStamps[i] != _projectedBuildStamp)
                {
                    firstUnseen = i;
                    break;
                }
            }

            if (firstUnseen < 0)
            {
                return;
            }

            int write = firstUnseen;
            for (int read = firstUnseen; read < _barCount; read++)
            {
                if (_barProjectedBuildStamps[read] != _projectedBuildStamp)
                {
                    int removedStableId = _bars[read].StableId;
                    if (removedStableId > 0)
                    {
                        AddRemovedStableId(removedStableId);
                        ClearStableIndex(_barIndexByStableId, removedStableId);
                    }

                    _bulkProjectedBuildChanged = true;
                    _flattenedDirty = true;
                    continue;
                }

                if (write != read)
                {
                    _bars[write] = _bars[read];
                    _barProjectedBuildStamps[write] = _barProjectedBuildStamps[read];
                    _flattenedDirty = true;
                }

                int movedStableId = _bars[write].StableId;
                if (movedStableId > 0)
                {
                    SetStableIndex(_barIndexByStableId, movedStableId, write);
                }

                write++;
            }

            for (int i = write; i < _barCount; i++)
            {
                _bars[i] = default;
                _barProjectedBuildStamps[i] = 0;
            }

            _barCount = write;
        }

        private void CompactProjectedTexts()
        {
            int firstUnseen = -1;
            for (int i = 0; i < _textCount; i++)
            {
                if (_textProjectedBuildStamps[i] != _projectedBuildStamp)
                {
                    firstUnseen = i;
                    break;
                }
            }

            if (firstUnseen < 0)
            {
                return;
            }

            int write = firstUnseen;
            for (int read = firstUnseen; read < _textCount; read++)
            {
                if (_textProjectedBuildStamps[read] != _projectedBuildStamp)
                {
                    int removedStableId = _texts[read].StableId;
                    if (removedStableId > 0)
                    {
                        AddRemovedStableId(removedStableId);
                        ClearStableIndex(_textIndexByStableId, removedStableId);
                    }

                    _bulkProjectedBuildChanged = true;
                    _flattenedDirty = true;
                    continue;
                }

                if (write != read)
                {
                    _texts[write] = _texts[read];
                    _textProjectedBuildStamps[write] = _textProjectedBuildStamps[read];
                    _flattenedDirty = true;
                }

                int movedStableId = _texts[write].StableId;
                if (movedStableId > 0)
                {
                    SetStableIndex(_textIndexByStableId, movedStableId, write);
                }

                write++;
            }

            for (int i = write; i < _textCount; i++)
            {
                _texts[i] = default;
                _textProjectedBuildStamps[i] = 0;
            }

            _textCount = write;
        }

        private void AddDirtyBar(in ScreenHudBarItem item)
        {
            if (_dirtyBarCount >= _dirtyBars.Length)
            {
                return;
            }

            _dirtyBars[_dirtyBarCount++] = item;
        }

        private void AddDirtyText(in ScreenHudTextItem item)
        {
            if (_dirtyTextCount >= _dirtyTexts.Length)
            {
                return;
            }

            _dirtyTexts[_dirtyTextCount++] = item;
        }

        private void AddPositionOnlyBar(int index, in ScreenHudBarItem item)
        {
            TrackPositionOnlyBarRange(index);
            if (_positionOnlyBarRangeOnly)
            {
                return;
            }

            if (_positionOnlyBarCount >= PositionOnlySparseCopyLimit)
            {
                _positionOnlyBarCount = 0;
                _positionOnlyBarRangeOnly = true;
                return;
            }

            if (_positionOnlyBarCount >= _positionOnlyBars.Length)
            {
                return;
            }

            _positionOnlyBars[_positionOnlyBarCount++] = item;
        }

        private void AddPositionOnlyText(int index, in ScreenHudTextItem item)
        {
            TrackPositionOnlyTextRange(index);
            if (_positionOnlyTextRangeOnly)
            {
                return;
            }

            if (_positionOnlyTextCount >= PositionOnlySparseCopyLimit)
            {
                _positionOnlyTextCount = 0;
                _positionOnlyTextRangeOnly = true;
                return;
            }

            if (_positionOnlyTextCount >= _positionOnlyTexts.Length)
            {
                return;
            }

            _positionOnlyTexts[_positionOnlyTextCount++] = item;
        }

        private void TrackPositionOnlyBarRange(int index)
        {
            if (index < 0)
            {
                return;
            }

            if (index < _positionOnlyBarStart)
            {
                _positionOnlyBarStart = index;
            }

            if (index > _positionOnlyBarEnd)
            {
                _positionOnlyBarEnd = index;
            }
        }

        private void TrackPositionOnlyTextRange(int index)
        {
            if (index < 0)
            {
                return;
            }

            if (index < _positionOnlyTextStart)
            {
                _positionOnlyTextStart = index;
            }

            if (index > _positionOnlyTextEnd)
            {
                _positionOnlyTextEnd = index;
            }
        }

        private void AddRemovedStableId(int stableId)
        {
            if (_removedStableIdCount >= _removedStableIds.Length)
            {
                return;
            }

            _removedStableIds[_removedStableIdCount++] = stableId;
        }

        private bool RemoveBar(int stableId)
        {
            EnsureStableIndex();
            if (!TryGetStableIndex(_barIndexByStableId, stableId, _barCount, out int index))
            {
                return false;
            }

            RemoveBarAt(index);
            return true;
        }

        private void RemoveBarAt(int index)
        {
            int removedStableId = _bars[index].StableId;
            int lastIndex = _barCount - 1;
            if (index != lastIndex)
            {
                ScreenHudBarItem moved = _bars[lastIndex];
                _bars[index] = moved;
                _barProjectedBuildStamps[index] = _barProjectedBuildStamps[lastIndex];
                if (moved.StableId > 0)
                {
                    SetStableIndex(_barIndexByStableId, moved.StableId, index);
                }
            }

            _barCount = lastIndex;
            if (removedStableId > 0)
            {
                ClearStableIndex(_barIndexByStableId, removedStableId);
            }

            _bars[lastIndex] = default;
            _barProjectedBuildStamps[lastIndex] = 0;
        }

        private bool RemoveText(int stableId)
        {
            EnsureStableIndex();
            if (!TryGetStableIndex(_textIndexByStableId, stableId, _textCount, out int index))
            {
                return false;
            }

            RemoveTextAt(index);
            return true;
        }

        private void RemoveTextAt(int index)
        {
            int removedStableId = _texts[index].StableId;
            int lastIndex = _textCount - 1;
            if (index != lastIndex)
            {
                ScreenHudTextItem moved = _texts[lastIndex];
                _texts[index] = moved;
                _textProjectedBuildStamps[index] = _textProjectedBuildStamps[lastIndex];
                if (moved.StableId > 0)
                {
                    SetStableIndex(_textIndexByStableId, moved.StableId, index);
                }
            }

            _textCount = lastIndex;
            if (removedStableId > 0)
            {
                ClearStableIndex(_textIndexByStableId, removedStableId);
            }

            _texts[lastIndex] = default;
            _textProjectedBuildStamps[lastIndex] = 0;
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

        private void EnsureStableIndex()
        {
            if (_stableIndexValid)
            {
                return;
            }

            _barIndexByStableId.Clear();

            for (int i = 0; i < _barCount; i++)
            {
                int stableId = _bars[i].StableId;
                if (stableId > 0)
                {
                    SetStableIndex(_barIndexByStableId, stableId, i);
                }
            }

            _textIndexByStableId.Clear();

            for (int i = 0; i < _textCount; i++)
            {
                int stableId = _texts[i].StableId;
                if (stableId > 0)
                {
                    SetStableIndex(_textIndexByStableId, stableId, i);
                }
            }

            _stableIndexValid = true;
        }

        private static bool TryGetStableIndex(System.Collections.Generic.Dictionary<int, int> indicesByStableId, int stableId, int itemCount, out int index)
        {
            if (indicesByStableId.TryGetValue(stableId, out int encodedIndex))
            {
                if (encodedIndex > 0)
                {
                    index = encodedIndex - 1;
                    return (uint)index < (uint)itemCount;
                }
            }

            index = -1;
            return false;
        }

        private bool TryGetBarStableIndex(int stableId, int preferredIndex, out int index)
        {
            if ((uint)preferredIndex < (uint)_barCount && _bars[preferredIndex].StableId == stableId)
            {
                index = preferredIndex;
                return true;
            }

            if (!_stableIndexValid)
            {
                index = -1;
                return false;
            }

            return TryGetStableIndex(_barIndexByStableId, stableId, _barCount, out index);
        }

        private bool TryGetTextStableIndex(int stableId, int preferredIndex, out int index)
        {
            if ((uint)preferredIndex < (uint)_textCount && _texts[preferredIndex].StableId == stableId)
            {
                index = preferredIndex;
                return true;
            }

            if (!_stableIndexValid)
            {
                index = -1;
                return false;
            }

            return TryGetStableIndex(_textIndexByStableId, stableId, _textCount, out index);
        }

        private static void SetStableIndex(System.Collections.Generic.Dictionary<int, int> indicesByStableId, int stableId, int index)
        {
            if (stableId <= 0)
            {
                return;
            }

            indicesByStableId[stableId] = index + 1;
        }

        private static void ClearStableIndex(System.Collections.Generic.Dictionary<int, int> indicesByStableId, int stableId)
        {
            if (stableId > 0)
            {
                indicesByStableId.Remove(stableId);
            }
        }

        private static bool BarEquals(in ScreenHudBarItem left, in ScreenHudBarItem right)
        {
            return left.StableId == right.StableId &&
                   left.DirtySerial == right.DirtySerial &&
                   left.ScreenX == right.ScreenX &&
                   left.ScreenY == right.ScreenY &&
                   left.Color0 == right.Color0 &&
                   left.Color1 == right.Color1 &&
                   left.Width == right.Width &&
                   left.Height == right.Height &&
                   left.Value0 == right.Value0;
        }

        private static bool BarContentEquals(in ScreenHudBarItem left, in ScreenHudBarItem right)
        {
            return left.StableId == right.StableId &&
                   left.DirtySerial == right.DirtySerial &&
                   left.Color0 == right.Color0 &&
                   left.Color1 == right.Color1 &&
                   left.Width == right.Width &&
                   left.Height == right.Height &&
                   left.Value0 == right.Value0;
        }

        private static bool TextEquals(in ScreenHudTextItem left, in ScreenHudTextItem right)
        {
            return left.StableId == right.StableId &&
                   left.DirtySerial == right.DirtySerial &&
                   left.ScreenX == right.ScreenX &&
                   left.ScreenY == right.ScreenY &&
                   left.Color0 == right.Color0 &&
                   left.Value0 == right.Value0 &&
                   left.Value1 == right.Value1 &&
                   left.Id0 == right.Id0 &&
                   left.Id1 == right.Id1 &&
                   left.FontSize == right.FontSize &&
                   TextPacketEquals(in left.Text, in right.Text);
        }

        private static bool TextContentEquals(in ScreenHudTextItem left, in ScreenHudTextItem right)
        {
            return left.StableId == right.StableId &&
                   left.DirtySerial == right.DirtySerial &&
                   left.Color0 == right.Color0 &&
                   left.Value0 == right.Value0 &&
                   left.Value1 == right.Value1 &&
                   left.Id0 == right.Id0 &&
                   left.Id1 == right.Id1 &&
                   left.FontSize == right.FontSize &&
                   TextPacketEquals(in left.Text, in right.Text);
        }

        private static bool TextPacketEquals(in PresentationTextPacket left, in PresentationTextPacket right)
        {
            return left.TokenId == right.TokenId &&
                   left.ArgCount == right.ArgCount &&
                   left.Reserved0 == right.Reserved0 &&
                   left.Reserved1 == right.Reserved1 &&
                   TextArgEquals(in left.Arg0, in right.Arg0) &&
                   TextArgEquals(in left.Arg1, in right.Arg1) &&
                   TextArgEquals(in left.Arg2, in right.Arg2) &&
                   TextArgEquals(in left.Arg3, in right.Arg3);
        }

        private static bool TextArgEquals(in PresentationTextArg left, in PresentationTextArg right)
        {
            return left.Type == right.Type &&
                   left.Format == right.Format &&
                   left.Reserved == right.Reserved &&
                   left.Raw32 == right.Raw32;
        }
    }
}
