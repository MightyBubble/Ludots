using System;
using SkiaSharp;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// 在宿主当前 OpenGL 上下文上创建 Skia GPU 上下文。GL 函数经 SkiaSharp 原生接口解析
    /// （WGL/GLX/EGL），不得直连平台 GL pinvoke，Linux 宿主才能保住 GPU HUD。
    /// 跨引擎合同见 gitbook/architecture/skia-gpu-overlay-adapter-guide.md 接缝 1。
    /// </summary>
    public static class RaylibSkiaGlContext
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
