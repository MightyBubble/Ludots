namespace Ludots.Core.Presentation.Camera
{
    public readonly struct CameraClipPlanes
    {
        public CameraClipPlanes(float nearMeters, float farMeters)
        {
            NearMeters = nearMeters;
            FarMeters = farMeters;
        }

        public float NearMeters { get; }

        public float FarMeters { get; }
    }
}
