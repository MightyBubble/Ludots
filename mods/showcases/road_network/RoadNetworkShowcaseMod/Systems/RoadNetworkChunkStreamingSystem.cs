using Arch.System;
using Ludots.Core.Engine;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Systems
{
    internal sealed class RoadNetworkChunkStreamingSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly RoadNetworkShowcaseRuntime _runtime;

        public RoadNetworkChunkStreamingSystem(GameEngine engine, RoadNetworkShowcaseRuntime runtime)
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
