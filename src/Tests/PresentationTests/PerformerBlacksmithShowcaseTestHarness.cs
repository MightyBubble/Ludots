using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using PerformerBlacksmithShowcaseMod.Runtime;

namespace Ludots.Tests.Presentation
{
    internal static class PerformerBlacksmithShowcaseTestHarness
    {
        internal static readonly string[] ShowcaseMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "PerformerBlacksmithShowcaseMod"
        };

        internal static GameEngine CreateEngine(params string[] modIds)
        {
            ResetGlobalRegistries();
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            string[] resolvedModIds = modIds.Length == 0 ? ShowcaseMods : modIds;
            List<string> modPaths = RepoModPaths.ResolveExplicit(repoRoot, resolvedModIds);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);
            InstallHeadlessPresentation(engine);
            engine.Start();
            return engine;
        }

        private static void ResetGlobalRegistries()
        {
            AttributeRegistry.Clear();
            Ludots.Core.Gameplay.GAS.Registry.TagRegistry.Clear();
            Ludots.Core.Presentation.Performers.PerformerScopeTagRegistry.Clear();
        }

        internal static void LoadMap(GameEngine engine, string mapId, int frames = 8)
        {
            engine.LoadMap(mapId);
            Tick(engine, frames);
        }

        internal static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.Tick(1f / 60f);
                UpdateHeadlessCamera(engine);
            }
        }

        internal static WorldHudToScreenSystem CreateHeadlessHudProjection(GameEngine engine)
        {
            var worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            var strings = engine.GetService(CoreServiceKeys.PresentationWorldHudStrings);
            var projector = engine.GetService(CoreServiceKeys.ScreenProjector);
            var view = engine.GetService(CoreServiceKeys.ViewController);
            var timings = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);

            Assert.That(worldHud, Is.Not.Null, "Blacksmith showcase benchmark requires WorldHudBatchBuffer.");
            Assert.That(screenHud, Is.Not.Null, "Blacksmith showcase benchmark requires ScreenHudBatchBuffer.");
            Assert.That(projector, Is.Not.Null, "Blacksmith showcase benchmark requires a screen projector.");
            Assert.That(view, Is.Not.Null, "Blacksmith showcase benchmark requires a view controller.");

            return new WorldHudToScreenSystem(engine.World, worldHud!, strings, projector!, view!, screenHud!, timings);
        }

        internal static void TickWithHudProjection(GameEngine engine, WorldHudToScreenSystem hudProjection, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.Tick(1f / 60f);
                UpdateHeadlessCamera(engine);
                hudProjection.Update(1f / 60f);
            }
        }

        internal static int EnqueueScatter(
            GameEngine engine,
            int totalBuildings,
            int seed,
            float minRadiusCm = PerformerBlacksmithScatterPlanner.DefaultMinRadiusCm,
            float maxRadiusCm = PerformerBlacksmithScatterPlanner.DefaultMaxRadiusCm)
        {
            var queue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");

            return PerformerBlacksmithScatterPlanner.EnqueueScatter(
                queue,
                engine.CurrentMapSession?.MapId ?? default,
                Math.Max(0, totalBuildings - 1),
                seed,
                minRadiusCm,
                maxRadiusCm);
        }

        internal static string FindRepoRoot()
        {
            string current = TestContext.CurrentContext.WorkDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "mods")) &&
                    File.Exists(Path.Combine(current, "AGENTS.md")))
                {
                    return current;
                }

                current = Path.GetDirectoryName(current)!;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.AuthoritativeInput, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static void InstallHeadlessPresentation(GameEngine engine)
        {
            var view = new HeadlessViewController();
            engine.SetService(CoreServiceKeys.ViewController, view);

            var cameraAdapter = new HeadlessCameraAdapter();
            var timings = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
            var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter, timings);
            var screenProjector = new CoreScreenProjector(engine.GameSession.Camera, view);
            var screenRayProvider = new CoreScreenRayProvider(engine.GameSession.Camera, view);
            screenProjector.BindPresenter(cameraPresenter);
            screenRayProvider.BindPresenter(cameraPresenter);
            engine.SetService(CoreServiceKeys.ScreenProjector, screenProjector);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, screenRayProvider);

            var culling = new CameraCullingSystem(
                engine.World,
                engine.GameSession.Camera,
                engine.SpatialQueries,
                view,
                loadedChunks: null,
                performers: engine.GetService(CoreServiceKeys.PerformerEntityRuntime),
                timingDiagnostics: timings);
            engine.InsertPresentationSystemBefore<PresentationEntityLifecycleSystem>(culling);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);
            engine.GlobalContext["Tests.PerformerBlacksmith.HeadlessCamera"] = new HeadlessCameraRuntime(
                cameraPresenter,
                engine.GetService(CoreServiceKeys.PresentationFrameSetup));
        }

        private static void UpdateHeadlessCamera(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue("Tests.PerformerBlacksmith.HeadlessCamera", out object? runtimeObj) ||
                runtimeObj is not HeadlessCameraRuntime runtime)
            {
                return;
            }

            float alpha = runtime.PresentationFrameSetup?.GetInterpolationAlpha() ?? 1f;
            runtime.CameraPresenter.Update(engine.GameSession.Camera, alpha);
        }

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private sealed class HeadlessViewController : IViewController
        {
            public Vector2 Resolution { get; set; } = new Vector2(1920f, 1080f);
            public float Fov { get; set; } = 60f;
            public float AspectRatio { get; set; } = 16f / 9f;
        }

        private sealed class HeadlessCameraAdapter : ICameraAdapter
        {
            public void UpdateCamera(in CameraRenderState3D state)
            {
            }
        }

        private sealed class HeadlessCameraRuntime
        {
            public HeadlessCameraRuntime(CameraPresenter cameraPresenter, PresentationFrameSetupSystem? presentationFrameSetup)
            {
                CameraPresenter = cameraPresenter;
                PresentationFrameSetup = presentationFrameSetup;
            }

            public CameraPresenter CameraPresenter { get; }

            public PresentationFrameSetupSystem? PresentationFrameSetup { get; }
        }
    }
}
