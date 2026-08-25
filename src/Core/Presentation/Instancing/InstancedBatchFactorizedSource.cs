using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Instancing
{
    /// <summary>
    /// Core-loaded factorized (SoA) transform data for an external instanced batch source.
    /// Produced once at config load time by InstancedBatchFactorizedSourceLoader and consumed
    /// by the typed InstancedBatch request path; never re-derived or re-read per frame.
    /// </summary>
    public sealed class InstancedBatchFactorizedSource
    {
        public InstancedBatchFactorizedSource(
            string format,
            string assetUri,
            string setId,
            int instanceCount,
            bool groundToVisualHeightmap,
            Vector3[] positionCm,
            Quaternion[] rotation,
            Vector3[] scale)
        {
            if (instanceCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(instanceCount));
            }

            if (positionCm == null || positionCm.Length != instanceCount)
            {
                throw new ArgumentException($"positionCm must contain exactly {instanceCount} entries.", nameof(positionCm));
            }

            if (rotation == null || rotation.Length != instanceCount)
            {
                throw new ArgumentException($"rotation must contain exactly {instanceCount} entries.", nameof(rotation));
            }

            if (scale == null || scale.Length != instanceCount)
            {
                throw new ArgumentException($"scale must contain exactly {instanceCount} entries.", nameof(scale));
            }

            Format = format ?? throw new ArgumentNullException(nameof(format));
            AssetUri = assetUri ?? throw new ArgumentNullException(nameof(assetUri));
            SetId = setId ?? throw new ArgumentNullException(nameof(setId));
            InstanceCount = instanceCount;
            GroundToVisualHeightmap = groundToVisualHeightmap;
            PositionCm = positionCm;
            Rotation = rotation;
            Scale = scale;
        }

        public string Format { get; }
        public string AssetUri { get; }
        public string SetId { get; }
        public int InstanceCount { get; }
        public bool GroundToVisualHeightmap { get; }
        public Vector3[] PositionCm { get; }
        public Quaternion[] Rotation { get; }
        public Vector3[] Scale { get; }
    }
}
