using System;
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
            var engine = new GameEngine();
            engine.Initialize(config, assetsRoot);
            return new WorldRuntime(engine);
        }

        public void Dispose()
        {
            Engine.Dispose();
        }
    }
}

