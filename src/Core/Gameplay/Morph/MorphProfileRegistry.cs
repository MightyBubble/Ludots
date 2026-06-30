using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Morph
{
    public sealed class MorphProfileRegistry
    {
        private readonly Dictionary<string, int> _nameToId = new(StringComparer.Ordinal);
        private readonly List<MorphProfileDescriptor> _profiles = new() { null! };

        public int Count => _profiles.Count - 1;

        public void Clear()
        {
            _nameToId.Clear();
            _profiles.Clear();
            _profiles.Add(null!);
        }

        public int Register(string id, MorphProfileDescriptor descriptor)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Morph profile id must not be null or whitespace.", nameof(id));
            }

            if (!string.Equals(id, id.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException("Morph profile id must not include leading or trailing whitespace.", nameof(id));
            }

            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            if (_nameToId.TryGetValue(id, out int existingId))
            {
                _profiles[existingId] = descriptor;
                return existingId;
            }

            int newId = _profiles.Count;
            _nameToId[id] = newId;
            _profiles.Add(descriptor);
            return newId;
        }

        public int GetId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !_nameToId.TryGetValue(id, out int resolvedId))
            {
                throw new InvalidOperationException($"Unknown morph profile '{id}'.");
            }

            return resolvedId;
        }

        public bool TryGetId(string id, out int profileId)
        {
            if (!string.IsNullOrWhiteSpace(id) && _nameToId.TryGetValue(id, out profileId))
            {
                return true;
            }

            profileId = 0;
            return false;
        }

        public MorphProfileDescriptor Get(int profileId)
        {
            if (!TryGet(profileId, out MorphProfileDescriptor descriptor))
            {
                throw new InvalidOperationException($"Unknown morph profile id '{profileId}'.");
            }

            return descriptor;
        }

        public bool TryGet(int profileId, out MorphProfileDescriptor descriptor)
        {
            if (profileId > 0 && profileId < _profiles.Count)
            {
                descriptor = _profiles[profileId];
                return descriptor != null;
            }

            descriptor = null!;
            return false;
        }
    }
}
