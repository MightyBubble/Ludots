using System;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Terrain
{
    public sealed class VisualHeightmapRuntime : IVisualHeightmap, IVisualHeightmapRenderSource, IVisualHeightmapSampleAccessor
    {
        private const int PreferredRenderChunkSampleSpan = 33;
        private readonly VisualHeightmapAsset _asset;
        private readonly VisualHeightmapRenderProfile _renderProfile;
        private readonly int _renderChunkColumns;
        private readonly int _renderChunkRows;
        private readonly int _renderChunkStepColumns;
        private readonly int _renderChunkStepRows;

        public VisualHeightmapRuntime(VisualHeightmapAsset asset)
            : this(asset, VisualHeightmapRenderProfile.CreateDefault())
        {
        }

        public VisualHeightmapRuntime(VisualHeightmapAsset asset, VisualHeightmapRenderProfile renderProfile)
        {
            _asset = asset ?? throw new ArgumentNullException(nameof(asset));
            _renderProfile = (renderProfile ?? throw new ArgumentNullException(nameof(renderProfile))).NormalizeAndValidate();
            _renderChunkColumns = ResolveRenderChunkCount(_asset.SampleColumns);
            _renderChunkRows = ResolveRenderChunkCount(_asset.SampleRows);
            _renderChunkStepColumns = ResolveRenderChunkStep(_asset.SampleColumns, _renderChunkColumns);
            _renderChunkStepRows = ResolveRenderChunkStep(_asset.SampleRows, _renderChunkRows);
        }

        public VisualHeightmapAsset Asset => _asset;

        public WorldAabbCm Bounds => _asset.Bounds;

        public int ChunkColumns => _renderChunkColumns;

        public int ChunkRows => _renderChunkRows;

        public int SamplesPerChunkColumn => _renderChunkStepColumns + 1;

        public int SamplesPerChunkRow => _renderChunkStepRows + 1;

        public int DefaultLayerIndex => _asset.DefaultLayerIndex;

        public int Revision => 0;

        public VisualHeightmapRenderProfile RenderProfile => _renderProfile;

        public bool TryGetChunk(int chunkX, int chunkY, out VisualHeightmapRenderChunk chunk)
        {
            if ((uint)chunkX >= (uint)_renderChunkColumns ||
                (uint)chunkY >= (uint)_renderChunkRows ||
                !TryResolveLayerIndex(_asset.DefaultLayerIndex, out int resolvedLayer))
            {
                chunk = default;
                return false;
            }

            VisualHeightmapLayerDefinition layer = _asset.Layers[resolvedLayer];
            int sampleX = chunkX * _renderChunkStepColumns;
            int sampleY = chunkY * _renderChunkStepRows;
            int sampleEndX = Math.Min(_asset.SampleColumns - 1, sampleX + _renderChunkStepColumns);
            int sampleEndY = Math.Min(_asset.SampleRows - 1, sampleY + _renderChunkStepRows);
            int sampleColumns = sampleEndX - sampleX + 1;
            int sampleRows = sampleEndY - sampleY + 1;
            float sampleStepXCm = _asset.Bounds.Width / (float)(_asset.SampleColumns - 1);
            float sampleStepYCm = _asset.Bounds.Height / (float)(_asset.SampleRows - 1);
            int boundsLeft = RoundSampleWorldCm(_asset.Bounds.Left, sampleX, sampleStepXCm);
            int boundsTop = RoundSampleWorldCm(_asset.Bounds.Top, sampleY, sampleStepYCm);
            int boundsRight = RoundSampleWorldCm(_asset.Bounds.Left, sampleEndX, sampleStepXCm);
            int boundsBottom = RoundSampleWorldCm(_asset.Bounds.Top, sampleEndY, sampleStepYCm);
            chunk = new VisualHeightmapRenderChunk(
                chunkX,
                chunkY,
                new WorldAabbCm(
                    boundsLeft,
                    boundsTop,
                    boundsRight - boundsLeft,
                    boundsBottom - boundsTop),
                sampleColumns,
                sampleRows,
                sampleStepXCm,
                sampleStepYCm,
                _asset.HeightSamplesCm,
                _asset.HeightSamplesRaw,
                _asset.SampleScale,
                _asset.StorageLayout,
                _asset.SampleColumns,
                layer.SampleOffset + (sampleY * _asset.SampleColumns) + sampleX,
                Revision);
            return true;
        }

        public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = 0)
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

        public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = 0)
        {
            if (worldXCm.Length != worldYCm.Length || worldXCm.Length != outHeightCm.Length)
            {
                throw new ArgumentException("Visual heightmap batch sample spans must have identical lengths.");
            }

            if (!TryResolveLayer(layerIndex, out VisualHeightmapLayerDefinition layer))
            {
                return false;
            }

            WorldAabbCm bounds = _asset.Bounds;
            VisualHeightmapQueries.SampleHeightsCm(
                this,
                in bounds,
                _asset.SampleColumns,
                _asset.SampleRows,
                _asset.InterpolationMode,
                layer.SampleOffset,
                worldXCm,
                worldYCm,
                outHeightCm);

            return true;
        }

        public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = 0)
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
            resolvedLayer = layerIndex >= 0 ? layerIndex : _asset.DefaultLayerIndex;
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

        private static int ResolveRenderChunkCount(int sampleCount)
        {
            if (sampleCount <= PreferredRenderChunkSampleSpan)
            {
                return 1;
            }

            int sampleSteps = sampleCount - 1;
            int chunkSteps = PreferredRenderChunkSampleSpan - 1;
            return Math.Max(1, (int)Math.Ceiling(sampleSteps / (double)chunkSteps));
        }

        private static int ResolveRenderChunkStep(int sampleCount, int chunkCount)
        {
            int sampleSteps = sampleCount - 1;
            return Math.Max(1, (int)Math.Ceiling(sampleSteps / (double)chunkCount));
        }

        private static int RoundSampleWorldCm(int originCm, int sampleIndex, float sampleStepCm)
        {
            return originCm + (int)MathF.Round(sampleIndex * sampleStepCm);
        }
    }
}
