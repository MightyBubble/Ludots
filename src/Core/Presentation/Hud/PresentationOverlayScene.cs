using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Minimap;

namespace Ludots.Core.Presentation.Hud
{
    public sealed class PresentationOverlayScene
    {
        private const int KindCount = 5;
        private const int LaneCount = 10;

        private readonly LaneState[] _lanes;
        private readonly PresentationOverlayItem[] _flattenedItems;
        private readonly int[] _layerVersions;
        private readonly int _capacity;

        private MinimapScreenMarkerBuffer? _topMostMinimapMarkers;
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

        public MinimapScreenMarkerBuffer? TopMostMinimapMarkers => _topMostMinimapMarkers;

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

        public ReadOnlySpan<PresentationOverlayItem> GetLaneMutatedSpan(
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind)
        {
            LaneState lane = _lanes[GetLaneIndex(layer, kind)];
            return new ReadOnlySpan<PresentationOverlayItem>(lane.MutatedItems, 0, lane.MutatedCount);
        }

        public ReadOnlySpan<PresentationOverlayItem> GetLaneDirtyRegionSpan(
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind)
        {
            LaneState lane = _lanes[GetLaneIndex(layer, kind)];
            return new ReadOnlySpan<PresentationOverlayItem>(lane.DirtyRegionItems, 0, lane.DirtyRegionCount);
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
            _topMostMinimapMarkers = null;
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
            _topMostMinimapMarkers = null;
            DirtyLaneCount = 0;
            DroppedSinceClear = 0;
            RetainedItemCountLastBuild = 0;
            MutatedItemCountLastBuild = 0;

            for (int laneIndex = 0; laneIndex < _lanes.Length; laneIndex++)
            {
                LaneState lane = _lanes[laneIndex];
                lane.PendingCount = 0;
                ResetLaneBuildDeltas(lane);
                lane.SeenStableIds.Clear();
            }
        }

        public void BeginAppendOnlyBuild()
        {
            _building = false;
            _buildCount = 0;
            _flattenedDirty = true;
            _topMostMinimapMarkers = null;
            DirtyLaneCount = 0;
            DroppedSinceClear = 0;
            RetainedItemCountLastBuild = 0;
            MutatedItemCountLastBuild = 0;

            for (int laneIndex = 0; laneIndex < _lanes.Length; laneIndex++)
            {
                LaneState lane = _lanes[laneIndex];
                lane.Count = 0;
                lane.PendingCount = 0;
                lane.StableIndexById.Clear();
                lane.SeenStableIds.Clear();
                ResetLaneBuildDeltas(lane);
            }

            _count = 0;
        }

        public void BeginDeltaBuild()
        {
            _building = false;
            _buildCount = 0;
            _topMostMinimapMarkers = null;
            DirtyLaneCount = 0;
            DroppedSinceClear = 0;
            RetainedItemCountLastBuild = 0;
            MutatedItemCountLastBuild = 0;

            for (int laneIndex = 0; laneIndex < _lanes.Length; laneIndex++)
            {
                ResetLaneBuildDeltas(_lanes[laneIndex]);
            }
        }

        public void BeginLayerBuild(PresentationOverlayLayer layer)
        {
            _building = true;
            _buildCount = 0;

            for (int kindValue = (int)PresentationOverlayItemKind.Text; kindValue <= (int)PresentationOverlayItemKind.Line; kindValue++)
            {
                LaneState lane = _lanes[GetLaneIndex(layer, (PresentationOverlayItemKind)kindValue)];
                lane.PendingCount = 0;
                ResetLaneBuildDeltas(lane);
                lane.SeenStableIds.Clear();
            }
        }

