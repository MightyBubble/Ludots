using Ludots.Core.UI;
using Ludots.UI;

namespace Ludots.Adapter.Godot
{
    /// <summary>
    /// Minimal IUiSystem for Godot. Phase 1: no-op. Phase 5 may add Godot Control-based UI.
    /// </summary>
    public sealed class GodotUiSystem : IUiSystem
    {
        private readonly UIRoot _root;

        public GodotUiSystem(UIRoot root)
        {
            _root = root;
        }

        public void SetHtml(string html, string css)
        {
            // Godot UI not yet implemented; stub for compatibility
        }
    }
}
