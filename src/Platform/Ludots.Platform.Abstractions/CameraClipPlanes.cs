namespace Ludots.Platform.Abstractions
{
    public readonly struct CameraClipPlanes
    {
        public const float DefaultNearPlaneMeters = 0.1f;
        public const float DefaultFarPlaneMeters = 10000f;
        public const float FarPlaneDistanceMultiplier = 8f;

        // 远距(策略尺度)下 near 若保持 0.1m,near/far 比可达 1e9,24 位深度的分辨率退化到
        // 百米级,整张地图与天空穹顶挤进同一深度桶发生全屏 z-fighting。near 随距离抬升。
        public const float NearPlaneDistanceRatio = 0.01f;

        public CameraClipPlanes(float nearMeters, float farMeters)
        {
            NearMeters = nearMeters;
            FarMeters = farMeters;
        }

        public float NearMeters { get; }

        public float FarMeters { get; }
    }
}
