using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Adapter.Raylib;
using Ludots.Adapter.Raylib.Rendering;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Instancing;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using NUnit.Framework;
using InstancedBatchDemoMod;

namespace Ludots.Tests.RaylibAdapter
{
    /// <summary>
    /// End-to-end headless pass over the typed instanced batch channel: demo presenter emits
    /// InstancedBatchRequest chunks from Core, the adapter lane store mirrors them into a
    /// resident lane, and completion stops re-emitting. GPU drawing itself is covered by the
    /// preset acceptance capture, not here.
    /// </summary>
    [TestFixture]
    public sealed class RaylibInstancedBatchDemoIntegrationTests
    {
        private const int GridInstances = 64;

        [Test]
        public void DemoGrid_ProducesOneCompletedResidentLane()
        {
            using GameEngine engine = CreateEngine();
            MeshAssetRegistry meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry);
            var store = new RaylibInstancedBatchLaneStore();
            InstancedBatchRequestBuffer requests = engine.GetService(CoreServiceKeys.InstancedBatchRequestBuffer);
            InstancedBatchAssetRegistry batches = engine.GetService(CoreServiceKeys.InstancedBatchAssetRegistry);

            engine.LoadMap("instanced_batch_demo");
            int settleFrames = 0;
            for (int i = 0; i < 12 && store.ResidentLaneCount == 0; i++)
            {
                Tick(engine);
                store.ApplyRequests(requests.GetSpan(), batches);
                settleFrames++;
            }

            Assert.That(settleFrames, Is.LessThanOrEqualTo(12), "Typed lane never became resident.");
            Assert.That(store.ResidentLaneCount, Is.EqualTo(1));
            RaylibInstancedBatchLane lane = store.GetResidentLane(0);
            Assert.That(lane.Count, Is.EqualTo(GridInstances));
            Assert.That(lane.MeshAssetId, Is.EqualTo(meshes.GetId("cube")));
            Assert.That(lane.RenderPath, Is.EqualTo(VisualRenderPath.InstancedStaticMesh));
            Assert.That(lane.Visible, Is.True);

            // First grid cell sits at positionCm (-875, 0, -875) with uniform scale (2, 1.2, 2).
            Matrix4x4 first = lane.Matrices[0];
            Assert.That(first.M11, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(first.M22, Is.EqualTo(1.2f).Within(0.0001f));
            Assert.That(first.M33, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(first.M41, Is.EqualTo(-8.75f).Within(0.0001f));
            Assert.That(first.M42, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(first.M43, Is.EqualTo(-8.75f).Within(0.0001f));

            // Submission runtime marks the batch Completed; steady ticks re-emit nothing so the
            // lane revision (and the renderer's matrix cache behind it) stays put.
            int stableRevision = lane.Revision;
            for (int i = 0; i < 3; i++)
            {
                Tick(engine);
                store.ApplyRequests(requests.GetSpan(), batches);
            }

            Assert.That(store.LastAppliedRequestCount, Is.EqualTo(0));
            Assert.That(store.GetResidentLane(0).Revision, Is.EqualTo(stableRevision));
            Assert.That(store.GetResidentLane(0).Count, Is.EqualTo(GridInstances));
        }

        private static GameEngine CreateEngine()
        {
            ResetGlobalRegistries();
            _ = typeof(InstancedBatchDemoModEntry).Assembly;
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            List<string> modPaths = ResolveModPaths(repoRoot, new[] { "LudotsCoreMod", "CoreInputMod", "InstancedBatchDemoMod" });

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            engine.RegisterPresentationAdapterCapabilities(
                new PresentationAdapterCapabilities(RaylibHostComposer.ComposePresentationVisualCapabilities()));
            InstallInput(engine);
            InstallHeadlessPresentation(engine);
            engine.Start();
            return engine;
        }

        private static void Tick(GameEngine engine)
        {
            engine.Tick(1f / 60f);
        }

        private static void ResetGlobalRegistries()
        {
            AttributeRegistry.Clear();
            TagRegistry.Clear();
            Ludots.Core.Presentation.Presenters.PresenterScopeTagRegistry.Clear();
            AnimationChannelRegistry.Clear();
        }

        private static List<string> ResolveModPaths(string repoRoot, IEnumerable<string> modIds)
        {
            var discovered = ModDiscovery.DiscoverMods(new[] { Path.Combine(repoRoot, "mods") });
            var byName = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < discovered.Count; i++)
            {
                byName[discovered[i].Manifest.Name] = discovered[i].DirectoryPath;
            }

            var result = new List<string>();
            foreach (string modId in modIds)
            {
                if (!byName.TryGetValue(modId, out string modPath))
                {
                    throw new DirectoryNotFoundException($"Mod not found in repo: {modId}");
                }

                result.Add(modPath);
            }

            return result;
        }

        private static string FindRepoRoot()
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
            var authorityCamera = ClientLocalSeatAccess.ResolveAuthorityCamera(engine);
            var screenProjector = new CoreScreenProjector(authorityCamera, view);
            var screenRayProvider = new CoreScreenRayProvider(authorityCamera, view);
            screenProjector.BindPresenter(cameraPresenter);
            screenRayProvider.BindPresenter(cameraPresenter);
            engine.SetService(CoreServiceKeys.ScreenProjector, screenProjector);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, screenRayProvider);

            var culling = new CameraCullingSystem(
                engine.World,
                authorityCamera,
                engine.SpatialQueries,
                view,
                loadedChunks: null,
                presenters: engine.GetService(CoreServiceKeys.PresenterEntityRuntime),
                timingDiagnostics: timings,
                cullingConfig: engine.MergedConfig.Presentation.CameraCulling);
            engine.InsertPresentationSystemBefore<PresentationEntityLifecycleSystem>(culling);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);
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
    }
}
