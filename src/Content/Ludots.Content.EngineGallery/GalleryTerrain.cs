using System.Numerics;
using Ludots.Platform.Abstractions;

namespace Ludots.Content.EngineGallery
{
    /// <summary>
    /// 程序化 VertexMap 风格 chunk 地形源：解析高度函数 + 分带顶点色，按需产出水下水面网格，
    /// 供水面反射与地表着色两个场景共用（画廊内零 Core 依赖的 ITerrainChunkMeshSource 直读实现）。
    /// </summary>
    public sealed class GalleryChunkTerrainSource : ITerrainChunkMeshSource
    {
        private readonly int _seed;
        private readonly float _waterLevelMeters;
        private readonly bool _emitWater;
        private readonly bool _islandMode;

        public GalleryChunkTerrainSource(
            int chunksPerSide,
            float chunkSpacingMeters,
            int quadsPerChunk,
            int seed,
            float waterLevelMeters,
            bool emitWater,
            bool islandMode)
        {
            WidthInChunks = chunksPerSide;
            HeightInChunks = chunksPerSide;
            ChunkSpacingXMeters = chunkSpacingMeters;
            ChunkSpacingYMeters = chunkSpacingMeters;
            _quadsPerChunk = quadsPerChunk;
            _seed = seed;
            _waterLevelMeters = waterLevelMeters;
            _emitWater = emitWater;
            _islandMode = islandMode;
        }

        private readonly int _quadsPerChunk;

        public int WidthInChunks { get; }
        public int HeightInChunks { get; }
        public float ChunkSpacingXMeters { get; }
        public float ChunkSpacingYMeters { get; }

        public long GetChunkKey(int chunkX, int chunkY)
        {
            return ((long)(uint)chunkX << 32) | (uint)chunkY;
        }

        public float SampleHeightMeters(float worldX, float worldZ)
        {
            float half = WidthInChunks * ChunkSpacingXMeters * 0.5f;
            float nx = worldX / half;
            float nz = worldZ / half;
            if (_islandMode)
            {
                float radial = MathF.Sqrt((nx * nx) + (nz * nz));
                float falloff = Math.Clamp(1f - radial, 0f, 1f);
                float ridge = MathF.Sin(nx * 3.1f) * MathF.Cos(nz * 2.7f) * 0.5f + 0.5f;
                float detail = GalleryTextureFactory.SmoothNoise(
                    (int)(worldX * 0.35f),
                    (int)(worldZ * 0.35f),
                    _seed);
                return (falloff * falloff * 26f * (0.55f + (ridge * 0.45f))) + (detail * 3.2f) - 6.5f;
            }

            float swell = MathF.Sin(worldX * 0.055f) * MathF.Cos(worldZ * 0.042f);
            float chop = GalleryTextureFactory.SmoothNoise(
                (int)(worldX * 0.6f),
                (int)(worldZ * 0.6f),
                _seed);
            return (swell * 3.4f) + (chop * 2.2f) - 4.2f;
        }

        public void BuildChunk(int chunkX, int chunkY, bool simplifiedCliffs, float heightScale, VertexMapChunkMeshData dst)
        {
            dst.Terrain.Clear();
            dst.Water.Clear();
            float step = ChunkSpacingXMeters / _quadsPerChunk;
            float originX = chunkX * ChunkSpacingXMeters;
            float originZ = chunkY * ChunkSpacingYMeters;

            for (int qz = 0; qz < _quadsPerChunk; qz++)
            {
                for (int qx = 0; qx < _quadsPerChunk; qx++)
                {
                    float x0 = originX + (qx * step);
                    float z0 = originZ + (qz * step);
                    float x1 = x0 + step;
                    float z1 = z0 + step;
                    AppendTerrainQuad(dst, x0, z0, x1, z1, heightScale, simplifiedCliffs);

                    if (_emitWater && Math.Min(
                            Math.Min(SampleHeightMeters(x0, z0), SampleHeightMeters(x1, z0)),
                            Math.Min(SampleHeightMeters(x0, z1), SampleHeightMeters(x1, z1))) < _waterLevelMeters)
                    {
                        AppendWaterQuad(dst, x0, z0, x1, z1);
                    }
                }
            }
        }

