namespace Ludots.Platform.Abstractions
{
    public readonly struct CameraClipPlanes
    {
        public const float DefaultNearPlaneMeters = 0.1f;
        public const float DefaultFarPlaneMeters = 10000f;
        public const float FarPlaneDistanceMultiplier = 8f;

        public CameraClipPlanes(float nearMeters, float farMeters)
        {
            NearMeters = nearMeters;
            FarMeters = farMeters;
        }

        public float NearMeters { get; }

        public float FarMeters { get; }
    }
}
