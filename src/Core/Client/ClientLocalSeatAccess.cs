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

        public static Entity RequireSolePossessedRep(GameEngine engine)
        {
            ClientLocalSeatRegistry registry = RequireRegistry(engine);
            Entity possessed = registry.RequireSolePossessedRep();
            if (!engine.World.IsAlive(possessed))
            {
                throw new InvalidOperationException(
                    $"The sole client local seat possesses entity {possessed}, but that entity is not alive.");
            }

            WireAuthorityCameraServices(engine);
            return possessed;
        }

        private static void WireAuthorityCameraServices(GameEngine engine)
        {
            if (!TryResolveSolePresentCamera(engine, out Ludots.Core.Gameplay.Camera.CameraManager camera, out _))
            {
                return;
            }

            if (camera.VirtualCameraBrain == null &&
                engine.GetService(CoreServiceKeys.VirtualCameraRegistry) is Ludots.Core.Gameplay.Camera.VirtualCameraRegistry virtualCameras)
            {
                camera.SetVirtualCameraRegistry(virtualCameras);
            }

            if (engine.GetService(CoreServiceKeys.CameraImpulseRuntime) is Ludots.Core.Gameplay.Camera.CameraImpulseRuntime impulse)
            {
                camera.SetImpulseRuntime(impulse);
            }

            if (engine.GetService(CoreServiceKeys.PlatformManagedCameraDriverRegistry) is Ludots.Core.Gameplay.Camera.PlatformManagedCameraDriverRegistry drivers)
            {
                camera.SetPlatformManagedCameraDriverRegistry(drivers);
            }
        }

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
        /// Authority camera for tools/single-viewport consumers:
        /// sole PresentBinding → sole LogicView → client-present LogicView (created if needed).
        /// Multi-PresentBinding consumers must enumerate seats via <see cref="CopyPresentBindings"/>.
        /// </summary>
        public static CameraManager ResolveAuthorityCamera(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            if (TryResolveSolePresentCamera(engine, out CameraManager presentCamera, out _))
            {
                return presentCamera;
            }

            ClientLocalSeatRegistry seats = RequireRegistry(engine);
            if (seats.PresentBindingCount > 1)
            {
                throw new InvalidOperationException(
                    "ResolveAuthorityCamera requires a sole PresentBinding when multiple present bindings exist; enumerate CopyPresentBindings for split-screen.");
            }

            LogicViewRegistry views = RequireLogicViews(engine);
            if (views.Count == 1)
            {
                var cameras = new List<CameraManager>(1);
                views.CopyCameras(cameras);
                return cameras[0];
            }

            if (views.TryGetClientPresentCamera(out CameraManager clientPresent))
            {
                return clientPresent;
            }

            if (views.Count == 0)
            {
                string id = views.EnsureClientPresentView();
                CameraManager camera = views.RequireCamera(id);
                WireAuthorityCameraServices(engine, camera);
                return camera;
            }

            throw new InvalidOperationException(
                "ResolveAuthorityCamera requires PresentBinding when multiple LogicViews exist (split-screen present is PresentBinding-driven).");
        }

        public static void CopyPresentBindings(
            GameEngine engine,
            List<(string SeatId, PresentBinding Binding)> destination)
        {
            ArgumentNullException.ThrowIfNull(engine);
            RequireRegistry(engine).CopyPresentBindings(destination);
        }

        /// <summary>
        /// Single-viewport camera consumers under multi-binding: first PresentBinding's camera in seat
        /// order; with no bindings falls back to <see cref="ResolveAuthorityCamera"/>. Per-binding
        /// draw/pick consumers must enumerate <see cref="CopyPresentBindings"/> instead of relying on
        /// a single camera for all bindings.
        /// </summary>
        public static CameraManager ResolveFirstPresentBindingCamera(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ClientLocalSeatRegistry seats = RequireRegistry(engine);
            IReadOnlyList<string> ids = seats.SeatIds;
            for (int i = 0; i < ids.Count; i++)
            {
                if (seats.TryGet(ids[i], out ClientLocalSeat seat) &&
                    seat.PresentBinding is PresentBinding present)
                {
                    return RequireLogicViews(engine).RequireCamera(present.LogicViewId);
                }
            }

            return ResolveAuthorityCamera(engine);
        }

        /// <summary>Declared PresentBinding layout from the merged GameConfig service; null means fullscreen.</summary>
        public static string? ResolveDeclaredPresentLayout(IDictionary<string, object> globals)
        {
            if (globals != null &&
                globals.TryGetValue(CoreServiceKeys.GameConfig.Name, out object? configObj) &&
                configObj is Config.GameConfig config &&
                !string.IsNullOrWhiteSpace(config.StartupPresentLayout))
            {
                return config.StartupPresentLayout.Trim();
            }

            return null;
        }

        public static string? ResolveDeclaredPresentLayout(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            return ResolveDeclaredPresentLayout(engine.GlobalContext);
        }

        public static bool TryResolvePresentCamera(
            GameEngine engine,
            string seatId,
            out CameraManager camera,
            out PresentBinding binding)
        {
            ArgumentNullException.ThrowIfNull(engine);
            camera = null!;
            binding = default;
            ClientLocalSeat seat = RequireRegistry(engine).Require(seatId);
            if (seat.PresentBinding is not PresentBinding present)
            {
                return false;
            }

            binding = present;
            camera = RequireLogicViews(engine).RequireCamera(present.LogicViewId);
            return true;
        }

        private static void WireAuthorityCameraServices(GameEngine engine, CameraManager camera)
        {
            if (camera.VirtualCameraBrain == null &&
                engine.TryGetService(CoreServiceKeys.VirtualCameraRegistry, out VirtualCameraRegistry? registry) &&
                registry != null)
            {
                camera.SetVirtualCameraRegistry(registry);
            }

            if (engine.TryGetService(CoreServiceKeys.CameraImpulseRuntime, out CameraImpulseRuntime? impulse) &&
                impulse != null)
            {
                camera.SetImpulseRuntime(impulse);
            }

            if (engine.TryGetService(
                    CoreServiceKeys.PlatformManagedCameraDriverRegistry,
                    out PlatformManagedCameraDriverRegistry? drivers) &&
                drivers != null)
            {
                camera.SetPlatformManagedCameraDriverRegistry(drivers);
            }
        }
    }
}
