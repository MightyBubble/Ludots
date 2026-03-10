using System;
using System.Text;
using Arch.Core;
using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;

namespace CameraAcceptanceMod.UI
{
    internal sealed class CameraAcceptancePanelController
    {
        private const float PanelWidth = 520f;

        private UiScene? _mountedScene;

        public UiScene BuildScene(GameEngine engine, string mapId)
        {
            var scene = new UiScene();
            int nextId = 1;
            scene.Mount(BuildRoot(engine, mapId).Build(scene.Dispatcher, ref nextId));
            _mountedScene = scene;
            return scene;
        }

        public void ClearIfOwned(UIRoot root)
        {
            if (ReferenceEquals(root.Scene, _mountedScene))
            {
                root.ClearScene();
            }

            _mountedScene = null;
        }

        private UiElementBuilder BuildRoot(GameEngine engine, string mapId)
        {
            string activeModeId = ResolveActiveModeId(engine);
            string activeCameraId = engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId ?? "none";
            string selectedIds = ResolveSelectedEntityIds(engine);
            string followTarget = FormatVector(engine.GameSession.Camera.FollowTargetPositionCm);
            string visibleSummary = ResolveVisibleEntitySummary(engine);

            return Ui.Card(
                Ui.Text("Camera Acceptance").FontSize(22f).Bold().Color("#F7FAFF"),
                Ui.Text(CameraAcceptanceIds.DescribeMap(mapId)).FontSize(14f).Color("#D0D8E6").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Map: {mapId}").FontSize(13f).Color("#8EA2BD"),
                Ui.Text($"Camera: {activeCameraId}").FontSize(13f).Color("#8EA2BD"),
                Ui.Text($"Mode: {activeModeId}").FontSize(13f).Color("#8EA2BD"),
                Ui.Text($"Selected IDs: {selectedIds}").FontSize(13f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Follow Target: {followTarget}").FontSize(13f).Color("#8EA2BD"),
                Ui.Text($"Viewport: {visibleSummary}").FontSize(13f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text("Scenarios").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Row(
                    BuildMapButton("Proj", mapId == CameraAcceptanceIds.ProjectionMapId, ctx => LoadAcceptanceMap(engine, CameraAcceptanceIds.ProjectionMapId)),
                    BuildMapButton("RTS", mapId == CameraAcceptanceIds.RtsMapId, ctx => LoadAcceptanceMap(engine, CameraAcceptanceIds.RtsMapId)),
                    BuildMapButton("TPS", mapId == CameraAcceptanceIds.TpsMapId, ctx => LoadAcceptanceMap(engine, CameraAcceptanceIds.TpsMapId)),
                    BuildMapButton("Blend", mapId == CameraAcceptanceIds.BlendMapId, ctx => LoadAcceptanceMap(engine, CameraAcceptanceIds.BlendMapId)),
                    BuildMapButton("Follow", mapId == CameraAcceptanceIds.FollowMapId, ctx => LoadAcceptanceMap(engine, CameraAcceptanceIds.FollowMapId)),
                    BuildMapButton("Stack", mapId == CameraAcceptanceIds.StackMapId, ctx => LoadAcceptanceMap(engine, CameraAcceptanceIds.StackMapId))
                ).Wrap().Gap(8f),
                Ui.Text("Actions").FontSize(12f).Bold().Color("#F4C77D"),
                BuildScenarioActions(engine, mapId, activeModeId),
                Ui.Text("How To Verify").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Text(CameraAcceptanceIds.DescribeControls(mapId)).FontSize(12f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal)
            ).Width(PanelWidth)
             .Padding(16f)
             .Gap(10f)
             .Radius(18f)
             .Background("#101A29")
             .Absolute(16f, 16f)
             .ZIndex(20);
        }

        private UiElementBuilder BuildScenarioActions(GameEngine engine, string mapId, string activeModeId)
        {
            return mapId switch
            {
                CameraAcceptanceIds.BlendMapId => Ui.Row(
                    BuildActionButton("Cut", ResolveActiveBlendCameraId(engine) == CameraAcceptanceIds.BlendCutCameraId, ctx => SetBlendCamera(engine, CameraAcceptanceIds.BlendCutCameraId)),
                    BuildActionButton("Linear", ResolveActiveBlendCameraId(engine) == CameraAcceptanceIds.BlendLinearCameraId, ctx => SetBlendCamera(engine, CameraAcceptanceIds.BlendLinearCameraId)),
                    BuildActionButton("Smooth", ResolveActiveBlendCameraId(engine) == CameraAcceptanceIds.BlendSmoothCameraId, ctx => SetBlendCamera(engine, CameraAcceptanceIds.BlendSmoothCameraId))
                ).Wrap().Gap(8f),
                CameraAcceptanceIds.FollowMapId => Ui.Row(
                    BuildActionButton("Close", activeModeId == CameraAcceptanceIds.FollowCloseModeId, ctx => SwitchViewMode(engine, CameraAcceptanceIds.FollowCloseModeId)),
                    BuildActionButton("Wide", activeModeId == CameraAcceptanceIds.FollowWideModeId, ctx => SwitchViewMode(engine, CameraAcceptanceIds.FollowWideModeId))
                ).Wrap().Gap(8f),
                CameraAcceptanceIds.StackMapId => Ui.Row(
                    BuildActionButton("Reveal", false, ctx => RequestVirtualCamera(engine, CameraAcceptanceIds.StackRevealShotId, clear: false)),
                    BuildActionButton("Alert", false, ctx => RequestVirtualCamera(engine, CameraAcceptanceIds.StackAlertShotId, clear: false)),
                    BuildActionButton("Clear", false, ctx => RequestVirtualCamera(engine, id: null, clear: true))
                ).Wrap().Gap(8f),
                CameraAcceptanceIds.RtsMapId => Ui.Row(
                    BuildActionButton("RTS Mode", activeModeId == CameraAcceptanceIds.RtsModeId, ctx => SwitchViewMode(engine, CameraAcceptanceIds.RtsModeId))
                ).Wrap().Gap(8f),
                CameraAcceptanceIds.TpsMapId => Ui.Row(
                    BuildActionButton("TPS Mode", activeModeId == CameraAcceptanceIds.TpsModeId, ctx => SwitchViewMode(engine, CameraAcceptanceIds.TpsModeId))
                ).Wrap().Gap(8f),
                _ => Ui.Text("Interact directly in the world for this slice.").FontSize(12f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal)
            };
        }

        private static UiElementBuilder BuildMapButton(string label, bool active, Action<UiActionContext> onClick)
        {
            return Ui.Button(label, onClick)
                .Padding(10f, 8f)
                .Radius(999f)
                .Background(active ? "#244E66" : "#182436")
                .Color(active ? "#F7FAFF" : "#C7D3E1");
        }

        private static UiElementBuilder BuildActionButton(string label, bool active, Action<UiActionContext> onClick)
        {
            return Ui.Button(label, onClick)
                .Padding(10f, 8f)
                .Radius(10f)
                .Background(active ? "#5B441A" : "#121B29")
                .Color("#F7FAFF");
        }

        private void LoadAcceptanceMap(GameEngine engine, string mapId)
        {
            string? currentMapId = engine.CurrentMapSession?.MapId.Value;
            if (string.Equals(currentMapId, mapId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (CameraAcceptanceIds.IsAcceptanceMap(currentMapId))
            {
                engine.UnloadMap(currentMapId!);
            }

            engine.LoadMap(mapId);
        }

        private void SwitchViewMode(GameEngine engine, string modeId)
        {
            if (ResolveViewModeManager(engine) is ViewModeManager viewModeManager)
            {
                viewModeManager.SwitchTo(modeId);
                Refresh(engine);
            }
        }

        private void SetBlendCamera(GameEngine engine, string cameraId)
        {
            engine.GlobalContext[CameraAcceptanceIds.ActiveBlendCameraIdKey] = cameraId;
            Refresh(engine);
        }

        private void RequestVirtualCamera(GameEngine engine, string? id, bool clear)
        {
            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Id = id ?? string.Empty,
                Clear = clear
            });
            Refresh(engine);
        }

        private void Refresh(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            string? mapId = engine.CurrentMapSession?.MapId.Value;
            if (!CameraAcceptanceIds.IsAcceptanceMap(mapId))
            {
                return;
            }

            root.MountScene(BuildScene(engine, mapId!));
            root.IsDirty = true;
        }

        private static ViewModeManager? ResolveViewModeManager(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(ViewModeManager.GlobalKey, out var managerObj) &&
                managerObj is ViewModeManager manager)
            {
                return manager;
            }

            return null;
        }

        private static string ResolveActiveModeId(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(ViewModeManager.ActiveModeIdKey, out var modeObj) && modeObj is string modeId
                ? modeId
                : "map-default";
        }

        private static string ResolveActiveBlendCameraId(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(CameraAcceptanceIds.ActiveBlendCameraIdKey, out var value) &&
                   value is string cameraId &&
                   !string.IsNullOrWhiteSpace(cameraId)
                ? cameraId
                : CameraAcceptanceIds.BlendSmoothCameraId;
        }

        private static string ResolveSelectedEntityIds(GameEngine engine)
        {
            if (!SelectionRuntime.TryGetSelectionBuffer(engine.World, engine.GlobalContext, out var selection) || selection.Count <= 0)
            {
                return "none";
            }

            var builder = new StringBuilder();
            int written = 0;
            for (int i = 0; i < selection.Count; i++)
            {
                var entity = selection.Get(i);
                if (!engine.World.IsAlive(entity))
                {
                    continue;
                }

                if (written > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(entity.Id);
                written++;
            }

            return written > 0 ? builder.ToString() : "none";
        }

        private static string FormatVector(System.Numerics.Vector2? value)
        {
            if (!value.HasValue)
            {
                return "none";
            }

            return $"{value.Value.X:0},{value.Value.Y:0}";
        }

        private static string ResolveVisibleEntitySummary(GameEngine engine)
        {
            int visibleCount = 0;
            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.CameraCullingDebugState.Name, out var cullingObj) &&
                cullingObj is CameraCullingDebugState cullingState)
            {
                visibleCount = cullingState.VisibleEntityCount;
            }

            string[] names = new string[6];
            int nameCount = 0;
            var query = new QueryDescription().WithAll<Ludots.Core.Components.Name, CullState>();
            engine.World.Query(in query, (Entity entity, ref Ludots.Core.Components.Name name, ref CullState cull) =>
            {
                if (!cull.IsVisible || nameCount >= names.Length)
                {
                    return;
                }

                names[nameCount++] = name.Value;
            });

            if (nameCount == 0)
            {
                return visibleCount > 0 ? $"{visibleCount} visible" : "no visible entities";
            }

            string joined = string.Join(", ", names, 0, nameCount);
            return visibleCount > 0 ? $"{visibleCount} visible: {joined}" : joined;
        }
    }
}
