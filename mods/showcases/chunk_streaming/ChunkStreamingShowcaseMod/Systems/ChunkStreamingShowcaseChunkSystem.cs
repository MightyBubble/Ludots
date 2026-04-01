using Arch.System;
using ChunkStreamingShowcaseMod.Runtime;
using Ludots.Core.Engine;

namespace ChunkStreamingShowcaseMod.Systems
{
    internal sealed class ChunkStreamingShowcaseChunkSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly ChunkStreamingShowcaseRuntime _runtime;

        public ChunkStreamingShowcaseChunkSystem(GameEngine engine, ChunkStreamingShowcaseRuntime runtime)
        {
            _engine = engine;
            _runtime = runtime;
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            _runtime.UpdateLoadedChunks(_engine);
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }
    }
}
