using System;
using System.Collections.Generic;
using Ludots.Core.UI.EntityCommandPanels;

namespace EntityCommandPanelMod.Runtime
{
    internal sealed class EntityCommandPanelCollectionQueryConfigRegistry : IEntityCommandPanelCollectionQueryConfigRegistry
    {
        private readonly Dictionary<string, EntityCommandPanelCollectionQueryConfig> _configs = new(StringComparer.Ordinal);

        public void Register(EntityCommandPanelCollectionQueryConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            string id = RequireKey(config.Id, "entity command panel collection query id");
            string collectionKey = RequireKey(config.CollectionKey, $"entity command panel collection query '{id}' collection key");

            _configs[id] = new EntityCommandPanelCollectionQueryConfig
            {
                Id = id,
                CollectionKey = collectionKey,
                Title = config.Title ?? string.Empty,
                Filter = NormalizeFilter(config.Filter),
                Sort = config.Sort
            };
        }

        public bool TryGet(string id, out EntityCommandPanelCollectionQueryConfig config)
        {
            if (!string.IsNullOrWhiteSpace(id) &&
                _configs.TryGetValue(id.Trim(), out config!))
            {
                return true;
            }

            config = null!;
            return false;
        }

        private static EntityCommandPanelCollectionFilter NormalizeFilter(EntityCommandPanelCollectionFilter filter)
        {
            return filter.Kind == EntityCommandPanelCollectionFilterKind.ActionId
                ? filter with { ActionId = filter.ActionId?.Trim() ?? string.Empty }
                : filter;
        }

        private static string RequireKey(string? value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{label} is required.");
            }

            return value.Trim();
        }
    }
}
