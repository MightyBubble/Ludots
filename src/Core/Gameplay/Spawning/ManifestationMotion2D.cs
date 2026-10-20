namespace Ludots.Core.Gameplay.Spawning
{
    public enum ManifestationFacingSource2D : byte
    {
        None = 0,
        SweepVelocity = 1,
        ParentExecutionTarget = 2,
        ParentCastFacing = 3,
    }

    /// <summary>
    /// Declarative motion hints for manifestation entities that need to keep
    /// following a parent anchor and/or update their facing over time.
    /// </summary>
    public struct ManifestationMotion2D
    {
        public byte FollowParentPosition;
        public ManifestationFacingSource2D FacingSource;
        public float SweepDegreesPerSecond;
        public int ForwardOffsetCm;
    }
}
