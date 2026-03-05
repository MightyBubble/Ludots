using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Config;
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
            var resolved = new List<string>();
            for (int i = 0; i < modPaths.Count; i++)
            {
                var raw = modPaths[i];
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var path = Path.IsPathRooted(raw) ? raw : Path.Combine(baseDir, raw);
                resolved.Add(Path.GetFullPath(path));
            }
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(resolved, assetsRoot);
            return new WorldRuntime(engine);
        }

        public void Dispose()
        {
            Engine.Dispose();
        }
    }
}

