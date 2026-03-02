using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.Map.Board;
using Ludots.Core.Scripting;

namespace Ludots.Core.Map
{
    /// <summary>
    /// Lifecycle manager for a loaded map.
    /// Holds Board collection, triggers, context, and state.
    /// </summary>
    public sealed class MapSession : IDisposable
    {
        public MapId MapId { get; }
        public MapConfig MapConfig { get; }
        public MapSessionState State { get; set; }
        public MapContext Context { get; }

        private readonly Dictionary<string, IBoard> _boards = new Dictionary<string, IBoard>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Trigger> _triggers = new List<Trigger>();

        private static readonly QueryDescription _mapEntityQuery =
            new QueryDescription().WithAll<MapEntity>();

        public MapSession(MapId mapId, MapConfig mapConfig, MapContext parentContext = null)
        {
            MapId = mapId;
            MapConfig = mapConfig;
            State = MapSessionState.Active;
            Context = new MapContext(parentContext);
        }

        public void AddBoard(IBoard board)
        {
            _boards[board.Name] = board;
        }

        public IBoard GetBoard(string name)
        {
            return _boards.TryGetValue(name, out var board) ? board : null;
        }

        public T GetBoard<T>(string name) where T : class, IBoard
        {
            return _boards.TryGetValue(name, out var board) ? board as T : null;
        }

        /// <summary>
        /// Returns the first board, or null. Convenience for single-board maps.
        /// </summary>
        public IBoard PrimaryBoard
        {
            get
            {
                foreach (var kvp in _boards)
                    return kvp.Value;
                return null;
            }
        }

        public IReadOnlyList<IBoard> AllBoards
        {
            get
            {
                var list = new List<IBoard>(_boards.Count);
                foreach (var kvp in _boards)
                    list.Add(kvp.Value);
                return list;
            }
        }

        public void AddTrigger(Trigger trigger)
        {
            _triggers.Add(trigger);
        }

        public IReadOnlyList<Trigger> Triggers => _triggers;

        /// <summary>
        /// Destroy entities belonging to THIS map (filtered by MapId) and dispose all boards.
        /// </summary>
        public void Cleanup(World world)
        {
            if (world == null) return;

            Log.Info(in LogChannels.Map, $"Cleaning up map '{MapId}'...");

            // Collect entities that belong to THIS map, then destroy.
            // Two-pass to avoid structural changes during query iteration.
            var toDestroy = new List<Entity>();
            var targetMapId = MapId;
            world.Query(in _mapEntityQuery, (Entity entity, ref MapEntity mapEntity) =>
            {
                if (mapEntity.MapId == targetMapId)
                    toDestroy.Add(entity);
            });

            for (int i = 0; i < toDestroy.Count; i++)
            {
                if (world.IsAlive(toDestroy[i]))
                    world.Destroy(toDestroy[i]);
            }

            Log.Info(in LogChannels.Map, $"Destroyed {toDestroy.Count} map entities (MapId={MapId}).");

            // Dispose all boards
            foreach (var kvp in _boards)
            {
                try
                {
                    kvp.Value.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Error(in LogChannels.Map, $"Error disposing board '{kvp.Key}': {ex.Message}");
                }
            }
            _boards.Clear();

            State = MapSessionState.Disposed;
        }

        public void Dispose()
        {
            if (State != MapSessionState.Disposed)
            {
                foreach (var kvp in _boards)
                {
                    try { kvp.Value.Dispose(); } catch { }
                }
                _boards.Clear();
                State = MapSessionState.Disposed;
            }
        }
    }
}
