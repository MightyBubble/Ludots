using System;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class PresentationImageDefinition
    {
        public int ImageAssetId;
        public PresentationImageAssetKind AssetKind;
        public PresentationImageLocatorDefinition[] Locators = Array.Empty<PresentationImageLocatorDefinition>();
        public string? FallbackGlyph;
        public string? FallbackAccentColorHex;
        public string? FallbackSurfaceColorHex;

        public bool TryResolveLocator(string backendId, out PresentationImageLocatorDefinition locator)
        {
            if (!string.IsNullOrWhiteSpace(backendId))
            {
                for (int i = 0; i < Locators.Length; i++)
                {
                    if (string.Equals(Locators[i].BackendId, backendId, StringComparison.OrdinalIgnoreCase))
                    {
                        locator = Locators[i];
                        return true;
                    }
                }
            }

            locator = default;
            return false;
        }

        public bool TryResolveGlyphFallback(out PresentationImageGlyphFallbackDefinition fallback)
        {
            if (!string.IsNullOrWhiteSpace(FallbackGlyph) &&
                !string.IsNullOrWhiteSpace(FallbackAccentColorHex) &&
                !string.IsNullOrWhiteSpace(FallbackSurfaceColorHex))
            {
                fallback = new PresentationImageGlyphFallbackDefinition(
                    FallbackGlyph,
                    FallbackAccentColorHex,
                    FallbackSurfaceColorHex);
                return true;
            }

            fallback = default;
            return false;
        }
    }
}
