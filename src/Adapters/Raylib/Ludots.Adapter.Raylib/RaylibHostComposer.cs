using System;
using Ludots.Adapter.Raylib.UI;
using Ludots.Client.Raylib.Input;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Hosting;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.UI;

namespace Ludots.Adapter.Raylib
{
    internal sealed record RaylibHostSetup(GameEngine Engine, GameConfig Config, UIRoot UiRoot);

    internal static class RaylibHostComposer
    {
        public static RaylibHostSetup Compose(string baseDir, string? gameConfigFile = null)
        {
            var result = GameBootstrapper.InitializeFromBaseDirectory(baseDir, gameConfigFile ?? "game.json");
            var engine = result.Engine;
            var config = result.Config;

            var uiRoot = new UIRoot();
            engine.GlobalContext[ContextKeys.UIRoot] = uiRoot;
            engine.GlobalContext[ContextKeys.UISystem] = new DesktopUiSystem(uiRoot);

            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            IInputBackend inputBackend = new RaylibInputBackend();
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

            return new RaylibHostSetup(engine, config, uiRoot);
        }

        private static void ValidateRequiredContextBeforeStart(GameEngine engine)
        {
            ValidateKey<UIRoot>(engine, ContextKeys.UIRoot);
            ValidateKey<Ludots.Core.UI.IUiSystem>(engine, ContextKeys.UISystem);
            ValidateKey<PlayerInputHandler>(engine, ContextKeys.InputHandler);
            ValidateKey<IInputBackend>(engine, ContextKeys.InputBackend);
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
