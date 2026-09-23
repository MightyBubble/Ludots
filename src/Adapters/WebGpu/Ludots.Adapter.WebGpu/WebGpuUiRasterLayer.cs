using System;
using Ludots.Client.WebGpu.Rendering;
using Ludots.Core.Presentation.Hud;
using Ludots.Presentation.Skia;
using Ludots.UI;
using Ludots.UI.Skia;
using SkiaSharp;

namespace Ludots.Adapter.WebGpu
{
    internal sealed unsafe class WebGpuUiRasterLayer : IDisposable
    {
        private readonly SkiaUiRenderer _renderer;
        private readonly SkiaOverlayRenderer _overlayRenderer = new();
        private readonly PresentationOverlayLanePacer _underlayPacer = new(PresentationOverlayLayer.UnderUi);
        private SKBitmap? _bitmap;
        private SKCanvas? _canvas;
        private byte[] _uploadBuffer = Array.Empty<byte>();
        private int _width;
        private int _height;
        private int _bytesPerRow;

        public WebGpuUiRasterLayer(SkiaUiRenderer renderer)
        {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        }

        public int Width => _width;
        public int Height => _height;
        public int BytesPerRow => _bytesPerRow;
        public ReadOnlySpan<byte> UploadBytes => _uploadBuffer.AsSpan(0, WebGpuUiRenderer.CalculateUploadByteCount(_width, _height));

        public bool TryRender(UIRoot uiRoot, int width, int height)
        {
            return TryRender(null, uiRoot, width, height, out _);
        }

        public bool TryRender(
            PresentationOverlayScene? overlayScene,
            UIRoot uiRoot,
            int width,
            int height,
            out WebGpuUiRasterStats stats)
        {
            if (uiRoot == null)
            {
                throw new ArgumentNullException(nameof(uiRoot));
            }

            bool hasUnderUi = overlayScene?.ContainsLayer(PresentationOverlayLayer.UnderUi) == true;
            bool hasTopMost = overlayScene?.ContainsLayer(PresentationOverlayLayer.TopMost) == true;
            bool hasRetainedUi = uiRoot.Scene != null;
            int minimapMarkers = overlayScene?.TopMostMinimapMarkers?.Count ?? 0;
            int overlayItems = overlayScene?.Count ?? 0;
            int dropped = overlayScene?.DroppedSinceClear ?? 0;

            stats = new WebGpuUiRasterStats(
                HasContent: hasUnderUi || hasTopMost || hasRetainedUi,
                OverlayItemCount: overlayItems,
                UnderUiItemCount: CountLayerItems(overlayScene, PresentationOverlayLayer.UnderUi),
                TopMostItemCount: CountLayerItems(overlayScene, PresentationOverlayLayer.TopMost),
                MinimapMarkerCount: minimapMarkers,
                Dropped: dropped,
                Width: Math.Max(1, width),
                Height: Math.Max(1, height),
                BytesPerRow: WebGpuUiRenderer.AlignBytesPerRow(Math.Max(1, width)));

            if (!stats.HasContent)
            {
                return false;
            }

            Resize(width, height);
            if (_bitmap == null || _canvas == null)
            {
                throw new InvalidOperationException("WebGPU UI raster layer was not initialized before rendering.");
            }

            ClearBitmap();
            _overlayRenderer.ResetFrameStats();
            if (hasUnderUi)
            {
                PresentationOverlayLanePacer.LaneRefreshPlan underlayPlan = _underlayPacer.BuildPlan(overlayScene!);
                _overlayRenderer.Render(overlayScene!, _canvas, PresentationOverlayLayer.UnderUi, underlayPlan);
                _underlayPacer.MarkPresented(overlayScene!, underlayPlan);
            }
            else
            {
                _underlayPacer.Reset();
            }

            _renderer.SetCanvas(_canvas);
            if (hasRetainedUi)
            {
                uiRoot.Render();
            }

            if (hasTopMost)
            {
                _overlayRenderer.Render(overlayScene!, _canvas, PresentationOverlayLayer.TopMost);
            }

            _canvas.Flush();
            CopyBitmapToUploadBuffer();
            return true;
        }

