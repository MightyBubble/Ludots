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
    public sealed class ChunkedVisualHeightmapRuntime : IVisualHeightmap, IVisualHeightmapMipRenderSource, IVisualHeightmapSampleAccessor
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

        public WorldAabbCm Bounds => _descriptor.Bounds;

        public int ChunkColumns => _descriptor.ChunkColumns;

        public int ChunkRows => _descriptor.ChunkRows;

        public int SamplesPerChunkColumn => _descriptor.SamplesPerChunkColumn;

        public int SamplesPerChunkRow => _descriptor.SamplesPerChunkRow;

        public int DefaultLayerIndex => _descriptor.DefaultLayerIndex;

        public int Revision => _store.Revision;

        public int MaxRenderMipLevel => _store.MaxMipLevel;

        public bool TryGetChunk(int chunkX, int chunkY, out VisualHeightmapRenderChunk chunk)
        {
            return TryGetChunk(chunkX, chunkY, mipLevel: 0, out chunk);
        }

        public bool TryGetChunk(int chunkX, int chunkY, int mipLevel, out VisualHeightmapRenderChunk chunk)
        {
            chunk = default;
            if (mipLevel < 0 ||
                !_store.TryGetChunk(chunkX, chunkY, out ChunkedVisualHeightmapChunk source) ||
                !TryResolveLayerIndex(_descriptor.DefaultLayerIndex, out int layerIndex))
            {
                return false;
            }

            if (mipLevel > 0)
            {
                return TryGetMipChunk(source, mipLevel, layerIndex, out chunk);
            }

            VisualHeightmapLayerDefinition layer = _descriptor.Layers[layerIndex];
            WorldAabbCm bounds = new WorldAabbCm(
                _descriptor.Bounds.Left + (chunkX * _descriptor.ChunkWorldWidthCm),
                _descriptor.Bounds.Top + (chunkY * _descriptor.ChunkWorldHeightCm),
                _descriptor.ChunkWorldWidthCm,
                _descriptor.ChunkWorldHeightCm);
            chunk = new VisualHeightmapRenderChunk(
                chunkX,
                chunkY,
                bounds,
                _descriptor.SamplesPerChunkColumn,
                _descriptor.SamplesPerChunkRow,
                _descriptor.ChunkWorldWidthCm / (float)(_descriptor.SamplesPerChunkColumn - 1),
                _descriptor.ChunkWorldHeightCm / (float)(_descriptor.SamplesPerChunkRow - 1),
                source.HeightSamplesCm,
                source.HeightSamplesRaw,
                _descriptor.SampleScale,
                _descriptor.StorageLayout,
                _descriptor.SamplesPerChunkColumn,
                layer.SampleOffset,
                HashCode.Combine(_store.Revision, source.Generation));
            return true;
        }

        public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = 0)
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

        private bool TryGetMipChunk(
            ChunkedVisualHeightmapChunk source,
            int mipLevel,
            int layerIndex,
            out VisualHeightmapRenderChunk chunk)
        {
            if (!source.TryGetMipLevel(mipLevel, out ChunkedVisualHeightmapChunkMipLevel mip))
            {
                chunk = default;
                return false;
            }

            WorldAabbCm bounds = new WorldAabbCm(
                _descriptor.Bounds.Left + (source.ChunkX * _descriptor.ChunkWorldWidthCm),
                _descriptor.Bounds.Top + (source.ChunkY * _descriptor.ChunkWorldHeightCm),
                _descriptor.ChunkWorldWidthCm,
                _descriptor.ChunkWorldHeightCm);
            int layerSampleOffset = checked(layerIndex * mip.SamplesPerLayerPerChunk);
            chunk = new VisualHeightmapRenderChunk(
                source.ChunkX,
                source.ChunkY,
                bounds,
                mip.SamplesPerChunkColumn,
                mip.SamplesPerChunkRow,
                _descriptor.ChunkWorldWidthCm / (float)(mip.SamplesPerChunkColumn - 1),
                _descriptor.ChunkWorldHeightCm / (float)(mip.SamplesPerChunkRow - 1),
                mip.HeightSamplesCm,
                mip.HeightSamplesRaw,
                _descriptor.SampleScale,
                _descriptor.StorageLayout,
                mip.SamplesPerChunkColumn,
                layerSampleOffset,
                HashCode.Combine(_store.Revision, source.Generation, mipLevel));
            return true;
        }

        public bool TrySampleSurface(float worldXCm, float worldYCm, out float heightCm, out Vector3 normal, int layerIndex = 0)
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

        public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = 0)
        {
            if (worldXCm.Length != worldYCm.Length || worldXCm.Length != outHeightCm.Length)
            {
                throw new ArgumentException("Chunked visual heightmap batch sample spans must have identical lengths.");
            }

            if (!TryResolveLayer(layerIndex, out VisualHeightmapLayerDefinition layer))
            {
                return false;
            }

            SampleHeightsCmDirect(in layer, worldXCm, worldYCm, outHeightCm);
            return true;
        }

        public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = 0)
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
            int layerIndex = 0)
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

        private void SampleHeightsCmDirect(
            in VisualHeightmapLayerDefinition layer,
            ReadOnlySpan<float> worldXCm,
            ReadOnlySpan<float> worldYCm,
            Span<float> outHeightCm)
        {
            WorldAabbCm bounds = _descriptor.Bounds;
            int sampleColumns = _descriptor.GlobalSampleColumns;
            int sampleRows = _descriptor.GlobalSampleRows;
            float invWidth = 1f / Math.Max(1f, bounds.Width);
            float invHeight = 1f / Math.Max(1f, bounds.Height);
            float sampleScaleX = sampleColumns > 1 ? sampleColumns - 1 : 0f;
            float sampleScaleY = sampleRows > 1 ? sampleRows - 1 : 0f;
            int maxCellX = Math.Max(0, sampleColumns - 2);
            int maxCellY = Math.Max(0, sampleRows - 2);
            bool triangle = _descriptor.InterpolationMode == VisualHeightmapInterpolationMode.TriangleHeightfield;

            for (int i = 0; i < outHeightCm.Length; i++)
            {
                float worldX = worldXCm[i];
                float worldY = worldYCm[i];
                if (!float.IsFinite(worldX) ||
                    !float.IsFinite(worldY) ||
                    worldX < bounds.Left ||
                    worldX > bounds.Right ||
                    worldY < bounds.Top ||
                    worldY > bounds.Bottom)
                {
                    outHeightCm[i] = float.NaN;
                    continue;
                }

                float sampleX = sampleColumns > 1
                    ? sampleScaleX * Math.Clamp((worldX - bounds.Left) * invWidth, 0f, 1f)
                    : 0f;
                float sampleY = sampleRows > 1
                    ? sampleScaleY * Math.Clamp((worldY - bounds.Top) * invHeight, 0f, 1f)
                    : 0f;
                int x0 = sampleColumns > 1 ? Math.Clamp((int)sampleX, 0, maxCellX) : 0;
                int y0 = sampleRows > 1 ? Math.Clamp((int)sampleY, 0, maxCellY) : 0;
                int x1 = sampleColumns > 1 ? x0 + 1 : 0;
                int y1 = sampleRows > 1 ? y0 + 1 : 0;
                float tx = sampleColumns > 1 ? sampleX - x0 : 0f;
                float ty = sampleRows > 1 ? sampleY - y0 : 0f;

                if (!TryReadCellSamplesCmDirect(
                        layer.SampleOffset,
                        x0,
                        x1,
                        y0,
                        y1,
                        out float h00,
                        out float h10,
                        out float h01,
                        out float h11))
                {
                    outHeightCm[i] = float.NaN;
                    continue;
                }

                float heightCm = EvaluateHeight(triangle, x0 == x1 || y0 == y1, h00, h10, h01, h11, tx, ty);
                outHeightCm[i] = float.IsFinite(heightCm) ? heightCm : float.NaN;
            }
        }

        private bool TryReadCellSamplesCmDirect(
            int layerSampleOffset,
            int globalX0,
            int globalX1,
            int globalY0,
            int globalY1,
            out float h00,
            out float h10,
            out float h01,
            out float h11)
        {
            h00 = h10 = h01 = h11 = default;
            if ((uint)globalX0 >= (uint)_descriptor.GlobalSampleColumns ||
                (uint)globalX1 >= (uint)_descriptor.GlobalSampleColumns ||
                (uint)globalY0 >= (uint)_descriptor.GlobalSampleRows ||
                (uint)globalY1 >= (uint)_descriptor.GlobalSampleRows)
            {
                return false;
            }

            int chunkStepX = _descriptor.SamplesPerChunkColumn - 1;
            int chunkStepY = _descriptor.SamplesPerChunkRow - 1;
            int chunkX = ResolveChunkIndex(globalX0, _descriptor.GlobalSampleColumns, _descriptor.ChunkColumns, chunkStepX);
            int chunkY = ResolveChunkIndex(globalY0, _descriptor.GlobalSampleRows, _descriptor.ChunkRows, chunkStepY);
            int localX0 = ResolveLocalSampleIndex(globalX0, _descriptor.GlobalSampleColumns, chunkX, _descriptor.SamplesPerChunkColumn, chunkStepX);
            int localY0 = ResolveLocalSampleIndex(globalY0, _descriptor.GlobalSampleRows, chunkY, _descriptor.SamplesPerChunkRow, chunkStepY);
            int localX1 = ResolveLocalSampleIndex(globalX1, _descriptor.GlobalSampleColumns, chunkX, _descriptor.SamplesPerChunkColumn, chunkStepX);
            int localY1 = ResolveLocalSampleIndex(globalY1, _descriptor.GlobalSampleRows, chunkY, _descriptor.SamplesPerChunkRow, chunkStepY);

            if (!_store.TryGetChunk(chunkX, chunkY, out ChunkedVisualHeightmapChunk chunk))
            {
                return false;
            }

            int sampleStride = _descriptor.SamplesPerChunkColumn;
            int sample00 = layerSampleOffset + (localY0 * sampleStride) + localX0;
            int sample10 = layerSampleOffset + (localY0 * sampleStride) + localX1;
            int sample01 = layerSampleOffset + (localY1 * sampleStride) + localX0;
            int sample11 = layerSampleOffset + (localY1 * sampleStride) + localX1;
            switch (_descriptor.StorageLayout)
            {
                case VisualHeightmapStorageLayout.ChunkedRowMajorInt16Centimeters:
                case VisualHeightmapStorageLayout.RowMajorInt16Centimeters:
                    short[] cm = chunk.HeightSamplesCm;
                    if ((uint)sample00 >= (uint)cm.Length ||
                        (uint)sample10 >= (uint)cm.Length ||
                        (uint)sample01 >= (uint)cm.Length ||
                        (uint)sample11 >= (uint)cm.Length)
                    {
                        return false;
                    }

                    h00 = cm[sample00];
                    h10 = cm[sample10];
                    h01 = cm[sample01];
                    h11 = cm[sample11];
                    return true;

                case VisualHeightmapStorageLayout.ChunkedRowMajorUInt16Scaled:
                case VisualHeightmapStorageLayout.RowMajorUInt16Scaled:
                    ushort[] raw = chunk.HeightSamplesRaw;
                    if ((uint)sample00 >= (uint)raw.Length ||
                        (uint)sample10 >= (uint)raw.Length ||
                        (uint)sample01 >= (uint)raw.Length ||
                        (uint)sample11 >= (uint)raw.Length)
                    {
                        return false;
                    }

                    VisualHeightSampleScale sampleScale = _descriptor.SampleScale;
                    h00 = sampleScale.Decode(raw[sample00]);
                    h10 = sampleScale.Decode(raw[sample10]);
                    h01 = sampleScale.Decode(raw[sample01]);
                    h11 = sampleScale.Decode(raw[sample11]);
                    return true;

                default:
                    return false;
            }
        }

        private bool TryReadSampleCmDirect(int layerSampleOffset, int globalSampleX, int globalSampleY, out float heightCm)
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

            return TryReadLocalSampleCm(layerSampleOffset, chunkX, chunkY, localX, localY, out heightCm);
        }

        private bool TryReadLocalSampleCm(
            int layerSampleOffset,
            int chunkX,
            int chunkY,
            int localX,
            int localY,
            out float heightCm)
        {
            heightCm = default;
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

        private static float EvaluateHeight(
            bool triangle,
            bool degenerateCell,
            float h00,
            float h10,
            float h01,
            float h11,
            float tx,
            float ty)
        {
            if (triangle && !degenerateCell)
            {
                if (tx + ty <= 1f)
                {
                    return h00 + ((h10 - h00) * tx) + ((h01 - h00) * ty);
                }

                return h11 + ((h01 - h11) * (1f - tx)) + ((h10 - h11) * (1f - ty));
            }

            float hx0 = h00 + ((h10 - h00) * tx);
            float hx1 = h01 + ((h11 - h01) * tx);
            return hx0 + ((hx1 - hx0) * ty);
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
