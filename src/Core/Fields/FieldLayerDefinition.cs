using System;

namespace Ludots.Core.Fields
{
    public readonly struct FieldLayerDefinition
    {
        public FieldLayerDefinition(
            FieldLayerId id,
            string key,
            FieldLayerKind kind,
            int cellSizeCm,
            int chunkSizeCells,
            FieldLayerDefaultValue defaultValue,
            bool persistent,
            string writerDomain,
            int maxRegionIds)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Field layer key is required.", nameof(key));
            }

            if (cellSizeCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSizeCm), "Field layer cell size must be positive.");
            }

            if (chunkSizeCells <= 0 || (chunkSizeCells & (chunkSizeCells - 1)) != 0)
            {
                throw new ArgumentException("Field layer chunk size must be a positive power of two.", nameof(chunkSizeCells));
            }

            if (string.IsNullOrWhiteSpace(writerDomain))
            {
                throw new ArgumentException("Field layer writer domain is required.", nameof(writerDomain));
            }

            if (maxRegionIds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRegionIds), "Field layer max region ids must be non-negative.");
            }

            Id = id;
            Key = key;
            Kind = kind;
            CellSizeCm = cellSizeCm;
            ChunkSizeCells = chunkSizeCells;
            DefaultValue = defaultValue;
            Persistent = persistent;
            WriterDomain = writerDomain;
            MaxRegionIds = maxRegionIds;
        }

        public readonly FieldLayerId Id;
        public readonly string Key;
        public readonly FieldLayerKind Kind;
        public readonly int CellSizeCm;
        public readonly int ChunkSizeCells;
        public readonly FieldLayerDefaultValue DefaultValue;
        public readonly bool Persistent;
        public readonly string WriterDomain;
        public readonly int MaxRegionIds;
    }
}
