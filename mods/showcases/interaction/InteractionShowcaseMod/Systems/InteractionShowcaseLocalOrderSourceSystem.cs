using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace InteractionShowcaseMod.Systems
{
    public sealed class InteractionShowcaseLocalOrderSourceSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly LocalOrderSourceHelper _helper;
        private readonly IModContext _ctx;
        private InputOrderMappingSystem? _mapping;
        private bool _initialized;

        public InteractionShowcaseLocalOrderSourceSystem(World world, Dictionary<string, object> globals, OrderQueue orders, IModContext ctx)
        {
            _world = world;
            _globals = globals;
            _ctx = ctx;
            _helper = new LocalOrderSourceHelper(world, globals, orders);
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            EnsureInitialized();
            if (_mapping == null)
            {
                return;
            }

            var actor = _helper.GetControlledActor();
            if (!_world.IsAlive(actor))
            {
                return;
            }

            if (!_helper.TryBindSoleSeatActor(_mapping, actor))
            {
                return;
            }

            _mapping.Update(dt);
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _mapping = _helper.TryCreateMapping(_ctx);
            if (_mapping == null)
            {
                return;
            }

            _globals[SkillBarOverlaySystem.SkillBarKeyLabelsKey] = new[] { "Q", "W", "E", "R", "Z", "F", "Space", "X+C" };
            _mapping.SetQueueModifierProvider(() =>
            {
                return _globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) &&
                       inputObj is IInputActionReader input &&
                       input.IsDown("QueueModifier");
            });
        }
    }
}
