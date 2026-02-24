using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Ludots.Core.Scripting
{
    public readonly struct TriggerError
    {
        public readonly EventKey EventKey;
        public readonly string TriggerName;
        public readonly Exception Exception;

        public TriggerError(EventKey eventKey, string triggerName, Exception exception)
        {
            EventKey = eventKey;
            TriggerName = triggerName ?? string.Empty;
            Exception = exception;
        }
    }

    public class TriggerManager
    {
        // Event Key -> List of Triggers
        private readonly Dictionary<EventKey, List<Trigger>> _triggers = new Dictionary<EventKey, List<Trigger>>();
        
        // Type -> Singleton Trigger Instance
        private readonly Dictionary<Type, Trigger> _typeRegistry = new Dictionary<Type, Trigger>();

        private readonly List<TriggerError> _errors = new List<TriggerError>();
        private readonly object _errorsLock = new object();

        public IReadOnlyList<TriggerError> Errors
        {
            get
            {
                lock (_errorsLock)
                {
                    return _errors.ToArray();
                }
            }
        }

        public TriggerManager()
        {
        }

        public void RegisterTrigger(Trigger trigger)
        {
            if (trigger == null) return;
            
            // 1. Register Singleton by Type
            var type = trigger.GetType();
            if (!_typeRegistry.ContainsKey(type))
            {
                _typeRegistry[type] = trigger;
            }
            else
            {
                // If it's the base Trigger class (anonymous/builder usage), we don't overwrite the singleton registry 
                // unless we want to support only one anonymous trigger? 
                // Actually, Builder created triggers are usually instances of 'Trigger' class directly.
                // We only register subclasses as singletons usually.
                if (type != typeof(Trigger))
                {
                    Console.WriteLine($"[TriggerManager] Warning: Duplicate registration for trigger type {type.Name}. Keeping original.");
                }
            }

            // 2. Register for Event
            if (string.IsNullOrEmpty(trigger.EventKey.Value))
            {
                 // Some triggers might not be event-driven but just hooks? 
                 // But usually they need an event to start.
                 return;
            }

            if (!_triggers.ContainsKey(trigger.EventKey))
            {
                _triggers[trigger.EventKey] = new List<Trigger>();
            }
            _triggers[trigger.EventKey].Add(trigger);
        }
        
        public T Get<T>() where T : Trigger
        {
            if (_typeRegistry.TryGetValue(typeof(T), out var trigger))
            {
                return (T)trigger;
            }
            return null;
        }

        public void FireEvent(EventKey eventKey, ScriptContext context)
        {
            if (!_triggers.TryGetValue(eventKey, out var triggerList))
            {
                return;
            }

            // Console.WriteLine($"[TriggerManager] Firing event '{eventKey}' - Found {triggerList.Count} triggers.");

            // Create a snapshot to safely iterate
            var currentTriggers = triggerList.ToList();

            foreach (var trigger in currentTriggers)
            {
                // We launch the async execution. 
                // Since we are on GameSyncContext (Main Thread), the synchronous parts run immediately.
                // Awaits will post back to the context.
                _ = FireTriggerAsync(trigger, eventKey, context, propagateExceptions: false);
            }
        }

        public void FireEvent(string eventKey, ScriptContext context)
        {
            FireEvent(new EventKey(eventKey), context);
        }

        public Task FireEventAsync(EventKey eventKey, ScriptContext context)
        {
            if (!_triggers.TryGetValue(eventKey, out var triggerList) || triggerList.Count == 0)
            {
                return Task.CompletedTask;
            }

            var currentTriggers = triggerList.ToList();
            var tasks = new Task[currentTriggers.Count];
            for (int i = 0; i < currentTriggers.Count; i++)
            {
                tasks[i] = FireTriggerAsync(currentTriggers[i], eventKey, context, propagateExceptions: true);
            }
            return Task.WhenAll(tasks);
        }

        public Task FireEventAsync(string eventKey, ScriptContext context)
        {
            return FireEventAsync(new EventKey(eventKey), context);
        }

        public void ClearErrors()
        {
            lock (_errorsLock)
            {
                _errors.Clear();
            }
        }

        private async Task FireTriggerAsync(Trigger trigger, EventKey eventKey, ScriptContext context, bool propagateExceptions)
        {
            // Console.WriteLine($"[TriggerManager] Checking trigger '{trigger.Name}'...");
            try
            {
                // Lifecycle hook: OnMapEnter check? 
                // This is a bit tricky. When do we call OnMapEnter? 
                // Ideally, GameEngine calls OnMapEnter on all triggers when map loads?
                // For now, let's assume OnMapEnter is called separately or we handle it here if event is MapLoaded.
                
                if (trigger.EventKey == GameEvents.MapLoaded)
                {
                     trigger.OnMapEnter(context);
                }
                
                // Check condition
                if (trigger.CheckConditions(context))
                {
                    // Console.WriteLine($"[TriggerManager] Executing trigger '{trigger.Name}'...");
                    await trigger.ExecuteAsync(context);
                }
            }
            catch (Exception ex)
            {
                lock (_errorsLock)
                {
                    _errors.Add(new TriggerError(eventKey, trigger?.Name ?? string.Empty, ex));
                }
                Console.WriteLine($"[TriggerManager] Error executing trigger {trigger.Name}: {ex}");
                if (propagateExceptions) throw;
            }
        }
        
        public void UnregisterTrigger(Trigger trigger)
        {
             if (trigger == null) return;
             if (!string.IsNullOrEmpty(trigger.EventKey.Value) && _triggers.TryGetValue(trigger.EventKey, out var list))
             {
                 list.Remove(trigger);
             }
             // We don't remove from TypeRegistry typically, unless unloading mod?
        }
    }
}
