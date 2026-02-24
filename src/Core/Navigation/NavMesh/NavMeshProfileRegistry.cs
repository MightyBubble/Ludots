using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Navigation.NavMesh
{
    public sealed class NavMeshProfileRegistry
    {
        private readonly Dictionary<string, int> _indexById;
        private readonly List<string> _idsByIndex;

        public NavMeshProfileRegistry(NavMeshBakeConfig cfg)
        {
            if (cfg?.Profiles == null || cfg.Profiles.Count == 0) throw new InvalidOperationException("NavMeshBakeConfig.profiles is empty.");
            _indexById = new Dictionary<string, int>(cfg.Profiles.Count, StringComparer.OrdinalIgnoreCase);
            _idsByIndex = new List<string>(cfg.Profiles.Count);

            for (int i = 0; i < cfg.Profiles.Count; i++)
            {
                var p = cfg.Profiles[i];
                if (p == null) throw new InvalidOperationException("NavMeshBakeConfig.profiles contains null.");
                if (string.IsNullOrWhiteSpace(p.Id)) throw new InvalidOperationException("NavMeshBakeConfig.profiles.id is required.");
                if (_indexById.ContainsKey(p.Id)) throw new InvalidOperationException($"Duplicate NavMesh profile id: {p.Id}");
                _indexById[p.Id] = i;
                _idsByIndex.Add(p.Id);
            }
        }

        public int Count => _idsByIndex.Count;

        public bool TryGetIndex(string profileId, out int index)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                index = -1;
                return false;
            }
            return _indexById.TryGetValue(profileId, out index);
        }

        public string GetId(int index)
        {
            if ((uint)index >= (uint)_idsByIndex.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _idsByIndex[index];
        }
    }
}

