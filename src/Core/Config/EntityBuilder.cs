using System.Collections.Generic;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Config
{
    public class EntityBuilder
    {
        private readonly World _world;
        private readonly Dictionary<string, EntityTemplate> _templates;
        private readonly IReadOnlyDictionary<string, string> _templateSources;
        private readonly ComponentAuthoringContext _authoringContext;
        
        // Temporary storage for components to apply
        private EntityTemplate _activeTemplate;
        private string _activeTemplateId;
        private string _activeEntityContext;
        private Dictionary<string, JsonNode> _overrides = new Dictionary<string, JsonNode>();

        public EntityBuilder(
            World world,
            Dictionary<string, EntityTemplate> templates,
            IReadOnlyDictionary<string, string> templateSources = null,
            ComponentAuthoringContext authoringContext = null)
        {
            _world = world;
            _templates = templates;
            _templateSources = templateSources;
            _authoringContext = authoringContext ?? ComponentAuthoringContext.Empty;
        }

        public EntityBuilder(
            World world,
            Dictionary<string, EntityTemplate> templates,
            ComponentAuthoringContext authoringContext)
        {
            _world = world;
            _templates = templates;
            _authoringContext = authoringContext ?? ComponentAuthoringContext.Empty;
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
                _activeTemplateId = templateId;
                return this;
            }

            throw new System.InvalidOperationException($"Unknown entity template '{templateId}'.");
        }

        public EntityBuilder WithEntityContext(string context)
        {
            _activeEntityContext = context;
            return this;
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
                        ApplyComponent(entity, kvp.Key, kvp.Value, isOverride: false);
                    }
                }
            }

            // 2. Apply Overrides
            foreach (var kvp in _overrides)
            {
                ApplyComponent(entity, kvp.Key, kvp.Value, isOverride: true);
            }

            if (_world.Has<OrderBuffer>(entity))
            {
                OrderBlackboardStateInstaller.EnsureInstalled(_world, entity);
            }

            if (_world.Has<AbilityStateBuffer>(entity))
            {
                AbilityFormSetRegistry? formSets = _world.Has<AbilityFormSetRef>(entity)
                    ? _authoringContext.Require<AbilityFormSetRegistry>(ComponentAuthoringServiceKeys.AbilityFormSetRegistry)
                    : null;
                AbilityRuntimeStateInstaller.EnsureForAuthoredAbilities(
                    _world,
                    entity,
                    _authoringContext.Require<AbilityDefinitionRegistry>(ComponentAuthoringServiceKeys.AbilityDefinitionRegistry),
                    formSets,
                    BuildEntityContext());
            }

            if (_world.Has<AbilityTagGrantReceiver>(entity))
            {
                AbilityTagGrantReceiverInstaller.EnsureInstalled(_world, entity);
            }
            else if (_world.Has<GameplayTagContainer>(entity))
            {
                TagStateInstaller.EnsureInstalled(_world, entity);
            }
            else if (_world.Has<AttributeBuffer>(entity) && !_world.Has<DirtyFlags>(entity))
            {
                _world.Add(entity, new DirtyFlags());
            }

            // Reset for next use
            _activeTemplate = null;
            _activeTemplateId = null;
            _activeEntityContext = null;
            _overrides.Clear();

            return entity;
        }

        private void ApplyComponent(Entity entity, string componentName, JsonNode data, bool isOverride)
        {
            if (string.Equals(componentName, "Presentation", System.StringComparison.Ordinal))
            {
                throw new System.InvalidOperationException(
                    "Entity template component 'Presentation' has been removed. Migrate entity visuals to Presentation/performers.json keyed lifecycle rules.");
            }

            ComponentRegistry.Apply(
                entity,
                componentName,
                data,
                _authoringContext,
                BuildComponentContext(componentName, isOverride));
        }

        private string BuildComponentContext(string componentName, bool isOverride)
        {
            string templateId = string.IsNullOrWhiteSpace(_activeTemplateId) ? "<no-template>" : _activeTemplateId;
            string context = BuildEntityContext();

            if (isOverride)
            {
                return $"{context} override component '{componentName}'";
            }

            string source = _templateSources != null && _templateSources.TryGetValue(templateId, out string sourceUri)
                ? sourceUri
                : "Entities/templates.json";
            return $"{context} template '{templateId}' source '{source}' component '{componentName}'";
        }

        private string BuildEntityContext()
        {
            string templateId = string.IsNullOrWhiteSpace(_activeTemplateId) ? "<no-template>" : _activeTemplateId;
            return string.IsNullOrWhiteSpace(_activeEntityContext)
                ? $"EntityBuilder template '{templateId}'"
                : _activeEntityContext;
        }
    }
}
