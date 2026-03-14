using System.Numerics;

namespace Ludots.Core.Presentation.Hud
{
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
        public PresentationTextPacket Text;
    }
}
