using System;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Terrain
{
    /// <summary>
    /// IVisualHeightmap runtime over a sparse loaded chunk store.
    /// Missing chunks return false instead of inventing implicit global terrain.
    /// </summary>
    public sealed class ChunkedVisualHeightmapRuntime : IVisualHeightmap, IVisualHeightmapSampleAccessor
    {
        private readonly ChunkedVisualHeightmapDescriptor _descriptor;
        private readonly ChunkedVisualHeightmapStore _store;

        public ChunkedVisualHeightmapRuntime(ChunkedVisualHeightmapDescriptor descriptor, ChunkedVisualHeightmapStore store)
        {
            _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public ChunkedVisualHeightmapDescriptor Descriptor => _descriptor;

        public ChunkedVisualHeightmapStore Store => _store;

        public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = -1)
        {
            heightCm = default;
            WorldAabbCm bounds = _descriptor.Bounds;
            return TryResolveLayer(layerIndex, out VisualHeightmapLayerDefinition layer) &&
                   VisualHeightmapQueries.TrySampleHeightCm(
                       this,
                       in bounds,
                       _descriptor.GlobalSampleColumns,
                       _descriptor.GlobalSampleRows,
                       _descriptor.InterpolationMode,
                       layer.SampleOffset,
                       worldXCm,
                       worldYCm,
                       out heightCm);
        }

        public bool TrySampleSurface(float worldXCm, float worldYCm, out float heightCm, out Vector3 normal, int layerIndex = -1)
        {
            heightCm = default;
            normal = Vector3.UnitY;
            WorldAabbCm bounds = _descriptor.Bounds;
            return TryResolveLayer(layerIndex, out VisualHeightmapLayerDefinition layer) &&
                   VisualHeightmapQueries.TrySampleSurface(
                       this,
                       in bounds,
                       _descriptor.GlobalSampleColumns,
                       _descriptor.GlobalSampleRows,
                       _descriptor.InterpolationMode,
                       layer.SampleOffset,
                       worldXCm,
                       worldYCm,
                       out heightCm,
                       out normal);
        }

        public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = -1)
        {
            if (worldXCm.Length != worldYCm.Length || worldXCm.Length != outHeightCm.Length)
            {
                throw new ArgumentException("Chunked visual heightmap batch sample spans must have identical lengths.");
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
            WorldAabbCm bounds = _descriptor.Bounds;
            return TryResolveLayerIndex(layerIndex, out int resolvedLayer) &&
                   VisualHeightmapQueries.TryRaycastGround(
                       this,
                       in bounds,
                       _descriptor.GlobalSampleColumns,
                       _descriptor.GlobalSampleRows,
                       _descriptor.InterpolationMode,
                       resolvedLayer,
                       _descriptor.Layers[resolvedLayer].SampleOffset,
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
                throw new ArgumentException("Chunked visual heightmap batch raycast spans must have identical lengths.");
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

        bool IVisualHeightmapSampleAccessor.TryReadSampleCm(int layerSampleOffset, int globalSampleX, int globalSampleY, out float heightCm)
        {
            heightCm = default;
            if ((uint)globalSampleX >= (uint)_descriptor.GlobalSampleColumns ||
                (uint)globalSampleY >= (uint)_descriptor.GlobalSampleRows)
            {
                return false;
            }

            int chunkStepX = _descriptor.SamplesPerChunkColumn - 1;
            int chunkStepY = _descriptor.SamplesPerChunkRow - 1;
            int chunkX = ResolveChunkIndex(globalSampleX, _descriptor.GlobalSampleColumns, _descriptor.ChunkColumns, chunkStepX);
            int chunkY = ResolveChunkIndex(globalSampleY, _descriptor.GlobalSampleRows, _descriptor.ChunkRows, chunkStepY);
            int localX = ResolveLocalSampleIndex(globalSampleX, _descriptor.GlobalSampleColumns, chunkX, _descriptor.SamplesPerChunkColumn, chunkStepX);
            int localY = ResolveLocalSampleIndex(globalSampleY, _descriptor.GlobalSampleRows, chunkY, _descriptor.SamplesPerChunkRow, chunkStepY);

            if (!_store.TryGetChunk(chunkX, chunkY, out ChunkedVisualHeightmapChunk chunk))
            {
                return false;
            }

            int sampleIndex = layerSampleOffset + (localY * _descriptor.SamplesPerChunkColumn) + localX;
            switch (_descriptor.StorageLayout)
            {
                case VisualHeightmapStorageLayout.ChunkedRowMajorInt16Centimeters:
                case VisualHeightmapStorageLayout.RowMajorInt16Centimeters:
                    heightCm = chunk.HeightSamplesCm[sampleIndex];
                    return true;

                case VisualHeightmapStorageLayout.ChunkedRowMajorUInt16Scaled:
                case VisualHeightmapStorageLayout.RowMajorUInt16Scaled:
                    heightCm = _descriptor.SampleScale.Decode(chunk.HeightSamplesRaw[sampleIndex]);
                    return true;

                default:
                    return false;
            }
        }

        private bool TryResolveLayer(int layerIndex, out VisualHeightmapLayerDefinition layer)
        {
            if (!TryResolveLayerIndex(layerIndex, out int resolvedIndex))
            {
                layer = default;
                return false;
            }

            layer = _descriptor.Layers[resolvedIndex];
            return true;
        }

        private bool TryResolveLayerIndex(int requestedLayerIndex, out int resolvedLayerIndex)
        {
            if (requestedLayerIndex < 0)
            {
                resolvedLayerIndex = _descriptor.DefaultLayerIndex;
                return true;
            }

            if ((uint)requestedLayerIndex >= (uint)_descriptor.Layers.Length)
            {
                resolvedLayerIndex = default;
                return false;
            }

            resolvedLayerIndex = requestedLayerIndex;
            return true;
        }

        private static int ResolveChunkIndex(int globalSampleIndex, int globalSampleCount, int chunkCount, int chunkStep)
        {
            if (globalSampleIndex >= globalSampleCount - 1)
            {
                return chunkCount - 1;
            }

            return globalSampleIndex / chunkStep;
        }

        private static int ResolveLocalSampleIndex(int globalSampleIndex, int globalSampleCount, int chunkIndex, int samplesPerChunk, int chunkStep)
        {
            if (globalSampleIndex >= globalSampleCount - 1)
            {
                return samplesPerChunk - 1;
            }

            return globalSampleIndex - (chunkIndex * chunkStep);
        }
    }
}
