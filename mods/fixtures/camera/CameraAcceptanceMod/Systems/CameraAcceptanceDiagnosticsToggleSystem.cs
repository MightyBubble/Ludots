using CameraAcceptanceMod.Runtime;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Arch.System;

namespace CameraAcceptanceMod.Systems
{
    internal sealed class CameraAcceptanceDiagnosticsToggleSystem : ISystem<float>
    {
        private static readonly LogChannel LogChannel = Log.RegisterChannel("CameraAcceptanceMod");

        private readonly GameEngine _engine;

        public CameraAcceptanceDiagnosticsToggleSystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            string? mapId = _engine.CurrentMapSession?.MapId.Value;
            if (!CameraAcceptanceIds.IsAcceptanceMap(mapId))
            {
                return;
            }

            if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input ||
                _engine.GetService(CameraAcceptanceServiceKeys.DiagnosticsState) is not CameraAcceptanceDiagnosticsState diagnostics ||
                _engine.GetService(CoreServiceKeys.RenderDebugState) is not RenderDebugState renderDebug)
            {
                return;
            }

            bool isHotpathMap = string.Equals(mapId, CameraAcceptanceIds.HotpathMapId, System.StringComparison.OrdinalIgnoreCase);
            bool changed = false;
            if (input.PressedThisFrame(CameraAcceptanceIds.TogglePanelActionId))
            {
                renderDebug.DrawSkiaUi = !renderDebug.DrawSkiaUi;
                changed = true;
            }

            if (input.PressedThisFrame(CameraAcceptanceIds.ToggleHudActionId))
            {
                diagnostics.HudEnabled = !diagnostics.HudEnabled;
                changed = true;
            }

            if (input.PressedThisFrame(CameraAcceptanceIds.ToggleTextActionId))
            {
                diagnostics.TextEnabled = !diagnostics.TextEnabled;
                changed = true;
            }

            if (isHotpathMap && input.PressedThisFrame(CameraAcceptanceIds.ToggleHotpathBarsActionId))
            {
                diagnostics.HotpathBarsEnabled = !diagnostics.HotpathBarsEnabled;
                changed = true;
            }

            if (isHotpathMap && input.PressedThisFrame(CameraAcceptanceIds.ToggleHotpathHudTextActionId))
            {
                diagnostics.HotpathHudTextEnabled = !diagnostics.HotpathHudTextEnabled;
                changed = true;
            }

            if (isHotpathMap && input.PressedThisFrame(CameraAcceptanceIds.ToggleTerrainActionId))
            {
                renderDebug.DrawTerrain = !renderDebug.DrawTerrain;
                changed = true;
            }

            if (isHotpathMap && input.PressedThisFrame(CameraAcceptanceIds.TogglePrimitiveActionId))
            {
                renderDebug.DrawPrimitives = !renderDebug.DrawPrimitives;
                changed = true;
            }

            if (isHotpathMap && input.PressedThisFrame(CameraAcceptanceIds.ToggleHotpathCullCrowdActionId))
            {
                diagnostics.HotpathCullCrowdEnabled = !diagnostics.HotpathCullCrowdEnabled;
                changed = true;
            }

            if (changed)
            {
                if (isHotpathMap)
                {
                    Log.Info(in LogChannel,
                        $"CameraAcceptance diagnostics toggles: panel={(renderDebug.DrawSkiaUi ? "ON" : "OFF")} hud={(diagnostics.HudEnabled ? "ON" : "OFF")} select={(diagnostics.TextEnabled ? "ON" : "OFF")} bars={(diagnostics.HotpathBarsEnabled ? "ON" : "OFF")} hudText={(diagnostics.HotpathHudTextEnabled ? "ON" : "OFF")} terrain={(renderDebug.DrawTerrain ? "ON" : "OFF")} primitives={(renderDebug.DrawPrimitives ? "ON" : "OFF")} crowd={(diagnostics.HotpathCullCrowdEnabled ? "ON" : "OFF")}");
                }
                else
                {
                    Log.Info(in LogChannel,
                        $"CameraAcceptance diagnostics toggles: panel={(renderDebug.DrawSkiaUi ? "ON" : "OFF")} hud={(diagnostics.HudEnabled ? "ON" : "OFF")} text={(diagnostics.TextEnabled ? "ON" : "OFF")}");
                }
            }
        }
    }
}
