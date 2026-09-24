using Arch;
using Arch.Core;
using Ludots.Core.Client;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
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
using Ludots.UI;
using Ludots.UI.Skia;
using Ludots.UI.Surface;
using NUnit.Framework;
using PresenterBlacksmithShowcaseMod;
using PresenterBlacksmithShowcaseMod.Runtime;
using Ludots.Tests.TestCommon;

namespace Ludots.Tests.Presentation
{
    internal static class PresenterBlacksmithShowcaseTestHarness
    {
        internal static readonly string[] ShowcaseMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "PresenterBlacksmithShowcaseMod"
        };

        internal static GameEngine CreateEngine(params string[] modIds)
        {
            ResetGlobalRegistries();
            _ = typeof(PresenterBlacksmithShowcaseModEntry).Assembly;
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            string[] resolvedModIds = modIds.Length == 0 ? ShowcaseMods : modIds;
            List<string> modPaths = RepoModPaths.ResolveExplicit(repoRoot, resolvedModIds);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);
            InstallHeadlessPresentation(engine);
            engine.Start();
            InstallSoleLocalSeatViewer(engine);
            return engine;
        }

        /// <summary>
        /// 与实机同一坐席合同：唯一本地 seat 持有一个在场 viewer。没有它，Core 默认注册的
        /// 知识 resolver 会按迷雾合同把全部 owned minimap marker 挡成 0——基准/验收要测的是
        /// marker 管线本身，必须以真实观众身份跑，而不是豁免知识门。
        /// </summary>
        private static void InstallSoleLocalSeatViewer(GameEngine engine)
        {
            var seats = engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry)
                ?? throw new InvalidOperationException("ClientLocalSeatRegistry must be installed by engine core.");
            Entity viewer = engine.World.Create(new Ludots.Core.Gameplay.Components.PlayerIdentity { PlayerId = 1 });
            seats.Add(new ClientLocalSeat("seat.0"));
            seats.SetPossession("seat.0", playerId: 1, viewer);
        }

        private static void ResetGlobalRegistries()
        {
            AttributeRegistry.Clear();
            Ludots.Core.Gameplay.GAS.Registry.TagRegistry.Clear();
            Ludots.Core.Presentation.Presenters.PresenterScopeTagRegistry.Clear();
            Ludots.Core.Presentation.Assets.AnimationChannelRegistry.Clear();
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
                RebindHeadlessCulling(engine);
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
                RebindHeadlessCulling(engine);
                engine.Tick(1f / 60f);
                UpdateHeadlessCamera(engine);
                hudProjection.Update(1f / 60f);
            }
        }

        internal static int EnqueueScatter(
            GameEngine engine,
            int totalBuildings,
            int seed,
            float minRadiusCm = PresenterBlacksmithScatterPlanner.DefaultMinRadiusCm,
            float maxRadiusCm = PresenterBlacksmithScatterPlanner.DefaultMaxRadiusCm)
        {
            var queue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");

            WorldAabbCm bounds = engine.WorldSizeSpec.Bounds;
            return PresenterBlacksmithScatterPlanner.EnqueueScatter(
                queue,
                engine.CurrentMapSession?.MapId ?? default,
                Math.Max(0, totalBuildings - 1),
                seed,
                bounds.Left + (bounds.Width * 0.5f),
                bounds.Top + (bounds.Height * 0.5f),
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
            var screenProjector = new CoreScreenProjector(engine.AuthorityCamera(), view);
            var screenRayProvider = new CoreScreenRayProvider(engine.AuthorityCamera(), view);
            screenProjector.BindPresenter(cameraPresenter);
            screenRayProvider.BindPresenter(cameraPresenter);
            engine.SetService(CoreServiceKeys.ScreenProjector, screenProjector);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, screenRayProvider);

            var culling = new CameraCullingSystem(
                engine.World,
                engine.AuthorityCamera(),
                engine.SpatialQueries,
                view,
                loadedChunks: null,
                presenters: engine.GetService(CoreServiceKeys.PresenterEntityRuntime),
                timingDiagnostics: timings,
                cullingConfig: engine.MergedConfig.Presentation.CameraCulling);
            engine.InsertPresentationSystemBefore<PresentationEntityLifecycleSystem>(culling);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);
            engine.GlobalContext["Tests.PresenterBlacksmith.HeadlessCamera"] = new HeadlessCameraRuntime(
                cameraPresenter,
                engine.GetService(CoreServiceKeys.PresentationFrameSetup),
                culling,
                view,
                cameraPresenterBoundCamera: engine.AuthorityCamera());
        }

        internal static UIRoot InstallHeadlessUi(GameEngine engine, float width = 1280f, float height = 720f)
        {
            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(width, height);

            var textMeasurer = new SkiaTextMeasurer();
            var imageSizeProvider = new SkiaImageSizeProvider();
            var surfaceHost = new UiSurfaceHost(uiRoot, textMeasurer, imageSizeProvider);

            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, textMeasurer);
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, imageSizeProvider);
            engine.SetService(CoreServiceKeys.UiSurfaceHost, surfaceHost);
            return uiRoot;
        }

        private static void RebindHeadlessCulling(GameEngine engine)
        {
            if (!TryGetHeadlessCamera(engine, out HeadlessCameraRuntime runtime))
            {
                return;
            }

            // 地图加载会把作者取景写到当时的呈现相机上。无头夹具若还拿着启动时那台相机，
            // 裁剪就停在原点，棋盘中心的人全部落在画面外。
            CameraManager authority = engine.AuthorityCamera();
            if (ReferenceEquals(runtime.CullingCamera, authority))
            {
                return;
            }

            runtime.Culling.RebindPresentBinding(authority, runtime.View);
            runtime.CullingCamera = authority;
        }

        private static void UpdateHeadlessCamera(GameEngine engine)
        {
            if (!TryGetHeadlessCamera(engine, out HeadlessCameraRuntime runtime))
            {
                return;
            }

            float alpha = runtime.PresentationFrameSetup?.GetInterpolationAlpha() ?? 1f;
            runtime.CameraPresenter.Update(engine.AuthorityCamera(), alpha);
        }

        private static bool TryGetHeadlessCamera(GameEngine engine, out HeadlessCameraRuntime runtime)
        {
            if (engine.GlobalContext.TryGetValue("Tests.PresenterBlacksmith.HeadlessCamera", out object? runtimeObj) &&
                runtimeObj is HeadlessCameraRuntime resolved)
            {
                runtime = resolved;
                return true;
            }

            runtime = null!;
            return false;
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
            public HeadlessCameraRuntime(
                CameraPresenter cameraPresenter,
                PresentationFrameSetupSystem? presentationFrameSetup,
                CameraCullingSystem culling,
                IViewController view,
                CameraManager cameraPresenterBoundCamera)
            {
                CameraPresenter = cameraPresenter;
                PresentationFrameSetup = presentationFrameSetup;
                Culling = culling;
                View = view;
                CullingCamera = cameraPresenterBoundCamera;
            }

            public CameraPresenter CameraPresenter { get; }

            public PresentationFrameSetupSystem? PresentationFrameSetup { get; }

            public CameraCullingSystem Culling { get; }

            public IViewController View { get; }

            public CameraManager CullingCamera { get; set; }
        }
    }
}
