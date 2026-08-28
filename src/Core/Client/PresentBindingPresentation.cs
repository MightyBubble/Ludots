using System;
using System.Collections.Generic;
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
    /// Syncs presenter / screen projector / screen ray / render culling to seat PresentBindings.
    /// Pose authority is each binding's LogicView; rect/resolution are PresentBinding metrics from the host surface.
    /// Bindings are served per seat: <see cref="TryRebindPresentBindingPipeline"/> rebinds one binding's pipeline,
    /// and the multi-binding host entry syncs metrics for every binding before serving the first in seat order.
    /// No merged cross-binding visible set is produced here.
    /// </summary>
    public static class PresentBindingPresentation
    {
        public static bool TryEnsurePresentBindings(
            GameEngine engine,
            CoreScreenProjector projector,
            CoreScreenRayProvider rayProvider,
            float fovYDeg,
            IViewController hostView,
            CameraCullingSystem? culling = null)
        {
            ArgumentNullException.ThrowIfNull(projector);
            ArgumentNullException.ThrowIfNull(rayProvider);
            if (!TryEnsureAllPresentBindings(engine, hostView, out PresentBinding firstBinding))
            {
                culling?.DisarmPresentBindingCulling();
                return false;
            }

            RebindPipeline(engine, projector, rayProvider, fovYDeg, firstBinding, culling);
            return true;
        }

        public static bool TrySyncPresentPipelines(
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
            ArgumentNullException.ThrowIfNull(presenter);
            ArgumentNullException.ThrowIfNull(projector);
            ArgumentNullException.ThrowIfNull(rayProvider);
            if (fovYDeg <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fovYDeg));
            }

            if (!TryResolveHostView(engine, culling, ref hostView))
            {
                return false;
            }

            if (!TryEnsureAllPresentBindings(engine, hostView, out PresentBinding firstBinding))
            {
                culling?.DisarmPresentBindingCulling();
                return false;
            }

            RebindPipeline(engine, projector, rayProvider, fovYDeg, firstBinding, culling);
            LogicViewRegistry views = ClientLocalSeatAccess.RequireLogicViews(engine);
            CameraManager camera = views.RequireCamera(firstBinding.LogicViewId);
            presenter.Update(camera, interpolationAlpha, cameraDebug);
            return true;
        }

        /// <summary>
        /// Rebind one seat's projector / ray / culling to its own PresentBinding — the per-binding unit of the
        /// multi-binding present pipeline. Each binding keeps its own camera pose and surface metrics.
        /// </summary>
        public static bool TryRebindPresentBindingPipeline(
            GameEngine engine,
            string seatId,
            CoreScreenProjector projector,
            CoreScreenRayProvider rayProvider,
            float fovYDeg,
            CameraCullingSystem? culling = null)
        {
            ArgumentNullException.ThrowIfNull(projector);
            ArgumentNullException.ThrowIfNull(rayProvider);
            if (fovYDeg <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fovYDeg));
            }

            if (!ClientLocalSeatAccess.TryResolvePresentCamera(engine, seatId, out CameraManager _, out PresentBinding binding))
            {
                return false;
            }

            RebindPipeline(engine, projector, rayProvider, fovYDeg, binding, culling);
            return true;
        }

        /// <summary>
        /// Pre-tick arming: render culling gets one pass per present binding in seat order. The first
        /// pass is the baseline; each later pass unions into the shared CullState (visible in any
        /// binding ⇒ drawn) without ever removing an earlier pass's visibility. Call after
        /// <see cref="TryEnsurePresentBindings"/> and before the tick's presentation phase.
        /// </summary>
        public static bool TryArmPresentBindingCullingPasses(
            GameEngine engine,
            float fovYDeg,
            IViewController hostView,
            CameraCullingSystem? culling = null)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(hostView);
            if (fovYDeg <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fovYDeg));
            }

            if (!TryEnsureAllPresentBindings(engine, hostView, out _))
            {
                culling?.DisarmPresentBindingCulling();
                return false;
            }

            if (culling != null)
            {
                var passes = new List<PresentBindingCullPass>(4);
                CollectCullPasses(engine, fovYDeg, passes);
                culling.RebindPresentBindings(passes);
            }

            return true;
        }

        /// <summary>
        /// Post-tick drive, host-agnostic: per binding in seat order — rebind projector / ray to that
        /// binding, update the presenter with the binding's interpolated camera, then hand the binding
        /// to the host draw callback (null when the host draws nothing per binding this frame).
        /// After the walk the presenter / projector / ray rest on the first binding in seat order, the
        /// state single-viewport consumers (HUD projection, picking fallback, adapter readers) see today.
        /// </summary>
        public static bool TryDrivePresentBindings(
            GameEngine engine,
            CameraPresenter presenter,
            CoreScreenProjector projector,
            CoreScreenRayProvider rayProvider,
            float interpolationAlpha,
            float fovYDeg,
            DrawPresentBinding? drawBinding = null,
            RenderCameraDebugState? cameraDebug = null,
            IViewController? hostView = null,
            CameraCullingSystem? culling = null)
        {
            ArgumentNullException.ThrowIfNull(presenter);
            ArgumentNullException.ThrowIfNull(projector);
            ArgumentNullException.ThrowIfNull(rayProvider);
            if (fovYDeg <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fovYDeg));
            }

            if (!TryResolveHostView(engine, culling, ref hostView))
            {
                return false;
            }

            if (!TryEnsureAllPresentBindings(engine, hostView, out PresentBinding firstBinding))
            {
                culling?.DisarmPresentBindingCulling();
                return false;
            }

            ClientLocalSeatRegistry registry = ClientLocalSeatAccess.RequireRegistry(engine);
            LogicViewRegistry views = ClientLocalSeatAccess.RequireLogicViews(engine);
            IReadOnlyList<string> seatIds = registry.SeatIds;
            int drivenCount = 0;
            for (int i = 0; i < seatIds.Count; i++)
            {
                ClientLocalSeat seat = registry.Require(seatIds[i]);
                if (seat.PresentBinding is not PresentBinding binding)
                {
                    continue;
                }

                CameraManager camera = views.RequireCamera(binding.LogicViewId);
                var surface = new PresentBindingSurface(binding, fovYDeg);
                projector.Rebind(camera, surface);
                rayProvider.Rebind(camera, surface);
                presenter.Update(camera, interpolationAlpha, cameraDebug);
                drivenCount++;
                if (drawBinding != null)
                {
                    drawBinding(new PresentBindingDrawFrame(seat.SeatId, binding, camera, surface, interpolationAlpha));
                }
            }

            if (drivenCount > 1)
            {
                CameraManager firstCamera = views.RequireCamera(firstBinding.LogicViewId);
                var firstSurface = new PresentBindingSurface(firstBinding, fovYDeg);
                projector.Rebind(firstCamera, firstSurface);
                rayProvider.Rebind(firstCamera, firstSurface);
                presenter.Update(firstCamera, interpolationAlpha, cameraDebug);
            }

            return drivenCount > 0;
        }

        private static void CollectCullPasses(GameEngine engine, float fovYDeg, List<PresentBindingCullPass> passes)
        {
            ClientLocalSeatRegistry registry = ClientLocalSeatAccess.RequireRegistry(engine);
            LogicViewRegistry views = ClientLocalSeatAccess.RequireLogicViews(engine);
            IReadOnlyList<string> seatIds = registry.SeatIds;
            for (int i = 0; i < seatIds.Count; i++)
            {
                ClientLocalSeat seat = registry.Require(seatIds[i]);
                if (seat.PresentBinding is not PresentBinding binding)
                {
                    continue;
                }

                CameraManager camera = views.RequireCamera(binding.LogicViewId);
                passes.Add(new PresentBindingCullPass(seat.SeatId, camera, new PresentBindingSurface(binding, fovYDeg)));
            }
        }

        public static bool TryUpdatePresentBindingPresenter(
            GameEngine engine,
            string seatId,
            CameraPresenter presenter,
            float interpolationAlpha,
            RenderCameraDebugState? cameraDebug = null)
        {
            ArgumentNullException.ThrowIfNull(presenter);
            if (!ClientLocalSeatAccess.TryResolvePresentCamera(engine, seatId, out CameraManager camera, out _))
            {
                return false;
            }

            presenter.Update(camera, interpolationAlpha, cameraDebug);
            return true;
        }

        public static bool TryEnsureSolePresentBindingPipeline(
            GameEngine engine,
            CoreScreenProjector projector,
            CoreScreenRayProvider rayProvider,
            float fovYDeg,
            IViewController hostView,
            CameraCullingSystem? culling = null)
        {
            ArgumentNullException.ThrowIfNull(projector);
            ArgumentNullException.ThrowIfNull(rayProvider);
            ArgumentNullException.ThrowIfNull(hostView);
            if (!TryResolveSolePresentSeat(engine, out _))
            {
                culling?.DisarmPresentBindingCulling();
                return false;
            }

            if (!TryEnsureAllPresentBindings(engine, hostView, out PresentBinding firstBinding))
            {
                return false;
            }

            RebindPipeline(engine, projector, rayProvider, fovYDeg, firstBinding, culling);
            return true;
        }

        public static bool TryUpdateSolePresenter(
            GameEngine engine,
            CameraPresenter presenter,
            float interpolationAlpha,
            float fovYDeg,
            RenderCameraDebugState? cameraDebug = null)
        {
            ArgumentNullException.ThrowIfNull(presenter);
            if (fovYDeg <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fovYDeg));
            }

            if (!TryResolveSolePresentSeat(engine, out string seatId))
            {
                return false;
            }

            return TryUpdatePresentBindingPresenter(engine, seatId, presenter, interpolationAlpha, cameraDebug);
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
            ArgumentNullException.ThrowIfNull(presenter);
            ArgumentNullException.ThrowIfNull(projector);
            ArgumentNullException.ThrowIfNull(rayProvider);
            if (fovYDeg <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fovYDeg));
            }

            if (!TryResolveHostView(engine, culling, ref hostView))
            {
                return false;
            }

            if (!TryResolveSolePresentSeat(engine, out string seatId))
            {
                culling?.DisarmPresentBindingCulling();
                return false;
            }

            if (!TryEnsureAllPresentBindings(engine, hostView, out PresentBinding firstBinding))
            {
                return false;
            }

            RebindPipeline(engine, projector, rayProvider, fovYDeg, firstBinding, culling);
            return TryUpdatePresentBindingPresenter(engine, seatId, presenter, interpolationAlpha, cameraDebug);
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

        private static bool TryResolveHostView(GameEngine engine, CameraCullingSystem? culling, ref IViewController? hostView)
        {
            if (hostView != null)
            {
                return true;
            }

            if (engine.TryGetService(CoreServiceKeys.ViewController, out IViewController? registered) &&
                registered != null)
            {
                hostView = registered;
                return true;
            }

            if (!HasAnySeat(engine))
            {
                culling?.DisarmPresentBindingCulling();
                return false;
            }

            throw new InvalidOperationException(
                "PresentBindingPresentation requires ViewController to sync present-surface metrics.");
        }

        private static bool TryResolveSolePresentSeat(GameEngine engine, out string seatId)
        {
            seatId = string.Empty;
            if (!engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out ClientLocalSeatRegistry? registry) ||
                registry == null ||
                registry.Count == 0)
            {
                return false;
            }

            if (!registry.TryGetSoleSeat(out ClientLocalSeat seat))
            {
                return false;
            }

            seatId = seat.SeatId;
            return true;
        }

        private static bool HasAnySeat(GameEngine engine)
        {
            return engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out ClientLocalSeatRegistry? registry) &&
                registry != null &&
                registry.Count > 0;
        }

        private static bool TryEnsureAllPresentBindings(
            GameEngine engine,
            IViewController hostView,
            out PresentBinding firstBinding)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(hostView);
            firstBinding = default;
            if (!engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out ClientLocalSeatRegistry? registry) ||
                registry == null ||
                registry.Count == 0)
            {
                return false;
            }

            string? declaredLayout = ClientLocalSeatAccess.ResolveDeclaredPresentLayout(engine);
            PresentBinding.ValidateDeclaredLayout(declaredLayout);
            Vector2 hostResolution = RequirePositiveResolution(hostView);
            LogicViewRegistry views = ClientLocalSeatAccess.RequireLogicViews(engine);
            IReadOnlyList<string> seatIds = registry.SeatIds;
            bool hasBinding = false;
            for (int i = 0; i < seatIds.Count; i++)
            {
                ClientLocalSeat seat = registry.Require(seatIds[i]);
                PresentBinding binding = EnsureSeatPresentBinding(registry, seat, i, seatIds.Count, declaredLayout, hostResolution, views);
                if (!hasBinding)
                {
                    firstBinding = binding;
                    hasBinding = true;
                }
            }

            return hasBinding;
        }

        private static PresentBinding EnsureSeatPresentBinding(
            ClientLocalSeatRegistry seats,
            ClientLocalSeat seat,
            int seatIndex,
            int seatCount,
            string? declaredLayout,
            Vector2 hostResolution,
            LogicViewRegistry views)
        {
            if (seat.PresentBinding is PresentBinding existing)
            {
                Vector2 bindingResolution = PresentBinding.PresentResolutionForHost(hostResolution, existing.NormalizedScreenRect);
                if (existing.PresentResolutionPx.Equals(bindingResolution))
                {
                    return existing;
                }

                var refreshed = new PresentBinding(existing.LogicViewId, existing.NormalizedScreenRect, bindingResolution);
                seats.SetPresentBinding(seat.SeatId, refreshed);
                return refreshed;
            }

            if (!seat.HasPossession)
            {
                throw new InvalidOperationException(
                    $"Client local seat '{seat.SeatId}' must possess a participant before PresentBinding can be created.");
            }

            if (!views.TryGetDefaultViewId(seat.PossessedRep, out string viewId))
            {
                throw new InvalidOperationException(
                    $"Client local seat '{seat.SeatId}' possession has no LogicView for PresentBinding.");
            }

            PresentBinding created = PresentBinding.FromDeclaredLayout(declaredLayout, viewId, seatIndex, seatCount, hostResolution);
            seats.SetPresentBinding(seat.SeatId, created);
            return created;
        }

        private static void RebindPipeline(
            GameEngine engine,
            CoreScreenProjector projector,
            CoreScreenRayProvider rayProvider,
            float fovYDeg,
            in PresentBinding binding,
            CameraCullingSystem? culling)
        {
            if (fovYDeg <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fovYDeg));
            }

            LogicViewRegistry views = ClientLocalSeatAccess.RequireLogicViews(engine);
            CameraManager camera = views.RequireCamera(binding.LogicViewId);
            var surface = new PresentBindingSurface(binding, fovYDeg);
            projector.Rebind(camera, surface);
            rayProvider.Rebind(camera, surface);
            culling?.RebindPresentBinding(camera, surface);
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
