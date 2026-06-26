using System;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Terrain
{
    /// <summary>
    /// Explicit flat-ground heightmap for maps that author a flat visual terrain surface.
    /// Consumers still read a single height SSOT instead of inventing independent ground projection rules.
    /// </summary>
    public sealed class FlatVisualHeightmap : IVisualHeightmap, IVisualHeightmapMipRenderSource
    {
        private readonly float _heightCm;
        private readonly WorldAabbCm _bounds;
        private readonly bool _hasBounds;
        private readonly short[] _renderSamplesCm;
        private readonly bool _hasRenderSamples;

        public FlatVisualHeightmap()
        {
            _heightCm = 0f;
            _bounds = default;
            _hasBounds = false;
            _renderSamplesCm = CreateRenderSamplesCm(_heightCm, out _hasRenderSamples);
        }

        public FlatVisualHeightmap(float heightCm)
        {
            if (!float.IsFinite(heightCm)) throw new ArgumentOutOfRangeException(nameof(heightCm));

            _heightCm = heightCm;
            _bounds = default;
            _hasBounds = false;
            _renderSamplesCm = CreateRenderSamplesCm(_heightCm, out _hasRenderSamples);
        }

        public FlatVisualHeightmap(WorldAabbCm bounds, float heightCm = 0f)
        {
            if (!float.IsFinite(heightCm)) throw new ArgumentOutOfRangeException(nameof(heightCm));

            _heightCm = heightCm;
            _bounds = bounds;
            _hasBounds = true;
            _renderSamplesCm = CreateRenderSamplesCm(_heightCm, out _hasRenderSamples);
        }

        public WorldAabbCm Bounds => _bounds;

        public int ChunkColumns => _hasBounds ? 1 : 0;

        public int ChunkRows => _hasBounds ? 1 : 0;

        public int SamplesPerChunkColumn => 2;

        public int SamplesPerChunkRow => 2;

        public int DefaultLayerIndex => 0;

        public int Revision => 0;

        public int MaxRenderMipLevel => 0;

        public bool TryGetChunk(int chunkX, int chunkY, out VisualHeightmapRenderChunk chunk)
        {
            return TryGetChunk(chunkX, chunkY, mipLevel: 0, out chunk);
        }

        public bool TryGetChunk(int chunkX, int chunkY, int mipLevel, out VisualHeightmapRenderChunk chunk)
        {
            if (!_hasBounds ||
                !_hasRenderSamples ||
                mipLevel != 0 ||
                chunkX != 0 ||
                chunkY != 0)
            {
                chunk = default;
                return false;
            }

            chunk = new VisualHeightmapRenderChunk(
                chunkX,
                chunkY,
                _bounds,
                SamplesPerChunkColumn,
                SamplesPerChunkRow,
                _bounds.Width,
                _bounds.Height,
                _renderSamplesCm,
                ReadOnlyMemory<ushort>.Empty,
                VisualHeightSampleScale.IdentityCentimeters,
                VisualHeightmapStorageLayout.RowMajorInt16Centimeters,
                SamplesPerChunkColumn,
                0,
                Revision);
            return true;
        }

        public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = -1)
        {
            heightCm = _heightCm;
            return ResolveLayerIndex(layerIndex) == 0 &&
                Contains(worldXCm, worldYCm);
        }

        public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = -1)
        {
            if (worldXCm.Length != worldYCm.Length || worldXCm.Length != outHeightCm.Length)
            {
                throw new ArgumentException("FlatVisualHeightmap batch spans must have identical lengths.");
            }

            if (ResolveLayerIndex(layerIndex) != 0)
            {
                return false;
            }

            for (int i = 0; i < outHeightCm.Length; i++)
            {
                outHeightCm[i] = Contains(worldXCm[i], worldYCm[i])
                    ? _heightCm
                    : float.NaN;
            }

            return true;
        }

        public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = -1)
        {
            hit = default;
            if (ResolveLayerIndex(layerIndex) != 0)
            {
                return false;
            }

            float dirY = ray.Direction.Y;
            if (!float.IsFinite(dirY) || MathF.Abs(dirY) < 1e-6f)
            {
                return false;
            }

            float originY = ray.Origin.Y;
            if (!float.IsFinite(originY))
            {
                return false;
            }

            float t = ((_heightCm * 0.01f) - originY) / dirY;
            if (!float.IsFinite(t) || t < 0f)
            {
                return false;
            }

            Vector3 point = ray.Origin + (ray.Direction * t);
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Z))
            {
                return false;
            }
            if (!Contains(point.X * 100f, point.Z * 100f))
            {
                return false;
            }

            hit = new VisualGroundHit(
                worldXCm: point.X * 100f,
                worldYCm: point.Z * 100f,
                heightCm: _heightCm,
                layerIndex: 0,
                distanceMeters: t,
                normal: Vector3.UnitY);
            return true;
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
            if (ResolveLayerIndex(layerIndex) != 0)
            {
                outHitMask.Clear();
                return false;
            }

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
                throw new ArgumentException("FlatVisualHeightmap raycast batch spans must have identical lengths.");
            }

            for (int i = 0; i < count; i++)
            {
                float dirY = directionY[i];
                if (!float.IsFinite(dirY) || MathF.Abs(dirY) < 1e-6f)
                {
                    outHitMask[i] = 0;
                    continue;
                }

                float originY = originYMeters[i];
                if (!float.IsFinite(originY))
                {
                    outHitMask[i] = 0;
                    continue;
                }

                float t = ((_heightCm * 0.01f) - originY) / dirY;
                if (!float.IsFinite(t) || t < 0f)
                {
                    outHitMask[i] = 0;
                    continue;
                }

                float x = originXMeters[i] + (directionX[i] * t);
                float z = originZMeters[i] + (directionZ[i] * t);
                if (!float.IsFinite(x) || !float.IsFinite(z))
                {
                    outHitMask[i] = 0;
                    continue;
                }
                if (!Contains(x * 100f, z * 100f))
                {
                    outHitMask[i] = 0;
                    continue;
                }

                outWorldXCm[i] = x * 100f;
                outWorldYCm[i] = z * 100f;
                outHeightCm[i] = _heightCm;
                outDistanceMeters[i] = t;
                outNormalX[i] = 0f;
                outNormalY[i] = 1f;
                outNormalZ[i] = 0f;
                outLayerIndex[i] = 0;
                outHitMask[i] = 1;
            }

            return true;
        }

        private static int ResolveLayerIndex(int layerIndex)
        {
            return layerIndex < 0 ? 0 : layerIndex;
        }

        private bool Contains(float worldXCm, float worldYCm)
        {
            if (!_hasBounds)
            {
                return true;
            }

            return float.IsFinite(worldXCm) &&
                float.IsFinite(worldYCm) &&
                worldXCm >= _bounds.Left &&
                worldXCm <= _bounds.Right &&
                worldYCm >= _bounds.Top &&
                worldYCm <= _bounds.Bottom;
        }

        private static short[] CreateRenderSamplesCm(float heightCm, out bool canRender)
        {
            float roundedHeightCm = MathF.Round(heightCm);
            canRender = MathF.Abs(heightCm - roundedHeightCm) <= 0.0001f &&
                roundedHeightCm >= short.MinValue &&
                roundedHeightCm <= short.MaxValue;
            if (!canRender)
            {
                return Array.Empty<short>();
            }

            short sample = (short)roundedHeightCm;
            return new[] { sample, sample, sample, sample };
        }
    }
}
