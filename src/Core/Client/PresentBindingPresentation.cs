using System;
using System.Numerics;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace Ludots.Core.Client
{
    /// <summary>
    /// Syncs presenter / screen projector / screen ray / render culling to the sole seat PresentBinding (Epic #896 P2).
    /// Pose authority is the bound LogicView; rect/resolution are PresentBinding metrics from the host surface.
    /// </summary>
    public static class PresentBindingPresentation
    {
        public static bool TryEnsureSolePresentBindingPipeline(
            GameEngine engine,
            CoreScreenProjector projector,
            CoreScreenRayProvider rayProvider,
            float fovYDeg,
            IViewController hostView,
            CameraCullingSystem? culling = null)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(projector);
            ArgumentNullException.ThrowIfNull(rayProvider);
            ArgumentNullException.ThrowIfNull(hostView);
            if (fovYDeg <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fovYDeg));
            }

            if (!TryResolveSolePresentSeat(engine, out ClientLocalSeat seat, out ClientLocalSeatRegistry seats))
            {
                culling?.DisarmPresentBindingCulling();
                return false;
            }

            PresentBinding binding = EnsurePresentBindingFromHostSurface(engine, seats, seat, hostView);
            LogicViewRegistry views = ClientLocalSeatAccess.RequireLogicViews(engine);
            CameraManager camera = views.RequireCamera(binding.LogicViewId);
            var surface = new PresentBindingSurface(binding, fovYDeg);
            projector.Rebind(camera, surface);
            rayProvider.Rebind(camera, surface);
            culling?.RebindPresentBinding(camera, surface);
            return true;
        }

        public static bool TryUpdateSolePresenter(
            GameEngine engine,
            CameraPresenter presenter,
            float interpolationAlpha,
            float fovYDeg,
            RenderCameraDebugState? cameraDebug = null)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(presenter);
            if (fovYDeg <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fovYDeg));
            }

            if (!ClientLocalSeatAccess.TryResolveSolePresentCamera(engine, out CameraManager camera, out _))
            {
                return false;
            }

            presenter.Update(camera, interpolationAlpha, cameraDebug);
            return true;
        }

        public static bool TrySyncSolePresentPipeline(
            GameEngine engine,
            CameraPresenter presenter,
            CoreScreenProjector projector,
            CoreScreenRayProvider rayProvider,
            float interpolationAlpha,
            float fovYDeg,
            RenderCameraDebugState? cameraDebug = null,
            IViewController? hostView = null,
            CameraCullingSystem? culling = null)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(presenter);
            ArgumentNullException.ThrowIfNull(projector);
            ArgumentNullException.ThrowIfNull(rayProvider);
            if (fovYDeg <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fovYDeg));
            }

            if (hostView == null)
            {
                if (!engine.TryGetService(CoreServiceKeys.ViewController, out IViewController? registered) ||
                    registered == null)
                {
                    if (!TryResolveSolePresentSeat(engine, out _, out _))
                    {
                        culling?.DisarmPresentBindingCulling();
                        return false;
                    }

                    throw new InvalidOperationException(
                        "PresentBindingPresentation requires ViewController to sync present-surface metrics.");
                }

                hostView = registered;
            }

            if (!TryEnsureSolePresentBindingPipeline(
                    engine,
                    projector,
                    rayProvider,
                    fovYDeg,
                    hostView,
                    culling))
            {
                return false;
            }

            return TryUpdateSolePresenter(engine, presenter, interpolationAlpha, fovYDeg, cameraDebug);
        }

        public static void SyncSolePresentPipelineOrThrow(
            GameEngine engine,
            CameraPresenter presenter,
            CoreScreenProjector projector,
            CoreScreenRayProvider rayProvider,
            float interpolationAlpha,
            float fovYDeg,
            RenderCameraDebugState? cameraDebug = null,
            IViewController? hostView = null,
            CameraCullingSystem? culling = null)
        {
            if (!TrySyncSolePresentPipeline(
                    engine,
                    presenter,
                    projector,
                    rayProvider,
                    interpolationAlpha,
                    fovYDeg,
                    cameraDebug,
                    hostView,
                    culling))
            {
                throw new InvalidOperationException(
                    "PresentBinding is required once ClientLocalSeatRegistry has a sole presenting seat.");
            }
        }

        private static bool TryResolveSolePresentSeat(
            GameEngine engine,
            out ClientLocalSeat seat,
            out ClientLocalSeatRegistry seats)
        {
            seat = null!;
            seats = null!;
            if (!engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out ClientLocalSeatRegistry? registry) ||
                registry == null ||
                registry.Count == 0)
            {
                return false;
            }

            seats = registry;
            if (!seats.TryGetSoleSeat(out seat))
            {
                throw new InvalidOperationException(
                    "PresentBindingPresentation P2 requires exactly one ClientLocalSeat (multi-seat present is P3).");
            }

            return true;
        }

        private static PresentBinding EnsurePresentBindingFromHostSurface(
            GameEngine engine,
            ClientLocalSeatRegistry seats,
            ClientLocalSeat seat,
            IViewController hostView)
        {
            Vector2 resolution = RequirePositiveResolution(hostView);
            if (seat.PresentBinding is PresentBinding existing)
            {
                if (existing.PresentResolutionPx.Equals(resolution))
                {
                    return existing;
                }

                var refreshed = new PresentBinding(existing.LogicViewId, existing.NormalizedScreenRect, resolution);
                seats.SetPresentBinding(seat.SeatId, refreshed);
                return refreshed;
            }

            if (!seat.HasPossession)
            {
                throw new InvalidOperationException(
                    $"Client local seat '{seat.SeatId}' must possess a participant before PresentBinding can be created.");
            }

            LogicViewRegistry views = ClientLocalSeatAccess.RequireLogicViews(engine);
            if (!views.TryGetDefaultViewId(seat.PossessedRep, out string viewId))
            {
                throw new InvalidOperationException(
                    $"Client local seat '{seat.SeatId}' possession has no LogicView for PresentBinding.");
            }

            PresentBinding created = PresentBinding.FullScreen(viewId, resolution);
            seats.SetPresentBinding(seat.SeatId, created);
            return created;
        }

        private static Vector2 RequirePositiveResolution(IViewController hostView)
        {
            Vector2 resolution = hostView.Resolution;
            if (resolution.X <= 0f || resolution.Y <= 0f)
            {
                throw new InvalidOperationException(
                    "ViewController.Resolution must be positive before syncing PresentBinding metrics.");
            }

            return resolution;
        }
    }
}
