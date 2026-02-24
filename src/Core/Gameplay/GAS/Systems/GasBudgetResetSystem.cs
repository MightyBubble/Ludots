using Arch.System;
using Ludots.Core.Gameplay.GAS;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class GasBudgetResetSystem : ISystem<float>
    {
        private readonly GasBudget _budget;

        public GasBudgetResetSystem(GasBudget budget)
        {
            _budget = budget;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }

        public void Update(in float dt)
        {
            _budget?.Reset();
        }

        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
