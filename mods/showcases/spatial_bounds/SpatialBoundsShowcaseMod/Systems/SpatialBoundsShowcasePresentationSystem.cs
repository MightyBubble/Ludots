using Arch.System;
using Ludots.Core.Engine;
using SpatialBoundsShowcaseMod.Runtime;

namespace SpatialBoundsShowcaseMod.Systems
{
    internal sealed class SpatialBoundsShowcasePresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly SpatialBoundsShowcaseRuntime _runtime;

        public SpatialBoundsShowcasePresentationSystem(GameEngine engine, SpatialBoundsShowcaseRuntime runtime)
        {
            _engine = engine;
            _runtime = runtime;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            _runtime.Update(_engine);
        }
    }
}
