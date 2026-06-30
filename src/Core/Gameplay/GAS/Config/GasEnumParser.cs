using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS.Config
{
    /// <summary>
    /// Centralized string → GAS enum parsing utilities.
    /// Single source of truth for all config loaders (EffectTemplateLoader, PresetTypeLoader, etc.).
    /// All methods require exact semantic casing.
    /// </summary>
    public static class GasEnumParser
    {
        // ── EffectPresetType ──

        private static readonly Dictionary<string, EffectPresetType> PresetTypeMap = new(StringComparer.Ordinal)
        {
            { "None", EffectPresetType.None },
            { "ApplyForce2D", EffectPresetType.ApplyForce2D },
            { "InstantDamage", EffectPresetType.InstantDamage },
            { "DoT", EffectPresetType.DoT },
            { "Heal", EffectPresetType.Heal },
            { "HoT", EffectPresetType.HoT },
            { "Buff", EffectPresetType.Buff },
            { "Search", EffectPresetType.Search },
            { "PeriodicSearch", EffectPresetType.PeriodicSearch },
            { "LaunchProjectile", EffectPresetType.LaunchProjectile },
            { "CreateUnit", EffectPresetType.CreateUnit },
            { "Displacement", EffectPresetType.Displacement },
            { "Relation", EffectPresetType.Relation },
            { "Exchange", EffectPresetType.Exchange },
            { "CompleteProgression", EffectPresetType.CompleteProgression },
            { "SubmitOrderFromBlackboard", EffectPresetType.SubmitOrderFromBlackboard },
        };

        /// <summary>
        /// Parse a preset type string. Throws when the value is missing or not an exact semantic key.
        /// </summary>
        public static EffectPresetType ParsePresetType(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("presetType must be explicitly defined.");
            }

            if (PresetTypeMap.TryGetValue(value, out var result))
            {
                return result;
            }

            throw new InvalidOperationException($"Unsupported presetType '{value}'.");
        }

        /// <summary>
        /// Parse a preset type string. Throws on unknown values (for strict config contexts).
        /// </summary>
        public static EffectPresetType ParsePresetTypeStrict(string? value, string context)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{context}: presetType must be explicitly defined.");
            }

            if (PresetTypeMap.TryGetValue(value, out var result)) return result;
            throw new InvalidOperationException($"{context}: unsupported presetType '{value}'.");
        }

        // ── EffectPhaseId ──

        private static readonly Dictionary<string, EffectPhaseId> PhaseNameMap = new(StringComparer.Ordinal)
        {
            { "OnPropose", EffectPhaseId.OnPropose },
            { "OnCalculate", EffectPhaseId.OnCalculate },
            { "OnResolve", EffectPhaseId.OnResolve },
            { "OnHit", EffectPhaseId.OnHit },
            { "OnApply", EffectPhaseId.OnApply },
            { "OnPeriod", EffectPhaseId.OnPeriod },
            { "OnExpire", EffectPhaseId.OnExpire },
            { "OnRemove", EffectPhaseId.OnRemove },
        };

        /// <summary>
        /// Parse a phase name string to <see cref="EffectPhaseId"/>.
        /// Returns false if the name is not recognized exactly.
        /// </summary>
        public static bool TryParsePhaseId(string? name, out EffectPhaseId phaseId)
        {
            if (string.IsNullOrEmpty(name))
            {
                phaseId = default;
                return false;
            }

            return PhaseNameMap.TryGetValue(name, out phaseId);
        }

        /// <summary>
        /// Parse a phase name to a <see cref="PhaseFlags"/> value.
        /// Throws if the name is missing or not recognized exactly.
        /// </summary>
        public static PhaseFlags ParsePhaseFlag(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Effect phase name must be explicitly defined.");
            }

            if (TryParsePhaseId(name, out var id))
            {
                return id.ToFlag();
            }

            throw new InvalidOperationException($"Unsupported effect phase '{name}'.");
        }

        // ── EffectLifetimeKind / LifetimeFlags ──

        private static readonly Dictionary<string, EffectLifetimeKind> LifetimeKindMap = new(StringComparer.Ordinal)
        {
            { "Instant", EffectLifetimeKind.Instant },
            { "After", EffectLifetimeKind.After },
            { "Infinite", EffectLifetimeKind.Infinite },
        };

        /// <summary>
        /// Parse a lifetime string to <see cref="EffectLifetimeKind"/>.
        /// Returns false if not recognized.
        /// </summary>
        public static bool TryParseLifetimeKind(string? value, out EffectLifetimeKind kind)
        {
            if (string.IsNullOrEmpty(value))
            {
                kind = default;
                return false;
            }

            return LifetimeKindMap.TryGetValue(value, out kind);
        }

        /// <summary>
        /// Parse a lifetime string to <see cref="EffectLifetimeKind"/>. Throws on unknown values.
        /// </summary>
        public static EffectLifetimeKind ParseLifetimeKindStrict(string? value, string context)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{context}: lifetime must be explicitly defined.");
            }

            if (TryParseLifetimeKind(value, out var kind)) return kind;
            throw new InvalidOperationException($"{context}: unknown lifetime '{value}'.");
        }

        /// <summary>
        /// Parse a lifetime string to a <see cref="LifetimeFlags"/> value.
        /// Throws if the name is missing or not recognized exactly.
        /// </summary>
        public static LifetimeFlags ParseLifetimeFlag(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Effect lifetime name must be explicitly defined.");
            }

            if (TryParseLifetimeKind(name, out var kind))
            {
                return kind.ToFlag();
            }

            throw new InvalidOperationException($"Unsupported effect lifetime '{name}'.");
        }

        // ── BuiltinHandlerId ──

        private static readonly Dictionary<string, BuiltinHandlerId> BuiltinHandlerMap = new(StringComparer.Ordinal)
        {
            { "ApplyModifiers", BuiltinHandlerId.ApplyModifiers },
            { "SpatialQuery", BuiltinHandlerId.SpatialQuery },
            { "DispatchPayload", BuiltinHandlerId.DispatchPayload },
            { "ReResolveAndDispatch", BuiltinHandlerId.ReResolveAndDispatch },
            { "ApplyForce", BuiltinHandlerId.ApplyForce },
            { "CreateProjectile", BuiltinHandlerId.CreateProjectile },
            { "CreateUnit", BuiltinHandlerId.CreateUnit },
            { "ApplyDisplacement", BuiltinHandlerId.ApplyDisplacement },
            { "ApplyRelation", BuiltinHandlerId.ApplyRelation },
            { "ExecuteExchange", BuiltinHandlerId.ExecuteExchange },
            { "CompleteProgression", BuiltinHandlerId.CompleteProgression },
            { "SubmitOrderFromBlackboard", BuiltinHandlerId.SubmitOrderFromBlackboard },
        };

        /// <summary>
        /// Parse a builtin handler name string to <see cref="BuiltinHandlerId"/>.
        /// Throws if the name is missing or not recognized exactly.
        /// </summary>
        public static BuiltinHandlerId ParseBuiltinHandlerId(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Builtin handler id must be explicitly defined.");
            }

            if (BuiltinHandlerMap.TryGetValue(name, out var result))
            {
                return result;
            }

            throw new InvalidOperationException($"Unsupported builtin handler id '{name}'.");
        }

        // ── ComponentFlags ──

        private static readonly Dictionary<string, ComponentFlags> ComponentFlagMap = new(StringComparer.Ordinal)
        {
            { "ModifierParams", ComponentFlags.ModifierParams },
            { "DurationParams", ComponentFlags.DurationParams },
            { "TargetQueryParams", ComponentFlags.TargetQueryParams },
            { "TargetFilterParams", ComponentFlags.TargetFilterParams },
            { "TargetDispatchParams", ComponentFlags.TargetDispatchParams },
            { "ForceParams", ComponentFlags.ForceParams },
            { "ProjectileParams", ComponentFlags.ProjectileParams },
            { "UnitCreationParams", ComponentFlags.UnitCreationParams },
            { "RelationParams", ComponentFlags.RelationParams },
            { "PhaseGraphBindings", ComponentFlags.PhaseGraphBindings },
            { "PhaseListenerSetup", ComponentFlags.PhaseListenerSetup },
        };

        /// <summary>
        /// Parse a component flag name string to <see cref="ComponentFlags"/>.
        /// Throws if the name is missing or not recognized exactly.
        /// </summary>
        public static ComponentFlags ParseComponentFlag(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Component flag must be explicitly defined.");
            }

            if (ComponentFlagMap.TryGetValue(name, out var result))
            {
                return result;
            }

            throw new InvalidOperationException($"Unsupported component flag '{name}'.");
        }
    }
}
