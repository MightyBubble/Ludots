using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Instancing
{
    /// <summary>
    /// Core-loaded factorized transform data for an external instanced batch source: per-instance
    /// struct arrays (<c>Vector3[] positionCm</c>, <c>Quaternion[] rotation</c>, <c>Vector3[] scale</c>)
    /// owned by Core. Produced once at config load time by InstancedBatchFactorizedSourceLoader and
    /// consumed by the typed InstancedBatch request path; never re-derived or re-read per frame.
    /// </summary>
    public sealed class InstancedBatchFactorizedSource
    {
        public InstancedBatchFactorizedSource(
            string format,
            string assetUri,
            string setId,
            int instanceCount,
            bool groundToContinuousHeightmap,
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
            GroundToContinuousHeightmap = groundToContinuousHeightmap;
            PositionCm = positionCm;
            Rotation = rotation;
            Scale = scale;
        }

        public string Format { get; }
        public string AssetUri { get; }
        public string SetId { get; }
        public int InstanceCount { get; }
        public bool GroundToContinuousHeightmap { get; }
        public Vector3[] PositionCm { get; }
        public Quaternion[] Rotation { get; }
        public Vector3[] Scale { get; }
    }
}
