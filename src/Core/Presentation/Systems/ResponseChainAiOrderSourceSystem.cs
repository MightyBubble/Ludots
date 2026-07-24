using Arch.System;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class ResponseChainAiOrderSourceSystem : ISystem<float>
    {
        private readonly ResponseChainUiState _ui;
        private readonly OrderQueue _chainOrders;
        private readonly int _chainPassOrderTypeId;
        private int _lastSubmittedRootId;

        public OrderSubmitResult LastSubmissionResult { get; private set; } = OrderSubmitResult.RejectedByRule;
        public int LastSubmittedOrderId { get; private set; }

        public ResponseChainAiOrderSourceSystem(ResponseChainUiState ui, OrderQueue chainOrders, int chainPassOrderTypeId = 1)
        {
            _ui = ui;
            _chainOrders = chainOrders;
            _chainPassOrderTypeId = chainPassOrderTypeId;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!_ui.Visible) return;
            if (_ui.PlayerId != 2) return;
            if (_ui.RootId == _lastSubmittedRootId) return;

            var order = new Order
            {
                OrderTypeId = _chainPassOrderTypeId,
                PlayerId = _ui.PlayerId,
                Actor = _ui.Actor,
                Target = _ui.Target,
                TargetContext = _ui.TargetContext,
                Args = default
            };
            LastSubmissionResult = _chainOrders.SubmitAssigned(ref order);
            LastSubmittedOrderId = order.OrderId;

            if (OrderSubmitResultSemantics.IsAccepted(LastSubmissionResult))
            {
                _lastSubmittedRootId = _ui.RootId;
            }
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
