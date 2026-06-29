using System;
using Ludots.Platform.Abstractions;
using Ludots.UI.Browser;

namespace Ludots.Adapter.Raylib
{
    public sealed class RaylibGameHost : IGameHost
    {
        private readonly string _baseDir;
        private readonly string? _gameConfigFile;

        public RaylibGameHost(string baseDir, string? gameConfigFile = null)
        {
            _baseDir = baseDir;
            _gameConfigFile = gameConfigFile;
        }

        public void Run()
        {
            RaylibHostSetup? setup = null;
            try
            {
                setup = RaylibHostComposer.Compose(_baseDir, _gameConfigFile);
                RaylibHostLoop.Run(setup);
            }
            finally
            {
                ShutdownBrowserRuntimeForHostExit(setup);
            }
        }

        public void Dispose()
        {
        }

        private static void ShutdownBrowserRuntimeForHostExit(RaylibHostSetup? setup)
        {
            if (setup?.BrowserRuntime == null)
            {
                return;
            }

            try
            {
                setup.BrowserRuntime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                ShutdownBrowserRuntimeProcessForHostExit(setup);
            }
        }

        private static void ShutdownBrowserRuntimeProcessForHostExit(RaylibHostSetup setup)
        {
            if (!setup.Engine.GlobalContext.TryGetValue(BrowserRuntimeServiceNames.HostLifecycle, out object? lifecycle))
            {
                return;
            }

            if (lifecycle is not IBrowserRuntimeHostLifecycle browserHostLifecycle)
            {
                throw new InvalidOperationException(
                    $"Browser runtime service '{BrowserRuntimeServiceNames.HostLifecycle}' is registered with incompatible type '{lifecycle.GetType().FullName}'.");
            }

            browserHostLifecycle.ShutdownProcessForHostExit();
        }
    }
}
