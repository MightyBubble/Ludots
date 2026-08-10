using System;
using System.Collections.Generic;

namespace Ludots.Core.Fields.Influence
{
    /// <summary>
    /// Registry for named influence fields (threat, opportunity, ally-density, etc.).
    /// Multiple independent fields can coexist with different keys and grid specs.
    /// </summary>
    public sealed class InfluenceFieldRegistry
    {
        private readonly Dictionary<string, InfluenceField> _fields;

        public InfluenceFieldRegistry()
        {
            _fields = new Dictionary<string, InfluenceField>(StringComparer.Ordinal);
        }

        /// <summary>Register or retrieve a field. If it exists with different grid, throws.</summary>
        public InfluenceField GetOrCreate(string key, FieldGridSpec2D grid, float defaultValue = 0f)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Field key cannot be null or empty.", nameof(key));
            }

            if (_fields.TryGetValue(key, out var existing))
            {
                if (existing.Grid.CellSizeCm != grid.CellSizeCm || existing.Grid.ChunkSizeCells != grid.ChunkSizeCells)
                {
                    throw new InvalidOperationException(
                        $"Influence field '{key}' already exists with incompatible grid spec. " +
                        $"Existing: cell={existing.Grid.CellSizeCm}cm chunk={existing.Grid.ChunkSizeCells}, " +
                        $"Requested: cell={grid.CellSizeCm}cm chunk={grid.ChunkSizeCells}.");
                }
                return existing;
            }

            var field = new InfluenceField(key, grid, defaultValue);
            _fields.Add(key, field);
            return field;
        }

        /// <summary>Try to get existing field by key.</summary>
        public bool TryGet(string key, out InfluenceField field)
        {
            return _fields.TryGetValue(key, out field!);
        }

        /// <summary>Remove field by key.</summary>
        public bool Remove(string key)
        {
            return _fields.Remove(key);
        }

        /// <summary>Clear all fields.</summary>
        public void Clear()
        {
            _fields.Clear();
        }

        /// <summary>Decay all registered fields by the same factor.</summary>
        public void DecayAll(float factor)
        {
            foreach (var field in _fields.Values)
            {
                field.Decay(factor);
            }
        }

        public IReadOnlyDictionary<string, InfluenceField> Fields => _fields;
    }
}
