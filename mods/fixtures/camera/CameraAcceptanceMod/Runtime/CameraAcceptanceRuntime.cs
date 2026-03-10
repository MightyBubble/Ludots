using System.Threading.Tasks;
using System.Numerics;
using Arch.Core;
using CameraAcceptanceMod.UI;
using CoreInputMod.Triggers;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics;
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

            ApplySelectionProfileOwnership(engine, engine.CurrentMapSession?.MapId.Value);
            RefreshPanel(engine);

            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            var mapId = context.Get(CoreServiceKeys.MapId);
            if (CameraAcceptanceIds.IsAcceptanceMap(mapId.Value))
            {
                ClearPanelIfOwned(context);
                ClearSelectionProfileIfOwned(context.GetEngine());
            }

            return Task.CompletedTask;
        }

        public void RefreshPanel(GameEngine engine)
        {
            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            ApplySelectionProfileOwnership(engine, activeMapId);
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

        private void HandleSelectionConfirmed(GameEngine engine, in WorldCmInt2 worldCm, Entity resolved)
        {
            string? mapId = engine.CurrentMapSession?.MapId.Value;
            if (string.Equals(mapId, CameraAcceptanceIds.ProjectionMapId, System.StringComparison.OrdinalIgnoreCase))
            {
                if (!engine.World.IsAlive(resolved))
                {
                    EmitCueMarker(engine, worldCm);
                }
            }
        }

        private static void ApplySelectionProfileOwnership(GameEngine engine, string? mapId)
        {
            if (string.Equals(mapId, CameraAcceptanceIds.ProjectionMapId, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mapId, CameraAcceptanceIds.FollowMapId, System.StringComparison.OrdinalIgnoreCase))
            {
                engine.GlobalContext[CoreServiceKeys.ActiveSelectionProfileId.Name] = CameraAcceptanceIds.SelectionProfileId;
                return;
            }

            ClearSelectionProfileIfOwned(engine);
        }

        private static void ClearSelectionProfileIfOwned(GameEngine? engine)
        {
            if (engine == null)
            {
                return;
            }

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.ActiveSelectionProfileId.Name, out var value) &&
                value is string profileId &&
                string.Equals(profileId, CameraAcceptanceIds.SelectionProfileId, System.StringComparison.Ordinal))
            {
                engine.GlobalContext.Remove(CoreServiceKeys.ActiveSelectionProfileId.Name);
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

    }
}
