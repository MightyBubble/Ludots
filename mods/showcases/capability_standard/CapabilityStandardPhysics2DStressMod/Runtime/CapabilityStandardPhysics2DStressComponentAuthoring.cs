using Ludots.Core.Physics2D.Authoring;

namespace CapabilityStandardPhysics2DStressMod.Runtime;

internal static class CapabilityStandardPhysics2DStressComponentAuthoring
{
    public static void Register(string modId)
    {
        Physics2DTemplateAuthoring.RegisterRigidBody("CapabilityStandardPhysics2DStress.RigidBody", modId);
    }
}
