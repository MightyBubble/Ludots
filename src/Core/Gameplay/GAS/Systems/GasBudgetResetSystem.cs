using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class GasBudgetResetSystem : ISystem<float>
    {
        private readonly GasBudget _budget;
        private readonly OrderTerminalResultBuffer? _orderTerminalResults;
        private readonly OrderAdmissionResultBuffer? _orderAdmissionResults;

        public GasBudgetResetSystem(
            GasBudget budget,
            OrderTerminalResultBuffer? orderTerminalResults = null,
            OrderAdmissionResultBuffer? orderAdmissionResults = null)
        {
            _budget = budget;
            _orderTerminalResults = orderTerminalResults;
            _orderAdmissionResults = orderAdmissionResults;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }

        public void Update(in float dt)
        {
            _budget?.Reset();
            _orderTerminalResults?.Clear();
            _orderAdmissionResults?.BeginLogicStep();
        }

        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
