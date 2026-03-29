using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class AnimationClipRegistry
    {
        private readonly StringIntRegistry _ids;
        private AnimationClipDefinition[] _definitions;
        private bool[] _hasDefinitions;

        public AnimationClipRegistry(int capacity = 256)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0, comparer: StringComparer.OrdinalIgnoreCase);
            _definitions = new AnimationClipDefinition[capacity];
            _hasDefinitions = new bool[capacity];
        }

        public int Register(string key, AnimationClipDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            int id = _ids.Register(key);
            EnsureCapacity(id);
            definition.ClipAssetId = id;
            _definitions[id] = definition;
            _hasDefinitions[id] = true;
            return id;
        }

        public int GetId(string key) => _ids.GetId(key);
        public string GetName(int id) => _ids.GetName(id);

        public bool TryGet(int clipAssetId, out AnimationClipDefinition definition)
        {
            if ((uint)clipAssetId < (uint)_definitions.Length && _hasDefinitions[clipAssetId])
            {
                definition = _definitions[clipAssetId];
                return true;
            }

            definition = null!;
            return false;
        }

        public bool TryResolveLocator(int clipAssetId, string backendId, out AnimationClipLocatorDefinition locator)
        {
            if (TryGet(clipAssetId, out var definition))
            {
                return definition.TryResolveLocator(backendId, out locator);
            }

            locator = default;
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
