using System;
using Ludots.Core.Presentation.Primitives;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class PrefabRegistry
    {
        private readonly PrefabDefinition[] _table;

        public PrefabRegistry(int capacity = 256)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _table = new PrefabDefinition[capacity];

            Register(new PrefabDefinition
            {
                PrefabId = PrimitivePrefabIds.CueMarker,
                MeshAssetId = PrimitiveMeshAssetIds.Cube,
                BaseScale = 0.2f,
            });
        }

        public void Register(in PrefabDefinition definition)
        {
            if ((uint)definition.PrefabId >= (uint)_table.Length) throw new ArgumentOutOfRangeException(nameof(definition.PrefabId));
            _table[definition.PrefabId] = definition;
        }

        public bool TryGet(int prefabId, out PrefabDefinition definition)
        {
            if ((uint)prefabId >= (uint)_table.Length)
            {
                definition = default;
                return false;
            }

            definition = _table[prefabId];
            return definition.PrefabId != 0;
        }
    }
}
