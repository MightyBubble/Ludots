using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Generic GAS input-response system.
    /// Resolves InputRequest to InputResponse using the current interaction bindings
    /// and the current selected entity from globals.
    /// </summary>
    public sealed class GasInputResponseSystem : ISystem<float>
    {
        private static readonly InteractionActionBindings DefaultBindings = new InteractionActionBindings();

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private InputRequest _active;
        private bool _hasActive;

        public GasInputResponseSystem(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) || inputObj is not IInputActionReader input) return;
            if (!_globals.TryGetValue(CoreServiceKeys.AbilityInputRequestQueue.Name, out var reqObj) || reqObj is not InputRequestQueue requests) return;
            if (!_globals.TryGetValue(CoreServiceKeys.InputResponseBuffer.Name, out var respObj) || respObj is not InputResponseBuffer responses) return;

            if (!_hasActive && requests.TryDequeue(out var req))
            {
                _active = req;
                _hasActive = true;
            }

            if (!_hasActive) return;

            var bindings = ResolveBindings();
            if (!input.PressedThisFrame(bindings.ConfirmActionId)) return;

            Entity target = default;
            if (_globals.TryGetValue(CoreServiceKeys.SelectedEntity.Name, out var selObj) && selObj is Entity e && _world.IsAlive(e))
            {
                target = e;
            }

            responses.TryAdd(new InputResponse
            {
                RequestId = _active.RequestId,
                ResponseTagId = _active.RequestTagId,
                Source = _active.Source,
                Target = target,
                TargetContext = _active.Context,
                PayloadA = _active.PayloadA,
                PayloadB = _active.PayloadB,
            });

            _hasActive = false;
            _active = default;
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
