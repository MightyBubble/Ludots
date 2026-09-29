using Ludots.Core.Presentation;

namespace Ludots.Tests.Presentation
{
    internal static class MinimapTestActions
    {
        public static MinimapActionsConfig Create()
        {
            return new MinimapActionsConfig
            {
                Toggle = "Minimap.Toggle",
                TogglePreset = "Minimap.TogglePreset",
                ToggleRotateWithCamera = "Minimap.ToggleRotateWithCamera",
                Zoom = "Minimap.Zoom",
                ZoomIn = "Minimap.ZoomIn",
                ZoomOut = "Minimap.ZoomOut",
                Pan = "Minimap.Pan",
                CenterOnFocusPrimary = "Minimap.CenterOnFocusPrimary",
            };
        }
    }
}
