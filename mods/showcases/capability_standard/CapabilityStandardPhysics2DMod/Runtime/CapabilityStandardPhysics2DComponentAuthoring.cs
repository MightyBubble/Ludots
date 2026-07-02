using Ludots.Core.Physics2D.Authoring;

namespace CapabilityStandardPhysics2DMod.Runtime;

internal static class CapabilityStandardPhysics2DComponentAuthoring
{
    public static void Register(string modId)
    {
        Physics2DTemplateAuthoring.RegisterRigidBody("CapabilityStandardPhysics2D.RigidBody", modId);
        Physics2DTemplateAuthoring.RegisterDampingField("CapabilityStandardPhysics2D.DampingField", modId);
    }
}
