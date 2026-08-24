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
        private static readonly HashSet<string> ExecItemKinds = BuildExecItemKinds();

        private static readonly HashSet<string> EffectPresetTypes =
            new(Enum.GetNames<EffectPresetType>(), StringComparer.Ordinal);

        public static void ValidateExecItemKind(string kind, string abilityId)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new InvalidOperationException(
                    $"Ability '{abilityId}' has an empty exec item kind.");
            }

            if (!ExecItemKinds.Contains(kind) || string.Equals(kind, nameof(ExecItemKind.None), StringComparison.Ordinal))
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

        private static HashSet<string> BuildExecItemKinds()
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in Enum.GetNames<ExecItemKind>())
            {
                if (!string.Equals(name, nameof(ExecItemKind.None), StringComparison.Ordinal))
                {
                    result.Add(name);
                }
            }

            return result;
        }
    }
}
