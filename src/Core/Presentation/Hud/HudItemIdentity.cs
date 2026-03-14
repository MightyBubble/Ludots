using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Hud
{
    public static class HudItemIdentity
    {
        public static int ComposeStableId(int ownerStableId, WorldHudItemKind kind, int discriminator = 0)
        {
            int hash = 17;
            hash = Mix(hash, ownerStableId);
            hash = Mix(hash, (int)kind);
            hash = Mix(hash, discriminator);
            return Finalize(hash);
        }

        public static int ComposeBarDirtySerial(
            float width,
            float height,
            float value,
            in Vector4 background,
            in Vector4 foreground)
        {
            int hash = 23;
            hash = Mix(hash, BitConverter.SingleToInt32Bits(width));
            hash = Mix(hash, BitConverter.SingleToInt32Bits(height));
            hash = Mix(hash, BitConverter.SingleToInt32Bits(value));
            hash = Mix(hash, background);
            hash = Mix(hash, foreground);
            return Finalize(hash);
        }

        public static int ComposeTextDirtySerial(
            int fontSize,
            int legacyStringId,
            int legacyModeId,
            float value0,
            float value1,
            in Vector4 color,
            in PresentationTextPacket packet)
        {
            int hash = 29;
            hash = Mix(hash, fontSize);
            hash = Mix(hash, legacyStringId);
            hash = Mix(hash, legacyModeId);
            hash = Mix(hash, BitConverter.SingleToInt32Bits(value0));
            hash = Mix(hash, BitConverter.SingleToInt32Bits(value1));
            hash = Mix(hash, color);
            hash = Mix(hash, packet.TokenId);
            hash = Mix(hash, packet.ArgCount);
            hash = Mix(hash, packet.Arg0);
            hash = Mix(hash, packet.Arg1);
            hash = Mix(hash, packet.Arg2);
            hash = Mix(hash, packet.Arg3);
            return Finalize(hash);
        }

        private static int Mix(int hash, int value)
        {
            return unchecked((hash * 16777619) ^ value);
        }

        private static int Mix(int hash, byte value)
        {
            return Mix(hash, (int)value);
        }

        private static int Mix(int hash, in Vector4 value)
        {
            hash = Mix(hash, BitConverter.SingleToInt32Bits(value.X));
            hash = Mix(hash, BitConverter.SingleToInt32Bits(value.Y));
            hash = Mix(hash, BitConverter.SingleToInt32Bits(value.Z));
            hash = Mix(hash, BitConverter.SingleToInt32Bits(value.W));
            return hash;
        }

        private static int Mix(int hash, in PresentationTextArg value)
        {
            hash = Mix(hash, (int)value.Type);
            hash = Mix(hash, (int)value.Format);
            hash = Mix(hash, value.Raw32);
            return hash;
        }

        private static int Finalize(int hash)
        {
            hash &= int.MaxValue;
            return hash == 0 ? 1 : hash;
        }
    }
}
