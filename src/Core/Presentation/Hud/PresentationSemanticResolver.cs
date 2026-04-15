using System;
using Ludots.Core.Gameplay.Teams;

namespace Ludots.Core.Presentation.Hud
{
    public sealed class PresentationSemanticResolver
    {
        private readonly PresentationTextCatalog _textCatalog;
        private readonly PresentationTextLocaleSelection _localeSelection;
        private readonly PresentationSemanticCatalog _semanticCatalog;

        public PresentationSemanticResolver(
            PresentationTextCatalog textCatalog,
            PresentationTextLocaleSelection localeSelection,
            PresentationSemanticCatalog semanticCatalog)
        {
            _textCatalog = textCatalog ?? throw new ArgumentNullException(nameof(textCatalog));
            _localeSelection = localeSelection ?? throw new ArgumentNullException(nameof(localeSelection));
            _semanticCatalog = semanticCatalog ?? throw new ArgumentNullException(nameof(semanticCatalog));
        }

        public int ActiveLocaleId => _localeSelection.ActiveLocaleId;

        public string ResolveAttributeLabelRequired(string semanticKey)
        {
            PresentationSemanticAttributeDefinition definition = GetRequiredAttribute(semanticKey);
            return ResolveRequiredToken(definition.LabelTokenId);
        }

        public string FormatAttributeValueRequired(
            string semanticKey,
            PresentationAttributeValueDisplayKind displayKind,
            float currentValue,
            float baseValue)
        {
            PresentationSemanticAttributeDefinition definition = GetRequiredAttribute(semanticKey);
            return FormatAttributeValueRequired(definition, displayKind, currentValue, baseValue);
        }

        public bool TryFormatAttributeById(
            int attributeId,
            PresentationAttributeValueDisplayKind displayKind,
            float currentValue,
            float baseValue,
            out string formattedValue)
        {
            formattedValue = string.Empty;
            if (!_semanticCatalog.TryGetAttribute(attributeId, out PresentationSemanticAttributeDefinition definition))
            {
                return false;
            }

            formattedValue = FormatAttributeValueRequired(definition, displayKind, currentValue, baseValue);
            return true;
        }

        public bool TryResolveAttributeLabelById(int attributeId, out string label)
        {
            label = string.Empty;
            if (!_semanticCatalog.TryGetAttribute(attributeId, out PresentationSemanticAttributeDefinition definition))
            {
                return false;
            }

            label = ResolveRequiredToken(definition.LabelTokenId);
            return true;
        }

        private string FormatAttributeValueRequired(
            PresentationSemanticAttributeDefinition definition,
            PresentationAttributeValueDisplayKind displayKind,
            float currentValue,
            float baseValue)
        {
            int formatTokenId = displayKind switch
            {
                PresentationAttributeValueDisplayKind.Current => definition.CurrentFormatTokenId,
                PresentationAttributeValueDisplayKind.CurrentOverBase => definition.CurrentOverBaseFormatTokenId,
                _ => definition.ConstantFormatTokenId,
            };

            var packet = PresentationTextPacket.FromToken(formatTokenId);
            switch (displayKind)
            {
                case PresentationAttributeValueDisplayKind.Current:
                case PresentationAttributeValueDisplayKind.Constant:
                    packet.SetArg(0, CreateNumericArg(currentValue));
                    break;

                case PresentationAttributeValueDisplayKind.CurrentOverBase:
                    packet.SetArg(0, CreateNumericArg(currentValue));
                    packet.SetArg(1, CreateNumericArg(baseValue));
                    break;
            }

            if (!PresentationTextFormatter.TryFormat(_textCatalog, _localeSelection.ActiveLocaleId, in packet, out string formattedValue))
            {
                throw new InvalidOperationException(
                    $"Presentation semantic attribute '{definition.SemanticKey}' could not be formatted for locale '{_localeSelection.ActiveLocaleKey}'.");
            }

            if (definition.UnitTokenId <= 0)
            {
                return formattedValue;
            }

            return $"{formattedValue} {ResolveRequiredToken(definition.UnitTokenId)}";
        }

        public string ResolveMappingLabelRequired(string mappingId)
        {
            PresentationSemanticValueMappingDefinition mapping = GetRequiredMapping(mappingId);
            return ResolveRequiredToken(mapping.LabelTokenId);
        }

        public string ResolveMappedValueRequired(string mappingId, string valueKey)
        {
            PresentationSemanticValueMappingDefinition mapping = GetRequiredMapping(mappingId);
            if (!mapping.TryGetValueTokenId(valueKey, out int tokenId) || tokenId <= 0)
            {
                throw new InvalidOperationException(
                    $"Presentation semantic mapping '{mappingId}' does not define value '{valueKey}'.");
            }

            return ResolveRequiredToken(tokenId);
        }

        public string ResolveMappedRuntimeValueRequired(string mappingId, int runtimeValue)
        {
            PresentationSemanticValueMappingDefinition mapping = GetRequiredMapping(mappingId);
            if (!mapping.TryResolveRuntimeValueKey(runtimeValue, out string valueKey) || string.IsNullOrWhiteSpace(valueKey))
            {
                throw new InvalidOperationException(
                    $"Presentation semantic mapping '{mappingId}' does not define runtime value '{runtimeValue}'.");
            }

            return ResolveMappedValueRequired(mappingId, valueKey);
        }

        public string ResolveMappedRuntimeValueKeyRequired(string mappingId, int runtimeValue)
        {
            PresentationSemanticValueMappingDefinition mapping = GetRequiredMapping(mappingId);
            if (!mapping.TryResolveRuntimeValueKey(runtimeValue, out string valueKey) || string.IsNullOrWhiteSpace(valueKey))
            {
                throw new InvalidOperationException(
                    $"Presentation semantic mapping '{mappingId}' does not define runtime value '{runtimeValue}'.");
            }

            return valueKey;
        }

        public string ResolveTeamRelationshipValueRequired(TeamRelationship relationship)
        {
            return ResolveMappedRuntimeValueRequired(WellKnownPresentationSemanticMappingKeys.TeamRelationship, (int)relationship);
        }

        private PresentationSemanticAttributeDefinition GetRequiredAttribute(string semanticKey)
        {
            if (!_semanticCatalog.TryGetAttribute(semanticKey, out PresentationSemanticAttributeDefinition definition))
            {
                throw new InvalidOperationException($"Presentation semantic attribute definition '{semanticKey}' is not registered.");
            }

            return definition;
        }

        private PresentationSemanticValueMappingDefinition GetRequiredMapping(string mappingId)
        {
            if (!_semanticCatalog.TryGetMapping(mappingId, out PresentationSemanticValueMappingDefinition definition))
            {
                throw new InvalidOperationException($"Presentation semantic mapping '{mappingId}' is not registered.");
            }

            return definition;
        }

        private string ResolveRequiredToken(int tokenId)
        {
            if (!PresentationTextFormatter.TryFormat(
                    _textCatalog,
                    _localeSelection.ActiveLocaleId,
                    PresentationTextPacket.FromToken(tokenId),
                    out string text))
            {
                throw new InvalidOperationException(
                    $"Presentation text token id '{tokenId}' is not available for locale '{_localeSelection.ActiveLocaleKey}'.");
            }

            return text;
        }

        private static PresentationTextArg CreateNumericArg(float value)
        {
            float rounded = MathF.Round(value);
            if (MathF.Abs(value - rounded) < 0.001f)
            {
                return PresentationTextArg.FromInt32((int)rounded);
            }

            return PresentationTextArg.FromFloat32(value, PresentationTextArgFormat.Fixed1);
        }
    }
}
