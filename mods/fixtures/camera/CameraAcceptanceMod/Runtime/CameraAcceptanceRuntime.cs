using System.Threading.Tasks;
using System.Numerics;
using Arch.Core;
using CameraAcceptanceMod.UI;
using CoreInputMod.Triggers;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Scripting;
using Ludots.UI;

namespace CameraAcceptanceMod.Runtime
{
    internal sealed class CameraAcceptanceRuntime
    {
        private readonly CameraAcceptancePanelController _panelController = new();
        private bool _selectionCallbacksInstalled;
        private int _cueMarkerPrefabId;

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

            RefreshPanel(engine);

            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            var mapId = context.Get(CoreServiceKeys.MapId);
            if (CameraAcceptanceIds.IsAcceptanceMap(mapId.Value))
            {
                ClearPanelIfOwned(context);
            }

            return Task.CompletedTask;
        }

        public void RefreshPanel(GameEngine engine)
        {
            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            if (CameraAcceptanceIds.IsAcceptanceMap(activeMapId))
            {
                MountPanel(engine, activeMapId!);
            }
            else
            {
                ClearPanelIfOwned(engine);
            }
        }

        private void MountPanel(GameEngine engine, string activeMapId)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            root.MountScene(_panelController.BuildScene(engine, activeMapId));
            root.IsDirty = true;
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

            _panelController.ClearIfOwned(root);
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

                EnqueueProjectionSpawn(engine, worldCm);
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

        private static void EnqueueProjectionSpawn(GameEngine engine, in WorldCmInt2 worldCm)
        {
            if (engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is not RuntimeEntitySpawnQueue spawnQueue)
            {
                throw new System.InvalidOperationException("RuntimeEntitySpawnQueue is required for projection verification.");
            }

            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = CameraAcceptanceIds.ProjectionSpawnTemplateId,
                WorldPositionCm = Fix64Vec2.FromInt(worldCm.X, worldCm.Y),
                MapId = engine.CurrentMapSession?.MapId ?? default,
            };

            if (!spawnQueue.TryEnqueue(request))
            {
                throw new System.InvalidOperationException("Projection verification spawn queue is full.");
            }
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