        private void AppendTerrainQuad(VertexMapChunkMeshData dst, float x0, float z0, float x1, float z1, float heightScale, bool simplifiedCliffs)
        {
            float cliffDrop = simplifiedCliffs ? 0.35f : 1f;
            Vector3 p00 = new(x0, SampleHeightMeters(x0, z0) * heightScale * cliffDrop, z0);
            Vector3 p10 = new(x1, SampleHeightMeters(x1, z0) * heightScale * cliffDrop, z0);
            Vector3 p11 = new(x1, SampleHeightMeters(x1, z1) * heightScale * cliffDrop, z1);
            Vector3 p01 = new(x0, SampleHeightMeters(x0, z1) * heightScale * cliffDrop, z1);

            Vector3 normal = Vector3.Normalize(Vector3.Cross(p10 - p00, p01 - p00));
            if (!float.IsFinite(normal.Y) || normal.Y < 0f)
            {
                normal = Vector3.UnitY;
            }

            Vector4 c00 = TerrainColor(p00.Y, normal.Y);
            Vector4 c10 = TerrainColor(p10.Y, normal.Y);
            Vector4 c11 = TerrainColor(p11.Y, normal.Y);
            Vector4 c01 = TerrainColor(p01.Y, normal.Y);

            dst.Terrain.EnsureAdditionalVertices(6);
            dst.Terrain.AppendVertex(p00, normal, c00);
            dst.Terrain.AppendVertex(p10, normal, c10);
            dst.Terrain.AppendVertex(p11, normal, c11);
            dst.Terrain.AppendVertex(p00, normal, c00);
            dst.Terrain.AppendVertex(p11, normal, c11);
            dst.Terrain.AppendVertex(p01, normal, c01);
        }

        private Vector4 TerrainColor(float heightMeters, float upDot)
        {
            float slope = Math.Clamp(1f - upDot, 0f, 1f);
            if (_islandMode)
            {
                Vector3 color = heightMeters switch
                {
                    < -2f => new Vector3(0.10f, 0.26f, 0.42f),
                    < 0.5f => new Vector3(0.78f, 0.70f, 0.48f),
                    < 8f => new Vector3(0.22f, 0.48f, 0.20f),
                    < 16f => new Vector3(0.42f, 0.36f, 0.24f),
                    _ => new Vector3(0.62f, 0.60f, 0.56f),
                };
                color *= 1f - (slope * 0.35f);
                return new Vector4(color, 1f);
            }

            Vector3 shelf = heightMeters switch
            {
                < -6f => new Vector3(0.05f, 0.14f, 0.30f),
                < -3.5f => new Vector3(0.09f, 0.30f, 0.46f),
                < -1f => new Vector3(0.20f, 0.55f, 0.62f),
                _ => new Vector3(0.55f, 0.50f, 0.38f),
            };
            shelf *= 1f - (slope * 0.25f);
            return new Vector4(shelf, 1f);
        }

        private void AppendWaterQuad(VertexMapChunkMeshData dst, float x0, float z0, float x1, float z1)
        {
            dst.Water.EnsureAdditionalVertices(6);
            Vector4 tint = new(0.24f, 0.71f, 0.88f, 0.62f);
            dst.Water.AppendVertex(new Vector3(x0, _waterLevelMeters, z0), Vector3.UnitY, tint);
            dst.Water.AppendVertex(new Vector3(x1, _waterLevelMeters, z0), Vector3.UnitY, tint);
            dst.Water.AppendVertex(new Vector3(x1, _waterLevelMeters, z1), Vector3.UnitY, tint);
            dst.Water.AppendVertex(new Vector3(x0, _waterLevelMeters, z0), Vector3.UnitY, tint);
            dst.Water.AppendVertex(new Vector3(x1, _waterLevelMeters, z1), Vector3.UnitY, tint);
            dst.Water.AppendVertex(new Vector3(x0, _waterLevelMeters, z1), Vector3.UnitY, tint);
        }
    }

    /// <summary>
    /// 程序化岛屿高度场：同时实现 IContinuousHeightmapRenderSource 与 IContinuousHeightmap，
    /// cm 精度的 short 采样、行主序布局，供视觉高度图与投影贴花两个场景共用。
    /// </summary>
    public sealed class GalleryIslandHeightmap : IContinuousHeightmapRenderSource, IContinuousHeightmap
    {
        private readonly int _chunksPerSide;
        private readonly int _samplesPerChunk;
        private readonly short[] _heightsCm;

        public GalleryIslandHeightmap(int chunksPerSide, int samplesPerChunk, int worldSizeMeters, int seed)
        {
            if (samplesPerChunk < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(samplesPerChunk));
            }

            _chunksPerSide = chunksPerSide;
            _samplesPerChunk = samplesPerChunk;
            int samplesPerSide = (chunksPerSide * (samplesPerChunk - 1)) + 1;
            _heightsCm = new short[samplesPerSide * samplesPerSide];
            float half = worldSizeMeters * 0.5f;

            for (int y = 0; y < samplesPerSide; y++)
            {
                for (int x = 0; x < samplesPerSide; x++)
                {
                    float worldX = ((x / (float)(samplesPerSide - 1)) * worldSizeMeters) - half;
                    float worldZ = ((y / (float)(samplesPerSide - 1)) * worldSizeMeters) - half;
                    _heightsCm[(y * samplesPerSide) + x] = (short)Math.Clamp(
                        MathF.Round(SampleIslandHeightCm(worldX, worldZ, seed)),
                        short.MinValue,
                        short.MaxValue);
                }
            }

