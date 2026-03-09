using System;
using System.Numerics;
using System.Threading.Tasks;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace VirtualCameraBaselineMod.Triggers
{
    public sealed class VirtualCameraBaselineOnMapLoadedTrigger : Trigger
    {
        private readonly IModContext _context;

        public VirtualCameraBaselineOnMapLoadedTrigger(IModContext context)
        {
            _context = context;
            EventKey = GameEvents.MapLoaded;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var mapId = context.Get(CoreServiceKeys.MapId);
            if (mapId != new MapId(VirtualCameraBaselineIds.EntryMapId))
            {
                return Task.CompletedTask;
            }

            var registry = context.Get(CoreServiceKeys.VirtualCameraRegistry)
                ?? throw new InvalidOperationException("VirtualCameraRegistry is required for VirtualCameraBaselineMod.");
            RegisterBaselineCamera(registry);

            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Id = VirtualCameraBaselineIds.IntroFocusCameraId,
                BlendDurationSeconds = 0f
            });

            _context.Log($"[VirtualCameraBaselineMod] Activated {VirtualCameraBaselineIds.IntroFocusCameraId} on {VirtualCameraBaselineIds.EntryMapId}");
            return Task.CompletedTask;
        }

        private static void RegisterBaselineCamera(VirtualCameraRegistry registry)
        {
            registry.Register(new VirtualCameraDefinition
            {
                Id = VirtualCameraBaselineIds.IntroFocusCameraId,
                RigKind = CameraRigKind.TopDown,
                TargetSource = VirtualCameraTargetSource.Fixed,
                FixedTargetCm = new Vector2(6400f, 3200f),
                Yaw = 210f,
                Pitch = 75f,
                DistanceCm = 18000f,
                FovYDeg = 42f,
                DefaultBlendDuration = 0f,
                BlendCurve = CameraBlendCurve.Cut,
                AllowUserInput = false
            });
        }
    }
}
