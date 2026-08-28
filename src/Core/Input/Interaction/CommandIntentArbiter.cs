using System;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Frame command-intent arbitration (RFC-0065 INT-7, DEC-14). Resolves which command intent
    /// profile an un-intercepted pointer command action routes through, in the
    /// <see cref="InteractionContextStack.CommandIntentProfileIdRegistry"/> id space.
    /// <para>
    /// Calling convention: the caller first offers the action to the top frame's
    /// <c>frameActions</c> via <see cref="CastCommitProfileRegistry.TryExecuteFrameAction"/>; only
    /// when that returns false does the arbiter run. Resolution chain: the top frame's explicit
    /// <see cref="InteractionContextFrame.CommandIntentProfileId"/> wins; otherwise, if the top
    /// frame is the reserved default frame, the possessed representative's
    /// <see cref="CommandPref"/> player default applies; otherwise 0 — the pointer command does
    /// not route and never bubbles to lower frames (DEC-14, no fallback). The default-frame
    /// branch is the player's preference, never the active control scheme: switching schemes
    /// changes bindings only, never routing preferences.
    /// </para>
    /// </summary>
    public static class CommandIntentArbiter
    {
        /// <summary>
        /// Resolve the active command intent profile id; 0 = do not route. The player preference
        /// argument is the possessed representative's component — callers that cannot present one
        /// fail fast upstream rather than passing a fabricated default.
        /// </summary>
        public static int ResolveActiveCommandIntent(InteractionContextStack stack, in CommandPref playerPref)
        {
            if (stack == null)
            {
                throw new ArgumentNullException(nameof(stack));
            }

            if (!stack.TryPeek(out InteractionContextFrame frame))
            {
                return 0;
            }

            if (frame.CommandIntentProfileId != 0)
            {
                return frame.CommandIntentProfileId;
            }

            if (IsDefaultFrame(stack, in frame))
            {
                return playerPref.DefaultCommandIntentId;
            }

            return 0;
        }

        /// <summary>True when the frame is the reserved bottom frame the player preference applies to.</summary>
        public static bool IsDefaultFrame(InteractionContextStack stack, in InteractionContextFrame frame)
        {
            return stack.ContextIdRegistry.TryGetId(InteractionContextIds.Default, out int defaultContextId) &&
                frame.ContextId == defaultContextId;
        }
    }
}
