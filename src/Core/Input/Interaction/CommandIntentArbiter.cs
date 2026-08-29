using System;
using Arch.Core;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Frame command-intent arbitration (RFC-0065 INT-7, DEC-14). Resolves which command intent
    /// profile an un-intercepted pointer command action routes through, in the
    /// <see cref="CommandIntentProfileRegistry.ProfileIdRegistry"/> id space.
    /// <para>
    /// Calling convention: the caller first offers the action to the active context's
    /// <c>frameActions</c> via <see cref="CastCommitProfileRegistry.TryExecuteFrameAction"/>; only
    /// when that returns false does the arbiter run. Resolution chain reads the entity-mounted
    /// interaction state: the subject's <see cref="ActiveInteractionContext.CommandIntentProfileId"/>
    /// wins when positive (context explicit); zero on a mounted context means the context declares
    /// no intent and the pointer command does not route; with no context mounted (steady state)
    /// the possessed representative's <see cref="CommandPref"/> player default applies. Never
    /// bubbles to anything else (DEC-14, no fallback). The steady-state branch is the player's
    /// preference, never the active control scheme: switching schemes changes bindings only,
    /// never routing preferences.
    /// </para>
    /// </summary>
    public static class CommandIntentArbiter
    {
        /// <summary>
        /// Resolve the active command intent profile id for one interaction subject; 0 = do not
        /// route. The player preference argument is the possessed representative's component —
        /// callers that cannot present one fail fast upstream rather than passing a fabricated
        /// default.
        /// </summary>
        public static int ResolveActiveCommandIntent(World world, Entity interactionSubject, in CommandPref playerPref)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (world.TryGet<ActiveInteractionContext>(interactionSubject, out ActiveInteractionContext context))
            {
                return context.CommandIntentProfileId;
            }

            return playerPref.DefaultCommandIntentId;
        }
    }
}
