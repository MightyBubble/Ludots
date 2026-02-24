using Arch.System;
using Ludots.Core.Gameplay.GAS;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class GameplayEventDispatchSystem : ISystem<float>
    {
        private readonly GameplayEventBus _eventBus;

        public GameplayEventDispatchSystem(GameplayEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }

        public void Update(in float dt)
        {
            _eventBus?.Update();
        }

        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
