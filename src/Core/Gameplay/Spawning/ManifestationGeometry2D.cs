namespace Ludots.Core.Gameplay.Spawning
{
    public enum ManifestationPrimitiveKind : byte
    {
        Beam = 0,
        SplineBeam = 1,
        RingPulse = 2,
        DiskWave = 3,
    }

    /// <summary>
    /// Declarative geometry payload for manifestation-backed beam / wave visuals.
    /// Values are authored in centimeters and converted by the presentation layer.
    /// </summary>
    public struct ManifestationGeometry2D
    {
        public ManifestationPrimitiveKind PrimitiveKind;
        public int LengthCm;
        public int WidthCm;
        public int EndWidthCm;
        public int InnerRadiusCm;
        public int OuterRadiusCm;
        public float SweepAngleDeg;
        public byte SegmentCount;
        public int ArcHeightCm;
        public int ControlPoint0XCm;
        public int ControlPoint0YCm;
        public int ControlPoint1XCm;
        public int ControlPoint1YCm;
        public float PulseSpeed;
        public int PulseAmplitudeCm;
    }
}
