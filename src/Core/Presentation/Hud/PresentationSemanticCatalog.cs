using System;
using System.Collections.Generic;

namespace Ludots.Core.Presentation.Hud
{
    public sealed class PresentationSemanticCatalog
    {
        public static readonly PresentationSemanticCatalog Empty = new(
            new Dictionary<string, PresentationSemanticAttributeDefinition>(StringComparer.Ordinal),
            new Dictionary<int, PresentationSemanticAttributeDefinition>(),
            new Dictionary<string, PresentationSemanticValueMappingDefinition>(StringComparer.Ordinal));

        private readonly Dictionary<string, PresentationSemanticAttributeDefinition> _attributesByKey;
        private readonly Dictionary<int, PresentationSemanticAttributeDefinition> _attributesById;
        private readonly Dictionary<string, PresentationSemanticValueMappingDefinition> _mappings;

        public PresentationSemanticCatalog(
            Dictionary<string, PresentationSemanticAttributeDefinition> attributesByKey,
            Dictionary<int, PresentationSemanticAttributeDefinition> attributes,
            Dictionary<string, PresentationSemanticValueMappingDefinition> mappings)
        {
            _attributesByKey = attributesByKey ?? throw new ArgumentNullException(nameof(attributesByKey));
            _attributesById = attributes ?? throw new ArgumentNullException(nameof(attributes));
            _mappings = mappings ?? throw new ArgumentNullException(nameof(mappings));
        }

        public bool TryGetAttribute(int attributeId, out PresentationSemanticAttributeDefinition definition)
        {
            return _attributesById.TryGetValue(attributeId, out definition!);
        }

        public bool TryGetAttribute(string semanticKey, out PresentationSemanticAttributeDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(semanticKey))
            {
                definition = null!;
                return false;
            }

            return _attributesByKey.TryGetValue(semanticKey, out definition!);
        }

        public bool TryGetMapping(string mappingId, out PresentationSemanticValueMappingDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(mappingId))
            {
                definition = null!;
                return false;
            }

            return _mappings.TryGetValue(mappingId, out definition!);
        }
    }
}
