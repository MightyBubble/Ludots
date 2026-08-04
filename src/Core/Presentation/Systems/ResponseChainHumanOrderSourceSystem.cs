using System.Collections.Generic;
using System;
using Arch.System;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class ResponseChainHumanOrderSourceSystem : ISystem<float>
    {
        private readonly Dictionary<string, object> _globals;
        private readonly ResponseChainUiState _ui;
        private readonly OrderQueue _chainOrders;
        private readonly ResponseChainOrderTypes _responseChainOrderTypes;

        public OrderSubmitResult LastSubmissionResult { get; private set; } = OrderSubmitResult.RejectedByRule;
        public int LastSubmittedOrderId { get; private set; }

        public ResponseChainHumanOrderSourceSystem(Dictionary<string, object> globals, ResponseChainUiState ui, OrderQueue chainOrders)
        {
            _globals = globals;
            _ui = ui;
            _chainOrders = chainOrders;

            if (!_globals.TryGetValue(CoreServiceKeys.GameConfig.Name, out var configObj) || configObj is not GameConfig config)
            {
                throw new InvalidOperationException(
                    $"{nameof(ResponseChainHumanOrderSourceSystem)} requires GameConfig constants.responseChainOrderTypeIds (chainPass, chainNegate, chainActivateEffect).");
            }

            _responseChainOrderTypes = new ResponseChainOrderTypes
            {
                ChainPass = RequireResponseChainOrderTypeId(config, "chainPass"),
                ChainNegate = RequireResponseChainOrderTypeId(config, "chainNegate"),
                ChainActivateEffect = RequireResponseChainOrderTypeId(config, "chainActivateEffect")
            };
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!_ui.Visible) return;
            if (!_globals.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) || inputObj is not PlayerInputHandler input) return;

            var bindings = ResolveBindings();

            if (input.PressedThisFrame(bindings.ResponseChainPassActionId))
            {
                Submit(new Order
                {
                    OrderTypeId = _responseChainOrderTypes.ChainPass,
                    PlayerId = _ui.PlayerId,
                    Actor = _ui.Actor,
                    Target = _ui.Target,
                    TargetContext = _ui.TargetContext,
                    Args = default
                });
            }

            if (input.PressedThisFrame(bindings.ResponseChainNegateActionId))
            {
                Submit(new Order
                {
                    OrderTypeId = _responseChainOrderTypes.ChainNegate,
                    PlayerId = _ui.PlayerId,
                    Actor = _ui.Actor,
                    Target = _ui.Target,
                    TargetContext = _ui.TargetContext,
                    Args = default
                });
            }

            if (input.PressedThisFrame(bindings.ResponseChainActivateActionId))
            {
                var args = default(OrderArgs);
                OrderBuilder.SetIntArg(ref args, OrderIntArgSlot.I0, _ui.PromptTagId);
                Submit(new Order
                {
                    OrderTypeId = _responseChainOrderTypes.ChainActivateEffect,
                    PlayerId = _ui.PlayerId,
                    Actor = _ui.Actor,
                    Target = _ui.Target,
                    TargetContext = _ui.TargetContext,
                    Args = args
                });
            }
        }

        private void Submit(Order order)
        {
            LastSubmissionResult = _chainOrders.SubmitAssigned(ref order);
            LastSubmittedOrderId = order.OrderId;
        }

        private InteractionActionBindings ResolveBindings()
        {
            return InteractionActionBindingsResolver.Require(_globals, nameof(ResponseChainHumanOrderSourceSystem));
        }

        private static int RequireResponseChainOrderTypeId(GameConfig config, string key)
        {
            if (config.Constants == null ||
                config.Constants.ResponseChainOrderTypeIds == null ||
                !config.Constants.ResponseChainOrderTypeIds.TryGetValue(key, out int orderTypeId) ||
                orderTypeId <= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(ResponseChainHumanOrderSourceSystem)} requires GameConfig constants.responseChainOrderTypeIds.{key} to be a positive registered order type id.");
            }

            return orderTypeId;
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
