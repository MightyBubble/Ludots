using System.Collections.Generic;
using Arch.System;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
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

        private bool _prevSpace;
        private bool _prevN;
        private bool _prev1;

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
            if (!_globals.TryGetValue(CoreServiceKeys.InputBackend.Name, out var backendObj) || backendObj is not IInputBackend backend) return;

            bool space = backend.GetButton("<Keyboard>/Space");
            bool n = backend.GetButton("<Keyboard>/N");
            bool one = backend.GetButton("<Keyboard>/1");

            if (ConsumePressed(space, ref _prevSpace))
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

            if (ConsumePressed(n, ref _prevN))
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

            if (ConsumePressed(one, ref _prev1))
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

        private static bool ConsumePressed(bool current, ref bool prev)
        {
            bool pressed = current && !prev;
            prev = current;
            return pressed;
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
