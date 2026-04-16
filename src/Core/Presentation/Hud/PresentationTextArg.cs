using System;

namespace Ludots.Core.Presentation.Hud
{
    public struct PresentationTextArg
    {
        public PresentationTextArgType Type;
        public PresentationTextArgFormat Format;
        public short Reserved;
        public int Raw32;

        public static PresentationTextArg FromInt32(int value, PresentationTextArgFormat format = PresentationTextArgFormat.Integer)
        {
            return new PresentationTextArg
            {
                Type = PresentationTextArgType.Int32,
                Format = format,
                Raw32 = value,
            };
        }

        public static PresentationTextArg FromFloat32(float value, PresentationTextArgFormat format = PresentationTextArgFormat.Default)
        {
            return new PresentationTextArg
            {
                Type = PresentationTextArgType.Float32,
                Format = format,
                Raw32 = BitConverter.SingleToInt32Bits(value),
            };
        }

        public static PresentationTextArg FromToken(int tokenId)
        {
            return new PresentationTextArg
            {
                Type = PresentationTextArgType.Token,
                Raw32 = tokenId,
            };
        }

        public int AsInt32() => Raw32;

        public float AsFloat32() => BitConverter.Int32BitsToSingle(Raw32);

        public int AsTokenId() => Raw32;
    }
}
