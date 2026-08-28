using System;
using Ludots.Adapter.Raylib.Services;
using NUnit.Framework;
using SkiaSharp;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibFramebufferCaptureTests
{
    [Test]
    public void FillBitmapRgba_CopiesRowsVerbatim()
    {
        // 2x2 RGBA: distinct colors per pixel prove both row order and channel order survive.
        byte[] source =
        {
            10, 20, 30, 255,     40, 50, 60, 255,
            70, 80, 90, 255,   100, 110, 120, 255,
        };
        using var bitmap = new SKBitmap(2, 2, SKColorType.Rgba8888, SKAlphaType.Opaque);

        RaylibFramebufferCapture.FillBitmapRgba(bitmap, source);

        Assert.That(((SKColor)bitmap.GetPixel(0, 0)).Red, Is.EqualTo(10));
        Assert.That(((SKColor)bitmap.GetPixel(0, 0)).Green, Is.EqualTo(20));
        Assert.That(((SKColor)bitmap.GetPixel(0, 0)).Blue, Is.EqualTo(30));
        Assert.That(((SKColor)bitmap.GetPixel(1, 0)).Red, Is.EqualTo(40));
        Assert.That(((SKColor)bitmap.GetPixel(0, 1)).Red, Is.EqualTo(70));
        Assert.That(((SKColor)bitmap.GetPixel(1, 1)).Blue, Is.EqualTo(120));
    }

    [Test]
    public void FillBitmapRgba_RejectsUndersizedSource()
    {
        using var bitmap = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Opaque);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => RaylibFramebufferCapture.FillBitmapRgba(bitmap, new byte[4 * 4 * 4 - 1]));
    }
}