        public void EndLayerBuild(PresentationOverlayLayer layer)
        {
            if (!_building)
            {
                return;
            }

            bool layerDirty = false;
            for (int kindValue = (int)PresentationOverlayItemKind.Text; kindValue <= (int)PresentationOverlayItemKind.Line; kindValue++)
            {
                LaneState lane = _lanes[GetLaneIndex(layer, (PresentationOverlayItemKind)kindValue)];
                if (lane.Count != lane.PendingCount)
                {
                    if (lane.StableIndexById.Count > 0)
                    {
                        for (int removedIndex = 0; removedIndex < lane.Count; removedIndex++)
                        {
                            ref readonly PresentationOverlayItem previous = ref lane.Items[removedIndex];
                            if (previous.StableId > 0 && lane.SeenStableIds.Contains(previous.StableId))
                            {
                                continue;
                            }

                            AddDirtyRegionItem(lane, in previous);
                        }
                    }
                    else if (lane.PendingCount < lane.Count)
                    {
                        for (int removedIndex = lane.PendingCount; removedIndex < lane.Count; removedIndex++)
                        {
                            AddDirtyRegionItem(lane, in lane.Items[removedIndex]);
                        }

                        Array.Clear(lane.Items, lane.PendingCount, lane.Count - lane.PendingCount);
                    }

                    lane.Count = lane.PendingCount;
                    RebuildStableIndex(lane);
                    lane.Dirty = true;
                    lane.WorkingMutationKind = PresentationOverlayLaneMutationKind.Content;
                }
                else if (lane.Dirty)
                {
                    RebuildStableIndex(lane);
                }

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
                    layerDirty = true;
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
            _buildCount = 0;
            RecalculateTotalCount();
            if (layerDirty)
            {
                Version++;
                _flattenedDirty = true;
                _layerVersions[(int)layer]++;
            }
        }

        public void ClearLayer(PresentationOverlayLayer layer)
        {
            bool layerDirty = false;
            for (int kindValue = (int)PresentationOverlayItemKind.Text; kindValue <= (int)PresentationOverlayItemKind.Line; kindValue++)
            {
                LaneState lane = _lanes[GetLaneIndex(layer, (PresentationOverlayItemKind)kindValue)];
                if (lane.Count <= 0)
                {
                    ResetLaneBuildDeltas(lane);
                    continue;
                }

                Array.Clear(lane.Items, 0, lane.Count);
                lane.Count = 0;
                lane.PendingCount = 0;
                lane.StableIndexById.Clear();
                lane.SeenStableIds.Clear();
                ResetLaneBuildDeltas(lane);
                lane.MutationKind = PresentationOverlayLaneMutationKind.Content;
                lane.Version++;
                DirtyLaneCount++;
                layerDirty = true;
            }

            if (!layerDirty)
            {
                return;
            }

            RecalculateTotalCount();
            Version++;
            _flattenedDirty = true;
            _layerVersions[(int)layer]++;
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
                    if (lane.StableIndexById.Count > 0)
                    {
                        for (int removedIndex = 0; removedIndex < lane.Count; removedIndex++)
                        {
                            ref readonly PresentationOverlayItem previous = ref lane.Items[removedIndex];
                            if (previous.StableId > 0 && lane.SeenStableIds.Contains(previous.StableId))
                            {
                                continue;
                            }

                            AddDirtyRegionItem(lane, in previous);
                        }
                    }
                    else if (lane.PendingCount < lane.Count)
                    {
                        for (int removedIndex = lane.PendingCount; removedIndex < lane.Count; removedIndex++)
                        {
                            AddDirtyRegionItem(lane, in lane.Items[removedIndex]);
                        }

                        Array.Clear(lane.Items, lane.PendingCount, lane.Count - lane.PendingCount);
                    }

                    lane.Count = lane.PendingCount;
                    RebuildStableIndex(lane);
                    lane.Dirty = true;
                    lane.WorkingMutationKind = PresentationOverlayLaneMutationKind.Content;
                }
                else if (lane.Dirty)
                {
                    RebuildStableIndex(lane);
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
            if (layer == PresentationOverlayLayer.TopMost &&
                _topMostMinimapMarkers != null &&
                _topMostMinimapMarkers.Count > 0)
            {
                return true;
            }

            for (int kind = 1; kind <= KindCount; kind++)
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
            int dirtySerial = 0,
            PresentationClipShape clipShape = default)
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
                Color1 = border,
                ClipShape = clipShape
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

        public void SetTopMostMinimapMarkers(MinimapScreenMarkerBuffer? markers)
        {
            bool hadMarkers = _topMostMinimapMarkers != null && _topMostMinimapMarkers.Count > 0;
            bool hasMarkers = markers != null && markers.Count > 0;
            _topMostMinimapMarkers = hasMarkers ? markers : null;
            if (hasMarkers || hadMarkers)
            {
                DirtyLaneCount++;
                Version++;
                _layerVersions[(int)PresentationOverlayLayer.TopMost]++;
            }
        }

        public bool TryAddLine(
            PresentationOverlayLayer layer,
            float x0,
            float y0,
            float x1,
            float y1,
            float thickness,
            in Vector4 color,
            int stableId = 0,
            int dirtySerial = 0,
            PresentationClipShape clipShape = default)
        {
            if (thickness <= 0f || color.W <= 0f)
            {
                return true;
            }

            var item = new PresentationOverlayItem
            {
                StableId = stableId,
                DirtySerial = dirtySerial,
                Kind = PresentationOverlayItemKind.Line,
                Layer = layer,
                X = x0,
                Y = y0,
                Width = x1,
                Height = y1,
                Value0 = thickness,
                Color0 = color,
                ClipShape = clipShape
            };
            return TryStore(in item);
        }

        public bool TryUpsertBar(
            PresentationOverlayLayer layer,
            float x,
            float y,
            float width,
            float height,
            float value,
            in Vector4 background,
            in Vector4 foreground,
            int stableId,
            int dirtySerial)
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
            return TryUpsertStable(in item);
        }

