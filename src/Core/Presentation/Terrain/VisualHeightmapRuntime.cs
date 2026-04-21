using System;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Terrain
{
    public sealed class VisualHeightmapRuntime : IVisualHeightmap, IVisualHeightmapSampleAccessor
    {
        private readonly VisualHeightmapAsset _asset;
        private readonly int _defaultLayerIndex;

        public VisualHeightmapRuntime(VisualHeightmapAsset asset, int defaultLayerIndex = -1)
        {
            _asset = asset ?? throw new ArgumentNullException(nameof(asset));
            _defaultLayerIndex = defaultLayerIndex >= 0 ? defaultLayerIndex : asset.DefaultLayerIndex;
            if ((uint)_defaultLayerIndex >= (uint)asset.Layers.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(defaultLayerIndex), "Visual heightmap default layer index is outside the asset layer range.");
            }
        }

        public VisualHeightmapAsset Asset => _asset;

        public int DefaultLayerIndex => _defaultLayerIndex;

        public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = -1)
        {
            heightCm = default;
            WorldAabbCm bounds = _asset.Bounds;
            return TryResolveLayer(layerIndex, out VisualHeightmapLayerDefinition layer) &&
                   VisualHeightmapQueries.TrySampleHeightCm(
                       this,
                       in bounds,
                       _asset.SampleColumns,
                       _asset.SampleRows,
                       _asset.InterpolationMode,
                       layer.SampleOffset,
                       worldXCm,
                       worldYCm,
                       out heightCm);
        }

        public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = -1)
        {
            if (worldXCm.Length != worldYCm.Length || worldXCm.Length != outHeightCm.Length)
            {
                throw new ArgumentException("Visual heightmap batch sample spans must have identical lengths.");
            }

            if (!TryResolveLayer(layerIndex, out _))
            {
                return false;
            }

            for (int i = 0; i < outHeightCm.Length; i++)
            {
                outHeightCm[i] = TrySampleHeightCm(worldXCm[i], worldYCm[i], out float heightCm, layerIndex)
                    ? heightCm
                    : float.NaN;
            }

            return true;
        }

        public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = -1)
        {
            hit = default;
            WorldAabbCm bounds = _asset.Bounds;
            return TryResolveLayerIndex(layerIndex, out int resolvedLayer) &&
                   VisualHeightmapQueries.TryRaycastGround(
                       this,
                       in bounds,
                       _asset.SampleColumns,
                       _asset.SampleRows,
                       _asset.InterpolationMode,
                       resolvedLayer,
                       _asset.Layers[resolvedLayer].SampleOffset,
                       in ray,
                       out hit);
        }

        public bool RaycastGroundBatch(
            ReadOnlySpan<float> originXMeters,
            ReadOnlySpan<float> originYMeters,
            ReadOnlySpan<float> originZMeters,
            ReadOnlySpan<float> directionX,
            ReadOnlySpan<float> directionY,
            ReadOnlySpan<float> directionZ,
            Span<float> outWorldXCm,
            Span<float> outWorldYCm,
            Span<float> outHeightCm,
            Span<float> outDistanceMeters,
            Span<float> outNormalX,
            Span<float> outNormalY,
            Span<float> outNormalZ,
            Span<int> outLayerIndex,
            Span<byte> outHitMask,
            int layerIndex = -1)
        {
            int count = originXMeters.Length;
            if (originYMeters.Length != count ||
                originZMeters.Length != count ||
                directionX.Length != count ||
                directionY.Length != count ||
                directionZ.Length != count ||
                outWorldXCm.Length != count ||
                outWorldYCm.Length != count ||
                outHeightCm.Length != count ||
                outDistanceMeters.Length != count ||
                outNormalX.Length != count ||
                outNormalY.Length != count ||
                outNormalZ.Length != count ||
                outLayerIndex.Length != count ||
                outHitMask.Length != count)
            {
                throw new ArgumentException("Visual heightmap batch raycast spans must have identical lengths.");
            }

            if (!TryResolveLayer(layerIndex, out _))
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                ScreenRay ray = new ScreenRay(
                    new System.Numerics.Vector3(originXMeters[i], originYMeters[i], originZMeters[i]),
                    new System.Numerics.Vector3(directionX[i], directionY[i], directionZ[i]));

                if (TryRaycastGround(in ray, out VisualGroundHit hit, layerIndex))
                {
                    outWorldXCm[i] = hit.WorldXCm;
                    outWorldYCm[i] = hit.WorldYCm;
                    outHeightCm[i] = hit.HeightCm;
                    outDistanceMeters[i] = hit.DistanceMeters;
                    outNormalX[i] = hit.Normal.X;
                    outNormalY[i] = hit.Normal.Y;
                    outNormalZ[i] = hit.Normal.Z;
                    outLayerIndex[i] = hit.LayerIndex;
                    outHitMask[i] = 1;
                }
                else
                {
                    outWorldXCm[i] = float.NaN;
                    outWorldYCm[i] = float.NaN;
                    outHeightCm[i] = float.NaN;
                    outDistanceMeters[i] = float.NaN;
                    outNormalX[i] = 0f;
                    outNormalY[i] = 0f;
                    outNormalZ[i] = 0f;
                    outLayerIndex[i] = -1;
                    outHitMask[i] = 0;
                }
            }

            return true;
        }

        bool IVisualHeightmapSampleAccessor.TryReadSampleCm(int layerSampleOffset, int sampleX, int sampleY, out float heightCm)
        {
            heightCm = default;
            int index = layerSampleOffset + (sampleY * _asset.SampleColumns) + sampleX;
            switch (_asset.StorageLayout)
            {
                case VisualHeightmapStorageLayout.RowMajorInt16Centimeters:
                case VisualHeightmapStorageLayout.ChunkedRowMajorInt16Centimeters:
                    heightCm = _asset.HeightSamplesCm[index];
                    return true;

                case VisualHeightmapStorageLayout.RowMajorUInt16Scaled:
                case VisualHeightmapStorageLayout.ChunkedRowMajorUInt16Scaled:
                    heightCm = _asset.SampleScale.Decode(_asset.HeightSamplesRaw[index]);
                    return true;

                default:
                    return false;
            }
        }

        private bool TryResolveLayerIndex(int layerIndex, out int resolvedLayer)
        {
            resolvedLayer = layerIndex >= 0 ? layerIndex : _defaultLayerIndex;
            return (uint)resolvedLayer < (uint)_asset.Layers.Length;
        }

        private bool TryResolveLayer(int layerIndex, out VisualHeightmapLayerDefinition layer)
        {
            if (!TryResolveLayerIndex(layerIndex, out int resolvedLayer))
            {
                layer = default;
                return false;
            }

            layer = _asset.Layers[resolvedLayer];
            return true;
        }
    }
}
