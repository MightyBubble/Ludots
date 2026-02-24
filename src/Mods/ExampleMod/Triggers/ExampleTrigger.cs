using Ludots.Core.Scripting;
using System;
using System.Threading.Tasks;

namespace ExampleMod.Triggers
{
    public class ExampleTrigger : Trigger
    {
        public ExampleTrigger()
        {
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            Console.WriteLine("[ExampleMod] Hello from Trigger!");
            return Task.CompletedTask;
        }
    }
}
