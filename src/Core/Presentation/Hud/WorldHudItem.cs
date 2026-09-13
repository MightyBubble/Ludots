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
        public PresentationTextPacket Text;
    }
}
