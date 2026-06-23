using Ludots.Core.Config;
using Ludots.Core.Physics2D.Authoring;

namespace CapabilityStandardPhysics2DPlaygroundV2Mod.Runtime;

internal static class CapabilityStandardPhysics2DPlaygroundV2ComponentAuthoring
{
    public static void Register(string modId)
    {
        ComponentRegistry.Register<CapabilityStandardPhysics2DPlaygroundV2ModePartition>(
            "CapabilityStandardPhysics2DPlaygroundV2.ModePartition",
            modId);
        Physics2DTemplateAuthoring.RegisterRigidBody("CapabilityStandardPhysics2DPlaygroundV2.RigidBody", modId);
    }
}
