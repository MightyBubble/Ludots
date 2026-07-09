using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Generic GAS input-response system.
    /// Resolves InputRequest to InputResponse using the current interaction bindings.
    /// Request producers must provide target/context data explicitly.
    /// </summary>
    public sealed class GasInputResponseSystem : ISystem<float>
    {
        private readonly Dictionary<string, object> _globals;
        private InputRequest _active;
        private bool _hasActive;

        public GasInputResponseSystem(World world, Dictionary<string, object> globals)
        {
            _globals = globals;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.AbilityInputRequestQueue.Name, out var reqObj) || reqObj is not InputRequestQueue requests) return;
            if (!_globals.TryGetValue(CoreServiceKeys.InputResponseBuffer.Name, out var respObj) || respObj is not InputResponseBuffer responses) return;
            if (!PointerInteractionSnapshotReader.TryRead(_globals, out PointerInteractionSnapshot pointer)) return;

            if (!_hasActive && requests.TryDequeue(out var req))
            {
                _active = req;
                _hasActive = true;
            }

            if (!_hasActive) return;

            if (!pointer.Confirm.PressedThisFrame) return;

            responses.TryAdd(new InputResponse
            {
                RequestId = _active.RequestId,
                ResponseTagId = _active.RequestTagId,
                Source = _active.Source,
                Target = _active.Target,
                TargetContext = _active.Context,
                PayloadA = _active.PayloadA,
                PayloadB = _active.PayloadB,
            });

            _hasActive = false;
            _active = default;
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
