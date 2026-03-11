using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;

namespace CameraShowcaseMod.UI
{
    internal sealed class CameraShowcasePanelController
    {
        private UiScene? _mountedScene;

        public UiScene BuildScene(GameEngine engine, string mapId, ViewModeManager? viewModeManager)
        {
            var scene = new UiScene();
            int nextId = 1;
            scene.Mount(BuildRoot(engine, mapId, viewModeManager).Build(scene.Dispatcher, ref nextId));
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

        private UiElementBuilder BuildRoot(GameEngine engine, string mapId, ViewModeManager? viewModeManager)
        {
            string activeMode = engine.GlobalContext.TryGetValue(ViewModeManager.ActiveModeIdKey, out var modeObj) && modeObj is string modeId
                ? modeId
                : "map-default";

            return Ui.Card(
                Ui.Text("Camera Showcase").FontSize(22f).Bold().Color("#F7FAFF"),
                Ui.Text(CameraShowcaseIds.DescribeMap(mapId)).FontSize(14f).Color("#D0D8E6").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text($"Map: {mapId}").FontSize(13f).Color("#8EA2BD"),
                Ui.Text($"Mode: {activeMode}").FontSize(13f).Color("#8EA2BD"),
                Ui.Text("Maps").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Row(
                    BuildMapButton("Hub", mapId == CameraShowcaseIds.HubMapId, ctx => LoadShowcaseMap(engine, CameraShowcaseIds.HubMapId)),
                    BuildMapButton("Stack", mapId == CameraShowcaseIds.StackMapId, ctx => LoadShowcaseMap(engine, CameraShowcaseIds.StackMapId)),
                    BuildMapButton("Selection", mapId == CameraShowcaseIds.SelectionMapId, ctx => LoadShowcaseMap(engine, CameraShowcaseIds.SelectionMapId)),
                    BuildMapButton("Bootstrap", mapId == CameraShowcaseIds.BootstrapMapId, ctx => LoadShowcaseMap(engine, CameraShowcaseIds.BootstrapMapId))
                ).Wrap().Gap(8f),
                Ui.Text("View Modes").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Row(
                    BuildActionButton("Tactical", activeMode == CameraShowcaseIds.TacticalModeId, ctx => viewModeManager?.SwitchTo(CameraShowcaseIds.TacticalModeId)),
                    BuildActionButton("Follow", activeMode == CameraShowcaseIds.FollowModeId, ctx => viewModeManager?.SwitchTo(CameraShowcaseIds.FollowModeId)),
                    BuildActionButton("Inspect", activeMode == CameraShowcaseIds.InspectModeId, ctx => viewModeManager?.SwitchTo(CameraShowcaseIds.InspectModeId)),
                    BuildActionButton("Selection", activeMode == CameraShowcaseIds.SelectionModeId, ctx => viewModeManager?.SwitchTo(CameraShowcaseIds.SelectionModeId))
                ).Wrap().Gap(8f),
                Ui.Text("Runtime").FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Row(
                    BuildActionButton("Reveal", false, ctx => ActivateVirtualCamera(engine, CameraShowcaseIds.RevealShotId)),
                    BuildActionButton("Clear", false, ctx => ClearTopShot(engine)),
                    BuildActionButton("Tighten", false, ctx => TightenActiveCameraPose(engine)),
                    BuildActionButton("Reset", false, ctx => ResetActiveCameraPose(engine))
                ).Wrap().Gap(8f),
                Ui.Text("Keyboard: F1/F2/F3 shared modes, F4 selection mode, Tab cycles selection target.").FontSize(12f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal)
            ).Width(440f)
             .Padding(16f)
             .Gap(10f)
             .Radius(18f)
             .Background("#101A29")
             .Absolute(16f, 16f)
             .ZIndex(20);
        }

        private static UiElementBuilder BuildMapButton(string label, bool active, System.Action<UiActionContext> onClick)
        {
            return Ui.Button(label, onClick)
                .Padding(10f, 8f)
                .Radius(999f)
                .Background(active ? "#244E66" : "#182436")
                .Color(active ? "#F7FAFF" : "#C7D3E1");
        }

        private static UiElementBuilder BuildActionButton(string label, bool active, System.Action<UiActionContext> onClick)
        {
            return Ui.Button(label, onClick)
                .Padding(10f, 8f)
                .Radius(10f)
                .Background(active ? "#5B441A" : "#121B29")
                .Color("#F7FAFF");
        }

        private static void LoadShowcaseMap(GameEngine engine, string mapId)
        {
            string? currentMapId = engine.CurrentMapSession?.MapId.Value;
            if (string.Equals(currentMapId, mapId, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (CameraShowcaseIds.IsShowcaseMap(currentMapId))
            {
                engine.UnloadMap(currentMapId!);
            }

            engine.LoadMap(mapId);
        }

        private static void ActivateVirtualCamera(GameEngine engine, string virtualCameraId)
        {
            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Id = virtualCameraId
            });
        }

        private static void ClearTopShot(GameEngine engine)
        {
            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Clear = true
            });
        }

        private static void TightenActiveCameraPose(GameEngine engine)
        {
            if (!TryResolveActiveVirtualCamera(engine, out var activeCameraId, out var definition))
            {
                return;
            }

            var state = engine.GameSession.Camera.State;
            float minDistance = definition.MinDistanceCm > 0f ? definition.MinDistanceCm : 100f;
            float maxDistance = definition.MaxDistanceCm > 0f ? definition.MaxDistanceCm : System.MathF.Max(minDistance, state.DistanceCm);
            float minPitch = definition.MinPitchDeg < definition.MaxPitchDeg ? definition.MinPitchDeg : -89f;
            float maxPitch = definition.MinPitchDeg < definition.MaxPitchDeg ? definition.MaxPitchDeg : 89f;

            engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
            {
                VirtualCameraId = activeCameraId,
                TargetCm = definition.TargetSource == VirtualCameraTargetSource.Fixed ? definition.FixedTargetCm : null,
                Yaw = state.Yaw,
                Pitch = System.Math.Clamp(state.Pitch - 6f, minPitch, maxPitch),
                DistanceCm = System.Math.Clamp(state.DistanceCm * 0.82f, minDistance, maxDistance),
                FovYDeg = System.MathF.Max(35f, state.FovYDeg - 4f)
            });
        }

        private static void ResetActiveCameraPose(GameEngine engine)
        {
            if (!TryResolveActiveVirtualCamera(engine, out var activeCameraId, out var definition))
            {
                return;
            }

            engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
            {
                VirtualCameraId = activeCameraId,
                TargetCm = definition.TargetSource == VirtualCameraTargetSource.Fixed ? definition.FixedTargetCm : null,
                Yaw = definition.Yaw,
                Pitch = definition.Pitch,
                DistanceCm = definition.DistanceCm,
                FovYDeg = definition.FovYDeg
            });
        }

        private static bool TryResolveActiveVirtualCamera(GameEngine engine, out string activeCameraId, out VirtualCameraDefinition definition)
        {
            activeCameraId = string.Empty;
            definition = null!;

            var brain = engine.GameSession.Camera.VirtualCameraBrain;
            var registry = engine.GetService(CoreServiceKeys.VirtualCameraRegistry);
            if (brain == null || !brain.HasActiveCamera || registry == null)
            {
                return false;
            }

            activeCameraId = brain.ActiveCameraId;
            return registry.TryGet(activeCameraId, out definition!);
        }

        private static SkiaSharp.SKColor ParseColor(string color)
        {
            return SkiaSharp.SKColor.TryParse(color, out var parsed)
                ? parsed
                : SkiaSharp.SKColors.White;
        }
    }
}
