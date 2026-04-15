using System;

namespace Ludots.Core.Presentation.Assets
{
    public readonly struct PresentationImageGlyphFallbackDefinition
    {
        public PresentationImageGlyphFallbackDefinition(
            string glyph,
            string accentColorHex,
            string surfaceColorHex)
        {
            Glyph = string.IsNullOrWhiteSpace(glyph)
                ? throw new ArgumentException("Presentation image glyph fallback must define a glyph.", nameof(glyph))
                : glyph.Trim();
            AccentColorHex = string.IsNullOrWhiteSpace(accentColorHex)
                ? throw new ArgumentException("Presentation image glyph fallback must define an accent color.", nameof(accentColorHex))
                : accentColorHex.Trim();
            SurfaceColorHex = string.IsNullOrWhiteSpace(surfaceColorHex)
                ? throw new ArgumentException("Presentation image glyph fallback must define a surface color.", nameof(surfaceColorHex))
                : surfaceColorHex.Trim();
        }

        public string Glyph { get; }

        public string AccentColorHex { get; }

        public string SurfaceColorHex { get; }
    }
}
