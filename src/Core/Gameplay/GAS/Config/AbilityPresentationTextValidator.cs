using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.GAS.Config
{
    /// <summary>
    /// Fail-fast validation that ability presentation text references PresentationText tokens
    /// with locale coverage. Does not invent Unknown / Ability#N / empty / English fallbacks.
    /// </summary>
    public static class AbilityPresentationTextValidator
    {
        public static void Validate(
            string abilityId,
            AbilityPresentationConfig presentation,
            PresentationTextCatalog textCatalog,
            string localeKey)
        {
            if (string.IsNullOrWhiteSpace(abilityId))
            {
                throw new ArgumentException("Ability id is required.", nameof(abilityId));
            }

            ArgumentNullException.ThrowIfNull(presentation);
            ArgumentNullException.ThrowIfNull(textCatalog);
            if (string.IsNullOrWhiteSpace(localeKey))
            {
                throw new ArgumentException("Locale key is required.", nameof(localeKey));
            }

            if (!presentation.HasPresentationTokens)
            {
                throw new InvalidOperationException(
                    $"Ability '{abilityId}' presentation has no displayNameToken/hintTextToken/modeHintTokens. " +
                    "Tooltip and tokenized ability text require PresentationText token references; final-string displayName/hintText are migration debt and must not be silently accepted as tokens.");
            }

            int localeId = textCatalog.GetLocaleId(localeKey);
            if (localeId <= 0)
            {
                throw new InvalidOperationException(
                    $"Ability '{abilityId}' presentation validation references unknown locale '{localeKey}'.");
            }

            if (!string.IsNullOrWhiteSpace(presentation.DisplayNameToken))
            {
                RequireTokenAndLocale(abilityId, "displayNameToken", presentation.DisplayNameToken, textCatalog, localeId, localeKey);
            }

            if (!string.IsNullOrWhiteSpace(presentation.HintTextToken))
            {
                RequireTokenAndLocale(abilityId, "hintTextToken", presentation.HintTextToken, textCatalog, localeId, localeKey);
            }

            foreach (KeyValuePair<string, string> pair in presentation.ModeHintTokenOverrides)
            {
                RequireTokenAndLocale(
                    abilityId,
                    $"modeHintTokens.{pair.Key}",
                    pair.Value,
                    textCatalog,
                    localeId,
                    localeKey);
            }
        }

        public static void RejectTokenizedPresentationWithoutLocaleCatalog(AbilityDefinitionRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);
            RegistryMapping[] mappings = AbilityIdRegistry.SnapshotMappings();
            for (int i = 0; i < mappings.Length; i++)
            {
                int abilityId = mappings[i].Id;
                string abilityKey = mappings[i].Name;
                if (!registry.TryGet(abilityId, out AbilityDefinition definition) ||
                    !definition.HasPresentation ||
                    definition.Presentation == null ||
                    !definition.Presentation.HasPresentationTokens)
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"Ability '{abilityKey}' declares presentation text tokens, but PresentationTextCatalog has no default locale. Token/locale coverage cannot be validated.");
            }
        }

        public static void ValidateRegisteredAbilities(
            AbilityDefinitionRegistry registry,
            PresentationTextCatalog textCatalog,
            string localeKey,
            bool requireTokensOnAllPresentations = false)
        {
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(textCatalog);
            if (string.IsNullOrWhiteSpace(localeKey))
            {
                throw new ArgumentException("Locale key is required.", nameof(localeKey));
            }

            RegistryMapping[] mappings = AbilityIdRegistry.SnapshotMappings();
            var errors = new List<string>();
            for (int i = 0; i < mappings.Length; i++)
            {
                int abilityId = mappings[i].Id;
                string abilityKey = mappings[i].Name;
                if (!registry.TryGet(abilityId, out AbilityDefinition definition) || !definition.HasPresentation)
                {
                    continue;
                }

                AbilityPresentationConfig? presentation = definition.Presentation;
                if (presentation == null)
                {
                    errors.Add($"Ability '{abilityKey}' HasPresentation is true but Presentation is null.");
                    continue;
                }

                if (!presentation.HasPresentationTokens)
                {
                    if (requireTokensOnAllPresentations)
                    {
                        errors.Add(
                            $"Ability '{abilityKey}' presentation still uses final-string displayName/hintText/modeHints without token fields.");
                    }

                    continue;
                }

                try
                {
                    Validate(abilityKey, presentation, textCatalog, localeKey);
                }
                catch (InvalidOperationException ex)
                {
                    errors.Add(ex.Message);
                }
            }

            if (errors.Count > 0)
            {
                throw new AggregateException(
                    $"[AbilityPresentationTextValidator] {errors.Count} ability presentation token error(s) for locale '{localeKey}'.",
                    errors.ConvertAll(static e => (Exception)new InvalidOperationException(e)));
            }
        }

        private static void RequireTokenAndLocale(
            string abilityId,
            string fieldPath,
            string tokenKey,
            PresentationTextCatalog textCatalog,
            int localeId,
            string localeKey)
        {
            if (string.IsNullOrWhiteSpace(tokenKey))
            {
                throw new InvalidOperationException(
                    $"Ability '{abilityId}' presentation.{fieldPath} must be a non-empty PresentationText token key.");
            }

            if (string.Equals(tokenKey, "Unknown", StringComparison.OrdinalIgnoreCase) ||
                tokenKey.StartsWith("Ability#", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Ability '{abilityId}' presentation.{fieldPath} must not use fallback token key '{tokenKey}'.");
            }

            int tokenId = textCatalog.GetTokenId(tokenKey);
            if (tokenId <= 0)
            {
                throw new InvalidOperationException(
                    $"Ability '{abilityId}' presentation.{fieldPath} references unknown text token '{tokenKey}'.");
            }

            if (!textCatalog.TryGetTemplate(localeId, tokenId, out _))
            {
                throw new InvalidOperationException(
                    $"Ability '{abilityId}' presentation.{fieldPath} token '{tokenKey}' has no template for locale '{localeKey}'.");
            }
        }
    }
}
