using System;
using Ludots.Client.Godot.Input;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Hosting;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.UI;

namespace Ludots.Adapter.Godot
{
    public sealed record GodotHostSetup(GameEngine Engine, GameConfig Config, UIRoot UiRoot);

    public static class GodotHostComposer
    {
        public static GodotHostSetup Compose(string baseDir, string? gameConfigFile, Ludots.Core.Input.Runtime.IInputBackend inputBackend)
        {
            var consoleBackend = new ConsoleLogBackend();
            Log.Initialize(consoleBackend);

            var result = GameBootstrapper.InitializeFromBaseDirectory(baseDir, gameConfigFile ?? "game.json");
            var engine = result.Engine;
            var config = result.Config;

            ILogBackend effectiveBackend = consoleBackend;
            if (config.Logging.FileLogging)
            {
                var fileBackend = new FileLogBackend(config.Logging.LogFilePath);
                effectiveBackend = new MultiLogBackend(consoleBackend, fileBackend);
                Log.Initialize(effectiveBackend, Enum.TryParse<LogLevel>(config.Logging.GlobalLevel, true, out var lvl) ? lvl : LogLevel.Info);
                LogConfigApplier.Apply(config.Logging);
            }

            engine.GlobalContext[ContextKeys.LogBackend] = effectiveBackend;

            var uiRoot = new UIRoot();
            engine.GlobalContext[ContextKeys.UIRoot] = uiRoot;
            engine.GlobalContext[ContextKeys.UISystem] = new GodotUiSystem(uiRoot);

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
            engine.GlobalContext[ContextKeys.InputHandler] = inputHandler;
            engine.GlobalContext[ContextKeys.InputBackend] = inputBackend;

            ValidateRequiredContextBeforeStart(engine);

            return new GodotHostSetup(engine, config, uiRoot);
        }

        private static void ValidateRequiredContextBeforeStart(GameEngine engine)
        {
            ValidateKey<UIRoot>(engine, ContextKeys.UIRoot);
            ValidateKey<Ludots.Core.UI.IUiSystem>(engine, ContextKeys.UISystem);
            ValidateKey<PlayerInputHandler>(engine, ContextKeys.InputHandler);
            ValidateKey<Ludots.Core.Input.Runtime.IInputBackend>(engine, ContextKeys.InputBackend);
        }

        private static void ValidateKey<T>(GameEngine engine, string key)
        {
            if (!engine.GlobalContext.TryGetValue(key, out var obj) || obj is not T)
            {
                throw new InvalidOperationException($"GlobalContext missing or invalid: {key} expected {typeof(T).FullName}");
            }
        }
    }
}
