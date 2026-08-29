using System;
using System.Buffers;
using Ludots.UI.Browser;
using Ludots.UI.Runtime;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Raylib.Render;

namespace Ludots.Adapter.Raylib
{
    internal sealed class RaylibBrowserLayerRenderer : IDisposable
    {
        private readonly Dictionary<BrowserSurfaceId, BrowserLayerState> _states = new();
        private readonly HashSet<BrowserSurfaceId> _seenThisFrame = new();

        public void Render(UiScene? scene, int width, int height)
        {
            _seenThisFrame.Clear();
            if (scene?.Root == null || width <= 0 || height <= 0)
            {
                ReleaseStaleStates();
                return;
            }

            scene.Layout(width, height);

            foreach (UiNode node in scene.EnumerateVisualNodes())
            {
                if (!IsRenderableBrowserNode(node, out IUiBrowserCanvasContent? content) || content == null)
                {
                    continue;
                }

                UiRect rect = content.GetContentRect(node);
                if (rect.Width <= 0.01f || rect.Height <= 0.01f)
                {
                    continue;
                }

                content.EnsureSurfaceViewport(rect.Width, rect.Height);

                BrowserSurfaceId id = content.Surface.Id;
                _seenThisFrame.Add(id);
                if (!_states.TryGetValue(id, out BrowserLayerState? state))
                {
                    state = new BrowserLayerState();
                    _states.Add(id, state);
                }

                if (content.TryReadLatestFrame(state, static (in BrowserFrameAccess frame, BrowserLayerState layerState) =>
                    {
                        layerState.Update(frame);
                    }))
                {
                    state.Draw(rect);
                }
            }

            ReleaseStaleStates();
        }

        public void Dispose()
        {
            foreach (BrowserLayerState state in _states.Values)
            {
                state.Dispose();
            }

            _states.Clear();
            _seenThisFrame.Clear();
        }

        private static bool IsRenderableBrowserNode(UiNode node, out IUiBrowserCanvasContent? content)
        {
            content = node.CanvasContent as IUiBrowserCanvasContent;
            if (content == null)
            {
                return false;
            }

            UiStyle style = node.RenderStyle;
            return style.Visible && style.Display != UiDisplay.None;
        }

        private void ReleaseStaleStates()
        {
            if (_states.Count == 0)
            {
                return;
            }

            List<BrowserSurfaceId>? staleIds = null;
            foreach (BrowserSurfaceId id in _states.Keys)
            {
                if (!_seenThisFrame.Contains(id))
                {
                    staleIds ??= new List<BrowserSurfaceId>();
                    staleIds.Add(id);
                }
            }

            if (staleIds == null)
            {
                return;
            }

            foreach (BrowserSurfaceId id in staleIds)
            {
                if (_states.Remove(id, out BrowserLayerState? state))
                {
                    state.Dispose();
                }
            }
        }

        private sealed class BrowserLayerState : IDisposable
        {
            private Texture2D _texture;
            private byte[]? _rgbaScratch;
            private int _width;
            private int _height;
            private long _uploadedSequence = -1;

            public void Update(in BrowserFrameAccess frame)
            {
                EnsureTexture(frame.Viewport.Width, frame.Viewport.Height);
                if (frame.Sequence == _uploadedSequence)
                {
                    return;
                }

                IReadOnlyList<BrowserDirtyRect> dirtyRects = frame.DirtyRects.Count > 0
                    ? frame.DirtyRects
                    : new[] { new BrowserDirtyRect(0, 0, frame.Viewport.Width, frame.Viewport.Height) };

                foreach (BrowserDirtyRect rect in dirtyRects)
                {
                    UploadRect(frame, rect);
                }

                _uploadedSequence = frame.Sequence;
            }

            public void Draw(UiRect rect)
            {
                if (_texture.id == 0)
                {
                    return;
                }

                var source = new Rectangle(0f, 0f, _width, _height);
                var dest = new Rectangle(rect.X, rect.Y, rect.Width, rect.Height);
                Rl.BeginBlendMode(BlendMode.BLEND_ALPHA_PREMULTIPLY);
                Rl.DrawTexturePro(_texture, source, dest, new System.Numerics.Vector2(0f, 0f), 0f, Color.WHITE);
                Rl.EndBlendMode();
            }

