using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Client.Raylib.Rendering
{
    public sealed unsafe class RaylibVisualHeightmapRenderer : IDisposable
    {
        private readonly Dictionary<long, ChunkGpu> _chunks = new(1024);
        private readonly List<long> _evictKeys = new(256);
        private readonly ChunkMeshWriteBuffer _waterMeshData = new();

        private Shader _terrainShader;
        private Material _terrainMaterial;
        private int _locTerrainLightPos;
        private int _locTerrainViewPos;
        private int _locTerrainAmbient;
        private int _locTerrainIntensity;

        private Shader _waterShader;
        private Material _waterMaterial;
        private int _locWaterLightPos;
        private int _locWaterViewPos;
        private int _locWaterAmbient;
        private int _locWaterIntensity;

        private bool _initialized;
        private int _frameIndex;

        public int DrawnChunkCountLastFrame { get; private set; }

        public int BuiltChunkCountLastFrame { get; private set; }

        public int MissingChunkCountLastFrame { get; private set; }

        public int TerrainVertexCountLastFrame { get; private set; }

        public int WaterVertexCountLastFrame { get; private set; }

        public double ChunkBuildMsLastFrame { get; private set; }

        public int CachedChunkCount => _chunks.Count;

        public float VisibleRadiusCm { get; set; } = 120_000f;

        public Vector3 LightPosition { get; set; } = new(50f, 200f, 100f);

        public float Ambient { get; set; } = 0.45f;

        public float LightIntensity { get; set; } = 0.55f;

        public void Render(IVisualHeightmapRenderSource source, in Camera3D camera)
        {
            if (source == null)
            {
                return;
            }

            EnsureInitialized();
            UpdateUniforms(camera);

            _frameIndex++;
            DrawnChunkCountLastFrame = 0;
            BuiltChunkCountLastFrame = 0;
            MissingChunkCountLastFrame = 0;
            TerrainVertexCountLastFrame = 0;
            WaterVertexCountLastFrame = 0;
            ChunkBuildMsLastFrame = 0d;
            IVisualTerrainRenderFeatureSource? featureSource = source as IVisualTerrainRenderFeatureSource;

            int minChunkX = ResolveChunkIndex((camera.target.X * 100f) - VisibleRadiusCm, source.Bounds.Left, source.Bounds.Width, source.ChunkColumns);
            int maxChunkX = ResolveChunkIndex((camera.target.X * 100f) + VisibleRadiusCm, source.Bounds.Left, source.Bounds.Width, source.ChunkColumns);
            int minChunkY = ResolveChunkIndex((camera.target.Z * 100f) - VisibleRadiusCm, source.Bounds.Top, source.Bounds.Height, source.ChunkRows);
            int maxChunkY = ResolveChunkIndex((camera.target.Z * 100f) + VisibleRadiusCm, source.Bounds.Top, source.Bounds.Height, source.ChunkRows);

            for (int y = minChunkY; y <= maxChunkY; y++)
            {
                for (int x = minChunkX; x <= maxChunkX; x++)
                {
                    if (!source.TryGetChunk(x, y, out VisualHeightmapRenderChunk chunk))
                    {
                        MissingChunkCountLastFrame++;
                        continue;
                    }

                    ref ChunkGpu gpu = ref GetOrCreateChunk(in chunk, featureSource);
                    gpu.LastUsedFrame = _frameIndex;
                    RaylibMatrix identity = RaylibMatrix.Identity;
                    Rl.rlDisableBackfaceCulling();
                    Rl.DrawMesh(gpu.Mesh, _terrainMaterial, identity);
                    if (gpu.WaterMesh.vertexCount > 0)
                    {
                        Rl.BeginBlendMode(BlendMode.BLEND_ALPHA);
                        Rl.DrawMesh(gpu.WaterMesh, _waterMaterial, identity);
                        Rl.EndBlendMode();
                        WaterVertexCountLastFrame += gpu.WaterMesh.vertexCount;
                    }

                    if (featureSource != null)
                    {
                        DrawFeatureEdges(in chunk, featureSource);
                    }

                    Rl.rlEnableBackfaceCulling();

                    DrawnChunkCountLastFrame++;
                    TerrainVertexCountLastFrame += gpu.Mesh.vertexCount;
                }
            }

            EvictUnusedChunks(240);
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            string baseDir = AppContext.BaseDirectory;
            _terrainShader = Rl.LoadShader(Path.Combine(baseDir, "terrain.vs"), Path.Combine(baseDir, "terrain.fs"));
            if (_terrainShader.id == 0)
            {
                throw new InvalidOperationException("Failed to load visual heightmap terrain shader (shader.id == 0).");
            }

            _terrainMaterial = Rl.LoadMaterialDefault();
            _terrainMaterial.shader = _terrainShader;
            _locTerrainLightPos = Rl.GetShaderLocation(_terrainShader, "uLightPos");
            _locTerrainViewPos = Rl.GetShaderLocation(_terrainShader, "uViewPos");
            _locTerrainAmbient = Rl.GetShaderLocation(_terrainShader, "uAmbient");
            _locTerrainIntensity = Rl.GetShaderLocation(_terrainShader, "uLightIntensity");

            _waterShader = Rl.LoadShader(Path.Combine(baseDir, "water.vs"), Path.Combine(baseDir, "water.fs"));
            if (_waterShader.id == 0)
            {
                throw new InvalidOperationException("Failed to load visual heightmap water shader (shader.id == 0).");
            }

            _waterMaterial = Rl.LoadMaterialDefault();
            _waterMaterial.shader = _waterShader;
            _locWaterLightPos = Rl.GetShaderLocation(_waterShader, "uLightPos");
            _locWaterViewPos = Rl.GetShaderLocation(_waterShader, "uViewPos");
            _locWaterAmbient = Rl.GetShaderLocation(_waterShader, "uAmbient");
            _locWaterIntensity = Rl.GetShaderLocation(_waterShader, "uLightIntensity");
            _initialized = true;
        }

        private void UpdateUniforms(in Camera3D camera)
        {
            Vector3 lightPos = LightPosition;
            Vector3 viewPos = camera.position;
            float ambient = Ambient;
            float intensity = LightIntensity;

            Rl.SetShaderValue(_terrainShader, _locTerrainLightPos, &lightPos, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_terrainShader, _locTerrainViewPos, &viewPos, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_terrainShader, _locTerrainAmbient, &ambient, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_terrainShader, _locTerrainIntensity, &intensity, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);

            Rl.SetShaderValue(_waterShader, _locWaterLightPos, &lightPos, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_waterShader, _locWaterViewPos, &viewPos, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_waterShader, _locWaterAmbient, &ambient, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_waterShader, _locWaterIntensity, &intensity, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        }

        private ref ChunkGpu GetOrCreateChunk(
            in VisualHeightmapRenderChunk chunk,
            IVisualTerrainRenderFeatureSource? featureSource)
        {
            long key = GraphChunkKey.Pack(chunk.ChunkX, chunk.ChunkY);
            if (_chunks.TryGetValue(key, out ChunkGpu existing))
            {
                if (existing.Matches(in chunk, featureSource != null))
                {
                    _chunks[key] = existing;
                    return ref _chunks.GetValueRefOrNullRef(key);
                }

                existing.Dispose();
                _chunks.Remove(key);
            }

            long buildStart = Stopwatch.GetTimestamp();
            ChunkGpu gpu = new()
            {
                Mesh = CreateChunkMesh(in chunk, featureSource),
                WaterMesh = featureSource != null ? CreateWaterMesh(in chunk, featureSource, _waterMeshData) : default,
                Revision = chunk.Revision,
                Bounds = chunk.Bounds,
                SampleColumns = chunk.SampleColumns,
                SampleRows = chunk.SampleRows,
                HasFeatures = featureSource != null,
                LastUsedFrame = _frameIndex,
            };
            BuiltChunkCountLastFrame++;
            ChunkBuildMsLastFrame += (Stopwatch.GetTimestamp() - buildStart) * 1000d / Stopwatch.Frequency;
            _chunks[key] = gpu;
            return ref _chunks.GetValueRefOrNullRef(key);
        }

        private static Mesh CreateChunkMesh(
            in VisualHeightmapRenderChunk chunk,
            IVisualTerrainRenderFeatureSource? featureSource)
        {
            int columns = chunk.SampleColumns;
            int rows = chunk.SampleRows;
            int vertexCount = checked(columns * rows);
            int indexCount = checked((columns - 1) * (rows - 1) * 6);
            if (vertexCount > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Raylib visual heightmap chunk ({chunk.ChunkX},{chunk.ChunkY}) has {vertexCount} vertices, exceeding the platform mesh index limit. Reduce samples per chunk.");
            }

            Mesh mesh = new()
            {
                vertexCount = vertexCount,
                triangleCount = indexCount / 3,
            };

            int vertexFloatCount = vertexCount * 3;
            int colorByteCount = vertexCount * 4;
            mesh.vertices = (float*)Rl.MemAlloc(sizeof(float) * vertexFloatCount);
            mesh.normals = (float*)Rl.MemAlloc(sizeof(float) * vertexFloatCount);
            mesh.colors = (byte*)Rl.MemAlloc(sizeof(byte) * colorByteCount);
            mesh.indices = (ushort*)Rl.MemAlloc(sizeof(ushort) * indexCount);

            float stepXCm = chunk.SampleStepXCm;
            float stepYCm = chunk.SampleStepYCm;
            ResolveChunkHeightRange(in chunk, out float minHeightCm, out float maxHeightCm);
            float heightRangeCm = MathF.Max(1f, maxHeightCm - minHeightCm);
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int vertex = (y * columns) + x;
                    float worldXCm = chunk.Bounds.Left + (x * stepXCm);
                    float worldYCm = chunk.Bounds.Top + (y * stepYCm);
                    chunk.TryReadHeightCm(x, y, out float heightCm);
                    Vector3 normal = ComputeNormal(in chunk, x, y, stepXCm, stepYCm);
                    int f = vertex * 3;
                    mesh.vertices[f + 0] = worldXCm * 0.01f;
                    mesh.vertices[f + 1] = heightCm * 0.01f;
                    mesh.vertices[f + 2] = worldYCm * 0.01f;
                    mesh.normals[f + 0] = normal.X;
                    mesh.normals[f + 1] = normal.Y;
                    mesh.normals[f + 2] = normal.Z;

                    int c = vertex * 4;
                    ResolveVertexColor(
                        in chunk,
                        featureSource,
                        x,
                        y,
                        heightCm,
                        minHeightCm,
                        heightRangeCm,
                        normal,
                        out byte red,
                        out byte green,
                        out byte blue);
                    mesh.colors[c + 0] = red;
                    mesh.colors[c + 1] = green;
                    mesh.colors[c + 2] = blue;
                    mesh.colors[c + 3] = 255;
                }
            }

            int cursor = 0;
            for (int y = 0; y < rows - 1; y++)
            {
                for (int x = 0; x < columns - 1; x++)
                {
                    int p00 = (y * columns) + x;
                    int p10 = p00 + 1;
                    int p01 = p00 + columns;
                    int p11 = p01 + 1;

                    mesh.indices[cursor++] = checked((ushort)p00);
                    mesh.indices[cursor++] = checked((ushort)p01);
                    mesh.indices[cursor++] = checked((ushort)p10);
                    mesh.indices[cursor++] = checked((ushort)p11);
                    mesh.indices[cursor++] = checked((ushort)p10);
                    mesh.indices[cursor++] = checked((ushort)p01);
                }
            }

            Rl.UploadMesh(ref mesh, false);
            return mesh;
        }

        private static void ResolveChunkHeightRange(in VisualHeightmapRenderChunk chunk, out float minHeightCm, out float maxHeightCm)
        {
            minHeightCm = float.PositiveInfinity;
            maxHeightCm = float.NegativeInfinity;
            int columns = chunk.SampleColumns;
            int rows = chunk.SampleRows;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    if (!chunk.TryReadHeightCm(x, y, out float heightCm))
                    {
                        continue;
                    }

                    minHeightCm = MathF.Min(minHeightCm, heightCm);
                    maxHeightCm = MathF.Max(maxHeightCm, heightCm);
                }
            }

            if (!float.IsFinite(minHeightCm) || !float.IsFinite(maxHeightCm))
            {
                minHeightCm = 0f;
                maxHeightCm = 1f;
            }
        }

        private static void ResolveVertexColor(
            in VisualHeightmapRenderChunk chunk,
            IVisualTerrainRenderFeatureSource? featureSource,
            int sampleX,
            int sampleY,
            float heightCm,
            float minHeightCm,
            float heightRangeCm,
            in Vector3 normal,
            out byte red,
            out byte green,
            out byte blue)
        {
            if (featureSource != null &&
                TryResolveFeatureCorner(in chunk, featureSource, sampleX, sampleY, out int cornerX, out int cornerY))
            {
                Vector4 sum = default;
                int count = 0;
                for (int dy = -1; dy <= 0; dy++)
                {
                    for (int dx = -1; dx <= 0; dx++)
                    {
                        if (!featureSource.TryReadFeatureCell(cornerX + dx, cornerY + dy, out VisualTerrainRenderCell cell))
                        {
                            continue;
                        }

                        sum += TerrainVisualRules.GetTerrainFeatureColor(in cell);
                        count++;
                    }
                }

                if (count > 0)
                {
                    float inv = 1f / count;
                    red = ClampUnitToByte(sum.X * inv);
                    green = ClampUnitToByte(sum.Y * inv);
                    blue = ClampUnitToByte(sum.Z * inv);
                    return;
                }
            }

            float heightBand = Math.Clamp((heightCm - minHeightCm) / heightRangeCm, 0f, 1f);
            float slope = Math.Clamp(1f - normal.Y, 0f, 1f);
            ResolveTerrainColor(heightBand, slope, out red, out green, out blue);
        }

        private static Mesh CreateWaterMesh(
            in VisualHeightmapRenderChunk chunk,
            IVisualTerrainRenderFeatureSource featureSource,
            ChunkMeshWriteBuffer buffer)
        {
            buffer.Clear();
            int cellColumns = chunk.SampleColumns - 1;
            int cellRows = chunk.SampleRows - 1;
            Vector3 normal = Vector3.UnitY;
            Vector4 color = new(0x4F / 255f, 0xC3 / 255f, 0xF7 / 255f, 0.6f);

            for (int y = 0; y < cellRows; y++)
            {
                for (int x = 0; x < cellColumns; x++)
                {
                    if (!TryResolveFeatureCell(in chunk, featureSource, x, y, out int cellX, out int cellY) ||
                        !featureSource.TryReadFeatureCell(cellX, cellY, out VisualTerrainRenderCell cell) ||
                        !cell.HasWater ||
                        cell.WaterHeightCm <= cell.SurfaceHeightCm)
                    {
                        continue;
                    }

                    float x0 = (chunk.Bounds.Left + (x * chunk.SampleStepXCm)) * 0.01f;
                    float x1 = (chunk.Bounds.Left + ((x + 1) * chunk.SampleStepXCm)) * 0.01f;
                    float z0 = (chunk.Bounds.Top + (y * chunk.SampleStepYCm)) * 0.01f;
                    float z1 = (chunk.Bounds.Top + ((y + 1) * chunk.SampleStepYCm)) * 0.01f;
                    float waterY = (cell.WaterHeightCm * 0.01f) + 0.003f;

                    buffer.EnsureAdditionalVertices(6);
                    buffer.AppendVertex(new Vector3(x0, waterY, z0), normal, color);
                    buffer.AppendVertex(new Vector3(x1, waterY, z0), normal, color);
                    buffer.AppendVertex(new Vector3(x1, waterY, z1), normal, color);
                    buffer.AppendVertex(new Vector3(x0, waterY, z0), normal, color);
                    buffer.AppendVertex(new Vector3(x1, waterY, z1), normal, color);
                    buffer.AppendVertex(new Vector3(x0, waterY, z1), normal, color);
                }
            }

            return buffer.VertexCount > 0 ? CreateUnindexedMesh(buffer) : default;
        }

        private static Mesh CreateUnindexedMesh(ChunkMeshWriteBuffer src)
        {
            Mesh mesh = new()
            {
                vertexCount = src.VertexCount,
                triangleCount = src.VertexCount / 3,
            };

            int vertexFloatCount = src.VertexCount * 3;
            int colorByteCount = src.VertexCount * 4;
            mesh.vertices = (float*)Rl.MemAlloc(sizeof(float) * vertexFloatCount);
            mesh.normals = (float*)Rl.MemAlloc(sizeof(float) * vertexFloatCount);
            mesh.colors = (byte*)Rl.MemAlloc(sizeof(byte) * colorByteCount);

            src.Vertices.AsSpan(0, vertexFloatCount).CopyTo(new Span<float>(mesh.vertices, vertexFloatCount));
            src.Normals.AsSpan(0, vertexFloatCount).CopyTo(new Span<float>(mesh.normals, vertexFloatCount));
            src.Colors.AsSpan(0, colorByteCount).CopyTo(new Span<byte>(mesh.colors, colorByteCount));

            Rl.UploadMesh(ref mesh, false);
            return mesh;
        }

        private static void ResolveTerrainColor(float heightBand, float slope, out byte red, out byte green, out byte blue)
        {
            Vector3 low = new(35f, 86f, 88f);
            Vector3 mid = new(82f, 143f, 84f);
            Vector3 high = new(190f, 174f, 108f);
            Vector3 peak = new(226f, 220f, 184f);
            Vector3 color = heightBand < 0.50f
                ? Vector3.Lerp(low, mid, heightBand * 2f)
                : heightBand < 0.82f
                    ? Vector3.Lerp(mid, high, (heightBand - 0.50f) / 0.32f)
                    : Vector3.Lerp(high, peak, (heightBand - 0.82f) / 0.18f);
            float shade = 1f - Math.Clamp(slope * 0.42f, 0f, 0.42f);
            red = ClampToByte(color.X * shade);
            green = ClampToByte(color.Y * shade);
            blue = ClampToByte(color.Z * shade);
        }

        private static Vector3 ComputeNormal(in VisualHeightmapRenderChunk chunk, int x, int y, float stepXCm, float stepYCm)
        {
            int left = Math.Max(0, x - 1);
            int right = Math.Min(chunk.SampleColumns - 1, x + 1);
            int top = Math.Max(0, y - 1);
            int bottom = Math.Min(chunk.SampleRows - 1, y + 1);
            chunk.TryReadHeightCm(left, y, out float hLeft);
            chunk.TryReadHeightCm(right, y, out float hRight);
            chunk.TryReadHeightCm(x, top, out float hTop);
            chunk.TryReadHeightCm(x, bottom, out float hBottom);

            float dx = MathF.Max(1f, (right - left) * stepXCm);
            float dz = MathF.Max(1f, (bottom - top) * stepYCm);
            Vector3 normal = Vector3.Normalize(new Vector3(-(hRight - hLeft) / dx, 1f, -(hBottom - hTop) / dz));
            return float.IsFinite(normal.X) && float.IsFinite(normal.Y) && float.IsFinite(normal.Z)
                ? normal
                : Vector3.UnitY;
        }

        private void EvictUnusedChunks(int maxAgeFrames)
        {
            if (_chunks.Count == 0)
            {
                return;
            }

            int threshold = _frameIndex - maxAgeFrames;
            _evictKeys.Clear();
            foreach (var kvp in _chunks)
            {
                if (kvp.Value.LastUsedFrame < threshold)
                {
                    _evictKeys.Add(kvp.Key);
                }
            }

            for (int i = 0; i < _evictKeys.Count; i++)
            {
                long key = _evictKeys[i];
                if (_chunks.TryGetValue(key, out ChunkGpu chunk))
                {
                    chunk.Dispose();
                    _chunks.Remove(key);
                }
            }
        }

        private static void DrawFeatureEdges(
            in VisualHeightmapRenderChunk chunk,
            IVisualTerrainRenderFeatureSource featureSource)
        {
            int cellColumns = chunk.SampleColumns - 1;
            int cellRows = chunk.SampleRows - 1;
            for (int y = 0; y < cellRows; y++)
            {
                for (int x = 0; x < cellColumns; x++)
                {
                    if (!TryResolveFeatureCell(in chunk, featureSource, x, y, out int cellX, out int cellY) ||
                        !featureSource.TryReadFeatureCell(cellX, cellY, out VisualTerrainRenderCell cell))
                    {
                        continue;
                    }

                    if (featureSource.TryReadFeatureCell(cellX + 1, cellY, out VisualTerrainRenderCell right) &&
                        right.HeightLevel != cell.HeightLevel)
                    {
                        float edgeX = (chunk.Bounds.Left + ((x + 1) * chunk.SampleStepXCm)) * 0.01f;
                        float z0 = (chunk.Bounds.Top + (y * chunk.SampleStepYCm)) * 0.01f;
                        float z1 = (chunk.Bounds.Top + ((y + 1) * chunk.SampleStepYCm)) * 0.01f;
                        float edgeY = ResolveEdgeY(cell, right);
                        Rl.DrawLine3D(
                            new Vector3(edgeX, edgeY, z0),
                            new Vector3(edgeX, edgeY, z1),
                            ResolveEdgeColor(cell, right));
                    }

                    if (featureSource.TryReadFeatureCell(cellX, cellY + 1, out VisualTerrainRenderCell bottom) &&
                        bottom.HeightLevel != cell.HeightLevel)
                    {
                        float x0 = (chunk.Bounds.Left + (x * chunk.SampleStepXCm)) * 0.01f;
                        float x1 = (chunk.Bounds.Left + ((x + 1) * chunk.SampleStepXCm)) * 0.01f;
                        float edgeZ = (chunk.Bounds.Top + ((y + 1) * chunk.SampleStepYCm)) * 0.01f;
                        float edgeY = ResolveEdgeY(cell, bottom);
                        Rl.DrawLine3D(
                            new Vector3(x0, edgeY, edgeZ),
                            new Vector3(x1, edgeY, edgeZ),
                            ResolveEdgeColor(cell, bottom));
                    }
                }
            }
        }

        private static float ResolveEdgeY(
            in VisualTerrainRenderCell a,
            in VisualTerrainRenderCell b)
            => (Math.Max(a.SurfaceHeightCm, b.SurfaceHeightCm) * 0.01f) + 0.04f;

        private static Color ResolveEdgeColor(
            in VisualTerrainRenderCell a,
            in VisualTerrainRenderCell b)
            => a.IsRamp || b.IsRamp
                ? new Color(70, 190, 116, 255)
                : new Color(214, 76, 76, 255);

        private static bool TryResolveFeatureCell(
            in VisualHeightmapRenderChunk chunk,
            IVisualTerrainRenderFeatureSource featureSource,
            int localCellX,
            int localCellY,
            out int cellX,
            out int cellY)
        {
            float worldXCm = chunk.Bounds.Left + (localCellX * chunk.SampleStepXCm);
            float worldYCm = chunk.Bounds.Top + (localCellY * chunk.SampleStepYCm);
            return TryResolveFeatureCellIndex(featureSource, worldXCm, worldYCm, useCornerRounding: false, out cellX, out cellY);
        }

        private static bool TryResolveFeatureCorner(
            in VisualHeightmapRenderChunk chunk,
            IVisualTerrainRenderFeatureSource featureSource,
            int localSampleX,
            int localSampleY,
            out int cornerX,
            out int cornerY)
        {
            float worldXCm = chunk.Bounds.Left + (localSampleX * chunk.SampleStepXCm);
            float worldYCm = chunk.Bounds.Top + (localSampleY * chunk.SampleStepYCm);
            return TryResolveFeatureCellIndex(featureSource, worldXCm, worldYCm, useCornerRounding: true, out cornerX, out cornerY);
        }

        private static bool TryResolveFeatureCellIndex(
            IVisualTerrainRenderFeatureSource featureSource,
            float worldXCm,
            float worldYCm,
            bool useCornerRounding,
            out int cellX,
            out int cellY)
        {
            cellX = default;
            cellY = default;
            if (featureSource.FeatureCellColumns <= 0 ||
                featureSource.FeatureCellRows <= 0 ||
                featureSource.FeatureBounds.Width <= 0 ||
                featureSource.FeatureBounds.Height <= 0)
            {
                return false;
            }

            float cellWidthCm = featureSource.FeatureBounds.Width / (float)featureSource.FeatureCellColumns;
            float cellHeightCm = featureSource.FeatureBounds.Height / (float)featureSource.FeatureCellRows;
            if (!float.IsFinite(cellWidthCm) ||
                !float.IsFinite(cellHeightCm) ||
                cellWidthCm <= 0f ||
                cellHeightCm <= 0f)
            {
                return false;
            }

            float x = (worldXCm - featureSource.FeatureBounds.Left) / cellWidthCm;
            float y = (worldYCm - featureSource.FeatureBounds.Top) / cellHeightCm;
            if (!float.IsFinite(x) || !float.IsFinite(y))
            {
                return false;
            }

            if (useCornerRounding)
            {
                cellX = Math.Clamp((int)MathF.Round(x), 0, featureSource.FeatureCellColumns);
                cellY = Math.Clamp((int)MathF.Round(y), 0, featureSource.FeatureCellRows);
            }
            else
            {
                cellX = Math.Clamp((int)MathF.Floor(x + 0.0001f), 0, featureSource.FeatureCellColumns - 1);
                cellY = Math.Clamp((int)MathF.Floor(y + 0.0001f), 0, featureSource.FeatureCellRows - 1);
            }

            return true;
        }

        private static int ResolveChunkIndex(float worldCm, int minCm, int sizeCm, int chunkCount)
        {
            float normalized = (worldCm - minCm) / Math.Max(1f, sizeCm);
            return Math.Clamp((int)MathF.Floor(normalized * chunkCount), 0, chunkCount - 1);
        }

        private static byte ClampUnitToByte(float value)
        {
            if (value <= 0f) return 0;
            if (value >= 1f) return 255;
            return (byte)MathF.Round(value * 255f);
        }

        private static byte ClampToByte(float value)
        {
            return (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
        }

        public void Dispose()
        {
            foreach (var kvp in _chunks)
            {
                kvp.Value.Dispose();
            }

            _chunks.Clear();
            if (!_initialized)
            {
                return;
            }

            _terrainMaterial.shader = default;
            Rl.UnloadMaterial(_terrainMaterial);
            Rl.UnloadShader(_terrainShader);
            _waterMaterial.shader = default;
            Rl.UnloadMaterial(_waterMaterial);
            Rl.UnloadShader(_waterShader);
            _initialized = false;
        }

        private struct ChunkGpu : IDisposable
        {
            public Mesh Mesh;
            public Mesh WaterMesh;
            public int Revision;
            public WorldAabbCm Bounds;
            public int SampleColumns;
            public int SampleRows;
            public bool HasFeatures;
            public int LastUsedFrame;

            public bool Matches(in VisualHeightmapRenderChunk chunk, bool hasFeatures)
                => Revision == chunk.Revision &&
                   Bounds == chunk.Bounds &&
                   SampleColumns == chunk.SampleColumns &&
                   SampleRows == chunk.SampleRows &&
                   HasFeatures == hasFeatures;

            public void Dispose()
            {
                if (Mesh.vertexCount > 0)
                {
                    Rl.UnloadMesh(Mesh);
                }

                if (WaterMesh.vertexCount > 0)
                {
                    Rl.UnloadMesh(WaterMesh);
                }
            }
        }
    }
}
