using System;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>One player-level preference pair inside an <see cref="InteractionPrefSeed"/>.</summary>
    public readonly record struct InteractionPrefPairSeed(int KeyId, int ValueId);

    /// <summary>One ability-template preference pair inside an <see cref="InteractionPrefSeed"/>.</summary>
    public readonly record struct InteractionPrefAbilityPrefSeed(int AbilityTemplateId, int KeyId, int ValueId);

    /// <summary>
    /// Resolved game-instance seed of <c>Input/interaction_prefs.json</c>. Intent ids live in the
    /// command-intent profile registry; dispatch ids in the cast-dispatch profile registry;
    /// preference ids in <see cref="InteractionPrefCatalog"/>.
    /// </summary>
    public readonly record struct InteractionPrefSeed(
        int CommandIntentId,
        int CastDispatchProfileId,
        InteractionPrefPairSeed[] PlayerPrefs,
        InteractionPrefAbilityPrefSeed[] AbilityPrefs);

    /// <summary>
    /// Player interaction preferences on the representative. Command intent and cast dispatch
    /// stay a complete player-level pair plus per-ability overrides. Preference keys are a
    /// separate fixed table: player-level pairs, and per-ability-template pairs that may pin
    /// a key without touching intent or dispatch. A resolved preference of 0 was never seeded;
    /// readers fail fast instead of inventing a value. All ids ride world saves through the
    /// auto-discovered unmanaged formatter as raw registry ids.
    /// </summary>
    public struct InteractionPref
    {
        /// <summary>Fixed per-ability-template intent/dispatch override capacity.</summary>
        public const int MaxAbilityOverrides = 8;

        /// <summary>Fixed player-level preference pair capacity.</summary>
        public const int MaxPlayerPrefs = 8;

        /// <summary>Fixed per-ability preference pair capacity. One ability may pin several keys.</summary>
        public const int MaxAbilityPrefs = 16;

        public int DefaultCommandIntentId;
        public int DefaultCastDispatchProfileId;

        public int OverrideCount;
        public unsafe fixed int OverrideAbilityIds[MaxAbilityOverrides];
        public unsafe fixed int OverrideCommandIntentIds[MaxAbilityOverrides];
        public unsafe fixed int OverrideCastDispatchProfileIds[MaxAbilityOverrides];

        public int PlayerPrefCount;
        public unsafe fixed int PlayerPrefKeyIds[MaxPlayerPrefs];
        public unsafe fixed int PlayerPrefValueIds[MaxPlayerPrefs];

        public int AbilityPrefCount;
        public unsafe fixed int AbilityPrefAbilityIds[MaxAbilityPrefs];
        public unsafe fixed int AbilityPrefKeyIds[MaxAbilityPrefs];
        public unsafe fixed int AbilityPrefValueIds[MaxAbilityPrefs];

        /// <summary>Seed from the resolved game-instance defaults. Intent and dispatch must be installed ids.</summary>
        public static InteractionPref FromSeed(in InteractionPrefSeed seed)
        {
            var pref = default(InteractionPref);
            pref.SetPlayerDefault(seed.CommandIntentId, seed.CastDispatchProfileId);
            InteractionPrefPairSeed[] pairs = seed.PlayerPrefs ?? Array.Empty<InteractionPrefPairSeed>();
            for (int i = 0; i < pairs.Length; i++)
            {
                pref.SetPlayerPref(pairs[i].KeyId, pairs[i].ValueId);
            }

            InteractionPrefAbilityPrefSeed[] abilityPrefs = seed.AbilityPrefs ?? Array.Empty<InteractionPrefAbilityPrefSeed>();
            for (int i = 0; i < abilityPrefs.Length; i++)
            {
                pref.SetAbilityPref(abilityPrefs[i].AbilityTemplateId, abilityPrefs[i].KeyId, abilityPrefs[i].ValueId);
            }

            return pref;
        }

        /// <summary>Write the player-level command intent and cast dispatch. Both ids must be positive.</summary>
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

        /// <summary>
        /// Write (or replace) the intent/dispatch override for one ability template.
        /// A zero id inherits the player-level default for that field. An all-zero override is rejected.
        /// </summary>
        public void SetAbilityOverride(int abilityTemplateId, int commandIntentId, int castDispatchProfileId)
        {
            if (abilityTemplateId <= 0)
            {
                throw new InvalidOperationException("InteractionPref ability overrides require a positive ability template id.");
            }

            if (commandIntentId <= 0 && castDispatchProfileId <= 0)
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
                OverrideCount++;
            }
        }

        /// <summary>Remove the intent/dispatch override for one ability template; true when an entry existed.</summary>
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

        /// <summary>True when an intent/dispatch override entry exists for the ability template.</summary>
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
        /// Write (or replace) one player-level preference. Both ids must be positive.
        /// A full table fails fast; replacing an existing key does not grow the table.
        /// </summary>
        public void SetPlayerPref(int keyId, int valueId)
        {
            if (keyId <= 0 || valueId <= 0)
            {
                throw new InvalidOperationException(
                    $"InteractionPref player preference requires positive key and value ids (got {keyId}, {valueId}).");
            }

            unsafe
            {
                for (int i = 0; i < PlayerPrefCount; i++)
                {
                    if (PlayerPrefKeyIds[i] == keyId)
                    {
                        PlayerPrefValueIds[i] = valueId;
                        return;
                    }
                }

                if (PlayerPrefCount >= MaxPlayerPrefs)
                {
                    throw new InvalidOperationException(
                        $"InteractionPref already holds {MaxPlayerPrefs} player preferences; raise {nameof(MaxPlayerPrefs)} or replace an existing key.");
                }

                PlayerPrefKeyIds[PlayerPrefCount] = keyId;
                PlayerPrefValueIds[PlayerPrefCount] = valueId;
                PlayerPrefCount++;
            }
        }

        /// <summary>
        /// Write (or replace) one ability-template preference. Does not create an intent/dispatch override.
        /// Replacing the same ability and key does not grow the table.
        /// </summary>
        public void SetAbilityPref(int abilityTemplateId, int keyId, int valueId)
        {
            if (abilityTemplateId <= 0 || keyId <= 0 || valueId <= 0)
            {
                throw new InvalidOperationException(
                    $"InteractionPref ability preference requires positive ability, key, and value ids (got {abilityTemplateId}, {keyId}, {valueId}).");
            }

            unsafe
            {
                for (int i = 0; i < AbilityPrefCount; i++)
                {
                    if (AbilityPrefAbilityIds[i] == abilityTemplateId && AbilityPrefKeyIds[i] == keyId)
                    {
                        AbilityPrefValueIds[i] = valueId;
                        return;
                    }
                }

                if (AbilityPrefCount >= MaxAbilityPrefs)
                {
                    throw new InvalidOperationException(
                        $"InteractionPref already holds {MaxAbilityPrefs} ability preferences; raise {nameof(MaxAbilityPrefs)} or replace an existing pair.");
                }

                AbilityPrefAbilityIds[AbilityPrefCount] = abilityTemplateId;
                AbilityPrefKeyIds[AbilityPrefCount] = keyId;
                AbilityPrefValueIds[AbilityPrefCount] = valueId;
                AbilityPrefCount++;
            }
        }

        /// <summary>
        /// Resolve the effective command intent: the ability override's non-zero id wins, else the
        /// player-level default. <paramref name="abilityTemplateId"/> 0 addresses the whole-command
        /// scope and always yields the player default.
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

        /// <summary>
        /// Ability-template pair wins when one exists for <paramref name="keyId"/>; otherwise the
        /// player-level pair. <paramref name="abilityTemplateId"/> 0 reads the player level only.
        /// Returns 0 when neither level stored the key.
        /// </summary>
        public int ResolvePref(int abilityTemplateId, int keyId)
        {
            if (keyId <= 0)
            {
                return 0;
            }

            unsafe
            {
                if (abilityTemplateId != 0)
                {
                    for (int i = 0; i < AbilityPrefCount; i++)
                    {
                        if (AbilityPrefAbilityIds[i] == abilityTemplateId && AbilityPrefKeyIds[i] == keyId)
                        {
                            return AbilityPrefValueIds[i];
                        }
                    }
                }

                for (int i = 0; i < PlayerPrefCount; i++)
                {
                    if (PlayerPrefKeyIds[i] == keyId)
                    {
                        return PlayerPrefValueIds[i];
                    }
                }
            }

            return 0;
        }
    }

    /// <summary>Merged root of <c>Input/interaction_prefs.json</c>.</summary>
    public sealed class InteractionPrefsConfig
    {
        public System.Collections.Generic.List<InteractionPrefKeyDefinition>? Keys { get; set; }

        public InteractionPrefDefaultsDefinition? Defaults { get; set; }
    }

    /// <summary>One declared preference key. <see cref="ContextValued"/> means every value is an interaction context profile id.</summary>
    public sealed class InteractionPrefKeyDefinition
    {
        public string? Id { get; set; }

        public bool ContextValued { get; set; }

        public System.Collections.Generic.List<string>? Values { get; set; }
    }

    /// <summary>One seeded player-level preference.</summary>
    public sealed class InteractionPrefPairDefinition
    {
        public string? Key { get; set; }

        public string? Value { get; set; }
    }

    /// <summary>One seeded ability-template preference. It does not pin intent or dispatch.</summary>
    public sealed class InteractionPrefAbilityPrefDefinition
    {
        public string? AbilityId { get; set; }

        public string? Key { get; set; }

        public string? Value { get; set; }
    }

    /// <summary>The mod-declared player-level default seeded onto bound player representatives.</summary>
    public sealed class InteractionPrefDefaultsDefinition
    {
        public string CommandIntentId { get; set; } = string.Empty;

        public string CastDispatchProfileId { get; set; } = string.Empty;

        public System.Collections.Generic.List<InteractionPrefPairDefinition>? Prefs { get; set; }

        public System.Collections.Generic.List<InteractionPrefAbilityPrefDefinition>? AbilityPrefs { get; set; }
    }
}
