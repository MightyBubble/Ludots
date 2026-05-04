using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.ChunkDebug
{
    public sealed class ChunkDebugPanelPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly ChunkDebugPanelRuntime _runtime;

        public ChunkDebugPanelPresentationSystem(GameEngine engine, ChunkDebugPanelRuntime runtime)
        {
            _engine = engine;
            _runtime = runtime;
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float t)
        {
        }

        public void Update(in float t)
        {
            if (_engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is ScreenOverlayBuffer overlay)
            {
                _runtime.Render(_engine, overlay);
            }
        }

        public void AfterUpdate(in float t)
        {
        }

        public void Dispose()
        {
        }
    }
}
