using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using SkiaSharp;

namespace Ludots.Presentation.Skia
{
    public sealed class SkiaOverlayRenderer : IDisposable
    {
        private const int KindCount = 5;
        private const int LaneCount = 10;
        private const int MaxBarSpriteCacheEntries = 2048;
        private const int MaxTextLayoutCacheEntries = 8192;
        private const int MaxTextSpriteCacheEntries = 8192;
        private const int MaxMarkerSpriteCacheEntries = 2048;
        private const int ImmediateUnderUiBarThreshold = 48;
        private const int ImmediateUnderUiTextThreshold = 48;
        private const int DeferredLargeTextChunkSize = 128;
        private const int DeferredLargeTextChunksPerFrame = 1;
        private const int TextBatchBucketsPerBlob = 256;
        private static readonly PresentationOverlayItemKind[] RenderOrder =
        {
            PresentationOverlayItemKind.Rect,
            PresentationOverlayItemKind.MinimapMarker,
            PresentationOverlayItemKind.Line,
            PresentationOverlayItemKind.Bar,
            PresentationOverlayItemKind.Text
        };

        private readonly SKPaint _fillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        private readonly SKPaint _textPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _clearPaint = new() { IsAntialias = false, Style = SKPaintStyle.Fill, BlendMode = SKBlendMode.Clear };
        private readonly MinimapMarkerAtlasBatch _minimapMarkerAtlasBatch = new();
        private readonly Dictionary<FontCacheKey, SKFont> _fontCache = new();
        private readonly Dictionary<BarSpriteCacheKey, SKImage> _barSpriteCache = new();
        private readonly Dictionary<BarSpriteCacheKey, int> _barBatchMap = new();
        private readonly List<BarBatchBucket> _barBatchBuckets = new();
        private readonly Dictionary<TextLayoutCacheKey, CachedTextLayout> _textLayoutCache = new();
        private readonly Dictionary<TextBatchKey, int> _textBatchMap = new();
        private readonly List<TextBatchBucket> _textBatchBuckets = new();
        private readonly Dictionary<TextSpriteCacheKey, CachedTextSprite> _textSpriteCache = new();
        private readonly Dictionary<TextBatchKey, int> _textSpriteBatchMap = new();
        private readonly List<TextSpriteBatchBucket> _textSpriteBatchBuckets = new();
        private readonly Dictionary<MinimapMarkerRenderBucketKey, CachedMarkerSprite> _markerSpriteCache = new();
        private readonly RetainedBarLaneState[] _retainedBarLanes = new RetainedBarLaneState[LaneCount];
        private readonly RetainedTextSpriteLaneState[] _retainedTextSpriteLanes = new RetainedTextSpriteLaneState[LaneCount];
        private readonly SKPicture?[] _lanePictures = new SKPicture?[LaneCount];
        private readonly int[] _laneVersions = new int[LaneCount];
        private readonly float[] _lanePictureOffsetsX = new float[LaneCount];
        private readonly float[] _lanePictureOffsetsY = new float[LaneCount];
        private readonly LargeTextLaneState[] _largeTextLaneStates = new LargeTextLaneState[LaneCount];
        private readonly StringBuilder _runText = new();

        public SkiaOverlayRenderer()
        {
            Array.Fill(_laneVersions, -1);
            for (int i = 0; i < _largeTextLaneStates.Length; i++)
            {
                _largeTextLaneStates[i] = new LargeTextLaneState();
                _retainedBarLanes[i] = new RetainedBarLaneState();
                _retainedTextSpriteLanes[i] = new RetainedTextSpriteLaneState();
            }
        }

        public int CachedTextLayoutCount => _textLayoutCache.Count;

        public int RebuiltLaneCountLastFrame { get; private set; }
        public double LastUnderUiBarMs { get; private set; }
        public double LastUnderUiTextMs { get; private set; }
        public double LastBarBatchBuildMs { get; private set; }
        public double LastBarBatchDrawMs { get; private set; }
        public double LastTextBatchBuildMs { get; private set; }
        public double LastTextBatchDrawMs { get; private set; }
        public double LastMinimapMarkerBatchBuildMs { get; private set; }
        public double LastMinimapMarkerBatchDrawMs { get; private set; }
        public int LastBarBatchBucketCount { get; private set; }
        public int LastTextSpriteBatchBucketCount { get; private set; }
        public int LastMinimapMarkerBatchBucketCount { get; private set; }
        public int LastMinimapMarkerOrientationBatchBucketCount { get; private set; }
        public int LastMinimapMarkerSpriteCacheHits { get; private set; }
        public int LastMinimapMarkerSpriteCacheMisses { get; private set; }
        public int LastMinimapMarkerSpriteCacheClears { get; private set; }
        public int LastBarSpriteCacheHits { get; private set; }
        public int LastBarSpriteCacheMisses { get; private set; }
        public int LastBarSpriteCacheClears { get; private set; }
        public int LastTextSpriteCacheHits { get; private set; }
        public int LastTextSpriteCacheMisses { get; private set; }
        public int LastTextSpriteCacheClears { get; private set; }
        public int LastTextLayoutCacheHits { get; private set; }
        public int LastTextLayoutCacheMisses { get; private set; }
        public int LastTextLayoutCacheClears { get; private set; }
        public int BarSpriteCacheCount => _barSpriteCache.Count;
        public int TextSpriteCacheCount => _textSpriteCache.Count;
        public int MarkerSpriteCacheCount => _markerSpriteCache.Count;

        public void ResetFrameStats()
        {
            RebuiltLaneCountLastFrame = 0;
            LastUnderUiBarMs = 0d;
            LastUnderUiTextMs = 0d;
            LastBarBatchBuildMs = 0d;
            LastBarBatchDrawMs = 0d;
            LastTextBatchBuildMs = 0d;
            LastTextBatchDrawMs = 0d;
            LastMinimapMarkerBatchBuildMs = 0d;
            LastMinimapMarkerBatchDrawMs = 0d;
            LastBarBatchBucketCount = 0;
            LastTextSpriteBatchBucketCount = 0;
            LastMinimapMarkerBatchBucketCount = 0;
            LastMinimapMarkerOrientationBatchBucketCount = 0;
            LastMinimapMarkerSpriteCacheHits = 0;
            LastMinimapMarkerSpriteCacheMisses = 0;
            LastMinimapMarkerSpriteCacheClears = 0;
            LastBarSpriteCacheHits = 0;
            LastBarSpriteCacheMisses = 0;
            LastBarSpriteCacheClears = 0;
            LastTextSpriteCacheHits = 0;
            LastTextSpriteCacheMisses = 0;
            LastTextSpriteCacheClears = 0;
            LastTextLayoutCacheHits = 0;
            LastTextLayoutCacheMisses = 0;
            LastTextLayoutCacheClears = 0;
        }

        public void Render(PresentationOverlayScene scene, SKCanvas canvas, PresentationOverlayLayer layer)
        {
            if (scene == null)
            {
                throw new ArgumentNullException(nameof(scene));
            }

            if (canvas == null)
            {
                throw new ArgumentNullException(nameof(canvas));
            }

            for (int i = 0; i < RenderOrder.Length; i++)
            {
                PresentationOverlayItemKind kind = RenderOrder[i];
                RenderLane(scene, canvas, layer, kind, hasRefreshPlan: false, refreshDirtyLane: true);
                if (kind == PresentationOverlayItemKind.MinimapMarker)
                {
                    RenderDirectMinimapMarkers(scene, canvas, layer);
                }
            }
        }

        public void Render(
            PresentationOverlayScene scene,
            SKCanvas canvas,
            PresentationOverlayLayer layer,
            in PresentationOverlayLanePacer.LaneRefreshPlan refreshPlan)
        {
            if (scene == null)
            {
                throw new ArgumentNullException(nameof(scene));
            }

            if (canvas == null)
            {
                throw new ArgumentNullException(nameof(canvas));
            }

            for (int i = 0; i < RenderOrder.Length; i++)
            {
                PresentationOverlayItemKind kind = RenderOrder[i];
                long laneStart = Stopwatch.GetTimestamp();
                RenderLane(scene, canvas, layer, kind, hasRefreshPlan: true, refreshDirtyLane: refreshPlan.ShouldRefresh(kind));
                if (kind == PresentationOverlayItemKind.MinimapMarker)
                {
                    RenderDirectMinimapMarkers(scene, canvas, layer);
                }

                ObserveLaneRender(layer, kind, laneStart);
            }
        }

        private void RenderDirectMinimapMarkers(PresentationOverlayScene scene, SKCanvas canvas, PresentationOverlayLayer layer)
        {
            if (layer != PresentationOverlayLayer.TopMost ||
                scene.TopMostMinimapMarkers is not MinimapScreenMarkerBuffer markers ||
                markers.Count <= 0)
            {
                return;
            }

            DrawMinimapMarkersBatched(canvas, markers);
        }

        public void RenderLane(
            PresentationOverlayScene scene,
            SKCanvas canvas,
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind)
        {
            RenderLane(scene, canvas, layer, kind, hasRefreshPlan: false, refreshDirtyLane: true);
        }

        public bool RenderLaneIncremental(
            PresentationOverlayScene scene,
            SKCanvas canvas,
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind)
        {
            if (scene == null)
            {
                throw new ArgumentNullException(nameof(scene));
            }

            if (canvas == null)
            {
                throw new ArgumentNullException(nameof(canvas));
            }

            ReadOnlySpan<PresentationOverlayItem> dirtyRegions = scene.GetLaneDirtyRegionSpan(layer, kind);
            ReadOnlySpan<PresentationOverlayItem> mutated = scene.GetLaneMutatedSpan(layer, kind);
            if (dirtyRegions.Length == 0 && mutated.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < dirtyRegions.Length; i++)
            {
                ClearItemBounds(canvas, dirtyRegions[i]);
            }

            DrawLaneImmediate(canvas, kind, mutated);
            return true;
        }

        public void ClearLaneDirtyRegions(
            PresentationOverlayScene scene,
            SKCanvas canvas,
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind)
        {
            if (scene == null)
            {
                throw new ArgumentNullException(nameof(scene));
            }

            if (canvas == null)
            {
                throw new ArgumentNullException(nameof(canvas));
            }

            ReadOnlySpan<PresentationOverlayItem> dirtyRegions = scene.GetLaneDirtyRegionSpan(layer, kind);
            for (int i = 0; i < dirtyRegions.Length; i++)
            {
                ClearItemBounds(canvas, dirtyRegions[i]);
            }
        }

        public void RenderLaneMutated(
            PresentationOverlayScene scene,
            SKCanvas canvas,
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind)
        {
            if (scene == null)
            {
                throw new ArgumentNullException(nameof(scene));
            }

            if (canvas == null)
            {
                throw new ArgumentNullException(nameof(canvas));
            }

            ReadOnlySpan<PresentationOverlayItem> mutated = scene.GetLaneMutatedSpan(layer, kind);
            DrawLaneImmediate(canvas, kind, mutated);
        }

        private void RenderLane(
            PresentationOverlayScene scene,
            SKCanvas canvas,
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind,
            bool hasRefreshPlan,
            bool refreshDirtyLane)
        {
            if (scene == null)
            {
                throw new ArgumentNullException(nameof(scene));
            }

            if (canvas == null)
            {
                throw new ArgumentNullException(nameof(canvas));
            }

            int laneIndex = GetLaneIndex(layer, kind);
            int laneVersion = scene.GetLaneVersion(layer, kind);
            ReadOnlySpan<PresentationOverlayItem> span = scene.GetLaneSpan(layer, kind);
            if (span.Length == 0)
            {
                if (kind == PresentationOverlayItemKind.Text)
                {
                    ClearLargeTextLaneState(laneIndex);
                }

                if (_laneVersions[laneIndex] != laneVersion)
                {
                    InvalidateLanePicture(laneIndex);
                    _laneVersions[laneIndex] = laneVersion;
                }

                return;
            }

            bool isLargeUnderUiLane = ShouldRenderImmediate(layer, kind, span.Length);
            if (!hasRefreshPlan && isLargeUnderUiLane)
            {
                RenderLargeImmediateLane(scene, canvas, layer, kind, laneIndex, laneVersion, span);
                return;
            }

            if (hasRefreshPlan && isLargeUnderUiLane && kind is PresentationOverlayItemKind.Text or PresentationOverlayItemKind.Bar)
            {
                RenderPacedLargeLane(scene, canvas, layer, kind, laneIndex, laneVersion, span, refreshDirtyLane);
                return;
            }

            if (kind == PresentationOverlayItemKind.Text)
            {
                ClearLargeTextLaneState(laneIndex);
            }

            if (isLargeUnderUiLane)
            {
                if (_laneVersions[laneIndex] != laneVersion)
                {
                    InvalidateLanePicture(laneIndex);
                    _laneVersions[laneIndex] = laneVersion;
                }

                DrawLaneImmediate(canvas, kind, span);
                return;
            }

            if (_laneVersions[laneIndex] != laneVersion)
            {
                RebuildLanePicture(scene, layer, kind, laneIndex, laneVersion);
            }

            SKPicture? picture = _lanePictures[laneIndex];
            if (picture != null)
            {
                canvas.DrawPicture(picture);
            }
        }

