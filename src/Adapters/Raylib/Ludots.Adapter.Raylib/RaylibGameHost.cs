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
                RaylibHostSetup? setupToShutdown = setup;
                IBrowserRuntime? browserRuntime = setupToShutdown?.BrowserRuntime;
                setupToShutdown = setupToShutdown?.BrowserRuntime == null
                    ? setupToShutdown
                    : setupToShutdown with { BrowserRuntime = null };
                setup = null;
                ShutdownBrowserRuntimeForHostExit(setupToShutdown, browserRuntime);
            }
        }

        public void Dispose()
        {
        }

        private static void ShutdownBrowserRuntimeForHostExit(RaylibHostSetup? setup, IBrowserRuntime? browserRuntime)
        {
            if (setup == null || browserRuntime == null)
            {
                return;
            }

            try
            {
                DisposeBrowserRuntime(browserRuntime);
            }
            finally
            {
                browserRuntime = null;
                ShutdownBrowserRuntimeProcessForHostExit(setup);
            }
        }

        private static void DisposeBrowserRuntime(IBrowserRuntime browserRuntime)
        {
            browserRuntime.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
