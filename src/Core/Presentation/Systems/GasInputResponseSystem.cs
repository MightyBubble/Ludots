using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// 通用的 GAS InputRequest 响应系统。
    /// 
    /// 当 GAS 技能系统发出 InputRequest（例如"等待玩家选择目标"）时，
    /// 此系统等待玩家点击 "Select" 按钮，然后将当前选中的实体
    /// 作为 InputResponse 返回给技能系统。
    /// 
    /// 此系统不包含任何游戏特定逻辑。
    /// </summary>
    public sealed class GasInputResponseSystem : ISystem<float>
    {
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
            if (!_globals.TryGetValue(ContextKeys.InputHandler, out var inputObj) || inputObj is not PlayerInputHandler input) return;
            if (!_globals.TryGetValue(ContextKeys.AbilityInputRequestQueue, out var reqObj) || reqObj is not InputRequestQueue requests) return;
            if (!_globals.TryGetValue(ContextKeys.InputResponseBuffer, out var respObj) || respObj is not InputResponseBuffer responses) return;

            if (!_hasActive && requests.TryDequeue(out var req))
            {
                _active = req;
                _hasActive = true;
            }

            if (!_hasActive) return;
            if (!input.PressedThisFrame("Select")) return;

            Entity target = default;
            if (_globals.TryGetValue(ContextKeys.SelectedEntity, out var selObj) && selObj is Entity e && _world.IsAlive(e))
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
                PayloadB = _active.PayloadB
            });

            _hasActive = false;
            _active = default;
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
