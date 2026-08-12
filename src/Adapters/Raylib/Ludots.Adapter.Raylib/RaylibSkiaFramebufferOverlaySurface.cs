using System;
using System.Runtime.InteropServices;
using Ludots.Core.Diagnostics;
using Ludots.Core.Presentation.Hud;
using Ludots.Presentation.Skia;
using SkiaSharp;

namespace Ludots.Adapter.Raylib
{
    internal sealed class RaylibSkiaFramebufferOverlaySurface : IDisposable
    {
        private const uint GlRgba8 = 0x8058;

        private readonly GRGlInterface _glInterface;
        private readonly GRContext _context;
        private readonly GRGlGetProcedureAddressDelegate _getProcAddress;

        private GRBackendRenderTarget? _renderTarget;
        private SKSurface? _surface;
        private int _width;
        private int _height;

        public RaylibSkiaFramebufferOverlaySurface()
        {
            _getProcAddress = ResolveGlProcAddress;
            _glInterface = GRGlInterface.CreateOpenGl(_getProcAddress)
                ?? throw new InvalidOperationException("Skia framebuffer overlay could not create an OpenGL function interface.");
            _context = GRContext.CreateGl(_glInterface)
                ?? throw new InvalidOperationException("Skia framebuffer overlay could not create a GRContext for the current Raylib OpenGL context.");
            Log.Info(in LogChannels.Presentation, "GPU Accelerated: True (Raylib Skia direct framebuffer overlay)");
        }

        public void Render(
            PresentationOverlayScene scene,
            SkiaOverlayRenderer renderer,
            PresentationOverlayLayer layer,
            int width,
            int height)
        {
            Render(scene, renderer, layer, default, hasRefreshPlan: false, width, height);
        }

        public void Render(
            PresentationOverlayScene scene,
            SkiaOverlayRenderer renderer,
            PresentationOverlayLayer layer,
            in PresentationOverlayLanePacer.LaneRefreshPlan refreshPlan,
            int width,
            int height)
        {
            Render(scene, renderer, layer, refreshPlan, hasRefreshPlan: true, width, height);
        }

        private void Render(
            PresentationOverlayScene scene,
            SkiaOverlayRenderer renderer,
            PresentationOverlayLayer layer,
            in PresentationOverlayLanePacer.LaneRefreshPlan refreshPlan,
            bool hasRefreshPlan,
            int width,
            int height)
        {
            EnsureSurface(width, height);

            _context.ResetContext(GRGlBackendState.All);
            SKCanvas canvas = _surface!.Canvas;
            canvas.ResetMatrix();
            if (hasRefreshPlan)
            {
                renderer.Render(scene, canvas, layer, refreshPlan);
            }
            else
            {
                renderer.Render(scene, canvas, layer);
            }

            _surface.Flush(submit: true, synchronous: false);
            _context.Submit(synchronous: false);
        }

        public void Dispose()
        {
            _surface?.Dispose();
            _surface = null;
            _renderTarget?.Dispose();
            _renderTarget = null;
            _context.Dispose();
            _glInterface.Dispose();
        }

        private void EnsureSurface(int width, int height)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            if (_surface != null && _width == width && _height == height)
            {
                return;
            }

            _surface?.Dispose();
            _surface = null;
            _renderTarget?.Dispose();
            _renderTarget = null;

            _renderTarget = new GRBackendRenderTarget(
                width,
                height,
                sampleCount: 0,
                stencilBits: 8,
                glInfo: new GRGlFramebufferInfo(0, GlRgba8));
            _surface = SKSurface.Create(
                _context,
                _renderTarget,
                GRSurfaceOrigin.BottomLeft,
                SKColorType.Rgba8888);
            if (_surface == null)
            {
                throw new InvalidOperationException("SKSurface.Create returned null for Raylib framebuffer.");
            }

            _width = width;
            _height = height;
        }

        private static IntPtr ResolveGlProcAddress(string name)
        {
            if (OperatingSystem.IsWindows())
            {
                IntPtr proc = WglGetProcAddress(name);
                if (proc != IntPtr.Zero && proc.ToInt64() is not 1 and not 2 and not 3 and not -1)
                {
                    return proc;
                }

                return NativeLibrary.TryLoad("opengl32.dll", out IntPtr module) &&
                    NativeLibrary.TryGetExport(module, name, out proc)
                        ? proc
                        : IntPtr.Zero;
            }

            // Linux / Unix: resolve via libGL (Raylib already created a GLX/EGL context).
            if (NativeLibrary.TryLoad("libGL.so.1", out IntPtr gl) ||
                NativeLibrary.TryLoad("libGL.so", out gl))
            {
                if (NativeLibrary.TryGetExport(gl, "glXGetProcAddressARB", out IntPtr getProc) ||
                    NativeLibrary.TryGetExport(gl, "glXGetProcAddress", out getProc))
                {
                    var getter = Marshal.GetDelegateForFunctionPointer<GlXGetProcAddress>(getProc);
                    IntPtr proc = getter(name);
                    if (proc != IntPtr.Zero)
                    {
                        return proc;
                    }
                }

                if (NativeLibrary.TryGetExport(gl, name, out IntPtr direct))
                {
                    return direct;
                }
            }

            return IntPtr.Zero;
        }

        private delegate IntPtr GlXGetProcAddress(string procName);

        [DllImport("opengl32.dll", EntryPoint = "wglGetProcAddress", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr WglGetProcAddress(string procName);
    }
}
