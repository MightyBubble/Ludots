using System;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>One ability-template cast-mode pin inside an <see cref="InteractionPrefSeed"/>.</summary>
    public readonly record struct InteractionPrefAbilityCastModeSeed(int AbilityTemplateId, int CastModeId);

    /// <summary>
    /// Resolved game-instance seed of <c>Input/interaction_prefs.json</c>. Intent ids live in the
    /// <see cref="CommandIntentProfileRegistry.ProfileIdRegistry"/> id space; dispatch ids in
    /// <see cref="CastDispatchProfileRegistry.ProfileIdRegistry"/>; cast mode ids in the cast-mode
    /// <see cref="Ludots.Core.Registry.StringIntRegistry"/>. <see cref="DefaultCastModeId"/> 0 means
    /// the player default was not seeded. <see cref="AbilityCastModes"/> is empty when the config
    /// omits the list.
    /// </summary>
    public readonly record struct InteractionPrefSeed(
        int CommandIntentId,
        int CastDispatchProfileId,
        int DefaultCastModeId,
        InteractionPrefAbilityCastModeSeed[] AbilityCastModes);

    /// <summary>
    /// Player interaction preferences, simulation-side: the player-level default command intent,
    /// cast dispatch profile, and cast mode, plus per-ability-template overrides where any one
    /// field may be pinned alone. Sparse-optional on the player representative — map binding
    /// seeds the game-instance default when absent and readers fail fast on a missing component.
    /// Ability template ids are <c>AbilityDefinitionRegistry</c> ids. Cast mode 0 means that
    /// field was not seeded; an <c>IsCastMode</c> read of a resolved 0 fails fast. All ids ride
    /// world saves through the auto-discovered unmanaged formatter as raw registry ids.
    /// </summary>
    public struct InteractionPref
    {
        /// <summary>Fixed per-ability-template override capacity; exceeding it fails fast.</summary>
        public const int MaxAbilityOverrides = 8;

        public int DefaultCommandIntentId;
        public int DefaultCastDispatchProfileId;
        public int DefaultCastModeId;

        public int OverrideCount;
        public unsafe fixed int OverrideAbilityIds[MaxAbilityOverrides];
        public unsafe fixed int OverrideCommandIntentIds[MaxAbilityOverrides];
        public unsafe fixed int OverrideCastDispatchProfileIds[MaxAbilityOverrides];
        public unsafe fixed int OverrideCastModeIds[MaxAbilityOverrides];

        /// <summary>
        /// Seed from the resolved game-instance defaults. Command intent and cast dispatch must
        /// be installed ids. Cast mode 0 stays unset; each ability pair pins cast mode alone.
        /// </summary>
        public static InteractionPref FromSeed(in InteractionPrefSeed seed)
        {
            var pref = default(InteractionPref);
            pref.SetPlayerDefault(seed.CommandIntentId, seed.CastDispatchProfileId);
            if (seed.DefaultCastModeId > 0)
            {
                pref.SetDefaultCastMode(seed.DefaultCastModeId);
            }
            else if (seed.DefaultCastModeId < 0)
            {
                throw new InvalidOperationException(
                    $"InteractionPref seed cast mode id must be 0 or a positive registry id (got {seed.DefaultCastModeId}).");
            }

            InteractionPrefAbilityCastModeSeed[] pairs = seed.AbilityCastModes
                ?? Array.Empty<InteractionPrefAbilityCastModeSeed>();
            for (int i = 0; i < pairs.Length; i++)
            {
                pref.SetAbilityOverride(pairs[i].AbilityTemplateId, 0, 0, pairs[i].CastModeId);
            }

            return pref;
        }

        /// <summary>
        /// Write the player-level command intent and cast dispatch. Both ids must be positive —
        /// that pair is complete by contract. Cast mode is a separate field and stays 0 until
        /// <see cref="SetDefaultCastMode"/>.
        /// </summary>
        public void SetPlayerDefault(int commandIntentId, int castDispatchProfileId)
        {
            if (commandIntentId <= 0 || castDispatchProfileId <= 0)
            {
                throw new InvalidOperationException(
                    $"InteractionPref player default requires positive command intent and cast dispatch profile ids (got {commandIntentId}, {castDispatchProfileId}).");
            }

            DefaultCommandIntentId = commandIntentId;
            DefaultCastDispatchProfileId = castDispatchProfileId;
        }

        /// <summary>Write the player-level cast mode. The id must be a positive registry id.</summary>
        public void SetDefaultCastMode(int castModeId)
        {
            if (castModeId <= 0)
            {
                throw new InvalidOperationException(
                    $"InteractionPref player cast mode requires a positive cast mode id (got {castModeId}).");
            }

            DefaultCastModeId = castModeId;
        }

        /// <summary>
        /// Write (or replace) the override for one ability template. A zero id inherits the
        /// player-level default for that field, so an override may pin any one field alone — but
        /// an all-zero override is rejected (it would be a silent no-op).
        /// </summary>
        public void SetAbilityOverride(int abilityTemplateId, int commandIntentId, int castDispatchProfileId, int castModeId)
        {
            if (abilityTemplateId <= 0)
            {
                throw new InvalidOperationException("InteractionPref ability overrides require a positive ability template id.");
            }

            if (commandIntentId <= 0 && castDispatchProfileId <= 0 && castModeId <= 0)
            {
                throw new InvalidOperationException(
                    $"InteractionPref override for ability template {abilityTemplateId} must override at least one field; use {nameof(ClearAbilityOverride)} to remove it.");
            }

            unsafe
            {
                for (int i = 0; i < OverrideCount; i++)
                {
                    if (OverrideAbilityIds[i] == abilityTemplateId)
                    {
                        OverrideCommandIntentIds[i] = commandIntentId;
                        OverrideCastDispatchProfileIds[i] = castDispatchProfileId;
                        OverrideCastModeIds[i] = castModeId;
                        return;
                    }
                }

                if (OverrideCount >= MaxAbilityOverrides)
                {
                    throw new InvalidOperationException(
                        $"InteractionPref already holds {MaxAbilityOverrides} ability overrides; raise {nameof(MaxAbilityOverrides)} or clear stale entries.");
                }

                OverrideAbilityIds[OverrideCount] = abilityTemplateId;
                OverrideCommandIntentIds[OverrideCount] = commandIntentId;
                OverrideCastDispatchProfileIds[OverrideCount] = castDispatchProfileId;
                OverrideCastModeIds[OverrideCount] = castModeId;
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
                    OverrideCastModeIds[i] = OverrideCastModeIds[last];
                    OverrideAbilityIds[last] = 0;
                    OverrideCommandIntentIds[last] = 0;
                    OverrideCastDispatchProfileIds[last] = 0;
                    OverrideCastModeIds[last] = 0;
                    OverrideCount--;
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when an override entry exists for the ability template.</summary>
        public bool TryGetAbilityOverride(
            int abilityTemplateId,
            out int commandIntentId,
            out int castDispatchProfileId,
            out int castModeId)
        {
            commandIntentId = 0;
            castDispatchProfileId = 0;
            castModeId = 0;
            unsafe
            {
                for (int i = 0; i < OverrideCount; i++)
                {
                    if (OverrideAbilityIds[i] == abilityTemplateId)
                    {
                        commandIntentId = OverrideCommandIntentIds[i];
                        castDispatchProfileId = OverrideCastDispatchProfileIds[i];
                        castModeId = OverrideCastModeIds[i];
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
                TryGetAbilityOverride(abilityTemplateId, out int intentId, out _, out _) &&
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
                TryGetAbilityOverride(abilityTemplateId, out _, out int dispatchId, out _) &&
                dispatchId != 0)
            {
                return dispatchId;
            }

            return DefaultCastDispatchProfileId;
        }

        /// <summary>
        /// Cast-mode counterpart of <see cref="ResolveCommandIntent"/>. A resolved 0 means the
        /// field was never seeded; callers that require a mode fail fast rather than invent one.
        /// </summary>
        public int ResolveCastMode(int abilityTemplateId)
        {
            if (abilityTemplateId != 0 &&
                TryGetAbilityOverride(abilityTemplateId, out _, out _, out int castModeId) &&
                castModeId != 0)
            {
                return castModeId;
            }

            return DefaultCastModeId;
        }
    }

    /// <summary>Merged root of <c>Input/interaction_prefs.json</c> (game-instance player-default seed).</summary>
    public sealed class InteractionPrefsConfig
    {
        public InteractionPrefDefaultsDefinition? Defaults { get; set; }
    }

    /// <summary>One <c>defaults.abilityCastModes</c> entry: an ability template pinned to a cast mode.</summary>
    public sealed class InteractionPrefAbilityCastModeDefinition
    {
        public string? AbilityId { get; set; }

        public string? CastModeId { get; set; }
    }

    /// <summary>
    /// The mod-declared player-level default seeded onto bound player representatives.
    /// <see cref="CastModeId"/> and <see cref="AbilityCastModes"/> are optional; omitted means
    /// the cast mode stays unseeded (0) and no ability pins a mode.
    /// </summary>
    public sealed class InteractionPrefDefaultsDefinition
    {
        public string CommandIntentId { get; set; } = string.Empty;

        public string CastDispatchProfileId { get; set; } = string.Empty;

        public string? CastModeId { get; set; }

        public System.Collections.Generic.List<InteractionPrefAbilityCastModeDefinition>? AbilityCastModes { get; set; }
    }
}
