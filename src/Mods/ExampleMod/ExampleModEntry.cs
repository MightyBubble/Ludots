using System;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using ExampleMod.Triggers;

namespace ExampleMod
{
    public class ExampleModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("ExampleMod Loaded!");
            context.OnEvent(GameEvents.GameStart, new ExampleTrigger().ExecuteAsync);
            context.OnEvent(ExampleModEvents.SetSkillStep10Hz, new ClockStepPolicyTrigger(ExampleModEvents.SetSkillStep10Hz, stepEveryFixedTicks: 6).ExecuteAsync);
            context.OnEvent(ExampleModEvents.SetSkillStep60Hz, new ClockStepPolicyTrigger(ExampleModEvents.SetSkillStep60Hz, stepEveryFixedTicks: 1).ExecuteAsync);
        }

        public void OnUnload()
        {
            Console.WriteLine("[ExampleMod] Unloaded.");
        }
    }
}
