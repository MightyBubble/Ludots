using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Config
{
    /// <summary>
    /// Shared runtime entrypoint for instantiating merged entity templates outside map loading.
    /// Reuses the same EntityBuilder/template pipeline as MapLoader.
    /// </summary>
    public sealed class RuntimeEntityTemplateSpawner
    {
        private readonly World _world;
        private readonly Dictionary<string, EntityTemplate> _templates;
        private readonly EntityBuilder _builder;

        public RuntimeEntityTemplateSpawner(World world, DataRegistry<EntityTemplate> templateRegistry)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            ArgumentNullException.ThrowIfNull(templateRegistry);

            _templates = new Dictionary<string, EntityTemplate>(StringComparer.OrdinalIgnoreCase);
            foreach (var template in templateRegistry.GetAll())
            {
                _templates[template.Id] = template;
            }

            _builder = new EntityBuilder(_world, _templates);
        }

        public Entity Spawn(string templateId, Action<World, Entity>? configure = null)
        {
            var entity = _builder.UseTemplate(templateId).Build();
            configure?.Invoke(_world, entity);
            return entity;
        }
    }
}
