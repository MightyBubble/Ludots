using System.Threading.Tasks;
using Ludots.Core.Scripting;

namespace Ludots.Core.Commands
{
    public class AnchorCommand : GameCommand
    {
        public object Key { get; }

        public AnchorCommand(object key)
        {
            Key = key;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            // Do nothing, just a placeholder
            return Task.CompletedTask;
        }
    }
}
