using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Hud
{
    public sealed class PresentationOverlayScene
    {
        private const int LaneCount = 6;

        private readonly LaneState[] _lanes;
        private readonly PresentationOverlayItem[] _flattenedItems;
        private readonly LaneReference[] _orderedItems;
        private readonly int _capacity;

        private int _count;
        private int _orderedCount;
        private int _buildCount;
        private bool _building;
        private bool _flattenedDirty;

        public PresentationOverlayScene(int capacity = 32768)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _capacity = capacity;
            _lanes = new LaneState[LaneCount];
            for (int i = 0; i < _lanes.Length; i++)
            {
                _lanes[i] = new LaneState();
            }

            _flattenedItems = new PresentationOverlayItem[capacity];
            _orderedItems = new LaneReference[capacity];
        }

        public int Count => _count;

        public int Capacity => _capacity;

        public int DroppedSinceClear { get; private set; }

        public int DroppedTotal { get; private set; }

        public int DirtyLaneCount { get; private set; }

        public int Version { get; private set; }

        public int RetainedItemCountLastBuild { get; private set; }

        public int MutatedItemCountLastBuild { get; private set; }

        public ReadOnlySpan<PresentationOverlayItem> GetSpan()
        {
            if (_flattenedDirty)
            {
                RebuildFlattenedItems();
            }

            return new ReadOnlySpan<PresentationOverlayItem>(_flattenedItems, 0, _count);
        }

        public ReadOnlySpan<PresentationOverlayItem> GetLaneSpan(
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind)
        {
            LaneState lane = _lanes[GetLaneIndex(layer, kind)];
            return new ReadOnlySpan<PresentationOverlayItem>(lane.Items, 0, lane.Count);
        }

        public int GetLaneVersion(PresentationOverlayLayer layer, PresentationOverlayItemKind kind)
        {
            return _lanes[GetLaneIndex(layer, kind)].Version;
        }

        public void Clear()
        {
            bool hadContent = _count > 0;
            for (int laneIndex = 0; laneIndex < _lanes.Length; laneIndex++)
            {
                LaneState lane = _lanes[laneIndex];
                if (lane.Count > 0)
                {
                    Array.Clear(lane.Items, 0, lane.Count);
                    lane.Count = 0;
                    lane.PendingCount = 0;
                    lane.Version++;
                }

                lane.Dirty = false;
            }

            _count = 0;
            _orderedCount = 0;
            _buildCount = 0;
            _building = false;
            _flattenedDirty = false;
            DirtyLaneCount = 0;
            DroppedSinceClear = 0;
            RetainedItemCountLastBuild = 0;
            MutatedItemCountLastBuild = 0;

            if (hadContent)
            {
                Version++;
            }
        }

        public void BeginBuild()
        {
            _building = true;
            _buildCount = 0;
            _flattenedDirty = false;
            DirtyLaneCount = 0;
            DroppedSinceClear = 0;
            RetainedItemCountLastBuild = 0;
            MutatedItemCountLastBuild = 0;

            for (int laneIndex = 0; laneIndex < _lanes.Length; laneIndex++)
            {
                LaneState lane = _lanes[laneIndex];
                lane.PendingCount = 0;
                lane.Dirty = false;
            }
        }

        public void EndBuild()
        {
            if (!_building)
            {
                return;
            }

            bool sceneDirty = _orderedCount != _buildCount || _flattenedDirty;
            for (int laneIndex = 0; laneIndex < _lanes.Length; laneIndex++)
            {
                LaneState lane = _lanes[laneIndex];
                if (lane.Count != lane.PendingCount)
                {
                    if (lane.PendingCount < lane.Count)
                    {
                        Array.Clear(lane.Items, lane.PendingCount, lane.Count - lane.PendingCount);
                    }

                    lane.Count = lane.PendingCount;
                    lane.Dirty = true;
                }

                if (lane.Dirty)
                {
                    lane.Version++;
                    DirtyLaneCount++;
                    sceneDirty = true;
                }
            }

            _building = false;
            _orderedCount = _buildCount;
            _count = _buildCount;

            if (sceneDirty)
            {
                Version++;
                _flattenedDirty = true;
            }
        }

        public bool ContainsLayer(PresentationOverlayLayer layer)
        {
            for (int kind = 1; kind <= 3; kind++)
            {
                if (_lanes[GetLaneIndex(layer, (PresentationOverlayItemKind)kind)].Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryAddText(
            PresentationOverlayLayer layer,
            float x,
            float y,
            string text,
            int fontSize,
            in Vector4 color)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            var item = new PresentationOverlayItem
            {
                Kind = PresentationOverlayItemKind.Text,
                Layer = layer,
                X = x,
                Y = y,
                FontSize = fontSize,
                Text = text,
                Color0 = color
            };
            return TryStore(in item);
        }

        public bool TryAddRect(
            PresentationOverlayLayer layer,
            float x,
            float y,
            float width,
            float height,
            in Vector4 fill,
            in Vector4 border)
        {
            if (width <= 0f || height <= 0f)
            {
                return true;
            }

            var item = new PresentationOverlayItem
            {
                Kind = PresentationOverlayItemKind.Rect,
                Layer = layer,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                Color0 = fill,
                Color1 = border
            };
            return TryStore(in item);
        }

        public bool TryAddBar(
            PresentationOverlayLayer layer,
            float x,
            float y,
            float width,
            float height,
            float value,
            in Vector4 background,
            in Vector4 foreground)
        {
            if (width <= 0f || height <= 0f)
            {
                return true;
            }

            var item = new PresentationOverlayItem
            {
                Kind = PresentationOverlayItemKind.Bar,
                Layer = layer,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                Value0 = value,
                Color0 = background,
                Color1 = foreground
            };
            return TryStore(in item);
        }

        private bool TryStore(in PresentationOverlayItem item)
        {
            return _building
                ? TryStoreRetained(in item)
                : TryStoreImmediate(in item);
        }

        private bool TryStoreImmediate(in PresentationOverlayItem item)
        {
            if (_count >= _capacity)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            int laneIndex = GetLaneIndex(item.Layer, item.Kind);
            LaneState lane = _lanes[laneIndex];
            EnsureLaneCapacity(lane, lane.Count + 1);
            lane.Items[lane.Count] = item;
            _orderedItems[_count] = new LaneReference(laneIndex, lane.Count);
            _flattenedItems[_count] = item;
            lane.Count++;
            lane.Version++;
            _count++;
            _orderedCount = _count;
            Version++;
            return true;
        }

        private bool TryStoreRetained(in PresentationOverlayItem item)
        {
            if (_buildCount >= _capacity)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            int laneIndex = GetLaneIndex(item.Layer, item.Kind);
            LaneState lane = _lanes[laneIndex];
            int slotIndex = lane.PendingCount;
            EnsureLaneCapacity(lane, slotIndex + 1);

            if (slotIndex >= lane.Count || !ItemsEqual(in lane.Items[slotIndex], in item))
            {
                lane.Items[slotIndex] = item;
                lane.Dirty = true;
                MutatedItemCountLastBuild++;
            }
            else
            {
                RetainedItemCountLastBuild++;
            }

            var laneReference = new LaneReference(laneIndex, slotIndex);
            if (_buildCount >= _orderedCount || !_orderedItems[_buildCount].Equals(laneReference))
            {
                _flattenedDirty = true;
            }

            _orderedItems[_buildCount] = laneReference;
            lane.PendingCount++;
            _buildCount++;
            return true;
        }

        private void RebuildFlattenedItems()
        {
            for (int i = 0; i < _orderedCount; i++)
            {
                LaneReference laneReference = _orderedItems[i];
                _flattenedItems[i] = _lanes[laneReference.LaneIndex].Items[laneReference.SlotIndex];
            }

            _count = _orderedCount;
            _flattenedDirty = false;
        }

        private static bool ItemsEqual(in PresentationOverlayItem left, in PresentationOverlayItem right)
        {
            return left.Kind == right.Kind
                && left.Layer == right.Layer
                && left.X == right.X
                && left.Y == right.Y
                && left.Width == right.Width
                && left.Height == right.Height
                && left.FontSize == right.FontSize
                && string.Equals(left.Text, right.Text, StringComparison.Ordinal)
                && left.Color0.Equals(right.Color0)
                && left.Color1.Equals(right.Color1)
                && left.Value0 == right.Value0;
        }

        private static int GetLaneIndex(PresentationOverlayLayer layer, PresentationOverlayItemKind kind)
        {
            if (kind is PresentationOverlayItemKind.None or < PresentationOverlayItemKind.Text or > PresentationOverlayItemKind.Bar)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            return ((int)layer * 3) + ((int)kind - 1);
        }

        private static void EnsureLaneCapacity(LaneState lane, int required)
        {
            if (lane.Items.Length >= required)
            {
                return;
            }

            int next = lane.Items.Length == 0 ? 4 : lane.Items.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref lane.Items, next);
        }

        private sealed class LaneState
        {
            public PresentationOverlayItem[] Items = Array.Empty<PresentationOverlayItem>();
            public int Count;
            public int PendingCount;
            public int Version;
            public bool Dirty;
        }

        private readonly record struct LaneReference(int LaneIndex, int SlotIndex);
    }
}
