using System.Numerics;

namespace Ludots.Client.WebGpu.Runtime;

/// <summary>
/// Shared UV contract for screen and world glyph shaders.
/// <see cref="WebGpuGlyphPlacement.UvRect"/> / instance <c>UvRect</c> store atlas bounds as min.xy / max.zw
/// (not origin+size). Sampling must lerp from min to max with corner t in [0,1].
/// </summary>
public static class WebGpuFontUvSampling
{
    /// <summary>
    /// Samples a min/max UV rect using a local quad corner in [-1, 1] (same basis as the WGSL glyph meshes).
    /// </summary>
    public static Vector2 SampleMinMax(Vector4 uvRectMinMax, Vector2 localNegOneToOne)
    {
        float tx = (localNegOneToOne.X * 0.5f) + 0.5f;
        float ty = (localNegOneToOne.Y * 0.5f) + 0.5f;
        return new Vector2(
            float.Lerp(uvRectMinMax.X, uvRectMinMax.Z, tx),
            float.Lerp(uvRectMinMax.Y, uvRectMinMax.W, ty));
    }

    /// <summary>
    /// Incorrect origin+size sampling kept only so tests can prove min/max is required.
    /// </summary>
    public static Vector2 SampleOriginSizeIncorrect(Vector4 uvRectMinMaxMisreadAsOriginSize, Vector2 localNegOneToOne)
    {
        Vector2 origin = new(uvRectMinMaxMisreadAsOriginSize.X, uvRectMinMaxMisreadAsOriginSize.Y);
        Vector2 size = new(uvRectMinMaxMisreadAsOriginSize.Z, uvRectMinMaxMisreadAsOriginSize.W);
        Vector2 t = (localNegOneToOne + Vector2.One) * 0.5f;
        return origin + (t * size);
    }
}
