using System;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Calendar
{
    public sealed class CalendarSystem : ISystem<float>
    {
        private readonly CalendarRuntime _runtime;
        private readonly GasClockStepPolicy _stepPolicy;
        private readonly Func<ScriptContext>? _contextFactory;
        private readonly Action<EventKey, ScriptContext>? _fireEvent;

        public CalendarSystem(
            CalendarRuntime runtime,
            GasClockStepPolicy stepPolicy,
            Func<ScriptContext>? contextFactory = null,
            Action<EventKey, ScriptContext>? fireEvent = null)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _stepPolicy = stepPolicy ?? throw new ArgumentNullException(nameof(stepPolicy));
            _contextFactory = contextFactory;
            _fireEvent = fireEvent;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            _runtime.Advance(_stepPolicy.LastConsumedSteps, _contextFactory, _fireEvent);
        }

        public void BeforeUpdate(in float dt) { }

        public void AfterUpdate(in float dt) { }

        public void Dispose() { }
    }
}
