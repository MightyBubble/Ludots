using System;
using Ludots.Core.Modding;
using ExampleMod.Triggers;

namespace ExampleMod
{
    public class ExampleModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("ExampleMod Loaded!");
            context.TriggerManager.RegisterTrigger(new ExampleTrigger());
            context.TriggerManager.RegisterTrigger(new ClockStepPolicyTrigger(ExampleModEvents.SetSkillStep10Hz, stepEveryFixedTicks: 6));
            context.TriggerManager.RegisterTrigger(new ClockStepPolicyTrigger(ExampleModEvents.SetSkillStep60Hz, stepEveryFixedTicks: 1));
        }

        public void OnUnload()
        {
            Console.WriteLine("[ExampleMod] Unloaded.");
        }
    }
}
