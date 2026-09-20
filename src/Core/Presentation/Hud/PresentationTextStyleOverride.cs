using System;

namespace Ludots.Core.Presentation.Hud
{
    public readonly struct PresentationTextStyleOverride : IEquatable<PresentationTextStyleOverride>
    {
        public PresentationTextStyleOverride(bool bold, bool italic, bool hasColor, byte a, byte r, byte g, byte b)
        {
            Bold = bold;
            Italic = italic;
            HasColor = hasColor;
            A = a;
            R = r;
            G = g;
            B = b;
        }

        public static PresentationTextStyleOverride None { get; }

        public bool Bold { get; }

        public bool Italic { get; }

        public bool HasColor { get; }

        public byte A { get; }

        public byte R { get; }

        public byte G { get; }

        public byte B { get; }

        public bool IsEmpty => !Bold && !Italic && !HasColor;

        public static PresentationTextStyleOverride CreateBold() =>
            new PresentationTextStyleOverride(bold: true, italic: false, hasColor: false, a: 0, r: 0, g: 0, b: 0);

        public static PresentationTextStyleOverride CreateItalic() =>
            new PresentationTextStyleOverride(bold: false, italic: true, hasColor: false, a: 0, r: 0, g: 0, b: 0);

        public static PresentationTextStyleOverride CreateColor(byte a, byte r, byte g, byte b) =>
            new PresentationTextStyleOverride(bold: false, italic: false, hasColor: true, a: a, r: r, g: g, b: b);

        public bool Equals(PresentationTextStyleOverride other) =>
            Bold == other.Bold &&
            Italic == other.Italic &&
            HasColor == other.HasColor &&
            A == other.A &&
            R == other.R &&
            G == other.G &&
            B == other.B;

        public override bool Equals(object? obj) =>
            obj is PresentationTextStyleOverride other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Bold, Italic, HasColor, A, R, G, B);
    }
}
