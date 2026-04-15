using System;
using System.Collections.Generic;

namespace Ludots.Core.Presentation.Hud
{
    public sealed class PresentationSemanticValueMappingDefinition
    {
        private readonly Dictionary<string, int> _valueTokenIds;

        public PresentationSemanticValueMappingDefinition(
            string id,
            int labelTokenId,
            Dictionary<string, int> valueTokenIds)
        {
            Id = string.IsNullOrWhiteSpace(id)
                ? throw new ArgumentException("Mapping id must not be empty.", nameof(id))
                : id;
            LabelTokenId = labelTokenId;
            _valueTokenIds = valueTokenIds ?? throw new ArgumentNullException(nameof(valueTokenIds));
        }

        public string Id { get; }

        public int LabelTokenId { get; }

        public bool TryGetValueTokenId(string key, out int tokenId)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                tokenId = 0;
                return false;
            }

            return _valueTokenIds.TryGetValue(key, out tokenId);
        }
    }
}
