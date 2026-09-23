using System;
using Ludots.Adapter.WebGpu.Services;
using Ludots.Client.WebGpu.Input;
using Ludots.Client.WebGpu.Runtime;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Hosting;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using Ludots.UI.Surface;

namespace Ludots.Adapter.WebGpu
{
    public sealed record WebGpuHostSetup(
        GameEngine Engine,
        GameConfig Config,
        UIRoot UiRoot,
        SkiaUiRenderer SkiaRenderer,
        WebGpuCameraAdapter CameraAdapter);

    public static class WebGpuHostComposer
    {
        public const string AdapterId = WebGpuCapabilityDiagnostics.AdapterId;

        public static WebGpuHostSetup Compose(string baseDir, string? gameConfigFile = null)
        {
            var consoleBackend = new ConsoleLogBackend();
            ILogBackend effectiveBackend = consoleBackend;
            Log.Initialize(effectiveBackend);

            var result = GameBootstrapper.InitializeFromBaseDirectory(baseDir, gameConfigFile ?? "launcher.runtime.json");
            var engine = result.Engine;
            var config = result.Config;

            if (!engine.TryGetService(CoreServiceKeys.PresentationMeshAssetRegistry, out MeshAssetRegistry meshAssets))
            {
                throw new InvalidOperationException("WebGPU host requires PresentationMeshAssetRegistry before host asset binding.");
            }

            if (!engine.TryGetService(CoreServiceKeys.PresentationMaterialRegistry, out PresentationMaterialRegistry materialAssets))
            {
                throw new InvalidOperationException("WebGPU host requires PresentationMaterialRegistry before host asset binding.");
            }

            new PresentationHostAssetConfigLoader(engine.ConfigPipeline, meshAssets, materialAssets)
                .Apply(AdapterId, engine.ConfigCatalog, engine.ConfigConflictReport);

            if (config.Logging.FileLogging)
            {
                var fileBackend = new FileLogBackend(config.Logging.LogFilePath);
                var multiBackend = new MultiLogBackend(consoleBackend, fileBackend);
                effectiveBackend = multiBackend;
                Log.Initialize(multiBackend, Enum.TryParse<LogLevel>(config.Logging.GlobalLevel, true, out var lvl) ? lvl : LogLevel.Info);
                LogConfigApplier.Apply(config.Logging);
            }

            engine.SetService(CoreServiceKeys.LogBackend, effectiveBackend);

            var renderer = new SkiaUiRenderer();
            IUiTextMeasurer textMeasurer = new SkiaTextMeasurer();
            IUiImageSizeProvider imageSizeProvider = new SkiaImageSizeProvider();
            var uiRoot = new UIRoot(renderer);
            var uiSurfaceHost = new UiSurfaceHost(uiRoot, textMeasurer, imageSizeProvider);
            engine.SetService(CoreServiceKeys.UIRoot, (object)uiRoot);
            engine.SetService(CoreServiceKeys.UiSurfaceHost, (object)uiSurfaceHost);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)textMeasurer);
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)imageSizeProvider);
            engine.SetService(CoreServiceKeys.UISystem, (Core.UI.IUiSystem)new MarkupUiSystem(uiSurfaceHost));

            // Input backend is window-bound and attached in WebGpuHostLoop after the Silk.NET window exists.
            // Composer still validates InputHandler/InputBackend keys once the loop registers them.
            var cameraAdapter = new WebGpuCameraAdapter();

            ValidateComposerServices(engine);

            var composerChannel = Log.RegisterChannel("WebGpuHostComposer");
            Log.Info(in composerChannel, WebGpuCapabilityDiagnostics.BuildStartupReport());

            return new WebGpuHostSetup(engine, config, uiRoot, renderer, cameraAdapter);
        }

        public static void RegisterInputRuntime(
            GameEngine engine,
            GameConfig config,
            WebGpuInputBackend inputBackend)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(inputBackend, inputConfig);
            if (config.StartupInputContexts != null)
            {
                foreach (var contextId in config.StartupInputContexts)
                {
                    if (!string.IsNullOrWhiteSpace(contextId))
                    {
                        inputHandler.PushContext(contextId);
                    }
                }
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)inputBackend);
            ValidateKey(engine, CoreServiceKeys.InputHandler);
            ValidateKey(engine, CoreServiceKeys.InputBackend);
        }

        public static void ValidateRequiredContextBeforeLoop(GameEngine engine)
        {
            ValidateComposerServices(engine);
            ValidateKey(engine, CoreServiceKeys.InputHandler);
            ValidateKey(engine, CoreServiceKeys.InputBackend);
            ValidateKey(engine, CoreServiceKeys.ViewController);
            ValidateKey(engine, CoreServiceKeys.ScreenProjector);
            ValidateKey(engine, CoreServiceKeys.ScreenRayProvider);
        }

        private static void ValidateComposerServices(GameEngine engine)
        {
            ValidateKey(engine, CoreServiceKeys.UIRoot);
            ValidateKey(engine, CoreServiceKeys.UiSurfaceHost);
            ValidateKey(engine, CoreServiceKeys.UISystem);
        }

        private static void ValidateKey<T>(GameEngine engine, ServiceKey<T> key)
        {
            if (!engine.TryGetService(key, out _))
            {
                throw new InvalidOperationException($"Required service missing or invalid: {key.Name} expected {typeof(T).FullName}");
            }
        }
    }
}
