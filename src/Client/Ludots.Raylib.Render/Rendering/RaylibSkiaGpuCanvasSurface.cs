using System;
using Raylib_cs;
using SkiaSharp;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// Skia GPU render-texture 画布表面：把 Raylib RenderTexture2D 包成 GRBackendRenderTarget，
    /// 供任意 Skia 内容（HUD 批、retained UI 面板）直渲。宿主与 Skia 共享 GL 状态，因此每批
    /// 渲染前 ResetContext、Flush+Submit 之后宿主才可消费该纹理——两条不变量是跨引擎合同
    /// （gitbook/architecture/skia-gpu-overlay-adapter-guide.md 接缝 2）。retained 内容配合
    /// 脏门控复用缓存纹理，见接缝 4。
    /// </summary>
    public sealed class RaylibSkiaGpuCanvasSurface : IDisposable
    {
        private const uint GlRgba8 = 0x8058;

        private readonly GRGlInterface _glInterface;
        private readonly GRContext _context;
        private readonly string _purpose;

        private RenderTexture2D _target;
        private GRBackendRenderTarget? _renderTarget;
        private SKSurface? _surface;
        private int _width;
        private int _height;
        private bool _warnedResizeFailure;

        public RaylibSkiaGpuCanvasSurface(string purpose)
        {
            if (string.IsNullOrWhiteSpace(purpose))
            {
                throw new ArgumentException("GPU canvas surface purpose is required.", nameof(purpose));
            }

            _purpose = purpose;
            (_glInterface, _context) = RaylibSkiaGlContext.Create($"GPU {purpose}");
            RenderDiagnostics.Info($"GPU Accelerated: True (Raylib Skia render-texture {purpose})");
        }

        public bool HasTarget => _target.id != 0;

        public bool TryRender(int width, int height, Action<SKSurface> render)
        {
            ArgumentNullException.ThrowIfNull(render);
            if (!EnsureSurface(width, height))
            {
                return false;
            }

            _context.ResetContext(GRGlBackendState.All);
            Rl.BeginTextureMode(_target);
            Rl.ClearBackground(Color.BLANK);
            render(_surface!);
            _surface!.Flush(submit: true, synchronous: false);
            _context.Submit(synchronous: false);
            Rl.EndTextureMode();
            return true;
        }

        public void Clear(int width, int height)
        {
            if (!EnsureSurface(width, height))
            {
                return;
            }

            Rl.BeginTextureMode(_target);
            Rl.ClearBackground(Color.BLANK);
            Rl.EndTextureMode();
        }

        public void Draw()
        {
            if (_target.id == 0)
            {
                return;
            }

            Rl.BeginBlendMode(BlendMode.BLEND_ALPHA_PREMULTIPLY);
            Rl.DrawTextureRec(
                _target.texture,
                new Rectangle(0f, 0f, _width, -_height),
                new System.Numerics.Vector2(0f, 0f),
                Color.WHITE);
            Rl.EndBlendMode();
        }

        public void Dispose()
        {
            _surface?.Dispose();
            _surface = null;
            _renderTarget?.Dispose();
            _renderTarget = null;
            if (_target.id != 0)
            {
                RaylibNativeResources.UnloadRenderTexture(_target);
                _target = default;
            }

            _context.Dispose();
            _glInterface.Dispose();
        }

        private bool EnsureSurface(int width, int height)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            if (_surface != null && _width == width && _height == height)
            {
                return true;
            }

            _surface?.Dispose();
            _surface = null;
            _renderTarget?.Dispose();
            _renderTarget = null;
            if (_target.id != 0)
            {
                RaylibNativeResources.UnloadRenderTexture(_target);
                _target = default;
            }

            try
            {
                _target = RaylibNativeResources.LoadRenderTexture(width, height);
                if (_target.id == 0 || _target.texture.id == 0)
                {
                    throw new InvalidOperationException("Raylib LoadRenderTexture returned an empty render target.");
                }

                _renderTarget = new GRBackendRenderTarget(
                    width,
                    height,
                    sampleCount: 0,
                    stencilBits: 8,
                    glInfo: new GRGlFramebufferInfo(_target.id, GlRgba8));
                // GL render texture 的行序是 bottom-left；Skia 必须按 BottomLeft 解释，
                // 上屏时 DrawTextureRec 的负高度翻转才能把内容摆正（两次取向缺一不可）。
                _surface = SKSurface.Create(
                    _context,
                    _renderTarget,
                    GRSurfaceOrigin.BottomLeft,
                    SKColorType.Rgba8888);
                if (_surface == null)
                {
                    throw new InvalidOperationException("SKSurface.Create returned null for Raylib render texture.");
                }

                _width = width;
                _height = height;
                _warnedResizeFailure = false;
                return true;
            }
            catch (Exception ex)
            {
                if (!_warnedResizeFailure)
                {
                    RenderDiagnostics.Warn($"Skia GPU render-texture surface unavailable ({_purpose}). Reason: {ex.Message}");
                    _warnedResizeFailure = true;
                }

                _surface?.Dispose();
                _surface = null;
                _renderTarget?.Dispose();
                _renderTarget = null;
                if (_target.id != 0)
                {
                    RaylibNativeResources.UnloadRenderTexture(_target);
                    _target = default;
                }

                return false;
            }
        }
    }
}