        public bool TryUpsertText(
            PresentationOverlayLayer layer,
            float x,
            float y,
            string text,
            int fontSize,
            in Vector4 color,
            int stableId,
            int dirtySerial)
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
            return TryUpsertStable(in item);
        }

        public bool TryUpdateStablePosition(
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind,
            int stableId,
            float x,
            float y)
        {
            if (stableId <= 0)
            {
                return true;
            }

            int laneIndex = GetLaneIndex(layer, kind);
            LaneState lane = _lanes[laneIndex];
            if (!lane.StableIndexById.TryGetValue(stableId, out int index))
            {
                return true;
            }

            ref PresentationOverlayItem item = ref lane.Items[index];
            float deltaX = x - item.X;
            float deltaY = y - item.Y;
            if (deltaX == 0f && deltaY == 0f)
            {
                RetainedItemCountLastBuild++;
                return true;
            }

            PresentationOverlayItem previous = item;
            item.X = x;
            item.Y = y;
            MarkLaneMutated(
                lane,
                PresentationOverlayLaneMutationKind.PositionOnly,
                in previous,
                in item,
                addMutated: true,
                deltaX,
                deltaY);
            _flattenedDirty = true;
            Version++;
            _layerVersions[(int)layer]++;
            return true;
        }

        public void TryUpdateStableBarPositions(
            PresentationOverlayLayer layer,
            ReadOnlySpan<ScreenHudBarItem> items)
        {
            UpdateStablePositions(layer, PresentationOverlayItemKind.Bar, items);
        }

        public void TryUpdateStableBarPositionRange(
            PresentationOverlayLayer layer,
            ReadOnlySpan<ScreenHudBarItem> items,
            int start,
            int count)
        {
            UpdateStablePositionRange(layer, PresentationOverlayItemKind.Bar, items, start, count);
        }

        public void TryUpdateStableTextPositions(
            PresentationOverlayLayer layer,
            ReadOnlySpan<ScreenHudTextItem> items)
        {
            UpdateStablePositions(layer, PresentationOverlayItemKind.Text, items);
        }

        public void TryUpdateStableTextPositionRange(
            PresentationOverlayLayer layer,
            ReadOnlySpan<ScreenHudTextItem> items,
            int start,
            int count)
        {
            UpdateStablePositionRange(layer, PresentationOverlayItemKind.Text, items, start, count);
        }

