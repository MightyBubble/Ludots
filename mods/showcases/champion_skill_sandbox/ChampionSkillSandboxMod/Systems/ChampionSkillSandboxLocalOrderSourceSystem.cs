using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace ChampionSkillSandboxMod.Systems
{
    public sealed class ChampionSkillSandboxLocalOrderSourceSystem : ISystem<float>
    {
        public const string LastUpdateDebugKey = "ChampionSkillSandbox.Debug.LocalOrderSource";

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly LocalOrderSourceHelper _helper;
        private readonly IModContext _context;
        private InputOrderMappingSystem? _mapping;
        private bool _initialized;

        public ChampionSkillSandboxLocalOrderSourceSystem(World world, Dictionary<string, object> globals, OrderQueue orders, IModContext context)
        {
            _world = world;
            _globals = globals;
            _context = context;
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
                _globals[LastUpdateDebugKey] = "mapping=<missing>";
                return;
            }

            Entity actor = _helper.GetControlledActor();
            if (!_world.IsAlive(actor))
            {
                _globals[LastUpdateDebugKey] = $"actor=<dead> commandPressed={IsCommandPressed()}";
                return;
            }

            if (!_helper.TrySetLocalPlayer(_mapping, actor))
            {
                _globals[LastUpdateDebugKey] = $"actor={actor.Id}:{actor.WorldId}:{actor.Version} setLocalPlayer=false commandPressed={IsCommandPressed()}";
                return;
            }

            _globals[LastUpdateDebugKey] = $"actor={actor.Id}:{actor.WorldId}:{actor.Version} setLocalPlayer=true commandPressed={IsCommandPressed()}";
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
            _mapping = _helper.TryCreateMapping(_context);
            if (_mapping == null)
            {
                return;
            }

            _mapping.SetQueueModifierProvider(() =>
            {
                return _globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) &&
                       inputObj is IInputActionReader input &&
                       input.IsDown("QueueModifier");
            });
        }

        private bool IsCommandPressed()
        {
            return _globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) &&
                   inputObj is IInputActionReader input &&
                   input.PressedThisFrame("Command");
        }
    }
}
