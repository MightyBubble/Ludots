using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Scripting;

namespace Ludots.Core.Client
{
    /// <summary>Resolve <see cref="ClientLocalSeatRegistry"/> / possessed reps without global LocalPlayer slots.</summary>
    public static class ClientLocalSeatAccess
    {
        public static ClientLocalSeatRegistry RequireRegistry(IReadOnlyDictionary<string, object> globals)
        {
            if (globals == null)
            {
                throw new ArgumentNullException(nameof(globals));
            }

            if (!globals.TryGetValue(CoreServiceKeys.ClientLocalSeatRegistry.Name, out object? obj) ||
                obj is not ClientLocalSeatRegistry registry)
            {
                throw new InvalidOperationException(
                    $"{CoreServiceKeys.ClientLocalSeatRegistry.Name} must be registered.");
            }

            return registry;
        }

        public static ClientLocalSeatRegistry RequireRegistry(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            return engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry)
                ?? throw new InvalidOperationException(
                    $"{CoreServiceKeys.ClientLocalSeatRegistry.Name} must be registered.");
        }

        public static LogicViewRegistry RequireLogicViews(IReadOnlyDictionary<string, object> globals)
        {
            if (globals == null)
            {
                throw new ArgumentNullException(nameof(globals));
            }

            if (!globals.TryGetValue(CoreServiceKeys.LogicViewRegistry.Name, out object? obj) ||
                obj is not LogicViewRegistry registry)
            {
                throw new InvalidOperationException(
                    $"{CoreServiceKeys.LogicViewRegistry.Name} must be registered.");
            }

            return registry;
        }

        public static LogicViewRegistry RequireLogicViews(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            return engine.GetService(CoreServiceKeys.LogicViewRegistry)
                ?? throw new InvalidOperationException(
                    $"{CoreServiceKeys.LogicViewRegistry.Name} must be registered.");
        }

        public static Entity RequireSolePossessedRep(IReadOnlyDictionary<string, object> globals) =>
            RequireRegistry(globals).RequireSolePossessedRep();

        public static Entity RequireSolePossessedRep(GameEngine engine) =>
            RequireRegistry(engine).RequireSolePossessedRep();

        public static bool TryGetSolePossessedRep(IReadOnlyDictionary<string, object> globals, out Entity rep)
        {
            rep = Entity.Null;
            if (globals == null ||
                !globals.TryGetValue(CoreServiceKeys.ClientLocalSeatRegistry.Name, out object? obj) ||
                obj is not ClientLocalSeatRegistry registry)
            {
                return false;
            }

            return registry.TryGetSolePossessedRep(out rep);
        }

        public static bool TryGetSolePossessedRep(GameEngine engine, out Entity rep)
        {
            rep = Entity.Null;
            if (engine == null ||
                !engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out ClientLocalSeatRegistry? registry) ||
                registry == null)
            {
                return false;
            }

            return registry.TryGetSolePossessedRep(out rep);
        }

        public static bool TryGetSolePresentBinding(
            IReadOnlyDictionary<string, object> globals,
            out PresentBinding binding)
        {
            binding = default;
            if (globals == null ||
                !globals.TryGetValue(CoreServiceKeys.ClientLocalSeatRegistry.Name, out object? seatsObj) ||
                seatsObj is not ClientLocalSeatRegistry seats ||
                !seats.TryGetSoleSeat(out ClientLocalSeat seat) ||
                seat.PresentBinding is not PresentBinding present)
            {
                return false;
            }

            binding = present;
            return true;
        }

        public static bool TryGetSolePresentBinding(GameEngine engine, out PresentBinding binding)
        {
            binding = default;
            ArgumentNullException.ThrowIfNull(engine);
            return TryGetSolePresentBinding(engine.GlobalContext, out binding);
        }

        public static PresentBinding RequireSolePresentBinding(IReadOnlyDictionary<string, object> globals)
        {
            if (!TryGetSolePresentBinding(globals, out PresentBinding binding))
            {
                throw new InvalidOperationException(
                    "Sole ClientLocalSeat PresentBinding is required for presentation/picking.");
            }

            return binding;
        }

        public static PresentBinding RequireSolePresentBinding(GameEngine engine) =>
            RequireSolePresentBinding(engine.GlobalContext);

        public static bool TryResolveSolePresentCamera(
            IReadOnlyDictionary<string, object> globals,
            out CameraManager camera,
            out PresentBinding binding)
        {
            camera = null!;
            binding = default;
            if (!TryGetSolePresentBinding(globals, out binding))
            {
                return false;
            }

            if (!globals.TryGetValue(CoreServiceKeys.LogicViewRegistry.Name, out object? viewsObj) ||
                viewsObj is not LogicViewRegistry views)
            {
                throw new InvalidOperationException(
                    $"{CoreServiceKeys.LogicViewRegistry.Name} must be registered when PresentBinding exists.");
            }

            camera = views.RequireCamera(binding.LogicViewId);
            return true;
        }

        public static bool TryResolveSolePresentCamera(
            GameEngine engine,
            out CameraManager camera,
            out PresentBinding binding)
        {
            ArgumentNullException.ThrowIfNull(engine);
            return TryResolveSolePresentCamera(engine.GlobalContext, out camera, out binding);
        }

        public static CameraManager RequireSolePresentCamera(IReadOnlyDictionary<string, object> globals)
        {
            if (!TryResolveSolePresentCamera(globals, out CameraManager camera, out _))
            {
                throw new InvalidOperationException(
                    "Sole PresentBinding LogicView camera is required for presentation/picking.");
            }

            return camera;
        }

        public static CameraManager RequireSolePresentCamera(GameEngine engine) =>
            RequireSolePresentCamera(engine.GlobalContext);

        public static PresentBindingSurface RequireSolePresentSurface(
            IReadOnlyDictionary<string, object> globals,
            float fovYDeg)
        {
            PresentBinding binding = RequireSolePresentBinding(globals);
            return new PresentBindingSurface(binding, fovYDeg);
        }

        public static PresentBindingSurface RequireSolePresentSurface(GameEngine engine, float fovYDeg) =>
            RequireSolePresentSurface(engine.GlobalContext, fovYDeg);

        /// <summary>
        /// Sole LogicView camera when present; otherwise session boot camera before any LogicView exists.
        /// </summary>
        public static CameraManager ResolveAuthorityCamera(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            if (TryResolveSolePresentCamera(engine, out CameraManager presentCamera, out _))
            {
                return presentCamera;
            }

            if (engine.TryGetService(CoreServiceKeys.LogicViewRegistry, out LogicViewRegistry? views) &&
                views != null &&
                views.Count > 0)
            {
                if (views.Count != 1)
                {
                    throw new InvalidOperationException(
                        "ResolveAuthorityCamera requires a sole LogicView when PresentBinding is absent (multi-view is P3).");
                }

                var cameras = new List<CameraManager>(1);
                views.CopyCameras(cameras);
                return cameras[0];
            }

            return engine.GameSession.Camera;
        }
    }
}
