using Ludots.Core.Presentation.Events;

namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// Specifies which <see cref="PresentationEvent"/>s a <see cref="PerformerRule"/>
    /// reacts to. Both Kind and KeyId must match for the rule to fire.
    /// </summary>
    public struct EventFilter
    {
        /// <summary>The event kind that must match exactly.</summary>
        public PresentationEventKind Kind;

        /// <summary>
        /// The event KeyId to match.
        /// Negative value (e.g., -1) matches any KeyId.
        /// Non-negative value matches exactly.
        /// </summary>
        public int KeyId;

        /// <summary>
        /// Returns true if the given event passes this filter.
        /// </summary>
        public bool Matches(in PresentationEvent evt)
        {
            return evt.Kind == Kind && (KeyId < 0 || evt.KeyId == KeyId);
        }
    }
}
