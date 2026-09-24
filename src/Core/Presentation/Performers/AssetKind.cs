using System;

namespace Ludots.Core.Presentation.Performers
{
    public enum AssetKind : byte
    {
        Mesh = 1,
        SkinnedMesh = 2,
        Decal = 3,
        // Numeric slot 4 is permanently retired with the removed VFX kind.
        Sound = 5,
        Spline = 6,
        WorldHud = 7,
        WorldText = 8,
        GroundOverlay = 9,
        Surface = 10,
        Ring = 11,
        Line = 12,
        SpriteEmitter = 13,
        RibbonEmitter = 14,
        ModelEmitter = 15,
        TrackEmitter = 16,
        RingEmitter = 17,
    }

    public static class AssetKindSemantics
    {
        public static bool TryParseEmitterKind(string value, out AssetKind kind)
        {
            if (!string.IsNullOrEmpty(value) &&
                Enum.TryParse(value, ignoreCase: false, out kind) &&
                string.Equals(value, kind.ToString(), StringComparison.Ordinal) &&
                kind.IsEmitterKind())
            {
                return true;
            }

            kind = default;
            return false;
        }

        public static bool IsVisualProxyKind(this AssetKind kind)
        {
            return kind is AssetKind.Mesh
                or AssetKind.SkinnedMesh
                or AssetKind.Decal
                or AssetKind.Surface ||
                kind.IsConcretePrimitiveKind() ||
                kind.IsEmitterKind();
        }

        public static bool IsConcretePrimitiveKind(this AssetKind kind)
        {
            return kind is AssetKind.Ring
                or AssetKind.Line;
        }

        public static bool IsEmitterKind(this AssetKind kind)
        {
            return kind is AssetKind.SpriteEmitter
                or AssetKind.RibbonEmitter
                or AssetKind.ModelEmitter
                or AssetKind.TrackEmitter
                or AssetKind.RingEmitter;
        }
    }
}
