using System.Collections.Generic;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Diagnostics;
using Ludots.Core.Presentation.Config;

namespace Ludots.Core.Config
{
    public class EntityBuilder
    {
        private readonly World _world;
        private readonly Dictionary<string, EntityTemplate> _templates;
        private readonly PresentationAuthoringContext? _presentationAuthoring;
        
        // Temporary storage for components to apply
        private EntityTemplate _activeTemplate;
        private Dictionary<string, JsonNode> _overrides = new Dictionary<string, JsonNode>();

        public EntityBuilder(
            World world,
            Dictionary<string, EntityTemplate> templates,
            PresentationAuthoringContext? presentationAuthoring = null)
        {
            _world = world;
            _templates = templates;
            _presentationAuthoring = presentationAuthoring;
        }

        public EntityBuilder UseTemplate(string templateId)
        {
            if (_templates.TryGetValue(templateId, out var template))
            {
                _activeTemplate = template;
                return this;
            }
            
            _activeTemplate = null;
            if (!string.IsNullOrWhiteSpace(templateId))
            {
                Log.Warn(in LogChannels.Config, $"Unknown template '{templateId}', spawning entity with overrides only.");
            }
            return this;
        }

        public EntityBuilder WithOverride(string componentName, JsonNode data)
        {
            if (data == null)
            {
                Log.Warn(in LogChannels.Config, $"Override '{componentName}' is null, skipping.");
                return this;
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
            if (string.Equals(componentName, "Presentation", System.StringComparison.OrdinalIgnoreCase))
            {
                if (_presentationAuthoring == null)
                    throw new System.InvalidOperationException("Presentation authoring requires PresentationAuthoringContext.");

                _presentationAuthoring.Apply(entity, data);
                return;
            }

            ComponentRegistry.Apply(entity, componentName, data);
        }
    }
}
