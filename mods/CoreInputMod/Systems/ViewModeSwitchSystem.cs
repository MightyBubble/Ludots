using System.Collections.Generic;
using Arch.System;
using CoreInputMod.ViewMode;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CoreInputMod.Systems
{
    public sealed class ViewModeSwitchSystem : ISystem<float>
    {
        private const string NextAction = "ViewModeNext";
        private const string PrevAction = "ViewModePrev";

        private static readonly System.Numerics.Vector4 HudBackground = new(0f, 0f, 0f, 0.5f);
        private static readonly System.Numerics.Vector4 HudBorder = new(1f, 1f, 1f, 0.15f);
        private static readonly System.Numerics.Vector4 HudTitle = new(1f, 1f, 0.6f, 1f);
        private static readonly System.Numerics.Vector4 HudHint = new(0.75f, 0.9f, 1f, 0.85f);

        private readonly Dictionary<string, object> _globals;

        public ViewModeSwitchSystem(Dictionary<string, object> globals)
        {
            _globals = globals;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(ViewModeManager.GlobalKey, out var managerObj) || managerObj is not ViewModeManager manager)
            {
                return;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) || inputObj is not IInputActionReader input)
            {
                return;
            }

            if (input.PressedThisFrame(NextAction))
            {
                manager.SwitchNext();
            }
            else if (input.PressedThisFrame(PrevAction))
            {
                manager.SwitchPrev();
            }
            else
            {
                foreach (var mode in manager.Modes)
                {
                    if (!string.IsNullOrEmpty(mode.SwitchActionId) && input.PressedThisFrame(mode.SwitchActionId))
                    {
                        manager.SwitchTo(mode.Id);
                        break;
                    }
                }
            }

            RenderModeHud(manager);
        }

        private void RenderModeHud(ViewModeManager manager)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.ScreenOverlayBuffer.Name, out var overlayObj) || overlayObj is not ScreenOverlayBuffer overlay)
            {
                return;
            }

            var active = manager.ActiveMode;
            if (active == null)
            {
                return;
            }

            overlay.AddRect(8, 8, 300, 50, HudBackground, HudBorder);
            overlay.AddText(16, 14, $"ViewMode: {active.DisplayName}", 18, HudTitle);
            if (manager.Modes.Count > 1)
            {
                overlay.AddText(16, 36, "V: next | B: prev", 13, HudHint);
            }
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
