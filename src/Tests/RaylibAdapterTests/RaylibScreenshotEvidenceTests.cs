using Ludots.Adapter.Raylib;
using NUnit.Framework;
using SkiaSharp;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibScreenshotEvidenceTests
{
    [Test]
    public void ValidateRuntimeScreenshotEvidence_AcceptsPngWithExpectedDimensions()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"raylib-screenshot-{Guid.NewGuid():N}.png");
        try
        {
            WritePng(path, width: 1280, height: 720, flat: false);

            RaylibHostLoop.ValidateRuntimeScreenshotEvidence(path, expectedWidth: 1280, expectedHeight: 720);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public void ValidateRuntimeScreenshotEvidence_RejectsMissingFile()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"missing-raylib-screenshot-{Guid.NewGuid():N}.png");

        Assert.That(
            () => RaylibHostLoop.ValidateRuntimeScreenshotEvidence(path, expectedWidth: 1280, expectedHeight: 720),
            Throws.InvalidOperationException.With.Message.Contains("was not written"));
    }

    [Test]
    public void ValidateRuntimeScreenshotEvidence_RejectsDimensionMismatch()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"raylib-screenshot-mismatch-{Guid.NewGuid():N}.png");
        try
        {
            WritePng(path, width: 640, height: 360, flat: false);

            Assert.That(
                () => RaylibHostLoop.ValidateRuntimeScreenshotEvidence(path, expectedWidth: 1280, expectedHeight: 720),
                Throws.InvalidOperationException.With.Message.Contains("dimensions mismatch"));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public void ValidateRuntimeScreenshotEvidence_RejectsUndecodableHeaderOnlyPng()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"raylib-screenshot-corrupt-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, new byte[]
            {
                137, 80, 78, 71, 13, 10, 26, 10,
                0, 0, 0, 13,
                (byte)'I', (byte)'H', (byte)'D', (byte)'R',
                0, 0, 5, 0,
                0, 0, 2, 208,
            });

            Assert.That(
                () => RaylibHostLoop.ValidateRuntimeScreenshotEvidence(path, expectedWidth: 1280, expectedHeight: 720),
                Throws.InvalidOperationException.With.Message.Contains("decodable PNG"));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public void ValidateRuntimeScreenshotEvidence_RejectsVisuallyFlatPng()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"raylib-screenshot-flat-{Guid.NewGuid():N}.png");
        try
        {
            WritePng(path, width: 1280, height: 720, flat: true);

            Assert.That(
                () => RaylibHostLoop.ValidateRuntimeScreenshotEvidence(path, expectedWidth: 1280, expectedHeight: 720),
                Throws.InvalidOperationException.With.Message.Contains("visually flat"));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void WritePng(string path, int width, int height, bool flat)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(20, 30, 40, 255));
        if (!flat)
        {
            for (int y = 0; y < height; y++)
            {
                byte shade = (byte)(40 + (y * 140 / Math.Max(1, height - 1)));
                for (int x = 0; x < width; x++)
                {
                    bitmap.SetPixel(x, y, new SKColor((byte)(shade / 2), shade, (byte)(120 + (x % 80)), 255));
                }
            }
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using Stream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }
}
