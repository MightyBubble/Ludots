namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// The visual output category of a performer.
    /// Determines which draw buffer the PerformerEmitSystem writes to.
    /// </summary>
    public enum PerformerVisualKind : byte
    {
        /// <summary>Ground-projected overlay (circle, cone, line, ring).</summary>
        GroundOverlay = 0,

        /// <summary>3D mesh marker (transient or persistent).</summary>
        Marker3D = 1,

        /// <summary>World-space floating text (damage numbers, labels).</summary>
        WorldText = 2,

        /// <summary>World-space bar (health bar, cast bar).</summary>
        WorldBar = 3,

        /// <summary>Ground-following cubic spline ribbon for roads, lanes, and route highlights.</summary>
        RoadSpline = 4,

        /// <summary>Adapter-neutral decal request emitted through PresentationVisualRequestBuffer.</summary>
        Decal = 5,

        /// <summary>Adapter-neutral VFX request emitted through PresentationVisualRequestBuffer.</summary>
        Vfx = 6,

        /// <summary>Adapter-neutral surface/RVT request emitted through PresentationVisualRequestBuffer.</summary>
        Surface = 7,

        /// <summary>Adapter-neutral material override request emitted through PresentationVisualRequestBuffer.</summary>
        MaterialOverride = 8,

        /// <summary>Adapter-neutral per-instance custom data request emitted through PresentationVisualRequestBuffer.</summary>
        InstanceCustomData = 9,
    }
}
