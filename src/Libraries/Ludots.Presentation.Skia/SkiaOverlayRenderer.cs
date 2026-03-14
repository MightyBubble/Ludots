using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using Ludots.Core.Presentation.Hud;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using SkiaSharp;

namespace Ludots.Presentation.Skia
{
    public sealed class SkiaOverlayRenderer : IDisposable
    {
        private const int LaneCount = 6;
        private const int MaxBarSpriteCacheEntries = 2048;
        private const int MaxTextLayoutCacheEntries = 8192;
        private const int MaxTextSpriteCacheEntries = 8192;
        private const int ImmediateUnderUiBarThreshold = 48;
        private const int ImmediateUnderUiTextThreshold = 48;
        private const int DeferredLargeTextChunkSize = 128;
        private const int DeferredLargeTextChunksPerFrame = 1;
        private const int TextBatchBucketsPerBlob = 256;

        private static readonly PresentationOverlayItemKind[] RenderOrder =
        {
            PresentationOverlayItemKind.Rect,
            PresentationOverlayItemKind.Bar,
            PresentationOverlayItemKind.Text
        };

        private readonly SKPaint _fillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        private readonly SKPaint _textPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
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
            }
        }

        public int CachedTextLayoutCount => _textLayoutCache.Count;

        public int RebuiltLaneCountLastFrame { get; private set; }

        public void ResetFrameStats()
        {
            RebuiltLaneCountLastFrame = 0;
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
                RenderLane(scene, canvas, layer, RenderOrder[i], hasRefreshPlan: false, refreshDirtyLane: true);
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
                RenderLane(scene, canvas, layer, kind, hasRefreshPlan: true, refreshDirtyLane: refreshPlan.ShouldRefresh(kind));
            }
        }

        public void RenderLane(
            PresentationOverlayScene scene,
            SKCanvas canvas,
            PresentationOverlayLayer layer,
            PresentationOverlayItemKind kind)
        {
            RenderLane(scene, canvas, layer, kind, hasRefreshPlan: false, refreshDirtyLane: true);
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
                if (kind == PresentationOverlayItemKind.Text)
                {
                    ClearLargeTextLaneState(laneIndex);
                }

                RenderLargeImmediateLane(scene, canvas, layer, kind, laneIndex, laneVersion, span);
                return;
            }

            if (hasRefreshPlan && isLargeUnderUiLane && kind is PresentationOverlayItemKind.Text or PresentationOverlayItemKind.Bar)
            {
                if (kind == PresentationOverlayItemKind.Text)
                {
                    ClearLargeTextLaneState(laneIndex);
                }

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
            DrawLargeLaneHotpath(canvas, kind, span);
        }

        public void Dispose()
        {
            ClearTextLayoutCache();
            ClearTextSpriteCache();
            ClearBarSpriteCache();
            foreach ((_, SKFont font) in _fontCache)
            {
                font.Dispose();
            }

            for (int i = 0; i < _lanePictures.Length; i++)
            {
                _lanePictures[i]?.Dispose();
                _lanePictures[i] = null;
                _largeTextLaneStates[i].Clear();
            }

            _fontCache.Clear();
            _fillPaint.Dispose();
            _strokePaint.Dispose();
            _textPaint.Dispose();
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
                }
            }
        }

        private void DrawLargeLaneHotpath(SKCanvas canvas, PresentationOverlayItemKind kind, ReadOnlySpan<PresentationOverlayItem> span)
        {
            switch (kind)
            {
                case PresentationOverlayItemKind.Bar:
                    for (int i = 0; i < span.Length; i++)
                    {
                        DrawBar(canvas, span[i]);
                    }

                    break;

                case PresentationOverlayItemKind.Text:
                    DrawTextDirect(canvas, span);
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
                RebuildLanePicture(scene, layer, kind, laneIndex, laneVersion);
            }

            DrawLanePictureOrHotpath(canvas, kind, laneIndex, span);
        }

        private void DrawBarBatched(SKCanvas canvas, ReadOnlySpan<PresentationOverlayItem> span)
        {
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

            for (int bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
            {
                BarBatchBucket bucket = _barBatchBuckets[bucketIndex];
                if (bucket.Count == 1)
                {
                    canvas.DrawImage(bucket.Image, bucket.X[0], bucket.Y[0]);
                    continue;
                }

                bucket.PrepareAtlas();
                canvas.DrawAtlas(bucket.Image, bucket.Sprites, bucket.Transforms, SKSamplingOptions.Default);
            }
        }

        private void DrawTextBatched(SKCanvas canvas, ReadOnlySpan<PresentationOverlayItem> span)
        {
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
        }

        private CachedTextLayout GetTextLayout(string text, int fontSize)
        {
            var cacheKey = new TextLayoutCacheKey(text, fontSize);
            if (_textLayoutCache.TryGetValue(cacheKey, out CachedTextLayout? cached))
            {
                return cached;
            }

            if (_textLayoutCache.Count >= MaxTextLayoutCacheEntries)
            {
                ClearTextLayoutCache();
            }

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
                CachedTextLayout layout = GetTextLayout(item.Text, fontSize);
                float baselineY = item.Y + fontSize;
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

        private void DrawTextSpriteBatched(SKCanvas canvas, ReadOnlySpan<PresentationOverlayItem> span)
        {
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

            for (int bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
            {
                TextSpriteBatchBucket bucket = _textSpriteBatchBuckets[bucketIndex];
                if (bucket.Count == 1)
                {
                    canvas.DrawImage(bucket.Sprite.Image, bucket.X[0], bucket.Y[0]);
                    continue;
                }

                bucket.PrepareAtlas();
                canvas.DrawAtlas(bucket.Sprite.Image, bucket.Sprites, bucket.Transforms, SKSamplingOptions.Default);
            }
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
                return sprite;
            }

            if (_textSpriteCache.Count >= MaxTextSpriteCacheEntries)
            {
                ClearTextSpriteCache();
            }

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
                return new CachedBarSprite(image);
            }

            if (_barSpriteCache.Count >= MaxBarSpriteCacheEntries)
            {
                ClearBarSpriteCache();
            }

            int widthPx = Math.Max(1, (int)MathF.Ceiling(item.Width));
            int heightPx = Math.Max(1, (int)MathF.Ceiling(item.Height));
            using var surface = SKSurface.Create(new SKImageInfo(widthPx, heightPx));
            SKCanvas spriteCanvas = surface.Canvas;
            spriteCanvas.Clear(SKColors.Transparent);

            SKRect rect = new(0f, 0f, item.Width, item.Height);
            _fillPaint.Color = ToSkColor(item.Color0);
            spriteCanvas.DrawRect(rect, _fillPaint);

            float clampedValue = Math.Clamp(item.Value0, 0f, 1f);
            if (clampedValue > 0f)
            {
                _fillPaint.Color = ToSkColor(item.Color1);
                spriteCanvas.DrawRect(0f, 0f, item.Width * clampedValue, item.Height, _fillPaint);
            }

            _strokePaint.Color = SKColors.Black;
            spriteCanvas.DrawRect(rect, _strokePaint);

            image = surface.Snapshot();
            _barSpriteCache[key] = image;
            return new CachedBarSprite(image);
        }

        private static BarSpriteCacheKey CreateBarSpriteCacheKey(in PresentationOverlayItem item)
        {
            return new BarSpriteCacheKey(
                BitConverter.SingleToInt32Bits(item.Width),
                BitConverter.SingleToInt32Bits(item.Height),
                BitConverter.SingleToInt32Bits(item.Value0),
                ToColorKey(ToSkColor(item.Color0)),
                ToColorKey(ToSkColor(item.Color1)));
        }

        private void ClearBarSpriteCache()
        {
            foreach ((_, SKImage image) in _barSpriteCache)
            {
                image.Dispose();
            }

            _barSpriteCache.Clear();
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
                DrawLargeLaneHotpath(canvas, kind, span);
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
            if (layer != PresentationOverlayLayer.UnderUi || itemCount <= 0)
            {
                return false;
            }

            return kind switch
            {
                PresentationOverlayItemKind.Bar => itemCount >= ImmediateUnderUiBarThreshold,
                PresentationOverlayItemKind.Text => itemCount >= ImmediateUnderUiTextThreshold,
                _ => false,
            };
        }


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
            return ((int)layer * 3) + ((int)kind - 1);
        }

        private readonly record struct FontCacheKey(string FamilyName, int FontSize);

        private readonly record struct BarSpriteCacheKey(
            int WidthBits,
            int HeightBits,
            int ValueBits,
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

            public SKImage Image { get; private set; } = null!;

            public int Count { get; private set; }

            public float[] X => _x;

            public float[] Y => _y;

            public SKRect[] Sprites => _sprites;

            public SKRotationScaleMatrix[] Transforms => _transforms;

            public void Reset(SKImage image, float width, float height)
            {
                Image = image;
                Count = 0;
            }

            public void Add(float x, float y)
            {
                EnsureCapacity(Count + 1);
                _x[Count] = x;
                _y[Count] = y;
                Count++;
            }

            public void PrepareAtlas()
            {
                if (_sprites.Length != Count)
                {
                    Array.Resize(ref _sprites, Count);
                }

                if (_transforms.Length != Count)
                {
                    Array.Resize(ref _transforms, Count);
                }

                SKRect spriteRect = new(0f, 0f, Image.Width, Image.Height);
                for (int i = 0; i < Count; i++)
                {
                    _sprites[i] = spriteRect;
                    _transforms[i] = SKRotationScaleMatrix.CreateTranslation(_x[i], _y[i]);
                }
            }

            private void EnsureCapacity(int required)
            {
                if (_x.Length >= required && _y.Length >= required)
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
            }
        }

        private sealed class TextSpriteBatchBucket
        {
            private float[] _x = Array.Empty<float>();
            private float[] _y = Array.Empty<float>();
            private SKRect[] _sprites = Array.Empty<SKRect>();
            private SKRotationScaleMatrix[] _transforms = Array.Empty<SKRotationScaleMatrix>();

            public CachedTextSprite Sprite { get; private set; } = null!;

            public int Count { get; private set; }

            public float[] X => _x;

            public float[] Y => _y;

            public SKRect[] Sprites => _sprites;

            public SKRotationScaleMatrix[] Transforms => _transforms;

            public void Reset(CachedTextSprite sprite)
            {
                Sprite = sprite;
                Count = 0;
            }

            public void Add(float x, float y)
            {
                EnsureCapacity(Count + 1);
                _x[Count] = x;
                _y[Count] = y;
                Count++;
            }

            public void PrepareAtlas()
            {
                if (_sprites.Length != Count)
                {
                    Array.Resize(ref _sprites, Count);
                }

                if (_transforms.Length != Count)
                {
                    Array.Resize(ref _transforms, Count);
                }

                SKRect spriteRect = new(0f, 0f, Sprite.Image.Width, Sprite.Image.Height);
                for (int i = 0; i < Count; i++)
                {
                    _sprites[i] = spriteRect;
                    _transforms[i] = SKRotationScaleMatrix.CreateTranslation(_x[i], _y[i]);
                }
            }

            private void EnsureCapacity(int required)
            {
                if (_x.Length >= required && _y.Length >= required)
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
