namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// A declarative event-driven rule within a <see cref="PerformerDefinition"/>.
    /// When a <see cref="Events.PresentationEvent"/> matches <see cref="Event"/> and
    /// <see cref="Condition"/> evaluates to true, <see cref="Command"/> is executed.
    /// </summary>
    public struct PerformerRule
    {
        /// <summary>Which events this rule reacts to.</summary>
        public EventFilter Event;

        /// <summary>
        /// Optional condition gate. Evaluated only when the event matches.
        /// Default (all zeroes) = always true.
        /// </summary>
        public ConditionRef Condition;

        /// <summary>The command to produce when event matches and condition passes.</summary>
        public PerformerCommand Command;
    }
}
