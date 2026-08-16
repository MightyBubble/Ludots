using System;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Assets
{
    public readonly struct VisualLodEntry
    {
        public VisualLodEntry(int meshAssetId, int materialOverrideId, float maxDistanceCm, float minScreenCoverage01)
        {
            if (meshAssetId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(meshAssetId));
            }

            if (maxDistanceCm <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDistanceCm));
            }

            if (minScreenCoverage01 < 0f || minScreenCoverage01 > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(minScreenCoverage01));
            }

            MeshAssetId = meshAssetId;
            MaterialOverrideId = materialOverrideId;
            MaxDistanceCm = maxDistanceCm;
            MinScreenCoverage01 = minScreenCoverage01;
        }

        public int MeshAssetId { get; }

        public int MaterialOverrideId { get; }

        public float MaxDistanceCm { get; }

        public float MinScreenCoverage01 { get; }
    }

    public readonly struct VisualLodProfile
    {
        public VisualLodProfile(in VisualLodEntry high, in VisualLodEntry medium, in VisualLodEntry low)
        {
            High = high;
            Medium = medium;
            Low = low;
        }

        public VisualLodEntry High { get; }

        public VisualLodEntry Medium { get; }

        public VisualLodEntry Low { get; }

        public readonly VisualLodEntry Resolve(LODLevel lod)
        {
            return lod switch
            {
                LODLevel.High => High,
                LODLevel.Medium => Medium,
                LODLevel.Low => Low,
                _ => Low,
            };
        }
    }
}
