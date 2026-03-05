using System.Numerics;

namespace Ludots.Core.Presentation.Hud
{
    /// <summary>
    /// Screen-space HUD item. Output of WorldHudToScreenSystem; adapter draws without projection or culling.
    /// </summary>
    public struct ScreenHudItem
    {
        public WorldHudItemKind Kind;
        public float ScreenX;
        public float ScreenY;
        public Vector4 Color0;
        public Vector4 Color1;
        public float Width;
        public float Height;
        public float Value0;
        public float Value1;
        public int Id0;
        public int Id1;
        public int FontSize;
    }
}
