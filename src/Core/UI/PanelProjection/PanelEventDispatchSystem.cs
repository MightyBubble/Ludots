using System;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// In-tick drain for panel companion events: payload-carrying panel fires queue on the
    /// <see cref="PanelEventActionBridge"/> at click time and dispatch here, in the engine's
    /// EventDispatch phase — never at presentation time — so trigger-graph consumption of
    /// panel payloads keeps deterministic phase ordering with every other event source.
    /// </summary>
    public sealed class PanelEventDispatchSystem : ISystem<float>
    {
        private readonly Func<PanelEventActionBridge?> _bridge;
        private readonly TriggerManager _triggers;
        private readonly Gameplay.MapTriggers.CustomEventNameRegistry _customEvents;
        private readonly Func<MapId?> _currentMapId;
        private readonly Func<ScriptContext> _createContext;
        private int _lastDispatched;

        public PanelEventDispatchSystem(
            Func<PanelEventActionBridge?> bridge,
            TriggerManager triggers,
            Gameplay.MapTriggers.CustomEventNameRegistry customEvents,
            Func<MapId?> currentMapId,
            Func<ScriptContext> createContext)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            _triggers = triggers ?? throw new ArgumentNullException(nameof(triggers));
            _customEvents = customEvents ?? throw new ArgumentNullException(nameof(customEvents));
            _currentMapId = currentMapId ?? throw new ArgumentNullException(nameof(currentMapId));
            _createContext = createContext ?? throw new ArgumentNullException(nameof(createContext));
        }

        /// <summary>Events dispatched by the last Update (diagnostics/tests).</summary>
        public int LastDispatched => _lastDispatched;

        public void Initialize() { }

        public void Update(in float dt)
        {
            PanelEventActionBridge? bridge = _bridge();
            if (bridge == null)
            {
                _lastDispatched = 0;
                return;
            }

            MapId mapId = _currentMapId() ?? default;
            if (mapId == default)
            {
                _lastDispatched = 0;
                return;
            }

            _lastDispatched = bridge.DispatchPendingCompanions(_triggers, _customEvents, mapId, _createContext);
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
