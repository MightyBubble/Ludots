using System;
using Ludots.Adapter.Godot.Services;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;

namespace Ludots.Adapter.Godot
{
    /// <summary>
    /// Godot host loop. Call Initialize after Compose, then Tick each _Process frame.
    /// </summary>
    public sealed class GodotHostLoop
    {
        private readonly GodotHostSetup _setup;
        private readonly GodotHostContext _context;

        public GameEngine Engine => _setup.Engine;
        private readonly CameraPresenter _cameraPresenter;
        private bool _started;

        public GodotHostLoop(GodotHostSetup setup, GodotHostContext context)
        {
            _setup = setup;
            _context = context;
            _cameraPresenter = new CameraPresenter(setup.Engine.SpatialCoords, context.CameraAdapter);
        }

        public void Initialize()
        {
            var engine = _setup.Engine;
            var viewController = _context.ViewController;
            var screenProjector = new CoreScreenProjector(engine.GameSession.Camera, viewController);
            engine.GlobalContext[ContextKeys.ViewController] = viewController;
            engine.GlobalContext[ContextKeys.ScreenProjector] = screenProjector;
            engine.GlobalContext[ContextKeys.ScreenRayProvider] = _context.ScreenRayProvider;

            var cullingSystem = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, viewController);
            engine.RegisterPresentationSystem(cullingSystem);

            if (engine.GlobalContext.TryGetValue(ContextKeys.PresentationWorldHudBuffer, out var whObj) && whObj is WorldHudBatchBuffer worldHud &&
                engine.GlobalContext.TryGetValue(ContextKeys.PresentationScreenHudBuffer, out var shObj) && shObj is ScreenHudBatchBuffer screenHud)
            {
                engine.GlobalContext.TryGetValue(ContextKeys.PresentationWorldHudStrings, out var strObj);
                var worldHudStrings = strObj as Ludots.Core.Presentation.Config.WorldHudStringTable;
                engine.RegisterPresentationSystem(new WorldHudToScreenSystem(engine.World, worldHud, worldHudStrings, screenProjector, viewController, screenHud));
            }

            ValidateRequiredContextBeforeLoop(engine);

            engine.Start();
            if (string.IsNullOrWhiteSpace(_setup.Config.StartupMapId))
            {
                throw new InvalidOperationException("Invalid game.json: 'StartupMapId' cannot be empty.");
            }
            engine.LoadMap(_setup.Config.StartupMapId);
            _started = true;
        }

        public void Tick(float dt)
        {
            if (!_started) return;

            var engine = _setup.Engine;
            var uiRoot = _setup.UiRoot;

            int w = (int)_context.ViewController.Resolution.X;
            int h = (int)_context.ViewController.Resolution.Y;
            if (w > 0 && h > 0)
            {
                uiRoot.Resize(w, h);
            }

            engine.GlobalContext[ContextKeys.UiCaptured] = false;
            engine.Tick(dt);

            _cameraPresenter.Update(engine.GameSession.Camera.State, dt);
        }

        public void Stop()
        {
            if (_started)
            {
                _setup.Engine.Stop();
                _started = false;
            }
        }

        private static void ValidateRequiredContextBeforeLoop(GameEngine engine)
        {
            ValidateKey<IScreenProjector>(engine, ContextKeys.ScreenProjector);
            ValidateKey<IScreenRayProvider>(engine, ContextKeys.ScreenRayProvider);
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
