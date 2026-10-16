using System;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.Mathematics;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Adapter.Raylib;

/// <summary>
/// 地形类型格图的纹理由宿主持有。高度图渲染器只绑定，不卸载。
/// 关掉这一层时只解绑自己占住的外部槽，URI 自有贴图仍由 ClearNavWalkabilityOverlay 管。
/// </summary>
internal sealed class RaylibTerrainTypeOverlayBinding
{
    private CrowdTerrainTypeOverlay? _source;
    private Texture2D _texture;
    private byte[]? _scratch;
    private bool _bound;

    public bool Bound => _bound;

    public void Apply(RaylibContinuousHeightmapRenderer renderer, CrowdTerrainTypeOverlay? overlay)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (overlay == null || !overlay.Enabled)
        {
            ReleaseSlot(renderer);
            if (overlay == null) UnloadTexture();
            _source = overlay;
            return;
        }

        if (!ReferenceEquals(_source, overlay) || _texture.id == 0 || _texture.width != overlay.Width || _texture.height != overlay.Height)
        {
            UnloadTexture();
            _source = overlay;
            EnsureTexture(overlay);
        }

        overlay.Sync();
        Upload(overlay);
        renderer.NavWalkabilityOverlayVisibleInOverview = true;
        renderer.SetNavWalkabilityOverlayExternal(_texture, overlay.BoundsCm, CrowdTerrainTypeOverlay.BlendStrength);
        _bound = true;
        overlay.ConsumeDirty();
    }

    private void ReleaseSlot(RaylibContinuousHeightmapRenderer renderer)
    {
        if (!_bound) return;
        renderer.ClearNavWalkabilityOverlayExternal();
        renderer.NavWalkabilityOverlayVisibleInOverview = false;
        _bound = false;
    }

    private void EnsureTexture(CrowdTerrainTypeOverlay overlay)
    {
        Image image = Rl.GenImageColor(overlay.Width, overlay.Height, Color.BLANK);
        _texture = RaylibNativeResources.LoadTextureFromImage(image);
        Rl.UnloadImage(image);
        Rl.SetTextureFilter(_texture, Rl.TextureFilter.TEXTURE_FILTER_POINT);
        Rl.SetTextureWrap(_texture, Rl.TextureWrap.TEXTURE_WRAP_CLAMP);
        int bytes = overlay.Width * overlay.Height * 4;
        if (_scratch == null || _scratch.Length < bytes) _scratch = new byte[bytes];
    }

    private void Upload(CrowdTerrainTypeOverlay overlay)
    {
        int count = overlay.DirtyCount;
        if (count == 0) return;
        ReadOnlySpan<IntRect> rects = overlay.DirtyRects;
        for (int i = 0; i < count; i++)
        {
            IntRect rect = rects[i];
            if (rect.Width <= 0 || rect.Height <= 0) continue;
            if (rect.X == 0 && rect.Y == 0 && rect.Width == overlay.Width && rect.Height == overlay.Height)
            {
                unsafe
                {
                    fixed (byte* ptr = overlay.Rgba)
                    {
                        Rl.UpdateTexture(_texture, ptr);
                    }
                }

                continue;
            }

            byte[] scratch = _scratch ?? throw new InvalidOperationException("地形类型叠层纹理未建。");
            int rowBytes = rect.Width * 4;
            for (int y = 0; y < rect.Height; y++)
            {
                int src = ((rect.Y + y) * overlay.Width + rect.X) * 4;
                Buffer.BlockCopy(overlay.Rgba, src, scratch, y * rowBytes, rowBytes);
            }

            unsafe
            {
                fixed (byte* ptr = scratch)
                {
                    Rl.UpdateTextureRec(_texture, new Rectangle(rect.X, rect.Y, rect.Width, rect.Height), ptr);
                }
            }
        }
    }

    private void UnloadTexture()
    {
        if (_texture.id == 0) return;
        RaylibNativeResources.UnloadTexture(_texture);
        _texture = default;
    }
}
