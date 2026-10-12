using System;
using System.Text.Json;

namespace Ludots.Core.Presentation.Rendering
{
    public readonly struct FogDecalColor
    {
        public FogDecalColor(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public readonly byte R;
        public readonly byte G;
        public readonly byte B;
        public readonly byte A;
    }

    public sealed class FogDecalPalette
    {
        public FogDecalPalette(FogDecalColor unseen, FogDecalColor explored, FogDecalColor visible, FogDecalColor denied)
        {
            Unseen = unseen;
            Explored = explored;
            Visible = visible;
            Denied = denied;
        }

        public FogDecalColor Unseen { get; }
        public FogDecalColor Explored { get; }
        public FogDecalColor Visible { get; }
        public FogDecalColor Denied { get; }

        public FogDecalColor For(byte visibility) => visibility switch
        {
            0 => Unseen,
            1 => Explored,
            2 => Visible,
            3 => Denied,
            _ => throw new ArgumentOutOfRangeException(nameof(visibility), visibility, "Unsupported fog visibility."),
        };

        public static FogDecalPalette Read(JsonElement parent, string owner)
        {
            if (!parent.TryGetProperty("fogDecal", out JsonElement node) || node.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"{owner} 缺少 fogDecal。");
            }

            return new FogDecalPalette(
                Color(node, "unseen", owner),
                Color(node, "explored", owner),
                Color(node, "visible", owner),
                Color(node, "denied", owner));
        }

        private static FogDecalColor Color(JsonElement node, string name, string owner)
        {
            if (!node.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 4)
            {
                throw new InvalidOperationException($"{owner} fogDecal.{name} 需要 4 个 0..255 的整数。");
            }

            var channels = new byte[4];
            int index = 0;
            foreach (JsonElement channel in value.EnumerateArray())
            {
                if (!channel.TryGetInt32(out int number) || (uint)number > 255)
                {
                    throw new InvalidOperationException($"{owner} fogDecal.{name}[{index}] 超出 0..255。");
                }

                channels[index++] = (byte)number;
            }

            return new FogDecalColor(channels[0], channels[1], channels[2], channels[3]);
        }
    }
}
