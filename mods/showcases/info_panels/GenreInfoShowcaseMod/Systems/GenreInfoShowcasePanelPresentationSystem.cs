using Arch.System;
using GenreInfoShowcaseMod.Runtime;
using Ludots.Core.Engine;

namespace GenreInfoShowcaseMod.Systems
{
    internal sealed class GenreInfoShowcasePanelPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly GenreInfoShowcaseRuntime _runtime;

        public GenreInfoShowcasePanelPresentationSystem(GameEngine engine, GenreInfoShowcaseRuntime runtime)
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
            _runtime.RefreshPanel(_engine);
        }
    }
}
