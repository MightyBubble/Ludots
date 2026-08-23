namespace CapabilityStandardCrowdPhysicsArenaMod.Runtime;

/// <summary>
/// Entity layer vocabulary of the arena. The plate layer is the only contact event
/// emitter layer and must be listed in `Physics2D/kinematic.json` contactEventEmitterLayers.
/// </summary>
public static class CrowdPhysicsArenaLayerNames
{
    public const string Plate = "arena.plate";
    public const string Prop = "arena.prop";
    public const string Wall = "arena.wall";
}
