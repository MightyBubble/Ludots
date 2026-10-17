using Ludots.Core.Presentation.Events;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// Specifies which <see cref="PresentationEvent"/>s a <see cref="PresenterRule"/>
    /// reacts to. Both Kind and KeyId must match for the rule to fire.
    /// </summary>
    public struct EventFilter
    {
        public PresentationEventKind Kind;

        /// <summary>
        /// The event KeyId to match.
        /// Negative value (e.g., -1) matches any KeyId.
        /// Non-negative value matches exactly.
        /// </summary>
        public int KeyId;

        public bool Matches(in PresentationEvent evt)
        {
            return evt.Kind == Kind && (KeyId < 0 || evt.KeyId == KeyId);
        }
    }
}
