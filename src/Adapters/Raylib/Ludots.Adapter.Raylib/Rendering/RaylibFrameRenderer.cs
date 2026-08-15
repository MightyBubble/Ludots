using System;
using System.Diagnostics;
using System.Numerics;
using Ludots.Adapter.Raylib.Services;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Skia;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Adapter.Raylib
{
    internal enum RaylibFramePass
    {
        Clear,
        BeginWorldTexture,
        BeginWorld3D,
        Skybox,
        DebugGuides,
        Terrain,
        GlobalField,
        BenchmarkScene,
        PrimitiveVisuals,
        GroundOverlay,
        RoadSpline,
        DebugDraw,
        EndWorld3D,
        PostProcessComposite,
        BrowserLayer,
        OverlayComposite,
    }

    internal readonly record struct RaylibFramePassPlanInput(
        bool DrawDebugGuides,
        bool DrawTerrain,
        bool DrawVisualHeightmap,
        bool HasGlobalFieldBuffer,
        bool DrawFieldOverlays,
        bool HasBenchmarkRenderer,
        bool DrawPrimitives,
        bool HasGroundOverlays,
        bool HasRoadSplines,
        bool DrawDebugDraw,
        bool DrawSkiaUi,
        bool DrawEnvironment,
        bool UsePostProcess);

    internal readonly record struct RaylibRenderFrame(
        Camera3D ActiveCamera,
        CameraRenderState3D ActiveCameraState,
        RenderDebugState RenderDebug,
        PresentationOverlayScene? OverlayScene,
        int Width,
        int Height,
        double TimeSeconds,
        bool ActiveMapRequestsDeepBackground,
        bool HostDebugGuidesSuppressed,
        bool DrawTerrain,
        bool DrawVisualHeightmap,
        bool HasVisualHeightmap,
        bool DrawPrimitives,
        bool DrawDebugDraw,
        bool DrawFieldOverlays,
        bool DrawSkiaUi,
        bool CleanPerformanceMode,
        bool HostDiagnosticUiSuppressed,
        bool EmptyBufferWarned);

    internal readonly record struct RaylibRenderFrameResult(bool EmptyBufferWarned);

    internal sealed class RaylibFrameRenderer
    {
        private readonly GameEngine _engine;
        private readonly UIRoot _uiRoot;
        private readonly SkiaUiRenderer _skiaRenderer;
        private readonly RaylibOverlayCompositor _overlayCompositor;
        private readonly RaylibBrowserLayerRenderer _browserLayerRenderer;
        private readonly RaylibRenderEnvironmentRenderer _environmentRenderer;
        private readonly RaylibTerrainRenderer _terrainRenderer;
        private readonly RaylibVisualHeightmapRenderer _visualHeightmapRenderer;
        private readonly RaylibFieldRenderPresenter _fieldRenderPresenter;
        private readonly RaylibPrimitiveRenderer _primitiveRenderer;
        private readonly RaylibDebugDrawRenderer _debugDrawRenderer;
        private readonly RaylibBenchmarkRenderService? _benchmarkRenderer;
        private readonly GlobalFieldVisualBuffer? _globalFieldVisualBuffer;
        private readonly ScreenOverlayBuffer? _screenOverlayBuffer;
        private readonly PresentationTimingDiagnostics? _presentationTiming;

        public RaylibFrameRenderer(
            GameEngine engine,
            UIRoot uiRoot,
            SkiaUiRenderer skiaRenderer,
            RaylibOverlayCompositor overlayCompositor,
            RaylibBrowserLayerRenderer browserLayerRenderer,
            RaylibRenderEnvironmentRenderer environmentRenderer,
            RaylibTerrainRenderer terrainRenderer,
            RaylibVisualHeightmapRenderer visualHeightmapRenderer,
            RaylibFieldRenderPresenter fieldRenderPresenter,
            RaylibPrimitiveRenderer primitiveRenderer,
            RaylibDebugDrawRenderer debugDrawRenderer,
            RaylibBenchmarkRenderService? benchmarkRenderer,
            GlobalFieldVisualBuffer? globalFieldVisualBuffer,
            ScreenOverlayBuffer? screenOverlayBuffer,
            PresentationTimingDiagnostics? presentationTiming)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _uiRoot = uiRoot ?? throw new ArgumentNullException(nameof(uiRoot));
            _skiaRenderer = skiaRenderer ?? throw new ArgumentNullException(nameof(skiaRenderer));
            _overlayCompositor = overlayCompositor ?? throw new ArgumentNullException(nameof(overlayCompositor));
            _browserLayerRenderer = browserLayerRenderer ?? throw new ArgumentNullException(nameof(browserLayerRenderer));
            _environmentRenderer = environmentRenderer ?? throw new ArgumentNullException(nameof(environmentRenderer));
            _terrainRenderer = terrainRenderer ?? throw new ArgumentNullException(nameof(terrainRenderer));
            _visualHeightmapRenderer = visualHeightmapRenderer ?? throw new ArgumentNullException(nameof(visualHeightmapRenderer));
            _fieldRenderPresenter = fieldRenderPresenter ?? throw new ArgumentNullException(nameof(fieldRenderPresenter));
            _primitiveRenderer = primitiveRenderer ?? throw new ArgumentNullException(nameof(primitiveRenderer));
            _debugDrawRenderer = debugDrawRenderer ?? throw new ArgumentNullException(nameof(debugDrawRenderer));
            _benchmarkRenderer = benchmarkRenderer;
            _globalFieldVisualBuffer = globalFieldVisualBuffer;
            _screenOverlayBuffer = screenOverlayBuffer;
            _presentationTiming = presentationTiming;
        }

        public RaylibRenderFrameResult RenderFrame(in RaylibRenderFrame frame)
        {
            bool emptyBufferWarned = frame.EmptyBufferWarned;
            bool drawingActive = false;
            bool worldFrameActive = false;
            bool mode3DActive = false;
            bool frameCompleted = false;

            try
            {
                long beginDrawingStart = Stopwatch.GetTimestamp();
                Rl.BeginDrawing();
                drawingActive = true;
                _presentationTiming?.ObserveBeginDrawing(ElapsedMs(beginDrawingStart));
                Restore3DDepthState();
                worldFrameActive = true;
                _environmentRenderer.BeginWorldFrame(frame.Width, frame.Height, frame.ActiveMapRequestsDeepBackground);

                long mode3DStart = Stopwatch.GetTimestamp();
                Restore3DDepthState();
                CameraRenderState3D activeCameraState = frame.ActiveCameraState;
                mode3DActive = true;
                BeginCoreMode3D(frame.ActiveCamera, in activeCameraState);
                Restore3DDepthState();

                _environmentRenderer.DrawSkybox(frame.ActiveCamera, frame.TimeSeconds);
                DrawDebugGuides(in frame);
                DrawTerrain(in frame);
                DrawGlobalFields(in frame);
                bool benchmarkDrew = DrawBenchmarkScene(frame.ActiveCamera);
                emptyBufferWarned = DrawPrimitiveVisuals(in frame, benchmarkDrew, emptyBufferWarned);
                DrawGroundOverlays(frame.CleanPerformanceMode);
                DrawSplineRibbons(frame.CleanPerformanceMode);
                DrawDebugCommands(in frame);

                EndCoreMode3D();
                mode3DActive = false;
                _presentationTiming?.ObserveMode3D(ElapsedMs(mode3DStart));
                _environmentRenderer.EndWorldFrame(frame.TimeSeconds);
                worldFrameActive = false;

                DrawUiLayers(in frame);
                frameCompleted = true;
                return new RaylibRenderFrameResult(emptyBufferWarned);
            }
            finally
            {
                if (!frameCompleted)
                {
                    if (mode3DActive)
                    {
                        EndCoreMode3D();
                    }

                    if (worldFrameActive)
                    {
                        _environmentRenderer.AbortWorldFrame();
                    }

                    if (drawingActive)
                    {
                        Rl.EndDrawing();
                    }
                }
            }
        }

        public static int BuildPassPlan(in RaylibFramePassPlanInput input, Span<RaylibFramePass> output)
        {
            int count = 0;
            Add(output, ref count, RaylibFramePass.Clear);
            if (input.UsePostProcess)
            {
                Add(output, ref count, RaylibFramePass.BeginWorldTexture);
            }

            Add(output, ref count, RaylibFramePass.BeginWorld3D);
            if (input.DrawEnvironment)
            {
                Add(output, ref count, RaylibFramePass.Skybox);
            }

            if (input.DrawDebugGuides)
            {
                Add(output, ref count, RaylibFramePass.DebugGuides);
            }

            if (input.DrawTerrain || input.DrawVisualHeightmap)
            {
                Add(output, ref count, RaylibFramePass.Terrain);
            }

            if (input.DrawFieldOverlays && input.HasGlobalFieldBuffer)
            {
                Add(output, ref count, RaylibFramePass.GlobalField);
            }

            if (input.HasBenchmarkRenderer)
            {
                Add(output, ref count, RaylibFramePass.BenchmarkScene);
            }

            if (input.DrawPrimitives)
            {
                Add(output, ref count, RaylibFramePass.PrimitiveVisuals);
            }

            if (input.HasGroundOverlays)
            {
                Add(output, ref count, RaylibFramePass.GroundOverlay);
            }

            if (input.HasRoadSplines)
            {
                Add(output, ref count, RaylibFramePass.RoadSpline);
            }

            if (input.DrawDebugDraw)
            {
                Add(output, ref count, RaylibFramePass.DebugDraw);
            }

            Add(output, ref count, RaylibFramePass.EndWorld3D);
            if (input.UsePostProcess)
            {
                Add(output, ref count, RaylibFramePass.PostProcessComposite);
            }

            if (input.DrawSkiaUi)
            {
                Add(output, ref count, RaylibFramePass.BrowserLayer);
            }

            Add(output, ref count, RaylibFramePass.OverlayComposite);
            return count;
        }

        private static void Add(Span<RaylibFramePass> output, ref int count, RaylibFramePass pass)
        {
            if (count >= output.Length)
            {
                throw new InvalidOperationException(
                    $"Raylib frame pass output capacity {output.Length} is too small.");
            }

            output[count++] = pass;
        }

        private void DrawDebugGuides(in RaylibRenderFrame frame)
        {
            if (!frame.DrawDebugDraw ||
                (frame.DrawVisualHeightmap && frame.HasVisualHeightmap) ||
                frame.HostDebugGuidesSuppressed)
            {
                return;
            }

            DrawInfiniteGrid(frame.ActiveCamera.target, 300, 1.0f, 10);

            Vector3 target = frame.ActiveCamera.target;
            Rl.DrawLine3D(target, target + new Vector3(2.0f, 0, 0), Color.RED);
            Rl.DrawLine3D(target, target + new Vector3(0, 0, 2.0f), Color.BLUE);
            Rl.DrawLine3D(target, target + new Vector3(0, 2.0f, 0), Color.GREEN);
        }

        private void DrawTerrain(in RaylibRenderFrame frame)
        {
            if (frame.DrawVisualHeightmap &&
                _engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap? visualHeightmapForTerrain) &&
                visualHeightmapForTerrain is IVisualHeightmapRenderSource visualTerrainSource)
            {
                long terrainStart = Stopwatch.GetTimestamp();
                _visualHeightmapRenderer.Render(visualTerrainSource, frame.ActiveCamera);
                _presentationTiming?.ObserveTerrain(
                    ElapsedMs(terrainStart),
                    _visualHeightmapRenderer.ChunkBuildMsLastFrame,
                    _visualHeightmapRenderer.DrawnChunkCountLastFrame,
                    _visualHeightmapRenderer.BuiltChunkCountLastFrame);
                return;
            }

            if (frame.DrawTerrain)
            {
                long terrainStart = Stopwatch.GetTimestamp();
                _terrainRenderer.Render(_engine.VertexMap, frame.ActiveCamera);
                _presentationTiming?.ObserveTerrain(
                    ElapsedMs(terrainStart),
                    _terrainRenderer.ChunkBuildMsLastFrame,
                    _terrainRenderer.DrawnChunkCountLastFrame,
                    _terrainRenderer.BuiltChunkCountLastFrame);
                return;
            }

            _presentationTiming?.ObserveTerrain(0d, 0d, 0, 0);
        }

        private void DrawGlobalFields(in RaylibRenderFrame frame)
        {
            if (frame.DrawFieldOverlays && _globalFieldVisualBuffer != null)
            {
                long fieldRenderStart = Stopwatch.GetTimestamp();
                _fieldRenderPresenter.Draw(_globalFieldVisualBuffer);
                _presentationTiming?.ObserveGlobalFieldRender(
                    ElapsedMs(fieldRenderStart),
                    _fieldRenderPresenter.LastFieldTextureCount,
                    _fieldRenderPresenter.LastDirtyUploadCount,
                    _fieldRenderPresenter.LastDirtyUploadArea,
                    _fieldRenderPresenter.LastDrawCount);
                return;
            }

            _presentationTiming?.ObserveGlobalFieldRender(0d, 0, 0, 0, 0);
        }

        private bool DrawBenchmarkScene(in Camera3D activeCamera)
        {
            return _benchmarkRenderer != null && _benchmarkRenderer.Draw(activeCamera);
        }

        private bool DrawPrimitiveVisuals(
            in RaylibRenderFrame frame,
            bool benchmarkDrew,
            bool emptyBufferWarned)
        {
            if (frame.DrawPrimitives &&
                _engine.TryGetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer, out PrimitiveDrawBuffer draw) &&
                _engine.TryGetService(CoreServiceKeys.PresentationMeshAssetRegistry, out MeshAssetRegistry meshes))
            {
                if (!emptyBufferWarned && draw.GetSpan().Length == 0)
                {
                    Debug.WriteLine("[RaylibHostLoop] PrimitiveDrawBuffer is empty on first render frame; no Marker3D presenters emitting?");
                    emptyBufferWarned = true;
                }

                long primitiveStart = Stopwatch.GetTimestamp();
                PrimitiveDrawBuffer? snapshot = _engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer);
                SkinnedVisualBatchBuffer? skinnedBatch = _engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer);
                _engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap? visualHeightmap);
                _primitiveRenderer.Draw(
                    draw,
                    frame.ActiveCamera,
                    snapshot,
                    skinnedBatch,
                    meshes,
                    frame.RenderDebug.AcceptanceScaleMultiplier,
                    visualHeightmap,
                    frame.TimeSeconds);
                _presentationTiming?.ObservePrimitiveRender(
                    ElapsedMs(primitiveStart),
                    _primitiveRenderer.LastInstancedInstances,
                    _primitiveRenderer.LastInstancedBatches,
                    _primitiveRenderer.LastInstancedMatrixBuildMs,
                    _primitiveRenderer.LastInstancedMeshDrawMs,
                    _primitiveRenderer.LastInstancedMatrixCacheHits,
                    _primitiveRenderer.LastInstancedMatrixCacheMisses,
                    _primitiveRenderer.LastPersistentSyncMs,
                    _primitiveRenderer.LastPersistentBucketDrawMs,
                    _primitiveRenderer.LastImmediateDrawMs,
                    _primitiveRenderer.LastImmediateSkippedCount,
                    skinnedBatch?.Count ?? 0,
                    _primitiveRenderer.LastGpuSkinnedInstances,
                    _primitiveRenderer.LastGpuSkinnedBatches,
                    _primitiveRenderer.LastGpuSkinnedMatrixBuildMs,
                    _primitiveRenderer.LastGpuSkinnedMeshDrawMs);
                return emptyBufferWarned;
            }

            _presentationTiming?.ObservePrimitiveRender(0d, 0, 0);
            return emptyBufferWarned;
        }

        private void DrawGroundOverlays(bool cleanPerformanceMode)
        {
            if (!cleanPerformanceMode &&
                _engine.TryGetService(CoreServiceKeys.GroundOverlayBuffer, out GroundOverlayBuffer overlays) &&
                overlays.Count > 0)
            {
                long groundOverlayStart = Stopwatch.GetTimestamp();
                RaylibWorldOverlayRenderer.DrawGroundOverlays(overlays);
                _presentationTiming?.ObserveGroundOverlayRender(ElapsedMs(groundOverlayStart), overlays.Count);
                return;
            }

            _presentationTiming?.ObserveGroundOverlayRender(0d, 0);
        }

        private void DrawSplineRibbons(bool cleanPerformanceMode)
        {
            if (!cleanPerformanceMode &&
                _engine.GlobalContext.TryGetValue(CoreServiceKeys.SplineRibbonBuffer.Name, out object? splineObj) &&
                splineObj is SplineRibbonBuffer splineRibbons &&
                splineRibbons.Count > 0)
            {
                long splineRibbonStart = Stopwatch.GetTimestamp();
                RaylibWorldOverlayRenderer.DrawSplineRibbons(splineRibbons);
                _presentationTiming?.ObserveSplineRibbonRender(ElapsedMs(splineRibbonStart), splineRibbons.Count);
                return;
            }

            _presentationTiming?.ObserveSplineRibbonRender(0d, 0);
        }

        private void DrawDebugCommands(in RaylibRenderFrame frame)
        {
            if (frame.DrawDebugDraw &&
                _engine.TryGetService(CoreServiceKeys.DebugDrawCommandBuffer, out DebugDrawCommandBuffer dd))
            {
                long debugDrawStart = Stopwatch.GetTimestamp();
                _debugDrawRenderer.Draw(dd);
                _presentationTiming?.ObserveDebugDrawRender(
                    ElapsedMs(debugDrawStart),
                    dd.Lines.Count + dd.Circles.Count + dd.Boxes.Count);
                return;
            }

            _presentationTiming?.ObserveDebugDrawRender(0d, 0);
        }

        private void DrawUiLayers(in RaylibRenderFrame frame)
        {
            if (frame.DrawSkiaUi)
            {
                _browserLayerRenderer.Render(_uiRoot.Scene, frame.Width, frame.Height);
            }

            long overlayStart = Stopwatch.GetTimestamp();
            OverlayCompositeResult overlayResult = _overlayCompositor.Render(
                frame.OverlayScene,
                _uiRoot,
                _skiaRenderer,
                frame.DrawSkiaUi,
                frame.HostDiagnosticUiSuppressed);
            _presentationTiming?.ObserveUiRender(overlayResult.UiRenderMs);
            _presentationTiming?.ObserveUiUpload(overlayResult.UploadMs);
            _presentationTiming?.ObserveCompositeSkip(!overlayResult.RefreshComposite);
            _screenOverlayBuffer?.Clear();
            _presentationTiming?.ObserveScreenOverlayDraw(
                ElapsedMs(overlayStart),
                overlayResult.PaintMs,
                overlayResult.CompositeMs,
                overlayResult.UploadMs,
                overlayResult.FinalDrawMs,
                _overlayCompositor.OverlayRenderer.RebuiltLaneCountLastFrame,
                _overlayCompositor.OverlayRenderer.CachedTextLayoutCount);
        }

        internal static void Restore3DDepthState()
        {
            Rl.rlEnableDepthTest();
            Rl.rlEnableDepthMask();
            Rl.rlEnableBackfaceCulling();
        }

        internal static unsafe void BeginCoreMode3D(in Camera3D camera, in CameraRenderState3D cameraState)
        {
            Rl.rlDrawRenderBatchActive();
            Rl.rlMatrixMode((int)RlMatrixMode.RL_PROJECTION);
            Rl.rlPushMatrix();
            Rl.rlLoadIdentity();

            CameraClipPlanes clipPlanes = CameraViewportUtil.ResolveClipPlanes(in cameraState);
            float aspect = MathF.Max(0.001f, Rl.GetScreenWidth() / (float)Math.Max(1, Rl.GetScreenHeight()));
            if (camera.projection == CameraProjection.CAMERA_ORTHOGRAPHIC)
            {
                double top = camera.fovy / 2.0;
                double right = top * aspect;
                Rl.rlOrtho(-right, right, -top, top, clipPlanes.NearMeters, clipPlanes.FarMeters);
            }
            else
            {
                double top = clipPlanes.NearMeters * Math.Tan(WorldPlane2D.DegToRadValue(camera.fovy) * 0.5);
                double right = top * aspect;
                Rl.rlFrustum(-right, right, -top, top, clipPlanes.NearMeters, clipPlanes.FarMeters);
            }

            Rl.rlMatrixMode((int)RlMatrixMode.RL_MODELVIEW);
            Rl.rlLoadIdentity();
            Matrix4x4 view = Matrix4x4.CreateLookAt(camera.position, camera.target, camera.up);
            RaylibMatrix raylibView = RaylibMatrix.FromSystemNumerics(in view);
            MultMatrix(in raylibView);
            Rl.rlEnableDepthTest();
        }

        internal static void EndCoreMode3D()
        {
            Rl.rlDrawRenderBatchActive();
            Rl.rlMatrixMode((int)RlMatrixMode.RL_PROJECTION);
            Rl.rlPopMatrix();
            Rl.rlMatrixMode((int)RlMatrixMode.RL_MODELVIEW);
            Rl.rlLoadIdentity();
            Rl.rlDisableDepthTest();
        }

        private static unsafe void MultMatrix(in RaylibMatrix matrix)
        {
            float* values = stackalloc float[16]
            {
                matrix.m0, matrix.m1, matrix.m2, matrix.m3,
                matrix.m4, matrix.m5, matrix.m6, matrix.m7,
                matrix.m8, matrix.m9, matrix.m10, matrix.m11,
                matrix.m12, matrix.m13, matrix.m14, matrix.m15
            };
            Rl.rlMultMatrixf(values);
        }

        private static void DrawInfiniteGrid(Vector3 anchor, int halfCount, float spacing, int majorEvery)
        {
            float y = -0.05f;
            float extent = halfCount * spacing;

            float minX = anchor.X - extent;
            float minZ = anchor.Z - extent;

            float startX = MathF.Floor(minX / spacing) * spacing;
            float startZ = MathF.Floor(minZ / spacing) * spacing;

            float endX = startX + 2f * extent;
            float endZ = startZ + 2f * extent;

            var minor = new Color(80, 80, 80, 255);
            var major = new Color(130, 130, 130, 255);

            int lineCount = halfCount * 2;
            for (int i = 0; i <= lineCount; i++)
            {
                float x = startX + i * spacing;
                float z = startZ + i * spacing;

                int xi = (int)MathF.Round(x / spacing);
                int zi = (int)MathF.Round(z / spacing);

                Color xCol = majorEvery > 0 && (xi % majorEvery) == 0 ? major : minor;
                Color zCol = majorEvery > 0 && (zi % majorEvery) == 0 ? major : minor;

                Rl.DrawLine3D(new Vector3(x, y, startZ), new Vector3(x, y, endZ), xCol);
                Rl.DrawLine3D(new Vector3(startX, y, z), new Vector3(endX, y, z), zCol);
            }
        }

        private static double ElapsedMs(long startTicks)
        {
            return (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
        }
    }
}
