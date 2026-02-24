using System;
using System.Threading.Tasks;
using Ludots.Core.Commands;
using Ludots.Core.Scripting;

namespace Ludots.Core.Scripting
{
    public static class TriggerExtensions
    {
        // Fluent Builder for internal use (filling the list)
        public static TriggerBuilder Sequence(this Trigger trigger)
        {
            return new TriggerBuilder(trigger);
        }
    }

    public class TriggerBuilder
    {
        private readonly Trigger _trigger;

        public TriggerBuilder(Trigger trigger)
        {
            _trigger = trigger;
        }

        public TriggerBuilder Do(GameCommand command)
        {
            _trigger.Actions.Add(command);
            return this;
        }

        public TriggerBuilder Do(Func<ScriptContext, Task> asyncAction)
        {
            _trigger.Actions.Add(new DelegateCommand(asyncAction));
            return this;
        }
        
        public TriggerBuilder Do(Action<ScriptContext> syncAction)
        {
            _trigger.Actions.Add(new DelegateCommand(syncAction));
            return this;
        }

        public TriggerBuilder Anchor(object key)
        {
            _trigger.Actions.Add(new AnchorCommand(key));
            return this;
        }
    }
}
