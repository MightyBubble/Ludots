namespace Ludots.Core.Gameplay.GraphBrains;

/// <summary>
/// Binds an entity to a behavior that reads the order glue and acts as its brain.
/// Two mutually exclusive sources: <see cref="HfsmId"/> names an HFSM definition in
/// AI/hfsm.json (driven by HfsmBrainHostSystem, the intended authoring surface), and
/// <see cref="ScriptKey"/> names a single Script graph (the legacy single-graph host).
/// Either host runs one behavior instance per entity, re-feeds the OrderGlueKeys
/// entity-blackboard values every tick (active order type/spatial/target, player id,
/// pending flag) and sets the slice caster to the entity itself. Cross-tick counters
/// live on the entity blackboard via the existing blackboard ops; the HFSM stack itself
/// is owned by the HFSM runtime, never duplicated onto the entity. No behavior
/// variables are declared on this component.
/// </summary>
public struct GraphActionBrain
{
    /// <summary>HFSM definition id (AI/hfsm.json) this entity runs. Empty when the entity uses <see cref="ScriptKey"/>.</summary>
    public string HfsmId;

    /// <summary>Behavior tree definition id (AI/behavior_trees.json) this entity runs. Empty when using HfsmId or ScriptKey.</summary>
    public string BtId;

    /// <summary>Single Script graph key (GAS graphs) this entity runs. Empty when the entity uses <see cref="HfsmId"/>.</summary>
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

/// <summary>
/// Per-entity HFSM runtime state, following the existing component-carried FSM pattern
/// (cf. AnimatorRuntimeState): each entity owns its own current HFSM leaf; the driver
/// system iterates entities carrying this component, so lifecycle follows the entity
/// (Spawn -> add, Destroy -> removed) with no separate agent pool, index, or release.
/// </summary>
public struct HfsmState
{
    /// <summary>Current HFSM leaf state index in the shared HfsmDefinition (NoState = -1 until bound).</summary>
    public const int NoState = -1;
    public int LeafIndex;
    /// <summary>Whether <see cref="LeafIndex"/> has been bound to the definition's default leaf.</summary>
    public bool Bound;
    /// <summary>Ticks the entity has remained in the current leaf (for per-state timing plans).</summary>
    public int StateTicks;
}

/// <summary>
/// Per-entity behavior tree runtime state, following the same component-carried pattern
/// as HfsmState and AnimatorRuntimeState: each entity owns its own BT execution status.
/// The tree definition is shared (BehaviorTreeDefinition); this component holds only the
/// per-entity execution state. Lifecycle follows the entity via ECS.
/// </summary>
public struct BtState
{
    /// <summary>Whether this entity's BT has been bound to its definition's root.</summary>
    public bool Bound;
    /// <summary>Last tick's overall tree result (Running/Success/Failure).</summary>
    public byte Status;
    /// <summary>Ticks the entity has been bound (for think-interval plans).</summary>
    public int StateTicks;
}


