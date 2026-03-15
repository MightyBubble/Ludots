using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using CameraAcceptanceMod.UI;
using CoreInputMod.Triggers;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Runtime;

namespace CameraAcceptanceMod.Runtime
{
    internal sealed class CameraAcceptanceRuntime
    {
        private const float TwoPiRadians = 6.2831855f;
        private const float GoldenAngleRadians = 2.3999631f;
        private const float ProjectionScatterSpacingCm = 120f;
        private const float ProjectionScatterJitterCm = 42f;

        private CameraAcceptancePanelController? _panelController;
        private bool _selectionCallbacksInstalled;
        private int _cueMarkerPrefabId;
        private string _lastConfiguredMapId = string.Empty;

        internal static void InitializeProjectionSpawnCount(GameEngine engine)
        {
            if (!engine.GlobalContext.ContainsKey(CameraAcceptanceIds.ProjectionSpawnCountKey))
            {
                engine.GlobalContext[CameraAcceptanceIds.ProjectionSpawnCountKey] = CameraAcceptanceIds.ProjectionSpawnCountDefault;
            }
        }

        internal static int ResolveProjectionSpawnCount(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(CameraAcceptanceIds.ProjectionSpawnCountKey, out var value) &&
                   value is int count &&
                   count >= 0
                ? count
                : CameraAcceptanceIds.ProjectionSpawnCountDefault;
        }

        internal static int AdjustProjectionSpawnCount(GameEngine engine, int delta)
        {
            int next = ResolveProjectionSpawnCount(engine) + delta;
            if (next < 0)
            {
                next = 0;
            }

            engine.GlobalContext[CameraAcceptanceIds.ProjectionSpawnCountKey] = next;
            return next;
        }

        public void InstallSelectionCallbacks(GameEngine engine)
        {
            if (_selectionCallbacksInstalled)
            {
                return;
            }

            if (!engine.GlobalContext.TryGetValue(InstallCoreInputOnGameStartTrigger.EntitySelectionCallbacksKey, out var callbacksObj) ||
                callbacksObj is not System.Collections.Generic.List<System.Action<WorldCmInt2, Entity>> callbacks)
            {
                throw new System.InvalidOperationException(
                    "CameraAcceptanceMod requires CoreInputMod entity selection callbacks to be installed before GameStart handlers run.");
            }

            callbacks.Add((worldCm, entity) => HandleSelectionConfirmed(engine, worldCm, entity));
            _selectionCallbacksInstalled = true;
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            ConfigureRenderDefaultsForMap(engine);
            RefreshPanel(engine);

            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            var mapId = context.Get(CoreServiceKeys.MapId);
            if (CameraAcceptanceIds.IsAcceptanceMap(mapId.Value))
            {
                if (string.Equals(_lastConfiguredMapId, mapId.Value, System.StringComparison.OrdinalIgnoreCase))
                {
                    _lastConfiguredMapId = string.Empty;
                }

                ClearPanelIfOwned(context);
            }

            return Task.CompletedTask;
        }

        private CameraAcceptancePanelController EnsurePanelController(GameEngine engine)
        {
            if (_panelController != null)
            {
                return _panelController;
            }

            var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _panelController = new CameraAcceptancePanelController(textMeasurer, imageSizeProvider);
            return _panelController;
        }

        public void RefreshPanel(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            if (engine.GetService(CoreServiceKeys.RenderDebugState) is RenderDebugState renderDebug &&
                !renderDebug.DrawSkiaUi)
            {
                ClearPanelIfOwned(engine);
                return;
            }

            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            if (!CameraAcceptanceIds.IsAcceptanceMap(activeMapId))
            {
                ClearPanelIfOwned(engine);
                return;
            }

            var panelController = EnsurePanelController(engine);
            panelController.MountOrSync(root, engine);
            if (engine.GetService(CameraAcceptanceServiceKeys.DiagnosticsState) is CameraAcceptanceDiagnosticsState diagnostics)
            {
                diagnostics.ObservePanelUpdate(
                    panelController.LastUpdateStats,
                    panelController.LastUpdateMetrics,
                    panelController.LastSelectionRowsTouched,
                    panelController.RowPoolSize,
                    panelController.FullRecomposeCount,
                    panelController.IncrementalPatchCount);
            }
        }

        private void ClearPanelIfOwned(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return;
            }

            ClearPanelIfOwned(engine);
        }

        private void ClearPanelIfOwned(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            _panelController?.ClearIfOwned(root);
        }

