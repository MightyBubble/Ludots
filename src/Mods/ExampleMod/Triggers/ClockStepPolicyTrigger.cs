using System;
using System.Threading.Tasks;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Scripting;

namespace ExampleMod.Triggers
{
    public sealed class ClockStepPolicyTrigger : Trigger
    {
        private readonly int _stepEveryFixedTicks;

        public ClockStepPolicyTrigger(EventKey eventKey, int stepEveryFixedTicks)
        {
            EventKey = eventKey;
            _stepEveryFixedTicks = stepEveryFixedTicks;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var policy = context.Get<GasClockStepPolicy>(ContextKeys.GasClockStepPolicy);
            if (policy == null)
            {
                Console.WriteLine("[ExampleMod] GasClockStepPolicy not found in context.");
                return Task.CompletedTask;
            }

            policy.SetStepEveryFixedTicks(_stepEveryFixedTicks);
            Console.WriteLine($"[ExampleMod] StepEveryFixedTicks set to {_stepEveryFixedTicks}.");
            return Task.CompletedTask;
        }
    }
}

