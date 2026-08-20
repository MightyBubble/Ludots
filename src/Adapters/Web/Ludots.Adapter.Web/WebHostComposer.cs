using System;
using Ludots.Adapter.Web.Services;
using Ludots.Adapter.Web.Streaming;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Hosting;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using Ludots.UI.Surface;

namespace Ludots.Adapter.Web
{
    public sealed record WebHostSetup(
        GameEngine Engine,
        GameConfig Config,
        UIRoot UiRoot,
        WebInputBackend InputBackend,
        WebViewController ViewController,
        WebCameraAdapter CameraAdapter,
        WebUiRuntimeBridge UiBridge,
        WebTransportLayer Transport,
        WebHostLoopStatus LoopStatus
    );

    public static class WebHostComposer
    {
        public static WebHostSetup Compose(string baseDir, string? gameConfigFile = null)
        {
            var consoleBackend = new ConsoleLogBackend();
            ILogBackend effectiveBackend = consoleBackend;
            Log.Initialize(effectiveBackend);

            var result = GameBootstrapper.InitializeFromBaseDirectory(baseDir, gameConfigFile ?? "launcher.runtime.json");
            var engine = result.Engine;
            var config = result.Config;

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
            Ludots.UI.Panels.PanelPresentationInstaller.Install(engine);

            var inputBackend = new WebInputBackend();
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

            var viewController = new WebViewController();
            var cameraAdapter = new WebCameraAdapter();
            inputBackend.SyncNeutralViewport((int)viewController.Resolution.X, (int)viewController.Resolution.Y);
            var uiBridge = new WebUiRuntimeBridge(uiRoot, inputBackend, viewController);
            var transport = new WebTransportLayer(inputBackend, viewController);
            var loopStatus = new WebHostLoopStatus();

            ValidateRequiredContextBeforeStart(engine);

            return new WebHostSetup(
                engine,
                config,
                uiRoot,
                inputBackend,
                viewController,
                cameraAdapter,
                uiBridge,
                transport,
                loopStatus);
        }

        private static void ValidateRequiredContextBeforeStart(GameEngine engine)
        {
            ValidateKey(engine, CoreServiceKeys.UIRoot);
            ValidateKey(engine, CoreServiceKeys.UiSurfaceHost);
            ValidateKey(engine, CoreServiceKeys.UISystem);
            ValidateKey(engine, CoreServiceKeys.InputHandler);
            ValidateKey(engine, CoreServiceKeys.InputBackend);
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
