using System;
using SkiaSharp;

namespace Ludots.Adapter.Raylib
{
    internal static class RaylibSkiaGlContext
    {
        public static (GRGlInterface GlInterface, GRContext Context) Create(string purpose)
        {
            if (string.IsNullOrWhiteSpace(purpose))
            {
                throw new ArgumentException("GL context purpose is required.", nameof(purpose));
            }

            GRGlInterface glInterface = GRGlInterface.Create()
                ?? throw new InvalidOperationException(
                    $"Skia {purpose} could not create a native OpenGL function interface for the current Raylib context.");
            GRContext context = GRContext.CreateGl(glInterface)
                ?? throw new InvalidOperationException(
                    $"Skia {purpose} could not create a GRContext for the current Raylib OpenGL context.");
            return (glInterface, context);
        }
    }
}
