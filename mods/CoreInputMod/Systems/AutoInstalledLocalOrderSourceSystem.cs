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
        private string _commandActionId = string.Empty;

        /// <summary>Generic per-tick install/binding diagnostic (actor, bind result, command edge).</summary>
        public const string LastUpdateDebugKey = "CoreInputMod.Debug.LocalOrderSource";

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
                if (_mapping != null)
                {
                    _commandActionId = Ludots.Core.Input.Interaction.InteractionActionBindingsResolver
                        .Require(_globals, nameof(AutoInstalledLocalOrderSourceSystem)).CommandActionId;
                }
            }

            InputOrderMappingSystem? mapping = _mapping;
            if (mapping == null)
            {
                _globals[LastUpdateDebugKey] = "mapping=<missing>";
                return;
            }

            if (!ClientLocalSeatAccess.TryGetSolePossessedRep(_globals, out Entity local) ||
                !_world.IsAlive(local))
            {
                _globals[LastUpdateDebugKey] = "rep=<unresolved>";
                return;
            }

            // Controlled-actor binding follows the mounted context carrier, so it is
            // re-resolved per tick exactly like the retired per-mod installers did; the
            // mapping only ticks while the binding resolves.
            Entity actor = _helper.GetControlledActor();
            if (!_world.IsAlive(actor))
            {
                _globals[LastUpdateDebugKey] = "actor=<dead> commandPressed=" + IsCommandPressed();
                return;
            }

            bool bound = _helper.TryBindSoleSeatActor(mapping, actor);
            _globals[LastUpdateDebugKey] =
                "actor=" + actor.Id + ":" + actor.WorldId + ":" + actor.Version +
                " bindSoleSeatActor=" + bound + " commandPressed=" + IsCommandPressed();
            if (bound)
            {
                mapping.Update(dt);
            }
        }

        private bool IsCommandPressed()
        {
            return _globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) &&
                inputObj is Ludots.Core.Input.Runtime.IInputActionReader input &&
                input.PressedThisFrame(_commandActionId);
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
