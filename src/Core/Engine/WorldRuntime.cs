using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Hosting;
using Ludots.Core.Modding;
using Ludots.Core.Spatial;

namespace Ludots.Core.Engine
{
    public sealed class WorldRuntime : IDisposable
    {
        public GameEngine Engine { get; }
        public WorldSizeSpec SizeSpec => Engine.WorldSizeSpec;

        private WorldRuntime(GameEngine engine)
        {
            Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public static WorldRuntime Create(GameConfig config, string assetsRoot)
        {
            var baseDir = Path.GetDirectoryName(Path.GetFullPath(assetsRoot)) ?? ".";
            var modPaths = config?.ModPaths ?? new List<string>();
            var orderedMods = new List<ResolvedModLoadEntry>();
            for (int i = 0; i < modPaths.Count; i++)
            {
                var raw = modPaths[i];
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var path = Path.IsPathRooted(raw) ? raw : Path.Combine(baseDir, raw);
                var fullPath = Path.GetFullPath(path);
                var manifestPath = Path.Combine(fullPath, "mod.json");
                if (!File.Exists(manifestPath))
                {
                    throw new FileNotFoundException($"mod.json not found in mod directory: {fullPath}");
                }

                var manifest = ModManifestJson.ParseStrict(File.ReadAllText(manifestPath), manifestPath)
                    ?? throw new InvalidOperationException($"Failed to parse mod manifest from '{manifestPath}'.");
                orderedMods.Add(new ResolvedModLoadEntry(manifest.Name, fullPath));
            }
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(ResolvedModLoadPlan.CreateExplicit(orderedMods), assetsRoot);
            return new WorldRuntime(engine);
        }

        public void Dispose()
        {
            Engine.Dispose();
        }
    }
}

