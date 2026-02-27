using System;
using Ludots.Core.Diagnostics;
using SkiaSharp;
using Raylib_cs;

namespace Ludots.Client.Raylib.Rendering
{
    public class RaylibSkiaRenderer : IDisposable
    {
        private GRContext _grContext;
        private SKSurface _surface;
        private Texture2D _texture;
        private GRBackendTexture _backendTexture;
        private int _width;
        private int _height;
        private bool _useGpu;

        public SKCanvas Canvas => _surface.Canvas;

        public RaylibSkiaRenderer(int width, int height)
        {
            // GPU Acceleration is currently unstable with Raylib interop (SEHException)
            // Defaulting to CPU (Raster) rendering.
            _useGpu = false;
            
            Log.Info(in LogChannels.Presentation, $"GPU Accelerated: {_useGpu}");

            Resize(width, height);
        }

        public void Resize(int width, int height)
        {
            if (_width == width && _height == height && _surface != null) return;
            
            _width = width;
            _height = height;

            _surface?.Dispose();
            
            if (_texture.id != 0) Raylib_cs.Raylib.UnloadTexture(_texture);
            
            // Create Raylib Texture
            Image img = Raylib_cs.Raylib.GenImageColor(width, height, Raylib_cs.Color.BLANK);
            _texture = Raylib_cs.Raylib.LoadTextureFromImage(img);
            Raylib_cs.Raylib.UnloadImage(img);

            if (_useGpu)
            {
                try
                {
                    var textureInfo = new GRGlTextureInfo((uint)_texture.id, 0x0DE1, 0x8058); 
                    _backendTexture = new GRBackendTexture(width, height, false, textureInfo);
                    
                    _surface = SKSurface.Create(_grContext, _backendTexture, GRSurfaceOrigin.TopLeft, SKColorType.Rgba8888);
                }
                catch (Exception ex)
                {
                    Log.Error(in LogChannels.Presentation, $"GPU Surface init failed: {ex.Message}");
                    _useGpu = false;
                    _surface = null;
                }
            }
            
            if (_surface == null) // Fallback or failed GPU
            {
                 if (_useGpu) 
                 {
                     Log.Warn(in LogChannels.Presentation, "GPU Surface creation failed, falling back to CPU");
                     _useGpu = false;
                 }
                 
                 var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
                 _surface = SKSurface.Create(info);
            }
        }

        public void UpdateTexture()
        {
            if (_useGpu)
            {
                // Reset context before drawing if Raylib modified GL state (it did)
                _grContext.ResetContext();
                _surface.Canvas.Flush();
            }
            else
            {
                using (var image = _surface.Snapshot())
                {
                    IntPtr ptr = image.PeekPixels().GetPixels();
                    unsafe
                    {
                        Raylib_cs.Raylib.UpdateTexture(_texture, (void*)ptr);
                    }
                }
            }
        }

        public void Draw()
        {
            Raylib_cs.Raylib.DrawTexture(_texture, 0, 0, Raylib_cs.Color.WHITE);
        }

        public void RenderToScreen()
        {
            UpdateTexture();
            Draw();
        }

        public void Dispose()
        {
            _surface?.Dispose();
            _grContext?.Dispose();
            if (_texture.id != 0) Raylib_cs.Raylib.UnloadTexture(_texture);
        }
    }
}
