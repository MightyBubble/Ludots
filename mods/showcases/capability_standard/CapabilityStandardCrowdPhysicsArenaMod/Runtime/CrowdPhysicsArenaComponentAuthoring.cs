using Ludots.Core.Config;
using Ludots.Core.Layers;
using Ludots.Core.Physics2D.Authoring;

namespace CapabilityStandardCrowdPhysicsArenaMod.Runtime;

internal static class CrowdPhysicsArenaComponentAuthoring
{
    public static void Register(string modId)
    {
        LayerRegistry.Register(CrowdPhysicsArenaLayerNames.Plate);
        LayerRegistry.Register(CrowdPhysicsArenaLayerNames.Prop);
        LayerRegistry.Register(CrowdPhysicsArenaLayerNames.Wall);

        Physics2DTemplateAuthoring.RegisterRigidBody("CrowdPhysicsArena.RigidBody", modId);
        Physics2DTemplateAuthoring.RegisterContactEventEmitter("CrowdPhysicsArena.ContactEventEmitter", modId);
        ComponentRegistry.Register<CrowdPhysicsArenaDoor>("CrowdPhysicsArena.Door", modId);
        ComponentRegistry.Register<CrowdPhysicsArenaPlateAnchor>("CrowdPhysicsArena.PlateAnchor", modId);
    }
}
