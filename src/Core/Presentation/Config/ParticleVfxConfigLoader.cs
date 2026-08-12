using System;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Particles;

namespace Ludots.Core.Presentation.Config
{
    public sealed class ParticleVfxConfigLoader
    {
        public const string RelativePath = "Presentation/particle_vfx.json";

        private readonly ConfigPipeline _configs;
        private readonly ParticleVfxRegistry _registry;

        public ParticleVfxConfigLoader(ConfigPipeline configs, ParticleVfxRegistry registry)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(
                catalog,
                RelativePath,
                ConfigMergePolicy.ArrayById,
                "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);

            for (int i = 0; i < merged.Count; i++)
            {
                var node = merged[i].Node;
                string key = node["id"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException($"{RelativePath} entry at index {i} is missing required 'id'.");
                }

                ParticleVfxAssetData effect = ParticleVfxConfigParser.ParseCatalogEntry(
                    node,
                    key,
                    RelativePath);
                _registry.Register(key, effect);
            }
        }
    }
}
