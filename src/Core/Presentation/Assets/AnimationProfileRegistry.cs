using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class AnimationProfileRegistry
    {
        private readonly StringIntRegistry _ids;
        private AnimationProfileDefinition[] _definitions;
        private bool[] _hasDefinitions;

        public AnimationProfileRegistry(int capacity = 256)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _definitions = new AnimationProfileDefinition[capacity];
            _hasDefinitions = new bool[capacity];
        }

        public int Register(string key, AnimationProfileDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            int id = _ids.Register(key);
            EnsureCapacity(id);
            definition.ProfileId = id;
            _definitions[id] = definition;
            _hasDefinitions[id] = true;
            return id;
        }

        public int GetId(string key) => _ids.GetId(key);
        public string GetName(int id) => _ids.GetName(id);

        public bool TryGet(int profileId, out AnimationProfileDefinition definition)
        {
            if ((uint)profileId < (uint)_definitions.Length && _hasDefinitions[profileId])
            {
                definition = _definitions[profileId];
                return true;
            }

            definition = null!;
            return false;
        }

        public bool TryResolveStateClipId(int profileId, int packedStateIndex, out int clipAssetId)
        {
            if (TryGet(profileId, out var definition))
            {
                return definition.TryResolveStateClipId(packedStateIndex, out clipAssetId);
            }

            clipAssetId = 0;
            return false;
        }

        private void EnsureCapacity(int id)
        {
            if (id < _definitions.Length)
            {
                return;
            }

            int newLength = Math.Max(_definitions.Length * 2, id + 1);
            Array.Resize(ref _definitions, newLength);
            Array.Resize(ref _hasDefinitions, newLength);
        }
    }
}