        private void RenderLargeImmediateLane(
            PresentationOverlayScene scene,
            SKCanvas canvas,
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind,
            int laneIndex,
            int laneVersion,
            ReadOnlySpan<PresentationOverlayItem> span)
        {
            if (_laneVersions[laneIndex] == laneVersion)
            {
                DrawLanePictureOrHotpath(canvas, kind, laneIndex, span);
                return;
            }

            if (scene.GetLaneMutationKind(layer, kind) == PresentationOverlayLaneMutationKind.PositionOnly &&
                scene.TryGetLaneUniformTranslation(layer, kind, out Vector2 translation))
            {
                if (_lanePictures[laneIndex] != null)
                {
                    _lanePictureOffsetsX[laneIndex] += translation.X;
                    _lanePictureOffsetsY[laneIndex] += translation.Y;
                    _laneVersions[laneIndex] = laneVersion;
                    DrawLanePictureOrHotpath(canvas, kind, laneIndex, span);
                    return;
                }

                RebuildLanePicture(scene, layer, kind, laneIndex, laneVersion);
                DrawLanePictureOrHotpath(canvas, kind, laneIndex, span);
                return;
            }

            InvalidateLanePicture(laneIndex);
            _laneVersions[laneIndex] = laneVersion;
            DrawLargeLaneHotpath(canvas, kind, laneIndex, laneVersion, span);
        }

        public void Dispose()
        {
            ClearTextLayoutCache();
            ClearTextSpriteCache();
            ClearBarSpriteCache();
            ClearMarkerSpriteCache();
            foreach ((_, SKFont font) in _fontCache)
            {
                font.Dispose();
            }

            for (int i = 0; i < _lanePictures.Length; i++)
            {
                _lanePictures[i]?.Dispose();
                _lanePictures[i] = null;
                _largeTextLaneStates[i].Clear();
                _retainedBarLanes[i].DisposeAtlas();
                _retainedTextSpriteLanes[i].DisposeAtlas();
            }

            _fontCache.Clear();
            _fillPaint.Dispose();
            _strokePaint.Dispose();
            _textPaint.Dispose();
            _clearPaint.Dispose();
            _minimapMarkerAtlasBatch.Dispose();
        }

        private void DrawRect(SKCanvas canvas, in PresentationOverlayItem item)
        {
            SKRect rect = new(item.X, item.Y, item.X + item.Width, item.Y + item.Height);
            _fillPaint.Color = ToSkColor(item.Color0);
            canvas.DrawRect(rect, _fillPaint);

            if (item.Color1.W > 0.01f)
            {
                _strokePaint.Color = ToSkColor(item.Color1);
                canvas.DrawRect(rect, _strokePaint);
            }
        }

        private void DrawBar(SKCanvas canvas, in PresentationOverlayItem item)
        {
            CachedBarSprite sprite = GetBarSprite(item);
            canvas.DrawImage(sprite.Image, item.X, item.Y);
        }

        private void ClearItemBounds(SKCanvas canvas, in PresentationOverlayItem item)
        {
            SKRect bounds = ResolveItemBounds(in item);
            if (bounds.Width <= 0f || bounds.Height <= 0f)
            {
                return;
            }

            canvas.DrawRect(bounds, _clearPaint);
        }

        private static SKRect ResolveItemBounds(in PresentationOverlayItem item)
        {
            const float pad = 2f;
            return item.Kind switch
            {
                PresentationOverlayItemKind.Bar =>
                    new SKRect(item.X - pad, item.Y - pad, item.X + item.Width + pad, item.Y + item.Height + pad),
                PresentationOverlayItemKind.Text =>
                    new SKRect(item.X - pad, item.Y - pad, item.X + EstimateTextWidth(item.Text, item.FontSize) + pad, item.Y + Math.Max(1, item.FontSize) * 1.5f + pad),
                PresentationOverlayItemKind.Rect =>
                    new SKRect(item.X - pad, item.Y - pad, item.X + item.Width + pad, item.Y + item.Height + pad),
                PresentationOverlayItemKind.Line =>
                    new SKRect(
                        MathF.Min(item.X, item.Width) - MathF.Max(pad, item.Value0),
                        MathF.Min(item.Y, item.Height) - MathF.Max(pad, item.Value0),
                        MathF.Max(item.X, item.Width) + MathF.Max(pad, item.Value0),
                        MathF.Max(item.Y, item.Height) + MathF.Max(pad, item.Value0)),
                _ => SKRect.Empty
            };
        }

        private static float EstimateTextWidth(string? text, int fontSize)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0f;
            }

