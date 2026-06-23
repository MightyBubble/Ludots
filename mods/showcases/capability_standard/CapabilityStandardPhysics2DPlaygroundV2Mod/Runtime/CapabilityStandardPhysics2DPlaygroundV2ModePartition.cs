namespace CapabilityStandardPhysics2DPlaygroundV2Mod.Runtime;

public enum CapabilityStandardPhysics2DPlaygroundV2Mode : byte
{
    PhysicsOnly = 0,
    Nav = 1
}

public struct CapabilityStandardPhysics2DPlaygroundV2ModePartition
{
    public CapabilityStandardPhysics2DPlaygroundV2Mode Mode;
}