        public bool TryAppendBar(
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
            return TryAppend(in item);
        }

        public bool TryAppendText(
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
            return TryAppend(in item);
        }

        public void RemoveStable(PresentationOverlayLayer layer, PresentationOverlayItemKind kind, int stableId)
        {
            if (stableId <= 0)
            {
                return;
            }

            int laneIndex = GetLaneIndex(layer, kind);
            LaneState lane = _lanes[laneIndex];
            if (!lane.StableIndexById.TryGetValue(stableId, out int index))
            {
                return;
            }

            PresentationOverlayItem previous = lane.Items[index];
            int lastIndex = lane.Count - 1;
            if (index != lastIndex)
            {
                PresentationOverlayItem moved = lane.Items[lastIndex];
                lane.Items[index] = moved;
                if (moved.StableId > 0)
                {
                    lane.StableIndexById[moved.StableId] = index;
                }
            }

            lane.Items[lastIndex] = default;
            lane.Count = lastIndex;
            lane.StableIndexById.Remove(stableId);
            MarkLaneMutated(lane, PresentationOverlayLaneMutationKind.Content, in previous, in previous, addMutated: false);
            RecalculateTotalCount();
            _flattenedDirty = true;
            Version++;
            _layerVersions[(int)layer]++;
        }

        public void EndAppendOnlyBuild()
        {
            Span<bool> layerDirty = stackalloc bool[_layerVersions.Length];
            int totalCount = 0;
            for (int laneIndex = 0; laneIndex < _lanes.Length; laneIndex++)
            {
                LaneState lane = _lanes[laneIndex];
                totalCount += lane.Count;
                lane.Version++;
                lane.MutationKind = PresentationOverlayLaneMutationKind.Content;
                if (lane.Count > 0)
                {
                    DirtyLaneCount++;
                    layerDirty[(int)GetLayer(laneIndex)] = true;
                }
            }

            _count = totalCount;
            Version++;
            IncrementDirtyLayers(layerDirty);
        }

        private bool TryStore(in PresentationOverlayItem item)
        {
            return _building
                ? TryStoreRetained(in item)
                : TryStoreImmediate(in item);
        }

        private bool TryAppend(in PresentationOverlayItem item)
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
            lane.Items[lane.Count++] = item;
            _count++;
            return true;
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
            if (item.StableId > 0)
            {
                lane.StableIndexById[item.StableId] = lane.Count;
            }

