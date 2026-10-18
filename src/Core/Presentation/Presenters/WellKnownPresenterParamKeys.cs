namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// Documents the implicit ParamKey conventions used by <see cref="PresenterParamBinding"/>
    /// across presenter behavior configurations.
    /// Mod developers should reference these constants instead of hard-coding numeric keys.
    /// </summary>
    public static class WellKnownPresenterParamKeys
    {
        // ── WorldBar ──

        /// <summary>Bar fill ratio (0..1). Default: 1.</summary>
        public const int BarFillRatio = 0;
        /// <summary>Bar width in pixels. Default: 40.</summary>
        public const int BarWidth = 1;
        /// <summary>Bar height in pixels. Default: 6.</summary>
        public const int BarHeight = 2;
        public const int BarForegroundR = 4;
        public const int BarForegroundG = 5;
        public const int BarForegroundB = 6;
        public const int BarForegroundA = 7;
        public const int BarBackgroundR = 8;
        public const int BarBackgroundG = 9;
        public const int BarBackgroundB = 10;
        public const int BarBackgroundA = 11;

        // ── WorldText ──

        /// <summary>Primary numeric value (e.g. damage amount).</summary>
        public const int TextValue0 = 0;
        /// <summary>Secondary numeric value.</summary>
        public const int TextValue1 = 1;
        /// <summary>Font size override.</summary>
        public const int TextFontSize = 3;
        public const int TextColorR = 4;
        public const int TextColorG = 5;
        public const int TextColorB = 6;
        public const int TextColorA = 7;
        /// <summary>Stable text token ID for localization.</summary>
        public const int TextTokenId = 15;
        // ── GroundOverlay ──

        /// <summary>Outer radius (or uniform scale).</summary>
        public const int OverlayRadius = 0;
        /// <summary>Inner radius (donut hole).</summary>
        public const int OverlayInnerRadius = 1;
        /// <summary>Arc angle in degrees.</summary>
        public const int OverlayAngle = 2;
        /// <summary>Rotation offset in degrees.</summary>
        public const int OverlayRotation = 3;
        public const int OverlayFillR = 4;
        public const int OverlayFillG = 5;
        public const int OverlayFillB = 6;
        public const int OverlayFillA = 7;
        public const int OverlayBorderR = 8;
        public const int OverlayBorderG = 9;
        public const int OverlayBorderB = 10;
        public const int OverlayBorderA = 11;
        public const int OverlayBorderWidth = 12;
        /// <summary>Rectangle length (for rectangular overlays).</summary>
        public const int OverlayLength = 13;
        /// <summary>Rectangle width (for rectangular overlays).</summary>
        public const int OverlayWidth = 14;

        // ── WorldSpline ──

        /// <summary>Spline start control point. Defaults to presenter world position.</summary>
        public const int SplineP0 = 20;
        /// <summary>Spline first Bezier control point.</summary>
        public const int SplineP1 = 21;
        /// <summary>Spline second Bezier control point.</summary>
        public const int SplineP2 = 22;
        /// <summary>Spline end control point.</summary>
        public const int SplineP3 = 23;
        /// <summary>Spline ribbon width.</summary>
        public const int SplineWidth = 24;
        public const int SplineFillColor = 25;
        public const int SplineBorderColor = 26;
        public const int SplineBorderWidth = 27;

        // ── Marker3D ──

        /// <summary>Uniform scale. Per-axis uses this value when no axis-specific value is authored.</summary>
        public const int MarkerScale = 0;
        /// <summary>Per-axis scale X override.</summary>
        public const int MarkerScaleX = 1;
        /// <summary>Per-axis scale Y override.</summary>
        public const int MarkerScaleY = 2;
        /// <summary>Per-axis scale Z override.</summary>
        public const int MarkerScaleZ = 3;
        public const int MarkerColorR = 4;
        public const int MarkerColorG = 5;
        public const int MarkerColorB = 6;
        public const int MarkerColorA = 7;
    }
}
