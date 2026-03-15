using Arch.System;
using EntityCommandPanelMod.Runtime;
using EntityCommandPanelMod.UI;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;

namespace EntityCommandPanelMod.Systems
{
    internal sealed class EntityCommandPanelPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly EntityCommandPanelRuntime _runtime;
        private readonly EntityCommandPanelController _controller;

        public EntityCommandPanelPresentationSystem(
            GameEngine engine,
            EntityCommandPanelRuntime runtime,
            EntityCommandPanelController controller)
        {
            _engine = engine;
            _runtime = runtime;
            _controller = controller;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            _runtime.RefreshObservedState();
            if (_engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
            {
                _controller.Sync(root);
            }
        }
    }
}
