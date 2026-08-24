using System;
using System.Collections.Generic;
using Ludots.Core.Diagnostics;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Adapter.Raylib
{
    /// <summary>
    /// <see cref="IAppHost"/> shell around the existing Raylib bootstrap + frame loop:
    /// Initialize wraps <see cref="RaylibHostComposer.Compose"/>, Run wraps <see cref="RaylibHostLoop"/>.
    /// </summary>
    public sealed class RaylibAppHost : IAppHost
    {
        public const string DefaultAppId = "raylib-main";
        public const string HostKind = "desktop";
        public const string AdapterId = "raylib";

        private readonly string? _gameConfigFile;
        private readonly AppHostLifecycle _lifecycle;
        private RaylibHostSetup? _setup;
        private volatile bool _shutdownRequested;

        public RaylibAppHost(string? gameConfigFile = null, string appId = DefaultAppId)
        {
            _gameConfigFile = gameConfigFile;
            Descriptor = new AppDescriptor(
                appId,
                HostKind,
                AdapterId,
                new Dictionary<string, string>
                {
                    ["gameConfigFile"] = gameConfigFile ?? "launcher.runtime.json"
                });
            _lifecycle = new AppHostLifecycle(Descriptor);
        }

        public AppDescriptor Descriptor { get; }

        public AppLifecyclePhase Phase => _lifecycle.Phase;

        public bool ShutdownRequested => _shutdownRequested;

        public event Action<AppStateChangedEventArgs>? PhaseChanged
        {
            add => _lifecycle.PhaseChanged += value;
            remove => _lifecycle.PhaseChanged -= value;
        }

        public void Initialize(AppInitContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            _lifecycle.TransitionTo(AppLifecyclePhase.Configuring);
            _setup = RaylibHostComposer.Compose(context.BaseDirectory, _gameConfigFile);
            var registry = _setup.Engine.GetService(CoreServiceKeys.AppHostRegistry);
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "GameEngine did not publish AppHostRegistry; engine bootstrap is incomplete.");
            }

            registry.Register(this);
            _lifecycle.TransitionTo(AppLifecyclePhase.Initialized);
        }

        public void Run()
        {
            RaylibHostSetup? setup = _setup;
            if (setup == null)
            {
                throw new InvalidOperationException($"App '{Descriptor.AppId}' must complete Initialize before Run.");
            }

            _lifecycle.TransitionTo(AppLifecyclePhase.Running);
            try
            {
                RaylibHostLoop.Run(setup);
            }
            finally
            {
                _setup = null;
                RaylibGameHost.ShutdownBrowserRuntimeForHostExit(setup, setup.BrowserRuntime);
                if (_lifecycle.Phase < AppLifecyclePhase.ShuttingDown)
                {
                    _lifecycle.TransitionTo(AppLifecyclePhase.ShuttingDown);
                }

                _lifecycle.TransitionTo(AppLifecyclePhase.Terminated);
            }
        }

        public void RequestShutdown(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Shutdown reason is required.", nameof(reason));
            }

            if (_lifecycle.Phase >= AppLifecyclePhase.ShuttingDown)
            {
                return;
            }

            _shutdownRequested = true;
            Log.Info(in LogChannels.Engine, $"Shutdown requested for app '{Descriptor.AppId}': {reason}");
            _lifecycle.TransitionTo(AppLifecyclePhase.ShuttingDown);
        }
    }
}
