using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CoreInputMod.Systems
{
    /// <summary>
    /// Standard local order source: lazily creates the source mod's InputOrderMappingSystem via
    /// LocalOrderSourceHelper, binds it to the sole possessed actor each frame, and updates it.
    /// Installed by CoreInputMod from the mod's assets/Input/local_order_source.json declaration;
    /// mods with bespoke order routing keep their own systems instead of declaring the config.
    /// </summary>
    public sealed class LocalOrderSourceSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly LocalOrderSourceHelper _helper;
        private readonly IModContext _context;
        private readonly LocalOrderSourceConfig _config;
        private readonly string _sourceModId;
        private InputOrderMappingSystem? _mapping;
        private bool _initialized;

        public LocalOrderSourceSystem(
            World world,
            Dictionary<string, object> globals,
            OrderQueue orders,
            IModContext context,
            LocalOrderSourceConfig config,
            string sourceModId)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _sourceModId = string.IsNullOrWhiteSpace(sourceModId)
                ? throw new ArgumentException("Source mod id is required.", nameof(sourceModId))
                : sourceModId;
            _helper = new LocalOrderSourceHelper(world, globals, orders);
        }

        public void Initialize() { }

        public void BeforeUpdate(in float dt) { }

        public void AfterUpdate(in float dt) { }

        public void Dispose() { }

        public void Update(in float dt)
        {
            EnsureInitialized();
            if (_mapping == null)
            {
                WriteDiagnostics("mapping=<missing>");
                return;
            }

            Entity actor = _helper.GetControlledActor();
            if (!_world.IsAlive(actor))
            {
                WriteDiagnostics("actor=<dead>");
                return;
            }

            if (!_helper.TryBindSoleSeatActor(_mapping, actor))
            {
                WriteDiagnostics($"actor={actor.Id}:{actor.WorldId}:{actor.Version} bindSoleSeatActor=false");
                return;
            }

            WriteDiagnostics($"actor={actor.Id}:{actor.WorldId}:{actor.Version} bindSoleSeatActor=true");
            _mapping.Update(dt);
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _mapping = _helper.TryCreateMapping(_context, _sourceModId);
            if (_mapping == null)
            {
                if (_config.RequireMapping)
                {
                    throw new InvalidOperationException(
                        $"[{_sourceModId}] local_order_source.json requires input_order_mappings.json; " +
                        "AuthoritativeInput and the VFS input config are both required.");
                }

                return;
            }

            if (_config.SkillBarEnabled.HasValue)
            {
                _globals[SkillBarOverlaySystem.SkillBarEnabledKey] = _config.SkillBarEnabled.Value;
            }

            if (_config.SkillBarKeyLabels != null)
            {
                _globals[SkillBarOverlaySystem.SkillBarKeyLabelsKey] = _config.SkillBarKeyLabels;
            }

            if (!string.IsNullOrWhiteSpace(_config.QueueModifierActionId))
            {
                string actionId = _config.QueueModifierActionId;
                _mapping.SetQueueModifierProvider(() =>
                {
                    return _globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) &&
                           inputObj is IInputActionReader input &&
                           input.IsDown(actionId);
                });
            }
        }

        private void WriteDiagnostics(string state)
        {
            if (!string.IsNullOrWhiteSpace(_config.DiagnosticsKey))
            {
                _globals[_config.DiagnosticsKey] = state;
            }
        }
    }
}
