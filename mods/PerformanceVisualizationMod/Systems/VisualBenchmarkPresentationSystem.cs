using Arch.System;
using Ludots.Core.Engine;
using PerformanceVisualizationMod.Runtime;

namespace PerformanceVisualizationMod.Systems
{
    internal sealed class VisualBenchmarkPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly VisualBenchmarkRuntime _runtime;

        public VisualBenchmarkPresentationSystem(GameEngine engine, VisualBenchmarkRuntime runtime)
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
            _runtime.Advance(_engine);
            _runtime.RefreshPanel(_engine);
        }
    }
}