            lane.Count++;
            lane.Version++;
            _count++;
            _flattenedDirty = true;
            _layerVersions[(int)item.Layer]++;
            Version++;
            return true;
        }

        private bool TryUpsertStable(in PresentationOverlayItem item)
        {
            if (item.StableId <= 0)
            {
                return TryStoreImmediate(in item);
            }

            int laneIndex = GetLaneIndex(item.Layer, item.Kind);
            LaneState lane = _lanes[laneIndex];
            if (lane.StableIndexById.TryGetValue(item.StableId, out int index))
            {
                PresentationOverlayItem previous = lane.Items[index];
                PresentationOverlayItemCompareResult compareResult = CompareItems(in previous, in item, out float deltaX, out float deltaY);
                if (compareResult == PresentationOverlayItemCompareResult.Equal)
                {
                    RetainedItemCountLastBuild++;
                    return true;
                }

                lane.Items[index] = item;
                MarkLaneMutated(lane, ToLaneMutationKind(compareResult), in previous, in item, addMutated: true, deltaX, deltaY);
                _flattenedDirty = true;
                RecalculateTotalCount();
                Version++;
                _layerVersions[(int)item.Layer]++;
                return true;
            }

            if (_count >= _capacity)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            EnsureLaneCapacity(lane, lane.Count + 1);
            lane.Items[lane.Count] = item;
            lane.StableIndexById[item.StableId] = lane.Count;
            lane.Count++;
            _count++;
            MarkLaneMutated(lane, PresentationOverlayLaneMutationKind.Content, in item, in item, addMutated: true);
            _flattenedDirty = true;
            Version++;
            _layerVersions[(int)item.Layer]++;
            return true;
        }

        private void UpdateStablePositions(
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind,
            ReadOnlySpan<ScreenHudBarItem> items)
        {
            int laneIndex = GetLaneIndex(layer, kind);
            LaneState lane = _lanes[laneIndex];
            int changedCount = 0;
            float translationX = 0f;
            float translationY = 0f;
            bool uniformSet = false;
            bool uniform = true;
            float uniformX = 0f;
            float uniformY = 0f;

            for (int i = 0; i < items.Length; i++)
            {
                ref readonly ScreenHudBarItem source = ref items[i];
                if (!TryResolveStableLaneIndex(lane, i, source.StableId, out int index))
                {
                    continue;
                }

                ref PresentationOverlayItem item = ref lane.Items[index];
                TrackPositionUpdate(ref item, source.ScreenX, source.ScreenY, ref changedCount, ref translationX, ref translationY, ref uniformSet, ref uniform, ref uniformX, ref uniformY);
            }

            MarkLanePositionBatch(layer, lane, changedCount, translationX, translationY, uniformSet && uniform, uniformX, uniformY);
        }

        private void UpdateStablePositionRange(
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind,
            ReadOnlySpan<ScreenHudBarItem> items,
            int start,
            int count)
        {
            int laneIndex = GetLaneIndex(layer, kind);
            LaneState lane = _lanes[laneIndex];
            int changedCount = 0;
            float translationX = 0f;
            float translationY = 0f;
            bool uniformSet = false;
            bool uniform = true;
            float uniformX = 0f;
            float uniformY = 0f;
            int end = Math.Min(items.Length, start + count);

            for (int i = Math.Max(0, start); i < end; i++)
            {
                ref readonly ScreenHudBarItem source = ref items[i];
                if ((uint)i >= (uint)lane.Count)
                {
                    if (!TryResolveStableLaneIndex(lane, i, source.StableId, out int resolvedIndex))
                    {
                        continue;
                    }

                    ref PresentationOverlayItem resolvedItem = ref lane.Items[resolvedIndex];
                    TrackPositionUpdate(ref resolvedItem, source.ScreenX, source.ScreenY, ref changedCount, ref translationX, ref translationY, ref uniformSet, ref uniform, ref uniformX, ref uniformY);
                    continue;
                }

                ref PresentationOverlayItem item = ref lane.Items[i];
                if (item.StableId != source.StableId)
                {
                    if (!TryResolveStableLaneIndex(lane, i, source.StableId, out int resolvedIndex))
                    {
                        continue;
                    }

                    item = ref lane.Items[resolvedIndex];
                }

                TrackPositionUpdate(ref item, source.ScreenX, source.ScreenY, ref changedCount, ref translationX, ref translationY, ref uniformSet, ref uniform, ref uniformX, ref uniformY);
            }

            MarkLanePositionBatch(layer, lane, changedCount, translationX, translationY, uniformSet && uniform, uniformX, uniformY);
        }

        private void UpdateStablePositions(
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind,
            ReadOnlySpan<ScreenHudTextItem> items)
        {
            int laneIndex = GetLaneIndex(layer, kind);
            LaneState lane = _lanes[laneIndex];
            int changedCount = 0;
            float translationX = 0f;
            float translationY = 0f;
            bool uniformSet = false;
            bool uniform = true;
            float uniformX = 0f;
            float uniformY = 0f;

            for (int i = 0; i < items.Length; i++)
            {
                ref readonly ScreenHudTextItem source = ref items[i];
                if (!TryResolveStableLaneIndex(lane, i, source.StableId, out int index))
                {
                    continue;
                }

                ref PresentationOverlayItem item = ref lane.Items[index];
                TrackPositionUpdate(ref item, source.ScreenX, source.ScreenY, ref changedCount, ref translationX, ref translationY, ref uniformSet, ref uniform, ref uniformX, ref uniformY);
            }

            MarkLanePositionBatch(layer, lane, changedCount, translationX, translationY, uniformSet && uniform, uniformX, uniformY);
        }

        private void UpdateStablePositionRange(
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind,
            ReadOnlySpan<ScreenHudTextItem> items,
            int start,
            int count)
        {
            int laneIndex = GetLaneIndex(layer, kind);
            LaneState lane = _lanes[laneIndex];
            int changedCount = 0;
            float translationX = 0f;
            float translationY = 0f;
            bool uniformSet = false;
            bool uniform = true;
            float uniformX = 0f;
            float uniformY = 0f;
            int end = Math.Min(items.Length, start + count);

            for (int i = Math.Max(0, start); i < end; i++)
            {
                ref readonly ScreenHudTextItem source = ref items[i];
                if ((uint)i >= (uint)lane.Count)
                {
                    if (!TryResolveStableLaneIndex(lane, i, source.StableId, out int resolvedIndex))
                    {
                        continue;
                    }

                    ref PresentationOverlayItem resolvedItem = ref lane.Items[resolvedIndex];
                    TrackPositionUpdate(ref resolvedItem, source.ScreenX, source.ScreenY, ref changedCount, ref translationX, ref translationY, ref uniformSet, ref uniform, ref uniformX, ref uniformY);
                    continue;
                }

                ref PresentationOverlayItem item = ref lane.Items[i];
                if (item.StableId != source.StableId)
                {
                    if (!TryResolveStableLaneIndex(lane, i, source.StableId, out int resolvedIndex))
                    {
                        continue;
                    }

                    item = ref lane.Items[resolvedIndex];
                }

                TrackPositionUpdate(ref item, source.ScreenX, source.ScreenY, ref changedCount, ref translationX, ref translationY, ref uniformSet, ref uniform, ref uniformX, ref uniformY);
            }

            MarkLanePositionBatch(layer, lane, changedCount, translationX, translationY, uniformSet && uniform, uniformX, uniformY);
        }

        private static bool TryResolveStableLaneIndex(LaneState lane, int preferredIndex, int stableId, out int index)
        {
            if (stableId <= 0)
            {
                index = -1;
                return false;
            }

            if ((uint)preferredIndex < (uint)lane.Count &&
                lane.Items[preferredIndex].StableId == stableId)
            {
                index = preferredIndex;
                return true;
            }

            return lane.StableIndexById.TryGetValue(stableId, out index);
        }

        private static void TrackPositionUpdate(
            ref PresentationOverlayItem item,
            float x,
            float y,
            ref int changedCount,
            ref float translationX,
            ref float translationY,
            ref bool uniformSet,
            ref bool uniform,
            ref float uniformX,
            ref float uniformY)
        {
            float deltaX = x - item.X;
            float deltaY = y - item.Y;
            if (deltaX == 0f && deltaY == 0f)
            {
                return;
            }

            item.X = x;
            item.Y = y;
            changedCount++;
            translationX += deltaX;
            translationY += deltaY;
            if (!uniformSet)
            {
                uniformSet = true;
                uniformX = deltaX;
                uniformY = deltaY;
            }
            else if (uniformX != deltaX || uniformY != deltaY)
            {
                uniform = false;
            }
        }

        private void MarkLanePositionBatch(
            PresentationOverlayLayer layer,
            LaneState lane,
            int changedCount,
            float translationX,
            float translationY,
            bool uniform,
            float uniformX,
            float uniformY)
        {
            if (changedCount <= 0)
            {
                RetainedItemCountLastBuild += lane.Count;
                return;
            }

            if (!lane.Dirty)
            {
                DirtyLaneCount++;
            }

            lane.Dirty = true;
            lane.MutationKind = PresentationOverlayLaneMutationKind.PositionOnly;
            lane.WorkingMutationKind = PresentationOverlayLaneMutationKind.PositionOnly;
            lane.AverageTranslationX = translationX / changedCount;
            lane.AverageTranslationY = translationY / changedCount;
            lane.HasUniformTranslation = uniform;
            lane.UniformTranslationX = uniform ? uniformX : 0f;
            lane.UniformTranslationY = uniform ? uniformY : 0f;
            lane.Version++;
            _flattenedDirty = true;
            Version++;
            _layerVersions[(int)layer]++;
        }

        private void RecalculateTotalCount()
        {
            int total = 0;
            for (int i = 0; i < _lanes.Length; i++)
            {
                total += _lanes[i].Count;
            }

            _count = total;
        }

        private static PresentationOverlayLaneMutationKind ToLaneMutationKind(
            PresentationOverlayItemCompareResult compareResult)
        {
            return compareResult == PresentationOverlayItemCompareResult.PositionOnly
                ? PresentationOverlayLaneMutationKind.PositionOnly
                : PresentationOverlayLaneMutationKind.Content;
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
            if (item.StableId > 0)
            {
                lane.SeenStableIds.Add(item.StableId);
            }

            if (slotIndex >= lane.Count)
            {
                lane.Items[slotIndex] = item;
                lane.Dirty = true;
                lane.WorkingMutationKind = PresentationOverlayLaneMutationKind.Content;
                AddDirtyRegionItem(lane, in item);
                AddMutatedItem(lane, in item);
                MutatedItemCountLastBuild++;
            }
            else
            {
                ref readonly PresentationOverlayItem previousItem = ref lane.Items[slotIndex];
                PresentationOverlayItem previousSnapshot = previousItem;
                PresentationOverlayItemCompareResult compareResult = CompareItems(in previousItem, in item, out float deltaX, out float deltaY);
                if (compareResult == PresentationOverlayItemCompareResult.Equal)
                {
                    RetainedItemCountLastBuild++;
                }
                else
                {
                    TrackMutation(lane, compareResult, deltaX, deltaY);
                    lane.Items[slotIndex] = item;
                    lane.Dirty = true;
                    AddDirtyRegionItem(lane, in previousSnapshot);
                    AddDirtyRegionItem(lane, in item);
                    AddMutatedItem(lane, in item);
                    MutatedItemCountLastBuild++;
                }
            }

            lane.PendingCount++;
            _buildCount++;
            return true;
        }

        private void MarkLaneMutated(
            LaneState lane,
            PresentationOverlayLaneMutationKind mutationKind,
            in PresentationOverlayItem previous,
            in PresentationOverlayItem current,
            bool addMutated,
            float deltaX = 0f,
            float deltaY = 0f)
        {
            if (!lane.Dirty)
            {
                DirtyLaneCount++;
            }

            lane.Dirty = true;
            lane.MutationKind = mutationKind;
            lane.WorkingMutationKind = mutationKind;
            lane.AverageTranslationX = 0f;
            lane.AverageTranslationY = 0f;
            lane.HasUniformTranslation = false;
            lane.UniformTranslationX = 0f;
            lane.UniformTranslationY = 0f;
            if (mutationKind == PresentationOverlayLaneMutationKind.PositionOnly)
            {
                lane.AverageTranslationX = deltaX;
                lane.AverageTranslationY = deltaY;
                lane.HasUniformTranslation = true;
                lane.UniformTranslationX = deltaX;
                lane.UniformTranslationY = deltaY;
            }

            lane.Version++;
            AddDirtyRegionItem(lane, in previous);
            if (addMutated)
            {
                AddDirtyRegionItem(lane, in current);
                AddMutatedItem(lane, in current);
                MutatedItemCountLastBuild++;
            }
        }

        private static void TrackMutation(
            LaneState lane,
            PresentationOverlayItemCompareResult compareResult,
            float deltaX,
            float deltaY)
        {
            if (lane.WorkingMutationKind == PresentationOverlayLaneMutationKind.Content)
            {
                return;
            }

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

                return;
            }

            lane.WorkingMutationKind = PresentationOverlayLaneMutationKind.Content;
            lane.WorkingTranslationX = 0f;
            lane.WorkingTranslationY = 0f;
            lane.WorkingTranslationCount = 0;
            lane.WorkingHasUniformTranslation = false;
            lane.WorkingUniformTranslationSet = false;
            lane.WorkingUniformTranslationX = 0f;
            lane.WorkingUniformTranslationY = 0f;
        }

        private static void ResetLaneBuildDeltas(LaneState lane)
        {
            lane.MutatedCount = 0;
            lane.DirtyRegionCount = 0;
            lane.Dirty = false;
            lane.MutationKind = PresentationOverlayLaneMutationKind.None;
            lane.WorkingMutationKind = PresentationOverlayLaneMutationKind.None;
            lane.AverageTranslationX = 0f;
            lane.AverageTranslationY = 0f;
            lane.HasUniformTranslation = false;
            lane.UniformTranslationX = 0f;
            lane.UniformTranslationY = 0f;
            lane.WorkingTranslationX = 0f;
            lane.WorkingTranslationY = 0f;
            lane.WorkingTranslationCount = 0;
            lane.WorkingHasUniformTranslation = true;
            lane.WorkingUniformTranslationSet = false;
            lane.WorkingUniformTranslationX = 0f;
            lane.WorkingUniformTranslationY = 0f;
        }

        private static void RebuildStableIndex(LaneState lane)
        {
            lane.StableIndexById.Clear();
            for (int i = 0; i < lane.Count; i++)
            {
                int stableId = lane.Items[i].StableId;
                if (stableId > 0)
                {
                    lane.StableIndexById[stableId] = i;
                }
            }
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

        private static void AddMutatedItem(LaneState lane, in PresentationOverlayItem item)
        {
            EnsureMutatedCapacity(lane, lane.MutatedCount + 1);
            lane.MutatedItems[lane.MutatedCount++] = item;
        }

        private static void AddDirtyRegionItem(LaneState lane, in PresentationOverlayItem item)
        {
            EnsureDirtyRegionCapacity(lane, lane.DirtyRegionCount + 1);
            lane.DirtyRegionItems[lane.DirtyRegionCount++] = item;
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
                left.Value0 != right.Value0 ||
                left.Value1 != right.Value1 ||
                left.Value2 != right.Value2 ||
                !left.ClipShape.Equals(right.ClipShape))
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
            return (PresentationOverlayLayer)(laneIndex / KindCount);
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
            if (kind is PresentationOverlayItemKind.None or < PresentationOverlayItemKind.Text or > PresentationOverlayItemKind.Line)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            return ((int)layer * KindCount) + ((int)kind - 1);
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

        private static void EnsureMutatedCapacity(LaneState lane, int required)
        {
            if (lane.MutatedItems.Length >= required)
            {
                return;
            }

            int next = lane.MutatedItems.Length == 0 ? 4 : lane.MutatedItems.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref lane.MutatedItems, next);
        }

        private static void EnsureDirtyRegionCapacity(LaneState lane, int required)
        {
            if (lane.DirtyRegionItems.Length >= required)
            {
                return;
            }

            int next = lane.DirtyRegionItems.Length == 0 ? 4 : lane.DirtyRegionItems.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref lane.DirtyRegionItems, next);
        }

        private sealed class LaneState
        {
            public PresentationOverlayItem[] Items = Array.Empty<PresentationOverlayItem>();
            public PresentationOverlayItem[] MutatedItems = Array.Empty<PresentationOverlayItem>();
            public PresentationOverlayItem[] DirtyRegionItems = Array.Empty<PresentationOverlayItem>();
            public int Count;
            public int PendingCount;
            public int MutatedCount;
            public int DirtyRegionCount;
            public int Version;
            public bool Dirty;
            public PresentationOverlayLaneMutationKind MutationKind;
            public PresentationOverlayLaneMutationKind WorkingMutationKind;
            public readonly Dictionary<int, int> StableIndexById = new();
            public readonly HashSet<int> SeenStableIds = new();
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
