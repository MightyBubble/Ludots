using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ludots.Core.Diagnostics;
using Ludots.Core.Map;

namespace Ludots.Core.Scripting
{
    internal interface ITriggerResumeProbe
    {
        bool IsSuspended { get; }
    }

    public interface IMapTriggerRoute
    {
        bool IsGlobalRoute { get; }
    }

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

        // Map -> Event -> triggers, maintained in priority order at registration time so
        // steady-state dispatch is a dictionary lookup with zero allocations.
        private readonly Dictionary<MapId, Dictionary<EventKey, List<Trigger>>> _mapEventTriggers
            = new Dictionary<MapId, Dictionary<EventKey, List<Trigger>>>();

        private readonly Dictionary<MapId, Dictionary<EventKey, List<Trigger>>> _globalMapEventTriggers
            = new Dictionary<MapId, Dictionary<EventKey, List<Trigger>>>();

        private readonly Dictionary<string, List<Trigger>> _modTriggers
            = new Dictionary<string, List<Trigger>>(StringComparer.Ordinal);

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

        public bool HasSuspendedModTriggers
        {
            get
            {
                foreach (List<Trigger> triggers in _modTriggers.Values)
                {
                    for (int i = 0; i < triggers.Count; i++)
                    {
                        if (triggers[i] is ITriggerResumeProbe probe && probe.IsSuspended)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
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
            _mapTriggers[mapId] = list;
            _mapEventTriggers[mapId] = new Dictionary<EventKey, List<Trigger>>();
            _globalMapEventTriggers[mapId] = new Dictionary<EventKey, List<Trigger>>();
            RegisterIntoMapList(mapId, triggers, list);
            Log.Info(in LogChannels.Engine, $"Registered {list.Count} triggers for map '{mapId}'.");
        }

        /// <summary>
        /// Append triggers to a map's registered list (runtime entity mounts). The map
        /// must already own its initial registration via <see cref="RegisterMapTriggers"/>.
        /// </summary>
        public void AddMapTriggers(MapId mapId, IReadOnlyList<Trigger> triggers)
        {
            if (triggers == null || triggers.Count == 0) return;

            if (!_mapTriggers.TryGetValue(mapId, out List<Trigger> list))
            {
                throw new InvalidOperationException(
                    $"Cannot append triggers to map '{mapId}' before its initial map-trigger registration.");
            }

            RegisterIntoMapList(mapId, triggers, list);
            Log.Info(in LogChannels.Engine, $"Appended {triggers.Count} triggers to map '{mapId}'.");
        }

        /// <summary>
        /// Remove specific triggers from a map's registered list (dead entity-mount sweep)
        /// and unregister them from event dispatch.
        /// </summary>
        public void RemoveMapTriggers(MapId mapId, IReadOnlyList<Trigger> triggers)
        {
            if (triggers == null || triggers.Count == 0) return;

            if (!_mapTriggers.TryGetValue(mapId, out List<Trigger> list))
            {
                return;
            }

            for (int i = 0; i < triggers.Count; i++)
            {
                list.Remove(triggers[i]);
                RemoveMapEventTrigger(mapId, triggers[i]);
                UnregisterTrigger(triggers[i]);
            }
        }

        private void RegisterIntoMapList(MapId mapId, IReadOnlyList<Trigger> triggers, List<Trigger> list)
        {
            for (int i = 0; i < triggers.Count; i++)
            {
                RegisterTrigger(triggers[i]);
                list.Add(triggers[i]);
                AddMapEventTrigger(mapId, triggers[i]);
            }
        }

        private void AddMapEventTrigger(MapId mapId, Trigger trigger)
        {
            if (string.IsNullOrEmpty(trigger.EventKey.Value))
            {
                return;
            }

            Dictionary<EventKey, List<Trigger>> eventTriggers = trigger is IMapTriggerRoute { IsGlobalRoute: true }
                ? _globalMapEventTriggers[mapId]
                : _mapEventTriggers[mapId];
            if (!eventTriggers.TryGetValue(trigger.EventKey, out List<Trigger> triggers))
            {
                triggers = new List<Trigger>();
                eventTriggers[trigger.EventKey] = triggers;
            }

            int insertIndex = triggers.Count;
            while (insertIndex > 0 && triggers[insertIndex - 1].Priority > trigger.Priority)
            {
                insertIndex--;
            }

            triggers.Insert(insertIndex, trigger);
        }

        private void RemoveMapEventTrigger(MapId mapId, Trigger trigger)
        {
            if (string.IsNullOrEmpty(trigger.EventKey.Value) ||
                !TryGetMapEventTriggers(mapId, trigger, out Dictionary<EventKey, List<Trigger>> eventTriggers) ||
                !eventTriggers.TryGetValue(trigger.EventKey, out List<Trigger> triggers))
            {
                return;
            }

            triggers.Remove(trigger);
            if (triggers.Count == 0)
            {
                eventTriggers.Remove(trigger.EventKey);
            }
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
            }

            _mapTriggers.Remove(mapId);
            _mapEventTriggers.Remove(mapId);
            _globalMapEventTriggers.Remove(mapId);
            Log.Info(in LogChannels.Engine, $"Unregistered all triggers for map '{mapId}'.");
        }

        // ────────────────────────────────────────────────────────────
        // Map-scoped event firing — only triggers belonging to the
        // specified map are evaluated, sorted by Priority (ascending).
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Fire a declared custom event on a map. Fail-closed: the event name must be
        /// declared in the map's Events/custom_events.json catalog (vocabulary gate).
        /// </summary>
        public void FireMapCustomEvent(MapId mapId, string eventName, ScriptContext context, Ludots.Core.Gameplay.MapTriggers.CustomEventNameRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);
            if (!registry.IsDeclaredCustom(eventName))
            {
                throw new InvalidOperationException(
                    $"Cannot fire '{eventName}' on map '{mapId.Value}': not a declared custom event. Declared: {registry.DescribeVocabulary()}.");
            }

            FireMapEvent(mapId, new EventKey(eventName), context);
        }

        /// <summary>
        /// Fire an event only to triggers registered for the given map.
        /// Triggers are sorted by Priority (lower values execute first).
        /// Also invokes matching EventHandlers.
        /// </summary>
        public void FireMapEvent(MapId mapId, EventKey eventKey, ScriptContext context)
        {
            // EventHandlers (mod callbacks) always fire
            FireEventHandlers(eventKey, context);

            if (_mapEventTriggers.TryGetValue(mapId, out var eventTriggers) &&
                eventTriggers.TryGetValue(eventKey, out var matching))
            {
                for (int i = 0; i < matching.Count; i++)
                {
                    FireTrigger(matching[i], eventKey, context);
                }
            }

            foreach (var globalByEvent in _globalMapEventTriggers.Values)
            {
                if (!globalByEvent.TryGetValue(eventKey, out var globalMatching)) continue;
                for (int i = 0; i < globalMatching.Count; i++)
                {
                    FireTrigger(globalMatching[i], eventKey, context);
                }
            }
        }

        /// <summary>
        /// Async version of FireMapEvent.
        /// </summary>
        public Task FireMapEventAsync(MapId mapId, EventKey eventKey, ScriptContext context)
        {
            // EventHandlers (mod callbacks)
            var handlerTask = FireEventHandlersAsync(eventKey, context);

            if (!_mapEventTriggers.TryGetValue(mapId, out var eventTriggers) ||
                !eventTriggers.TryGetValue(eventKey, out var matching) ||
                matching.Count == 0)
            {
                return FireGlobalMapEventAsync(eventKey, context, handlerTask);
            }

            var tasks = new Task[matching.Count + 1];
            tasks[0] = handlerTask;
            for (int i = 0; i < matching.Count; i++)
            {
                tasks[i + 1] = FireTriggerAsync(matching[i], eventKey, context, propagateExceptions: true);
            }
            return FireGlobalMapEventAsync(eventKey, context, Task.WhenAll(tasks));
        }

        private Task FireGlobalMapEventAsync(EventKey eventKey, ScriptContext context, Task prior)
        {
            var tasks = new List<Task> { prior };
            foreach (var globalByEvent in _globalMapEventTriggers.Values)
            {
                if (!globalByEvent.TryGetValue(eventKey, out var matching)) continue;
                for (int i = 0; i < matching.Count; i++)
                {
                    tasks.Add(FireTriggerAsync(matching[i], eventKey, context, propagateExceptions: true));
                }
            }

            return tasks.Count == 1 ? prior : Task.WhenAll(tasks);
        }

        public void RegisterModTriggers(string modId, IReadOnlyList<Trigger> triggers)
        {
            if (string.IsNullOrWhiteSpace(modId)) throw new ArgumentException("Mod id is required.", nameof(modId));
            if (triggers == null || triggers.Count == 0) return;
            if (_modTriggers.ContainsKey(modId))
            {
                throw new InvalidOperationException($"Mod '{modId}' already owns TriggerGraph mounts.");
            }

            var owned = new List<Trigger>(triggers.Count);
            _modTriggers.Add(modId, owned);
            for (int i = 0; i < triggers.Count; i++)
            {
                RegisterTrigger(triggers[i]);
                owned.Add(triggers[i]);
            }
        }

        public void UnregisterModTriggers(string modId)
        {
            if (!_modTriggers.TryGetValue(modId, out var triggers)) return;
            for (int i = 0; i < triggers.Count; i++) UnregisterTrigger(triggers[i]);
            _modTriggers.Remove(modId);
        }

        public void UnregisterAllModTriggers()
        {
            if (_modTriggers.Count == 0) return;
            var ids = new List<string>(_modTriggers.Keys);
            for (int i = 0; i < ids.Count; i++) UnregisterModTriggers(ids[i]);
        }

        private bool TryGetMapEventTriggers(MapId mapId, Trigger trigger, out Dictionary<EventKey, List<Trigger>> eventTriggers)
        {
            if (trigger is IMapTriggerRoute { IsGlobalRoute: true })
            {
                return _globalMapEventTriggers.TryGetValue(mapId, out eventTriggers);
            }

            return _mapEventTriggers.TryGetValue(mapId, out eventTriggers);
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

        /// <summary>
        /// Dispatches the engine-owned Mod TriggerGraph continuation pulse through
        /// the synchronous trigger path. The event has no author-facing handlers,
        /// so registration order is stable and no snapshot, sort, or Task is needed.
        /// </summary>
        public void FireModTriggerResume(ScriptContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (!_triggers.TryGetValue(GameEvents.ModTriggerResume, out List<Trigger> triggerList))
            {
                return;
            }

            for (int i = 0; i < triggerList.Count; i++)
            {
                FireTrigger(triggerList[i], GameEvents.ModTriggerResume, context);
            }
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

private void RecordTriggerError(EventKey eventKey, Trigger trigger, Exception exception)
        {
            lock (_errorsLock)
            {
                _errors.Add(new TriggerError(eventKey, trigger.Name, exception));
            }

            Log.Error(in LogChannels.Engine, $"Error executing trigger {trigger.Name}: {exception}");
        }

private static Exception GetExecutionException(Task execution)
        {
            if (execution.IsFaulted && execution.Exception != null)
            {
                return execution.Exception.InnerException ?? execution.Exception;
            }

            return new TaskCanceledException(execution);
        }

private async Task ObserveTriggerExecutionAsync(Trigger trigger, EventKey eventKey, Task execution)
        {
            try
            {
                await execution;
            }
            catch (Exception ex)
            {
                RecordTriggerError(eventKey, trigger, ex);
            }
        }

        /// <summary>
        /// Synchronous fire path for steady-state dispatch: synchronously-completing
        /// triggers must not allocate a Task per invocation.
        /// </summary>
        private void FireTrigger(Trigger trigger, EventKey eventKey, ScriptContext context)
        {
            ArgumentNullException.ThrowIfNull(trigger);

            try
            {
                if (trigger.EventKey == GameEvents.MapLoaded)
                {
                    trigger.OnMapEnter(context);
                }

                if (!trigger.CheckConditions(context))
                {
                    return;
                }

                Task execution = trigger.ExecuteAsync(context);
                if (execution.IsCompletedSuccessfully)
                {
                    return;
                }

                if (execution.IsFaulted || execution.IsCanceled)
                {
                    RecordTriggerError(eventKey, trigger, GetExecutionException(execution));
                    return;
                }

                _ = ObserveTriggerExecutionAsync(trigger, eventKey, execution);
            }
            catch (Exception ex)
            {
                RecordTriggerError(eventKey, trigger, ex);
            }
        }

        private async Task FireTriggerAsync(Trigger trigger, EventKey eventKey, ScriptContext context, bool propagateExceptions)
        {
            ArgumentNullException.ThrowIfNull(trigger);

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
                    _errors.Add(new TriggerError(eventKey, trigger.Name, ex));
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
