using System.Numerics;

namespace Ludots.Core.Presentation.Hud
{
    /// <summary>
    /// Screen-space HUD item. Output of WorldHudToScreenSystem; adapter draws without projection or culling.
    /// </summary>
    public struct ScreenHudItem
    {
        public int StableId;
        public int DirtySerial;
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
        /// <summary>非 0 表示值绑定（文本车道）：权威值由刷新通道按 Owner 属性现读。</summary>
        public byte ValueBound;
        public int BoundAttributeId;
        public Arch.Core.Entity Owner;
        public PresentationTextPacket Text;
    }
}
