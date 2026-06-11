using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class PresentationImageRegistry
    {
        private readonly StringIntRegistry _ids;
        private PresentationImageDefinition[] _definitions;
        private bool[] _hasDefinitions;

        public PresentationImageRegistry(int capacity = 128)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _definitions = new PresentationImageDefinition[capacity];
            _hasDefinitions = new bool[capacity];
        }

        public int Register(string key, PresentationImageDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            int id = _ids.Register(key);
            EnsureCapacity(id);
            definition.ImageAssetId = id;
            _definitions[id] = definition;
            _hasDefinitions[id] = true;
            return id;
        }

        public int GetId(string key) => _ids.GetId(key);

        public string GetName(int id) => _ids.GetName(id);

        public bool TryGet(int imageAssetId, out PresentationImageDefinition definition)
        {
            if ((uint)imageAssetId < (uint)_definitions.Length && _hasDefinitions[imageAssetId])
            {
                definition = _definitions[imageAssetId];
                return true;
            }

            definition = null!;
            return false;
        }

        public bool TryResolveLocator(int imageAssetId, string backendId, out PresentationImageLocatorDefinition locator)
        {
            if (TryGet(imageAssetId, out PresentationImageDefinition definition))
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
