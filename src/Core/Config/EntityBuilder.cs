using System.Collections.Generic;
using System.Text.Json.Nodes;
using Arch.Core;

namespace Ludots.Core.Config
{
    public class EntityBuilder
    {
        private readonly World _world;
        private readonly Dictionary<string, EntityTemplate> _templates;
        
        // Temporary storage for components to apply
        private EntityTemplate _activeTemplate;
        private Dictionary<string, JsonNode> _overrides = new Dictionary<string, JsonNode>();

        public EntityBuilder(
            World world,
            Dictionary<string, EntityTemplate> templates)
        {
            _world = world;
            _templates = templates;
        }

        public EntityBuilder UseTemplate(string templateId)
        {
            if (string.IsNullOrWhiteSpace(templateId))
            {
                throw new System.InvalidOperationException("EntityBuilder requires a non-empty template id.");
            }

            if (_templates.TryGetValue(templateId, out var template))
            {
                _activeTemplate = template;
                return this;
            }

            throw new System.InvalidOperationException($"Unknown entity template '{templateId}'.");
        }

        public EntityBuilder WithOverride(string componentName, JsonNode data)
        {
            if (string.IsNullOrWhiteSpace(componentName))
            {
                throw new System.InvalidOperationException("EntityBuilder override requires a non-empty component name.");
            }

            if (data == null)
            {
                throw new System.InvalidOperationException($"EntityBuilder override '{componentName}' requires non-null data.");
            }

            _overrides[componentName] = data;
            return this;
        }

        public Entity Build()
        {
            var entity = _world.Create();

            // 1. Apply Template Components
            if (_activeTemplate != null)
            {
                foreach (var kvp in _activeTemplate.Components)
                {
                    // Check if overridden
                    if (!_overrides.ContainsKey(kvp.Key))
                    {
                        ApplyComponent(entity, kvp.Key, kvp.Value);
                    }
                }
            }

            // 2. Apply Overrides
            foreach (var kvp in _overrides)
            {
                ApplyComponent(entity, kvp.Key, kvp.Value);
            }

            // Reset for next use
            _activeTemplate = null;
            _overrides.Clear();

            return entity;
        }

        private void ApplyComponent(Entity entity, string componentName, JsonNode data)
        {
            if (string.Equals(componentName, "Presentation", System.StringComparison.Ordinal))
            {
                throw new System.InvalidOperationException(
                    "Entity template component 'Presentation' has been removed. Migrate entity visuals to Presentation/performers.json keyed lifecycle rules.");
            }

            ComponentRegistry.Apply(entity, componentName, data);
        }
    }
}
