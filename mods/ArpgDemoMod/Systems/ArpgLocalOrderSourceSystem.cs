using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace ArpgDemoMod.Systems
{
    public sealed class ArpgLocalOrderSourceSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly LocalOrderSourceHelper _helper;
        private readonly IModContext _ctx;
        private InputOrderMappingSystem? _mapping;
        private bool _initialized;

        public ArpgLocalOrderSourceSystem(World world, Dictionary<string, object> globals, OrderQueue orders, IModContext ctx)
        {
            _world = world;
            _globals = globals;
            _ctx = ctx;
            _helper = new LocalOrderSourceHelper(world, globals, orders);
        }

        public void Initialize() { }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _mapping = _helper.TryCreateMapping(_ctx);
            if (_mapping != null)
            {
                _globals[SkillBarOverlaySystem.SkillBarKeyLabelsKey] = new[] { "1", "2", "3", "4", "5", "6" };
            }
        }

        public void Update(in float dt)
        {
            EnsureInitialized();
            if (_mapping == null)
            {
                return;
            }

            if (_globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) && localObj is Entity local && _world.IsAlive(local))
            {
                _mapping.SetLocalPlayer(local, 1);
                _mapping.Update(dt);
            }
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
