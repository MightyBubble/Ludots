using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CoreInputMod.Systems
{
    /// <summary>
    /// The single config-installed local order source (migration slice 2): exactly one loaded
    /// mod may ship <c>assets/Input/input_order_mappings.json</c> — the CoreInputMod composition
    /// root resolves it at GameStart and registers this system in its place, so mods carry no
    /// per-mod mapping installer code. The mapping is built once through
    /// <see cref="LocalOrderSourceHelper.TryCreateMapping(IModContext, string?)"/> and re-bound to
    /// the sole seat's controlled actor each tick (the controlled-actor resolution follows the
    /// mounted interaction context carrier, which can change without a possession change); the
    /// mapping only ticks while the binding resolves, matching the retired per-mod installers.
    /// </summary>
    public sealed class AutoInstalledLocalOrderSourceSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly IModContext _ctx;
        private readonly string _sourceModId;
        private readonly LocalOrderSourceHelper _helper;
        private InputOrderMappingSystem? _mapping;
        private bool _initialized;

        public AutoInstalledLocalOrderSourceSystem(
            World world,
            Dictionary<string, object> globals,
            OrderQueue orders,
            IModContext ctx,
            string sourceModId)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _sourceModId = string.IsNullOrWhiteSpace(sourceModId)
                ? throw new ArgumentException("The shipping mod id is required.", nameof(sourceModId))
                : sourceModId;
            _helper = new LocalOrderSourceHelper(world, globals, orders);
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!_initialized)
            {
                _initialized = true;
                _mapping = _helper.TryCreateMapping(_ctx, _sourceModId);
            }

            InputOrderMappingSystem? mapping = _mapping;
            if (mapping == null)
            {
                return;
            }

            if (!ClientLocalSeatAccess.TryGetSolePossessedRep(_globals, out Entity local) ||
                !_world.IsAlive(local))
            {
                return;
            }

            // Controlled-actor binding follows the mounted context carrier, so it is
            // re-resolved per tick exactly like the retired per-mod installers did; the
            // mapping only ticks while the binding resolves.
            if (_helper.TryBindSoleSeatActor(mapping, _helper.GetControlledActor()))
            {
                mapping.Update(dt);
            }
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
