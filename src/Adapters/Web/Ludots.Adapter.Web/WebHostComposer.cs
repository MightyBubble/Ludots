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

namespace Ludots.Adapter.Web
{
    public sealed record WebHostSetup(
        GameEngine Engine,
        GameConfig Config,
        UIRoot UiRoot,
        WebInputBackend InputBackend,
        WebViewController ViewController,
        WebCameraAdapter CameraAdapter,
        WebScreenRayProvider ScreenRayProvider,
        WebTransportLayer Transport
    );

    public static class WebHostComposer
    {
        public static WebHostSetup Compose(string baseDir, string? gameConfigFile = null)
        {
            var consoleBackend = new ConsoleLogBackend();
            ILogBackend effectiveBackend = consoleBackend;
            Log.Initialize(effectiveBackend);

            var result = GameBootstrapper.InitializeFromBaseDirectory(baseDir, gameConfigFile ?? "game.json");
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

            var uiRoot = new UIRoot();
            engine.SetService(CoreServiceKeys.UIRoot, (object)uiRoot);
            engine.SetService(CoreServiceKeys.UISystem, (Core.UI.IUiSystem)new WebUiSystem());

            var inputBackend = new WebInputBackend();
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(inputBackend, inputConfig);
            if (config.StartupInputContexts != null)
            {
                foreach (var contextId in config.StartupInputContexts)
                {
                    if (!string.IsNullOrWhiteSpace(contextId))
                        inputHandler.PushContext(contextId);
                }
            }
            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)inputBackend);

            var viewController = new WebViewController();
            var cameraAdapter = new WebCameraAdapter();
            var screenRayProvider = new WebScreenRayProvider(cameraAdapter, viewController);
            var transport = new WebTransportLayer(inputBackend, viewController);

            ValidateRequiredContextBeforeStart(engine);

            return new WebHostSetup(
                engine, config, uiRoot,
                inputBackend, viewController, cameraAdapter, screenRayProvider, transport
            );
        }

        private static void ValidateRequiredContextBeforeStart(GameEngine engine)
        {
            ValidateKey<object>(engine, CoreServiceKeys.UIRoot.Name);
            ValidateKey<Core.UI.IUiSystem>(engine, CoreServiceKeys.UISystem.Name);
            ValidateKey<PlayerInputHandler>(engine, CoreServiceKeys.InputHandler.Name);
            ValidateKey<IInputBackend>(engine, CoreServiceKeys.InputBackend.Name);
        }

        private static void ValidateKey<T>(GameEngine engine, string key)
        {
            if (!engine.GlobalContext.TryGetValue(key, out var obj) || obj is not T)
                throw new InvalidOperationException($"GlobalContext missing or invalid: {key} expected {typeof(T).FullName}");
        }
    }
}