        private void ConfigureRenderDefaultsForMap(GameEngine engine)
        {
            string? mapId = engine.CurrentMapSession?.MapId.Value;
            if (string.IsNullOrWhiteSpace(mapId) ||
                !CameraAcceptanceIds.IsAcceptanceMap(mapId) ||
                string.Equals(_lastConfiguredMapId, mapId, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (engine.GetService(CoreServiceKeys.RenderDebugState) is not RenderDebugState renderDebug)
            {
                return;
            }

            bool isHotpathMap = string.Equals(mapId, CameraAcceptanceIds.HotpathMapId, System.StringComparison.OrdinalIgnoreCase);
            renderDebug.DrawSkiaUi = true;
            renderDebug.DrawPrimitives = true;
            renderDebug.DrawTerrain = !isHotpathMap;
            renderDebug.DrawDebugDraw = !isHotpathMap;
            _lastConfiguredMapId = mapId;
        }

        private void HandleSelectionConfirmed(GameEngine engine, in WorldCmInt2 worldCm, Entity selectedEntity)
        {
            string? mapId = engine.CurrentMapSession?.MapId.Value;
            if (string.Equals(mapId, CameraAcceptanceIds.ProjectionMapId, System.StringComparison.OrdinalIgnoreCase))
            {
                if (engine.World.IsAlive(selectedEntity))
                {
                    return;
                }

                EnqueueProjectionSpawnBatch(engine, worldCm);
                EmitCueMarker(engine, worldCm);
                return;
            }

            if (string.Equals(mapId, CameraAcceptanceIds.BlendMapId, System.StringComparison.OrdinalIgnoreCase))
            {
                engine.GameSession.Camera.ActivateVirtualCamera(
                    ResolveActiveBlendCameraId(engine),
                    followTarget: new FixedPointFollowTarget(new Vector2(worldCm.X, worldCm.Y)),
                    snapToFollowTargetWhenAvailable: true,
                    resetRuntimeState: true);
            }
        }

        private void EmitCueMarker(GameEngine engine, in WorldCmInt2 worldCm)
        {
            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.PresentationCommandBuffer.Name, out var commandsObj) ||
                commandsObj is not PresentationCommandBuffer commands)
            {
                throw new System.InvalidOperationException("PresentationCommandBuffer is required for projection verification.");
            }

            commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.PlayOneShotPerformer,
                IdA = ResolveCueMarkerPrefabId(engine),
                Position = WorldUnits.WorldCmToVisualMeters(worldCm, yMeters: 0.15f),
                Param0 = new Vector4(0.15f, 0.88f, 1f, 1f),
                Param1 = 0.45f
            });
        }

        private static void EnqueueProjectionSpawnBatch(GameEngine engine, in WorldCmInt2 worldCm)
        {
            if (engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is not RuntimeEntitySpawnQueue spawnQueue)
            {
                throw new System.InvalidOperationException("RuntimeEntitySpawnQueue is required for projection verification.");
            }

            var bounds = engine.CurrentMapSession?.PrimaryBoard?.WorldSize.Bounds ?? engine.WorldSizeSpec.Bounds;
            int spawnCount = ResolveProjectionSpawnCount(engine);
            for (int i = 0; i < spawnCount; i++)
            {
                WorldCmInt2 spawnWorldCm = ResolveProjectionSpawnPosition(worldCm, i);
                spawnWorldCm = GroundRaycastUtil.ClampWorldCmToBounds(spawnWorldCm, bounds, out _);
                var request = new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = CameraAcceptanceIds.ProjectionSpawnTemplateId,
                    WorldPositionCm = Fix64Vec2.FromInt(spawnWorldCm.X, spawnWorldCm.Y),
                    MapId = engine.CurrentMapSession?.MapId ?? default,
                };

                if (!spawnQueue.TryEnqueue(request))
                {
                    throw new System.InvalidOperationException("Projection verification spawn queue is full.");
                }
            }
        }

        private static WorldCmInt2 ResolveProjectionSpawnPosition(in WorldCmInt2 center, int index)
        {
            if (index <= 0)
            {
                return center;
            }

            uint seed = Hash((uint)center.X) ^ RotateLeft(Hash((uint)center.Y), 13) ^ RotateLeft((uint)index * 0x9E3779B9u, 7);
            float baseAngle = Hash01(seed ^ 0xA511E9B3u) * TwoPiRadians;
            float jitterAngle = (Hash01(seed ^ 0x63D83595u) - 0.5f) * 0.42f;
            float ringRadius = ProjectionScatterSpacingCm * MathF.Sqrt(index);
            float jitterRadius = (Hash01(seed ^ 0xC2B2AE35u) - 0.5f) * ProjectionScatterJitterCm;
            float radius = MathF.Max(ProjectionScatterSpacingCm * 0.4f, ringRadius + jitterRadius);
            float angle = baseAngle + index * GoldenAngleRadians + jitterAngle;
            int x = center.X + (int)MathF.Round(MathF.Cos(angle) * radius);
            int y = center.Y + (int)MathF.Round(MathF.Sin(angle) * radius);
            return new WorldCmInt2(x, y);
        }

        private static uint Hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }

        private static uint RotateLeft(uint value, int amount)
        {
            return (value << amount) | (value >> (32 - amount));
        }

        private static float Hash01(uint seed)
        {
            return (Hash(seed) & 0x00FFFFFFu) / 16777216f;
        }

        private int ResolveCueMarkerPrefabId(GameEngine engine)
        {
            if (_cueMarkerPrefabId != 0)
            {
                return _cueMarkerPrefabId;
            }

            if (engine.GetService(CoreServiceKeys.PresentationPrefabRegistry) is not PrefabRegistry prefabs)
            {
                throw new System.InvalidOperationException("PresentationPrefabRegistry is required for projection verification.");
            }

            _cueMarkerPrefabId = prefabs.GetId("cue_marker");
            if (_cueMarkerPrefabId == 0)
            {
                throw new System.InvalidOperationException("Prefab 'cue_marker' is required for projection verification.");
            }

            return _cueMarkerPrefabId;
        }

        private static string ResolveActiveBlendCameraId(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(CameraAcceptanceIds.ActiveBlendCameraIdKey, out var value) &&
                   value is string cameraId &&
                   !string.IsNullOrWhiteSpace(cameraId)
                ? cameraId
                : CameraAcceptanceIds.BlendSmoothCameraId;
        }

        private sealed class FixedPointFollowTarget : ICameraFollowTarget
        {
            private readonly Vector2 _pointCm;

            public FixedPointFollowTarget(Vector2 pointCm)
            {
                _pointCm = pointCm;
            }

            public bool TryGetPosition(out Vector2 positionCm)
            {
                positionCm = _pointCm;
                return true;
            }
        }
    }
}
