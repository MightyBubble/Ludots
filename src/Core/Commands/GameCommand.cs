using System.Threading.Tasks;
using Ludots.Core.Scripting;

namespace Ludots.Core.Commands
{
    public abstract class GameCommand
    {
        public abstract Task ExecuteAsync(ScriptContext context);
    }
}