            SamplesPerSide = samplesPerSide;
            StepCm = (worldSizeMeters * 100f) / (samplesPerSide - 1);
            Bounds = new WorldAabbCm(
                x: -(int)MathF.Round(half * 100f),
                y: -(int)MathF.Round(half * 100f),
                width: (int)MathF.Round(worldSizeMeters * 100f),
                height: (int)MathF.Round(worldSizeMeters * 100f));
            RenderProfile = new ContinuousHeightmapRenderProfile
            {
                WaterEnabled = true,
                SeaLevelCm = 0f,
                DisplayHeightScale = 1f,
                ColorContrast = 1.15f,
                AbsoluteColorPeakSpanCm = 2600f,
            };
        }

        public int SamplesPerSide { get; }
        public float StepCm { get; }

        public WorldAabbCm Bounds { get; }
        public int ChunkColumns => _chunksPerSide;
        public int ChunkRows => _chunksPerSide;
        public int SamplesPerChunkColumn => _samplesPerChunk;
        public int SamplesPerChunkRow => _samplesPerChunk;
        public int DefaultLayerIndex => 0;
        public int Revision => 1;
        public ContinuousHeightmapRenderProfile RenderProfile { get; }

        private static float SampleIslandHeightCm(float worldX, float worldZ, int seed)
        {
            float nx = worldX / 260f;
            float nz = worldZ / 260f;
            float radial = MathF.Sqrt((nx * nx) + (nz * nz));
            float falloff = Math.Clamp(1.15f - radial, 0f, 1f);
            float ridges = MathF.Sin(worldX * 0.021f) * MathF.Cos(worldZ * 0.018f) * 0.5f + 0.5f;
            float detail = GalleryTextureFactory.SmoothNoise(
                (int)(worldX * 0.8f),
                (int)(worldZ * 0.8f),
                seed);
            float heightMeters = (falloff * falloff * 34f * (0.5f + (ridges * 0.5f))) + (detail * 4f) - 7f;
            return heightMeters * 100f;
        }

        public bool TryGetChunk(int chunkX, int chunkY, out ContinuousHeightmapRenderChunk chunk)
        {
            if ((uint)chunkX >= (uint)_chunksPerSide || (uint)chunkY >= (uint)_chunksPerSide)
            {
                chunk = default;
                return false;
            }

            int interval = _samplesPerChunk - 1;
            int originX = chunkX * interval;
            int originY = chunkY * interval;
            var samples = new short[_samplesPerChunk * _samplesPerChunk];
            for (int y = 0; y < _samplesPerChunk; y++)
            {
                int sourceRow = originY + y;
                int sourceOffset = (sourceRow * SamplesPerSide) + originX;
                Array.Copy(_heightsCm, sourceOffset, samples, y * _samplesPerChunk, _samplesPerChunk);
            }

            chunk = new ContinuousHeightmapRenderChunk(
                chunkX,
                chunkY,
                new WorldAabbCm(
                    Bounds.Left + (int)MathF.Round(chunkX * interval * StepCm),
                    Bounds.Top + (int)MathF.Round(chunkY * interval * StepCm),
                    (int)MathF.Round(interval * StepCm),
                    (int)MathF.Round(interval * StepCm)),
                sampleColumns: _samplesPerChunk,
                sampleRows: _samplesPerChunk,
                sampleStepXCm: StepCm,
                sampleStepYCm: StepCm,
                heightSamplesCm: samples,
                heightSamplesRaw: Array.Empty<ushort>(),
                sampleScale: ContinuousHeightSampleScale.IdentityCentimeters,
                storageLayout: ContinuousHeightmapStorageLayout.RowMajorInt16Centimeters,
                sampleStride: _samplesPerChunk,
                layerSampleOffset: 0,
                revision: 1);
            return true;
        }

        public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = -1)
        {
            heightCm = 0f;
            float fx = (worldXCm - Bounds.Left) / StepCm;
            float fy = (worldYCm - Bounds.Top) / StepCm;
            int x = (int)MathF.Floor(fx);
            int y = (int)MathF.Floor(fy);
            if (x < 0 || y < 0 || x >= SamplesPerSide - 1 || y >= SamplesPerSide - 1)
            {
                return false;
            }

            float tx = fx - x;
            float ty = fy - y;
            float h00 = _heightsCm[(y * SamplesPerSide) + x];
            float h10 = _heightsCm[(y * SamplesPerSide) + x + 1];
            float h01 = _heightsCm[((y + 1) * SamplesPerSide) + x];
            float h11 = _heightsCm[((y + 1) * SamplesPerSide) + x + 1];
            heightCm = Lerp(Lerp(h00, h10, tx), Lerp(h01, h11, tx), ty);
            return true;
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + ((b - a) * t);
        }

        public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = -1)
        {
            if (worldXCm.Length != worldYCm.Length || outHeightCm.Length < worldXCm.Length)
            {
                return false;
            }

            for (int i = 0; i < worldXCm.Length; i++)
            {
                if (!TrySampleHeightCm(worldXCm[i], worldYCm[i], out outHeightCm[i], layerIndex))
                {
                    return false;
                }
            }

            return true;
        }

        public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = -1)
        {
            hit = default;
            return false;
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
            outHitMask.Clear();
            return false;
        }
    }
}
