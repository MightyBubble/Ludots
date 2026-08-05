using System;
using System.IO;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Client.Raylib.Rendering
{
    public sealed unsafe class RaylibPostProcessRenderer : IDisposable
    {
        private Shader _shader;
        private RenderTexture2D _target;
        private bool _shaderLoaded;
        private bool _targetLoaded;
        private bool _worldFrameActive;
        private int _targetWidth;
        private int _targetHeight;
        private int _locResolution;
        private int _locTime;
        private int _locExposure;
        private int _locContrast;
        private int _locSaturation;
        private int _locVignetteStrength;

        public void BeginWorldFrame(int width, int height, Color clearColor, RaylibPostProcessConfig config)
        {
            config = config.Validate();
            if (!config.Enabled)
            {
                Rl.ClearBackground(clearColor);
                return;
            }

            EnsureRenderTarget(width, height);
            EnsureShader();
            if (_worldFrameActive)
            {
                throw new InvalidOperationException("Raylib post-process world frame is already active.");
            }

            Rl.BeginTextureMode(_target);
            _worldFrameActive = true;
            Rl.ClearBackground(clearColor);
        }

        public void EndWorldFrame(double timeSeconds, RaylibPostProcessConfig config)
        {
            config = config.Validate();
            if (!config.Enabled)
            {
                return;
            }

            if (!_targetLoaded)
            {
                throw new InvalidOperationException("Raylib post-process render target was not initialized before EndWorldFrame.");
            }

            if (!_worldFrameActive)
            {
                throw new InvalidOperationException("Raylib post-process world frame was not active before EndWorldFrame.");
            }

            EnsureShader();
            Rl.EndTextureMode();
            _worldFrameActive = false;
            UpdateUniforms(timeSeconds, config);

            Rl.BeginShaderMode(_shader);
            Rl.DrawTextureRec(_target.texture, BuildTextureSourceRectangle(_target.texture), Vector2.Zero, Color.WHITE);
            Rl.EndShaderMode();
        }

        public void AbortWorldFrame()
        {
            if (!_worldFrameActive)
            {
                return;
            }

            Rl.EndTextureMode();
            _worldFrameActive = false;
        }

        internal static Rectangle BuildTextureSourceRectangle(Texture2D texture)
        {
            return new Rectangle(0f, 0f, texture.width, -texture.height);
        }

        internal static bool NeedsRenderTargetResize(
            bool loaded,
            int currentWidth,
            int currentHeight,
            int requestedWidth,
            int requestedHeight)
        {
            if (requestedWidth <= 0) throw new ArgumentOutOfRangeException(nameof(requestedWidth));
            if (requestedHeight <= 0) throw new ArgumentOutOfRangeException(nameof(requestedHeight));
            return !loaded || currentWidth != requestedWidth || currentHeight != requestedHeight;
        }

        private void EnsureRenderTarget(int width, int height)
        {
            if (!NeedsRenderTargetResize(_targetLoaded, _targetWidth, _targetHeight, width, height))
            {
                return;
            }

            if (_targetLoaded)
            {
                Rl.UnloadRenderTexture(_target);
                _target = default;
                _targetLoaded = false;
            }

            _target = Rl.LoadRenderTexture(width, height);
            if (_target.id == 0 || _target.texture.id == 0)
            {
                throw new InvalidOperationException($"Raylib LoadRenderTexture returned an empty world target for {width}x{height}.");
            }

            Rl.SetTextureFilter(_target.texture, Rl.TextureFilter.TEXTURE_FILTER_BILINEAR);
            _targetWidth = width;
            _targetHeight = height;
            _targetLoaded = true;
        }

        private void EnsureShader()
        {
            if (_shaderLoaded)
            {
                return;
            }

            string baseDir = AppContext.BaseDirectory;
            _shader = Rl.LoadShader(null!, Path.Combine(baseDir, "postprocess.fs"));
            if (_shader.id == 0)
            {
                throw new InvalidOperationException("Failed to load post-process shader (shader.id == 0).");
            }

            _locResolution = RaylibShaderBindingGuard.RequireUniform(_shader, "uResolution", "post-process");
            _locTime = RaylibShaderBindingGuard.RequireUniform(_shader, "uTime", "post-process");
            _locExposure = RaylibShaderBindingGuard.RequireUniform(_shader, "uExposure", "post-process");
            _locContrast = RaylibShaderBindingGuard.RequireUniform(_shader, "uContrast", "post-process");
            _locSaturation = RaylibShaderBindingGuard.RequireUniform(_shader, "uSaturation", "post-process");
            _locVignetteStrength = RaylibShaderBindingGuard.RequireUniform(_shader, "uVignetteStrength", "post-process");
            _shaderLoaded = true;
        }

        private void UpdateUniforms(double timeSeconds, RaylibPostProcessConfig config)
        {
            Vector2 resolution = new(_targetWidth, _targetHeight);
            float time = (float)(timeSeconds % 100000.0);
            float exposure = config.Exposure;
            float contrast = config.Contrast;
            float saturation = config.Saturation;
            float vignetteStrength = config.VignetteStrength;

            Rl.SetShaderValue(_shader, _locResolution, &resolution, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC2);
            Rl.SetShaderValue(_shader, _locTime, &time, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_shader, _locExposure, &exposure, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_shader, _locContrast, &contrast, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_shader, _locSaturation, &saturation, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_shader, _locVignetteStrength, &vignetteStrength, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        }

        public void Dispose()
        {
            AbortWorldFrame();

            if (_targetLoaded)
            {
                Rl.UnloadRenderTexture(_target);
                _target = default;
                _targetLoaded = false;
            }

            if (_shaderLoaded)
            {
                Rl.UnloadShader(_shader);
                _shaderLoaded = false;
            }
        }
    }
}
