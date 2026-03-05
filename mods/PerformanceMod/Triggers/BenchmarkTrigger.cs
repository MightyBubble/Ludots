using Ludots.Core.Scripting;
using Ludots.Core.Commands;
using Ludots.Core.Engine;
using PerformanceMod.Maps;
using System;
using System.Threading.Tasks;

namespace PerformanceMod.Triggers
{
    public class BenchmarkTrigger : Trigger
    {
        public BenchmarkTrigger()
        {
            EventKey = GameEvents.MapLoaded;
            
            // Only run on BenchmarkMap
            AddCondition(ctx => ctx.IsMap<BenchmarkMap>());
        }

        public override async Task ExecuteAsync(ScriptContext context)
        {
            Console.WriteLine("[BenchmarkTrigger] Trigger Executed! Checking Engine...");
            var engine = context.GetEngine();
            if (engine == null) 
            {
                Console.WriteLine("[BenchmarkTrigger] Error: Engine not found in context!");
                return;
            }
            
            Console.WriteLine("[BenchmarkTrigger] Creating SpawnEntityCommand...");
            // Old GameContext usage removed, ScriptContext is now used directly in Commands
            
            var cmd = new SpawnEntityCommand { Count = 20000 };
            await cmd.ExecuteAsync(context);
            Console.WriteLine("[BenchmarkTrigger] Command Executed.");
        }
    }
}
