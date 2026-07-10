using System.Numerics;
using Ludots.Core.Presentation;

namespace Ludots.Core.Presentation.Hud
{
    public struct PresentationOverlayItem
    {
        public int StableId;
        public int DirtySerial;
        public PresentationOverlayItemKind Kind;
        public PresentationOverlayLayer Layer;
        public float X;
        public float Y;
        public float Width;
        public float Height;
        public int FontSize;
        public string? Text;
        public Vector4 Color0;
        public Vector4 Color1;
        public float Value0;
        public float Value1;
        public float Value2;
        public PresentationClipShape ClipShape;
    }
}
