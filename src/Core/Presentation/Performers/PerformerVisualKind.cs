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

        /// <summary>Primary model projection emitted through performer ownership.</summary>
        Model = 5,
    }
}
