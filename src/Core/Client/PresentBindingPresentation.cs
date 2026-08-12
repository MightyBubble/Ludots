using System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace Ludots.Core.Client
{
    /// <summary>
    /// Syncs presenter / screen projector / screen ray provider to the sole seat PresentBinding (Epic #896 P2).
    /// </summary>
    public static class PresentBindingPresentation
    {
        public static bool TrySyncSolePresentPipeline(
            GameEngine engine,
            CameraPresenter presenter,
            CoreScreenProjector projector,
            CoreScreenRayProvider rayProvider,
            float interpolationAlpha,
            float fovYDeg,
            RenderCameraDebugState? cameraDebug = null)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(presenter);
            ArgumentNullException.ThrowIfNull(projector);
            ArgumentNullException.ThrowIfNull(rayProvider);
            if (fovYDeg <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fovYDeg));
            }

            if (!engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out ClientLocalSeatRegistry? seats) ||
                seats == null ||
                seats.Count == 0)
            {
                return false;
            }

            if (!seats.TryGetSoleSeat(out ClientLocalSeat seat))
            {
                throw new InvalidOperationException(
                    "PresentBindingPresentation P2 requires exactly one ClientLocalSeat (multi-seat present is P3).");
            }

            if (seat.PresentBinding is not PresentBinding binding)
            {
                throw new InvalidOperationException(
                    $"Client local seat '{seat.SeatId}' must declare PresentBinding before presentation/picking.");
            }

            LogicViewRegistry views = ClientLocalSeatAccess.RequireLogicViews(engine);
            CameraManager camera = views.RequireCamera(binding.LogicViewId);
            var surface = new PresentBindingSurface(binding, fovYDeg);
            projector.Rebind(camera, surface);
            rayProvider.Rebind(camera, surface);
            presenter.Update(camera, interpolationAlpha, cameraDebug);
            return true;
        }

        public static void SyncSolePresentPipelineOrThrow(
            GameEngine engine,
            CameraPresenter presenter,
            CoreScreenProjector projector,
            CoreScreenRayProvider rayProvider,
            float interpolationAlpha,
            float fovYDeg,
            RenderCameraDebugState? cameraDebug = null)
        {
            if (!TrySyncSolePresentPipeline(
                    engine,
                    presenter,
                    projector,
                    rayProvider,
                    interpolationAlpha,
                    fovYDeg,
                    cameraDebug))
            {
                throw new InvalidOperationException(
                    "PresentBinding is required once ClientLocalSeatRegistry has a sole presenting seat.");
            }
        }
    }
}