namespace CapabilityStandardPhysics2DPlaygroundV2Mod.Runtime;

public static class CapabilityStandardPhysics2DPlaygroundV2State
{
    public const string ActiveModeServiceKey = "CapabilityStandardPhysics2DPlaygroundV2.ActiveMode";
    public const string PhysicsOnlyEntityCountServiceKey = "CapabilityStandardPhysics2DPlaygroundV2.PhysicsOnlyEntityCount";
    public const string NavEntityCountServiceKey = "CapabilityStandardPhysics2DPlaygroundV2.NavEntityCount";
    public const string BenchmarkSpawnCountServiceKey = "CapabilityStandardPhysics2DPlaygroundV2.BenchmarkSpawnCount";
    public const string BenchmarkEntityCountServiceKey = "CapabilityStandardPhysics2DPlaygroundV2.BenchmarkEntityCount";
    public const string BenchmarkLastSpawnedServiceKey = "CapabilityStandardPhysics2DPlaygroundV2.BenchmarkLastSpawned";
    public const string BenchmarkLastForcePulseServiceKey = "CapabilityStandardPhysics2DPlaygroundV2.BenchmarkLastForcePulse";
    public const string TotalEntityCountServiceKey = "CapabilityStandardPhysics2DPlaygroundV2.TotalEntityCount";
    public const string StaticPolygonCountServiceKey = "CapabilityStandardPhysics2DPlaygroundV2.StaticPolygonCount";
    public const string FrictionZoneCountServiceKey = "CapabilityStandardPhysics2DPlaygroundV2.FrictionZoneCount";
    public const string ExplosionLastAffectedServiceKey = "CapabilityStandardPhysics2DPlaygroundV2.ExplosionLastAffected";
    public const string ExplosionLastCandidateCountServiceKey = "CapabilityStandardPhysics2DPlaygroundV2.ExplosionLastCandidateCount";
    public const string ExplosionLastDroppedServiceKey = "CapabilityStandardPhysics2DPlaygroundV2.ExplosionLastDropped";
    public const string LastActionServiceKey = "CapabilityStandardPhysics2DPlaygroundV2.LastAction";

    public static bool Enabled;
    public static CapabilityStandardPhysics2DPlaygroundV2Mode ActiveMode = CapabilityStandardPhysics2DPlaygroundV2Mode.PhysicsOnly;
}
