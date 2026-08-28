using System;
using System.IO;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using SkiaSharp;

namespace Ludots.Adapter.Raylib.Services
{
    /// <summary>
    /// Framebuffer-true screenshot capture. <c>TakeScreenshot</c>/<c>LoadImageFromScreen</c>
    /// compose the capture at the monitor's physical size while the Raylib framebuffer stays
    /// logical on Windows display scaling, anchoring the frame bottom-left and leaving black
    /// bands over the uncovered margin. Reading the framebuffer directly keeps evidence PNGs
    /// exactly what the GPU presented, at every DPI scale.
    /// </summary>
    internal static unsafe class RaylibFramebufferCapture
    {
        public static byte[] EncodeFramebufferPng()
        {
            int width = Math.Max(1, Rl.GetRenderWidth());
            int height = Math.Max(1, Rl.GetRenderHeight());
            byte* pixels = Rl.rlReadScreenPixels(width, height);
            if (pixels == null)
            {
                throw new InvalidOperationException(
                    $"rlReadScreenPixels returned null for the {width}x{height} framebuffer.");
            }

            try
            {
                using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
                FillBitmapRgba(bitmap, new ReadOnlySpan<byte>(pixels, width * height * 4));
                using SKImage image = SKImage.FromBitmap(bitmap);
                using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
                return data.ToArray();
            }
            finally
            {
                Rl.MemFree(pixels);
            }
        }

        public static void WriteFramebufferPng(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Screenshot path cannot be null or whitespace.", nameof(path));
            }

            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(fullPath, EncodeFramebufferPng());
        }

        /// <summary>
        /// Copies RGBA readback rows into the bitmap. <c>rlReadScreenPixels</c> hands back
        /// image-ordered (top-down) rows, so this is a straight row copy; the source is RGBA
        /// bytes, which is the bitmap's own <see cref="SKColorType.Rgba8888"/> row encoding.
        /// </summary>
        internal static void FillBitmapRgba(SKBitmap bitmap, ReadOnlySpan<byte> rgbaRows)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            if (rgbaRows.Length < width * height * 4)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rgbaRows),
                    $"RGBA source needs {width * height * 4} bytes for {width}x{height}, got {rgbaRows.Length}.");
            }

            IntPtr dst = bitmap.GetPixels();
            rgbaRows.Slice(0, width * height * 4).CopyTo(new Span<byte>(dst.ToPointer(), width * height * 4));
        }
    }
}
