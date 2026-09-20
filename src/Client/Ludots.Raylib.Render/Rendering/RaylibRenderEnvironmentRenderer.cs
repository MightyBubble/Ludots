using System;
using Raylib_cs;

namespace Ludots.Raylib.Render
{
    public sealed class RaylibRenderEnvironmentRenderer : IDisposable
    {
        private readonly RaylibSkyboxRenderer _skyboxRenderer = new();
        private readonly RaylibPostProcessRenderer _postProcessRenderer = new();
        private RaylibRenderEnvironmentConfig _config;

        public RaylibRenderEnvironmentRenderer(RaylibRenderEnvironmentConfig config)
        {
            _config = config?.NormalizeAndValidate() ?? throw new ArgumentNullException(nameof(config));
        }

        public RaylibRenderEnvironmentConfig Config
        {
            get => _config;
            set => _config = value?.NormalizeAndValidate() ?? throw new ArgumentNullException(nameof(value));
        }

        public void BeginWorldFrame(int width, int height, bool activeMapRequestsDeepBackground)
        {
            BeginWorldFrame(width, height, ResolveClearColor(activeMapRequestsDeepBackground));
        }

        public void BeginWorldFrame(int width, int height, Color clearColor)
        {
            _postProcessRenderer.BeginWorldFrame(
                width,
                height,
                clearColor,
                _config.PostProcess);
        }

        public void DrawSkybox(in Camera3D camera, double timeSeconds)
        {
            _skyboxRenderer.Draw(camera, timeSeconds, _config);
        }

        public void EndWorldFrame(double timeSeconds)
        {
            _postProcessRenderer.EndWorldFrame(timeSeconds, _config.PostProcess);
        }

        public void AbortWorldFrame()
        {
            _postProcessRenderer.AbortWorldFrame();
        }

        internal Color ResolveClearColor(bool activeMapRequestsDeepBackground)
        {
            return activeMapRequestsDeepBackground
                ? _config.Skybox.DeepClearColor
                : _config.Skybox.ClearColor;
        }

        public void Dispose()
        {
            _skyboxRenderer.Dispose();
            _postProcessRenderer.Dispose();
        }
    }
}
