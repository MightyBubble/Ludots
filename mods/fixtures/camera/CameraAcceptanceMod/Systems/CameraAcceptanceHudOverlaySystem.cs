using System;
using System.Collections.Generic;
using System.Numerics;
using System.Diagnostics;
using Arch.System;
using CameraAcceptanceMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CameraAcceptanceMod.Systems
{
    internal sealed class CameraAcceptanceHudOverlaySystem : ISystem<float>
    {
        private const float AverageGlyphWidthPx = 6.25f;

        private readonly GameEngine _engine;
        private float _smoothedFrameMs = 16.67f;

        public CameraAcceptanceHudOverlaySystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            long start = Stopwatch.GetTimestamp();
            string? mapId = _engine.CurrentMapSession?.MapId.Value;
            if (!CameraAcceptanceIds.IsAcceptanceMap(mapId))
            {
                Observe(start);
                return;
            }

            if (_engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is not ScreenOverlayBuffer overlay)
            {
                Observe(start);
                return;
            }

            if (_engine.GetService(CameraAcceptanceServiceKeys.DiagnosticsState) is not CameraAcceptanceDiagnosticsState diagnostics ||
                _engine.GetService(CoreServiceKeys.RenderDebugState) is not RenderDebugState renderDebug)
            {
                Observe(start);
                return;
            }

            if (!diagnostics.HudEnabled)
            {
                Observe(start);
                return;
            }

            float frameMs = dt > 0f ? dt * 1000f : 0f;
            if (frameMs > 0f)
            {
                _smoothedFrameMs = (_smoothedFrameMs * 0.88f) + (frameMs * 0.12f);
            }

            float fps = _smoothedFrameMs > 0.001f ? 1000f / _smoothedFrameMs : 0f;
            string fpsLine = $"Camera Acceptance | FPS={fps:F1} | Frame={_smoothedFrameMs:F2}ms";
            var lines = new List<string>(8)
            {
                fpsLine,
                $"F6 Panel[{OnOff(renderDebug.DrawSkiaUi)}]  F7 HUD[{OnOff(diagnostics.HudEnabled)}]  F8 Select[{OnOff(diagnostics.TextEnabled)}]"
            };

            if (string.Equals(mapId, CameraAcceptanceIds.HotpathMapId, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"Build panel={diagnostics.PanelSyncMs:F2}ms  diagHud={diagnostics.HudBuildMs:F2}ms  select={diagnostics.TextBuildMs:F2}ms  bars={diagnostics.HotpathBarBuildMs:F2}ms  hudText={diagnostics.HotpathHudTextBuildMs:F2}ms  prims={diagnostics.HotpathPrimitiveBuildMs:F2}ms");
                lines.Add($"F9 Bars[{OnOff(diagnostics.HotpathBarsEnabled)}]  F10 HudText[{OnOff(diagnostics.HotpathHudTextEnabled)}]  F11 Terrain[{OnOff(renderDebug.DrawTerrain)}]  F12 Prim[{OnOff(renderDebug.DrawPrimitives)}]  C Crowd[{OnOff(diagnostics.HotpathCullCrowdEnabled)}]");
                lines.Add($"Hotpath crowd={diagnostics.HotpathCrowdCount}  visible={diagnostics.HotpathVisibleCrowdCount}  bars={diagnostics.HotpathBarItemCount}  hudText={diagnostics.HotpathHudTextItemCount}  prims={diagnostics.HotpathPrimitiveItemCount}  select={diagnostics.HotpathSelectionLabelCount}");
            }
            else
            {
                lines.Add($"Build panel={diagnostics.PanelSyncMs:F2}ms  hud={diagnostics.HudBuildMs:F2}ms  text={diagnostics.TextBuildMs:F2}ms");
            }

            if (_engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics) is PresentationTimingDiagnostics timings)
            {
                lines.Add($"Adapter uiIn={timings.UiInputMs:F2}ms  uiRender={timings.UiRenderMs:F2}ms  uiUpload={timings.UiUploadMs:F2}ms");
                lines.Add($"Adapter overlayDraw={timings.ScreenOverlayDrawMs:F2}ms");
                lines.Add($"Core cull={timings.CameraCullingMs:F2}ms  vis={timings.VisibleEntitiesLastFrame}  cam={timings.CameraPresenterMs:F2}ms  hudProj={timings.WorldHudProjectionMs:F2}ms");
                lines.Add($"Terrain render={timings.TerrainRenderMs:F2}ms  build={timings.TerrainChunkBuildMs:F2}ms  chunks={timings.TerrainChunksDrawnLastFrame}  built={timings.TerrainChunksBuiltLastFrame}");
                lines.Add($"Primitive draw={timings.PrimitiveRenderMs:F2}ms  instances={timings.PrimitiveInstancesLastFrame}  batches={timings.PrimitiveBatchesLastFrame}");
            }

            int x = 16;
            int y = 16;
            if (_engine.GetService(CoreServiceKeys.ViewController) is IViewController view)
            {
                int maxLength = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Length > maxLength)
                    {
                        maxLength = lines[i].Length;
                    }
                }

                int estimatedWidth = (int)MathF.Ceiling((maxLength * AverageGlyphWidthPx) + 24f);
                x = Math.Max(16, (int)view.Resolution.X - estimatedWidth);
            }

            for (int i = 0; i < lines.Count; i++)
            {
                int fontSize = i == 0 ? 16 : 14;
                overlay.AddText(x, y + (i * 18), lines[i], fontSize, new Vector4(0.96f, 0.98f, 1f, 1f));
            }

            if (string.Equals(mapId, CameraAcceptanceIds.ProjectionMapId, StringComparison.OrdinalIgnoreCase))
            {
                string batchLine = $"Spawn Batch={CameraAcceptanceRuntime.ResolveProjectionSpawnCount(_engine)} | Q/E +/-{CameraAcceptanceIds.ProjectionSpawnCountStep}";
                int batchX = x;
                if (_engine.GetService(CoreServiceKeys.ViewController) is IViewController batchView)
                {
                    int estimatedWidth = (int)MathF.Ceiling((batchLine.Length * AverageGlyphWidthPx) + 24f);
                    batchX = Math.Max(16, (int)batchView.Resolution.X - estimatedWidth);
                }

                overlay.AddText(batchX, y + (lines.Count * 18), batchLine, 14, new Vector4(0.76f, 0.83f, 0.92f, 0.96f));
            }

            Observe(start);
        }

        private void Observe(long startTicks)
        {
            if (_engine.GetService(CameraAcceptanceServiceKeys.DiagnosticsState) is CameraAcceptanceDiagnosticsState diagnostics)
            {
                diagnostics.ObserveHudBuild((Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency);
            }
        }

        private static string OnOff(bool value) => value ? "ON" : "OFF";
    }
}
