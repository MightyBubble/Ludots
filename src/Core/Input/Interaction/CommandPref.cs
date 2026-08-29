using System;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Resolved game-instance seed of <c>Input/command_prefs.json</c>: the player-level default
    /// command intent + cast dispatch profile map binding plants on every bound player
    /// representative that carries no <see cref="CommandPref"/> yet. Intent ids live in the
    /// <see cref="InteractionContextStack.CommandIntentProfileIdRegistry"/> id space; dispatch
    /// ids in <see cref="CastDispatchProfileRegistry.ProfileIdRegistry"/>.
    /// </summary>
    public readonly record struct CommandPrefSeed(int CommandIntentId, int CastDispatchProfileId);

    /// <summary>
    /// Player command preferences, simulation-side: the player-level default command intent and
    /// cast dispatch profile pointer commands route through, plus per-ability-template overrides
    /// where either field may be overridden alone. Sparse-optional on the player representative —
    /// map binding seeds the game-instance default when absent and readers fail fast on a missing
    /// component (no fallback). Granularity: per game instance = the config seed; per player =
    /// the player-level default here; per ability template = the override entries. Ability
    /// template ids are <c>AbilityDefinitionRegistry</c> ids. Both id pairs ride world saves
    /// through the auto-discovered unmanaged formatter as raw registry ids.
    /// </summary>
    public struct CommandPref
    {
        /// <summary>Fixed per-ability-template override capacity; exceeding it fails fast.</summary>
        public const int MaxAbilityOverrides = 8;

        public int DefaultCommandIntentId;
        public int DefaultCastDispatchProfileId;

        public int OverrideCount;
        public unsafe fixed int OverrideAbilityIds[MaxAbilityOverrides];
        public unsafe fixed int OverrideCommandIntentIds[MaxAbilityOverrides];
        public unsafe fixed int OverrideCastDispatchProfileIds[MaxAbilityOverrides];

        /// <summary>Seed from the resolved game-instance defaults; both ids must be installed ids.</summary>
        public static CommandPref FromSeed(in CommandPrefSeed seed)
        {
            var pref = default(CommandPref);
            pref.SetPlayerDefault(seed.CommandIntentId, seed.CastDispatchProfileId);
            return pref;
        }

        /// <summary>
        /// Write the player-level default. Both ids must be positive — the player default is
        /// complete by contract; partial taste is what per-ability overrides express.
        /// </summary>
        public void SetPlayerDefault(int commandIntentId, int castDispatchProfileId)
        {
            if (commandIntentId <= 0 || castDispatchProfileId <= 0)
            {
                throw new InvalidOperationException(
                    $"CommandPref player default requires positive command intent and cast dispatch profile ids (got {commandIntentId}, {castDispatchProfileId}).");
            }

            DefaultCommandIntentId = commandIntentId;
            DefaultCastDispatchProfileId = castDispatchProfileId;
        }

        /// <summary>
        /// Write (or replace) the override for one ability template. A zero id inherits the
        /// player-level default for that field, so an override may pin either field alone — but
        /// an all-zero override is rejected (it would be a silent no-op).
        /// </summary>
        public void SetAbilityOverride(int abilityTemplateId, int commandIntentId, int castDispatchProfileId)
        {
            if (abilityTemplateId <= 0)
            {
                throw new InvalidOperationException("CommandPref ability overrides require a positive ability template id.");
            }

            if (commandIntentId <= 0 && castDispatchProfileId <= 0)
            {
                throw new InvalidOperationException(
                    $"CommandPref override for ability template {abilityTemplateId} must override at least one field; use {nameof(ClearAbilityOverride)} to remove it.");
            }

            unsafe
            {
                for (int i = 0; i < OverrideCount; i++)
                {
                    if (OverrideAbilityIds[i] == abilityTemplateId)
                    {
                        OverrideCommandIntentIds[i] = commandIntentId;
                        OverrideCastDispatchProfileIds[i] = castDispatchProfileId;
                        return;
                    }
                }

                if (OverrideCount >= MaxAbilityOverrides)
                {
                    throw new InvalidOperationException(
                        $"CommandPref already holds {MaxAbilityOverrides} ability overrides; raise {nameof(MaxAbilityOverrides)} or clear stale entries.");
                }

                OverrideAbilityIds[OverrideCount] = abilityTemplateId;
                OverrideCommandIntentIds[OverrideCount] = commandIntentId;
                OverrideCastDispatchProfileIds[OverrideCount] = castDispatchProfileId;
                OverrideCount++;
            }
        }

        /// <summary>Remove the override for one ability template; true when an entry existed.</summary>
        public bool ClearAbilityOverride(int abilityTemplateId)
        {
            unsafe
            {
                for (int i = 0; i < OverrideCount; i++)
                {
                    if (OverrideAbilityIds[i] != abilityTemplateId)
                    {
                        continue;
                    }

                    int last = OverrideCount - 1;
                    OverrideAbilityIds[i] = OverrideAbilityIds[last];
                    OverrideCommandIntentIds[i] = OverrideCommandIntentIds[last];
                    OverrideCastDispatchProfileIds[i] = OverrideCastDispatchProfileIds[last];
                    OverrideAbilityIds[last] = 0;
                    OverrideCommandIntentIds[last] = 0;
                    OverrideCastDispatchProfileIds[last] = 0;
                    OverrideCount--;
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when an override entry exists for the ability template.</summary>
        public bool TryGetAbilityOverride(int abilityTemplateId, out int commandIntentId, out int castDispatchProfileId)
        {
            commandIntentId = 0;
            castDispatchProfileId = 0;
            unsafe
            {
                for (int i = 0; i < OverrideCount; i++)
                {
                    if (OverrideAbilityIds[i] == abilityTemplateId)
                    {
                        commandIntentId = OverrideCommandIntentIds[i];
                        castDispatchProfileId = OverrideCastDispatchProfileIds[i];
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Resolve the effective command intent: the ability override's non-zero id wins, else the
        /// player-level default. <paramref name="abilityTemplateId"/> 0 addresses the whole-command
        /// scope (no single ability in context) and always yields the player default.
        /// </summary>
        public int ResolveCommandIntent(int abilityTemplateId)
        {
            if (abilityTemplateId != 0 &&
                TryGetAbilityOverride(abilityTemplateId, out int intentId, out _) &&
                intentId != 0)
            {
                return intentId;
            }

            return DefaultCommandIntentId;
        }

        /// <summary>Dispatch-profile counterpart of <see cref="ResolveCommandIntent"/>.</summary>
        public int ResolveCastDispatchProfile(int abilityTemplateId)
        {
            if (abilityTemplateId != 0 &&
                TryGetAbilityOverride(abilityTemplateId, out _, out int dispatchId) &&
                dispatchId != 0)
            {
                return dispatchId;
            }

            return DefaultCastDispatchProfileId;
        }
    }

    /// <summary>Merged root of <c>Input/command_prefs.json</c> (game-instance player-default seed).</summary>
    public sealed class CommandPrefsConfig
    {
        public CommandPrefDefaultsDefinition Defaults { get; set; }
    }

    /// <summary>
    /// The mod-declared player-level default pair seeded onto bound player representatives:
    /// the command intent profile pointer commands route through on the default frame and the
    /// cast dispatch profile routed groups fan out through.
    /// </summary>
    public sealed class CommandPrefDefaultsDefinition
    {
        public string CommandIntentId { get; set; } = string.Empty;

        public string CastDispatchProfileId { get; set; } = string.Empty;
    }
}
