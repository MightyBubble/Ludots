namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// Documents the implicit ParamKey conventions used by <see cref="PerformerParamBinding"/>
    /// across all <see cref="PerformerVisualKind"/> types.
    /// Mod developers should reference these constants instead of hard-coding numeric keys.
    /// </summary>
    public static class WellKnownPerformerParamKeys
    {
        // ── WorldBar ──

        /// <summary>Bar fill ratio (0..1). Default: 1.</summary>
        public const int BarFillRatio = 0;
        /// <summary>Bar width in pixels. Default: 40.</summary>
        public const int BarWidth = 1;
        /// <summary>Bar height in pixels. Default: 6.</summary>
        public const int BarHeight = 2;
        /// <summary>Foreground color red channel.</summary>
        public const int BarForegroundR = 4;
        /// <summary>Foreground color green channel.</summary>
        public const int BarForegroundG = 5;
        /// <summary>Foreground color blue channel.</summary>
        public const int BarForegroundB = 6;
        /// <summary>Foreground color alpha channel.</summary>
        public const int BarForegroundA = 7;
        /// <summary>Background color red channel.</summary>
        public const int BarBackgroundR = 8;
        /// <summary>Background color green channel.</summary>
        public const int BarBackgroundG = 9;
        /// <summary>Background color blue channel.</summary>
        public const int BarBackgroundB = 10;
        /// <summary>Background color alpha channel.</summary>
        public const int BarBackgroundA = 11;

        // ── WorldText ──

        /// <summary>Primary numeric value (e.g. damage amount).</summary>
        public const int TextValue0 = 0;
        /// <summary>Secondary numeric value.</summary>
        public const int TextValue1 = 1;
        /// <summary>Font size override.</summary>
        public const int TextFontSize = 3;
        /// <summary>Text color red channel.</summary>
        public const int TextColorR = 4;
        /// <summary>Text color green channel.</summary>
        public const int TextColorG = 5;
        /// <summary>Text color blue channel.</summary>
        public const int TextColorB = 6;
        /// <summary>Text color alpha channel.</summary>
        public const int TextColorA = 7;
        /// <summary>Stable text token ID for localization.</summary>
        public const int TextTokenId = 15;
        /// <summary>WorldHudValueMode ordinal for legacy adapters.</summary>
        public const int TextValueMode = 16;

        // ── GroundOverlay ──

        /// <summary>Outer radius (or uniform scale).</summary>
        public const int OverlayRadius = 0;
        /// <summary>Inner radius (donut hole).</summary>
        public const int OverlayInnerRadius = 1;
        /// <summary>Arc angle in degrees.</summary>
        public const int OverlayAngle = 2;
        /// <summary>Rotation offset in degrees.</summary>
        public const int OverlayRotation = 3;
        /// <summary>Fill color red channel.</summary>
        public const int OverlayFillR = 4;
        /// <summary>Fill color green channel.</summary>
        public const int OverlayFillG = 5;
        /// <summary>Fill color blue channel.</summary>
        public const int OverlayFillB = 6;
        /// <summary>Fill color alpha channel.</summary>
        public const int OverlayFillA = 7;
        /// <summary>Border color red channel.</summary>
        public const int OverlayBorderR = 8;
        /// <summary>Border color green channel.</summary>
        public const int OverlayBorderG = 9;
        /// <summary>Border color blue channel.</summary>
        public const int OverlayBorderB = 10;
        /// <summary>Border color alpha channel.</summary>
        public const int OverlayBorderA = 11;
        /// <summary>Border width.</summary>
        public const int OverlayBorderWidth = 12;
        /// <summary>Rectangle length (for rectangular overlays).</summary>
        public const int OverlayLength = 13;
        /// <summary>Rectangle width (for rectangular overlays).</summary>
        public const int OverlayWidth = 14;

        // ── Marker3D ──

        /// <summary>Uniform scale. Per-axis falls back to this value.</summary>
        public const int MarkerScale = 0;
        /// <summary>Per-axis scale X override.</summary>
        public const int MarkerScaleX = 1;
        /// <summary>Per-axis scale Y override.</summary>
        public const int MarkerScaleY = 2;
        /// <summary>Per-axis scale Z override.</summary>
        public const int MarkerScaleZ = 3;
        /// <summary>Marker color red channel.</summary>
        public const int MarkerColorR = 4;
        /// <summary>Marker color green channel.</summary>
        public const int MarkerColorG = 5;
        /// <summary>Marker color blue channel.</summary>
        public const int MarkerColorB = 6;
        /// <summary>Marker color alpha channel.</summary>
        public const int MarkerColorA = 7;
    }
}
