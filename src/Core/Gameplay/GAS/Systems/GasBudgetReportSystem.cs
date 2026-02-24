using System;
using Arch.System;
using Ludots.Core.Gameplay.GAS;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class GasBudgetReportSystem : ISystem<float>
    {
        private readonly GasBudget _budget;

        public GasBudgetReportSystem(GasBudget budget)
        {
            _budget = budget;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }

        public void Update(in float dt)
        {
            if (_budget == null) return;
            if (!_budget.HasWarnings) return;

            // Budget warnings are exposed via GasBudget fields for structured telemetry.
            // No Console.WriteLine to avoid string allocation in hot path.
        }

        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
