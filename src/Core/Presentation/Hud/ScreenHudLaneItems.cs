using System.Numerics;

namespace Ludots.Core.Presentation.Hud
{
    /// <summary>
    /// css 排版装饰块(bar/text 共用一份字段布局,零值/哨兵=关闭)。
    /// 渐变 W=-1 表示无渐变;颜色 W=0 表示无该层(边框色退默认、无底板、无阴影)。
    /// </summary>
    public struct ScreenHudDecoration
    {
        public float CornerRadius;
        public float BorderWidth;
        public float Padding;
        public Vector4 BorderColor;
        public Vector4 FillGradientTo;
        public Vector4 BackgroundGradientTo;
        public Vector4 BoxBackground;
        public byte StyleFlags;
        public HudClipShape ClipShape;
        public Vector4 ShadowColor;
        public float ShadowOffsetX;
        public float ShadowOffsetY;
        public float ShadowBlur;

        public static ScreenHudDecoration FromWorld(in WorldHudItem item)
        {
            return new ScreenHudDecoration
            {
                CornerRadius = item.CornerRadius,
                BorderWidth = item.BorderWidth,
                Padding = item.Padding,
                BorderColor = item.BorderColor,
                FillGradientTo = item.FillGradientTo,
                BackgroundGradientTo = item.BackgroundGradientTo,
                BoxBackground = item.BoxBackground,
                StyleFlags = item.StyleFlags,
                ClipShape = item.ClipShape,
                ShadowColor = item.ShadowColor,
                ShadowOffsetX = item.ShadowOffsetX,
                ShadowOffsetY = item.ShadowOffsetY,
                ShadowBlur = item.ShadowBlur,
            };
        }
    }

    public struct ScreenHudBarItem
    {
        public int StableId;
        public int DirtySerial;
        public float ScreenX;
        public float ScreenY;
        public Vector4 Color0;
        public Vector4 Color1;
        public float Width;
        public float Height;
        public float Value0;
        /// <summary>图标条目:图片源在字符串表的 id(bar 车道此前未用 Id0,语义即图标)。</summary>
        public int Id0;
        public ScreenHudDecoration Decoration;
    }

    public struct ScreenHudTextItem
    {
        public int StableId;
        public int DirtySerial;
        public float ScreenX;
        public float ScreenY;
        public Vector4 Color0;
        public float Value0;
        public float Value1;
        public int Id0;
        public int Id1;
        public int FontSize;
        /// <summary>非 0 表示值绑定：刷新通道按 Owner 的 AttributeBuffer 现读 Value0/Value1。</summary>
        public byte ValueBound;
        public int BoundAttributeId;
        public Arch.Core.Entity Owner;
        public ScreenHudDecoration Decoration;
        public PresentationTextPacket Text;
    }
}
