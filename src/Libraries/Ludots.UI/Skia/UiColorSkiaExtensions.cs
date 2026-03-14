using System.Numerics;
using System.Runtime.CompilerServices;
using Ludots.UI.Runtime;
using SkiaSharp;

namespace Ludots.UI.Skia;

public static class UiSkiaExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SKColor ToSKColor(this UiColor c) => new SKColor(c.R, c.G, c.B, c.A);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UiColor ToUiColor(this SKColor c) => new UiColor(c.Red, c.Green, c.Blue, c.Alpha);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SKMatrix ToSKMatrix(this Matrix3x2 m) =>
        new SKMatrix(m.M11, m.M21, m.M31, m.M12, m.M22, m.M32, 0, 0, 1);
}
