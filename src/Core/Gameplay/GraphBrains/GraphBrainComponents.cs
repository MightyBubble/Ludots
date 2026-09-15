namespace Ludots.Core.Gameplay.GraphBrains;

/// <summary>
/// Binds an entity to a Script graph that acts as its order-driven behavior brain
/// (issue #1536). The host system runs one resident execution frame per entity and
/// re-feeds the OrderGlueKeys entity-blackboard values every tick (active order
/// type/spatial/target, player id, pending flag) and sets the slice caster to the
/// entity itself. Cross-tick counters live on the entity blackboard
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
