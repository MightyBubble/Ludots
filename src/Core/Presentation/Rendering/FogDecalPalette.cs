using System;

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
    }
}
