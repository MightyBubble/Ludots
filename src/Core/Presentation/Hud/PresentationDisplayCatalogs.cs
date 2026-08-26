using System;
using System.Collections.Generic;

namespace Ludots.Core.Presentation.Hud
{
    public sealed class PresentationSemanticMapCatalog
    {
        public static readonly PresentationSemanticMapCatalog Empty = new();

        private readonly Dictionary<(PresentationSemanticDomain Domain, string Key), PresentationSemanticMapDefinition> _byDomainKey =
            new();

        private readonly Dictionary<string, PresentationSemanticMapDefinition> _byId =
            new(StringComparer.OrdinalIgnoreCase);

        public int Count => _byId.Count;

        public void Register(PresentationSemanticMapDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (string.IsNullOrWhiteSpace(definition.Id))
            {
                throw new InvalidOperationException("Presentation semantic map id is required.");
            }

            if (string.IsNullOrWhiteSpace(definition.Key))
            {
                throw new InvalidOperationException($"Presentation semantic map '{definition.Id}' requires key.");
            }

            if (string.IsNullOrWhiteSpace(definition.TextToken))
            {
                throw new InvalidOperationException($"Presentation semantic map '{definition.Id}' requires textToken.");
            }

            var domainKey = (definition.Domain, definition.Key);
            if (_byDomainKey.ContainsKey(domainKey))
            {
                throw new InvalidOperationException(
                    $"Duplicate presentation semantic map for domain '{definition.Domain}' key '{definition.Key}'.");
            }

            if (!_byId.TryAdd(definition.Id, definition))
            {
                throw new InvalidOperationException($"Duplicate presentation semantic map id '{definition.Id}'.");
            }

            _byDomainKey[domainKey] = definition;
        }

        public bool TryGet(PresentationSemanticDomain domain, string key, out PresentationSemanticMapDefinition definition)
            => _byDomainKey.TryGetValue((domain, key ?? string.Empty), out definition!);

        public bool TryGetById(string id, out PresentationSemanticMapDefinition definition)
            => _byId.TryGetValue(id ?? string.Empty, out definition!);
    }

    public sealed class PresentationImageAssetCatalog
    {
        public static readonly PresentationImageAssetCatalog Empty = new();

        private readonly Dictionary<string, PresentationImageAssetDefinition> _byId =
            new(StringComparer.OrdinalIgnoreCase);

        public int Count => _byId.Count;

        public void Register(PresentationImageAssetDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (string.IsNullOrWhiteSpace(definition.Id))
            {
                throw new InvalidOperationException("Presentation image asset id is required.");
            }

            if (string.IsNullOrWhiteSpace(definition.Path) && string.IsNullOrWhiteSpace(definition.GlyphFallback))
            {
                throw new InvalidOperationException(
                    $"Presentation image asset '{definition.Id}' requires path or glyphFallback.");
            }

            if (!_byId.TryAdd(definition.Id, definition))
            {
                throw new InvalidOperationException($"Duplicate presentation image asset id '{definition.Id}'.");
            }
        }

        public bool TryGet(string id, out PresentationImageAssetDefinition definition)
            => _byId.TryGetValue(id ?? string.Empty, out definition!);

        public PresentationImageAssetDefinition Require(string id)
        {
            if (!TryGet(id, out PresentationImageAssetDefinition definition))
            {
                throw new InvalidOperationException(
                    $"Presentation image asset '{id}' is not registered. Author it under Presentation/image_assets.json.");
            }

            return definition;
        }
    }
}
