using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;

namespace Ludots.Core.Map
{
    /// <summary>
    /// Manages concurrent MapSessions with a focus stack for nested map support.
    /// </summary>
    public sealed class MapSessionManager
    {
        private readonly Dictionary<MapId, MapSession> _sessions = new Dictionary<MapId, MapSession>();
        private readonly Stack<MapId> _focusStack = new Stack<MapId>();

        public MapSession FocusedSession
        {
            get
            {
                if (_focusStack.Count == 0) return null;
                var top = _focusStack.Peek();
                return _sessions.TryGetValue(top, out var session) ? session : null;
            }
        }

        public IReadOnlyDictionary<MapId, MapSession> All => _sessions;

        public MapSession CreateSession(MapId mapId, MapConfig mapConfig, MapContext parentContext = null)
        {
            if (_sessions.ContainsKey(mapId))
            {
                Log.Warn(in LogChannels.Map, $"MapSession for '{mapId}' already exists. Replacing.");
                _sessions[mapId].Dispose();
                _sessions.Remove(mapId);
            }

            var session = new MapSession(mapId, mapConfig, parentContext);
            _sessions[mapId] = session;
            return session;
        }

        public void PushFocused(MapId mapId)
        {
            // Suspend current focused session
            if (_focusStack.Count > 0)
            {
                var current = _focusStack.Peek();
                if (_sessions.TryGetValue(current, out var currentSession))
                {
                    currentSession.State = MapSessionState.Suspended;
                    Log.Info(in LogChannels.Map, $"Map '{current}' suspended.");
                }
            }

            _focusStack.Push(mapId);

            if (_sessions.TryGetValue(mapId, out var session))
            {
                session.State = MapSessionState.Active;
            }

            Log.Info(in LogChannels.Map, $"Map '{mapId}' pushed to focus (stack depth={_focusStack.Count}).");
        }

        public MapId PopFocused()
        {
            if (_focusStack.Count == 0)
                throw new InvalidOperationException("Focus stack is empty.");

            var popped = _focusStack.Pop();
            Log.Info(in LogChannels.Map, $"Map '{popped}' popped from focus.");

            // Restore previous focused session
            if (_focusStack.Count > 0)
            {
                var restored = _focusStack.Peek();
                if (_sessions.TryGetValue(restored, out var restoredSession))
                {
                    restoredSession.State = MapSessionState.Active;
                    Log.Info(in LogChannels.Map, $"Map '{restored}' restored to Active.");
                }
            }

            return popped;
        }

        public void UnloadSession(MapId mapId, World world)
        {
            if (!_sessions.TryGetValue(mapId, out var session)) return;

            session.Cleanup(world);
            _sessions.Remove(mapId);

            // Remove from focus stack (may be at any position)
            RemoveFromFocusStack(mapId);

            // Restore the new top-of-stack to Active
            if (_focusStack.Count > 0)
            {
                var newTop = _focusStack.Peek();
                if (_sessions.TryGetValue(newTop, out var restoredSession) && restoredSession.State == MapSessionState.Suspended)
                {
                    restoredSession.State = MapSessionState.Active;
                    Log.Info(in LogChannels.Map, $"Map '{newTop}' restored to Active after unload of '{mapId}'.");
                }
            }

            Log.Info(in LogChannels.Map, $"Map '{mapId}' unloaded.");
        }

        private void RemoveFromFocusStack(MapId mapId)
        {
            // Stack doesn't support random removal, so rebuild without the target
            var temp = new Stack<MapId>();
            while (_focusStack.Count > 0)
            {
                var id = _focusStack.Pop();
                if (id != mapId)
                    temp.Push(id);
            }
            // Reverse back into _focusStack to preserve order
            while (temp.Count > 0)
                _focusStack.Push(temp.Pop());
        }

        public MapSession GetSession(MapId mapId)
        {
            return _sessions.TryGetValue(mapId, out var session) ? session : null;
        }
    }
}
