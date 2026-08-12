using System;
using System.Numerics;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    internal static class HeadlessPresentationTestHost
    {
        internal static void Install(GameEngine engine, CameraCullingFocusOverride? focusOverride = null)
        {
            var view = new FixedViewController();
            engine.SetService(CoreServiceKeys.ViewController, view);
            if (focusOverride != null)
            {
                engine.SetService(CoreServiceKeys.CameraCullingFocusOverride, focusOverride);
            }

            var cameraAdapter = new NullCameraAdapter();
            var timings = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
            var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter, timings);
            var screenProjector = new CoreScreenProjector(engine.GameSession.Camera, view);
            var screenRayProvider = new CoreScreenRayProvider(engine.GameSession.Camera, view);
            screenProjector.BindPresenter(cameraPresenter);
            screenRayProvider.BindPresenter(cameraPresenter);
            var presentationFrameSetup = engine.GetService(CoreServiceKeys.PresentationFrameSetup);
            screenProjector.BindPresentationAlphaProvider(() => presentationFrameSetup?.GetInterpolationAlpha() ?? 1f);
            screenRayProvider.BindPresentationAlphaProvider(() => presentationFrameSetup?.GetInterpolationAlpha() ?? 1f);

            engine.SetService(CoreServiceKeys.ScreenProjector, screenProjector);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, screenRayProvider);

            var culling = new CameraCullingSystem(
                engine.World,
                engine.GameSession.Camera,
                engine.SpatialQueries,
                view,
                loadedChunks: null,
                focusOverride: focusOverride,
                performers: engine.GetService(CoreServiceKeys.PerformerEntityRuntime),
                timingDiagnostics: timings,
                cullingConfig: engine.MergedConfig.Presentation.CameraCulling);
            culling.DisarmPresentBindingCulling();
            engine.InsertPresentationSystemBefore<PresentationEntityLifecycleSystem>(culling);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);
            engine.GlobalContext["Tests.HeadlessPresentation.Camera"] = new HeadlessCameraRuntime(
                cameraPresenter,
                screenProjector,
                screenRayProvider,
                view,
                culling,
                presentationFrameSetup);
        }

        internal static void UpdateCamera(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue("Tests.HeadlessPresentation.Camera", out object? runtimeObj) ||
                runtimeObj is not HeadlessCameraRuntime runtime)
            {
                return;
            }

            float alpha = runtime.PresentationFrameSetup?.GetInterpolationAlpha() ?? 1f;
            if (!PresentBindingPresentation.TrySyncSolePresentPipeline(
                    engine,
                    runtime.CameraPresenter,
                    runtime.ScreenProjector,
                    runtime.ScreenRayProvider,
                    alpha,
                    runtime.View.Fov,
                    hostView: runtime.View,
                    culling: runtime.Culling))
            {
                if (engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out ClientLocalSeatRegistry? seats) &&
                    seats != null &&
                    seats.Count > 0)
                {
                    throw new InvalidOperationException(
                        "ClientLocalSeatRegistry is published but headless PresentBinding pipeline failed to sync.");
                }

                runtime.CameraPresenter.Update(engine.GameSession.Camera, alpha);
            }
        }

        internal static CameraPresenter? GetCameraPresenter(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue("Tests.HeadlessPresentation.Camera", out object? runtimeObj) &&
                runtimeObj is HeadlessCameraRuntime runtime
                    ? runtime.CameraPresenter
                    : null;
        }

        private sealed class FixedViewController : IViewController
        {
            public Vector2 Resolution { get; } = new(1920f, 1080f);
            public float Fov { get; } = 60f;
            public float AspectRatio { get; } = 16f / 9f;
        }

        private sealed class NullCameraAdapter : ICameraAdapter
        {
            public void UpdateCamera(in CameraRenderState3D state)
            {
            }
        }

        private sealed class HeadlessCameraRuntime
        {
            public HeadlessCameraRuntime(
                CameraPresenter cameraPresenter,
                CoreScreenProjector screenProjector,
                CoreScreenRayProvider screenRayProvider,
                IViewController view,
                CameraCullingSystem culling,
                PresentationFrameSetupSystem? presentationFrameSetup)
            {
                CameraPresenter = cameraPresenter;
                ScreenProjector = screenProjector;
                ScreenRayProvider = screenRayProvider;
                View = view;
                Culling = culling;
                PresentationFrameSetup = presentationFrameSetup;
            }

            public CameraPresenter CameraPresenter { get; }
            public CoreScreenProjector ScreenProjector { get; }
            public CoreScreenRayProvider ScreenRayProvider { get; }
            public IViewController View { get; }
            public CameraCullingSystem Culling { get; }
            public PresentationFrameSetupSystem? PresentationFrameSetup { get; }
        }
    }
}
