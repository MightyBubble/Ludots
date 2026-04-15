using System;
using System.Collections.Generic;

namespace Ludots.Core.Presentation.Hud
{
    public sealed class PresentationSemanticValueMappingDefinition
    {
        private readonly Dictionary<string, int> _valueTokenIds;
        private readonly Dictionary<int, string> _runtimeValueKeys;

        public PresentationSemanticValueMappingDefinition(
            string id,
            int labelTokenId,
            Dictionary<string, int> valueTokenIds,
            Dictionary<int, string>? runtimeValueKeys = null)
        {
            Id = string.IsNullOrWhiteSpace(id)
                ? throw new ArgumentException("Mapping id must not be empty.", nameof(id))
                : id;
            LabelTokenId = labelTokenId;
            _valueTokenIds = valueTokenIds ?? throw new ArgumentNullException(nameof(valueTokenIds));
            _runtimeValueKeys = runtimeValueKeys ?? new Dictionary<int, string>();
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

        public bool TryResolveRuntimeValueKey(int runtimeValue, out string key)
        {
            return _runtimeValueKeys.TryGetValue(runtimeValue, out key!);
        }
    }
}
