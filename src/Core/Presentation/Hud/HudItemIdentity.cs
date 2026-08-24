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

        public static int ComposePresenterStableId(
            int ownerStableId,
            WorldHudItemKind kind,
            int definitionId,
            int slotIndex)
        {
            int discriminator = slotIndex == 0
                ? definitionId
                : HashCode.Combine(definitionId, slotIndex);
            return ComposeStableId(ownerStableId, kind, discriminator);
        }

        public static int ComposeBarDirtySerial(
            float width,
            float height,
            float value,
            in Vector4 background,
            in Vector4 foreground)
        {
            int widthPx = Math.Max(1, (int)MathF.Round(width));
            int heightPx = Math.Max(1, (int)MathF.Round(height));
            int fillPx = (int)MathF.Round(widthPx * Math.Clamp(value, 0f, 1f));
            fillPx = Math.Clamp(fillPx, 0, widthPx);
            int hash = 23;
            hash = Mix(hash, widthPx);
            hash = Mix(hash, heightPx);
            hash = Mix(hash, fillPx);
            hash = Mix(hash, background);
            hash = Mix(hash, foreground);
            return Finalize(hash);
        }

        public static int ComposeTextDirtySerial(
            int fontSize,
            int stringTableId,
            int valueModeId,
            float value0,
            float value1,
            in Vector4 color,
            in PresentationTextPacket packet)
        {
            int hash = 29;
            hash = Mix(hash, fontSize);
            hash = Mix(hash, stringTableId);
            hash = Mix(hash, valueModeId);
            hash = Mix(hash, color);
            hash = Mix(hash, packet.TokenId);
            hash = Mix(hash, packet.ArgCount);
            hash = Mix(hash, packet.Arg0);
            hash = Mix(hash, packet.Arg1);
            hash = Mix(hash, packet.Arg2);
            hash = Mix(hash, packet.Arg3);
            if (!packet.HasValue)
            {
                hash = MixWorldHudValueModeText(hash, valueModeId, value0, value1);
            }

            return Finalize(hash);
        }

        private static int MixWorldHudValueModeText(int hash, int valueModeId, float value0, float value1)
        {
            WorldHudValueMode mode = (WorldHudValueMode)valueModeId;
            switch (mode)
            {
                case WorldHudValueMode.AttributeCurrentOverBase:
                    hash = Mix(hash, (int)value0);
                    hash = Mix(hash, (int)value1);
                    return hash;

                case WorldHudValueMode.AttributeCurrent:
                    return Mix(hash, (int)value0);

                case WorldHudValueMode.Constant:
                    return Mix(hash, BitConverter.SingleToInt32Bits(value0));

                default:
                    return hash;
            }
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