            public void Dispose()
            {
                if (_texture.id != 0)
                {
                    RaylibNativeResources.UnloadTexture(_texture);
                    _texture = default;
                }

                if (_rgbaScratch != null)
                {
                    ArrayPool<byte>.Shared.Return(_rgbaScratch);
                    _rgbaScratch = null;
                }
            }

            private void EnsureTexture(int width, int height)
            {
                width = Math.Max(1, width);
                height = Math.Max(1, height);
                if (_texture.id != 0 && _width == width && _height == height)
                {
                    return;
                }

                if (_texture.id != 0)
                {
                    RaylibNativeResources.UnloadTexture(_texture);
                    _texture = default;
                }

                Image img = Rl.GenImageColor(width, height, Color.BLANK);
                _texture = RaylibNativeResources.LoadTextureFromImage(img);
                Rl.UnloadImage(img);
                _width = width;
                _height = height;
                _uploadedSequence = -1;
            }

            private void UploadRect(in BrowserFrameAccess frame, BrowserDirtyRect rect)
            {
                int byteCount = checked(rect.Width * rect.Height * BrowserFrameBuffer.BytesPerPixel);
                EnsureScratch(byteCount);
                Span<byte> target = _rgbaScratch!.AsSpan(0, byteCount);

                switch (frame.PixelFormat)
                {
                    case BrowserPixelFormat.Rgba8888Premultiplied:
                        CopyRect(frame, rect, target);
                        break;
                    case BrowserPixelFormat.Bgra8888Premultiplied:
                        CopyBgraRectAsRgba(frame, rect, target);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(frame), frame.PixelFormat, "Unsupported browser frame pixel format.");
                }

                unsafe
                {
                    fixed (byte* ptr = target)
                    {
                        Rl.UpdateTextureRec(
                            _texture,
                            new Rectangle(rect.X, rect.Y, rect.Width, rect.Height),
                            ptr);
                    }
                }
            }

            private void EnsureScratch(int byteCount)
            {
                if (_rgbaScratch != null && _rgbaScratch.Length >= byteCount)
                {
                    return;
                }

                if (_rgbaScratch != null)
                {
                    ArrayPool<byte>.Shared.Return(_rgbaScratch);
                }

                _rgbaScratch = ArrayPool<byte>.Shared.Rent(byteCount);
            }

            private static void CopyRect(in BrowserFrameAccess frame, BrowserDirtyRect rect, Span<byte> target)
            {
                ReadOnlySpan<byte> pixels = frame.Pixels.Span;
                int targetOffset = 0;
                int rowLength = checked(rect.Width * BrowserFrameBuffer.BytesPerPixel);
                for (int row = 0; row < rect.Height; row++)
                {
                    int sourceOffset = checked(((rect.Y + row) * frame.RowBytes) + (rect.X * BrowserFrameBuffer.BytesPerPixel));
                    pixels.Slice(sourceOffset, rowLength).CopyTo(target.Slice(targetOffset, rowLength));
                    targetOffset += rowLength;
                }
            }

            private static void CopyBgraRectAsRgba(in BrowserFrameAccess frame, BrowserDirtyRect rect, Span<byte> target)
            {
                ReadOnlySpan<byte> pixels = frame.Pixels.Span;
                int targetOffset = 0;
                for (int row = 0; row < rect.Height; row++)
                {
                    int sourceOffset = checked(((rect.Y + row) * frame.RowBytes) + (rect.X * BrowserFrameBuffer.BytesPerPixel));
                    for (int x = 0; x < rect.Width; x++)
                    {
                        int source = sourceOffset + (x * BrowserFrameBuffer.BytesPerPixel);
                        target[targetOffset] = pixels[source + 2];
                        target[targetOffset + 1] = pixels[source + 1];
                        target[targetOffset + 2] = pixels[source];
                        target[targetOffset + 3] = pixels[source + 3];
                        targetOffset += BrowserFrameBuffer.BytesPerPixel;
                    }
                }
            }
        }
    }
}
