namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// A declarative event-driven rule within a <see cref="PresenterDefinition"/>.
    /// When a <see cref="Events.PresentationEvent"/> matches <see cref="Event"/> and
    /// <see cref="Condition"/> evaluates to true, <see cref="Command"/> is executed.
    /// </summary>
    public struct PresenterRule
    {
        /// <summary>
        /// Owning presenter definition id. Filled by the loader/registry so runtime rule
        /// evaluation can target only matching presenter instances.
        /// </summary>
        public int OwnerDefinitionId;

        /// <summary>Which events this rule reacts to.</summary>
        public EventFilter Event;

        /// <summary>
        /// Optional condition gate. Evaluated only when the event matches.
        /// Default (all zeroes) = always true.
        /// </summary>
        public ConditionRef Condition;

        /// <summary>The command to produce when event matches and condition passes.</summary>
        public PresenterCommand Command;
    }
}
