using System;
using System.Threading.Tasks;
using Ludots.Core.Scripting;

namespace Ludots.Core.Commands
{
    public class DelegateCommand : GameCommand
    {
        private readonly Func<ScriptContext, Task> _asyncAction;
        private readonly Action<ScriptContext> _syncAction;

        public DelegateCommand(Func<ScriptContext, Task> action)
        {
            _asyncAction = action;
        }

        public DelegateCommand(Action<ScriptContext> action)
        {
            _syncAction = action;
        }

        public override async Task ExecuteAsync(ScriptContext context)
        {
            if (_asyncAction != null)
            {
                await _asyncAction(context);
            }
            else if (_syncAction != null)
            {
                _syncAction(context);
            }
        }
    }
}
