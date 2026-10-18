using System.Collections.Generic;

namespace Ludots.Core.Map.Board
{
    /// <summary>
    /// Interning registry: maps BoardId string to a stable int for use in blittable ECS components.
    /// Thread-safe via locking.
    /// </summary>
    public sealed class BoardIdRegistry
    {
        private readonly Dictionary<string, int> _nameToId = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _idToName = new List<string>();
        private readonly object _lock = new object();

        public int GetOrAdd(string boardName)
        {
            lock (_lock)
            {
                if (_nameToId.TryGetValue(boardName, out int id))
                    return id;

                id = _idToName.Count;
                _nameToId[boardName] = id;
                _idToName.Add(boardName);
                return id;
            }
        }

        public string GetName(int id)
        {
            lock (_lock)
            {
                return (id >= 0 && id < _idToName.Count) ? _idToName[id] : null;
            }
        }
    }
}
