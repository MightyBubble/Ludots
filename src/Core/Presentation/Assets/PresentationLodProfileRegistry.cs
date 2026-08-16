using System;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class PresentationLodProfileRegistry
    {
        private readonly StringIntRegistry _ids;
        private PresentationLodProfile[] _profiles;
        private bool[] _hasProfiles;

        public PresentationLodProfileRegistry(int capacity = 16)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _profiles = new PresentationLodProfile[capacity];
            _hasProfiles = new bool[capacity];
        }

        public int Register(string key, in PresentationLodProfile profile)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Presentation LOD profile key must not be empty.", nameof(key));
            }

            if (_ids.TryGetId(key, out int existingId) && IsRegistered(existingId))
            {
                throw new InvalidOperationException($"Presentation LOD profile '{key}' is already registered.");
            }

            int id = _ids.Register(key);
            EnsureCapacity(id);
            _profiles[id] = profile;
            _hasProfiles[id] = true;
            return id;
        }

        public int GetId(string key) => _ids.GetId(key);

        public string GetName(int id) => _ids.GetName(id);

        public bool TryGet(string key, out PresentationLodProfile profile)
        {
            if (_ids.TryGetId(key, out int id))
            {
                return TryGet(id, out profile);
            }

            profile = default;
            return false;
        }

        public bool TryGet(int id, out PresentationLodProfile profile)
        {
            if ((uint)id < (uint)_profiles.Length && _hasProfiles[id])
            {
                profile = _profiles[id];
                return true;
            }

            profile = default;
            return false;
        }

        public PresentationLodProfile Require(string key)
        {
            if (!TryGet(key, out PresentationLodProfile profile))
            {
                throw new InvalidOperationException($"Presentation LOD profile '{key}' is not registered.");
            }

            return profile;
        }

        private bool IsRegistered(int id)
        {
            return (uint)id < (uint)_hasProfiles.Length && _hasProfiles[id];
        }

        private void EnsureCapacity(int id)
        {
            if (id < _profiles.Length)
            {
                return;
            }

            int newLength = Math.Max(_profiles.Length * 2, id + 1);
            Array.Resize(ref _profiles, newLength);
            Array.Resize(ref _hasProfiles, newLength);
        }
    }
}