        public void Resize(int width, int height)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            int bytesPerRow = WebGpuUiRenderer.AlignBytesPerRow(width);
            int byteCount = WebGpuUiRenderer.CalculateUploadByteCount(width, height);

            if (_bitmap != null &&
                _canvas != null &&
                _width == width &&
                _height == height &&
                _bytesPerRow == bytesPerRow &&
                _uploadBuffer.Length == byteCount)
            {
                return;
            }

            _canvas?.Dispose();
            _canvas = null;
            _bitmap?.Dispose();
            _bitmap = null;

            _width = width;
            _height = height;
            _bytesPerRow = bytesPerRow;
            _uploadBuffer = new byte[byteCount];

            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            _bitmap = new SKBitmap(info);
            _canvas = new SKCanvas(_bitmap);
            ClearBitmap();
        }

        public void Dispose()
        {
            _overlayRenderer.Dispose();
            _canvas?.Dispose();
            _canvas = null;
            _bitmap?.Dispose();
            _bitmap = null;
            _uploadBuffer = Array.Empty<byte>();
            _width = 0;
            _height = 0;
            _bytesPerRow = 0;
        }

        private void ClearBitmap()
        {
            if (_bitmap == null)
            {
                return;
            }

            IntPtr ptr = _bitmap.GetPixels();
            if (ptr == IntPtr.Zero)
            {
                _canvas?.Clear(SKColors.Transparent);
                return;
            }

            new Span<byte>((void*)ptr, _bitmap.ByteCount).Clear();
            _canvas?.ResetMatrix();
        }

        private void CopyBitmapToUploadBuffer()
        {
            if (_bitmap == null)
            {
                throw new InvalidOperationException("WebGPU UI raster bitmap is missing during upload copy.");
            }

            IntPtr ptr = _bitmap.GetPixels();
            if (ptr == IntPtr.Zero)
            {
                throw new InvalidOperationException("WebGPU UI raster bitmap returned a null pixel pointer.");
            }

            int sourceBytesPerRow = _bitmap.RowBytes;
            int visibleBytesPerRow = checked(_width * WebGpuUiRenderer.BytesPerPixel);
            if (sourceBytesPerRow < visibleBytesPerRow)
            {
                throw new InvalidOperationException(
                    $"WebGPU UI raster source row stride {sourceBytesPerRow} is smaller than visible row {visibleBytesPerRow}.");
            }

            var source = new ReadOnlySpan<byte>((void*)ptr, _bitmap.ByteCount);
            for (int row = 0; row < _height; row++)
            {
                ReadOnlySpan<byte> sourceRow = source.Slice(row * sourceBytesPerRow, visibleBytesPerRow);
                Span<byte> targetRow = _uploadBuffer.AsSpan(row * _bytesPerRow, _bytesPerRow);
                sourceRow.CopyTo(targetRow);
                if (_bytesPerRow > visibleBytesPerRow)
                {
                    targetRow.Slice(visibleBytesPerRow).Clear();
                }
            }
        }

        private static int CountLayerItems(PresentationOverlayScene? scene, PresentationOverlayLayer layer)
        {
            if (scene == null)
            {
                return 0;
            }

            int count = 0;
            for (int kindValue = (int)PresentationOverlayItemKind.Text;
                 kindValue <= (int)PresentationOverlayItemKind.Line;
                 kindValue++)
            {
                count += scene.GetLaneSpan(layer, (PresentationOverlayItemKind)kindValue).Length;
            }

            if (layer == PresentationOverlayLayer.TopMost)
            {
                count += scene.TopMostMinimapMarkers?.Count ?? 0;
            }

            return count;
        }
    }

    internal readonly record struct WebGpuUiRasterStats(
        bool HasContent,
        int OverlayItemCount,
        int UnderUiItemCount,
        int TopMostItemCount,
        int MinimapMarkerCount,
        int Dropped,
        int Width,
        int Height,
        int BytesPerRow);
}
