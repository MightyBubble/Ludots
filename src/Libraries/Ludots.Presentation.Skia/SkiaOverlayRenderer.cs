using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation;
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
        private const int TextBatchBucketsPerBlob = 256;
        private const int TextChurnSampleCount = 32;
        private const byte TextSpritePromotionStableFrames = 3;

        /// <summary>文本精灵烘焙的左内边距（防负 left bearing 被精灵边缘裁剪）；
        /// 所有把精灵贴到 item.X 的消费点都必须减去它，否则比直绘右偏 1px。</summary>
        private const float TextSpriteBakePaddingX = 1f;
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
        private readonly TextChurnSampler[] _textChurnSamplers = new TextChurnSampler[LaneCount];
        private readonly RetainedBarLaneState[] _retainedBarLanes = new RetainedBarLaneState[LaneCount];
        private readonly RetainedTextSpriteLaneState[] _retainedTextSpriteLanes = new RetainedTextSpriteLaneState[LaneCount];
        private readonly SKPicture?[] _lanePictures = new SKPicture?[LaneCount];
        private readonly int[] _laneVersions = new int[LaneCount];
        private readonly float[] _lanePictureOffsetsX = new float[LaneCount];
        private readonly float[] _lanePictureOffsetsY = new float[LaneCount];
        private readonly StringBuilder _runText = new();

        public SkiaOverlayRenderer()
        {
            Array.Fill(_laneVersions, -1);
            for (int i = 0; i < LaneCount; i++)
            {
                _textChurnSamplers[i] = new TextChurnSampler();
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

            if (TrySaveClipShape(canvas, markers.ClipShape, out int saveCount))
            {
                try
                {
                    DrawMinimapMarkersBatched(canvas, markers);
                }
                finally
                {
                    canvas.RestoreToCount(saveCount);
                }

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

        private void DrawRectWithClip(SKCanvas canvas, in PresentationOverlayItem item)
        {
            if (TrySaveClipShape(canvas, item.ClipShape, out int saveCount))
            {
                try
                {
                    DrawRect(canvas, item);
                }
                finally
                {
                    canvas.RestoreToCount(saveCount);
                }

                return;
            }

            DrawRect(canvas, item);
        }

        private void DrawBar(SKCanvas canvas, in PresentationOverlayItem item)
        {
            CachedBarSprite sprite = GetBarSprite(item);
            canvas.DrawImage(sprite.Image, item.X - sprite.OffsetX, item.Y - sprite.OffsetY);
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
            ScreenHudDecoration deco = item.Decoration;
            if (deco.ShadowColor.W > 0.001f)
            {
                using var shadowPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = ToSkColor(deco.ShadowColor),
                    MaskFilter = deco.ShadowBlur > 0.01f
                        ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, deco.ShadowBlur * 0.5f)
                        : null,
                };
                SKRect shadowRect = new(
                    rect.Left + deco.ShadowOffsetX,
                    rect.Top + deco.ShadowOffsetY,
                    rect.Right + deco.ShadowOffsetX,
                    rect.Bottom + deco.ShadowOffsetY);
                DrawRoundedOrPlain(canvas, in shadowRect, deco.CornerRadius, shadowPaint);
            }

            SKRect inner = new(rect.Left + deco.Padding, rect.Top + deco.Padding, rect.Right - deco.Padding, rect.Bottom - deco.Padding);
            SKShader? backgroundShader = null;
            if (deco.BackgroundGradientTo.W > 0.001f)
            {
                backgroundShader = SKShader.CreateLinearGradient(
                    new SKPoint(inner.Left, inner.Top),
                    new SKPoint(inner.Right, inner.Top),
                    new[] { ToSkColor(item.Color0), ToSkColor(deco.BackgroundGradientTo) },
                    null,
                    SKShaderTileMode.Clamp);
                _fillPaint.Shader = backgroundShader;
            }
            else
            {
                _fillPaint.Color = ToSkColor(item.Color0);
            }

            DrawRoundedOrPlain(canvas, in inner, deco.CornerRadius, _fillPaint);
            _fillPaint.Shader = null;
            backgroundShader?.Dispose();

            float clampedValue = Math.Clamp(item.Value0, 0f, 1f);
            if (clampedValue > 0f && inner.Width > 0.5f)
            {
                SKShader? fillShader = null;
                if (deco.FillGradientTo.W > 0.001f)
                {
                    fillShader = SKShader.CreateLinearGradient(
                        new SKPoint(inner.Left, inner.Top),
                        new SKPoint(inner.Right, inner.Top),
                        new[] { ToSkColor(item.Color1), ToSkColor(deco.FillGradientTo) },
                        null,
                        SKShaderTileMode.Clamp);
                    _fillPaint.Shader = fillShader;
                }
                else
                {
                    _fillPaint.Color = ToSkColor(item.Color1);
                }

                int save = canvas.SaveLayer();
                canvas.ClipRect(SKRect.Create(inner.Left, inner.Top, inner.Width * clampedValue, inner.Height));
                DrawRoundedOrPlain(canvas, in inner, deco.CornerRadius, _fillPaint);
                canvas.RestoreToCount(save);
                _fillPaint.Shader = null;
                fillShader?.Dispose();
            }

            if (deco.BorderWidth > 0.01f)
            {
                _strokePaint.StrokeWidth = deco.BorderWidth;
                _strokePaint.Color = deco.BorderColor.W > 0.001f ? ToSkColor(deco.BorderColor) : SKColors.Black;
            }
            else
            {
                _strokePaint.StrokeWidth = 1f;
                _strokePaint.Color = SKColors.Black;
            }

            DrawRoundedOrPlainStroke(canvas, in rect, deco.CornerRadius, _strokePaint);
            _strokePaint.StrokeWidth = 1f;
        }

        private void DrawText(SKCanvas canvas, in PresentationOverlayItem item)
        {
            if (string.IsNullOrEmpty(item.Text))
            {
                return;
            }

            int fontSize = item.FontSize <= 0 ? 16 : item.FontSize;
            ScreenHudDecoration deco = item.Decoration;
            _textPaint.Color = ToSkColor(item.Color0);
            CachedTextLayout layout = GetTextLayout(item.Text, fontSize, deco.StyleFlags);
            float baselineY = item.Y + fontSize;
            float originX = ResolveTextOriginX(item.X, layout.Width, deco.StyleFlags);

            if (deco.BoxBackground.W > 0.001f)
            {
                SKRect boxRect = new(
                    originX - deco.Padding - deco.BorderWidth,
                    item.Y - deco.Padding - deco.BorderWidth,
                    originX + layout.Width + deco.Padding + deco.BorderWidth,
                    item.Y + MathF.Max(1, fontSize) * 1.5f + deco.Padding + deco.BorderWidth);
                _fillPaint.Color = ToSkColor(deco.BoxBackground);
                DrawRoundedOrPlain(canvas, in boxRect, deco.CornerRadius, _fillPaint);
                if (deco.BorderWidth > 0.01f)
                {
                    _strokePaint.StrokeWidth = deco.BorderWidth;
                    _strokePaint.Color = deco.BorderColor.W > 0.001f ? ToSkColor(deco.BorderColor) : SKColors.Black;
                    DrawRoundedOrPlainStroke(canvas, in boxRect, deco.CornerRadius, _strokePaint);
                    _strokePaint.StrokeWidth = 1f;
                }
            }

            if (deco.ShadowColor.W > 0.001f)
            {
                using var shadowPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = ToSkColor(deco.ShadowColor),
                    MaskFilter = deco.ShadowBlur > 0.01f
                        ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, deco.ShadowBlur * 0.5f)
                        : null,
                };
                for (int i = 0; i < layout.Runs.Length; i++)
                {
                    CachedTextRun run = layout.Runs[i];
                    if (run.Blob != null)
                    {
                        canvas.DrawText(
                            run.Blob,
                            originX + run.XOffset + deco.ShadowOffsetX,
                            baselineY + deco.ShadowOffsetY,
                            shadowPaint);
                    }
                }
            }

            for (int i = 0; i < layout.Runs.Length; i++)
            {
                CachedTextRun run = layout.Runs[i];
                if (run.Blob != null)
                {
                    canvas.DrawText(run.Blob, originX + run.XOffset, baselineY, _textPaint);
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

        private void DrawLineWithClip(SKCanvas canvas, in PresentationOverlayItem item)
        {
            if (TrySaveClipShape(canvas, item.ClipShape, out int saveCount))
            {
                try
                {
                    DrawLine(canvas, item);
                }
                finally
                {
                    canvas.RestoreToCount(saveCount);
                }

                return;
            }

            DrawLine(canvas, item);
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
                        DrawRectWithClip(canvas, item);
                        break;

                    case PresentationOverlayItemKind.Line:
                        DrawLineWithClip(canvas, item);
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
                        if (_textChurnSamplers[laneIndex].IsChurned(span))
                        {
                            DrawTextBatched(canvas, span);
                        }
                        else
                        {
                            DrawTextSpriteRetainedBatched(canvas, laneIndex, laneVersion, span);
                        }
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
                    _barBatchBuckets[bucketIndex].Reset(sprite.Image, item.Width, item.Height, sprite.OffsetX, sprite.OffsetY);
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

            int bucketCount = state.Buckets.Count;
            int totalInstanceCount = 0;
            for (int bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
            {
                totalInstanceCount += state.Buckets[bucketIndex].Count;
            }

            if (totalInstanceCount <= 0)
            {
                return 0;
            }

            // 所有 bar 桶共享同一张 atlas，实例合并成一次 draw atlas；
            // fill 量化会让桶数随可见取值线性增长，逐桶签发会等量放大绘制调用
            state.EnsureDrawCapacity(totalInstanceCount);
            int writeIndex = 0;
            int activeBucketCount = 0;
            for (int bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
            {
                RetainedBarBatchBucket bucket = state.Buckets[bucketIndex];
                int count = bucket.Count;
                if (count <= 0)
                {
                    continue;
                }

                activeBucketCount++;
                SKRect spriteRect = state.AtlasSprites[bucketIndex];
                float[] positionsX = bucket.X;
                float[] positionsY = bucket.Y;
                SKRotationScaleMatrix[] drawTransforms = state.DrawTransforms;
                SKRect[] drawSprites = state.DrawSprites;
                float bucketOffsetX = bucket.OffsetX;
                float bucketOffsetY = bucket.OffsetY;
                for (int instanceIndex = 0; instanceIndex < count; instanceIndex++)
                {
                    drawSprites[writeIndex] = spriteRect;
                    drawTransforms[writeIndex] = new SKRotationScaleMatrix(
                        1f, 0f,
                        positionsX[instanceIndex] - bucketOffsetX,
                        positionsY[instanceIndex] - bucketOffsetY);
                    writeIndex++;
                }
            }

            DrawAtlasCount(canvas, state.AtlasImage, state.DrawSprites, state.DrawTransforms, totalInstanceCount);
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

                if (state.OrderStableIds[i] == stableId)
                {
                    RetainedBarEntry orderEntry = state.OrderEntries[i];
                    if (orderEntry.BucketIndex >= 0 &&
                        (uint)orderEntry.BucketIndex < (uint)state.Buckets.Count &&
                        orderEntry.DirtySerial == item.DirtySerial)
                    {
                        state.Buckets[orderEntry.BucketIndex].AddVisible(item.X, item.Y);
                        continue;
                    }
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
            state.Buckets.Add(new RetainedBarBatchBucket(sprite.Image, sprite.OffsetX, sprite.OffsetY));
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

            // 桶数随 fill 量化取值增长，单行条带会持续拉宽；按限宽 shelf 换行打包
            const int maxAtlasWidth = 2048;
            state.EnsureAtlasSpriteCapacity(bucketCount);
            int atlasWidth = 0;
            int atlasHeight = 0;
            int cursorX = 0;
            int rowHeight = 0;
            for (int i = 0; i < bucketCount; i++)
            {
                SKImage image = state.Buckets[i].Image;
                if (cursorX > 0 && cursorX + image.Width > maxAtlasWidth)
                {
                    cursorX = 0;
                    atlasHeight += rowHeight;
                    rowHeight = 0;
                }

                state.AtlasSprites[i] = new SKRect(cursorX, atlasHeight, cursorX + image.Width, atlasHeight + image.Height);
                cursorX += image.Width;
                atlasWidth = Math.Max(atlasWidth, cursorX);
                rowHeight = Math.Max(rowHeight, image.Height);
            }

            atlasHeight += rowHeight;
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
                atlasCanvas.DrawImage(state.Buckets[i].Image, sprite.Left, sprite.Top);
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
                DrawBarDirect(canvas, in item);
            }

            LastBarBatchBucketCount += 1;
            LastBarBatchDrawMs += ElapsedMs(drawStart);
        }

        private void DrawTextBatched(SKCanvas canvas, ReadOnlySpan<PresentationOverlayItem> span, ReadOnlySpan<int> itemIndices = default)
        {
            long buildStart = Stopwatch.GetTimestamp();
            _textBatchMap.Clear();
            int bucketCount = 0;
            bool hasSubset = itemIndices.Length > 0;
            int itemCount = hasSubset ? itemIndices.Length : span.Length;

            for (int itemCursor = 0; itemCursor < itemCount; itemCursor++)
            {
                ref readonly PresentationOverlayItem item = ref (hasSubset
                    ? ref span[itemIndices[itemCursor]]
                    : ref span[itemCursor]);
                if (string.IsNullOrEmpty(item.Text))
                {
                    continue;
                }

                int fontSize = item.FontSize <= 0 ? 16 : item.FontSize;
                SKColor color = ToSkColor(item.Color0);
                if (item.Decoration.ShadowColor.W > 0.001f || item.Decoration.BoxBackground.W > 0.001f)
                {
                    // 阴影/底板无法进文本 blob 批,回退逐条直绘(样式仍然完整)。
                    DrawText(canvas, item);
                    continue;
                }

                var key = new TextBatchKey(item.Text, fontSize, ToColorKey(color), item.Decoration.StyleFlags);
                if (!_textBatchMap.TryGetValue(key, out int bucketIndex))
                {
                    if (bucketCount >= _textBatchBuckets.Count)
                    {
                        _textBatchBuckets.Add(new TextBatchBucket());
                    }

                    CachedTextLayout layout = GetTextLayout(item.Text, fontSize, item.Decoration.StyleFlags);
                    bucketIndex = bucketCount++;
                    _textBatchMap[key] = bucketIndex;
                    _textBatchBuckets[bucketIndex].Reset(layout, color);
                }

                _textBatchBuckets[bucketIndex].Add(
                    ResolveTextOriginX(item.X, _textBatchBuckets[bucketIndex].Layout.Width, item.Decoration.StyleFlags),
                    item.Y + fontSize);
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

        private CachedTextLayout GetTextLayout(string text, int fontSize, byte styleFlags = 0)
        {
            var cacheKey = new TextLayoutCacheKey(text, fontSize, styleFlags);
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
                    cursorX = FlushRun(runs, activeTypeface, fontSize, cursorX, styleFlags);
                    _runText.Clear();
                }

                activeTypeface = typeface;
                _runText.Append(element);
            }

            if (_runText.Length > 0 && activeTypeface != null)
            {
                cursorX = FlushRun(runs, activeTypeface, fontSize, cursorX, styleFlags);
            }

            var created = new CachedTextLayout(runs.ToArray(), cursorX);
            _textLayoutCache[cacheKey] = created;
            return created;
        }

        private float FlushRun(List<CachedTextRun> runs, SKTypeface typeface, int fontSize, float cursorX, byte styleFlags = 0)
        {
            string runText = _runText.ToString();
            SKFont font = GetFont(typeface, fontSize, styleFlags);
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
                            DrawRectWithClip(pictureCanvas, item);
                            break;

                        case PresentationOverlayItemKind.Line:
                            DrawLineWithClip(pictureCanvas, item);
                            break;
                    }
                }
            }

            _lanePictures[laneIndex] = recorder.EndRecording();
            RebuiltLaneCountLastFrame++;
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
                CachedTextLayout layout = GetTextLayout(item.Text, fontSize, item.Decoration.StyleFlags);
                float originX = ResolveTextOriginX(item.X, layout.Width, item.Decoration.StyleFlags);
                for (int runIndex = 0; runIndex < layout.Runs.Length; runIndex++)
                {
                    CachedTextRun run = layout.Runs[runIndex];
                    if (run.Blob != null)
                    {
                        canvas.DrawText(run.Blob, originX + run.XOffset, baselineY, _textPaint);
                    }
                }
            }
        }

        private const byte TextAlignCenterFlag = 0x04;

        /// <summary>css text-align:center 的锚定语义:锚点 X 即文本水平中心(与血条一致);默认左锚。</summary>
        private static float ResolveTextOriginX(float anchorX, float textWidth, byte styleFlags)
        {
            return (styleFlags & TextAlignCenterFlag) != 0 ? anchorX - textWidth * 0.5f : anchorX;
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
                var key = new TextBatchKey(item.Text, fontSize, ToColorKey(color), item.Decoration.StyleFlags);
                if (!_textSpriteBatchMap.TryGetValue(key, out int bucketIndex))
                {
                    if (bucketCount >= _textSpriteBatchBuckets.Count)
                    {
                        _textSpriteBatchBuckets.Add(new TextSpriteBatchBucket());
                    }

                    bucketIndex = bucketCount++;
                    _textSpriteBatchMap[key] = bucketIndex;
                    CachedTextSprite sprite = GetTextSprite(item.Text, fontSize, color, item.Decoration);
                    _textSpriteBatchBuckets[bucketIndex].Reset(sprite);
                }

                CachedTextSprite cachedSprite = _textSpriteBatchBuckets[bucketIndex].Sprite;
                float drawY = (item.Y + fontSize) - cachedSprite.BaselineY;
                _textSpriteBatchBuckets[bucketIndex].Add(
                    ResolveTextOriginX(item.X, cachedSprite.TextWidth, item.Decoration.StyleFlags),
                    drawY);
            }

            LastTextSpriteBatchBucketCount += bucketCount;
            LastTextBatchBuildMs += ElapsedMs(buildStart);
            long drawStart = Stopwatch.GetTimestamp();
            for (int bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
            {
                TextSpriteBatchBucket bucket = _textSpriteBatchBuckets[bucketIndex];
                if (bucket.Count == 1)
                {
                    canvas.DrawImage(bucket.Sprite.Image, bucket.X[0] - TextSpriteBakePaddingX, bucket.Y[0]);
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
            if (state.PendingGlyphIndices.Count > 0)
            {
                DrawTextBatched(canvas, span, CollectionsMarshal.AsSpan(state.PendingGlyphIndices));
            }
        }

        private static bool CanUseRetainedTextSprites(ReadOnlySpan<PresentationOverlayItem> span)
        {
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationOverlayItem item = ref span[i];
                if (string.IsNullOrEmpty(item.Text))
                {
                    continue;
                }

                if (item.StableId <= 0)
                {
                    return false;
                }

                // 阴影/底板需要逐条烘进精灵的画布外扩,retained 图集按无外扩精灵布局;
                // 带这两种装饰的条目退回 blob 批(其内部再回退直绘),样式不丢。
                if (item.Decoration.ShadowColor.W > 0.001f || item.Decoration.BoxBackground.W > 0.001f)
                {
                    return false;
                }
            }

            return true;
        }

        private static int TextStabilityIdentity(in PresentationOverlayItem item)
        {
            if (item.DirtySerial != 0)
            {
                return item.DirtySerial;
            }

            return string.IsNullOrEmpty(item.Text) ? 0 : RuntimeHelpers.GetHashCode(item.Text);
        }

        private int DrawRetainedTextAtlas(SKCanvas canvas, RetainedTextSpriteLaneState state)
        {
            EnsureRetainedTextAtlas(state);
            if (state.AtlasImage == null)
            {
                return 0;
            }

            int totalInstanceCount = 0;
            int bucketCount = state.Buckets.Count;
            for (int bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
            {
                totalInstanceCount += state.Buckets[bucketIndex].Count;
            }

            if (totalInstanceCount <= 0)
            {
                return 0;
            }

            // 所有 bucket 共享同一张 atlas：实例合并成一次 draw atlas，
            // 避免 distinct 字符串数（数值文本可达数百）线性放大绘制调用数
            state.EnsureDrawCapacity(totalInstanceCount);
            int writeIndex = 0;
            int activeBucketCount = 0;
            for (int bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
            {
                RetainedTextSpriteBatchBucket bucket = state.Buckets[bucketIndex];
                int count = bucket.Count;
                if (count <= 0)
                {
                    continue;
                }

                activeBucketCount++;
                SKRect spriteRect = state.AtlasSprites[bucketIndex];
                float[] positionsX = bucket.X;
                float[] positionsY = bucket.Y;
                SKRotationScaleMatrix[] drawTransforms = state.DrawTransforms;
                SKRect[] drawSprites = state.DrawSprites;
                for (int instanceIndex = 0; instanceIndex < count; instanceIndex++)
                {
                    drawSprites[writeIndex] = spriteRect;
                    // 实例坐标取整到像素：亚像素基线会让文字逐帧微移而抖动（bar 走整数平移稳定，
                    // text 是独立 sprite 实例），像素对齐后免抖。bake 内边距是常值，取整仍保留。
                    float snappedX = MathF.Round(positionsX[instanceIndex] - TextSpriteBakePaddingX);
                    float snappedY = MathF.Round(positionsY[instanceIndex]);
                    drawTransforms[writeIndex] = new SKRotationScaleMatrix(1f, 0f, snappedX, snappedY);
                    writeIndex++;
                }
            }

            DrawAtlasCount(canvas, state.AtlasImage, state.DrawSprites, state.DrawTransforms, totalInstanceCount);
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
            state.PendingGlyphIndices.Clear();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationOverlayItem item = ref span[i];
                if (string.IsNullOrEmpty(item.Text))
                {
                    state.OrderStableIds[i] = 0;
                    continue;
                }

                int stableId = item.StableId;
                if (stableId <= 0)
                {
                    RebuildRetainedTextSpriteLane(state, laneVersion, span);
                    return;
                }

                int fontSize = item.FontSize <= 0 ? 16 : item.FontSize;
                int identity = TextStabilityIdentity(in item);
                byte streak;
                if (state.OrderStableIds[i] == stableId && state.OrderCandidateSerials[i] == identity)
                {
                    byte previousStreak = state.OrderStableStreaks[i];
                    streak = previousStreak >= TextSpritePromotionStableFrames
                        ? TextSpritePromotionStableFrames
                        : (byte)(previousStreak + 1);
                }
                else
                {
                    streak = 1;
                }

                state.OrderStableIds[i] = stableId;
                state.OrderCandidateSerials[i] = identity;
                state.OrderStableStreaks[i] = streak;
                state.OrderFontSizes[i] = fontSize;

                if (streak < TextSpritePromotionStableFrames)
                {
                    // 未稳定文本本帧走 glyph 直排且不新建精灵：churn 取值只重置 streak，
                    // 打不进 sprite 缓存；连续稳定帧数达标后才晋升为保留精灵
                    state.PendingGlyphIndices.Add(i);
                    state.OrderEntries[i] = new RetainedTextSpriteEntry(-1, 0, default, item.DirtySerial, fontSize, stamp);
                    continue;
                }

                RetainedTextSpriteEntry orderEntry = state.OrderEntries[i];
                if (orderEntry.BucketIndex >= 0 &&
                    (uint)orderEntry.BucketIndex < (uint)state.Buckets.Count &&
                    item.DirtySerial != 0 &&
                    orderEntry.DirtySerial == item.DirtySerial)
                {
                    RetainedTextSpriteBatchBucket orderBucket = state.Buckets[orderEntry.BucketIndex];
                    float orderDrawY = (item.Y + fontSize) - state.BucketBaselines[orderEntry.BucketIndex];
                    orderBucket.AddVisible(
                        ResolveTextOriginX(item.X, orderBucket.Sprite.TextWidth, item.Decoration.StyleFlags),
                        orderDrawY);
                    continue;
                }

                if (state.ItemsByStableId.TryGetValue(stableId, out RetainedTextSpriteEntry entry) &&
                    entry.BucketIndex >= 0 &&
                    (uint)entry.BucketIndex < (uint)state.Buckets.Count &&
                    (item.DirtySerial != 0
                        ? entry.DirtySerial == item.DirtySerial
                        : entry.Key.Equals(new TextBatchKey(item.Text, fontSize, ToColorKey(ToSkColor(item.Color0)), item.Decoration.StyleFlags))))
                {
                    RetainedTextSpriteBatchBucket bucket = state.Buckets[entry.BucketIndex];
                    float drawY = (item.Y + fontSize) - state.BucketBaselines[entry.BucketIndex];
                    bucket.AddVisible(
                        ResolveTextOriginX(item.X, bucket.Sprite.TextWidth, item.Decoration.StyleFlags),
                        drawY);
                    entry.SeenStamp = stamp;
                    entry.DirtySerial = item.DirtySerial;
                    entry.FontSize = fontSize;
                    state.ItemsByStableId[stableId] = entry;
                    state.OrderEntries[i] = entry;
                    continue;
                }

                SKColor color = ToSkColor(item.Color0);
                AddRetainedTextSpriteEntry(
                    state,
                    stableId,
                    new TextBatchKey(item.Text, fontSize, ToColorKey(color), item.Decoration.StyleFlags),
                    item,
                    fontSize,
                    color,
                    stamp,
                    i,
                    item.Decoration);
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
                float drawY = (item.Y + fontSize) - state.BucketBaselines[entry.BucketIndex];
                bucket.AddVisible(
                    ResolveTextOriginX(item.X, bucket.Sprite.TextWidth, item.Decoration.StyleFlags),
                    drawY);
            }

            state.LastVersion = laneVersion;
            state.PendingGlyphIndices.Clear();
            return true;
        }

        private void RebuildRetainedTextSpriteLane(
            RetainedTextSpriteLaneState state,
            int laneVersion,
            ReadOnlySpan<PresentationOverlayItem> span)
        {
            state.Clear();
            state.PendingGlyphIndices.Clear();
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
                    new TextBatchKey(item.Text, fontSize, ToColorKey(color), item.Decoration.StyleFlags),
                    item,
                    fontSize,
                    color,
                    stamp,
                    orderCount,
                    item.Decoration);
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
            int orderIndex,
            in ScreenHudDecoration decoration)
        {
            int bucketIndex = GetOrCreateRetainedTextSpriteBucket(state, key, item.Text!, fontSize, color, decoration);
            RetainedTextSpriteBatchBucket bucket = state.Buckets[bucketIndex];
            float drawY = (item.Y + fontSize) - bucket.Sprite.BaselineY;
            int slotIndex = bucket.Add(
                stableId,
                ResolveTextOriginX(item.X, bucket.Sprite.TextWidth, item.Decoration.StyleFlags),
                drawY);
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
            SKColor color,
            in ScreenHudDecoration decoration)
        {
            if (state.BucketIndexByKey.TryGetValue(key, out int bucketIndex))
            {
                return bucketIndex;
            }

            bucketIndex = state.Buckets.Count;
            CachedTextSprite sprite = GetTextSprite(text, fontSize, color, decoration);
            state.Buckets.Add(new RetainedTextSpriteBatchBucket(sprite));
            if (state.BucketBaselines.Length <= bucketIndex)
            {
                Array.Resize(ref state.BucketBaselines, ResolveNextCapacity(state.BucketBaselines.Length, bucketIndex + 1));
            }

            state.BucketBaselines[bucketIndex] = sprite.BaselineY;
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

            // 数值文本的 distinct 字符串可达数百，单行 atlas 会拉出数万 px 宽的条带，
            // 每个精灵行的读取跨度等于整条行字节量，blit 全部 cache miss；按限宽换行打包
            const int maxAtlasWidth = 2048;
            state.EnsureAtlasSpriteCapacity(bucketCount);
            int atlasWidth = 0;
            int atlasHeight = 0;
            int cursorX = 0;
            int rowHeight = 0;
            for (int i = 0; i < bucketCount; i++)
            {
                SKImage image = state.Buckets[i].Sprite.Image;
                if (cursorX > 0 && cursorX + image.Width > maxAtlasWidth)
                {
                    cursorX = 0;
                    atlasHeight += rowHeight;
                    rowHeight = 0;
                }

                state.AtlasSprites[i] = new SKRect(cursorX, atlasHeight, cursorX + image.Width, atlasHeight + image.Height);
                cursorX += image.Width;
                atlasWidth = Math.Max(atlasWidth, cursorX);
                rowHeight = Math.Max(rowHeight, image.Height);
            }

            atlasHeight += rowHeight;
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
                atlasCanvas.DrawImage(state.Buckets[i].Sprite.Image, sprite.Left, sprite.Top);
            }

            state.AtlasImage = surface.Snapshot();
            state.AtlasDirty = false;
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

        private CachedTextSprite GetTextSprite(string text, int fontSize, SKColor color, in ScreenHudDecoration decoration = default)
        {
            var key = new TextSpriteCacheKey(text, fontSize, ToColorKey(color), decoration);
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
            CachedTextLayout layout = GetTextLayout(text, fontSize, decoration.StyleFlags);
            float ascent = fontSize;
            float descent = Math.Max(1f, fontSize * 0.25f);
            for (int i = 0; i < layout.Runs.Length; i++)
            {
                SKFontMetrics metrics = layout.Runs[i].Font.Metrics;
                ascent = Math.Max(ascent, -metrics.Ascent);
                descent = Math.Max(descent, metrics.Descent);
            }

            bool hasShadow = decoration.ShadowColor.W > 0.001f;
            bool hasBox = decoration.BoxBackground.W > 0.001f;
            float shadowMargin = hasShadow
                ? MathF.Ceiling(MathF.Abs(decoration.ShadowOffsetX) + MathF.Abs(decoration.ShadowOffsetY) + decoration.ShadowBlur)
                : 0f;
            float boxPad = hasBox ? decoration.Padding + decoration.BorderWidth : 0f;

            float baselineY = MathF.Ceiling(ascent) + 1f;
            int widthPx = Math.Max(1, (int)MathF.Ceiling(layout.Width) + 2 + (int)(2 * (shadowMargin + boxPad)));
            int heightPx = Math.Max(1, (int)MathF.Ceiling(ascent + descent) + 2 + (int)(2 * (shadowMargin + boxPad)));
            float originX = shadowMargin + boxPad;
            float originY = shadowMargin + boxPad;

            using var surface = SKSurface.Create(new SKImageInfo(widthPx, heightPx));
            SKCanvas spriteCanvas = surface.Canvas;
            spriteCanvas.Clear(SKColors.Transparent);

            float drawBaseline = originY + baselineY;
            if (hasShadow)
            {
                using var shadowPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = ToSkColor(decoration.ShadowColor),
                    MaskFilter = decoration.ShadowBlur > 0.01f
                        ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, decoration.ShadowBlur * 0.5f)
                        : null,
                };
                for (int runIndex = 0; runIndex < layout.Runs.Length; runIndex++)
                {
                    CachedTextRun run = layout.Runs[runIndex];
                    if (run.Blob != null)
                    {
                        spriteCanvas.DrawText(
                            run.Blob,
                            TextSpriteBakePaddingX + originX + run.XOffset + decoration.ShadowOffsetX,
                            drawBaseline + decoration.ShadowOffsetY,
                            shadowPaint);
                    }
                }
            }

            if (hasBox)
            {
                float boxLeft = originX - boxPad + decoration.Padding * 0f;
                SKRect boxRect = new(
                    shadowMargin,
                    shadowMargin,
                    widthPx - shadowMargin,
                    heightPx - shadowMargin);
                _fillPaint.Color = ToSkColor(decoration.BoxBackground);
                DrawRoundedOrPlain(spriteCanvas, boxRect, decoration.CornerRadius, _fillPaint);
                if (decoration.BorderWidth > 0.01f)
                {
                    _strokePaint.StrokeWidth = decoration.BorderWidth;
                    _strokePaint.Color = decoration.BorderColor.W > 0.001f
                        ? ToSkColor(decoration.BorderColor)
                        : SKColors.Black;
                    DrawRoundedOrPlainStroke(spriteCanvas, boxRect, decoration.CornerRadius, _strokePaint);
                    _strokePaint.StrokeWidth = 1f;
                }
            }

            _textPaint.Color = color;
            for (int runIndex = 0; runIndex < layout.Runs.Length; runIndex++)
            {
                CachedTextRun run = layout.Runs[runIndex];
                if (run.Blob != null)
                {
                    spriteCanvas.DrawText(run.Blob, TextSpriteBakePaddingX + originX + run.XOffset, drawBaseline, _textPaint);
                }
            }

            sprite = new CachedTextSprite(
                surface.Snapshot(),
                baselineY + originY,
                layout.Width,
                offsetX: originX,
                offsetY: originY);
            _textSpriteCache[key] = sprite;
            return sprite;
        }

        /// <summary>css clip-path 预设的归一化路径(0..1 坐标,乘以条目矩形)。</summary>
        private static readonly float[][] ClipShapePoints =
        {
            Array.Empty<float>(),
            // Shield:尖底盾——顶边全宽,底边中点收尖。
            new[] { 0f, 0f, 1f, 0f, 1f, 0.62f, 0.5f, 1f, 0f, 0.62f },
            // Diamond:菱形。
            new[] { 0.5f, 0f, 1f, 0.5f, 0.5f, 1f, 0f, 0.5f },
            // Pennant:燕尾——底边中央内凹。
            new[] { 0f, 0f, 1f, 0f, 1f, 0.78f, 0.5f, 0.55f, 0f, 0.78f },
            // Parallelogram:右斜平行四边形。
            new[] { 0.12f, 0f, 1f, 0f, 0.88f, 1f, 0f, 1f },
            // PointedBottom:下尖水滴。
            new[] { 0f, 0f, 1f, 0f, 1f, 0.72f, 0.5f, 1f, 0f, 0.72f },
        };

        private static SKPath BuildItemPath(HudClipShape clipShape, in SKRect rect, float radius)
        {
            SKPath path = new();
            if (clipShape != HudClipShape.None)
            {
                float[] points = ClipShapePoints[(int)clipShape];
                for (int i = 0; i < points.Length; i += 2)
                {
                    float x = rect.Left + points[i] * rect.Width;
                    float y = rect.Top + points[i + 1] * rect.Height;
                    if (i == 0)
                    {
                        path.MoveTo(x, y);
                    }
                    else
                    {
                        path.LineTo(x, y);
                    }
                }

                path.Close();
                return path;
            }

            if (radius > 0.5f)
            {
                path.AddRoundRect(rect, radius, radius);
            }
            else
            {
                path.AddRect(rect);
            }

            return path;
        }

        private static void DrawRoundedOrPlain(SKCanvas canvas, in SKRect rect, float radius, SKPaint paint)
        {
            if (radius > 0.5f)
            {
                canvas.DrawRoundRect(rect, radius, radius, paint);
            }
            else
            {
                canvas.DrawRect(rect, paint);
            }
        }

        private static void DrawRoundedOrPlainStroke(SKCanvas canvas, in SKRect rect, float radius, SKPaint paint)
        {
            if (radius > 0.5f)
            {
                canvas.DrawRoundRect(rect, radius, radius, paint);
            }
            else
            {
                canvas.DrawRect(rect, paint);
            }
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
            ScreenHudDecoration deco = key.Decoration;
            bool isIcon = !string.IsNullOrEmpty(key.ImageSource);
            bool hasShadow = deco.ShadowColor.W > 0.001f;
            float shadowMargin = hasShadow
                ? MathF.Ceiling(MathF.Abs(deco.ShadowOffsetX) + MathF.Abs(deco.ShadowOffsetY) + deco.ShadowBlur)
                : 0f;

            int widthPx = key.WidthPx;
            int heightPx = key.HeightPx;
            using var surface = SKSurface.Create(
                new SKImageInfo(widthPx + (int)(2 * shadowMargin), heightPx + (int)(2 * shadowMargin)));
            SKCanvas spriteCanvas = surface.Canvas;
            spriteCanvas.Clear(SKColors.Transparent);

            SKRect rect = new(shadowMargin, shadowMargin, shadowMargin + widthPx, shadowMargin + heightPx);
            float radius = deco.CornerRadius;
            float pad = deco.Padding;
            SKRect inner = new(rect.Left + pad, rect.Top + pad, rect.Right - pad, rect.Bottom - pad);

            if (hasShadow)
            {
                using var shadowPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = ToSkColor(deco.ShadowColor),
                    MaskFilter = deco.ShadowBlur > 0.01f
                        ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, deco.ShadowBlur * 0.5f)
                        : null,
                };
                SKRect shadowRect = new(
                    rect.Left + deco.ShadowOffsetX,
                    rect.Top + deco.ShadowOffsetY,
                    rect.Right + deco.ShadowOffsetX,
                    rect.Bottom + deco.ShadowOffsetY);
                DrawRoundedOrPlain(spriteCanvas, in shadowRect, radius, shadowPaint);
            }

            // 图标条目:阴影(可选)→图片铺满矩形,不做底/填充/渐变;装饰块边框照常套在外圈。
            if (isIcon)
            {
                if (UiImageSourceCache.TryGetResource(key.ImageSource, out UiImageSourceCache.UiImageResource? resource) && resource != null)
                {
                    using SKPath iconPath = BuildItemPath(deco.ClipShape, rect, radius);
                    spriteCanvas.Save();
                    spriteCanvas.ClipPath(iconPath);
                    bool sliced = deco.ImageSliceTop > 0f || deco.ImageSliceRight > 0f ||
                                  deco.ImageSliceBottom > 0f || deco.ImageSliceLeft > 0f;
                    if (sliced)
                    {
                        // 九宫格/三宫格:源矩形取位图尺寸或 SVG CullRect;切片非法时 fail-closed
                        // (不画图,不回退整图拉伸),与 UI 车道 NineSliceBaker 同一合同。
                        float sourceLeft = 0f;
                        float sourceTop = 0f;
                        float sourceWidth;
                        float sourceHeight;
                        if (resource.RasterImage != null)
                        {
                            sourceWidth = resource.RasterImage.Width;
                            sourceHeight = resource.RasterImage.Height;
                        }
                        else
                        {
                            SKRect cull = resource.SvgPicture!.CullRect;
                            sourceLeft = cull.Left;
                            sourceTop = cull.Top;
                            sourceWidth = cull.Width;
                            sourceHeight = cull.Height;
                        }

                        if (NineSliceBaker.TryBuildPatches(
                                sourceWidth, sourceHeight, sourceLeft, sourceTop, rect,
                                deco.ImageSliceTop, deco.ImageSliceRight, deco.ImageSliceBottom, deco.ImageSliceLeft,
                                out NineSliceBaker.PatchSet patches))
                        {
                            if (resource.RasterImage != null)
                            {
                                NineSliceBaker.DrawImage(spriteCanvas, resource.RasterImage, in patches);
                            }
                            else
                            {
                                NineSliceBaker.DrawPicture(spriteCanvas, resource.SvgPicture!, in patches);
                            }
                        }
                    }
                    else if (resource.RasterImage != null)
                    {
                        spriteCanvas.DrawImage(resource.RasterImage, rect);
                    }
                    else if (resource.SvgPicture != null)
                    {
                        SKRect cull = resource.SvgPicture.CullRect;
                        float scaleX = rect.Width / cull.Width;
                        float scaleY = rect.Height / cull.Height;
                        spriteCanvas.Save();
                        spriteCanvas.Translate(rect.Left, rect.Top);
                        spriteCanvas.Scale(scaleX, scaleY);
                        spriteCanvas.DrawPicture(resource.SvgPicture, 0f, 0f);
                        spriteCanvas.Restore();
                    }

                    spriteCanvas.Restore();
                }

                if (deco.BorderWidth > 0.01f)
                {
                    _strokePaint.StrokeWidth = deco.BorderWidth;
                    _strokePaint.Color = deco.BorderColor.W > 0.001f ? ToSkColor(deco.BorderColor) : SKColors.Black;
                    using SKPath borderPath = BuildItemPath(deco.ClipShape, rect, radius);
                    spriteCanvas.DrawPath(borderPath, _strokePaint);
                    _strokePaint.StrokeWidth = 1f;
                }

                image = surface.Snapshot();
                _barSpriteCache[key] = image;
                return new CachedBarSprite(image, shadowMargin, shadowMargin);
            }

            // 背景:双色渐变或纯色;渐变跨度取内框整宽。
            SKShader? backgroundShader = null;
            if (deco.BackgroundGradientTo.W > 0.001f)
            {
                backgroundShader = SKShader.CreateLinearGradient(
                    new SKPoint(inner.Left, inner.Top),
                    new SKPoint(inner.Right, inner.Top),
                    new[] { ToSkColor(item.Color0), ToSkColor(deco.BackgroundGradientTo) },
                    null,
                    SKShaderTileMode.Clamp);
                _fillPaint.Shader = backgroundShader;
            }
            else
            {
                _fillPaint.Color = ToSkColor(item.Color0);
            }

            DrawRoundedOrPlain(spriteCanvas, in inner, radius, _fillPaint);
            _fillPaint.Shader = null;
            backgroundShader?.Dispose();

            // 前景填充:按值裁剪内框,渐变沿整宽保证跨条目视觉连续。
            float clamped = Math.Clamp(item.Value0, 0f, 1f);
            if (clamped > 0f && inner.Width > 0.5f)
            {
                SKRect fillClip = new(inner.Left, inner.Top, inner.Left + inner.Width * clamped, inner.Bottom);
                SKShader? fillShader = null;
                if (deco.FillGradientTo.W > 0.001f)
                {
                    fillShader = SKShader.CreateLinearGradient(
                        new SKPoint(inner.Left, inner.Top),
                        new SKPoint(inner.Right, inner.Top),
                        new[] { ToSkColor(item.Color1), ToSkColor(deco.FillGradientTo) },
                        null,
                        SKShaderTileMode.Clamp);
                    _fillPaint.Shader = fillShader;
                }
                else
                {
                    _fillPaint.Color = ToSkColor(item.Color1);
                }

                int save = spriteCanvas.SaveLayer();
                spriteCanvas.ClipRect(fillClip);
                DrawRoundedOrPlain(spriteCanvas, in inner, radius, _fillPaint);
                spriteCanvas.RestoreToCount(save);
                _fillPaint.Shader = null;
                fillShader?.Dispose();
            }

            // 边框:零宽时保留旧默认(1px 黑)以维持未声明样式的逐像素一致。
            if (deco.BorderWidth > 0.01f)
            {
                _strokePaint.StrokeWidth = deco.BorderWidth;
                _strokePaint.Color = deco.BorderColor.W > 0.001f ? ToSkColor(deco.BorderColor) : SKColors.Black;
            }
            else
            {
                _strokePaint.StrokeWidth = 1f;
                _strokePaint.Color = SKColors.Black;
            }

            DrawRoundedOrPlainStroke(spriteCanvas, in rect, radius, _strokePaint);
            _strokePaint.StrokeWidth = 1f;

            image = surface.Snapshot();
            _barSpriteCache[key] = image;
            return new CachedBarSprite(image, shadowMargin, shadowMargin);
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
                ToColorKey(ToSkColor(item.Color1)),
                item.Decoration,
                item.Text);
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

        private SKFont GetFont(SKTypeface typeface, int fontSize, byte styleFlags = 0)
        {
            string familyName = typeface.FamilyName ?? string.Empty;
            var key = new FontCacheKey(familyName, fontSize, styleFlags);
            if (_fontCache.TryGetValue(key, out SKFont? font))
            {
                return font;
            }

            font = new SKFont(typeface, fontSize)
            {
                Embolden = (styleFlags & 0x01) != 0,
                SkewX = (styleFlags & 0x02) != 0 ? -0.25f : 0f,
            };
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

        private static bool TrySaveClipShape(SKCanvas canvas, in PresentationClipShape clipShape, out int saveCount)
        {
            saveCount = -1;
            if (!clipShape.IsActive)
            {
                return false;
            }

            saveCount = canvas.Save();
            using SKPath path = CreateClipPath(in clipShape);
            canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);
            return true;
        }

        private static SKPath CreateClipPath(in PresentationClipShape clipShape)
        {
            var path = new SKPath();
            SKRect rect = new(
                clipShape.X,
                clipShape.Y,
                clipShape.X + clipShape.Width,
                clipShape.Y + clipShape.Height);
            switch (clipShape.Kind)
            {
                case PresentationClipShapeKind.Circle:
                {
                    float radius = MathF.Max(0f, MathF.Min(rect.Width, rect.Height) * 0.5f);
                    path.AddCircle((rect.Left + rect.Right) * 0.5f, (rect.Top + rect.Bottom) * 0.5f, radius);
                    return path;
                }

                case PresentationClipShapeKind.Diamond:
                {
                    float midX = (rect.Left + rect.Right) * 0.5f;
                    float midY = (rect.Top + rect.Bottom) * 0.5f;
                    path.MoveTo(midX, rect.Top);
                    path.LineTo(rect.Right, midY);
                    path.LineTo(midX, rect.Bottom);
                    path.LineTo(rect.Left, midY);
                    path.Close();
                    return path;
                }

                default:
                    path.AddRect(rect);
                    return path;
            }
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
            return ((int)layer * KindCount) + ((int)kind - 1);
        }

        private readonly record struct FontCacheKey(string FamilyName, int FontSize, byte StyleFlags);

        private readonly record struct BarSpriteCacheKey(
            int WidthPx,
            int HeightPx,
            int FillPx,
            uint BackgroundColor,
            uint ForegroundColor,
            ScreenHudDecoration Decoration,
            string? ImageSource);

        private readonly record struct TextLayoutCacheKey(string Text, int FontSize, byte StyleFlags);

        private readonly record struct TextBatchKey(string Text, int FontSize, uint ColorKey, byte StyleFlags);

        private readonly record struct TextSpriteCacheKey(string Text, int FontSize, uint ColorKey, ScreenHudDecoration Decoration);

        private readonly record struct CachedTextRun(
            SKTextBlob? Blob,
            float XOffset,
            SKFont Font,
            ushort[] Glyphs,
            SKPoint[] GlyphPositions);

        private readonly record struct CachedBarSprite(SKImage Image, float OffsetX = 0f, float OffsetY = 0f);

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
            public readonly List<int> PendingGlyphIndices = new();
            public float[] BucketBaselines = Array.Empty<float>();
            public int[] OrderStableIds = Array.Empty<int>();
            public RetainedTextSpriteEntry[] OrderEntries = Array.Empty<RetainedTextSpriteEntry>();
            public int[] OrderFontSizes = Array.Empty<int>();
            public int[] OrderCandidateSerials = Array.Empty<int>();
            public byte[] OrderStableStreaks = Array.Empty<byte>();
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
                PendingGlyphIndices.Clear();
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
                    OrderFontSizes.Length >= required &&
                    OrderCandidateSerials.Length >= required &&
                    OrderStableStreaks.Length >= required)
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
                Array.Resize(ref OrderCandidateSerials, next);
                Array.Resize(ref OrderStableStreaks, next);
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

            public RetainedBarBatchBucket(SKImage image, float offsetX = 0f, float offsetY = 0f)
            {
                Image = image;
                OffsetX = offsetX;
                OffsetY = offsetY;
            }

            public SKImage Image { get; }

            public float OffsetX { get; }

            public float OffsetY { get; }

            public int Count { get; private set; }

            public float[] X => _x;

            public float[] Y => _y;

            public void ResetVisible()
            {
                Count = 0;
            }

            public void AddVisible(float x, float y)
            {
                EnsureCapacity(Count + 1);
                int index = Count++;
                _stableIds[index] = 0;
                _x[index] = x;
                _y[index] = y;
            }

            public int Add(int stableId, float x, float y)
            {
                EnsureCapacity(Count + 1);
                int index = Count++;
                _stableIds[index] = stableId;
                _x[index] = x;
                _y[index] = y;
                return index;
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
            }
        }

        private sealed class RetainedTextSpriteBatchBucket
        {
            private int[] _stableIds = Array.Empty<int>();
            private float[] _x = Array.Empty<float>();
            private float[] _y = Array.Empty<float>();

            public RetainedTextSpriteBatchBucket(CachedTextSprite sprite)
            {
                Sprite = sprite;
            }

            public CachedTextSprite Sprite { get; }

            public int Count { get; private set; }

            public float[] X => _x;

            public float[] Y => _y;

            public void ResetVisible()
            {
                Count = 0;
            }

            public void AddVisible(float x, float y)
            {
                EnsureCapacity(Count + 1);
                int index = Count++;
                _stableIds[index] = 0;
                _x[index] = x;
                _y[index] = y;
            }

            public int Add(int stableId, float x, float y)
            {
                EnsureCapacity(Count + 1);
                int index = Count++;
                _stableIds[index] = stableId;
                _x[index] = x;
                _y[index] = y;
                return index;
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
            }
        }

        private sealed class TextChurnSampler
        {
            private readonly int[] _sampleStableIds = new int[TextChurnSampleCount];
            private readonly int[] _sampleIdentities = new int[TextChurnSampleCount];
            private int _populatedCount;

            public bool IsChurned(ReadOnlySpan<PresentationOverlayItem> span)
            {
                int sampleCount = Math.Min(TextChurnSampleCount, span.Length);
                if (sampleCount <= 0)
                {
                    return false;
                }

                int stride = Math.Max(1, span.Length / sampleCount);
                int changedCount = 0;
                for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                {
                    ref readonly PresentationOverlayItem item = ref span[sampleIndex * stride];
                    int identity = TextStabilityIdentity(in item);
                    int slot = FindSlot(item.StableId);
                    if (slot < 0)
                    {
                        slot = _populatedCount < TextChurnSampleCount ? _populatedCount++ : sampleIndex;
                        _sampleStableIds[slot] = item.StableId;
                        _sampleIdentities[slot] = identity;
                        changedCount++;
                        continue;
                    }

                    if (_sampleIdentities[slot] != identity)
                    {
                        _sampleIdentities[slot] = identity;
                        changedCount++;
                    }
                }

                return changedCount * 2 >= sampleCount;
            }

            private int FindSlot(int stableId)
            {
                int populated = Math.Min(_populatedCount, TextChurnSampleCount);
                for (int i = 0; i < populated; i++)
                {
                    if (_sampleStableIds[i] == stableId)
                    {
                        return i;
                    }
                }

                return -1;
            }
        }

        private sealed class CachedTextSprite : IDisposable
        {
            public CachedTextSprite(SKImage image, float baselineY, float textWidth, float offsetX = 0f, float offsetY = 0f)
            {
                Image = image;
                BaselineY = baselineY;
                TextWidth = textWidth;
                OffsetX = offsetX;
                OffsetY = offsetY;
            }

            public SKImage Image { get; }

            public float BaselineY { get; }

            /// <summary>纯字形测量宽(px),text-align:center 时用于锚点回中。</summary>
            public float TextWidth { get; }

            /// <summary>阴影/底板烘焙进精灵后的左上外扩(px),绘制侧按此回贴锚点。</summary>
            public float OffsetX { get; }

            public float OffsetY { get; }

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

            public float OffsetX { get; private set; }

            public float OffsetY { get; private set; }

            public void Reset(SKImage image, float width, float height, float offsetX = 0f, float offsetY = 0f)
            {
                Image = image;
                OffsetX = offsetX;
                OffsetY = offsetY;
                _spriteRect = new SKRect(0f, 0f, Image.Width, Image.Height);
                Count = 0;
            }

            public void Add(float x, float y)
            {
                EnsureCapacity(Count + 1);
                _x[Count] = x - OffsetX;
                _y[Count] = y - OffsetY;
                _sprites[Count] = _spriteRect;
                _transforms[Count] = SKRotationScaleMatrix.CreateTranslation(x - OffsetX, y - OffsetY);
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
                _transforms[Count] = SKRotationScaleMatrix.CreateTranslation(x - TextSpriteBakePaddingX, y);
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
                    _transforms[i] = SKRotationScaleMatrix.CreateTranslation(_x[i] - TextSpriteBakePaddingX, _y[i]);
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

    }
}
