namespace Ludots.Core.Presentation.Hud
{
    /// <summary>
    /// Single source of truth for runtime render-debug toggles.
    /// Adapter consumes this state only; mods/presentation systems decide values.
    /// </summary>
    public sealed class RenderDebugState
    {
        public bool DrawTerrain { get; set; } = true;
        public bool DrawPrimitives { get; set; } = true;
        public bool DrawDebugDraw { get; set; } = true;
        public bool DrawSkiaUi { get; set; } = true;
        public float AcceptanceScaleMultiplier { get; set; } = 1f;
    }
}
