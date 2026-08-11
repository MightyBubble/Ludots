using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS.Config
{
    /// <summary>
    /// Load-time whitelist for ability exec item kinds and effect preset types.
    /// Unknown names fail with the offending identifier — no silent skip.
    /// </summary>
    public static class GasOperatorWhitelist
    {
        private static readonly HashSet<string> ExecItemKinds = new(StringComparer.Ordinal)
        {
            "TagClip",
            "EffectSignal",
            "End",
            "Wait",
            "Cue",
        };

        private static readonly HashSet<string> EffectPresetTypes = new(StringComparer.Ordinal)
        {
            "Buff",
            "Damage",
            "CreateUnit",
            "ApplyGameplayEffect",
            "None",
        };

        public static void ValidateExecItemKind(string kind, string abilityId)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new InvalidOperationException(
                    $"Ability '{abilityId}' has an empty exec item kind.");
            }

            if (!ExecItemKinds.Contains(kind))
            {
                throw new InvalidOperationException(
                    $"Ability '{abilityId}' references exec item kind '{kind}' outside the GAS operator whitelist.");
            }
        }

        public static void ValidateEffectPresetType(string presetType, string effectId)
        {
            if (string.IsNullOrWhiteSpace(presetType))
            {
                return;
            }

            if (!EffectPresetTypes.Contains(presetType))
            {
                throw new InvalidOperationException(
                    $"Effect '{effectId}' references presetType '{presetType}' outside the GAS operator whitelist.");
            }
        }

        public static IReadOnlyCollection<string> ExecKinds => ExecItemKinds;
        public static IReadOnlyCollection<string> PresetTypes => EffectPresetTypes;
    }
}
