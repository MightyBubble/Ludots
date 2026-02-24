using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ludots.Core.Commands;

namespace Ludots.Core.Scripting
{
    public class Trigger
    {
        // Use Type Name as ID by default, or Name if overridden
        public virtual string Name => GetType().Name;
        public EventKey EventKey { get; set; }
        public int Priority { get; set; }

        public List<GameCommand> Actions { get; } = new List<GameCommand>();
        public List<Func<ScriptContext, bool>> Conditions { get; } = new List<Func<ScriptContext, bool>>();

        // Lifecycle Hooks
        public virtual void OnMapEnter(ScriptContext context) { }
        public virtual void OnMapExit(ScriptContext context) { }

        // Execution Logic
        public virtual async Task ExecuteAsync(ScriptContext context)
        {
            if (!CheckConditions(context)) return;

            // Execute Actions sequentially
            // We use a for-loop to allow modification of the list during execution (e.g. appending), 
            // though insertion at current index might be tricky. 
            // Standard approach: snapshot or robust index. 
            // For now, standard foreach is fine unless we modify the list *while* iterating.
            // If we allow dynamic hooking at runtime (while running), we should be careful.
            // Hooking usually happens at Load time.
            
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

        // Helper APIs
        public void AddCondition(Func<ScriptContext, bool> condition)
        {
            Conditions.Add(condition);
        }

        public void AddAction(GameCommand command)
        {
            Actions.Add(command);
        }

        // Hook APIs
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
            // Find AnchorCommand with matching Key
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
