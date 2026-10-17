using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ludots.Core.Commands;

namespace Ludots.Core.Scripting
{
    public class Trigger
    {
        public virtual string Name => GetType().Name;
        public EventKey EventKey { get; set; }
        public int Priority { get; set; }

        public List<GameCommand> Actions { get; } = new List<GameCommand>();
        public List<Func<ScriptContext, bool>> Conditions { get; } = new List<Func<ScriptContext, bool>>();

        public virtual void OnMapEnter(ScriptContext context) { }
        public virtual void OnMapExit(ScriptContext context) { }

        public virtual async Task ExecuteAsync(ScriptContext context)
        {
            if (!CheckConditions(context)) return;

            // Plain foreach is safe because hooking happens at load time, not during
            // execution; if runtime hooking is ever added, snapshot or robust-index this loop.
            
            foreach (var action in Actions)
            {
                await action.ExecuteAsync(context);
            }
        }

        public virtual bool CheckConditions(ScriptContext context)
        {
            foreach (var condition in Conditions)
            {
                if (!condition(context)) return false;
            }
            return true;
        }

        public void AddCondition(Func<ScriptContext, bool> condition)
        {
            Conditions.Add(condition);
        }

        public void AddAction(GameCommand command)
        {
            Actions.Add(command);
        }

        public void InsertAfter<TCommand>(GameCommand command) where TCommand : GameCommand
        {
            int index = Actions.FindLastIndex(c => c is TCommand);
            if (index >= 0)
            {
                Actions.Insert(index + 1, command);
            }
            else
            {
                throw new InvalidOperationException($"Could not find command of type {typeof(TCommand).Name} to insert after in trigger '{Name}'.");
            }
        }

        public void InsertBefore<TCommand>(GameCommand command) where TCommand : GameCommand
        {
            int index = Actions.FindIndex(c => c is TCommand);
            if (index >= 0)
            {
                Actions.Insert(index, command);
            }
            else
            {
                throw new InvalidOperationException($"Could not find command of type {typeof(TCommand).Name} to insert before in trigger '{Name}'.");
            }
        }

        public void OnAnchor(object key, GameCommand command)
        {
            int index = Actions.FindIndex(c => c is AnchorCommand anchor && (anchor.Key == key || (anchor.Key != null && anchor.Key.Equals(key))));
            
            if (index >= 0)
            {
                Actions.Insert(index + 1, command);
            }
            else
            {
                throw new InvalidOperationException($"Anchor '{key}' not found in trigger '{Name}'.");
            }
        }
    }
}
