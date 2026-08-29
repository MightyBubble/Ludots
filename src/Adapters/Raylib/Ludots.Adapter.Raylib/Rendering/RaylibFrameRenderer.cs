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
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Skia;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Raylib.Render;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Adapter.Raylib
{
    internal enum RaylibFramePass
    {
        Clear,
        WaterReflection,
        WaterRefraction,
        ShadowDepth,
        BeginWorldTexture,
        BeginWorld3D,
        Skybox,
        DebugGuides,
        Terrain,
        NavMeshOverlay,
        GlobalField,
        BenchmarkScene,
        PrimitiveVisuals,
        GroundOverlay,
        SplineRibbon,
        TrailMeshes,
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
        bool WaterEnabled,
        bool HasShadowFrame,
        bool HasGlobalFieldBuffer,
        bool DrawFieldOverlays,
        bool HasBenchmarkRenderer,
        bool DrawPrimitives,
        bool HasGroundOverlays,
        bool HasSplineRibbons,
        bool HasTrailMeshes,
        bool DrawNavMeshOverlay,
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
        float DeltaSeconds,
        bool ActiveMapRequestsDeepBackground,
        bool HostDebugGuidesSuppressed,
        bool DrawTerrain,
        bool DrawVisualHeightmap,
        bool HasVisualHeightmap,
        bool DrawPrimitives,
        bool DrawDebugDraw,
        bool DrawFieldOverlays,
        bool DrawSkiaUi,
        bool DrawNavMeshOverlay,
        bool CleanPerformanceMode,
        bool HostDiagnosticUiSuppressed,
        bool EmptyBufferWarned);

    internal readonly record struct RaylibRenderFrameResult(bool EmptyBufferWarned);

    /// <summary>
    /// 唯一生产帧执行者：RaylibHostLoop 构建一帧输入后由本类执行 BeginDrawing..覆盖层合成的完整 pass 序列。
    /// 执行方式是先经 BuildPassPlan 声明本帧 pass，再按声明顺序逐项执行——声明顺序与执行顺序由结构保证一致；
    /// LastExecutedPasses 在 RenderFrame 入口清零、按"已进入"记录（进入后抛错的 pass 也在轨迹内），供诊断与漂移测试消费。
    /// EndDrawing 及其后的截图取证仍归宿主循环（ENG-1c）。
    /// </summary>
    internal sealed class RaylibFrameRenderer : IDisposable
    {
        private const int MaxPassesPerFrame = 32;

        private readonly GameEngine _engine;
        private readonly UIRoot _uiRoot;
        private readonly SkiaUiRenderer _skiaRenderer;
        private readonly RaylibOverlayCompositor _overlayCompositor;
        private readonly RaylibBrowserLayerRenderer _browserLayerRenderer;
        private readonly RaylibRenderEnvironmentRenderer _environmentRenderer;
        private readonly RaylibSkyEnvironment _skyEnvironment;
        private readonly RaylibWaterPass _waterPass;
        private readonly RaylibFrameLighting _frameLighting;
        private readonly RaylibTerrainRenderer _terrainRenderer;
        private readonly RaylibVisualHeightmapRenderer _visualHeightmapRenderer;
        private readonly RaylibFieldRenderPresenter _fieldRenderPresenter;
        private readonly RaylibNavMeshPresentationRenderer _navMeshPresentationRenderer;
        private readonly Ludots.Core.Presentation.Navigation.NavMeshPresentationBuffer _navMeshPresentationBuffer;
        private readonly RaylibPrimitiveRenderer _primitiveRenderer;
        private readonly RaylibDebugDrawRenderer _debugDrawRenderer;
        private readonly RaylibBenchmarkRenderService? _benchmarkRenderer;
        private readonly GlobalFieldVisualBuffer? _globalFieldVisualBuffer;
        private readonly ScreenOverlayBuffer? _screenOverlayBuffer;
        private readonly PresentationTimingDiagnostics? _presentationTiming;

        private readonly RaylibFramePass[] _lastExecutedPasses = new RaylibFramePass[MaxPassesPerFrame];
        private int _lastExecutedPassCount;
        private long _mode3DStartTicks;

        public RaylibFrameRenderer(
            GameEngine engine,
            UIRoot uiRoot,
            SkiaUiRenderer skiaRenderer,
            RaylibOverlayCompositor overlayCompositor,
            RaylibBrowserLayerRenderer browserLayerRenderer,
            RaylibRenderEnvironmentRenderer environmentRenderer,
            RaylibSkyEnvironment skyEnvironment,
            RaylibWaterPass waterPass,
            RaylibFrameLighting frameLighting,
            RaylibTerrainRenderer terrainRenderer,
            RaylibVisualHeightmapRenderer visualHeightmapRenderer,
            RaylibFieldRenderPresenter fieldRenderPresenter,
            RaylibNavMeshPresentationRenderer navMeshPresentationRenderer,
            Ludots.Core.Presentation.Navigation.NavMeshPresentationBuffer navMeshPresentationBuffer,
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
            _skyEnvironment = skyEnvironment ?? throw new ArgumentNullException(nameof(skyEnvironment));
            _waterPass = waterPass ?? throw new ArgumentNullException(nameof(waterPass));
            _frameLighting = frameLighting ?? throw new ArgumentNullException(nameof(frameLighting));
            _terrainRenderer = terrainRenderer ?? throw new ArgumentNullException(nameof(terrainRenderer));
            _visualHeightmapRenderer = visualHeightmapRenderer ?? throw new ArgumentNullException(nameof(visualHeightmapRenderer));
            _fieldRenderPresenter = fieldRenderPresenter ?? throw new ArgumentNullException(nameof(fieldRenderPresenter));
            _navMeshPresentationRenderer = navMeshPresentationRenderer ?? throw new ArgumentNullException(nameof(navMeshPresentationRenderer));
            _navMeshPresentationBuffer = navMeshPresentationBuffer ?? throw new ArgumentNullException(nameof(navMeshPresentationBuffer));
            _primitiveRenderer = primitiveRenderer ?? throw new ArgumentNullException(nameof(primitiveRenderer));
            _debugDrawRenderer = debugDrawRenderer ?? throw new ArgumentNullException(nameof(debugDrawRenderer));
            _benchmarkRenderer = benchmarkRenderer;
            _globalFieldVisualBuffer = globalFieldVisualBuffer;
            _screenOverlayBuffer = screenOverlayBuffer;
            _presentationTiming = presentationTiming;
        }

        public int LastExecutedPassCount => _lastExecutedPassCount;

        internal ReadOnlySpan<RaylibFramePass> LastExecutedPasses => _lastExecutedPasses.AsSpan(0, _lastExecutedPassCount);

        public RaylibRenderFrameResult RenderFrame(in RaylibRenderFrame frame)
        {
            bool emptyBufferWarned = frame.EmptyBufferWarned;
            bool drawingActive = false;
            bool worldFrameActive = false;
            bool mode3DActive = false;
            bool frameCompleted = false;
            _lastExecutedPassCount = 0;

            try
            {
                long beginDrawingStart = Stopwatch.GetTimestamp();
                Rl.BeginDrawing();
                drawingActive = true;
                _presentationTiming?.ObserveBeginDrawing(ElapsedMs(beginDrawingStart));
                Restore3DDepthState();

                RaylibFrameWaterFrame waterFrame = PrepareFrameEnvironment(in frame);
                Span<RaylibFramePass> plan = stackalloc RaylibFramePass[MaxPassesPerFrame];
                int passCount = BuildPassPlan(BuildPlanInput(in frame, waterFrame), plan);

                for (int i = 0; i < passCount; i++)
                {
                    RaylibFramePass pass = plan[i];
                    _lastExecutedPasses[_lastExecutedPassCount++] = pass;
                    switch (pass)
                    {
                        case RaylibFramePass.Clear:
                            if (!waterFrame.PostProcessWorldFrame)
                            {
                                Rl.ClearBackground(waterFrame.ClearColor);
                            }

                            break;
                        case RaylibFramePass.BeginWorldTexture:
                            _environmentRenderer.BeginWorldFrame(frame.Width, frame.Height, waterFrame.ClearColor);
                            worldFrameActive = true;
                            break;
                        case RaylibFramePass.WaterReflection:
                            RenderWaterReflection(in frame, in waterFrame);
                            break;
                        case RaylibFramePass.WaterRefraction:
                            RenderWaterRefraction(in frame, in waterFrame);
                            break;
                        case RaylibFramePass.ShadowDepth:
                            RenderShadowDepth(in frame);
                            break;
                        case RaylibFramePass.BeginWorld3D:
                        {
                            long mode3DStart = Stopwatch.GetTimestamp();
                            Restore3DDepthState();
                            CameraRenderState3D activeCameraState = frame.ActiveCameraState;
                            BeginCoreMode3D(frame.ActiveCamera, in activeCameraState);
                            Restore3DDepthState();
                            mode3DActive = true;
                            _mode3DStartTicks = mode3DStart;
                            break;
                        }

                        case RaylibFramePass.Skybox:
                            _skyEnvironment.Draw(frame.ActiveCamera, frame.ActiveCameraState);
                            Restore3DDepthState();
                            break;
                        case RaylibFramePass.DebugGuides:
                            DrawDebugGuides(in frame);
                            break;
                        case RaylibFramePass.Terrain:
                            DrawTerrain(in frame, in waterFrame);
                            break;
                        case RaylibFramePass.NavMeshOverlay:
                            DrawNavMeshOverlay();
                            break;
                        case RaylibFramePass.GlobalField:
                            DrawGlobalFields(in frame);
                            break;
                        case RaylibFramePass.BenchmarkScene:
                            _ = _benchmarkRenderer!.Draw(frame.ActiveCamera);
                            break;
                        case RaylibFramePass.PrimitiveVisuals:
                            emptyBufferWarned = DrawPrimitiveVisuals(in frame, emptyBufferWarned);
                            break;
                        case RaylibFramePass.GroundOverlay:
                            DrawGroundOverlays(frame.CleanPerformanceMode);
                            break;
                        case RaylibFramePass.SplineRibbon:
                            DrawSplineRibbons(frame.CleanPerformanceMode);
                            break;
                        case RaylibFramePass.TrailMeshes:
                            DrawTrailMeshes(frame.CleanPerformanceMode);
                            break;
                        case RaylibFramePass.DebugDraw:
                            DrawDebugCommands(in frame);
                            break;
                        case RaylibFramePass.EndWorld3D:
                        {
                            EndCoreMode3D();
                            mode3DActive = false;
                            _presentationTiming?.ObserveMode3D(ElapsedMs(_mode3DStartTicks));
                            break;
                        }

                        case RaylibFramePass.PostProcessComposite:
                            _environmentRenderer.EndWorldFrame(frame.TimeSeconds);
                            worldFrameActive = false;
                            break;
                        case RaylibFramePass.BrowserLayer:
                            _browserLayerRenderer.Render(_uiRoot.Scene, frame.Width, frame.Height);
                            break;
                        case RaylibFramePass.OverlayComposite:
                            DrawUiLayers(in frame);
                            break;
                        default:
                            throw new InvalidOperationException($"Unhandled Raylib frame pass {pass}.");
                    }
                }

                ObserveSkippedPassTimings(plan, passCount);
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

        /// <summary>
        /// 计划层省略的可选 pass 与旧宿主一样每帧补零观测，避免诊断序列保留上一帧数值。
        /// </summary>
        private void ObserveSkippedPassTimings(Span<RaylibFramePass> plan, int passCount)
        {
            if (_presentationTiming == null)
            {
                return;
            }

            if (!WasPlanned(plan, passCount, RaylibFramePass.Terrain))
            {
                _presentationTiming.ObserveTerrain(0d, 0d, 0, 0);
            }

            if (!WasPlanned(plan, passCount, RaylibFramePass.GlobalField))
            {
                _presentationTiming.ObserveGlobalFieldRender(0d, 0, 0, 0, 0);
            }

            if (!WasPlanned(plan, passCount, RaylibFramePass.PrimitiveVisuals))
            {
                _presentationTiming.ObservePrimitiveRender(0d, 0, 0);
            }

            if (!WasPlanned(plan, passCount, RaylibFramePass.DebugDraw))
            {
                _presentationTiming.ObserveDebugDrawRender(0d, 0);
            }
        }

        private static bool WasPlanned(Span<RaylibFramePass> plan, int count, RaylibFramePass pass)
        {
            for (int i = 0; i < count; i++)
            {
                if (plan[i] == pass)
                {
                    return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            _shadowMap?.Dispose();
            _shadowMap = null;
        }

        private readonly record struct RaylibFrameWaterFrame(
            bool WaterOnVisualHeightmap,
            bool WaterOnVertexMap,
            bool WaterFboEnabled,
            bool PostProcessWorldFrame,
            Color ClearColor);

        private RaylibFrameWaterFrame PrepareFrameEnvironment(in RaylibRenderFrame frame)
        {
            string? activeMapId = _engine.CurrentMapSession?.MapId.Value;
            _skyEnvironment.EnsureActiveForMap(activeMapId);
            _waterPass.EnsureActiveForMap(activeMapId);
            _visualHeightmapRenderer.EnsureAlbedoActiveForMap(activeMapId);
            Color clearColor = _skyEnvironment.IsActive
                ? _skyEnvironment.ResolveClearColor()
                : (frame.ActiveMapRequestsDeepBackground
                    ? new Color(6, 10, 16, 255)
                    : new Color(0, 0, 0, 255));

            if (_skyEnvironment.HasDayPhase)
            {
                _frameLighting.SetDayPhase(_skyEnvironment.DayPhase01);
            }
            else
            {
                _frameLighting.Evaluate();
            }

            _shadowActive = frame.RenderDebug.DrawShadows;
            if (_shadowActive && _shadowMap == null)
            {
                _shadowMap = new RaylibDirectionalShadowMap(_environmentRenderer.Config.Shadow);
            }

            RaylibDirectionalShadowMap? frameShadow = _shadowActive ? _shadowMap : null;
            _terrainRenderer.ApplyFrameLighting(_frameLighting, frameShadow, TerrainShadowTexelWorld);
            _visualHeightmapRenderer.ApplyFrameLighting(_frameLighting, frameShadow, TerrainShadowTexelWorld);
            _primitiveRenderer.ApplyFrameLighting(_frameLighting, frame.ActiveCamera.position, frameShadow, PrimitiveShadowTexelWorld);
            _primitiveRenderer.DrawSurfaceWireBoxes = frame.DrawDebugDraw;

            bool waterOnVisualHeightmap = _waterPass.IsActive &&
                                          frame.DrawTerrain &&
                                          frame.DrawVisualHeightmap &&
                                          frame.HasVisualHeightmap;
            bool waterOnVertexMap = _waterPass.IsActive &&
                                    frame.DrawTerrain &&
                                    !waterOnVisualHeightmap &&
                                    _engine.VertexMap != null;
            bool waterFboEnabled = waterOnVisualHeightmap || waterOnVertexMap;
            return new RaylibFrameWaterFrame(
                waterOnVisualHeightmap,
                waterOnVertexMap,
                waterFboEnabled,
                !waterFboEnabled,
                clearColor);
        }

        private RaylibFramePassPlanInput BuildPlanInput(in RaylibRenderFrame frame, in RaylibFrameWaterFrame waterFrame)
        {
            return new RaylibFramePassPlanInput(
                DrawDebugGuides: frame.DrawDebugDraw &&
                    !(frame.DrawVisualHeightmap && frame.HasVisualHeightmap) &&
                    !frame.HostDebugGuidesSuppressed,
                DrawTerrain: frame.DrawTerrain,
                DrawVisualHeightmap: frame.DrawVisualHeightmap,
                WaterEnabled: waterFrame.WaterFboEnabled,
                HasShadowFrame: _shadowActive,
                HasGlobalFieldBuffer: _globalFieldVisualBuffer != null,
                DrawFieldOverlays: frame.DrawFieldOverlays,
                HasBenchmarkRenderer: _benchmarkRenderer != null,
                DrawPrimitives: frame.DrawPrimitives,
                HasGroundOverlays: true,
                HasSplineRibbons: true,
                HasTrailMeshes: true,
                DrawNavMeshOverlay: frame.DrawNavMeshOverlay,
                DrawDebugDraw: frame.DrawDebugDraw,
                DrawSkiaUi: frame.DrawSkiaUi,
                DrawEnvironment: _skyEnvironment.IsActive,
                UsePostProcess: _environmentRenderer.Config.PostProcess.Enabled);
        }

        public static int BuildPassPlan(in RaylibFramePassPlanInput input, Span<RaylibFramePass> output)
        {
            int count = 0;
            Add(output, ref count, RaylibFramePass.Clear);
            if (input.WaterEnabled)
            {
                Add(output, ref count, RaylibFramePass.WaterReflection);
                Add(output, ref count, RaylibFramePass.WaterRefraction);
            }

            if (input.HasShadowFrame)
            {
                Add(output, ref count, RaylibFramePass.ShadowDepth);
            }

            // 后处理 RT 必须在水面 RT pass 之后开启：水面 EndTextureMode 会把渲染目标切回默认帧缓冲，
            // 先开后处理 RT 再画水面会丢失 RT 绑定（旧宿主水面帧直接熄掉调色的根因）。
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

            if (input.DrawNavMeshOverlay)
            {
                Add(output, ref count, RaylibFramePass.NavMeshOverlay);
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

            if (input.HasSplineRibbons)
            {
                Add(output, ref count, RaylibFramePass.SplineRibbon);
            }

            if (input.HasTrailMeshes)
            {
                Add(output, ref count, RaylibFramePass.TrailMeshes);
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

        private void RenderWaterReflection(in RaylibRenderFrame frame, in RaylibFrameWaterFrame waterFrame)
        {
            _waterPass.EnsureRenderTargets(frame.Width, frame.Height);
            _waterPass.Advance(frame.DeltaSeconds);

            Camera3D reflectionCamera = _waterPass.BuildReflectionCamera(frame.ActiveCamera);
            _waterPass.BeginReflectionPass(waterFrame.ClearColor);
            Restore3DDepthState();
            BeginCoreMode3D(reflectionCamera, frame.ActiveCameraState);
            Restore3DDepthState();
            try
            {
                if (_skyEnvironment.IsActive)
                {
                    _skyEnvironment.Draw(reflectionCamera, frame.ActiveCameraState);
                    Restore3DDepthState();
                }

                if (waterFrame.WaterOnVisualHeightmap &&
                    _engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap? vhReflect) &&
                    vhReflect is IVisualHeightmapRenderSource reflectSource)
                {
                    _visualHeightmapRenderer.AbsoluteColorSeaLevelCm = _waterPass.WaterPlaneY * 100f;
                    _visualHeightmapRenderer.AbsoluteColorPeakSpanCm = reflectSource.RenderProfile.AbsoluteColorPeakSpanCm;
                    _visualHeightmapRenderer.Render(reflectSource, reflectionCamera);
                }
                else
                {
                    _terrainRenderer.RenderTerrainOnly(TerrainSource(), reflectionCamera);
                }
            }
            finally
            {
                EndCoreMode3D();
                _waterPass.EndPass();
            }
        }

        private void RenderWaterRefraction(in RaylibRenderFrame frame, in RaylibFrameWaterFrame waterFrame)
        {
            _waterPass.BeginRefractionPass(waterFrame.ClearColor);
            Restore3DDepthState();
            BeginCoreMode3D(frame.ActiveCamera, frame.ActiveCameraState);
            Restore3DDepthState();
            try
            {
                if (_skyEnvironment.IsActive)
                {
                    _skyEnvironment.Draw(frame.ActiveCamera, frame.ActiveCameraState);
                    Restore3DDepthState();
                }

                if (waterFrame.WaterOnVisualHeightmap &&
                    _engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap? vhRefract) &&
                    vhRefract is IVisualHeightmapRenderSource refractSource)
                {
                    _visualHeightmapRenderer.AbsoluteColorSeaLevelCm = _waterPass.WaterPlaneY * 100f;
                    _visualHeightmapRenderer.AbsoluteColorPeakSpanCm = refractSource.RenderProfile.AbsoluteColorPeakSpanCm;
                    _visualHeightmapRenderer.Render(refractSource, frame.ActiveCamera);
                }
                else
                {
                    _terrainRenderer.RenderTerrainOnly(TerrainSource(), frame.ActiveCamera);
                }
            }
            finally
            {
                EndCoreMode3D();
                _waterPass.EndPass();
            }
        }

        private VertexMapTerrainChunkMeshSource? _terrainSource;
        private RaylibDirectionalShadowMap? _shadowMap;
        private bool _shadowActive;

        private readonly float _shadowSceneRadiusMeters = RaylibHostLoop.ReadEnvFloatOrDefault("LUDOTS_RAYLIB_SHADOW_SCENE_RADIUS", 48f);
        private const float PrimitiveShadowTexelWorld = 0.04f;
        private const float TerrainShadowTexelWorld = 0.08f;

        private Ludots.Platform.Abstractions.ITerrainChunkMeshSource TerrainSource()
        {
            _terrainSource ??= new VertexMapTerrainChunkMeshSource(null);
            if (!ReferenceEquals(_terrainSource.Map, _engine.VertexMap))
            {
                _terrainSource = new VertexMapTerrainChunkMeshSource(_engine.VertexMap);
            }

            return _terrainSource;
        }

        private void RenderShadowDepth(in RaylibRenderFrame frame)
        {
            RaylibDirectionalShadowMap shadow = _shadowMap!;
            shadow.BeginFrame(_frameLighting.SunDirectionToward, frame.ActiveCamera.target, _shadowSceneRadiusMeters);
            try
            {
                if (frame.DrawVisualHeightmap &&
                    _engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap? vhCaster) &&
                    vhCaster is IVisualHeightmapRenderSource heightmapCaster)
                {
                    _visualHeightmapRenderer.RenderShadow(heightmapCaster, frame.ActiveCamera, shadow);
                }
                else if (frame.DrawTerrain)
                {
                    _terrainRenderer.RenderTerrainShadow(TerrainSource(), frame.ActiveCamera, shadow);
                }

                _benchmarkRenderer?.DrawShadow(frame.ActiveCamera, shadow);

                if (_engine.TryGetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer, out PrimitiveDrawBuffer? draw) &&
                    _engine.TryGetService(CoreServiceKeys.PresentationMeshAssetRegistry, out MeshAssetRegistry? meshes))
                {
                    _primitiveRenderer.DrawShadow(
                        draw,
                        shadow,
                        meshes,
                        frame.ActiveCamera,
                        frame.RenderDebug.AcceptanceScaleMultiplier);
                }

                SkinnedVisualBatchBuffer? skinnedBatch = _engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer);
                if (skinnedBatch != null &&
                    _engine.TryGetService(CoreServiceKeys.PresentationMeshAssetRegistry, out MeshAssetRegistry? skinMeshes))
                {
                    _primitiveRenderer.DrawShadow(
                        skinnedBatch,
                        shadow,
                        skinMeshes,
                        frame.RenderDebug.AcceptanceScaleMultiplier);
                }
            }
            finally
            {
                shadow.EndFrame();
            }
        }

        private void DrawDebugGuides(in RaylibRenderFrame frame)
        {
            DrawInfiniteGrid(frame.ActiveCamera.target, 300, 1.0f, 10);

            Vector3 target = frame.ActiveCamera.target;
            Rl.DrawLine3D(target, target + new Vector3(2.0f, 0, 0), Color.RED);
            Rl.DrawLine3D(target, target + new Vector3(0, 0, 2.0f), Color.BLUE);
            Rl.DrawLine3D(target, target + new Vector3(0, 2.0f, 0), Color.GREEN);
        }

        private void DrawTerrain(in RaylibRenderFrame frame, in RaylibFrameWaterFrame waterFrame)
        {
            if (frame.DrawVisualHeightmap &&
                _engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap? visualHeightmapForTerrain) &&
                visualHeightmapForTerrain is IVisualHeightmapRenderSource visualTerrainSource)
            {
                long terrainStart = Stopwatch.GetTimestamp();
                if (waterFrame.WaterOnVisualHeightmap)
                {
                    _visualHeightmapRenderer.AbsoluteColorSeaLevelCm = _waterPass.WaterPlaneY * 100f;
                    _visualHeightmapRenderer.AbsoluteColorPeakSpanCm = visualTerrainSource.RenderProfile.AbsoluteColorPeakSpanCm;
                }
                else
                {
                    _visualHeightmapRenderer.AbsoluteColorSeaLevelCm = null;
                }

                _visualHeightmapRenderer.Render(visualTerrainSource, frame.ActiveCamera);

                if (waterFrame.WaterOnVisualHeightmap)
                {
                    _terrainRenderer.EnsureWaterShadersReady();
                    _terrainRenderer.BindReflectiveWater(_waterPass);
                    // Half-extent covers the island board (~1.28km); plane follows camera target XZ.
                    _terrainRenderer.DrawReflectiveOceanPlane(
                        _waterPass.WaterPlaneY,
                        halfExtentMeters: 900f,
                        frame.ActiveCamera);
                }
                else
                {
                    _terrainRenderer.ClearReflectiveWater();
                }

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
                if (waterFrame.WaterOnVertexMap)
                {
                    _terrainRenderer.BindReflectiveWater(_waterPass);
                }
                else
                {
                    _terrainRenderer.ClearReflectiveWater();
                }

                _terrainRenderer.Render(TerrainSource(), frame.ActiveCamera);
                _presentationTiming?.ObserveTerrain(
                    ElapsedMs(terrainStart),
                    _terrainRenderer.ChunkBuildMsLastFrame,
                    _terrainRenderer.DrawnChunkCountLastFrame,
                    _terrainRenderer.BuiltChunkCountLastFrame);
                return;
            }

            _presentationTiming?.ObserveTerrain(0d, 0d, 0, 0);
        }

        private void DrawNavMeshOverlay()
        {
            _navMeshPresentationRenderer.Draw(_navMeshPresentationBuffer);
            _screenOverlayBuffer?.AddText(
                10,
                40,
                _navMeshPresentationBuffer.FormatMetadataLine(),
                14,
                new Vector4(1f, 0.92f, 0.5f, 1f));
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

        private bool DrawPrimitiveVisuals(in RaylibRenderFrame frame, bool emptyBufferWarned)
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
                if (_engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap? visualHeightmap) &&
                    visualHeightmap != null)
                {
                    _visualHeightmapRenderer.BindStampHeightSampleSource(visualHeightmap);
                    _terrainRenderer.BindStampHeightSampleSource(visualHeightmap);
                }

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

        private void DrawTrailMeshes(bool cleanPerformanceMode)
        {
            if (!cleanPerformanceMode &&
                _engine.TryGetService(CoreServiceKeys.TrailMeshBuffer, out TrailMeshBuffer trails) &&
                trails.Count > 0)
            {
                RaylibTrailMeshRenderer.DrawTrailMeshes(trails);
            }
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
                matrix.m12, matrix.m13, matrix.m14, matrix.m15,
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
