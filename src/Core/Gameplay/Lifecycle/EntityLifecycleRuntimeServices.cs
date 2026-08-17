using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Presenters;

namespace Ludots.Core.Gameplay.Lifecycle
{
    public sealed class EntityLifecycleRuntimeServices
    {
        private readonly World _world;
        private readonly DataRegistry<EntityTemplate> _templateRegistry;
        private readonly EntityTemplateKeyRegistry _templateKeys;
        private readonly PresentationStableIdAllocator _stableIds;
        private readonly PresenterEntitySpawnBootstrap _presenterBootstrap;
        private readonly Dictionary<string, EntityTemplate> _cachedTemplates;
        private readonly EntityBuilder _builder;
        private readonly ComponentAuthoringContext _authoringContext;
        private readonly TagOps _tagOps;

        public EntityLifecycleRuntimeServices(
            World world,
            DataRegistry<EntityTemplate> templateRegistry,
            EntityTemplateKeyRegistry templateKeys,
            PresentationStableIdAllocator stableIds,
            TagOps tagOps,
            PresenterEntityRuntime? presenterRuntime = null,
            PresenterDefinitionRegistry? presenterDefinitions = null,
            ComponentAuthoringContext? authoringContext = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _templateRegistry = templateRegistry ?? throw new ArgumentNullException(nameof(templateRegistry));
            _templateKeys = templateKeys ?? throw new ArgumentNullException(nameof(templateKeys));
            _stableIds = stableIds ?? throw new ArgumentNullException(nameof(stableIds));
            _tagOps = tagOps ?? throw new ArgumentNullException(nameof(tagOps));
            _authoringContext = authoringContext ?? ComponentAuthoringContext.Empty;
            _cachedTemplates = new Dictionary<string, EntityTemplate>(StringComparer.Ordinal);
            _builder = new EntityBuilder(world, _cachedTemplates, _authoringContext);
            _presenterBootstrap = new PresenterEntitySpawnBootstrap(
                world,
                templateKeys,
                stableIds,
                presenterRuntime,
                presenterDefinitions,
                presenterDefinitions?.BootstrapRegistry);
        }

        public World World => _world;
        public TagOps TagOps => _tagOps;

        internal EntityBuilder Builder => _builder;
        internal EntityTemplateKeyRegistry TemplateKeys => _templateKeys;
        internal PresenterEntitySpawnBootstrap PresenterBootstrap => _presenterBootstrap;

        internal EntityTemplate RequireTemplate(string templateId)
        {
            if (_cachedTemplates.TryGetValue(templateId, out EntityTemplate? cached))
            {
                return cached;
            }

            var template = _templateRegistry.Get(templateId)
                ?? throw new InvalidOperationException($"Unknown entity template '{templateId}'.");
            _cachedTemplates[templateId] = template;
            return template;
        }
    }
}
