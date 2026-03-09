using System.Collections.Generic;
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
        private static readonly InteractionActionBindings DefaultBindings = new InteractionActionBindings();

        private readonly Dictionary<string, object> _globals;
        private readonly ResponseChainUiState _ui;
        private readonly OrderQueue _chainOrders;
        private readonly ResponseChainOrderTypes _responseChainOrderTypes;

        public ResponseChainHumanOrderSourceSystem(Dictionary<string, object> globals, ResponseChainUiState ui, OrderQueue chainOrders)
        {
            _globals = globals;
            _ui = ui;
            _chainOrders = chainOrders;

            if (_globals.TryGetValue(CoreServiceKeys.GameConfig.Name, out var configObj) && configObj is GameConfig config)
            {
                _responseChainOrderTypes = new ResponseChainOrderTypes
                {
                    ChainPass = config.Constants.ResponseChainOrderTypeIds.GetValueOrDefault("chainPass", 1),
                    ChainNegate = config.Constants.ResponseChainOrderTypeIds.GetValueOrDefault("chainNegate", 2),
                    ChainActivateEffect = config.Constants.ResponseChainOrderTypeIds.GetValueOrDefault("chainActivateEffect", 3)
                };
            }
            else
            {
                _responseChainOrderTypes = ResponseChainOrderTypes.Default;
            }
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!_ui.Visible) return;
            if (!_globals.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) || inputObj is not PlayerInputHandler input) return;

            var bindings = ResolveBindings();

            if (input.PressedThisFrame(bindings.ResponseChainPassActionId))
            {
                _chainOrders.TryEnqueue(new Order
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
                _chainOrders.TryEnqueue(new Order
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
                args.I0 = _ui.PromptTagId;
                _chainOrders.TryEnqueue(new Order
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

        private InteractionActionBindings ResolveBindings()
        {
            if (_globals.TryGetValue(CoreServiceKeys.InteractionActionBindings.Name, out var obj) && obj is InteractionActionBindings bindings)
            {
                return bindings;
            }

            return DefaultBindings;
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
