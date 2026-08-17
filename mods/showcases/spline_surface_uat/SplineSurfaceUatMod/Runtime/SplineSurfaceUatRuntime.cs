using System.Numerics;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Scripting;
using Ludots.UI;
using SplineSurfaceUatMod.UI;
using Ludots.Core.Client;

namespace SplineSurfaceUatMod.Runtime
{
    internal enum SplineSurfaceFocusTarget : byte
    {
        Road = 1,
        River = 2,
        Lake = 3,
        RawMesh = 4,
    }

    internal sealed class SplineSurfaceUatRuntime
    {
        private const string TacticalCameraId = "Camera.Profile.Tactical";
        private string? _activeMapId;
        private readonly SplineSurfaceUatPanelController _panelController;

        public SplineSurfaceUatRuntime()
        {
            _panelController = new SplineSurfaceUatPanelController(this);
        }

        public bool IsActive => string.Equals(_activeMapId, SplineSurfaceUatIds.MapId, System.StringComparison.OrdinalIgnoreCase);
        public string LastStatus { get; private set; } = "Reset camera to pull road, river, lake, and raw procedural mesh back into view.";

        public bool IsActiveFor(GameEngine engine)
        {
            return string.Equals(engine?.CurrentMapSession?.MapId.Value, SplineSurfaceUatIds.MapId, System.StringComparison.OrdinalIgnoreCase);
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            _activeMapId = engine.CurrentMapSession?.MapId.Value;
            ApplyCameraPose(engine, SplineSurfaceUatIds.OverviewCameraTargetCm);
            LastStatus = "Camera framed on the procedural surface overview.";
            RefreshPanel(engine);
            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            if (context.GetEngine() is GameEngine engine)
            {
                Unbind(engine);
                return Task.CompletedTask;
            }

            string mapId = context.Get(CoreServiceKeys.MapId).Value;
            if (string.Equals(mapId, SplineSurfaceUatIds.MapId, System.StringComparison.OrdinalIgnoreCase))
            {
                _activeMapId = null;
            }

            return Task.CompletedTask;
        }

        public bool TryResetCamera(GameEngine engine)
        {
            if (engine == null || !IsActiveFor(engine))
            {
                return false;
            }

            ApplyCameraPose(engine, SplineSurfaceUatIds.OverviewCameraTargetCm);
            LastStatus = "Camera reset to the spline surface overview.";
            RefreshPanel(engine);
            return true;
        }

        public bool TryFocusSurface(GameEngine engine, SplineSurfaceFocusTarget target)
        {
            if (engine == null || !IsActiveFor(engine))
            {
                return false;
            }

            Vector2 focusTarget = target switch
            {
                SplineSurfaceFocusTarget.Road => ToCameraTarget(SplineSurfaceUatIds.RoadAnchorWorld),
                SplineSurfaceFocusTarget.River => ToCameraTarget(SplineSurfaceUatIds.RiverAnchorWorld),
                SplineSurfaceFocusTarget.Lake => ToCameraTarget(SplineSurfaceUatIds.LakeAnchorWorld),
                SplineSurfaceFocusTarget.RawMesh => ToCameraTarget(SplineSurfaceUatIds.RawAnchorWorld),
                _ => SplineSurfaceUatIds.OverviewCameraTargetCm,
            };

            ApplyCameraPose(engine, focusTarget);
            LastStatus = target switch
            {
                SplineSurfaceFocusTarget.Road => "Camera focused on the road spline ribbon bake.",
                SplineSurfaceFocusTarget.River => "Camera focused on the river spline ribbon bake.",
                SplineSurfaceFocusTarget.Lake => "Camera focused on the closed-area lake bake.",
                SplineSurfaceFocusTarget.RawMesh => "Camera focused on the raw procedural mesh bake.",
                _ => "Camera focused on the spline surface showcase.",
            };
            RefreshPanel(engine);
            return true;
        }

        public SplineSurfaceUatPanelState BuildPanelState(GameEngine engine)
        {
            Vector2 cameraTarget = ClientLocalSeatAccess.ResolveAuthorityCamera(engine).State.TargetCm;
            return new SplineSurfaceUatPanelState(
                Title: "Spline Surface UAT",
                Status: LastStatus,
                Camera: $"Camera ({cameraTarget.X:0},{cameraTarget.Y:0})",
                Surfaces: "Road | River | Lake | Raw Mesh",
                Hint: "Reset Camera returns to the shared overview. Focus buttons jump to each presenter-authored chunk-baked procedural surface.");
        }

        public void SyncPanel(GameEngine engine)
        {
            if (engine == null || !IsActiveFor(engine))
            {
                return;
            }

            RefreshPanel(engine);
        }

        private void ApplyCameraPose(GameEngine engine, Vector2 targetCm)
        {
            ActivateTacticalCamera(engine);
            ClientLocalSeatAccess.ResolveAuthorityCamera(engine).ApplyPose(new CameraPoseRequest
            {
                VirtualCameraId = TacticalCameraId,
                TargetCm = targetCm,
            });
        }

        private void ActivateTacticalCamera(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.VirtualCameraRegistry) is not VirtualCameraRegistry registry ||
                !registry.TryGet(TacticalCameraId, out VirtualCameraDefinition? definition) ||
                definition == null)
            {
                return;
            }

            ClientLocalSeatAccess.ResolveAuthorityCamera(engine).ResetVirtualCameras();
            ClientLocalSeatAccess.ResolveAuthorityCamera(engine).ActivateVirtualCamera(
                TacticalCameraId,
                blendDurationSeconds: 0f,
                followTarget: CameraFollowTargetFactory.Build(
                    engine.World,
                    engine.GlobalContext,
                    definition.FollowTargetKind,
                    Arch.Core.Entity.Null,
                    definition.FollowCollectionKey),
                snapToFollowTargetWhenAvailable: definition.SnapToFollowTargetWhenAvailable);
        }

        private void RefreshPanel(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            _panelController.MountOrSync(root, engine, BuildPanelState(engine));
        }

        private void Unbind(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
            {
                _panelController.ClearIfOwned(root);
            }

            _activeMapId = null;
            LastStatus = "Reset camera to pull road, river, lake, and raw procedural mesh back into view.";
        }

        private static Vector2 ToCameraTarget(Vector3 world)
        {
            return new Vector2(world.X, world.Z);
        }
    }
}
