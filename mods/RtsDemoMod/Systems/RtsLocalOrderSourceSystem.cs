using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace RtsDemoMod.Systems
{
    public sealed class RtsLocalOrderSourceSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly LocalOrderSourceHelper _helper;
        private readonly IModContext _ctx;
        private InputOrderMappingSystem? _mapping;
        private bool _initialized;

        public RtsLocalOrderSourceSystem(World world, Dictionary<string, object> globals, OrderQueue orders, IModContext ctx)
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
                _globals[SkillBarOverlaySystem.SkillBarKeyLabelsKey] = new[] { "Q", "W", "E", "R" };
            }
        }

        public void Update(in float dt)
        {
            if (IsThreeKingdomsScenarioActive())
            {
                return;
            }

            EnsureInitialized();
            if (_mapping == null)
            {
                return;
            }

            var actor = _helper.GetControlledActor();
            if (_world.IsAlive(actor))
            {
                _mapping.SetLocalPlayer(actor, 1);
                _mapping.Update(dt);
            }
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        private bool IsThreeKingdomsScenarioActive()
        {
            if (!_globals.TryGetValue(CoreServiceKeys.MapId.Name, out object? mapObj) ||
                mapObj is not MapId mapId ||
                !string.Equals(mapId.Value, "road_network_showcase_chunked", System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.MapTags.Name, out object? tagsObj) ||
                tagsObj is not List<string> tags)
            {
                return false;
            }

            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], "three_kingdoms_siege", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
