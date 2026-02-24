using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace Ludots.Core.Commands
{
    public class WaitCommand : GameCommand
    {
        public float Seconds { get; }

        public WaitCommand(float seconds)
        {
            Seconds = seconds;
        }

        public override async Task ExecuteAsync(ScriptContext context)
        {
            await GameTask.Delay(Seconds);
        }
    }
}
