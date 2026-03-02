using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ludots.Core.Diagnostics;
using Ludots.Core.Map;

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
        // Event Key -> List of Triggers (global, non-map triggers)
        private readonly Dictionary<EventKey, List<Trigger>> _triggers = new Dictionary<EventKey, List<Trigger>>();

        // Type -> Singleton Trigger Instance
        private readonly Dictionary<Type, Trigger> _typeRegistry = new Dictionary<Type, Trigger>();

        // Map-scoped trigger tracking
        private readonly Dictionary<MapId, List<Trigger>> _mapTriggers = new Dictionary<MapId, List<Trigger>>();
        private readonly HashSet<Trigger> _mapOwnedTriggers = new HashSet<Trigger>();

        // EventHandler storage (non-Trigger, simple callbacks registered by Mods)
        private readonly Dictionary<EventKey, List<Func<ScriptContext, Task>>> _eventHandlers
            = new Dictionary<EventKey, List<Func<ScriptContext, Task>>>();

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
                if (type != typeof(Trigger))
                {
                    Log.Warn(in LogChannels.Engine, $"Duplicate registration for trigger type {type.Name}. Keeping original.");
                }
            }

            // 2. Register for Event
            if (string.IsNullOrEmpty(trigger.EventKey.Value))
            {
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

        /// <summary>
        /// Register triggers owned by a specific map. They will be auto-unregistered on map unload.
        /// </summary>
        public void RegisterMapTriggers(MapId mapId, IReadOnlyList<Trigger> triggers)
        {
            if (triggers == null || triggers.Count == 0) return;

            var list = new List<Trigger>(triggers.Count);
            for (int i = 0; i < triggers.Count; i++)
            {
                RegisterTrigger(triggers[i]);
                _mapOwnedTriggers.Add(triggers[i]);
                list.Add(triggers[i]);
            }
            _mapTriggers[mapId] = list;
            Log.Info(in LogChannels.Engine, $"Registered {list.Count} triggers for map '{mapId}'.");
        }

        /// <summary>
        /// Unregister all triggers owned by a map. Calls OnMapExit before unregistering.
        /// </summary>
        public void UnregisterMapTriggers(MapId mapId, ScriptContext context)
        {
            if (!_mapTriggers.TryGetValue(mapId, out var list)) return;

            for (int i = 0; i < list.Count; i++)
            {
                try
                {
                    list[i].OnMapExit(context);
                }
                catch (Exception ex)
                {
                    Log.Error(in LogChannels.Engine, $"Error in OnMapExit for trigger '{list[i].Name}': {ex.Message}");
                }
            }

            for (int i = 0; i < list.Count; i++)
            {
                UnregisterTrigger(list[i]);
                _mapOwnedTriggers.Remove(list[i]);
            }

            _mapTriggers.Remove(mapId);
            Log.Info(in LogChannels.Engine, $"Unregistered all triggers for map '{mapId}'.");
        }

        // ────────────────────────────────────────────────────────────
        // Map-scoped event firing — only triggers belonging to the
        // specified map are evaluated, sorted by Priority (ascending).
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Fire an event only to triggers registered for the given map.
        /// Triggers are sorted by Priority (lower values execute first).
        /// Also invokes matching EventHandlers.
        /// </summary>
        public void FireMapEvent(MapId mapId, EventKey eventKey, ScriptContext context)
        {
            // EventHandlers (mod callbacks) always fire
            FireEventHandlers(eventKey, context);

            // Collect map-scoped triggers + compatible global triggers (excluding map-owned).
            var matching = CollectSortedMapAndGlobalTriggers(mapId, eventKey);
            if (matching.Count == 0) return;

            for (int i = 0; i < matching.Count; i++)
            {
                _ = FireTriggerAsync(matching[i], eventKey, context, propagateExceptions: false);
            }
        }

        /// <summary>
        /// Async version of FireMapEvent.
        /// </summary>
        public Task FireMapEventAsync(MapId mapId, EventKey eventKey, ScriptContext context)
        {
            // EventHandlers (mod callbacks)
            var handlerTask = FireEventHandlersAsync(eventKey, context);

            // Collect map-scoped triggers + compatible global triggers (excluding map-owned).
            var matching = CollectSortedMapAndGlobalTriggers(mapId, eventKey);
            if (matching.Count == 0) return handlerTask;

            var tasks = new Task[matching.Count + 1];
            tasks[0] = handlerTask;
            for (int i = 0; i < matching.Count; i++)
            {
                tasks[i + 1] = FireTriggerAsync(matching[i], eventKey, context, propagateExceptions: true);
            }
            return Task.WhenAll(tasks);
        }

        private List<Trigger> CollectSortedMapAndGlobalTriggers(MapId mapId, EventKey eventKey)
        {
            var matching = new List<Trigger>();

            if (_mapTriggers.TryGetValue(mapId, out var mapList) && mapList.Count > 0)
            {
                for (int i = 0; i < mapList.Count; i++)
                {
                    if (mapList[i].EventKey == eventKey)
                        matching.Add(mapList[i]);
                }
            }

            if (_triggers.TryGetValue(eventKey, out var globalList) && globalList.Count > 0)
            {
                for (int i = 0; i < globalList.Count; i++)
                {
                    var trigger = globalList[i];
                    if (_mapOwnedTriggers.Contains(trigger)) continue; // avoid duplicate map trigger execution
                    matching.Add(trigger);
                }
            }

            matching.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            return matching;
        }

        private static List<Trigger> CollectSortedMapTriggers(List<Trigger> mapList, EventKey eventKey)
        {
            var matching = new List<Trigger>();
            for (int i = 0; i < mapList.Count; i++)
            {
                if (mapList[i].EventKey == eventKey)
                    matching.Add(mapList[i]);
            }

            // Sort by Priority ascending (lower Priority executes first)
            matching.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            return matching;
        }

        // ────────────────────────────────────────────────────────────
        // Global event firing (for non-map events: GameStart, Tick, etc.)
        // Now also sorted by Priority.
        // ────────────────────────────────────────────────────────────

        public void FireEvent(EventKey eventKey, ScriptContext context)
        {
            FireEventHandlers(eventKey, context);

            if (!_triggers.TryGetValue(eventKey, out var triggerList))
            {
                return;
            }

            // Create a snapshot sorted by Priority
            var currentTriggers = new List<Trigger>(triggerList);
            currentTriggers.Sort((a, b) => a.Priority.CompareTo(b.Priority));

            for (int i = 0; i < currentTriggers.Count; i++)
            {
                _ = FireTriggerAsync(currentTriggers[i], eventKey, context, propagateExceptions: false);
            }
        }

        public void FireEvent(string eventKey, ScriptContext context)
        {
            FireEvent(new EventKey(eventKey), context);
        }

        public Task FireEventAsync(EventKey eventKey, ScriptContext context)
        {
            var handlerTask = FireEventHandlersAsync(eventKey, context);

            if (!_triggers.TryGetValue(eventKey, out var triggerList) || triggerList.Count == 0)
            {
                return handlerTask;
            }

            var currentTriggers = new List<Trigger>(triggerList);
            currentTriggers.Sort((a, b) => a.Priority.CompareTo(b.Priority));

            var tasks = new Task[currentTriggers.Count + 1];
            tasks[0] = handlerTask;
            for (int i = 0; i < currentTriggers.Count; i++)
            {
                tasks[i + 1] = FireTriggerAsync(currentTriggers[i], eventKey, context, propagateExceptions: true);
            }
            return Task.WhenAll(tasks);
        }

        public Task FireEventAsync(string eventKey, ScriptContext context)
        {
            return FireEventAsync(new EventKey(eventKey), context);
        }

        // ────────────────────────────────────────────────────────────
        // EventHandler registration (simple mod callbacks, not Triggers)
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Register a simple event handler callback. Unlike Triggers, handlers have
        /// no conditions, priority, or lifecycle hooks — they just execute.
        /// Primarily for Mod OnLoad callbacks via IModContext.OnEvent().
        /// </summary>
        public void RegisterEventHandler(EventKey eventKey, Func<ScriptContext, Task> handler)
        {
            if (handler == null) return;

            if (!_eventHandlers.TryGetValue(eventKey, out var list))
            {
                list = new List<Func<ScriptContext, Task>>();
                _eventHandlers[eventKey] = list;
            }
            list.Add(handler);
        }

        private void FireEventHandlers(EventKey eventKey, ScriptContext context)
        {
            if (!_eventHandlers.TryGetValue(eventKey, out var handlers) || handlers.Count == 0)
                return;

            for (int i = 0; i < handlers.Count; i++)
            {
                try
                {
                    _ = handlers[i](context);
                }
                catch (Exception ex)
                {
                    Log.Error(in LogChannels.Engine, $"Error in event handler for '{eventKey}': {ex.Message}");
                }
            }
        }

        private Task FireEventHandlersAsync(EventKey eventKey, ScriptContext context)
        {
            if (!_eventHandlers.TryGetValue(eventKey, out var handlers) || handlers.Count == 0)
                return Task.CompletedTask;

            var tasks = new Task[handlers.Count];
            for (int i = 0; i < handlers.Count; i++)
            {
                try
                {
                    tasks[i] = handlers[i](context);
                }
                catch (Exception ex)
                {
                    Log.Error(in LogChannels.Engine, $"Error in event handler for '{eventKey}': {ex.Message}");
                    tasks[i] = Task.CompletedTask;
                }
            }
            return Task.WhenAll(tasks);
        }

        // ────────────────────────────────────────────────────────────
        // Core
        // ────────────────────────────────────────────────────────────

        public void ClearErrors()
        {
            lock (_errorsLock)
            {
                _errors.Clear();
            }
        }

        private async Task FireTriggerAsync(Trigger trigger, EventKey eventKey, ScriptContext context, bool propagateExceptions)
        {
            try
            {
                if (trigger.EventKey == GameEvents.MapLoaded)
                {
                     trigger.OnMapEnter(context);
                }

                // Check condition
                if (trigger.CheckConditions(context))
                {
                    await trigger.ExecuteAsync(context);
                }
            }
            catch (Exception ex)
            {
                lock (_errorsLock)
                {
                    _errors.Add(new TriggerError(eventKey, trigger?.Name ?? string.Empty, ex));
                }
                Log.Error(in LogChannels.Engine, $"Error executing trigger {trigger.Name}: {ex}");
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
             // Remove from type registry if it's the same instance
             var type = trigger.GetType();
             if (_typeRegistry.TryGetValue(type, out var registered) && ReferenceEquals(registered, trigger))
             {
                 _typeRegistry.Remove(type);
             }
        }
    }
}
