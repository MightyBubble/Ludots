using Arch.Core;
using Arch.System;
using CameraAcceptanceMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;

namespace CameraAcceptanceMod.Systems
{
    internal sealed class CameraAcceptanceGroundClickSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly CameraAcceptanceRuntime _runtime;

        public CameraAcceptanceGroundClickSystem(GameEngine engine, CameraAcceptanceRuntime runtime)
        {
            _engine = engine;
            _runtime = runtime;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            string? mapId = _engine.CurrentMapSession?.MapId.Value;
            if (!string.Equals(mapId, CameraAcceptanceIds.ProjectionMapId, System.StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mapId, CameraAcceptanceIds.BlendMapId, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input)
            {
                return;
            }

            if (!PointerInteractionSnapshotReader.TryRead(_engine.GlobalContext, out PointerInteractionSnapshot pointer))
            {
                return;
            }

            if (string.Equals(mapId, CameraAcceptanceIds.BlendMapId, System.StringComparison.OrdinalIgnoreCase))
            {
                UpdateBlendSelection(input);
            }

            if (!pointer.Confirm.PressedThisFrame)
            {
                return;
            }

            if (!AuthoritativeGroundPointerHelper.TryResolveFromScreen(
                    _engine.GlobalContext,
                    pointer.Confirm.ResolvePressPointerOrCurrent(),
                    out WorldCmInt2 worldCm))
            {
                return;
            }

            if (string.Equals(mapId, CameraAcceptanceIds.ProjectionMapId, System.StringComparison.OrdinalIgnoreCase) &&
                _engine.GlobalContext.TryGetValue(CoreServiceKeys.HoveredEntity.Name, out var hoveredObj) &&
                hoveredObj is Entity hoveredEntity &&
                _engine.World.IsAlive(hoveredEntity))
            {
                return;
            }

            if (string.Equals(mapId, CameraAcceptanceIds.BlendMapId, System.StringComparison.OrdinalIgnoreCase))
            {
                _runtime.HandleBlendGroundClick(_engine, worldCm, ResolveActiveBlendCameraId());
                return;
            }

            _runtime.HandleGroundClick(_engine, worldCm);
        }

        private void UpdateBlendSelection(IInputActionReader input)
        {
            if (input.PressedThisFrame(CameraAcceptanceIds.BlendCutActionId))
            {
                _engine.GlobalContext[CameraAcceptanceIds.ActiveBlendCameraIdKey] = CameraAcceptanceIds.BlendCutCameraId;
            }
            else if (input.PressedThisFrame(CameraAcceptanceIds.BlendLinearActionId))
            {
                _engine.GlobalContext[CameraAcceptanceIds.ActiveBlendCameraIdKey] = CameraAcceptanceIds.BlendLinearCameraId;
            }
            else if (input.PressedThisFrame(CameraAcceptanceIds.BlendSmoothActionId))
            {
                _engine.GlobalContext[CameraAcceptanceIds.ActiveBlendCameraIdKey] = CameraAcceptanceIds.BlendSmoothCameraId;
            }
        }

        private string ResolveActiveBlendCameraId()
        {
            return _engine.GlobalContext.TryGetValue(CameraAcceptanceIds.ActiveBlendCameraIdKey, out var value) &&
                   value is string cameraId &&
                   !string.IsNullOrWhiteSpace(cameraId)
                ? cameraId
                : CameraAcceptanceIds.BlendSmoothCameraId;
        }
    }
}