            int resolvedFontSize = fontSize <= 0 ? 16 : fontSize;
            return Math.Max(resolvedFontSize, text.Length * resolvedFontSize * 0.7f);
        }

        private void ObserveLaneRender(PresentationOverlayLayer layer, PresentationOverlayItemKind kind, long startTimestamp)
        {
            if (layer != PresentationOverlayLayer.UnderUi)
            {
                return;
            }

            double elapsedMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
            if (kind == PresentationOverlayItemKind.Bar)
            {
                LastUnderUiBarMs += elapsedMs;
            }
            else if (kind == PresentationOverlayItemKind.Text)
            {
                LastUnderUiTextMs += elapsedMs;
            }
        }

        private void DrawBarDirect(SKCanvas canvas, in PresentationOverlayItem item)
        {
            SKRect rect = new(item.X, item.Y, item.X + item.Width, item.Y + item.Height);
            _fillPaint.Color = ToSkColor(item.Color0);
            canvas.DrawRect(rect, _fillPaint);

            float clampedValue = Math.Clamp(item.Value0, 0f, 1f);
            if (clampedValue > 0f)
            {
                _fillPaint.Color = ToSkColor(item.Color1);
                canvas.DrawRect(item.X, item.Y, item.Width * clampedValue, item.Height, _fillPaint);
            }

            _strokePaint.Color = SKColors.Black;
            canvas.DrawRect(rect, _strokePaint);
        }

        private void DrawText(SKCanvas canvas, in PresentationOverlayItem item)
        {
            if (string.IsNullOrEmpty(item.Text))
            {
                return;
            }

            int fontSize = item.FontSize <= 0 ? 16 : item.FontSize;
            _textPaint.Color = ToSkColor(item.Color0);
            CachedTextLayout layout = GetTextLayout(item.Text, fontSize);
            float baselineY = item.Y + fontSize;
            for (int i = 0; i < layout.Runs.Length; i++)
            {
                CachedTextRun run = layout.Runs[i];
                if (run.Blob != null)
                {
                    canvas.DrawText(run.Blob, item.X + run.XOffset, baselineY, _textPaint);
                }
            }
        }

        private void DrawLine(SKCanvas canvas, in PresentationOverlayItem item)
        {
            if (item.Value0 <= 0f || item.Color0.W <= 0f)
            {
                return;
            }

            SKPaintStyle previousStyle = _strokePaint.Style;
            SKStrokeCap previousCap = _strokePaint.StrokeCap;
            float previousStrokeWidth = _strokePaint.StrokeWidth;
            _strokePaint.Style = SKPaintStyle.Stroke;
            _strokePaint.StrokeCap = SKStrokeCap.Round;
            _strokePaint.StrokeWidth = MathF.Max(1f, item.Value0);
            _strokePaint.Color = ToSkColor(item.Color0);
            try
            {
                canvas.DrawLine(item.X, item.Y, item.Width, item.Height, _strokePaint);
            }
            finally
            {
                _strokePaint.Style = previousStyle;
                _strokePaint.StrokeCap = previousCap;
                _strokePaint.StrokeWidth = previousStrokeWidth;
            }
        }

        private void DrawLaneImmediate(SKCanvas canvas, PresentationOverlayItemKind kind, ReadOnlySpan<PresentationOverlayItem> span)
        {
            if (kind == PresentationOverlayItemKind.Bar)
            {
                DrawBarBatched(canvas, span);
                return;
            }

            if (kind == PresentationOverlayItemKind.Text)
            {
                DrawTextBatched(canvas, span);
                return;
            }

            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationOverlayItem item = ref span[i];
                switch (kind)
                {
                    case PresentationOverlayItemKind.Rect:
                        DrawRect(canvas, item);
                        break;

                    case PresentationOverlayItemKind.Line:
                        DrawLine(canvas, item);
                        break;
                }
            }
        }

        private void DrawLargeLaneHotpath(
            SKCanvas canvas,
            PresentationOverlayItemKind kind,
            int laneIndex,
            int laneVersion,
            ReadOnlySpan<PresentationOverlayItem> span)
        {
            switch (kind)
            {
                case PresentationOverlayItemKind.Bar:
                    DrawBarRetainedBatched(canvas, laneIndex, laneVersion, span);
                    break;

                case PresentationOverlayItemKind.Text:
                    if (CanUseRetainedTextSprites(span))
                    {
                        DrawTextSpriteRetainedBatched(canvas, laneIndex, laneVersion, span);
                    }
                    else
                    {
                        DrawTextBatched(canvas, span);
                    }

                    break;

                default:
                    DrawLaneImmediate(canvas, kind, span);
                    break;
            }
        }

        private void RenderPacedLargeLane(
            PresentationOverlayScene scene,
            SKCanvas canvas,
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind,
            int laneIndex,
            int laneVersion,
            ReadOnlySpan<PresentationOverlayItem> span,
            bool refreshDirtyLane)
        {
            if (_laneVersions[laneIndex] == laneVersion)
            {
                if (kind == PresentationOverlayItemKind.Text)
                {
                    RenderDeferredLargeTextLane(canvas, laneIndex, laneVersion, span, allowRefresh: true);
                    return;
                }

                DrawLanePictureOrHotpath(canvas, kind, laneIndex, span);
                return;
            }

            if (scene.GetLaneMutationKind(layer, kind) == PresentationOverlayLaneMutationKind.PositionOnly &&
                scene.TryGetLaneUniformTranslation(layer, kind, out Vector2 translation) &&
                _lanePictures[laneIndex] != null)
            {
                _lanePictureOffsetsX[laneIndex] += translation.X;
                _lanePictureOffsetsY[laneIndex] += translation.Y;
                _laneVersions[laneIndex] = laneVersion;
                DrawLanePictureOrHotpath(canvas, kind, laneIndex, span);
                return;
            }

            if (refreshDirtyLane || _lanePictures[laneIndex] == null)
            {
                if (layer == PresentationOverlayLayer.UnderUi &&
                    kind is PresentationOverlayItemKind.Text or PresentationOverlayItemKind.Bar)
                {
                    InvalidateLanePicture(laneIndex);
                    _laneVersions[laneIndex] = laneVersion;
                    DrawLargeLaneHotpath(canvas, kind, laneIndex, laneVersion, span);
                    return;
                }

                RebuildLanePicture(scene, layer, kind, laneIndex, laneVersion);
            }

            DrawLanePictureOrHotpath(canvas, kind, laneIndex, span);
        }

        private void DrawBarBatched(SKCanvas canvas, ReadOnlySpan<PresentationOverlayItem> span)
        {
            long buildStart = Stopwatch.GetTimestamp();
            _barBatchMap.Clear();
            int bucketCount = 0;
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationOverlayItem item = ref span[i];
                BarSpriteCacheKey key = CreateBarSpriteCacheKey(item);
                if (!_barBatchMap.TryGetValue(key, out int bucketIndex))
                {
                    if (bucketCount >= _barBatchBuckets.Count)
                    {
                        _barBatchBuckets.Add(new BarBatchBucket());
                    }

                    bucketIndex = bucketCount++;
                    _barBatchMap[key] = bucketIndex;
                    CachedBarSprite sprite = GetBarSprite(key, item);
                    _barBatchBuckets[bucketIndex].Reset(sprite.Image, item.Width, item.Height);
                }

                _barBatchBuckets[bucketIndex].Add(item.X, item.Y);
            }

            LastBarBatchBucketCount += bucketCount;
            LastBarBatchBuildMs += ElapsedMs(buildStart);
            long drawStart = Stopwatch.GetTimestamp();
            for (int bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
            {
                BarBatchBucket bucket = _barBatchBuckets[bucketIndex];
                if (bucket.Count == 1)
                {
                    canvas.DrawImage(bucket.Image, bucket.X[0], bucket.Y[0]);
                    continue;
                }

                DrawAtlasCount(canvas, bucket.Image, bucket.Sprites, bucket.Transforms, bucket.Count);
            }
            LastBarBatchDrawMs += ElapsedMs(drawStart);
        }

        private void DrawMinimapMarkersBatched(SKCanvas canvas, MinimapScreenMarkerBuffer markers)
        {
            long buildStart = Stopwatch.GetTimestamp();
            int bucketCount = markers.BucketCount;
            int orientationBucketCount = 0;
            for (int bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
            {
                MinimapScreenMarkerBucket screenBucket = markers.GetBucket(bucketIndex);
                if (screenBucket.Count <= 0)
                {
                    continue;
                }

                if (screenBucket.Key.HasOrientation)
                {
                    orientationBucketCount++;
                }
            }

            LastMinimapMarkerBatchBucketCount += bucketCount;
            LastMinimapMarkerOrientationBatchBucketCount += orientationBucketCount;
            LastMinimapMarkerBatchBuildMs += ElapsedMs(buildStart);
            if (bucketCount <= 0)
            {
                return;
            }

            buildStart = Stopwatch.GetTimestamp();
            _minimapMarkerAtlasBatch.Build(markers, this);
            LastMinimapMarkerBatchBuildMs += ElapsedMs(buildStart);

            long drawStart = Stopwatch.GetTimestamp();
            _minimapMarkerAtlasBatch.DrawTo(canvas);
            LastMinimapMarkerBatchDrawMs += ElapsedMs(drawStart);
        }

        private void DrawBarRetainedBatched(
            SKCanvas canvas,
            int laneIndex,
            int laneVersion,
            ReadOnlySpan<PresentationOverlayItem> span)
        {
            RetainedBarLaneState state = _retainedBarLanes[laneIndex];
            long buildStart = Stopwatch.GetTimestamp();
            if (state.LastVersion != laneVersion)
            {
                if (!TryUpdateRetainedBarLanePositions(state, laneVersion, span))
                {
                    UpdateRetainedBarLane(state, laneVersion, span);
                }
            }

            LastBarBatchBuildMs += ElapsedMs(buildStart);
            long drawStart = Stopwatch.GetTimestamp();
            int activeBucketCount = DrawRetainedBarAtlas(canvas, state);
            LastBarBatchBucketCount += activeBucketCount;
            LastBarBatchDrawMs += ElapsedMs(drawStart);
        }

        private int DrawRetainedBarAtlas(SKCanvas canvas, RetainedBarLaneState state)
        {
            EnsureRetainedBarAtlas(state);
            if (state.AtlasImage == null)
            {
                return 0;
            }

            int activeBucketCount = 0;
            for (int bucketIndex = 0; bucketIndex < state.Buckets.Count; bucketIndex++)
            {
                RetainedBarBatchBucket bucket = state.Buckets[bucketIndex];
                int count = bucket.Count;
                if (count <= 0)
                {
                    continue;
                }

                bucket.SetSpriteRect(state.AtlasSprites[bucketIndex]);
                DrawAtlasCount(canvas, state.AtlasImage, bucket.Sprites, bucket.Transforms, count);
                activeBucketCount++;
            }

            return activeBucketCount;
        }

        private void UpdateRetainedBarLane(
            RetainedBarLaneState state,
            int laneVersion,
            ReadOnlySpan<PresentationOverlayItem> span)
        {
            int stamp = state.NextStamp();
            state.BeginVisibleFrame();
            state.EnsureOrderCapacity(span.Length);
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationOverlayItem item = ref span[i];
                int stableId = item.StableId;
                if (stableId <= 0)
                {
                    RebuildRetainedBarLane(state, laneVersion, span);
                    return;
                }

                if (state.ItemsByStableId.TryGetValue(stableId, out RetainedBarEntry entry))
                {
                    if (entry.DirtySerial == item.DirtySerial)
                    {
                        RetainedBarBatchBucket retainedBucket = state.Buckets[entry.BucketIndex];
                        retainedBucket.AddVisible(item.X, item.Y);
                        entry.SeenStamp = stamp;
                        state.ItemsByStableId[stableId] = entry;
                        state.OrderStableIds[i] = stableId;
                        state.OrderEntries[i] = entry;
                        continue;
                    }

                    BarSpriteCacheKey key = CreateBarSpriteCacheKey(item);
                    if (!entry.Key.Equals(key))
                    {
                        AddRetainedBarEntry(state, stableId, key, item, stamp, i);
                        continue;
                    }

                    RetainedBarBatchBucket bucket = state.Buckets[entry.BucketIndex];
                    bucket.AddVisible(item.X, item.Y);
                    entry.SeenStamp = stamp;
                    entry.DirtySerial = item.DirtySerial;
                    state.ItemsByStableId[stableId] = entry;
                    state.OrderStableIds[i] = stableId;
                    state.OrderEntries[i] = entry;
                    continue;
                }

                BarSpriteCacheKey newKey = CreateBarSpriteCacheKey(item);
                AddRetainedBarEntry(state, stableId, newKey, item, stamp, i);
            }

            state.OrderCount = span.Length;
            state.LastVersion = laneVersion;
        }

        private static bool TryUpdateRetainedBarLanePositions(
            RetainedBarLaneState state,
            int laneVersion,
            ReadOnlySpan<PresentationOverlayItem> span)
        {
            if (state.LastVersion < 0 ||
                state.OrderCount != span.Length ||
                state.OrderStableIds.Length < span.Length ||
                state.OrderEntries.Length < span.Length)
            {
                return false;
            }

            state.BeginVisibleFrame();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationOverlayItem item = ref span[i];
                int stableId = item.StableId;
                if (stableId <= 0 || state.OrderStableIds[i] != stableId)
                {
                    return false;
                }

                RetainedBarEntry entry = state.OrderEntries[i];
                if (entry.DirtySerial != item.DirtySerial)
                {
                    return false;
                }

                if ((uint)entry.BucketIndex >= (uint)state.Buckets.Count)
                {
                    return false;
                }

                RetainedBarBatchBucket bucket = state.Buckets[entry.BucketIndex];
                bucket.AddVisible(item.X, item.Y);
            }

            state.LastVersion = laneVersion;
            return true;
        }

        private void RebuildRetainedBarLane(
            RetainedBarLaneState state,
            int laneVersion,
            ReadOnlySpan<PresentationOverlayItem> span)
        {
            state.Clear();
            int stamp = state.NextStamp();
            state.BeginVisibleFrame();
            state.EnsureOrderCapacity(span.Length);
            int orderCount = 0;
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationOverlayItem item = ref span[i];
                if (item.StableId <= 0)
                {
                    continue;
                }

                AddRetainedBarEntry(state, item.StableId, CreateBarSpriteCacheKey(item), item, stamp, orderCount);
                orderCount++;
            }

            state.OrderCount = orderCount;
            state.LastVersion = laneVersion;
        }

        private void AddRetainedBarEntry(
            RetainedBarLaneState state,
            int stableId,
            in BarSpriteCacheKey key,
            in PresentationOverlayItem item,
            int stamp,
            int orderIndex)
        {
            int bucketIndex = GetOrCreateRetainedBarBucket(state, key, item);
            RetainedBarBatchBucket bucket = state.Buckets[bucketIndex];
            int slotIndex = bucket.Add(stableId, item.X, item.Y);
            RetainedBarEntry entry = new(bucketIndex, slotIndex, key, item.DirtySerial, stamp);
            state.ItemsByStableId[stableId] = entry;
            if ((uint)orderIndex < (uint)state.OrderStableIds.Length)
            {
                state.OrderStableIds[orderIndex] = stableId;
                state.OrderEntries[orderIndex] = entry;
            }
        }

        private int GetOrCreateRetainedBarBucket(
            RetainedBarLaneState state,
            in BarSpriteCacheKey key,
            in PresentationOverlayItem item)
        {
            if (state.BucketIndexByKey.TryGetValue(key, out int bucketIndex))
            {
                return bucketIndex;
            }

            bucketIndex = state.Buckets.Count;
            CachedBarSprite sprite = GetBarSprite(key, item);
            state.Buckets.Add(new RetainedBarBatchBucket(sprite.Image));
            state.BucketIndexByKey[key] = bucketIndex;
            state.AtlasDirty = true;
            return bucketIndex;
        }

        private void RemoveUnseenRetainedBars(RetainedBarLaneState state, int stamp)
        {
            state.RemovedStableIds.Clear();
            foreach ((int stableId, RetainedBarEntry entry) in state.ItemsByStableId)
            {
                if (entry.SeenStamp != stamp)
                {
                    state.RemovedStableIds.Add(stableId);
                }
            }

            for (int i = 0; i < state.RemovedStableIds.Count; i++)
            {
                int stableId = state.RemovedStableIds[i];
                if (state.ItemsByStableId.TryGetValue(stableId, out RetainedBarEntry entry))
                {
                    RemoveRetainedBarEntry(state, stableId, entry);
                }
            }
        }

        private static void RemoveRetainedBarEntry(
            RetainedBarLaneState state,
            int stableId,
            in RetainedBarEntry entry)
        {
            RetainedBarBatchBucket bucket = state.Buckets[entry.BucketIndex];
            int movedStableId = bucket.RemoveAt(entry.SlotIndex);
            state.ItemsByStableId.Remove(stableId);
            if (movedStableId > 0 &&
                movedStableId != stableId &&
                state.ItemsByStableId.TryGetValue(movedStableId, out RetainedBarEntry movedEntry))
            {
                movedEntry.SlotIndex = entry.SlotIndex;
                state.ItemsByStableId[movedStableId] = movedEntry;
                UpdateRetainedBarOrderEntry(state, movedStableId, movedEntry);
            }
        }

        private static void UpdateRetainedBarOrderEntry(
            RetainedBarLaneState state,
            int stableId,
            in RetainedBarEntry entry)
        {
            for (int i = 0; i < state.OrderCount; i++)
            {
                if (state.OrderStableIds[i] == stableId)
                {
                    state.OrderEntries[i] = entry;
                    return;
                }
            }
        }

        private static void EnsureRetainedBarAtlas(RetainedBarLaneState state)
        {
            int bucketCount = state.Buckets.Count;
            if (!state.AtlasDirty &&
                state.AtlasImage != null &&
                state.AtlasSprites.Length >= bucketCount)
            {
                return;
            }

            state.DisposeAtlas();
            if (bucketCount <= 0)
            {
                state.AtlasDirty = false;
                return;
            }

            int atlasWidth = 0;
            int atlasHeight = 0;
            state.EnsureAtlasSpriteCapacity(bucketCount);
            for (int i = 0; i < bucketCount; i++)
            {
                SKImage image = state.Buckets[i].Image;
                state.AtlasSprites[i] = new SKRect(atlasWidth, 0f, atlasWidth + image.Width, image.Height);
                atlasWidth += image.Width;
                atlasHeight = Math.Max(atlasHeight, image.Height);
            }

            if (atlasWidth <= 0 || atlasHeight <= 0)
            {
                state.AtlasDirty = false;
                return;
            }

            using SKSurface surface = SKSurface.Create(new SKImageInfo(atlasWidth, atlasHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            SKCanvas atlasCanvas = surface.Canvas;
            atlasCanvas.Clear(SKColors.Transparent);
            for (int i = 0; i < bucketCount; i++)
            {
                SKRect sprite = state.AtlasSprites[i];
                atlasCanvas.DrawImage(state.Buckets[i].Image, sprite.Left, 0f);
            }

            state.AtlasImage = surface.Snapshot();
            state.AtlasDirty = false;
        }

        private void DrawBarDirectBatched(SKCanvas canvas, ReadOnlySpan<PresentationOverlayItem> span)
        {
            long drawStart = Stopwatch.GetTimestamp();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationOverlayItem item = ref span[i];
                SKRect rect = new(item.X, item.Y, item.X + item.Width, item.Y + item.Height);
                _fillPaint.Color = ToSkColor(item.Color0);
                canvas.DrawRect(rect, _fillPaint);

                float clampedValue = Math.Clamp(item.Value0, 0f, 1f);
                if (clampedValue > 0f)
                {
                    _fillPaint.Color = ToSkColor(item.Color1);
                    canvas.DrawRect(item.X, item.Y, item.Width * clampedValue, item.Height, _fillPaint);
                }

                _strokePaint.Color = SKColors.Black;
                canvas.DrawRect(rect, _strokePaint);
            }

            LastBarBatchBucketCount += 1;
            LastBarBatchDrawMs += ElapsedMs(drawStart);
        }

        private void DrawTextBatched(SKCanvas canvas, ReadOnlySpan<PresentationOverlayItem> span)
        {
            long buildStart = Stopwatch.GetTimestamp();
            _textBatchMap.Clear();
            int bucketCount = 0;

            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationOverlayItem item = ref span[i];
                if (string.IsNullOrEmpty(item.Text))
                {
                    continue;
                }

                int fontSize = item.FontSize <= 0 ? 16 : item.FontSize;
                SKColor color = ToSkColor(item.Color0);
                var key = new TextBatchKey(item.Text, fontSize, ToColorKey(color));
                if (!_textBatchMap.TryGetValue(key, out int bucketIndex))
                {
                    if (bucketCount >= _textBatchBuckets.Count)
                    {
                        _textBatchBuckets.Add(new TextBatchBucket());
                    }

                    CachedTextLayout layout = GetTextLayout(item.Text, fontSize);
                    bucketIndex = bucketCount++;
                    _textBatchMap[key] = bucketIndex;
                    _textBatchBuckets[bucketIndex].Reset(layout, color);
                }

                _textBatchBuckets[bucketIndex].Add(item.X, item.Y + fontSize);
            }

            LastTextSpriteBatchBucketCount += bucketCount;
            LastTextBatchBuildMs += ElapsedMs(buildStart);
            long drawStart = Stopwatch.GetTimestamp();
            int chunkStart = 0;
            while (chunkStart < bucketCount)
            {
                SKColor chunkColor = _textBatchBuckets[chunkStart].Color;
                int chunkEnd = chunkStart + 1;
                while (chunkEnd < bucketCount &&
                    chunkEnd - chunkStart < TextBatchBucketsPerBlob &&
                    _textBatchBuckets[chunkEnd].Color == chunkColor)
                {
                    chunkEnd++;
                }

                _textPaint.Color = chunkColor;
                using var chunkBuilder = new SKTextBlobBuilder();

                for (int bucketIndex = chunkStart; bucketIndex < chunkEnd; bucketIndex++)
                {
                    TextBatchBucket bucket = _textBatchBuckets[bucketIndex];
                    for (int runIndex = 0; runIndex < bucket.Layout.Runs.Length; runIndex++)
                    {
                        CachedTextRun run = bucket.Layout.Runs[runIndex];
                        if (run.Glyphs.Length == 0)
                        {
                            continue;
                        }

                        int totalGlyphCount = run.Glyphs.Length * bucket.Count;
                        SKRawRunBuffer<SKPoint> buffer = chunkBuilder.AllocateRawPositionedRun(run.Font, totalGlyphCount);
                        int glyphOffset = 0;
                        for (int itemIndex = 0; itemIndex < bucket.Count; itemIndex++)
                        {
                            run.Glyphs.AsSpan().CopyTo(buffer.Glyphs.Slice(glyphOffset, run.Glyphs.Length));
                            float originX = bucket.X[itemIndex] + run.XOffset;
                            float originY = bucket.BaselineY[itemIndex];
                            for (int glyphIndex = 0; glyphIndex < run.Glyphs.Length; glyphIndex++)
                            {
                                SKPoint glyphPosition = run.GlyphPositions[glyphIndex];
                                buffer.Positions[glyphOffset + glyphIndex] = new SKPoint(originX + glyphPosition.X, originY + glyphPosition.Y);
                            }

                            glyphOffset += run.Glyphs.Length;
                        }
                    }
                }

                using SKTextBlob? chunkBlob = chunkBuilder.Build();
                if (chunkBlob != null)
                {
                    canvas.DrawText(chunkBlob, 0f, 0f, _textPaint);
                }

                chunkStart = chunkEnd;
            }

            LastTextBatchDrawMs += ElapsedMs(drawStart);
        }

        private CachedTextLayout GetTextLayout(string text, int fontSize)
        {
            var cacheKey = new TextLayoutCacheKey(text, fontSize);
            if (_textLayoutCache.TryGetValue(cacheKey, out CachedTextLayout? cached))
            {
                LastTextLayoutCacheHits++;
                return cached;
            }

            if (_textLayoutCache.Count >= MaxTextLayoutCacheEntries)
            {
                // Avoid disposing layouts that may still be referenced by the current frame's batching work.
                _textLayoutCache.Clear();
                LastTextLayoutCacheClears++;
            }

            LastTextLayoutCacheMisses++;
            var runs = new List<CachedTextRun>(8);
            _runText.Clear();

            SKTypeface? activeTypeface = null;
            float cursorX = 0f;
            TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);
            while (enumerator.MoveNext())
            {
                string element = enumerator.GetTextElement();
                SKTypeface typeface = UiFontRegistry.ResolveTypefaceForTextElement(null, bold: false, element);
                if (activeTypeface != null && !UiFontRegistry.SameTypeface(activeTypeface, typeface))
                {
                    cursorX = FlushRun(runs, activeTypeface, fontSize, cursorX);
                    _runText.Clear();
                }

                activeTypeface = typeface;
                _runText.Append(element);
            }

            if (_runText.Length > 0 && activeTypeface != null)
            {
                cursorX = FlushRun(runs, activeTypeface, fontSize, cursorX);
            }

            var created = new CachedTextLayout(runs.ToArray(), cursorX);
            _textLayoutCache[cacheKey] = created;
            return created;
        }

        private float FlushRun(List<CachedTextRun> runs, SKTypeface typeface, int fontSize, float cursorX)
        {
            string runText = _runText.ToString();
            SKFont font = GetFont(typeface, fontSize);
            ushort[] glyphs = font.GetGlyphs(runText);
            SKPoint[] glyphPositions = font.GetGlyphPositions(glyphs);
            SKTextBlob? blob = SKTextBlob.Create(runText, font);
            float width = font.MeasureText(runText, _textPaint);
            runs.Add(new CachedTextRun(blob, cursorX, font, glyphs, glyphPositions));
            return cursorX + width;
        }

        private void RebuildLanePicture(
            PresentationOverlayScene scene,
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind,
            int laneIndex,
            int laneVersion)
        {
            _lanePictures[laneIndex]?.Dispose();
            _lanePictures[laneIndex] = null;
            _laneVersions[laneIndex] = laneVersion;
            _lanePictureOffsetsX[laneIndex] = 0f;
            _lanePictureOffsetsY[laneIndex] = 0f;

            ReadOnlySpan<PresentationOverlayItem> span = scene.GetLaneSpan(layer, kind);
            if (span.Length == 0)
            {
                return;
            }

            using var recorder = new SKPictureRecorder();
            SKCanvas pictureCanvas = recorder.BeginRecording(new SKRect(-1f, -1f, 4096f, 4096f));
            if (kind is PresentationOverlayItemKind.Bar or PresentationOverlayItemKind.Text)
            {
                DrawLaneImmediate(pictureCanvas, kind, span);
            }
            else
            {
                for (int i = 0; i < span.Length; i++)
                {
                    ref readonly PresentationOverlayItem item = ref span[i];
                    switch (kind)
                    {
                        case PresentationOverlayItemKind.Rect:
                            DrawRect(pictureCanvas, item);
                            break;

                        case PresentationOverlayItemKind.Line:
                            DrawLine(pictureCanvas, item);
                            break;
                    }
                }
            }

            _lanePictures[laneIndex] = recorder.EndRecording();
            RebuiltLaneCountLastFrame++;
        }

        private void RenderDeferredLargeTextLane(
            SKCanvas canvas,
            int laneIndex,
            int laneVersion,
            ReadOnlySpan<PresentationOverlayItem> span,
            bool allowRefresh)
        {
            LargeTextLaneState state = _largeTextLaneStates[laneIndex];
            int chunkCount = (span.Length + DeferredLargeTextChunkSize - 1) / DeferredLargeTextChunkSize;
            bool requiresFullReset = state.ChunkCount != chunkCount;
            state.EnsureChunkCapacity(chunkCount);

            if (requiresFullReset)
            {
                state.InvalidateAll();
            }

            if (state.HasMissingChunks)
            {
                for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
                {
                    RebuildDeferredLargeTextChunk(state, chunkIndex, laneVersion, span);
                }
            }
            else if (allowRefresh)
            {
                int rebuiltChunkCount = 0;
                while (rebuiltChunkCount < DeferredLargeTextChunksPerFrame)
                {
                    int chunkIndex = state.FindNextStaleChunk(laneVersion);
                    if (chunkIndex < 0)
                    {
                        break;
                    }

                    RebuildDeferredLargeTextChunk(state, chunkIndex, laneVersion, span);
                    rebuiltChunkCount++;
                }
            }

            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                SKPicture? picture = state.GetPicture(chunkIndex);
                if (picture == null)
                {
                    continue;
                }

                canvas.DrawPicture(picture);
            }
        }

        private void DrawTextDirect(SKCanvas canvas, ReadOnlySpan<PresentationOverlayItem> span)
        {
            uint currentColorKey = uint.MaxValue;
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationOverlayItem item = ref span[i];
                if (string.IsNullOrEmpty(item.Text))
                {
                    continue;
                }

                SKColor color = ToSkColor(item.Color0);
                uint colorKey = ToColorKey(color);
                if (colorKey != currentColorKey)
                {
                    _textPaint.Color = color;
                    currentColorKey = colorKey;
                }

                int fontSize = item.FontSize <= 0 ? 16 : item.FontSize;
                float baselineY = item.Y + fontSize;
                CachedTextLayout layout = GetTextLayout(item.Text, fontSize);
                for (int runIndex = 0; runIndex < layout.Runs.Length; runIndex++)
                {
                    CachedTextRun run = layout.Runs[runIndex];
                    if (run.Blob != null)
                    {
                        canvas.DrawText(run.Blob, item.X + run.XOffset, baselineY, _textPaint);
                    }
                }
            }
        }

        private static bool IsAsciiText(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] > 0x7f)
                {
                    return false;
                }
            }

            return true;
        }

        private void DrawTextSpriteBatched(SKCanvas canvas, ReadOnlySpan<PresentationOverlayItem> span)
        {
            long buildStart = Stopwatch.GetTimestamp();
            _textSpriteBatchMap.Clear();
            int bucketCount = 0;

            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationOverlayItem item = ref span[i];
                if (string.IsNullOrEmpty(item.Text))
                {
                    continue;
                }

                int fontSize = item.FontSize <= 0 ? 16 : item.FontSize;
                SKColor color = ToSkColor(item.Color0);
                var key = new TextBatchKey(item.Text, fontSize, ToColorKey(color));
                if (!_textSpriteBatchMap.TryGetValue(key, out int bucketIndex))
                {
                    if (bucketCount >= _textSpriteBatchBuckets.Count)
                    {
                        _textSpriteBatchBuckets.Add(new TextSpriteBatchBucket());
                    }

                    bucketIndex = bucketCount++;
                    _textSpriteBatchMap[key] = bucketIndex;
                    CachedTextSprite sprite = GetTextSprite(item.Text, fontSize, color);
                    _textSpriteBatchBuckets[bucketIndex].Reset(sprite);
                }

                CachedTextSprite cachedSprite = _textSpriteBatchBuckets[bucketIndex].Sprite;
                float drawY = (item.Y + fontSize) - cachedSprite.BaselineY;
                _textSpriteBatchBuckets[bucketIndex].Add(item.X, drawY);
            }

            LastTextSpriteBatchBucketCount += bucketCount;
            LastTextBatchBuildMs += ElapsedMs(buildStart);
            long drawStart = Stopwatch.GetTimestamp();
            for (int bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
            {
                TextSpriteBatchBucket bucket = _textSpriteBatchBuckets[bucketIndex];
                if (bucket.Count == 1)
                {
                    canvas.DrawImage(bucket.Sprite.Image, bucket.X[0], bucket.Y[0]);
                    continue;
                }

                DrawAtlasCount(canvas, bucket.Sprite.Image, bucket.Sprites, bucket.Transforms, bucket.Count);
            }
            LastTextBatchDrawMs += ElapsedMs(drawStart);
        }

        private void DrawTextSpriteRetainedBatched(
            SKCanvas canvas,
            int laneIndex,
            int laneVersion,
            ReadOnlySpan<PresentationOverlayItem> span)
        {
            RetainedTextSpriteLaneState state = _retainedTextSpriteLanes[laneIndex];
            long buildStart = Stopwatch.GetTimestamp();
            if (state.LastVersion != laneVersion)
            {
                if (!TryUpdateRetainedTextSpriteLanePositions(state, laneVersion, span))
                {
                    UpdateRetainedTextSpriteLane(state, laneVersion, span);
                }
            }

            LastTextBatchBuildMs += ElapsedMs(buildStart);
            long drawStart = Stopwatch.GetTimestamp();
            int activeBucketCount = DrawRetainedTextAtlas(canvas, state);
            LastTextSpriteBatchBucketCount += activeBucketCount;
            LastTextBatchDrawMs += ElapsedMs(drawStart);
        }

        private static bool CanUseRetainedTextSprites(ReadOnlySpan<PresentationOverlayItem> span)
        {
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationOverlayItem item = ref span[i];
                if (!string.IsNullOrEmpty(item.Text) && item.StableId <= 0)
                {
                    return false;
                }
            }

            return true;
        }

        private int DrawRetainedTextAtlas(SKCanvas canvas, RetainedTextSpriteLaneState state)
        {
            EnsureRetainedTextAtlas(state);
            if (state.AtlasImage == null)
            {
                return 0;
            }

            int activeBucketCount = 0;
            for (int bucketIndex = 0; bucketIndex < state.Buckets.Count; bucketIndex++)
            {
                RetainedTextSpriteBatchBucket bucket = state.Buckets[bucketIndex];
                int count = bucket.Count;
                if (count <= 0)
                {
                    continue;
                }

                bucket.SetSpriteRect(state.AtlasSprites[bucketIndex]);
                DrawAtlasCount(canvas, state.AtlasImage, bucket.Sprites, bucket.Transforms, count);
                activeBucketCount++;
            }

            return activeBucketCount;
        }

        private void UpdateRetainedTextSpriteLane(
            RetainedTextSpriteLaneState state,
            int laneVersion,
            ReadOnlySpan<PresentationOverlayItem> span)
        {
            int stamp = state.NextStamp();
            state.BeginVisibleFrame();
            state.EnsureOrderCapacity(span.Length);
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationOverlayItem item = ref span[i];
                if (string.IsNullOrEmpty(item.Text))
                {
                    continue;
                }

                int stableId = item.StableId;
                if (stableId <= 0)
                {
                    RebuildRetainedTextSpriteLane(state, laneVersion, span);
                    return;
                }

                if (state.ItemsByStableId.TryGetValue(stableId, out RetainedTextSpriteEntry entry))
                {
                    if (entry.DirtySerial == item.DirtySerial)
                    {
                        RetainedTextSpriteBatchBucket retainedBucket = state.Buckets[entry.BucketIndex];
                        int retainedFontSize = entry.FontSize <= 0 ? 16 : entry.FontSize;
                        float retainedDrawY = (item.Y + retainedFontSize) - retainedBucket.Sprite.BaselineY;
                        retainedBucket.AddVisible(item.X, retainedDrawY);
                        entry.SeenStamp = stamp;
                        state.ItemsByStableId[stableId] = entry;
                        state.OrderStableIds[i] = stableId;
                        state.OrderEntries[i] = entry;
                        state.OrderFontSizes[i] = retainedFontSize;
                        continue;
                    }

                    int fontSize = item.FontSize <= 0 ? 16 : item.FontSize;
                    SKColor color = ToSkColor(item.Color0);
                    var key = new TextBatchKey(item.Text, fontSize, ToColorKey(color));
                    if (!entry.Key.Equals(key))
                    {
                        AddRetainedTextSpriteEntry(state, stableId, key, item, fontSize, color, stamp, i);
                        continue;
                    }

                    RetainedTextSpriteBatchBucket bucket = state.Buckets[entry.BucketIndex];
                    float drawY = (item.Y + fontSize) - bucket.Sprite.BaselineY;
                    bucket.AddVisible(item.X, drawY);
                    entry.SeenStamp = stamp;
                    entry.DirtySerial = item.DirtySerial;
                    entry.FontSize = fontSize;
                    state.ItemsByStableId[stableId] = entry;
                    state.OrderStableIds[i] = stableId;
                    state.OrderEntries[i] = entry;
                    state.OrderFontSizes[i] = fontSize;
                    continue;
                }

                int newFontSize = item.FontSize <= 0 ? 16 : item.FontSize;
                SKColor newColor = ToSkColor(item.Color0);
                var newKey = new TextBatchKey(item.Text, newFontSize, ToColorKey(newColor));
                AddRetainedTextSpriteEntry(state, stableId, newKey, item, newFontSize, newColor, stamp, i);
            }

            state.OrderCount = span.Length;
            state.LastVersion = laneVersion;
        }

        private static bool TryUpdateRetainedTextSpriteLanePositions(
            RetainedTextSpriteLaneState state,
            int laneVersion,
            ReadOnlySpan<PresentationOverlayItem> span)
        {
            if (state.LastVersion < 0 ||
                state.OrderCount != span.Length ||
                state.OrderStableIds.Length < span.Length ||
                state.OrderEntries.Length < span.Length ||
                state.OrderFontSizes.Length < span.Length)
            {
                return false;
            }

            state.BeginVisibleFrame();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationOverlayItem item = ref span[i];
                int stableId = item.StableId;
                if (stableId <= 0 ||
                    string.IsNullOrEmpty(item.Text) ||
                    state.OrderStableIds[i] != stableId)
                {
                    return false;
                }

                RetainedTextSpriteEntry entry = state.OrderEntries[i];
                if (entry.DirtySerial != item.DirtySerial)
                {
                    return false;
                }

                if ((uint)entry.BucketIndex >= (uint)state.Buckets.Count)
                {
                    return false;
                }

                RetainedTextSpriteBatchBucket bucket = state.Buckets[entry.BucketIndex];
                int fontSize = state.OrderFontSizes[i];
                float drawY = (item.Y + fontSize) - bucket.Sprite.BaselineY;
                bucket.AddVisible(item.X, drawY);
            }

            state.LastVersion = laneVersion;
            return true;
        }

        private void RebuildRetainedTextSpriteLane(
            RetainedTextSpriteLaneState state,
            int laneVersion,
            ReadOnlySpan<PresentationOverlayItem> span)
        {
            state.Clear();
            int stamp = state.NextStamp();
            state.BeginVisibleFrame();
            state.EnsureOrderCapacity(span.Length);
            int orderCount = 0;
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationOverlayItem item = ref span[i];
                if (item.StableId <= 0 || string.IsNullOrEmpty(item.Text))
                {
                    continue;
                }

                int fontSize = item.FontSize <= 0 ? 16 : item.FontSize;
                SKColor color = ToSkColor(item.Color0);
                AddRetainedTextSpriteEntry(
                    state,
                    item.StableId,
                    new TextBatchKey(item.Text, fontSize, ToColorKey(color)),
                    item,
                    fontSize,
                    color,
                    stamp,
                    orderCount);
                orderCount++;
            }

            state.OrderCount = orderCount;
            state.LastVersion = laneVersion;
        }

        private void AddRetainedTextSpriteEntry(
            RetainedTextSpriteLaneState state,
            int stableId,
            in TextBatchKey key,
            in PresentationOverlayItem item,
            int fontSize,
            SKColor color,
            int stamp,
            int orderIndex)
        {
            int bucketIndex = GetOrCreateRetainedTextSpriteBucket(state, key, item.Text!, fontSize, color);
            RetainedTextSpriteBatchBucket bucket = state.Buckets[bucketIndex];
            float drawY = (item.Y + fontSize) - bucket.Sprite.BaselineY;
            int slotIndex = bucket.Add(stableId, item.X, drawY);
            RetainedTextSpriteEntry entry = new(bucketIndex, slotIndex, key, item.DirtySerial, fontSize, stamp);
            state.ItemsByStableId[stableId] = entry;
            if ((uint)orderIndex < (uint)state.OrderStableIds.Length)
            {
                state.OrderStableIds[orderIndex] = stableId;
                state.OrderEntries[orderIndex] = entry;
                state.OrderFontSizes[orderIndex] = fontSize;
            }
        }

        private int GetOrCreateRetainedTextSpriteBucket(
            RetainedTextSpriteLaneState state,
            in TextBatchKey key,
            string text,
            int fontSize,
            SKColor color)
        {
            if (state.BucketIndexByKey.TryGetValue(key, out int bucketIndex))
            {
                return bucketIndex;
            }

            bucketIndex = state.Buckets.Count;
            CachedTextSprite sprite = GetTextSprite(text, fontSize, color);
            state.Buckets.Add(new RetainedTextSpriteBatchBucket(sprite));
            state.BucketIndexByKey[key] = bucketIndex;
            state.AtlasDirty = true;
            return bucketIndex;
        }

        private void RemoveUnseenRetainedTextSprites(RetainedTextSpriteLaneState state, int stamp)
        {
            state.RemovedStableIds.Clear();
            foreach ((int stableId, RetainedTextSpriteEntry entry) in state.ItemsByStableId)
            {
                if (entry.SeenStamp != stamp)
                {
                    state.RemovedStableIds.Add(stableId);
                }
            }

            for (int i = 0; i < state.RemovedStableIds.Count; i++)
            {
                int stableId = state.RemovedStableIds[i];
                if (state.ItemsByStableId.TryGetValue(stableId, out RetainedTextSpriteEntry entry))
                {
                    RemoveRetainedTextSpriteEntry(state, stableId, entry);
                }
            }
        }

        private static void RemoveRetainedTextSpriteEntry(
            RetainedTextSpriteLaneState state,
            int stableId,
            in RetainedTextSpriteEntry entry)
        {
            RetainedTextSpriteBatchBucket bucket = state.Buckets[entry.BucketIndex];
            int movedStableId = bucket.RemoveAt(entry.SlotIndex);
            state.ItemsByStableId.Remove(stableId);
            if (movedStableId > 0 &&
                movedStableId != stableId &&
                state.ItemsByStableId.TryGetValue(movedStableId, out RetainedTextSpriteEntry movedEntry))
            {
                movedEntry.SlotIndex = entry.SlotIndex;
                state.ItemsByStableId[movedStableId] = movedEntry;
                UpdateRetainedTextSpriteOrderEntry(state, movedStableId, movedEntry);
            }
        }

        private static void UpdateRetainedTextSpriteOrderEntry(
            RetainedTextSpriteLaneState state,
            int stableId,
            in RetainedTextSpriteEntry entry)
        {
            for (int i = 0; i < state.OrderCount; i++)
            {
                if (state.OrderStableIds[i] == stableId)
                {
                    state.OrderEntries[i] = entry;
                    return;
                }
            }
        }

        private static void EnsureRetainedTextAtlas(RetainedTextSpriteLaneState state)
        {
            int bucketCount = state.Buckets.Count;
            if (!state.AtlasDirty &&
                state.AtlasImage != null &&
                state.AtlasSprites.Length >= bucketCount)
            {
                return;
            }

            state.DisposeAtlas();
            if (bucketCount <= 0)
            {
                state.AtlasDirty = false;
                return;
            }

            int atlasWidth = 0;
            int atlasHeight = 0;
            state.EnsureAtlasSpriteCapacity(bucketCount);
            for (int i = 0; i < bucketCount; i++)
            {
                SKImage image = state.Buckets[i].Sprite.Image;
                state.AtlasSprites[i] = new SKRect(atlasWidth, 0f, atlasWidth + image.Width, image.Height);
                atlasWidth += image.Width;
                atlasHeight = Math.Max(atlasHeight, image.Height);
            }

            if (atlasWidth <= 0 || atlasHeight <= 0)
            {
                state.AtlasDirty = false;
                return;
            }

            using SKSurface surface = SKSurface.Create(new SKImageInfo(atlasWidth, atlasHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            SKCanvas atlasCanvas = surface.Canvas;
            atlasCanvas.Clear(SKColors.Transparent);
            for (int i = 0; i < bucketCount; i++)
            {
                SKRect sprite = state.AtlasSprites[i];
                atlasCanvas.DrawImage(state.Buckets[i].Sprite.Image, sprite.Left, 0f);
            }

            state.AtlasImage = surface.Snapshot();
            state.AtlasDirty = false;
        }

        private void RebuildDeferredLargeTextChunk(
            LargeTextLaneState state,
            int chunkIndex,
            int laneVersion,
            ReadOnlySpan<PresentationOverlayItem> span)
        {
            int start = chunkIndex * DeferredLargeTextChunkSize;
            int length = Math.Min(DeferredLargeTextChunkSize, span.Length - start);
            using var recorder = new SKPictureRecorder();
            SKCanvas pictureCanvas = recorder.BeginRecording(new SKRect(-1f, -1f, 4096f, 4096f));
            DrawTextBatched(pictureCanvas, span.Slice(start, length));
            SKPicture? picture = recorder.EndRecording();
            state.SetChunk(chunkIndex, picture, laneVersion);
        }

        private void ClearLargeTextLaneState(int laneIndex)
        {
            _largeTextLaneStates[laneIndex].Clear();
        }

        private void ClearTextLayoutCache()
        {
            foreach ((_, CachedTextLayout layout) in _textLayoutCache)
            {
                layout.Dispose();
            }

            _textLayoutCache.Clear();
        }

        private void ClearTextSpriteCache()
        {
            foreach ((_, CachedTextSprite sprite) in _textSpriteCache)
            {
                sprite.Dispose();
            }

            _textSpriteCache.Clear();
        }

        private CachedBarSprite GetBarSprite(in PresentationOverlayItem item)
        {
            BarSpriteCacheKey key = CreateBarSpriteCacheKey(item);
            return GetBarSprite(key, item);
        }

        private CachedTextSprite GetTextSprite(string text, int fontSize, SKColor color)
        {
            var key = new TextSpriteCacheKey(text, fontSize, ToColorKey(color));
            if (_textSpriteCache.TryGetValue(key, out CachedTextSprite? sprite))
            {
                LastTextSpriteCacheHits++;
                return sprite;
            }

            if (_textSpriteCache.Count >= MaxTextSpriteCacheEntries)
            {
                // Avoid disposing sprites that may still be referenced by the current frame's batching work.
                _textSpriteCache.Clear();
                LastTextSpriteCacheClears++;
            }

            LastTextSpriteCacheMisses++;
            CachedTextLayout layout = GetTextLayout(text, fontSize);
            float ascent = fontSize;
            float descent = Math.Max(1f, fontSize * 0.25f);
            for (int i = 0; i < layout.Runs.Length; i++)
            {
                SKFontMetrics metrics = layout.Runs[i].Font.Metrics;
                ascent = Math.Max(ascent, -metrics.Ascent);
                descent = Math.Max(descent, metrics.Descent);
            }

            float baselineY = MathF.Ceiling(ascent) + 1f;
            int widthPx = Math.Max(1, (int)MathF.Ceiling(layout.Width) + 2);
            int heightPx = Math.Max(1, (int)MathF.Ceiling(ascent + descent) + 2);

            using var surface = SKSurface.Create(new SKImageInfo(widthPx, heightPx));
            SKCanvas spriteCanvas = surface.Canvas;
            spriteCanvas.Clear(SKColors.Transparent);

            _textPaint.Color = color;
            for (int runIndex = 0; runIndex < layout.Runs.Length; runIndex++)
            {
                CachedTextRun run = layout.Runs[runIndex];
                if (run.Blob != null)
                {
                    spriteCanvas.DrawText(run.Blob, 1f + run.XOffset, baselineY, _textPaint);
                }
            }

            sprite = new CachedTextSprite(surface.Snapshot(), baselineY);
            _textSpriteCache[key] = sprite;
            return sprite;
        }

        private CachedBarSprite GetBarSprite(in BarSpriteCacheKey key, in PresentationOverlayItem item)
        {
            if (_barSpriteCache.TryGetValue(key, out SKImage? image))
            {
                LastBarSpriteCacheHits++;
                return new CachedBarSprite(image);
            }

            if (_barSpriteCache.Count >= MaxBarSpriteCacheEntries)
            {
                // Avoid disposing bar sprites that may still be referenced by the current frame's batching work.
                _barSpriteCache.Clear();
                LastBarSpriteCacheClears++;
            }

            LastBarSpriteCacheMisses++;
            int widthPx = key.WidthPx;
            int heightPx = key.HeightPx;
            using var surface = SKSurface.Create(new SKImageInfo(widthPx, heightPx));
            SKCanvas spriteCanvas = surface.Canvas;
            spriteCanvas.Clear(SKColors.Transparent);

            SKRect rect = new(0f, 0f, widthPx, heightPx);
            _fillPaint.Color = ToSkColor(item.Color0);
            spriteCanvas.DrawRect(rect, _fillPaint);

            if (key.FillPx > 0)
            {
                _fillPaint.Color = ToSkColor(item.Color1);
                spriteCanvas.DrawRect(0f, 0f, key.FillPx, heightPx, _fillPaint);
            }

            _strokePaint.Color = SKColors.Black;
            spriteCanvas.DrawRect(rect, _strokePaint);

            image = surface.Snapshot();
            _barSpriteCache[key] = image;
            return new CachedBarSprite(image);
        }

        private CachedMarkerSprite GetMarkerSprite(in MinimapMarkerRenderBucketKey key)
        {
            if (_markerSpriteCache.TryGetValue(key, out CachedMarkerSprite? sprite))
            {
                LastMinimapMarkerSpriteCacheHits++;
                return sprite;
            }

            if (_markerSpriteCache.Count >= MaxMarkerSpriteCacheEntries)
            {
                ClearMarkerSpriteCache();
                LastMinimapMarkerSpriteCacheClears++;
            }

            LastMinimapMarkerSpriteCacheMisses++;
            sprite = CreateMarkerSprite(key);
            _markerSpriteCache[key] = sprite;
            return sprite;
        }

        private CachedMarkerSprite CreateMarkerSprite(in MinimapMarkerRenderBucketKey key)
        {
            SKColor color = ToSkColor(key.ColorKey);
            float sizePx = key.SizePx;
            float radius = sizePx * 0.5f;
            float lengthPx = 0f;
            float shadowStroke = 0f;
            float colorStroke = 0f;
            if (key.HasOrientation)
            {
                lengthPx = key.OrientationLengthKey / 16f;
                shadowStroke = key.ShadowStrokeKey / 16f;
                colorStroke = key.ColorStrokeKey / 16f;
            }

            float angle = key.HasOrientation
                ? WorldPlane2D.BucketToFacingRad(
                    key.OrientationBucket,
                    MinimapScreenMarkerBuffer.OrientationBucketCount)
                : 0f;
            float lineDx = key.HasOrientation ? MathF.Cos(angle) * lengthPx : 0f;
            float lineDy = key.HasOrientation ? MathF.Sin(angle) * lengthPx : 0f;
            float strokeHalf = shadowStroke * 0.5f;
            float minX = key.HasOrientation ? MathF.Min(-radius, MathF.Min(0f, lineDx) - strokeHalf) : -radius;
            float maxX = key.HasOrientation ? MathF.Max(radius, MathF.Max(0f, lineDx) + strokeHalf) : radius;
            float minY = key.HasOrientation ? MathF.Min(-radius, MathF.Min(0f, lineDy) - strokeHalf) : -radius;
            float maxY = key.HasOrientation ? MathF.Max(radius, MathF.Max(0f, lineDy) + strokeHalf) : radius;
            const float spritePad = 1f;
            minX -= spritePad;
            maxX += spritePad;
            minY -= spritePad;
            maxY += spritePad;
            int widthPx = Math.Max(1, (int)MathF.Ceiling(maxX - minX));
            int heightPx = Math.Max(1, (int)MathF.Ceiling(maxY - minY));
            float anchorX = -minX;
            float anchorY = -minY;

            using var surface = SKSurface.Create(new SKImageInfo(widthPx, heightPx, SKColorType.Rgba8888, SKAlphaType.Premul));
            SKCanvas spriteCanvas = surface.Canvas;
            spriteCanvas.Clear(SKColors.Transparent);

            if (key.HasOrientation)
            {
                float endX = anchorX + lineDx;
                float endY = anchorY + lineDy;
                SKPaintStyle previousStyle = _strokePaint.Style;
                SKStrokeCap previousCap = _strokePaint.StrokeCap;
                float previousStrokeWidth = _strokePaint.StrokeWidth;
                SKColor previousColor = _strokePaint.Color;
                _strokePaint.Style = SKPaintStyle.Stroke;
                _strokePaint.StrokeCap = SKStrokeCap.Round;
                try
                {
                    _strokePaint.Color = new SKColor(0, 0, 0, key.ShadowAlpha);
                    _strokePaint.StrokeWidth = shadowStroke;
                    spriteCanvas.DrawLine(anchorX, anchorY, endX, endY, _strokePaint);
                    _strokePaint.Color = color;
                    _strokePaint.StrokeWidth = colorStroke;
                    spriteCanvas.DrawLine(anchorX, anchorY, endX, endY, _strokePaint);
                }
                finally
                {
                    _strokePaint.Style = previousStyle;
                    _strokePaint.StrokeCap = previousCap;
                    _strokePaint.StrokeWidth = previousStrokeWidth;
                    _strokePaint.Color = previousColor;
                }
            }

            SKPaintStyle previousFillStyle = _fillPaint.Style;
            SKColor previousFillColor = _fillPaint.Color;
            _fillPaint.Style = SKPaintStyle.Fill;
            _fillPaint.Color = color;
            try
            {
                spriteCanvas.DrawCircle(anchorX, anchorY, MathF.Max(0.5f, radius), _fillPaint);
            }
            finally
            {
                _fillPaint.Style = previousFillStyle;
                _fillPaint.Color = previousFillColor;
            }

            return new CachedMarkerSprite(surface.Snapshot(), new SKRect(0f, 0f, widthPx, heightPx), anchorX, anchorY);
        }

        private static BarSpriteCacheKey CreateBarSpriteCacheKey(in PresentationOverlayItem item)
        {
            int widthPx = Math.Max(1, (int)MathF.Round(item.Width));
            int heightPx = Math.Max(1, (int)MathF.Round(item.Height));
            int fillPx = QuantizeBarFillPx(widthPx, item.Value0);
            return new BarSpriteCacheKey(
                widthPx,
                heightPx,
                fillPx,
                ToColorKey(ToSkColor(item.Color0)),
                ToColorKey(ToSkColor(item.Color1)));
        }

        private static int QuantizeStrokeWidth(float strokeWidth)
        {
            return Math.Max(1, (int)MathF.Round(strokeWidth * 16f));
        }

        private static int QuantizeBarFillPx(int widthPx, float value)
        {
            int fillPx = (int)MathF.Round(widthPx * Math.Clamp(value, 0f, 1f));
            return Math.Clamp(fillPx, 0, widthPx);
        }

        private void ClearBarSpriteCache()
        {
            foreach ((_, SKImage image) in _barSpriteCache)
            {
                image.Dispose();
            }

            _barSpriteCache.Clear();
        }

        private void ClearMarkerSpriteCache()
        {
            foreach ((_, CachedMarkerSprite sprite) in _markerSpriteCache)
            {
                sprite.Dispose();
            }

            _markerSpriteCache.Clear();
        }

        private SKFont GetFont(SKTypeface typeface, int fontSize)
        {
            string familyName = typeface.FamilyName ?? string.Empty;
            var key = new FontCacheKey(familyName, fontSize);
            if (_fontCache.TryGetValue(key, out SKFont? font))
            {
                return font;
            }

            font = new SKFont(typeface, fontSize);
            _fontCache[key] = font;
            return font;
        }

        private static uint ToColorKey(SKColor color)
        {
            return ((uint)color.Alpha << 24)
                | ((uint)color.Red << 16)
                | ((uint)color.Green << 8)
                | color.Blue;
        }

        private static SKColor FromColorKey(uint key)
        {
            byte a = (byte)(key >> 24);
            byte r = (byte)(key >> 16);
            byte g = (byte)(key >> 8);
            byte b = (byte)key;
            return new SKColor(r, g, b, a);
        }

        private static SKColor ToSkColor(uint key)
        {
            return FromColorKey(key);
        }

        private void InvalidateLanePicture(int laneIndex)
        {
            _lanePictures[laneIndex]?.Dispose();
            _lanePictures[laneIndex] = null;
            _laneVersions[laneIndex] = -1;
            _lanePictureOffsetsX[laneIndex] = 0f;
            _lanePictureOffsetsY[laneIndex] = 0f;
        }

        private void DrawLanePictureOrHotpath(
            SKCanvas canvas,
            PresentationOverlayItemKind kind,
            int laneIndex,
            ReadOnlySpan<PresentationOverlayItem> span)
        {
            SKPicture? picture = _lanePictures[laneIndex];
            if (picture == null)
            {
                DrawLargeLaneHotpath(canvas, kind, laneIndex, _laneVersions[laneIndex], span);
                return;
            }

            float offsetX = _lanePictureOffsetsX[laneIndex];
            float offsetY = _lanePictureOffsetsY[laneIndex];
            if (offsetX == 0f && offsetY == 0f)
            {
                canvas.DrawPicture(picture);
                return;
            }

            int restoreCount = canvas.Save();
            canvas.Translate(offsetX, offsetY);
            canvas.DrawPicture(picture);
            canvas.RestoreToCount(restoreCount);
        }

        private static bool ShouldRenderImmediate(PresentationOverlayLayer layer, PresentationOverlayItemKind kind, int itemCount)
        {
            if (itemCount <= 0)
            {
                return false;
            }

            return kind switch
            {
                PresentationOverlayItemKind.Bar => layer == PresentationOverlayLayer.UnderUi && itemCount >= ImmediateUnderUiBarThreshold,
                PresentationOverlayItemKind.Text => itemCount >= ImmediateUnderUiTextThreshold,
                _ => false,
            };
        }

        private static double ElapsedMs(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
        }

        private static int ResolveNextCapacity(int current, int required)
        {
            int next = current == 0 ? 4 : current;
            while (next < required)
            {
                next *= 2;
            }

            return next;
        }

        private static unsafe void DrawAtlasCount(
            SKCanvas canvas,
            SKImage image,
            SKRect[] sprites,
            SKRotationScaleMatrix[] transforms,
            int count)
        {
            if (count <= 0)
            {
                return;
            }

            fixed (SKRect* spritePtr = sprites)
            fixed (SKRotationScaleMatrix* transformPtr = transforms)
            {
                SKSamplingOptions sampling = SKSamplingOptions.Default;
                SkCanvasDrawAtlas(
                    canvas.Handle,
                    image.Handle,
                    (IntPtr)transformPtr,
                    (IntPtr)spritePtr,
                    IntPtr.Zero,
                    count,
                    SKBlendMode.Dst,
                    (IntPtr)(&sampling),
                    IntPtr.Zero,
                    IntPtr.Zero);
            }
        }

        [DllImport("libSkiaSharp", EntryPoint = "sk_canvas_draw_atlas", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SkCanvasDrawAtlas(
            IntPtr canvas,
            IntPtr atlas,
            IntPtr transforms,
            IntPtr sprites,
            IntPtr colors,
            int count,
            SKBlendMode mode,
            IntPtr sampling,
            IntPtr cullRect,
            IntPtr paint);

        private static SKColor ToSkColor(in System.Numerics.Vector4 color)
        {
            byte a = (byte)Math.Clamp(color.W * 255f, 0f, 255f);
            byte r = (byte)Math.Clamp(color.X * 255f, 0f, 255f);
            byte g = (byte)Math.Clamp(color.Y * 255f, 0f, 255f);
            byte b = (byte)Math.Clamp(color.Z * 255f, 0f, 255f);
            return new SKColor(r, g, b, a);
        }

        private static int GetLaneIndex(PresentationOverlayLayer layer, PresentationOverlayItemKind kind)
        {
            return ((int)layer * KindCount) + ((int)kind - 1);
        }

        private readonly record struct FontCacheKey(string FamilyName, int FontSize);

        private readonly record struct BarSpriteCacheKey(
            int WidthPx,
            int HeightPx,
            int FillPx,
            uint BackgroundColor,
            uint ForegroundColor);

        private readonly record struct TextLayoutCacheKey(string Text, int FontSize);

        private readonly record struct TextBatchKey(string Text, int FontSize, uint ColorKey);

        private readonly record struct TextSpriteCacheKey(string Text, int FontSize, uint ColorKey);

        private readonly record struct CachedTextRun(
            SKTextBlob? Blob,
            float XOffset,
            SKFont Font,
            ushort[] Glyphs,
            SKPoint[] GlyphPositions);

        private readonly record struct CachedBarSprite(SKImage Image);

        private sealed class CachedMarkerSprite : IDisposable
        {
            public CachedMarkerSprite(SKImage image, SKRect spriteRect, float anchorX, float anchorY)
            {
                Image = image;
                SpriteRect = spriteRect;
                AnchorX = anchorX;
                AnchorY = anchorY;
            }

            public SKImage Image { get; }

            public SKRect SpriteRect { get; }

            public float AnchorX { get; }

            public float AnchorY { get; }

            public void Dispose()
            {
                Image.Dispose();
            }
        }

        private struct RetainedBarEntry
        {
            public int BucketIndex;
            public int SlotIndex;
            public BarSpriteCacheKey Key;
            public int DirtySerial;
            public int SeenStamp;

            public RetainedBarEntry(int bucketIndex, int slotIndex, in BarSpriteCacheKey key, int dirtySerial, int seenStamp)
            {
                BucketIndex = bucketIndex;
                SlotIndex = slotIndex;
                Key = key;
                DirtySerial = dirtySerial;
                SeenStamp = seenStamp;
            }
        }

        private struct RetainedTextSpriteEntry
        {
            public int BucketIndex;
            public int SlotIndex;
            public TextBatchKey Key;
            public int DirtySerial;
            public int FontSize;
            public int SeenStamp;

            public RetainedTextSpriteEntry(int bucketIndex, int slotIndex, in TextBatchKey key, int dirtySerial, int fontSize, int seenStamp)
            {
                BucketIndex = bucketIndex;
                SlotIndex = slotIndex;
                Key = key;
                DirtySerial = dirtySerial;
                FontSize = fontSize;
                SeenStamp = seenStamp;
            }
        }

        private sealed class RetainedBarLaneState
        {
            public readonly Dictionary<int, RetainedBarEntry> ItemsByStableId = new();
            public readonly Dictionary<BarSpriteCacheKey, int> BucketIndexByKey = new();
            public readonly List<RetainedBarBatchBucket> Buckets = new();
            public readonly List<int> RemovedStableIds = new();
            public int[] OrderStableIds = Array.Empty<int>();
            public RetainedBarEntry[] OrderEntries = Array.Empty<RetainedBarEntry>();
            public int OrderCount;
            public int LastVersion = -1;
            private int _stamp;

            public int NextStamp()
            {
                _stamp++;
                if (_stamp != int.MaxValue)
                {
                    return _stamp;
                }

                _stamp = 1;
                return _stamp;
            }

            public void Clear()
            {
                ItemsByStableId.Clear();
                BucketIndexByKey.Clear();
                Buckets.Clear();
                RemovedStableIds.Clear();
                OrderCount = 0;
                LastVersion = -1;
                DisposeAtlas();
            }

            public void BeginVisibleFrame()
            {
                for (int i = 0; i < Buckets.Count; i++)
                {
                    Buckets[i].ResetVisible();
                }
            }

            public void EnsureOrderCapacity(int required)
            {
                if (OrderStableIds.Length >= required && OrderEntries.Length >= required)
                {
                    return;
                }

                int next = OrderStableIds.Length == 0 ? 4 : OrderStableIds.Length;
                while (next < required)
                {
                    next *= 2;
                }

                Array.Resize(ref OrderStableIds, next);
                Array.Resize(ref OrderEntries, next);
            }

            public SKImage? AtlasImage;
            public SKRect[] AtlasSprites = Array.Empty<SKRect>();
            public SKRect[] DrawSprites = Array.Empty<SKRect>();
            public SKRotationScaleMatrix[] DrawTransforms = Array.Empty<SKRotationScaleMatrix>();
            public bool AtlasDirty = true;

            public void EnsureAtlasSpriteCapacity(int required)
            {
                if (AtlasSprites.Length >= required)
                {
                    return;
                }

                Array.Resize(ref AtlasSprites, ResolveNextCapacity(AtlasSprites.Length, required));
            }

            public void EnsureDrawCapacity(int required)
            {
                if (DrawSprites.Length >= required && DrawTransforms.Length >= required)
                {
                    return;
                }

                int next = ResolveNextCapacity(DrawSprites.Length, required);
                Array.Resize(ref DrawSprites, next);
                Array.Resize(ref DrawTransforms, next);
            }

            public void DisposeAtlas()
            {
                AtlasImage?.Dispose();
                AtlasImage = null;
                AtlasDirty = true;
            }
        }

        private sealed class RetainedTextSpriteLaneState
        {
            public readonly Dictionary<int, RetainedTextSpriteEntry> ItemsByStableId = new();
            public readonly Dictionary<TextBatchKey, int> BucketIndexByKey = new();
            public readonly List<RetainedTextSpriteBatchBucket> Buckets = new();
            public readonly List<int> RemovedStableIds = new();
            public int[] OrderStableIds = Array.Empty<int>();
            public RetainedTextSpriteEntry[] OrderEntries = Array.Empty<RetainedTextSpriteEntry>();
            public int[] OrderFontSizes = Array.Empty<int>();
            public int OrderCount;
            public int LastVersion = -1;
            private int _stamp;

            public int NextStamp()
            {
                _stamp++;
                if (_stamp != int.MaxValue)
                {
                    return _stamp;
                }

                _stamp = 1;
                return _stamp;
            }

            public void Clear()
            {
                ItemsByStableId.Clear();
                BucketIndexByKey.Clear();
                Buckets.Clear();
                RemovedStableIds.Clear();
                OrderCount = 0;
                LastVersion = -1;
                DisposeAtlas();
            }

            public void BeginVisibleFrame()
            {
                for (int i = 0; i < Buckets.Count; i++)
                {
                    Buckets[i].ResetVisible();
                }
            }

            public void EnsureOrderCapacity(int required)
            {
                if (OrderStableIds.Length >= required &&
                    OrderEntries.Length >= required &&
                    OrderFontSizes.Length >= required)
                {
                    return;
                }

                int next = OrderStableIds.Length == 0 ? 4 : OrderStableIds.Length;
                while (next < required)
                {
                    next *= 2;
                }

                Array.Resize(ref OrderStableIds, next);
                Array.Resize(ref OrderEntries, next);
                Array.Resize(ref OrderFontSizes, next);
            }

            public SKImage? AtlasImage;
            public SKRect[] AtlasSprites = Array.Empty<SKRect>();
            public SKRect[] DrawSprites = Array.Empty<SKRect>();
            public SKRotationScaleMatrix[] DrawTransforms = Array.Empty<SKRotationScaleMatrix>();
            public bool AtlasDirty = true;

            public void EnsureAtlasSpriteCapacity(int required)
            {
                if (AtlasSprites.Length >= required)
                {
                    return;
                }

                Array.Resize(ref AtlasSprites, ResolveNextCapacity(AtlasSprites.Length, required));
            }

            public void EnsureDrawCapacity(int required)
            {
                if (DrawSprites.Length >= required && DrawTransforms.Length >= required)
                {
                    return;
                }

                int next = ResolveNextCapacity(DrawSprites.Length, required);
                Array.Resize(ref DrawSprites, next);
                Array.Resize(ref DrawTransforms, next);
            }

            public void DisposeAtlas()
            {
                AtlasImage?.Dispose();
                AtlasImage = null;
                AtlasDirty = true;
            }
        }

        private sealed class RetainedBarBatchBucket
        {
            private int[] _stableIds = Array.Empty<int>();
            private float[] _x = Array.Empty<float>();
            private float[] _y = Array.Empty<float>();
            private SKRect[] _sprites = Array.Empty<SKRect>();
            private SKRotationScaleMatrix[] _transforms = Array.Empty<SKRotationScaleMatrix>();
            private readonly SKRect _spriteRect;
            private SKRect _drawSpriteRect;

            public RetainedBarBatchBucket(SKImage image)
            {
                Image = image;
                _spriteRect = new SKRect(0f, 0f, image.Width, image.Height);
                _drawSpriteRect = _spriteRect;
            }

            public SKImage Image { get; }

            public int Count { get; private set; }

            public float[] X => _x;

            public float[] Y => _y;

            public SKRect[] Sprites => _sprites;

            public SKRotationScaleMatrix[] Transforms => _transforms;

            public void ResetVisible()
            {
                Count = 0;
            }

            public void AddVisible(float x, float y)
            {
                EnsureCapacity(Count + 1);
                int index = Count++;
                _stableIds[index] = 0;
                Update(index, x, y);
            }

            public int Add(int stableId, float x, float y)
            {
                EnsureCapacity(Count + 1);
                int index = Count++;
                _stableIds[index] = stableId;
                Update(index, x, y);
                return index;
            }

            public void Update(int index, float x, float y)
            {
                _x[index] = x;
                _y[index] = y;
                _sprites[index] = _drawSpriteRect;
                _transforms[index] = SKRotationScaleMatrix.CreateTranslation(x, y);
            }

            public void SetSpriteRect(SKRect spriteRect)
            {
                if (_drawSpriteRect == spriteRect)
                {
                    return;
                }

                _drawSpriteRect = spriteRect;
                for (int i = 0; i < Count; i++)
                {
                    _sprites[i] = spriteRect;
                }
            }

            public int RemoveAt(int index)
            {
                int lastIndex = Count - 1;
                int movedStableId = 0;
                if (index != lastIndex)
                {
                    movedStableId = _stableIds[lastIndex];
                    _stableIds[index] = movedStableId;
                    _x[index] = _x[lastIndex];
                    _y[index] = _y[lastIndex];
                    _sprites[index] = _sprites[lastIndex];
                    _transforms[index] = _transforms[lastIndex];
                }

                _stableIds[lastIndex] = 0;
                Count = lastIndex;
                return movedStableId;
            }

            private void EnsureCapacity(int required)
            {
                if (_stableIds.Length >= required)
                {
                    return;
                }

                int next = _stableIds.Length == 0 ? 4 : _stableIds.Length;
                while (next < required)
                {
                    next *= 2;
                }

                Array.Resize(ref _stableIds, next);
                Array.Resize(ref _x, next);
                Array.Resize(ref _y, next);
                Array.Resize(ref _sprites, next);
                Array.Resize(ref _transforms, next);
            }
        }

        private sealed class RetainedTextSpriteBatchBucket
        {
            private int[] _stableIds = Array.Empty<int>();
            private float[] _x = Array.Empty<float>();
            private float[] _y = Array.Empty<float>();
            private SKRect[] _sprites = Array.Empty<SKRect>();
            private SKRotationScaleMatrix[] _transforms = Array.Empty<SKRotationScaleMatrix>();
            private readonly SKRect _spriteRect;
            private SKRect _drawSpriteRect;

            public RetainedTextSpriteBatchBucket(CachedTextSprite sprite)
            {
                Sprite = sprite;
                _spriteRect = new SKRect(0f, 0f, sprite.Image.Width, sprite.Image.Height);
                _drawSpriteRect = _spriteRect;
            }

            public CachedTextSprite Sprite { get; }

            public int Count { get; private set; }

            public float[] X => _x;

            public float[] Y => _y;

            public SKRect[] Sprites => _sprites;

            public SKRotationScaleMatrix[] Transforms => _transforms;

            public void ResetVisible()
            {
                Count = 0;
            }

            public void AddVisible(float x, float y)
            {
                EnsureCapacity(Count + 1);
                int index = Count++;
                _stableIds[index] = 0;
                Update(index, x, y);
            }

            public int Add(int stableId, float x, float y)
            {
                EnsureCapacity(Count + 1);
                int index = Count++;
                _stableIds[index] = stableId;
                Update(index, x, y);
                return index;
            }

            public void Update(int index, float x, float y)
            {
                _x[index] = x;
                _y[index] = y;
                _sprites[index] = _drawSpriteRect;
                _transforms[index] = SKRotationScaleMatrix.CreateTranslation(x, y);
            }

            public void SetSpriteRect(SKRect spriteRect)
            {
                if (_drawSpriteRect == spriteRect)
                {
                    return;
                }

                _drawSpriteRect = spriteRect;
                for (int i = 0; i < Count; i++)
                {
                    _sprites[i] = spriteRect;
                }
            }

            public int RemoveAt(int index)
            {
                int lastIndex = Count - 1;
                int movedStableId = 0;
                if (index != lastIndex)
                {
                    movedStableId = _stableIds[lastIndex];
                    _stableIds[index] = movedStableId;
                    _x[index] = _x[lastIndex];
                    _y[index] = _y[lastIndex];
                    _sprites[index] = _sprites[lastIndex];
                    _transforms[index] = _transforms[lastIndex];
                }

                _stableIds[lastIndex] = 0;
                Count = lastIndex;
                return movedStableId;
            }

            private void EnsureCapacity(int required)
            {
                if (_stableIds.Length >= required)
                {
                    return;
                }

                int next = _stableIds.Length == 0 ? 4 : _stableIds.Length;
                while (next < required)
                {
                    next *= 2;
                }

                Array.Resize(ref _stableIds, next);
                Array.Resize(ref _x, next);
                Array.Resize(ref _y, next);
                Array.Resize(ref _sprites, next);
                Array.Resize(ref _transforms, next);
            }
        }

        private sealed class CachedTextSprite : IDisposable
        {
            public CachedTextSprite(SKImage image, float baselineY)
            {
                Image = image;
                BaselineY = baselineY;
            }

            public SKImage Image { get; }

            public float BaselineY { get; }

            public void Dispose()
            {
                Image.Dispose();
            }
        }

        private sealed class TextBatchBucket
        {
            private float[] _x = Array.Empty<float>();
            private float[] _baselineY = Array.Empty<float>();

            public CachedTextLayout Layout { get; private set; } = null!;

            public SKColor Color { get; private set; }

            public int Count { get; private set; }

            public float[] X => _x;

            public float[] BaselineY => _baselineY;

            public void Reset(CachedTextLayout layout, SKColor color)
            {
                Layout = layout;
                Color = color;
                Count = 0;
            }

            public void Add(float x, float baselineY)
            {
                EnsureCapacity(Count + 1);
                _x[Count] = x;
                _baselineY[Count] = baselineY;
                Count++;
            }

            private void EnsureCapacity(int required)
            {
                if (_x.Length >= required)
                {
                    return;
                }

                int next = _x.Length == 0 ? 4 : _x.Length;
                while (next < required)
                {
                    next *= 2;
                }

                Array.Resize(ref _x, next);
                Array.Resize(ref _baselineY, next);
            }
        }

        private sealed class BarBatchBucket
        {
            private float[] _x = Array.Empty<float>();
            private float[] _y = Array.Empty<float>();
            private SKRect[] _sprites = Array.Empty<SKRect>();
            private SKRotationScaleMatrix[] _transforms = Array.Empty<SKRotationScaleMatrix>();
            private SKRect _spriteRect;

            public SKImage Image { get; private set; } = null!;

            public int Count { get; private set; }

            public float[] X => _x;

            public float[] Y => _y;

            public SKRect[] Sprites => _sprites;

            public SKRotationScaleMatrix[] Transforms => _transforms;

            public void Reset(SKImage image, float width, float height)
            {
                Image = image;
                _spriteRect = new SKRect(0f, 0f, Image.Width, Image.Height);
                Count = 0;
            }

            public void Add(float x, float y)
            {
                EnsureCapacity(Count + 1);
                _x[Count] = x;
                _y[Count] = y;
                _sprites[Count] = _spriteRect;
                _transforms[Count] = SKRotationScaleMatrix.CreateTranslation(x, y);
                Count++;
            }

            public void PrepareAtlas()
            {
                if (_sprites.Length < Count)
                {
                    Array.Resize(ref _sprites, ResolveNextCapacity(_sprites.Length, Count));
                }

                if (_transforms.Length < Count)
                {
                    Array.Resize(ref _transforms, ResolveNextCapacity(_transforms.Length, Count));
                }

                for (int i = 0; i < Count; i++)
                {
                    _sprites[i] = _spriteRect;
                    _transforms[i] = SKRotationScaleMatrix.CreateTranslation(_x[i], _y[i]);
                }
            }

            private void EnsureCapacity(int required)
            {
                if (_x.Length >= required && _y.Length >= required && _sprites.Length >= required && _transforms.Length >= required)
                {
                    return;
                }

                int next = _x.Length == 0 ? 4 : _x.Length;
                while (next < required)
                {
                    next *= 2;
                }

                Array.Resize(ref _x, next);
                Array.Resize(ref _y, next);
                Array.Resize(ref _sprites, next);
                Array.Resize(ref _transforms, next);
            }

            private static int ResolveNextCapacity(int current, int required)
            {
                int next = current == 0 ? 4 : current;
                while (next < required)
                {
                    next *= 2;
                }

                return next;
            }
        }

        private sealed class MinimapMarkerAtlasBatch : IDisposable
        {
            private readonly Dictionary<MinimapMarkerRenderBucketKey, int> _atlasSlotByKey = new();
            private CachedMarkerSprite?[] _sprites = Array.Empty<CachedMarkerSprite?>();
            private SKRect[] _atlasSpriteRects = Array.Empty<SKRect>();
            private SKRect[] _drawSprites = Array.Empty<SKRect>();
            private SKRotationScaleMatrix[] _transforms = Array.Empty<SKRotationScaleMatrix>();
            private SKImage? _atlasImage;
            private int _atlasSlotCount;
            private int _count;
            private bool _atlasDirty = true;

            public void Build(
                MinimapScreenMarkerBuffer markers,
                SkiaOverlayRenderer renderer)
            {
                _count = 0;
                int bucketCount = markers.BucketCount;
                int markerCount = markers.Count;
                ReadOnlySpan<float> screenX = markers.ScreenX;
                ReadOnlySpan<float> screenY = markers.ScreenY;
                for (int bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
                {
                    MinimapScreenMarkerBucket bucket = markers.GetBucket(bucketIndex);
                    if (bucket.Count <= 0)
                    {
                        continue;
                    }

                    ResolveAtlasSlot(bucket.Key, renderer);
                }

                EnsureAtlasImage();

                EnsureInstanceCapacity(markerCount);
                for (int bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
                {
                    MinimapScreenMarkerBucket bucket = markers.GetBucket(bucketIndex);
                    if (bucket.Count <= 0)
                    {
                        continue;
                    }

                    int slot = _atlasSlotByKey[bucket.Key];
                    SKRect atlasRect = _atlasSpriteRects[slot];
                    CachedMarkerSprite sprite = _sprites[slot]!;
                    float anchorX = sprite.AnchorX;
                    float anchorY = sprite.AnchorY;
                    int start = bucket.Start;
                    int end = start + bucket.Count;
                    for (int markerIndex = start; markerIndex < end; markerIndex++)
                    {
                        float x = screenX[markerIndex] - anchorX;
                        float y = screenY[markerIndex] - anchorY;
                        _drawSprites[_count] = atlasRect;
                        _transforms[_count] = SKRotationScaleMatrix.CreateTranslation(x, y);
                        _count++;
                    }
                }
            }

            public void DrawTo(SKCanvas canvas)
            {
                if (_count <= 0 || _atlasImage == null)
                {
                    return;
                }

                DrawAtlasCount(canvas, _atlasImage, _drawSprites, _transforms, _count);
            }

            public void Dispose()
            {
                _atlasImage?.Dispose();
                _atlasImage = null;
            }

            private int ResolveAtlasSlot(
                in MinimapMarkerRenderBucketKey key,
                SkiaOverlayRenderer renderer)
            {
                if (_atlasSlotByKey.TryGetValue(key, out int slot))
                {
                    return slot;
                }

                slot = _atlasSlotCount++;
                EnsureAtlasSlotCapacity(slot + 1);
                _atlasSlotByKey[key] = slot;
                _sprites[slot] = renderer.GetMarkerSprite(in key);
                _atlasDirty = true;
                return slot;
            }

            private void EnsureAtlasSlotCapacity(int required)
            {
                if (_sprites.Length >= required &&
                    _atlasSpriteRects.Length >= required)
                {
                    return;
                }

                int next = ResolveNextCapacity(_sprites.Length, required);
                Array.Resize(ref _sprites, next);
                Array.Resize(ref _atlasSpriteRects, next);
            }

            private void EnsureInstanceCapacity(int required)
            {
                if (_drawSprites.Length >= required &&
                    _transforms.Length >= required)
                {
                    return;
                }

                int next = ResolveNextCapacity(_drawSprites.Length, required);
                Array.Resize(ref _drawSprites, next);
                Array.Resize(ref _transforms, next);
            }

            private void EnsureAtlasImage()
            {
                if (!_atlasDirty && _atlasImage != null)
                {
                    return;
                }

                int atlasWidth = 0;
                int atlasHeight = 0;
                for (int i = 0; i < _atlasSlotCount; i++)
                {
                    CachedMarkerSprite sprite = _sprites[i]!;
                    atlasWidth += sprite.Image.Width;
                    atlasHeight = Math.Max(atlasHeight, sprite.Image.Height);
                }

                _atlasImage?.Dispose();
                _atlasImage = null;
                using var surface = SKSurface.Create(new SKImageInfo(
                    Math.Max(1, atlasWidth),
                    Math.Max(1, atlasHeight),
                    SKColorType.Rgba8888,
                    SKAlphaType.Premul));
                SKCanvas atlasCanvas = surface.Canvas;
                atlasCanvas.Clear(SKColors.Transparent);

                float x = 0f;
                for (int i = 0; i < _atlasSlotCount; i++)
                {
                    CachedMarkerSprite sprite = _sprites[i]!;
                    atlasCanvas.DrawImage(sprite.Image, x, 0f);
                    _atlasSpriteRects[i] = new SKRect(
                        x,
                        0f,
                        x + sprite.Image.Width,
                        sprite.Image.Height);
                    x += sprite.Image.Width;
                }

                _atlasImage = surface.Snapshot();
                _atlasDirty = false;
            }
        }

        private sealed class TextSpriteBatchBucket
        {
            private float[] _x = Array.Empty<float>();
            private float[] _y = Array.Empty<float>();
            private SKRect[] _sprites = Array.Empty<SKRect>();
            private SKRotationScaleMatrix[] _transforms = Array.Empty<SKRotationScaleMatrix>();
            private SKRect _spriteRect;

            public CachedTextSprite Sprite { get; private set; } = null!;

            public int Count { get; private set; }

            public float[] X => _x;

            public float[] Y => _y;

            public SKRect[] Sprites => _sprites;

            public SKRotationScaleMatrix[] Transforms => _transforms;

            public void Reset(CachedTextSprite sprite)
            {
                Sprite = sprite;
                _spriteRect = new SKRect(0f, 0f, Sprite.Image.Width, Sprite.Image.Height);
                Count = 0;
            }

            public void Add(float x, float y)
            {
                EnsureCapacity(Count + 1);
                _x[Count] = x;
                _y[Count] = y;
                _sprites[Count] = _spriteRect;
                _transforms[Count] = SKRotationScaleMatrix.CreateTranslation(x, y);
                Count++;
            }

            public void PrepareAtlas()
            {
                if (_sprites.Length < Count)
                {
                    Array.Resize(ref _sprites, ResolveNextCapacity(_sprites.Length, Count));
                }

                if (_transforms.Length < Count)
                {
                    Array.Resize(ref _transforms, ResolveNextCapacity(_transforms.Length, Count));
                }

                for (int i = 0; i < Count; i++)
                {
                    _sprites[i] = _spriteRect;
                    _transforms[i] = SKRotationScaleMatrix.CreateTranslation(_x[i], _y[i]);
                }
            }

            private void EnsureCapacity(int required)
            {
                if (_x.Length >= required && _y.Length >= required && _sprites.Length >= required && _transforms.Length >= required)
                {
                    return;
                }

                int next = _x.Length == 0 ? 4 : _x.Length;
                while (next < required)
                {
                    next *= 2;
                }

                Array.Resize(ref _x, next);
                Array.Resize(ref _y, next);
                Array.Resize(ref _sprites, next);
                Array.Resize(ref _transforms, next);
            }

            private static int ResolveNextCapacity(int current, int required)
            {
                int next = current == 0 ? 4 : current;
                while (next < required)
                {
                    next *= 2;
                }

                return next;
            }
        }

        private sealed class CachedTextLayout : IDisposable
        {
            public CachedTextLayout(CachedTextRun[] runs, float width)
            {
                Runs = runs;
                Width = width;
            }

            public CachedTextRun[] Runs { get; }

            public float Width { get; }

            public void Dispose()
            {
                for (int i = 0; i < Runs.Length; i++)
                {
                    Runs[i].Blob?.Dispose();
                }
            }
        }

        private sealed class LargeTextLaneState
        {
            private SKPicture?[] _pictures = Array.Empty<SKPicture?>();
            private int[] _versions = Array.Empty<int>();

            public int ChunkCount { get; private set; }

            public int NextChunkCursor { get; private set; }

            public bool HasMissingChunks
            {
                get
                {
                    for (int i = 0; i < ChunkCount; i++)
                    {
                        if (_pictures[i] == null)
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }

            public void EnsureChunkCapacity(int required)
            {
                if (_pictures.Length < required)
                {
                    Array.Resize(ref _pictures, required);
                    Array.Resize(ref _versions, required);
                }

                ChunkCount = required;
                if (NextChunkCursor >= ChunkCount)
                {
                    NextChunkCursor = 0;
                }
            }

            public int FindNextStaleChunk(int version)
            {
                if (ChunkCount <= 0)
                {
                    return -1;
                }

                for (int offset = 0; offset < ChunkCount; offset++)
                {
                    int index = (NextChunkCursor + offset) % ChunkCount;
                    if (_pictures[index] == null || _versions[index] != version)
                    {
                        NextChunkCursor = (index + 1) % ChunkCount;
                        return index;
                    }
                }

                return -1;
            }

            public SKPicture? GetPicture(int chunkIndex)
            {
                return _pictures[chunkIndex];
            }

            public void SetChunk(int chunkIndex, SKPicture? picture, int version)
            {
                _pictures[chunkIndex]?.Dispose();
                _pictures[chunkIndex] = picture;
                _versions[chunkIndex] = version;
            }

            public void InvalidateAll()
            {
                for (int i = 0; i < ChunkCount; i++)
                {
                    _versions[i] = -1;
                    _pictures[i]?.Dispose();
                    _pictures[i] = null;
                }

                NextChunkCursor = 0;
            }

            public void Clear()
            {
                for (int i = 0; i < _pictures.Length; i++)
                {
                    _pictures[i]?.Dispose();
                    _pictures[i] = null;
                    _versions[i] = 0;
                }

                ChunkCount = 0;
                NextChunkCursor = 0;
            }
        }
    }
}
