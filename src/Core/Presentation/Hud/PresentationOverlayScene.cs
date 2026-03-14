using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Hud
{
    public sealed class PresentationOverlayScene
    {
        private const int LaneCount = 6;

        private readonly LaneState[] _lanes;
        private readonly PresentationOverlayItem[] _flattenedItems;
        private readonly int[] _layerVersions;
        private readonly int _capacity;

        private int _count;
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
            _layerVersions = new int[Enum.GetValues<PresentationOverlayLayer>().Length];
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

        public int GetLayerVersion(PresentationOverlayLayer layer)
        {
            return _layerVersions[(int)layer];
        }

        public PresentationOverlayLaneMutationKind GetLaneMutationKind(
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind)
        {
            return _lanes[GetLaneIndex(layer, kind)].MutationKind;
        }

        public Vector2 GetLaneAverageTranslation(
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind)
        {
            LaneState lane = _lanes[GetLaneIndex(layer, kind)];
            return new Vector2(lane.AverageTranslationX, lane.AverageTranslationY);
        }

        public bool TryGetLaneUniformTranslation(
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind,
            out Vector2 translation)
        {
            LaneState lane = _lanes[GetLaneIndex(layer, kind)];
            if (lane.HasUniformTranslation)
            {
                translation = new Vector2(lane.UniformTranslationX, lane.UniformTranslationY);
                return true;
            }

            translation = default;
            return false;
        }

        public void Clear()
        {
            bool hadContent = _count > 0;
            Span<bool> layerDirty = stackalloc bool[_layerVersions.Length];
            for (int laneIndex = 0; laneIndex < _lanes.Length; laneIndex++)
            {
                LaneState lane = _lanes[laneIndex];
                if (lane.Count > 0)
                {
                    Array.Clear(lane.Items, 0, lane.Count);
                    lane.Count = 0;
                    lane.PendingCount = 0;
                    lane.Version++;
                    layerDirty[(int)GetLayer(laneIndex)] = true;
                }

                lane.Dirty = false;
                lane.MutationKind = PresentationOverlayLaneMutationKind.None;
                lane.AverageTranslationX = 0f;
                lane.AverageTranslationY = 0f;
                lane.HasUniformTranslation = false;
                lane.UniformTranslationX = 0f;
                lane.UniformTranslationY = 0f;
            }

            _count = 0;
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
                IncrementDirtyLayers(layerDirty);
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
                lane.WorkingMutationKind = PresentationOverlayLaneMutationKind.None;
                lane.WorkingTranslationX = 0f;
                lane.WorkingTranslationY = 0f;
                lane.WorkingTranslationCount = 0;
                lane.WorkingHasUniformTranslation = true;
                lane.WorkingUniformTranslationSet = false;
                lane.WorkingUniformTranslationX = 0f;
                lane.WorkingUniformTranslationY = 0f;
            }
        }

        public void EndBuild()
        {
            if (!_building)
            {
                return;
            }

            bool sceneDirty = _count != _buildCount;
            Span<bool> layerDirty = stackalloc bool[_layerVersions.Length];
            int totalCount = 0;
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
                    lane.WorkingMutationKind = PresentationOverlayLaneMutationKind.Content;
                }

                totalCount += lane.Count;
                if (lane.Dirty)
                {
                    lane.MutationKind = lane.WorkingMutationKind;
                    if (lane.WorkingMutationKind == PresentationOverlayLaneMutationKind.PositionOnly &&
                        lane.WorkingTranslationCount > 0)
                    {
                        lane.AverageTranslationX = lane.WorkingTranslationX / lane.WorkingTranslationCount;
                        lane.AverageTranslationY = lane.WorkingTranslationY / lane.WorkingTranslationCount;
                        lane.HasUniformTranslation = lane.WorkingHasUniformTranslation && lane.WorkingUniformTranslationSet;
                        lane.UniformTranslationX = lane.WorkingUniformTranslationX;
                        lane.UniformTranslationY = lane.WorkingUniformTranslationY;
                    }
                    else
                    {
                        lane.AverageTranslationX = 0f;
                        lane.AverageTranslationY = 0f;
                        lane.HasUniformTranslation = false;
                        lane.UniformTranslationX = 0f;
                        lane.UniformTranslationY = 0f;
                    }

                    lane.Version++;
                    DirtyLaneCount++;
                    sceneDirty = true;
                    layerDirty[(int)GetLayer(laneIndex)] = true;
                }
                else
                {
                    lane.MutationKind = PresentationOverlayLaneMutationKind.None;
                    lane.AverageTranslationX = 0f;
                    lane.AverageTranslationY = 0f;
                    lane.HasUniformTranslation = false;
                    lane.UniformTranslationX = 0f;
                    lane.UniformTranslationY = 0f;
                }
            }

            _building = false;
            _count = totalCount;
            _buildCount = 0;

            if (sceneDirty)
            {
                Version++;
                _flattenedDirty = true;
                IncrementDirtyLayers(layerDirty);
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
            in Vector4 color,
            int stableId = 0,
            int dirtySerial = 0)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            var item = new PresentationOverlayItem
            {
                StableId = stableId,
                DirtySerial = dirtySerial,
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
            in Vector4 border,
            int stableId = 0,
            int dirtySerial = 0)
        {
            if (width <= 0f || height <= 0f)
            {
                return true;
            }

            var item = new PresentationOverlayItem
            {
                StableId = stableId,
                DirtySerial = dirtySerial,
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
            in Vector4 foreground,
            int stableId = 0,
            int dirtySerial = 0)
        {
            if (width <= 0f || height <= 0f)
            {
                return true;
            }

            var item = new PresentationOverlayItem
            {
                StableId = stableId,
                DirtySerial = dirtySerial,
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
            lane.Count++;
            lane.Version++;
            _count++;
            _flattenedDirty = true;
            _layerVersions[(int)item.Layer]++;
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

            if (slotIndex >= lane.Count)
            {
                lane.Items[slotIndex] = item;
                lane.Dirty = true;
                lane.WorkingMutationKind = PresentationOverlayLaneMutationKind.Content;
                MutatedItemCountLastBuild++;
            }
            else
            {
                ref readonly PresentationOverlayItem previousItem = ref lane.Items[slotIndex];
                PresentationOverlayItemCompareResult compareResult = CompareItems(in previousItem, in item, out float deltaX, out float deltaY);
                if (compareResult == PresentationOverlayItemCompareResult.Equal)
                {
                    RetainedItemCountLastBuild++;
                }
                else
                {
                    if (lane.WorkingMutationKind != PresentationOverlayLaneMutationKind.Content)
                    {
                        if (compareResult == PresentationOverlayItemCompareResult.PositionOnly)
                        {
                            lane.WorkingMutationKind = PresentationOverlayLaneMutationKind.PositionOnly;
                            lane.WorkingTranslationX += deltaX;
                            lane.WorkingTranslationY += deltaY;
                            lane.WorkingTranslationCount++;
                            if (!lane.WorkingUniformTranslationSet)
                            {
                                lane.WorkingUniformTranslationSet = true;
                                lane.WorkingUniformTranslationX = deltaX;
                                lane.WorkingUniformTranslationY = deltaY;
                            }
                            else if (lane.WorkingUniformTranslationX != deltaX || lane.WorkingUniformTranslationY != deltaY)
                            {
                                lane.WorkingHasUniformTranslation = false;
                            }
                        }
                        else
                        {
                            lane.WorkingMutationKind = PresentationOverlayLaneMutationKind.Content;
                            lane.WorkingTranslationX = 0f;
                            lane.WorkingTranslationY = 0f;
                            lane.WorkingTranslationCount = 0;
                            lane.WorkingHasUniformTranslation = false;
                            lane.WorkingUniformTranslationSet = false;
                            lane.WorkingUniformTranslationX = 0f;
                            lane.WorkingUniformTranslationY = 0f;
                        }
                    }

                    lane.Items[slotIndex] = item;
                    lane.Dirty = true;
                    MutatedItemCountLastBuild++;
                }
            }

            lane.PendingCount++;
            _buildCount++;
            return true;
        }

        private void RebuildFlattenedItems()
        {
            int offset = 0;
            for (int laneIndex = 0; laneIndex < _lanes.Length; laneIndex++)
            {
                LaneState lane = _lanes[laneIndex];
                if (lane.Count <= 0)
                {
                    continue;
                }

                Array.Copy(lane.Items, 0, _flattenedItems, offset, lane.Count);
                offset += lane.Count;
            }

            _count = offset;
            _flattenedDirty = false;
        }

        private static PresentationOverlayItemCompareResult CompareItems(
            in PresentationOverlayItem left,
            in PresentationOverlayItem right,
            out float deltaX,
            out float deltaY)
        {
            deltaX = right.X - left.X;
            deltaY = right.Y - left.Y;

            if (left.Kind != right.Kind ||
                left.Layer != right.Layer ||
                left.StableId != right.StableId)
            {
                return PresentationOverlayItemCompareResult.Content;
            }

            if (left.StableId != 0 && left.DirtySerial != 0)
            {
                if (left.DirtySerial != right.DirtySerial)
                {
                    return PresentationOverlayItemCompareResult.Content;
                }

                return (deltaX == 0f && deltaY == 0f)
                    ? PresentationOverlayItemCompareResult.Equal
                    : PresentationOverlayItemCompareResult.PositionOnly;
            }

            if (left.DirtySerial != right.DirtySerial ||
                left.Width != right.Width ||
                left.Height != right.Height ||
                left.FontSize != right.FontSize ||
                left.Value0 != right.Value0)
            {
                return PresentationOverlayItemCompareResult.Content;
            }

            bool sameContent = string.Equals(left.Text, right.Text, StringComparison.Ordinal)
                && left.Color0.Equals(right.Color0)
                && left.Color1.Equals(right.Color1);
            if (!sameContent)
            {
                return PresentationOverlayItemCompareResult.Content;
            }

            return (deltaX == 0f && deltaY == 0f)
                ? PresentationOverlayItemCompareResult.Equal
                : PresentationOverlayItemCompareResult.PositionOnly;
        }

        private static PresentationOverlayLayer GetLayer(int laneIndex)
        {
            return (PresentationOverlayLayer)(laneIndex / 3);
        }

        private void IncrementDirtyLayers(ReadOnlySpan<bool> layerDirty)
        {
            for (int i = 0; i < layerDirty.Length; i++)
            {
                if (layerDirty[i])
                {
                    _layerVersions[i]++;
                }
            }
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
            public PresentationOverlayLaneMutationKind MutationKind;
            public PresentationOverlayLaneMutationKind WorkingMutationKind;
            public float AverageTranslationX;
            public float AverageTranslationY;
            public bool HasUniformTranslation;
            public float UniformTranslationX;
            public float UniformTranslationY;
            public float WorkingTranslationX;
            public float WorkingTranslationY;
            public int WorkingTranslationCount;
            public bool WorkingHasUniformTranslation;
            public bool WorkingUniformTranslationSet;
            public float WorkingUniformTranslationX;
            public float WorkingUniformTranslationY;
        }

        private enum PresentationOverlayItemCompareResult : byte
        {
            Equal = 0,
            PositionOnly = 1,
            Content = 2,
        }
    }
}
