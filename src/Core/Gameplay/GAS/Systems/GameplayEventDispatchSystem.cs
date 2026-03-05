using Arch.System;
using Ludots.Core.Gameplay.GAS;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class GameplayEventDispatchSystem : ISystem<float>
    {
        private readonly GameplayEventBus _eventBus;
        private readonly GasBudget? _budget;

        public GameplayEventDispatchSystem(GameplayEventBus eventBus, GasBudget? budget = null)
        {
            _eventBus = eventBus;
            _budget = budget;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }

        public void Update(in float dt)
        {
            _eventBus?.Update();
            if (_budget != null && _eventBus != null)
            {
                _budget.GameplayEventBusDropped += _eventBus.DroppedEventsLastUpdate;
            }
        }

        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
