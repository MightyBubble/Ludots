using System;

namespace Ludots.Core.Presentation.Hud
{
    public struct PresentationTextPacket
    {
        public const byte MaxArgs = 4;

        public int TokenId;
        public byte ArgCount;
        public byte Reserved0;
        public short Reserved1;
        public PresentationTextArg Arg0;
        public PresentationTextArg Arg1;
        public PresentationTextArg Arg2;
        public PresentationTextArg Arg3;

        public bool HasValue => TokenId > 0;

        public static PresentationTextPacket FromToken(int tokenId)
        {
            return new PresentationTextPacket
            {
                TokenId = tokenId,
            };
        }

        public static PresentationTextPacket FromWorldHudValueMode(int tokenId, WorldHudValueMode mode, float value0, float value1)
        {
            if (tokenId <= 0)
            {
                return default;
            }

            var packet = FromToken(tokenId);
            switch (mode)
            {
                case WorldHudValueMode.AttributeCurrentOverBase:
                    packet.SetArg(0, PresentationTextArg.FromInt32((int)value0));
                    packet.SetArg(1, PresentationTextArg.FromInt32((int)value1));
                    break;
                case WorldHudValueMode.AttributeCurrent:
                    packet.SetArg(0, PresentationTextArg.FromInt32((int)value0));
                    break;
                case WorldHudValueMode.Constant:
                    packet.SetArg(0, PresentationTextArg.FromFloat32(value0));
                    break;
            }

            return packet;
        }

        public void SetArg(int index, in PresentationTextArg arg)
        {
            if ((uint)index >= MaxArgs)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            switch (index)
            {
                case 0:
                    Arg0 = arg;
                    break;
                case 1:
                    Arg1 = arg;
                    break;
                case 2:
                    Arg2 = arg;
                    break;
                default:
                    Arg3 = arg;
                    break;
            }

            int requiredCount = index + 1;
            if (requiredCount > ArgCount)
            {
                ArgCount = (byte)requiredCount;
            }
        }

        public PresentationTextArg GetArg(int index)
        {
            if ((uint)index >= MaxArgs)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return index switch
            {
                0 => Arg0,
                1 => Arg1,
                2 => Arg2,
                _ => Arg3,
            };
        }
    }
}
