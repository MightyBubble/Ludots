using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ludots.Core.Diagnostics;
using Ludots.Core.Gameplay.MapTriggers;
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

        // Global-scope event subscriptions owned by maps (#1123): Event -> priority-ordered
        // triggers. Kept separate from _mapEventTriggers so FireGlobalEvent cost scales with
        // the global subscriber count alone, never with loaded map or map-trigger volume.
        private readonly Dictionary<EventKey, List<Trigger>> _globalEventTriggers
            = new Dictionary<EventKey, List<Trigger>>();

        // Reverse index: owning map -> its global-subscription triggers, so suspend /
        // resume / unload detach a whole map's global subscriptions without scanning
        // every event list (fire-side stays a plain dictionary lookup).
        private readonly Dictionary<MapId, List<Trigger>> _mapGlobalTriggers
            = new Dictionary<MapId, List<Trigger>>();

        // Maps whose global subscriptions are currently detached (suspended focus,
        // in-flight map load). Registration while suspended parks in the reverse index only.
        private readonly HashSet<MapId> _suspendedGlobalMaps = new HashSet<MapId>();


        private readonly Dictionary<string, List<Trigger>> _modTriggers
            = new Dictionary<string, List<Trigger>>(StringComparer.Ordinal);


        // EventHandler storage (non-Trigger, simple callbacks registered by Mods)
        private readonly Dictionary<EventKey, List<Func<ScriptContext, Task>>> _eventHandlers
            = new Dictionary<EventKey, List<Func<ScriptContext, Task>>>();

        private readonly List<TriggerError> _errors = new List<TriggerError>();
        private readonly object _errorsLock = new object();
        private TriggerGraphActionBindingIndex? _actionBindings;

        /// <summary>
        /// Optional index of action-bound TriggerGraph mounts. When set, Register/Remove
        /// of map triggers keeps the index in sync with action-bound mounts.
        /// </summary>
        public TriggerGraphActionBindingIndex? ActionBindings
        {
            get => _actionBindings;
            set => _actionBindings = value;
        }

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

        /// <summary>
        /// Event parameter schema SSOT; when bound, every map-domain fire validates its
        /// payload contract (missing / mistyped / undeclared MapTrigger.* keys fail closed).
        /// </summary>
        public EventSchemaRegistry? EventSchemas { get; set; }

        /// <summary>
        /// Live map sessions; when bound, FireCrossMapEvent fail-closes on targets that
        /// have no loaded session instead of dispatching into nothing.
        /// </summary>
        public MapSessionManager? MapSessions { get; set; }

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
            RegisterIntoMapList(mapId, triggers, list);
            Log.Info(in LogChannels.Engine, $"Registered {list.Count} triggers for map '{mapId}'.");
        }

        /// <summary>
        /// True when the map already owns an initial trigger registration (runtime append
        /// callers like the context trigger gate need this probe to pick between
        /// <see cref="RegisterMapTriggers"/> and <see cref="AddMapTriggers"/>).
        /// </summary>
        public bool OwnsMapTriggers(MapId mapId)
        {
            return _mapTriggers.ContainsKey(mapId);
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
                if (triggers[i] is TriggerGraphMountTrigger actionMount &&
                    !string.IsNullOrWhiteSpace(actionMount.ActionId))
                {
                    _actionBindings?.Remove(actionMount);
                }

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
                if (triggers[i] is TriggerGraphMountTrigger actionMount &&
                    !string.IsNullOrWhiteSpace(actionMount.ActionId))
                {
                    _actionBindings?.Add(actionMount);
                }
            }
        }

        private void AddMapEventTrigger(MapId mapId, Trigger trigger)
        {
            if (string.IsNullOrEmpty(trigger.EventKey.Value))
            {
                return;
            }

            Dictionary<EventKey, List<Trigger>> eventTriggers = _mapEventTriggers[mapId];
            if (!eventTriggers.TryGetValue(trigger.EventKey, out List<Trigger> triggers))
            {
                triggers = new List<Trigger>();
                eventTriggers[trigger.EventKey] = triggers;
            }

            InsertSorted(triggers, trigger);
        }

        // Priority ascending (lower Priority executes first), same contract as the map
        // event tables; maintained at registration time so dispatch never sorts.
        private static void InsertSorted(List<Trigger> triggers, Trigger trigger)
        {
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
                !_mapEventTriggers.TryGetValue(mapId, out Dictionary<EventKey, List<Trigger>> eventTriggers) ||
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

        // ────────────────────────────────────────────────────────────
        // Global-scope subscriptions (#1123): map-owned triggers for
        // schema.Scope==Global events, dispatched by FireGlobalEvent
        // independent of map focus. Only triggers whose event schema
        // declares Global scope may enter this table (mount-time gate).
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Register global-scope event triggers owned by a map. The map owns the trigger
        /// instances; unload detaches them wholesale. A currently-suspended map parks its
        /// subscriptions in the reverse index only until <see cref="SetGlobalTriggersSuspended"/>.
        /// </summary>
        public void RegisterGlobalTriggers(MapId mapId, IReadOnlyList<Trigger> triggers)
        {
            if (triggers == null || triggers.Count == 0) return;

            if (!_mapGlobalTriggers.TryGetValue(mapId, out List<Trigger> owned))
            {
                owned = new List<Trigger>(triggers.Count);
                _mapGlobalTriggers[mapId] = owned;
            }

            for (int i = 0; i < triggers.Count; i++)
            {
                Trigger trigger = triggers[i];
                if (trigger == null || string.IsNullOrEmpty(trigger.EventKey.Value))
                {
                    continue;
                }

                owned.Add(trigger);
                if (!_suspendedGlobalMaps.Contains(mapId))
                {
                    AddGlobalEventTrigger(trigger);
                }
            }

            Log.Info(in LogChannels.Engine, $"Registered {triggers.Count} global-scope triggers for map '{mapId}'.");
        }

        /// <summary>
        /// Detach a map's global subscriptions wholesale (map unload). Suspend/resume
        /// transitions use <see cref="SetGlobalTriggersSuspended"/> instead.
        /// </summary>
        public void UnregisterGlobalTriggers(MapId mapId)
        {
            if (!_mapGlobalTriggers.TryGetValue(mapId, out List<Trigger> owned))
            {
                return;
            }

            for (int i = 0; i < owned.Count; i++)
            {
                RemoveGlobalEventTrigger(owned[i]);
            }

            _mapGlobalTriggers.Remove(mapId);
            _suspendedGlobalMaps.Remove(mapId);
            Log.Info(in LogChannels.Engine, $"Unregistered global-scope triggers for map '{mapId}'.");
        }

        /// <summary>
        /// Suspend/resume a map's global subscriptions: detached triggers leave the
        /// dispatch table entirely (fire-side stays zero-check) and return in priority
        /// order on resume. Map loads are briefly suspended too — a map's global
        /// subscriptions only go live once its load completes.
        /// </summary>
        public void SetGlobalTriggersSuspended(MapId mapId, bool suspended)
        {
            if (suspended)
            {
                if (!_suspendedGlobalMaps.Add(mapId))
                {
                    return;
                }

                if (_mapGlobalTriggers.TryGetValue(mapId, out List<Trigger> owned))
                {
                    for (int i = 0; i < owned.Count; i++)
                    {
                        RemoveGlobalEventTrigger(owned[i]);
                    }
                }

                return;
            }

            if (!_suspendedGlobalMaps.Remove(mapId))
            {
                return;
            }

            if (_mapGlobalTriggers.TryGetValue(mapId, out List<Trigger> resumed))
            {
                for (int i = 0; i < resumed.Count; i++)
                {
                    AddGlobalEventTrigger(resumed[i]);
                }
            }
        }

        private void AddGlobalEventTrigger(Trigger trigger)
        {
            if (!_globalEventTriggers.TryGetValue(trigger.EventKey, out List<Trigger> triggers))
            {
                triggers = new List<Trigger>();
                _globalEventTriggers[trigger.EventKey] = triggers;
            }

            InsertSorted(triggers, trigger);
        }

        private void RemoveGlobalEventTrigger(Trigger trigger)
        {
            if (!_globalEventTriggers.TryGetValue(trigger.EventKey, out List<Trigger> triggers))
            {
                return;
            }

            triggers.Remove(trigger);
            if (triggers.Count == 0)
            {
                _globalEventTriggers.Remove(trigger.EventKey);
            }
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

        /// <summary>
        /// Unregister all triggers owned by a map. Calls OnMapExit before unregistering.
        /// </summary>
        public void UnregisterMapTriggers(MapId mapId, ScriptContext context)
        {
            if (!_mapTriggers.TryGetValue(mapId, out var list))
            {
                // A map can own global-scope subscriptions with an empty map table.
                UnregisterGlobalTriggers(mapId);
                return;
            }

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
            UnregisterGlobalTriggers(mapId);
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
            EventSchemas?.ValidateFirePayload(eventKey, context);

            // EventHandlers (mod callbacks) always fire
            FireEventHandlers(eventKey, context);

            if (!_mapEventTriggers.TryGetValue(mapId, out var eventTriggers) ||
                !eventTriggers.TryGetValue(eventKey, out var matching) ||
                matching.Count == 0)
                return;

            for (int i = 0; i < matching.Count; i++)
            {
                FireTrigger(matching[i], eventKey, context);
            }
        }

        /// <summary>
        /// Fire a Global-scope event to every live (non-suspended, loaded) global
        /// subscription, priority ascending, plus mod event handlers. Deliberately does
        /// NOT touch the legacy _triggers table (FireEvent) or any map table: dispatch
        /// cost scales with the global subscriber count alone (#1123).
        /// </summary>
        public void FireGlobalEvent(EventKey eventKey, ScriptContext context)
        {
            EventSchemas?.ValidateFirePayload(eventKey, context);

            FireEventHandlers(eventKey, context);

            if (!_globalEventTriggers.TryGetValue(eventKey, out var matching) || matching.Count == 0)
                return;

            for (int i = 0; i < matching.Count; i++)
            {
                FireTrigger(matching[i], eventKey, context);
            }
        }

        /// <summary>
        /// Point-to-point cross-map fire (#1123): stamps MapTrigger.SourceMapId with the
        /// source map and dispatches through the target map's table only — no other map
        /// and no global subscription sees it. Fail closed when the target map has no
        /// loaded session; a loaded target with zero subscribers is a normal no-op.
        /// </summary>
        public void FireCrossMapEvent(MapId sourceMapId, MapId targetMapId, EventKey eventKey, ScriptContext context)
        {
            if (EventSchemas != null &&
                EventSchemas.TryGet(eventKey.Value, out EventSchema schema) &&
                schema.Scope == EventScope.Global)
            {
                throw new InvalidOperationException(
                    $"Cannot cross-map fire '{eventKey.Value}': the event schema declares Global scope; " +
                    "global events dispatch through FireGlobalEvent, not a per-map table.");
            }

            if (MapSessions == null || MapSessions.GetSession(targetMapId) == null)
            {
                throw new InvalidOperationException(
                    $"Cannot cross-map fire '{eventKey.Value}' at map '{targetMapId.Value}': no loaded map session " +
                    "for the target; cross-map fire fails closed instead of dispatching into nothing.");
            }

            context.Set(MapTriggerEventPayloadKeys.SourceMapId, sourceMapId);
            FireMapEvent(targetMapId, eventKey, context);
        }

        /// <summary>
        /// Zero-allocation subscriber probe for fire-side early returns: true when the
        /// map carries triggers for the event or any global event handler matches it.
        /// </summary>
        public bool HasMapEventSubscribers(MapId mapId, EventKey eventKey)
        {
            if (_eventHandlers.TryGetValue(eventKey, out var handlers) && handlers.Count > 0)
            {
                return true;
            }

            return _mapEventTriggers.TryGetValue(mapId, out var eventTriggers) &&
                eventTriggers.TryGetValue(eventKey, out var matching) &&
                matching.Count > 0;
        }

        /// <summary>
        /// Async version of FireMapEvent.
        /// </summary>
        public Task FireMapEventAsync(MapId mapId, EventKey eventKey, ScriptContext context)
        {
            EventSchemas?.ValidateFirePayload(eventKey, context);

            // EventHandlers (mod callbacks)
            var handlerTask = FireEventHandlersAsync(eventKey, context);

            if (!_mapEventTriggers.TryGetValue(mapId, out var eventTriggers) ||
                !eventTriggers.TryGetValue(eventKey, out var matching) ||
                matching.Count == 0)
                return handlerTask;

            var tasks = new Task[matching.Count + 1];
            tasks[0] = handlerTask;
            for (int i = 0; i < matching.Count; i++)
            {
                tasks[i + 1] = FireTriggerAsync(matching[i], eventKey, context, propagateExceptions: true);
            }
            return Task.WhenAll(tasks);
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
        /// Dispatches one already-mounted trigger outside the event bus (action-bound
        /// TriggerGraph entries). Uses the same CheckConditions / ExecuteAsync / error
        /// recording path as map-event fire.
        /// </summary>
        public void DispatchMountedTrigger(Trigger trigger, ScriptContext context)
        {
            ArgumentNullException.ThrowIfNull(trigger);
            ArgumentNullException.ThrowIfNull(context);
            EventKey key = string.IsNullOrEmpty(trigger.EventKey.Value)
                ? GameEvents.InputAction
                : trigger.EventKey;
            EventSchemas?.ValidateFirePayload(key, context);
            FireTrigger(trigger, key, context);
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
