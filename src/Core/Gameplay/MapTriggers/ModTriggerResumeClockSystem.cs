using System;
using Arch.System;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Resumes suspended Mod-domain TriggerGraphs on the fixed-step deferred
    /// trigger phase. The context is created once per engine lifetime, and the
    /// synchronous TriggerManager path keeps the steady-state pulse allocation-free.
    /// </summary>
    public sealed class ModTriggerResumeClockSystem : ISystem<float>
    {
        private readonly TriggerManager _triggerManager;
        private readonly Func<ScriptContext> _contextFactory;
        private ScriptContext? _context;

        public ModTriggerResumeClockSystem(
            TriggerManager triggerManager,
            Func<ScriptContext> contextFactory)
        {
            _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public void Initialize() { }

        public void BeforeUpdate(in float dt) { }

        public void Update(in float dt)
        {
            if (!_triggerManager.HasSuspendedModTriggers)
            {
                return;
            }

            _context ??= _contextFactory();
            _triggerManager.FireModTriggerResume(_context);
        }

        public void AfterUpdate(in float dt) { }

        public void Dispose() { }
    }
}
