using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class AnimatorControllerRegistry
    {
        private readonly StringIntRegistry _ids;
        private AnimatorControllerDefinition[] _definitions;
        private bool[] _hasDefinitions;

        public AnimatorControllerRegistry(int capacity = 256)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _ids = new StringIntRegistry(capacity, startId: 1, invalidId: 0, comparer: StringComparer.OrdinalIgnoreCase);
            _definitions = new AnimatorControllerDefinition[capacity];
            _hasDefinitions = new bool[capacity];
        }

        public int Register(string key) => _ids.Register(key);

        public int Register(string key, AnimatorControllerDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            int id = _ids.Register(key);
            EnsureCapacity(id);
            definition.ControllerId = id;
            _definitions[id] = definition;
            _hasDefinitions[id] = true;
            return id;
        }

        public int GetId(string key) => _ids.GetId(key);
        public string GetName(int id) => _ids.GetName(id);

        public bool TryGet(int id, out AnimatorControllerDefinition definition)
        {
            if ((uint)id < (uint)_definitions.Length && _hasDefinitions[id])
            {
                definition = _definitions[id];
                return true;
            }

            definition = null!;
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
