using System;
using Ludots.Client.Raylib.Diagnostics;
using Ludots.Client.Raylib.Input;
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
using Ludots.UI.Browser;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using Ludots.UI.Surface;

namespace Ludots.Adapter.Raylib
{
    internal sealed record RaylibHostSetup(
        GameEngine Engine,
        GameConfig Config,
        UIRoot UiRoot,
        SkiaUiRenderer Renderer,
        IBrowserRuntime? BrowserRuntime);

    internal static class RaylibHostComposer
    {
        public static RaylibHostSetup Compose(string baseDir, string? gameConfigFile = null)
        {
            // Initialize log with colored console backend before anything else
            var consoleBackend = new RaylibConsoleLogBackend();
            ILogBackend effectiveBackend = consoleBackend;

            // Check if file logging is requested after config merge
            Log.Initialize(effectiveBackend);

            var result = GameBootstrapper.InitializeFromBaseDirectory(baseDir, gameConfigFile ?? "launcher.runtime.json");
            var engine = result.Engine;
            var config = result.Config;
            IBrowserRuntime? browserRuntime = RaylibBrowserRuntimeInstaller.InstallIfConfigured(engine, config, baseDir);
            if (!engine.TryGetService(CoreServiceKeys.PresentationMeshAssetRegistry, out MeshAssetRegistry meshAssets))
            {
                throw new InvalidOperationException("Raylib host requires PresentationMeshAssetRegistry before host asset binding.");
            }
            if (!engine.TryGetService(CoreServiceKeys.PresentationMaterialRegistry, out PresentationMaterialRegistry materialAssets))
            {
                throw new InvalidOperationException("Raylib host requires PresentationMaterialRegistry before host asset binding.");
            }

            new PresentationHostAssetConfigLoader(engine.ConfigPipeline, meshAssets, materialAssets)
                .Apply("raylib", engine.ConfigCatalog, engine.ConfigConflictReport);

            // Upgrade backend with file logging if configured
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
            if (browserRuntime != null)
            {
                Ludots.WebUI.Browser.PanelWebSkinInstaller.TryInstall(engine, browserRuntime);
            }

            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var syntheticInput = new SyntheticInputDevice();
            IInputBackend inputBackend = new RaylibInputBackend(syntheticInput);
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
            engine.SetService(CoreServiceKeys.InputBackend, (Core.Input.Runtime.IInputBackend)inputBackend);
            engine.SetService(CoreServiceKeys.SyntheticInput, syntheticInput);
            engine.SetService(CoreServiceKeys.HostFrameCapture, (IHostFrameCapture)new Services.RaylibFrameCaptureService());

            ValidateRequiredContextBeforeStart(engine);

            return new RaylibHostSetup(engine, config, uiRoot, renderer, browserRuntime);
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
