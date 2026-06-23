using Ludots.Core.Physics2D.Authoring;

namespace CapabilityStandardKnockback2DMod.Runtime;

internal static class CapabilityStandardKnockback2DComponentAuthoring
{
    public static void Register(string modId)
    {
        Physics2DTemplateAuthoring.RegisterRigidBody("CapabilityStandardKnockback2D.RigidBody", modId);
    }
}
