namespace Ludots.Core.Gameplay.GraphBrains;

/// <summary>
/// Binds an entity to a Script graph that acts as its order-driven behavior brain
/// (issue #1536). The host system runs one resident execution frame per entity and
/// re-feeds the order glue contract every tick: I[0] = active order type id (0 when
/// none), B[0] = has active order, B[1] = has pending order, E[0] = the entity
/// itself (also the slice caster). Cross-tick counters live on the entity blackboard
/// via the existing blackboard ops; FSM phase variables follow the FsmState sugar
/// contract. No behavior variables are declared on this component.
/// </summary>
public struct GraphActionBrain
{
    public string ScriptKey;
    public int ThinkEveryNTicks;

    /// <summary>
    /// Birth state for the entity blackboard, written by the host when the brain slot is
    /// allocated (ConfigKeyRegistry space, same buffer the graph ops use). Not a parallel
    /// variable system: graphs keep reading/writing these keys through the normal ops;
    /// the defaults only guarantee first-tick reads succeed.
    /// </summary>
    public (string Key, int Value)[] BlackboardIntDefaults;
    public string[] BlackboardEntityDefaults;
}
