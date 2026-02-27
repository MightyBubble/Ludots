using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Components;
using Ludots.Core.Diagnostics;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;
using System;
using Arch.Core;

namespace Ludots.Core.Commands
{
    public class SpawnEntityCommand : GameCommand
    {
        public int Count { get; set; } = 1;

        public override Task ExecuteAsync(ScriptContext context)
        {
            Log.Info(in LogChannels.Engine, $"Executing... Spawning {Count} entities.");
            
            var world = context.Get<World>(ContextKeys.World);
            if (world == null)
            {
                Log.Error(in LogChannels.Engine, "World not found in context!");
                return Task.CompletedTask;
            }

            var random = new Random();

            for (int i = 0; i < Count; i++)
            {
                 var pos = new IntVector2(random.Next(-5000, 5000), random.Next(-5000, 5000));
                 var vel = new IntVector2(random.Next(-100, 101), random.Next(-100, 101));
                 
                 world.Create(
                    new Position { GridPos = pos },
                    new Velocity { Value = vel }
                 );
            }
            Log.Info(in LogChannels.Engine, "Spawning complete.");
            return Task.CompletedTask;
        }
    }
}
