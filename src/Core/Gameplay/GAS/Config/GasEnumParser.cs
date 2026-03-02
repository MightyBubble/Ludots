using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS.Config
{
    /// <summary>
    /// Centralized string → GAS enum parsing utilities.
    /// Single source of truth for all config loaders (EffectTemplateLoader, PresetTypeLoader, etc.).
    /// All methods are case-insensitive.
    /// </summary>
    public static class GasEnumParser
    {
        // ── EffectPresetType ──

        private static readonly Dictionary<string, EffectPresetType> PresetTypeMap = new(StringComparer.OrdinalIgnoreCase)
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
        };

        /// <summary>
        /// Parse a preset type string. Returns <see cref="EffectPresetType.None"/> for unknown values.
        /// </summary>
        public static EffectPresetType ParsePresetType(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return EffectPresetType.None;
            return PresetTypeMap.TryGetValue(value, out var result) ? result : EffectPresetType.None;
        }

        /// <summary>
        /// Parse a preset type string. Throws on unknown values (for strict config contexts).
        /// </summary>
        public static EffectPresetType ParsePresetTypeStrict(string value, string context)
        {
            if (string.IsNullOrWhiteSpace(value)) return EffectPresetType.None;
            if (PresetTypeMap.TryGetValue(value, out var result)) return result;
            if (value.Equals("None", StringComparison.OrdinalIgnoreCase)) return EffectPresetType.None;
            throw new InvalidOperationException($"{context}: unsupported presetType '{value}'.");
        }

        // ── EffectPhaseId ──

        private static readonly Dictionary<string, EffectPhaseId> PhaseNameMap = new(StringComparer.OrdinalIgnoreCase)
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
        /// Returns false if the name is not recognized.
        /// Accepts both PascalCase ("OnApply") and camelCase ("onApply").
        /// </summary>
        public static bool TryParsePhaseId(string name, out EffectPhaseId phaseId)
        {
            return PhaseNameMap.TryGetValue(name, out phaseId);
        }

        /// <summary>
        /// Parse a phase name to a <see cref="PhaseFlags"/> value.
        /// Returns <see cref="PhaseFlags.None"/> if not recognized.
        /// </summary>
        public static PhaseFlags ParsePhaseFlag(string name)
        {
            if (TryParsePhaseId(name, out var id))
                return id.ToFlag();
            return PhaseFlags.None;
        }

        // ── EffectLifetimeKind / LifetimeFlags ──

        private static readonly Dictionary<string, EffectLifetimeKind> LifetimeKindMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Instant", EffectLifetimeKind.Instant },
            { "After", EffectLifetimeKind.After },
            { "Infinite", EffectLifetimeKind.Infinite },
        };

        /// <summary>
        /// Parse a lifetime string to <see cref="EffectLifetimeKind"/>.
        /// Returns false if not recognized.
        /// </summary>
        public static bool TryParseLifetimeKind(string value, out EffectLifetimeKind kind)
        {
            return LifetimeKindMap.TryGetValue(value, out kind);
        }

        /// <summary>
        /// Parse a lifetime string to <see cref="EffectLifetimeKind"/>. Throws on unknown values.
        /// </summary>
        public static EffectLifetimeKind ParseLifetimeKindStrict(string value, string context)
        {
            if (TryParseLifetimeKind(value, out var kind)) return kind;
            throw new InvalidOperationException($"{context}: unknown lifetime '{value}'.");
        }

        /// <summary>
        /// Parse a lifetime string to a <see cref="LifetimeFlags"/> value.
        /// Returns <see cref="LifetimeFlags.None"/> if not recognized.
        /// </summary>
        public static LifetimeFlags ParseLifetimeFlag(string name)
        {
            if (TryParseLifetimeKind(name, out var kind))
                return kind.ToFlag();
            return LifetimeFlags.None;
        }

        // ── BuiltinHandlerId ──

        private static readonly Dictionary<string, BuiltinHandlerId> BuiltinHandlerMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "ApplyModifiers", BuiltinHandlerId.ApplyModifiers },
            { "SpatialQuery", BuiltinHandlerId.SpatialQuery },
            { "DispatchPayload", BuiltinHandlerId.DispatchPayload },
            { "ReResolveAndDispatch", BuiltinHandlerId.ReResolveAndDispatch },
            { "ApplyForce", BuiltinHandlerId.ApplyForce },
            { "CreateProjectile", BuiltinHandlerId.CreateProjectile },
            { "CreateUnit", BuiltinHandlerId.CreateUnit },
            { "ApplyDisplacement", BuiltinHandlerId.ApplyDisplacement },
        };

        /// <summary>
        /// Parse a builtin handler name string to <see cref="BuiltinHandlerId"/>.
        /// Returns <see cref="BuiltinHandlerId.None"/> if not recognized.
        /// </summary>
        public static BuiltinHandlerId ParseBuiltinHandlerId(string name)
        {
            if (string.IsNullOrEmpty(name)) return BuiltinHandlerId.None;
            return BuiltinHandlerMap.TryGetValue(name, out var result) ? result : BuiltinHandlerId.None;
        }

        // ── ComponentFlags ──

        private static readonly Dictionary<string, ComponentFlags> ComponentFlagMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "ModifierParams", ComponentFlags.ModifierParams },
            { "Modifiers", ComponentFlags.ModifierParams },
            { "DurationParams", ComponentFlags.DurationParams },
            { "Duration", ComponentFlags.DurationParams },
            { "TargetQueryParams", ComponentFlags.TargetQueryParams },
            { "TargetQuery", ComponentFlags.TargetQueryParams },
            { "TargetFilterParams", ComponentFlags.TargetFilterParams },
            { "TargetFilter", ComponentFlags.TargetFilterParams },
            { "TargetDispatchParams", ComponentFlags.TargetDispatchParams },
            { "TargetDispatch", ComponentFlags.TargetDispatchParams },
            { "ForceParams", ComponentFlags.ForceParams },
            { "Force", ComponentFlags.ForceParams },
            { "ProjectileParams", ComponentFlags.ProjectileParams },
            { "Projectile", ComponentFlags.ProjectileParams },
            { "UnitCreationParams", ComponentFlags.UnitCreationParams },
            { "UnitCreation", ComponentFlags.UnitCreationParams },
            { "PhaseGraphBindings", ComponentFlags.PhaseGraphBindings },
            { "PhaseListenerSetup", ComponentFlags.PhaseListenerSetup },
        };

        /// <summary>
        /// Parse a component flag name string to <see cref="ComponentFlags"/>.
        /// Returns <see cref="ComponentFlags.None"/> if not recognized.
        /// </summary>
        public static ComponentFlags ParseComponentFlag(string name)
        {
            if (string.IsNullOrEmpty(name)) return ComponentFlags.None;
            return ComponentFlagMap.TryGetValue(name, out var result) ? result : ComponentFlags.None;
        }
    }
}
