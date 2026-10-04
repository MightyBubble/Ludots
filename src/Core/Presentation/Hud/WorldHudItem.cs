using System.Numerics;
using Arch.Core;

namespace Ludots.Core.Presentation.Hud
{
    public struct WorldHudItem
    {
        public Entity Owner;
        public int StableId;
        public int DirtySerial;
        public WorldHudItemKind Kind;
        public Vector3 WorldPosition;
        public Vector4 Color0;
        public Vector4 Color1;
        public float Width;
        public float Height;
        public float Value0;
        public float Value1;
        public int Id0;
        public int Id1;
        public int FontSize;
        /// <summary>非 0 表示 Value0/Value1 为属性源快照初值，权威值在投影期现读 AttributeBuffer。</summary>
        public byte ValueBound;
        public int BoundAttributeId;
        /// <summary>屏幕空间像素偏移(css translate)。投影后应用,与相机距离无关;y 正方向为屏幕向下。</summary>
        public float ScreenOffsetX;
        public float ScreenOffsetY;

        // 排版装饰块(css 声明,零值=关闭;关闭时渲染路径与未声明时逐像素一致):
        public float CornerRadius;
        public float BorderWidth;
        public float Padding;
        /// <summary>W<=0 视为无边框色(渲染端用默认黑描边)。</summary>
        public Vector4 BorderColor;
        /// <summary>血条前景渐变终点;W<0 表示无渐变。</summary>
        public Vector4 FillGradientTo;
        /// <summary>背景渐变终点;W<0 表示无渐变。</summary>
        public Vector4 BackgroundGradientTo;
        /// <summary>文字底板颜色(名字板);W<=0 表示无底板。血条忽略。</summary>
        public Vector4 BoxBackground;
        public byte StyleFlags;
        /// <summary>W<=0 视为无阴影。</summary>
        public Vector4 ShadowColor;
        public float ShadowOffsetX;
        public float ShadowOffsetY;
        public float ShadowBlur;
        public PresentationTextPacket Text;
    }
}
