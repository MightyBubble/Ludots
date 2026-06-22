namespace CapabilityStandardPhysics2DPlaygroundV2Mod.Runtime;

public static class CapabilityStandardPhysics2DPlaygroundV2State
{
    public const string ActiveModeServiceKey = "CapabilityStandardPhysics2DPlaygroundV2.ActiveMode";
    public const string PhysicsOnlyEntityCountServiceKey = "CapabilityStandardPhysics2DPlaygroundV2.PhysicsOnlyEntityCount";
    public const string NavEntityCountServiceKey = "CapabilityStandardPhysics2DPlaygroundV2.NavEntityCount";

    public static bool Enabled;
    public static CapabilityStandardPhysics2DPlaygroundV2Mode ActiveMode = CapabilityStandardPhysics2DPlaygroundV2Mode.PhysicsOnly;
}
