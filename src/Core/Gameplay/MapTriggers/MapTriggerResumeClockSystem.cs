using System;
using System.Collections.Generic;
using Arch.System;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Resumes suspended map/entity-domain TriggerGraphs on the fixed-step deferred
    /// trigger phase (#1398 刀2). Replaces the retired MapHeartbeat think-wave as the
    /// map/entity-domain continuation cadence.
    ///
    /// Gating: a map's pulse fires only while that map carries a suspended run
    /// (<see cref="TriggerManager.HasSuspendedMapTriggers"/>), so the steady state
    /// (no suspension) costs one probe per map and fires nothing.
    ///
    /// Cadence: per-map intervals follow <see cref="MapConfig.HeartbeatIntervalTicks"/>
    /// (default 1). A suspended graph therefore resumes at most once per interval —
    /// preserving the retired pump's "N beats" pacing contract for graphs that use
    /// Yield suspension as a business-clock (e.g. night-raid's two-beat delay), without
    /// ever broadcasting to idle maps.
    /// </summary>
    public sealed class MapTriggerResumeClockSystem : ISystem<float>
    {
        public const int DefaultIntervalTicks = 1;

        private readonly Func<MapSessionManager?> _sessions;
        private readonly TriggerManager _triggerManager;
        private readonly Func<ScriptContext> _contextFactory;
        private readonly Dictionary<MapId, int> _ticksByMap = new();
        private ScriptContext? _context;

        public MapTriggerResumeClockSystem(
            Func<MapSessionManager?> sessions,
            TriggerManager triggerManager,
            Func<ScriptContext> contextFactory)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public void Initialize() { }

        public void BeforeUpdate(in float dt) { }

        public void Update(in float dt)
        {
            MapSessionManager? sessions = _sessions();
            if (sessions == null)
            {
                return;
            }

            foreach (KeyValuePair<MapId, MapSession> pair in sessions.All)
            {
                MapSession session = pair.Value;
                if (session.State != MapSessionState.Active)
                {
                    _ticksByMap.Remove(pair.Key);
                    continue;
                }

                if (!_triggerManager.HasSuspendedMapTriggers(pair.Key))
                {
                    _ticksByMap.Remove(pair.Key);
                    continue;
                }

                int interval = session.MapConfig?.HeartbeatIntervalTicks ?? DefaultIntervalTicks;
                if (interval < 1)
                {
                    interval = DefaultIntervalTicks;
                }

                _ticksByMap.TryGetValue(pair.Key, out int elapsed);
                elapsed++;
                if (elapsed < interval)
                {
                    _ticksByMap[pair.Key] = elapsed;
                    continue;
                }

                _ticksByMap.Remove(pair.Key);
                _context ??= _contextFactory();
                _context.Set(CoreServiceKeys.MapId, pair.Key);
                _triggerManager.FireMapTriggerResume(pair.Key, _context);
            }
        }

        public void AfterUpdate(in float dt) { }

        public void Dispose() { }
    }
}

