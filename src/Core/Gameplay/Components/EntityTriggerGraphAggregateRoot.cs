namespace Ludots.Core.Gameplay.Components
{
    /// <summary>
    /// Marks an entity-domain TriggerGraph scope as an attachment-tree aggregate root.
    /// Events with an entity payload may reach this scope from the root or any attached
    /// descendant; the marker carries no runtime state.
    /// </summary>
    public struct EntityTriggerGraphAggregateRoot
    {
    }
}
