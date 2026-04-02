using System;
using System.IO;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.Modding;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    public sealed class NavMeshBakeConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        public NavMeshBakeConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public NavMeshBakeConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = NavMeshConfigPaths.BakeConfigPath)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"NavMeshBakeConfig not found in any source for '{relativePath}'.");
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var config = mergedObject.Deserialize<NavMeshBakeConfig>(options);
            if (config == null)
            {
                throw new InvalidOperationException($"Failed to deserialize NavMeshBakeConfig from '{relativePath}'.");
            }

            if (config.Profiles == null || config.Profiles.Count == 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.profiles is empty.");
            }

            if (config.Layers == null || config.Layers.Count == 0)
            {
                config.Layers = new()
                {
                    new NavLayerConfig { Id = "Ground", Layer = 0 }
                };
            }

            if (config.Areas == null)
            {
                config.Areas = new();
            }

            return config;
        }

        public static NavMeshBakeConfig LoadFromRepoRoot(string repoRoot, string relativePath = NavMeshConfigPaths.BakeConfigPath)
        {
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                throw new ArgumentException("repoRoot is required.", nameof(repoRoot));
            }

            string assetsRoot = Path.Combine(Path.GetFullPath(repoRoot), "assets");
            if (!Directory.Exists(assetsRoot))
            {
                throw new DirectoryNotFoundException($"Repo root is missing assets/: {repoRoot}");
            }

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", assetsRoot);

            var pipeline = new ConfigPipeline(vfs, modLoader: null);
            return new NavMeshBakeConfigLoader(pipeline).Load(relativePath: relativePath);
        }
    }
}
