using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;

namespace VisualTerrainEditorMod.Runtime;

internal sealed class VisualTerrainEditorDocument : IDisposable
{
    private const float WaterHeight = 0.46f;
    private const float Tau = MathF.PI * 2f;
    private const float HeightAmplitudeCm = 4_000f;
    private readonly VisualTerrainAssetDescriptor _asset;
    private readonly int _defaultMaterialAssetId;
    private readonly VisualTerrainErosionParameters _parameters = new();
    private readonly ChunkedContinuousHeightmapStore _heightmapStore;
    private readonly ChunkedContinuousHeightmapRuntime _heightmapRuntime;
    private readonly Dictionary<long, ChunkState> _chunks = new();
    private readonly List<ChunkState> _dirtyChunksScratch = new();

    public VisualTerrainEditorDocument(VisualTerrainAssetDescriptor asset, int defaultMaterialAssetId)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        if (defaultMaterialAssetId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultMaterialAssetId), "Visual terrain editor requires a positive default material asset id.");
        }

        _defaultMaterialAssetId = defaultMaterialAssetId;
        _heightmapStore = new ChunkedContinuousHeightmapStore(_asset.CreateHeightmapDescriptor());
        _heightmapRuntime = new ChunkedContinuousHeightmapRuntime(_heightmapStore.Descriptor, _heightmapStore);
        Reset();
    }

    public TerrainViewMode ViewMode { get; private set; } = TerrainViewMode.Eroded;

    public bool LowerBrush { get; private set; }

    public float BrushRadiusMeters { get; private set; }

    public float MinHeightCm { get; private set; }

    public float MaxHeightCm { get; private set; }

    public VisualTerrainAssetDescriptor Asset => _asset;

    public IContinuousHeightmap HeightmapRuntime => _heightmapRuntime;

    public float Scale => _parameters.Scale;

    public float Strength => _parameters.Strength;

    public float GullyWeight => _parameters.GullyWeight;

    public float Detail => _parameters.Detail;

    public int Octaves => _parameters.Octaves;

    public int LoadedChunkCount => _chunks.Count;

    public int EditedChunkCount
    {
        get
        {
            int count = 0;
            foreach (ChunkState state in _chunks.Values)
            {
                if (state.Edited)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public float BrushRadius => BrushRadiusMeters;

    public VisualTerrainErosionSettingsSnapshot CreateErosionSettingsSnapshot()
    {
        return new VisualTerrainErosionSettingsSnapshot(
            _parameters.Scale,
            _parameters.Strength,
            _parameters.GullyWeight,
            _parameters.Detail,
            _parameters.RidgeRounding,
            _parameters.CreaseRounding,
            _parameters.InputRoundingMultiplier,
            _parameters.OctaveRoundingMultiplier,
            _parameters.InputOnset,
            _parameters.OctaveOnset,
            _parameters.RidgeMapInputOnset,
            _parameters.RidgeMapOctaveOnset,
            _parameters.AssumedSlopeValue,
            _parameters.AssumedSlopeMix,
            _parameters.CellScale,
            _parameters.Normalization,
            _parameters.Octaves,
            _parameters.Lacunarity,
            _parameters.Gain);
    }

    public void ApplyErosionSettingsSnapshot(in VisualTerrainErosionSettingsSnapshot snapshot)
    {
        snapshot.Validate();
        _parameters.Scale = snapshot.Scale;
        _parameters.Strength = snapshot.Strength;
        _parameters.GullyWeight = snapshot.GullyWeight;
        _parameters.Detail = snapshot.Detail;
        _parameters.RidgeRounding = snapshot.RidgeRounding;
        _parameters.CreaseRounding = snapshot.CreaseRounding;
        _parameters.InputRoundingMultiplier = snapshot.InputRoundingMultiplier;
        _parameters.OctaveRoundingMultiplier = snapshot.OctaveRoundingMultiplier;
        _parameters.InputOnset = snapshot.InputOnset;
        _parameters.OctaveOnset = snapshot.OctaveOnset;
        _parameters.RidgeMapInputOnset = snapshot.RidgeMapInputOnset;
        _parameters.RidgeMapOctaveOnset = snapshot.RidgeMapOctaveOnset;
        _parameters.AssumedSlopeValue = snapshot.AssumedSlopeValue;
        _parameters.AssumedSlopeMix = snapshot.AssumedSlopeMix;
        _parameters.CellScale = snapshot.CellScale;
        _parameters.Normalization = snapshot.Normalization;
        _parameters.Octaves = snapshot.Octaves;
        _parameters.Lacunarity = snapshot.Lacunarity;
        _parameters.Gain = snapshot.Gain;
        MarkAllLoadedChunksDirty();
    }

    public void GetChunkStatus(int chunkX, int chunkY, out bool loaded, out bool edited)
    {
        long key = GraphChunkKey.Pack(chunkX, chunkY);
        if (_chunks.TryGetValue(key, out ChunkState? state))
        {
            loaded = true;
            edited = state.Edited;
            return;
        }

        loaded = false;
        edited = false;
    }

    public void Reset()
    {
        foreach (long key in _chunks.Keys)
        {
            var (chunkX, chunkY) = GraphChunkKey.Unpack(key);
            _heightmapStore.RemoveChunk(chunkX, chunkY);
        }

        _chunks.Clear();
        _parameters.Reset();
        LowerBrush = false;
        ViewMode = TerrainViewMode.Eroded;
        BrushRadiusMeters = Math.Clamp(MathF.Min(_asset.Bounds.Width, _asset.Bounds.Height) * 0.01f * 0.03f, 5f, 100f);
        MinHeightCm = 0f;
        MaxHeightCm = 0f;
    }

    public bool Update()
    {
        _dirtyChunksScratch.Clear();
        foreach (ChunkState state in _chunks.Values)
        {
            if (!state.Dirty)
            {
                continue;
            }

            _dirtyChunksScratch.Add(state);
        }

        if (_dirtyChunksScratch.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < _dirtyChunksScratch.Count; i++)
        {
            RecomputeChunk(_dirtyChunksScratch[i]);
        }

        for (int i = 0; i < _dirtyChunksScratch.Count; i++)
        {
            ChunkState state = _dirtyChunksScratch[i];
            RebuildChunkProceduralMesh(state);
            state.Dirty = false;
        }

        float minHeightCm = float.PositiveInfinity;
        float maxHeightCm = float.NegativeInfinity;
        foreach (ChunkState state in _chunks.Values)
        {
            minHeightCm = MathF.Min(minHeightCm, state.MinHeightCm);
            maxHeightCm = MathF.Max(maxHeightCm, state.MaxHeightCm);
        }

        MinHeightCm = float.IsFinite(minHeightCm) ? minHeightCm : 0f;
        MaxHeightCm = float.IsFinite(maxHeightCm) ? maxHeightCm : 0f;
        return true;
    }

    public void EnsureChunkWindowLoaded(int centerChunkX, int centerChunkY, int radius)
    {
        int minChunkX = Math.Max(0, centerChunkX - radius);
        int maxChunkX = Math.Min(_asset.ChunkColumns - 1, centerChunkX + radius);
        int minChunkY = Math.Max(0, centerChunkY - radius);
        int maxChunkY = Math.Min(_asset.ChunkRows - 1, centerChunkY + radius);

        for (int chunkY = minChunkY; chunkY <= maxChunkY; chunkY++)
        {
            for (int chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
            {
                EnsureChunkLoaded(chunkX, chunkY, requireProceduralMesh: true);
            }
        }

        PrewarmChunkNeighborhood(minChunkX, maxChunkX, minChunkY, maxChunkY);
    }

    public void PruneUneditedChunksOutsideWindow(int centerChunkX, int centerChunkY, int radius)
    {
        int minChunkX = Math.Max(0, centerChunkX - radius);
        int maxChunkX = Math.Min(_asset.ChunkColumns - 1, centerChunkX + radius);
        int minChunkY = Math.Max(0, centerChunkY - radius);
        int maxChunkY = Math.Min(_asset.ChunkRows - 1, centerChunkY + radius);

        var keysToRemove = new List<long>();
        foreach ((long key, ChunkState state) in _chunks)
        {
            if (state.Edited)
            {
                continue;
            }

            bool inside = state.ChunkX >= minChunkX &&
                          state.ChunkX <= maxChunkX &&
                          state.ChunkY >= minChunkY &&
                          state.ChunkY <= maxChunkY;
            if (!inside)
            {
                keysToRemove.Add(key);
            }
        }

        for (int i = 0; i < keysToRemove.Count; i++)
        {
            long key = keysToRemove[i];
            ChunkState state = _chunks[key];
            _heightmapStore.RemoveChunk(state.ChunkX, state.ChunkY);
            _chunks.Remove(key);
        }
    }

    public bool TryGetChunkProceduralMesh(int chunkX, int chunkY, out ProceduralMeshAssetData proceduralMesh)
    {
        ChunkState state = EnsureChunkLoaded(chunkX, chunkY);
        proceduralMesh = state.ProceduralMesh;
        return proceduralMesh.VertexCount > 0;
    }

    public WorldAabbCm GetChunkBounds(int chunkX, int chunkY)
    {
        int left = _asset.Bounds.Left + (chunkX * _asset.ChunkWorldWidthCm);
        int top = _asset.Bounds.Top + (chunkY * _asset.ChunkWorldHeightCm);
        return new WorldAabbCm(left, top, _asset.ChunkWorldWidthCm, _asset.ChunkWorldHeightCm);
    }

    public IEnumerable<SavedChunkData> EnumerateEditedChunks()
    {
        foreach (ChunkState state in _chunks.Values)
        {
            if (state.Edited)
            {
                yield return new SavedChunkData(state.ChunkX, state.ChunkY, state.BaseHeight);
            }
        }
    }

    public void RestoreEditedChunk(int chunkX, int chunkY, ReadOnlySpan<float> baseHeight)
    {
        ChunkState state = EnsureChunkLoaded(chunkX, chunkY);
        if (baseHeight.Length != state.BaseHeight.Length)
        {
            throw new ArgumentException("Saved visual terrain chunk sample count does not match the target chunk shape.", nameof(baseHeight));
        }

        baseHeight.CopyTo(state.BaseHeight);
        state.Edited = true;
        state.Dirty = true;
        MarkNeighbourhoodDirty(chunkX, chunkY);
    }

    public void PaintWorld(int worldXCm, int worldYCm)
    {
        int radiusCm = Math.Max(100, (int)MathF.Round(BrushRadiusMeters * 100f));
        int minChunkX = WorldToChunkX(worldXCm - radiusCm);
        int maxChunkX = WorldToChunkX(worldXCm + radiusCm);
        int minChunkY = WorldToChunkY(worldYCm - radiusCm);
        int maxChunkY = WorldToChunkY(worldYCm + radiusCm);
        PrewarmChunkNeighborhood(minChunkX, maxChunkX, minChunkY, maxChunkY);

        float amount = LowerBrush ? -0.015f : 0.015f;
        for (int chunkY = minChunkY; chunkY <= maxChunkY; chunkY++)
        {
            for (int chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
            {
                ChunkState state = EnsureChunkLoaded(chunkX, chunkY);
                bool changed = PaintChunk(state, worldXCm, worldYCm, radiusCm, amount);
                if (changed)
                {
                    state.Dirty = true;
                    state.Edited = true;
                    MarkNeighbourhoodDirty(chunkX, chunkY);
                }
            }
        }
    }

    private void PrewarmChunkNeighborhood(int minChunkX, int maxChunkX, int minChunkY, int maxChunkY)
    {
        int prewarmMinChunkX = Math.Max(0, minChunkX - 1);
        int prewarmMaxChunkX = Math.Min(_asset.ChunkColumns - 1, maxChunkX + 1);
        int prewarmMinChunkY = Math.Max(0, minChunkY - 1);
        int prewarmMaxChunkY = Math.Min(_asset.ChunkRows - 1, maxChunkY + 1);

        for (int chunkY = prewarmMinChunkY; chunkY <= prewarmMaxChunkY; chunkY++)
        {
            for (int chunkX = prewarmMinChunkX; chunkX <= prewarmMaxChunkX; chunkX++)
            {
                EnsureChunkLoaded(chunkX, chunkY, requireProceduralMesh: false);
            }
        }
    }

    public void SetViewMode(TerrainViewMode viewMode)
    {
        if (ViewMode == viewMode)
        {
            return;
        }

        ViewMode = viewMode;
        foreach (ChunkState state in _chunks.Values)
        {
            state.Dirty = true;
        }
    }

    public void SetBrushMode(bool lowerBrush)
    {
        LowerBrush = lowerBrush;
    }

    public void AdjustBrushRadius(float deltaMeters)
    {
        BrushRadiusMeters = Math.Clamp(BrushRadiusMeters + deltaMeters, 2f, 500f);
    }

    public void AdjustScale(float delta)
    {
        _parameters.Scale = Math.Clamp(_parameters.Scale + delta, 0.05f, 0.40f);
        MarkAllLoadedChunksDirty();
    }

    public void AdjustStrength(float delta)
    {
        _parameters.Strength = Math.Clamp(_parameters.Strength + delta, 0.02f, 0.60f);
        MarkAllLoadedChunksDirty();
    }

    public void AdjustGullyWeight(float delta)
    {
        _parameters.GullyWeight = Math.Clamp(_parameters.GullyWeight + delta, 0f, 1f);
        MarkAllLoadedChunksDirty();
    }

    public void AdjustDetail(float delta)
    {
        _parameters.Detail = Math.Clamp(_parameters.Detail + delta, 0.5f, 3f);
        MarkAllLoadedChunksDirty();
    }

    public void AdjustOctaves(int delta)
    {
        _parameters.Octaves = Math.Clamp(_parameters.Octaves + delta, 1, 8);
        MarkAllLoadedChunksDirty();
    }

    public void Dispose()
    {
    }

    private ChunkState EnsureChunkLoaded(int chunkX, int chunkY, bool requireProceduralMesh = true)
    {
        chunkX = Math.Clamp(chunkX, 0, _asset.ChunkColumns - 1);
        chunkY = Math.Clamp(chunkY, 0, _asset.ChunkRows - 1);
        long key = GraphChunkKey.Pack(chunkX, chunkY);
        if (_chunks.TryGetValue(key, out ChunkState? existing))
        {
            if (requireProceduralMesh && existing.ProceduralMesh.VertexCount == 0)
            {
                existing.Dirty = true;
            }

            return existing;
        }

        var state = new ChunkState(
            chunkX,
            chunkY,
            _asset.SamplesPerChunkColumn,
            _asset.SamplesPerChunkRow,
            _asset.RuntimeVertexCapacityPerChunk,
            _defaultMaterialAssetId);
        PopulateDefaultChunkBaseHeights(state);
        _chunks.Add(key, state);
        _heightmapStore.SetChunk(state.HeightChunk);
        state.Dirty = requireProceduralMesh;
        return state;
    }

    private void PopulateDefaultChunkBaseHeights(ChunkState state)
    {
        int chunkStepX = _asset.SamplesPerChunkColumn - 1;
        int chunkStepY = _asset.SamplesPerChunkRow - 1;
        int globalSampleWidth = _asset.SampleColumns - 1;
        int globalSampleHeight = _asset.SampleRows - 1;

        for (int localY = 0; localY < _asset.SamplesPerChunkRow; localY++)
        {
            int globalY = (state.ChunkY * chunkStepY) + localY;
            float v = globalY / (float)globalSampleHeight;
            for (int localX = 0; localX < _asset.SamplesPerChunkColumn; localX++)
            {
                int globalX = (state.ChunkX * chunkStepX) + localX;
                float u = globalX / (float)globalSampleWidth;
                state.BaseHeight[GetChunkSampleIndex(localX, localY)] = GenerateBaseHeight01(u, v, _asset.DefaultHeight01);
            }
        }
    }

    private bool PaintChunk(ChunkState state, int brushWorldXCm, int brushWorldYCm, int radiusCm, float amount)
    {
        bool changed = false;
        int chunkStepX = _asset.SamplesPerChunkColumn - 1;
        int chunkStepY = _asset.SamplesPerChunkRow - 1;
        int globalSampleWidth = _asset.SampleColumns - 1;
        int globalSampleHeight = _asset.SampleRows - 1;

        for (int localY = 0; localY < _asset.SamplesPerChunkRow; localY++)
        {
            int globalY = (state.ChunkY * chunkStepY) + localY;
            float v = globalY / (float)globalSampleHeight;
            float worldYCm = Lerp(_asset.Bounds.Top, _asset.Bounds.Bottom, v);
            for (int localX = 0; localX < _asset.SamplesPerChunkColumn; localX++)
            {
                int globalX = (state.ChunkX * chunkStepX) + localX;
                float u = globalX / (float)globalSampleWidth;
                float worldXCm = Lerp(_asset.Bounds.Left, _asset.Bounds.Right, u);

                float dx = worldXCm - brushWorldXCm;
                float dy = worldYCm - brushWorldYCm;
                float dist = MathF.Sqrt((dx * dx) + (dy * dy));
                if (dist > radiusCm)
                {
                    continue;
                }

                float t = Clamp01(1f - (dist / radiusCm));
                float falloff = t * t * (3f - (2f * t));
                int index = GetChunkSampleIndex(localX, localY);
                float current = state.BaseHeight[index];
                float next = Clamp01(current + (amount * falloff));
                if (MathF.Abs(next - current) <= 1e-6f)
                {
                    continue;
                }

                state.BaseHeight[index] = next;
                changed = true;
            }
        }

        return changed;
    }

    private void MarkNeighbourhoodDirty(int chunkX, int chunkY)
    {
        for (int offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                long key = GraphChunkKey.Pack(chunkX + offsetX, chunkY + offsetY);
                if (_chunks.TryGetValue(key, out ChunkState? state))
                {
                    state.Dirty = true;
                }
            }
        }
    }

    private void MarkAllLoadedChunksDirty()
    {
        foreach (ChunkState state in _chunks.Values)
        {
            state.Dirty = true;
        }
    }

    private void RecomputeChunk(ChunkState state)
    {
        object rangeLock = new();
        float minHeightCm = float.PositiveInfinity;
        float maxHeightCm = float.NegativeInfinity;

        Parallel.For(
            0,
            _asset.SamplesPerChunkRow,
            () => new HeightRangeAccumulator(float.PositiveInfinity, float.NegativeInfinity),
            (localY, _, localRange) =>
            {
                for (int localX = 0; localX < _asset.SamplesPerChunkColumn; localX++)
                {
                    int index = GetChunkSampleIndex(localX, localY);
                    int globalX = (state.ChunkX * (_asset.SamplesPerChunkColumn - 1)) + localX;
                    int globalY = (state.ChunkY * (_asset.SamplesPerChunkRow - 1)) + localY;

                    float height = state.BaseHeight[index];
                    Vector2 slope = ComputeBaseSlopeGlobal(globalX, globalY);
                    float fadeTarget = Math.Clamp((height - _asset.DefaultHeight01) / 0.15f, -1f, 1f);
                    Vector2 uv = new(
                        globalX / (float)(_asset.SampleColumns - 1),
                        globalY / (float)(_asset.SampleRows - 1));

                    FilterOutput output = EvaluateErosion(uv, height, slope, fadeTarget, _parameters);
                    float erodedHeight = Clamp01(height + output.HeightDelta);
                    state.RidgeMap[index] = output.RidgeMap;
                    state.ErodedHeight[index] = erodedHeight;

                    float ridge01 = Clamp01((state.RidgeMap[index] * 0.5f) + 0.5f);
                    state.Drainage[index] = Clamp01((1f - Clamp01(ridge01 / 0.3f)) * 1.5f);

                    float heightCm = (erodedHeight - _asset.DefaultHeight01) * HeightAmplitudeCm;
                    state.HeightSamplesCm[index] = (short)Math.Clamp(MathF.Round(heightCm), short.MinValue, short.MaxValue);
                    localRange.Include(heightCm);
                }

                return localRange;
            },
            localRange =>
            {
                lock (rangeLock)
                {
                    minHeightCm = MathF.Min(minHeightCm, localRange.MinHeightCm);
                    maxHeightCm = MathF.Max(maxHeightCm, localRange.MaxHeightCm);
                }
            });

        state.MinHeightCm = float.IsFinite(minHeightCm) ? minHeightCm : 0f;
        state.MaxHeightCm = float.IsFinite(maxHeightCm) ? maxHeightCm : 0f;
    }

    private void RebuildChunkProceduralMesh(ChunkState state)
    {
        float meshStepX = 1f / (_asset.RenderColumnsPerChunk - 1);
        float meshStepY = 1f / (_asset.RenderRowsPerChunk - 1);
        WorldAabbCm chunkBounds = GetChunkBounds(state.ChunkX, state.ChunkY);
        float chunkCenterXMeters = ((chunkBounds.Left + chunkBounds.Right) * 0.5f) * 0.01f;
        float chunkCenterZMeters = ((chunkBounds.Top + chunkBounds.Bottom) * 0.5f) * 0.01f;
        int renderColumns = _asset.RenderColumnsPerChunk;
        int renderRows = _asset.RenderRowsPerChunk;
        int vertexCount = renderColumns * renderRows;
        int indexCount = _asset.RuntimeIndexCapacityPerChunk;

        Parallel.For(0, renderRows, y =>
        {
            float v = y * meshStepY;
            for (int x = 0; x < renderColumns; x++)
            {
                float u = x * meshStepX;
                RenderVertexData vertex = BuildRenderVertex(chunkBounds, chunkCenterXMeters, chunkCenterZMeters, u, v);
                WriteProceduralVertex(state.ProceduralMesh, (y * renderColumns) + x, in vertex, u, v);
            }
        });

        int indexCursor = 0;
        for (int y = 0; y < renderRows - 1; y++)
        {
            for (int x = 0; x < renderColumns - 1; x++)
            {
                int p00 = (y * renderColumns) + x;
                int p01 = ((y + 1) * renderColumns) + x;
                int p10 = (y * renderColumns) + (x + 1);
                int p11 = ((y + 1) * renderColumns) + (x + 1);

                state.ProceduralMesh.Indices[indexCursor++] = p00;
                state.ProceduralMesh.Indices[indexCursor++] = p01;
                state.ProceduralMesh.Indices[indexCursor++] = p10;
                state.ProceduralMesh.Indices[indexCursor++] = p11;
                state.ProceduralMesh.Indices[indexCursor++] = p10;
                state.ProceduralMesh.Indices[indexCursor++] = p01;
            }
        }

        state.ProceduralMesh.Commit(
            vertexCount,
            indexCount,
            new[] { new ProceduralSubmeshDescriptor(0, indexCount, state.MaterialAssetId) },
            new ProceduralMeshBounds(
                new Vector3(
                    0f,
                    ((state.MinHeightCm + state.MaxHeightCm) * 0.5f) * 0.01f,
                    0f),
                new Vector3(
                    chunkBounds.Width * 0.005f,
                    MathF.Max(0.5f, (state.MaxHeightCm - state.MinHeightCm) * 0.005f),
                    chunkBounds.Height * 0.005f)),
            ProceduralMeshUsageHint.Static);
    }

    private static Vector3 ComputeTriangleNormal(in Vector3 a, in Vector3 b, in Vector3 c)
    {
        Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        return normal.Y < 0f ? -normal : normal;
    }

    private RenderVertexData BuildRenderVertex(
        WorldAabbCm chunkBounds,
        float chunkCenterXMeters,
        float chunkCenterZMeters,
        float localU,
        float localV)
    {
        float worldXMeters = Lerp(chunkBounds.Left, chunkBounds.Right, localU) * 0.01f;
        float worldZMeters = Lerp(chunkBounds.Top, chunkBounds.Bottom, localV) * 0.01f;
        float worldXCm = worldXMeters * 100f;
        float worldYCm = worldZMeters * 100f;
        float baseHeight = SampleFieldWorld(worldXCm, worldYCm, TerrainFieldKind.Base);
        float ridge = SampleFieldWorld(worldXCm, worldYCm, TerrainFieldKind.Ridge);
        float drainage = SampleFieldWorld(worldXCm, worldYCm, TerrainFieldKind.Drainage);
        float height;
        Vector3 normal;
        if (ViewMode == TerrainViewMode.Base)
        {
            height = baseHeight;
            normal = ComputeBaseRenderNormal(worldXCm, worldYCm);
        }
        else
        {
            TrySampleRuntimeSurface(worldXCm, worldYCm, out height, out normal);
        }

        Vector3 position = new(
            worldXMeters - chunkCenterXMeters,
            HeightToMeters(height, _asset.DefaultHeight01),
            worldZMeters - chunkCenterZMeters);
        Vector3 color = ViewMode switch
        {
            TerrainViewMode.Base => ShadeSurface(baseHeight, normal, ridge, 0f),
            TerrainViewMode.Eroded => ShadeSurface(height, normal, ridge, drainage),
            _ => ShadeRidges(ridge, drainage),
        };

        return new RenderVertexData(position, normal, color);
    }

    private void TrySampleRuntimeSurface(float worldXCm, float worldYCm, out float height, out Vector3 normal)
    {
        if (_heightmapRuntime.TrySampleSurface(worldXCm, worldYCm, out float runtimeHeightCm, out normal))
        {
            height = _asset.DefaultHeight01 + (runtimeHeightCm / HeightAmplitudeCm);
            return;
        }

        throw new InvalidOperationException(BuildMissingRuntimeNeighborhoodMessage(worldXCm, worldYCm));
    }

    private string BuildMissingRuntimeNeighborhoodMessage(float worldXCm, float worldYCm)
    {
        float normalizedX = _asset.SampleColumns > 1
            ? (_asset.SampleColumns - 1) * Math.Clamp((worldXCm - _asset.Bounds.Left) / Math.Max(1f, _asset.Bounds.Width), 0f, 1f)
            : 0f;
        float normalizedY = _asset.SampleRows > 1
            ? (_asset.SampleRows - 1) * Math.Clamp((worldYCm - _asset.Bounds.Top) / Math.Max(1f, _asset.Bounds.Height), 0f, 1f)
            : 0f;

        int x0 = _asset.SampleColumns > 1 ? Math.Clamp((int)MathF.Floor(normalizedX), 0, _asset.SampleColumns - 2) : 0;
        int y0 = _asset.SampleRows > 1 ? Math.Clamp((int)MathF.Floor(normalizedY), 0, _asset.SampleRows - 2) : 0;
        int x1 = _asset.SampleColumns > 1 ? x0 + 1 : 0;
        int y1 = _asset.SampleRows > 1 ? y0 + 1 : 0;

        return
            $"Visual terrain editor mesh sampling requires the runtime heightmap chunk neighborhood to be loaded. " +
            $"world=({worldXCm:F1},{worldYCm:F1}) sample=({normalizedX:F3},{normalizedY:F3}) " +
            $"{DescribeRequiredSample("h00", x0, y0)} " +
            $"{DescribeRequiredSample("h10", x1, y0)} " +
            $"{DescribeRequiredSample("h01", x0, y1)} " +
            $"{DescribeRequiredSample("h11", x1, y1)} " +
            $"loadedChunks={_chunks.Count}.";
    }

    private string DescribeRequiredSample(string label, int globalX, int globalY)
    {
        ResolveChunkSample(globalX, globalY, out int chunkX, out int chunkY, out int localX, out int localY);
        bool loaded = _heightmapStore.TryGetChunk(chunkX, chunkY, out _);
        return $"{label}=g({globalX},{globalY}) c({chunkX},{chunkY}) l({localX},{localY}) loaded={loaded};";
    }

    private Vector3 ComputeBaseRenderNormal(float worldXCm, float worldYCm, TerrainFieldKind field = TerrainFieldKind.Base)
    {
        float sampleStepX = _asset.ChunkWorldWidthCm / (float)Math.Max(1, _asset.RenderColumnsPerChunk - 1);
        float sampleStepY = _asset.ChunkWorldHeightCm / (float)Math.Max(1, _asset.RenderRowsPerChunk - 1);
        float uPrev = MathF.Max(_asset.Bounds.Left, worldXCm - sampleStepX);
        float uNext = MathF.Min(_asset.Bounds.Right, worldXCm + sampleStepX);
        float vPrev = MathF.Max(_asset.Bounds.Top, worldYCm - sampleStepY);
        float vNext = MathF.Min(_asset.Bounds.Bottom, worldYCm + sampleStepY);

        float hL = HeightToMeters(SampleFieldWorld(uPrev, worldYCm, field), _asset.DefaultHeight01);
        float hR = HeightToMeters(SampleFieldWorld(uNext, worldYCm, field), _asset.DefaultHeight01);
        float hD = HeightToMeters(SampleFieldWorld(worldXCm, vPrev, field), _asset.DefaultHeight01);
        float hU = HeightToMeters(SampleFieldWorld(worldXCm, vNext, field), _asset.DefaultHeight01);

        float spanMetersX = MathF.Max(0.001f, (uNext - uPrev) * 0.01f);
        float spanMetersZ = MathF.Max(0.001f, (vNext - vPrev) * 0.01f);
        return Vector3.Normalize(new Vector3(
            -(hR - hL) / spanMetersX,
            1f,
            -(hU - hD) / spanMetersZ));
    }

    private Vector2 ComputeBaseSlopeGlobal(int globalX, int globalY)
    {
        float hL = SampleBaseHeightGlobal(globalX - 1, globalY);
        float hR = SampleBaseHeightGlobal(globalX + 1, globalY);
        float hD = SampleBaseHeightGlobal(globalX, globalY - 1);
        float hU = SampleBaseHeightGlobal(globalX, globalY + 1);

        float derivScaleX = _asset.SampleColumns * 0.55f;
        float derivScaleY = _asset.SampleRows * 0.55f;
        return new Vector2(
            (hR - hL) * 0.5f * derivScaleX,
            (hU - hD) * 0.5f * derivScaleY);
    }

    private float SampleBaseHeightGlobal(int globalX, int globalY)
    {
        globalX = Math.Clamp(globalX, 0, _asset.SampleColumns - 1);
        globalY = Math.Clamp(globalY, 0, _asset.SampleRows - 1);

        ResolveChunkSample(globalX, globalY, out int chunkX, out int chunkY, out int localX, out int localY);
        long key = GraphChunkKey.Pack(chunkX, chunkY);
        if (_chunks.TryGetValue(key, out ChunkState? state))
        {
            return state.BaseHeight[GetChunkSampleIndex(localX, localY)];
        }

        float u = globalX / (float)(_asset.SampleColumns - 1);
        float v = globalY / (float)(_asset.SampleRows - 1);
        return GenerateBaseHeight01(u, v, _asset.DefaultHeight01);
    }

    private float SampleFieldWorld(float worldXCm, float worldYCm, TerrainFieldKind field)
    {
        float sampleX = _asset.SampleColumns > 1
            ? (_asset.SampleColumns - 1) * Math.Clamp((worldXCm - _asset.Bounds.Left) / Math.Max(1f, _asset.Bounds.Width), 0f, 1f)
            : 0f;
        float sampleY = _asset.SampleRows > 1
            ? (_asset.SampleRows - 1) * Math.Clamp((worldYCm - _asset.Bounds.Top) / Math.Max(1f, _asset.Bounds.Height), 0f, 1f)
            : 0f;
        return SampleField(sampleX, sampleY, field);
    }

    private float SampleField(float sampleX, float sampleY, TerrainFieldKind field)
    {
        int x0 = _asset.SampleColumns > 1 ? Math.Clamp((int)MathF.Floor(sampleX), 0, _asset.SampleColumns - 2) : 0;
        int y0 = _asset.SampleRows > 1 ? Math.Clamp((int)MathF.Floor(sampleY), 0, _asset.SampleRows - 2) : 0;
        int x1 = _asset.SampleColumns > 1 ? x0 + 1 : 0;
        int y1 = _asset.SampleRows > 1 ? y0 + 1 : 0;
        float tx = _asset.SampleColumns > 1 ? sampleX - x0 : 0f;
        float ty = _asset.SampleRows > 1 ? sampleY - y0 : 0f;

        float h00 = SampleFieldAtGlobalSample(x0, y0, field);
        float h10 = SampleFieldAtGlobalSample(x1, y0, field);
        float h01 = SampleFieldAtGlobalSample(x0, y1, field);
        float h11 = SampleFieldAtGlobalSample(x1, y1, field);
        bool degenerateCell = x0 == x1 || y0 == y1;
        if (_asset.InterpolationMode == ContinuousHeightmapInterpolationMode.TriangleHeightfield && !degenerateCell)
        {
            if (tx + ty <= 1f)
            {
                return h00 + ((h10 - h00) * tx) + ((h01 - h00) * ty);
            }

            return h11 + ((h01 - h11) * (1f - tx)) + ((h10 - h11) * (1f - ty));
        }

        float hx0 = Lerp(h00, h10, tx);
        float hx1 = Lerp(h01, h11, tx);
        return Lerp(hx0, hx1, ty);
    }

    private float SampleFieldAtGlobalSample(int globalX, int globalY, TerrainFieldKind field)
    {
        globalX = Math.Clamp(globalX, 0, _asset.SampleColumns - 1);
        globalY = Math.Clamp(globalY, 0, _asset.SampleRows - 1);

        ResolveChunkSample(globalX, globalY, out int chunkX, out int chunkY, out int localX, out int localY);
        long key = GraphChunkKey.Pack(chunkX, chunkY);
        if (_chunks.TryGetValue(key, out ChunkState? state))
        {
            int index = GetChunkSampleIndex(localX, localY);
            return field switch
            {
                TerrainFieldKind.Base => state.BaseHeight[index],
                TerrainFieldKind.Eroded => state.ErodedHeight[index],
                TerrainFieldKind.Ridge => state.RidgeMap[index],
                TerrainFieldKind.Drainage => state.Drainage[index],
                _ => state.BaseHeight[index],
            };
        }

        return field switch
        {
            TerrainFieldKind.Base => GenerateBaseHeight01(globalX / (float)(_asset.SampleColumns - 1), globalY / (float)(_asset.SampleRows - 1), _asset.DefaultHeight01),
            TerrainFieldKind.Eroded => GenerateBaseHeight01(globalX / (float)(_asset.SampleColumns - 1), globalY / (float)(_asset.SampleRows - 1), _asset.DefaultHeight01),
            TerrainFieldKind.Ridge => 0f,
            TerrainFieldKind.Drainage => 0f,
            _ => 0f,
        };
    }

    private void ResolveChunkSample(int globalX, int globalY, out int chunkX, out int chunkY, out int localX, out int localY)
    {
        int chunkStepX = _asset.SamplesPerChunkColumn - 1;
        int chunkStepY = _asset.SamplesPerChunkRow - 1;
        chunkX = globalX >= _asset.SampleColumns - 1 ? _asset.ChunkColumns - 1 : globalX / chunkStepX;
        chunkY = globalY >= _asset.SampleRows - 1 ? _asset.ChunkRows - 1 : globalY / chunkStepY;
        localX = globalX >= _asset.SampleColumns - 1 ? _asset.SamplesPerChunkColumn - 1 : globalX - (chunkX * chunkStepX);
        localY = globalY >= _asset.SampleRows - 1 ? _asset.SamplesPerChunkRow - 1 : globalY - (chunkY * chunkStepY);
    }

    private FilterOutput EvaluateErosion(Vector2 p, float height, Vector2 slope, float fadeTarget, VisualTerrainErosionParameters parameters)
    {
        float strength = parameters.Strength * parameters.Scale;
        Vector3 heightAndSlope = new(height, slope.X, slope.Y);
        Vector3 inputHeightAndSlope = heightAndSlope;
        float freq = 1f / (parameters.Scale * parameters.CellScale);
        float slopeLength = MathF.Max(slope.Length(), 1e-10f);
        float roundingMult = 1f;

        Vector4 rounding = new(
            parameters.RidgeRounding,
            parameters.CreaseRounding,
            parameters.InputRoundingMultiplier,
            parameters.OctaveRoundingMultiplier);
        Vector4 onset = new(
            parameters.InputOnset,
            parameters.OctaveOnset,
            parameters.RidgeMapInputOnset,
            parameters.RidgeMapOctaveOnset);
        Vector2 assumedSlope = new(parameters.AssumedSlopeValue, parameters.AssumedSlopeMix);

        float roundingForInput = Lerp(rounding.Y, rounding.X, Clamp01(fadeTarget + 0.5f)) * rounding.Z;
        float combiMask = EaseOut(SmoothStart(slopeLength * onset.X, roundingForInput * onset.X));

        float ridgeMapCombiMask = EaseOut(slopeLength * onset.Z);
        float ridgeMapFadeTarget = fadeTarget;

        Vector2 gullySlope = Vector2.Lerp(
            slope,
            slope / slopeLength * assumedSlope.X,
            assumedSlope.Y);

        for (int i = 0; i < parameters.Octaves; i++)
        {
            PhacelleResult phacelle = PhacelleNoise(p * freq, SafeNormalize(gullySlope), parameters.CellScale, 0.25f, parameters.Normalization);
            Vector2 sideDir = -phacelle.SideDir * freq;
            float sloping = MathF.Abs(phacelle.Sin);

            gullySlope += MathF.Sign(phacelle.Sin) * sideDir * strength * parameters.GullyWeight;

            Vector3 gullies = new(
                phacelle.Cos,
                phacelle.Sin * sideDir.X,
                phacelle.Sin * sideDir.Y);
            Vector3 fadedGullies = Vector3.Lerp(
                new Vector3(fadeTarget, 0f, 0f),
                gullies * parameters.GullyWeight,
                combiMask);

            heightAndSlope += fadedGullies * strength;
            fadeTarget = fadedGullies.X;

            float roundingForOctave = Lerp(rounding.Y, rounding.X, Clamp01(phacelle.Cos + 0.5f)) * roundingMult;
            float newMask = EaseOut(SmoothStart(sloping * onset.Y, roundingForOctave * onset.Y));
            combiMask = PowInv(combiMask, parameters.Detail) * newMask;

            ridgeMapFadeTarget = Lerp(ridgeMapFadeTarget, gullies.X, ridgeMapCombiMask);
            float newRidgeMapMask = EaseOut(sloping * onset.W);
            ridgeMapCombiMask *= newRidgeMapMask;

            strength *= parameters.Gain;
            freq *= parameters.Lacunarity;
            roundingMult *= rounding.W;
        }

        Vector3 delta = heightAndSlope - inputHeightAndSlope;
        return new FilterOutput(delta.X, ridgeMapFadeTarget * (1f - ridgeMapCombiMask));
    }

    private static PhacelleResult PhacelleNoise(Vector2 p, Vector2 normDir, float freq, float offset, float normalization)
    {
        Vector2 sideDir = new(-normDir.Y, normDir.X);
        sideDir *= freq * Tau;
        float phaseOffset = offset * Tau;

        Vector2 pInt = new(MathF.Floor(p.X), MathF.Floor(p.Y));
        Vector2 pFrac = Fract(p);
        Vector2 phaseDir = Vector2.Zero;
        float weightSum = 0f;

        for (int i = -1; i <= 2; i++)
        {
            for (int j = -1; j <= 2; j++)
            {
                Vector2 gridOffset = new(i, j);
                Vector2 gridPoint = pInt + gridOffset;
                Vector2 randomOffset = Hash(gridPoint) * 0.5f;
                Vector2 vectorFromCellPoint = pFrac - gridOffset - randomOffset;

                float sqrDist = Vector2.Dot(vectorFromCellPoint, vectorFromCellPoint);
                float weight = MathF.Exp(-sqrDist * 2f);
                weight = MathF.Max(0f, weight - 0.01111f);
                weightSum += weight;

                float waveInput = Vector2.Dot(vectorFromCellPoint, sideDir) + phaseOffset;
                phaseDir += new Vector2(MathF.Cos(waveInput), MathF.Sin(waveInput)) * weight;
            }
        }

        if (weightSum <= 1e-10f)
        {
            return new PhacelleResult(1f, 0f, sideDir);
        }

        Vector2 interpolated = phaseDir / weightSum;
        float magnitude = MathF.Sqrt(Vector2.Dot(interpolated, interpolated));
        magnitude = MathF.Max(1f - normalization, magnitude);
        Vector2 normalized = interpolated / MathF.Max(1e-10f, magnitude);
        return new PhacelleResult(normalized.X, normalized.Y, sideDir);
    }

    private static float GenerateBaseHeight01(float u, float v, float defaultHeight01)
    {
        float dx = u - 0.5f;
        float dy = v - 0.5f;
        float dist = MathF.Sqrt((dx * dx) + (dy * dy));
        float mountain = MathF.Max(0f, 1f - (dist / 0.36f));
        mountain = mountain * mountain * (3f - (2f * mountain));
        float basin = MathF.Max(0f, 1f - (MathF.Abs(u - 0.72f) + MathF.Abs(v - 0.28f)) / 0.24f);
        basin = basin * basin * (3f - (2f * basin));
        float noise = Fbm(new Vector2(u * 6f, v * 6f), 4);
        return Clamp01(defaultHeight01 + (mountain * 0.16f) - (basin * 0.05f) + (noise * 0.025f));
    }

    private static Vector3 ShadeSurface(float height, Vector3 normal, float ridge, float drainage)
    {
        Vector3 lightDir = Vector3.Normalize(new Vector3(-0.55f, 1f, 0.35f));
        float lambert = MathF.Max(0f, Vector3.Dot(normal, lightDir));
        float shade = 0.28f + (lambert * 0.72f);

        Vector3 baseColor = height switch
        {
            < 0.425f => new Vector3(0.18f, 0.20f, 0.24f),
            < WaterHeight => new Vector3(0.78f, 0.70f, 0.58f),
            < 0.50f => Vector3.Lerp(new Vector3(0.17f, 0.30f, 0.13f), new Vector3(0.34f, 0.48f, 0.20f), Clamp01((height - WaterHeight) / 0.04f)),
            < 0.58f => Vector3.Lerp(new Vector3(0.42f, 0.38f, 0.28f), new Vector3(0.53f, 0.46f, 0.34f), Clamp01((height - 0.50f) / 0.08f)),
            < 0.69f => Vector3.Lerp(new Vector3(0.30f, 0.30f, 0.32f), new Vector3(0.58f, 0.56f, 0.52f), Clamp01((height - 0.58f) / 0.11f)),
            _ => Vector3.Lerp(new Vector3(0.66f, 0.67f, 0.68f), new Vector3(0.92f, 0.93f, 0.95f), Clamp01((height - 0.69f) / 0.16f)),
        };

        float cliff = Clamp01((1f - normal.Y) / 0.55f);
        baseColor = Vector3.Lerp(baseColor, new Vector3(0.18f, 0.18f, 0.20f), cliff * 0.75f);

        if (height <= WaterHeight)
        {
            baseColor = Vector3.Lerp(new Vector3(0.02f, 0.11f, 0.17f), new Vector3(0.05f, 0.28f, 0.33f), Clamp01((WaterHeight - height) * 12f));
        }

        baseColor = Vector3.Lerp(baseColor, new Vector3(0.83f, 0.88f, 0.92f), drainage * 0.30f);
        baseColor *= shade;
        baseColor = Vector3.Lerp(baseColor, new Vector3(0.90f, 0.92f, 0.95f), Clamp01((height - 0.76f) / 0.10f) * 0.55f);

        float ridgeHighlight = Clamp01((ridge - 0.15f) / 0.85f) * 0.16f;
        baseColor += new Vector3(ridgeHighlight * 1.00f, ridgeHighlight * 0.82f, ridgeHighlight * 0.55f);
        return Clamp01(baseColor);
    }

    private static Vector3 ShadeRidges(float ridge, float drainage)
    {
        if (ridge >= 0f)
        {
            return Vector3.Lerp(new Vector3(0.16f, 0.14f, 0.16f), new Vector3(0.95f, 0.62f, 0.28f), Clamp01(ridge));
        }

        Vector3 crease = Vector3.Lerp(new Vector3(0.08f, 0.16f, 0.20f), new Vector3(0.20f, 0.83f, 0.92f), Clamp01(-ridge));
        return Vector3.Lerp(crease, Vector3.One, drainage * 0.65f);
    }

    private static float Fbm(Vector2 p, int octaves)
    {
        float amplitude = 0.5f;
        float frequency = 1f;
        float sum = 0f;

        for (int i = 0; i < octaves; i++)
        {
            sum += GradientNoise(p * frequency) * amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }

        return sum;
    }

    private static float GradientNoise(Vector2 p)
    {
        Vector2 i = Floor(p);
        Vector2 f = Fract(p);
        Vector2 u = f * f * (new Vector2(3f) - (2f * f));

        float a = Vector2.Dot(Hash(i + new Vector2(0f, 0f)), f - new Vector2(0f, 0f));
        float b = Vector2.Dot(Hash(i + new Vector2(1f, 0f)), f - new Vector2(1f, 0f));
        float c = Vector2.Dot(Hash(i + new Vector2(0f, 1f)), f - new Vector2(0f, 1f));
        float d = Vector2.Dot(Hash(i + new Vector2(1f, 1f)), f - new Vector2(1f, 1f));

        float x0 = Lerp(a, b, u.X);
        float x1 = Lerp(c, d, u.X);
        return Lerp(x0, x1, u.Y);
    }

    private static Vector2 Hash(Vector2 x)
    {
        Vector2 k = new(0.3183099f, 0.3678794f);
        x = (x * k) + new Vector2(k.Y, k.X);
        float n = Fract(x.X * x.Y * (x.X + x.Y));
        Vector2 v = Fract(new Vector2(16f * k.X * n, 16f * k.Y * n));
        return -Vector2.One + (v * 2f);
    }

    private static void WriteProceduralVertex(ProceduralMeshAssetData proceduralMesh, int vertexIndex, in RenderVertexData vertex, float u, float v)
    {
        int floatOffset = vertexIndex * 3;
        proceduralMesh.Positions[floatOffset + 0] = vertex.Position.X;
        proceduralMesh.Positions[floatOffset + 1] = vertex.Position.Y;
        proceduralMesh.Positions[floatOffset + 2] = vertex.Position.Z;

        proceduralMesh.Normals[floatOffset + 0] = vertex.Normal.X;
        proceduralMesh.Normals[floatOffset + 1] = vertex.Normal.Y;
        proceduralMesh.Normals[floatOffset + 2] = vertex.Normal.Z;

        int tangentOffset = vertexIndex * 4;
        Vector3 tangent = ComputeRenderTangent(vertex.Normal);

        proceduralMesh.Tangents[tangentOffset + 0] = tangent.X;
        proceduralMesh.Tangents[tangentOffset + 1] = tangent.Y;
        proceduralMesh.Tangents[tangentOffset + 2] = tangent.Z;
        proceduralMesh.Tangents[tangentOffset + 3] = 1f;

        int uvOffset = vertexIndex * 2;
        proceduralMesh.Uv0[uvOffset + 0] = u;
        proceduralMesh.Uv0[uvOffset + 1] = v;

        if (proceduralMesh.Colors32 != null)
        {
            int colorOffset = vertexIndex * 4;
            WriteColor(proceduralMesh.Colors32, colorOffset, vertex.Color);
        }
    }

    private static float HeightToMeters(float height, float defaultHeight01)
    {
        return (height - defaultHeight01) * HeightAmplitudeCm * 0.01f;
    }

    private static Vector3 ComputeRenderTangent(Vector3 normal)
    {
        if (!IsFiniteNonZero(normal))
        {
            throw new InvalidOperationException("Visual terrain editor render vertex requires a finite non-zero normal.");
        }

        Vector3 unitNormal = Vector3.Normalize(normal);
        Vector3 tangent = Vector3.Cross(Vector3.UnitY, unitNormal);
        if (!IsFiniteNonZero(tangent))
        {
            tangent = Vector3.Cross(Vector3.UnitZ, unitNormal);
        }

        if (!IsFiniteNonZero(tangent))
        {
            throw new InvalidOperationException("Visual terrain editor render vertex could not derive a finite non-zero tangent.");
        }

        return Vector3.Normalize(tangent);
    }

    private static bool IsFiniteNonZero(Vector3 value)
    {
        return float.IsFinite(value.X) &&
            float.IsFinite(value.Y) &&
            float.IsFinite(value.Z) &&
            value.LengthSquared() > 1e-10f;
    }

    private int WorldToChunkX(int worldXCm)
    {
        int clamped = Math.Clamp(worldXCm, _asset.Bounds.Left, _asset.Bounds.Right);
        int relative = clamped - _asset.Bounds.Left;
        return Math.Clamp(relative / _asset.ChunkWorldWidthCm, 0, _asset.ChunkColumns - 1);
    }

    private int WorldToChunkY(int worldYCm)
    {
        int clamped = Math.Clamp(worldYCm, _asset.Bounds.Top, _asset.Bounds.Bottom);
        int relative = clamped - _asset.Bounds.Top;
        return Math.Clamp(relative / _asset.ChunkWorldHeightCm, 0, _asset.ChunkRows - 1);
    }

    private int GetChunkSampleIndex(int x, int y)
    {
        return (y * _asset.SamplesPerChunkColumn) + x;
    }

    private static float Clamp01(float value)
    {
        return Math.Clamp(value, 0f, 1f);
    }

    private static Vector3 Clamp01(Vector3 value)
    {
        return Vector3.Clamp(value, Vector3.Zero, Vector3.One);
    }

    private static float PowInv(float value, float power)
    {
        return 1f - MathF.Pow(1f - Clamp01(value), power);
    }

    private static float EaseOut(float value)
    {
        float v = 1f - Clamp01(value);
        return 1f - (v * v);
    }

    private static float SmoothStart(float value, float smoothing)
    {
        if (smoothing <= 1e-10f)
        {
            return value;
        }

        if (value >= smoothing)
        {
            return value - (0.5f * smoothing);
        }

        return 0.5f * value * value / smoothing;
    }

    private static Vector2 SafeNormalize(Vector2 value)
    {
        float length = value.Length();
        return length > 1e-10f ? value / length : value;
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * t);
    }

    private static Vector2 Fract(Vector2 value)
    {
        return new Vector2(Fract(value.X), Fract(value.Y));
    }

    private static Vector2 Floor(Vector2 value)
    {
        return new Vector2(MathF.Floor(value.X), MathF.Floor(value.Y));
    }

    private static float Fract(float value)
    {
        return value - MathF.Floor(value);
    }

    private static void WriteColor(byte[] colors, int offset, Vector3 value)
    {
        value = Clamp01(value);
        colors[offset + 0] = (byte)(value.X * 255f);
        colors[offset + 1] = (byte)(value.Y * 255f);
        colors[offset + 2] = (byte)(value.Z * 255f);
        colors[offset + 3] = 255;
    }

    internal readonly record struct VisualTerrainErosionSettingsSnapshot(
        float Scale,
        float Strength,
        float GullyWeight,
        float Detail,
        float RidgeRounding,
        float CreaseRounding,
        float InputRoundingMultiplier,
        float OctaveRoundingMultiplier,
        float InputOnset,
        float OctaveOnset,
        float RidgeMapInputOnset,
        float RidgeMapOctaveOnset,
        float AssumedSlopeValue,
        float AssumedSlopeMix,
        float CellScale,
        float Normalization,
        int Octaves,
        float Lacunarity,
        float Gain)
    {
        public void Validate()
        {
            if (Scale is < 0.05f or > 0.40f) throw new ArgumentOutOfRangeException(nameof(Scale));
            if (Strength is < 0.02f or > 0.60f) throw new ArgumentOutOfRangeException(nameof(Strength));
            if (GullyWeight is < 0f or > 1f) throw new ArgumentOutOfRangeException(nameof(GullyWeight));
            if (Detail is < 0.5f or > 3f) throw new ArgumentOutOfRangeException(nameof(Detail));
            if (Octaves is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(Octaves));
            if (CellScale <= 0f) throw new ArgumentOutOfRangeException(nameof(CellScale));
            if (Lacunarity <= 0f) throw new ArgumentOutOfRangeException(nameof(Lacunarity));
            if (Gain <= 0f) throw new ArgumentOutOfRangeException(nameof(Gain));
        }
    }

    internal readonly record struct SavedChunkData(int ChunkX, int ChunkY, float[] BaseHeight);

    private enum TerrainFieldKind
    {
        Base,
        Eroded,
        Ridge,
        Drainage,
    }

    private readonly record struct FilterOutput(float HeightDelta, float RidgeMap);

    private readonly record struct PhacelleResult(float Cos, float Sin, Vector2 SideDir);

    private readonly record struct RenderVertexData(Vector3 Position, Vector3 Normal, Vector3 Color);

    private sealed class ChunkState
    {
        public ChunkState(int chunkX, int chunkY, int sampleColumns, int sampleRows, int runtimeVertexCapacity, int materialAssetId)
        {
            ChunkX = chunkX;
            ChunkY = chunkY;
            BaseHeight = new float[sampleColumns * sampleRows];
            ErodedHeight = new float[sampleColumns * sampleRows];
            RidgeMap = new float[sampleColumns * sampleRows];
            Drainage = new float[sampleColumns * sampleRows];
            HeightSamplesCm = new short[sampleColumns * sampleRows];
            ProceduralMesh = new ProceduralMeshAssetData(
                runtimeVertexCapacity,
                maxIndexCount: ((sampleColumns - 1) * (sampleRows - 1) * 6),
                maxSubmeshCount: 1,
                includeUv1: false,
                includeColors32: true);
            HeightChunk = new ChunkedContinuousHeightmapChunk(chunkX, chunkY, HeightSamplesCm);
            Dirty = true;
            MaterialAssetId = materialAssetId;
        }

        public int ChunkX { get; }

        public int ChunkY { get; }

        public float[] BaseHeight { get; }

        public float[] ErodedHeight { get; }

        public float[] RidgeMap { get; }

        public float[] Drainage { get; }

        public short[] HeightSamplesCm { get; }

        public ProceduralMeshAssetData ProceduralMesh { get; }

        public int MaterialAssetId { get; }

        public ChunkedContinuousHeightmapChunk HeightChunk { get; }

        public bool Dirty { get; set; }

        public bool Edited { get; set; }

        public float MinHeightCm { get; set; }

        public float MaxHeightCm { get; set; }
    }

    private struct HeightRangeAccumulator
    {
        public HeightRangeAccumulator(float minHeightCm, float maxHeightCm)
        {
            MinHeightCm = minHeightCm;
            MaxHeightCm = maxHeightCm;
        }

        public float MinHeightCm;

        public float MaxHeightCm;

        public void Include(float heightCm)
        {
            MinHeightCm = MathF.Min(MinHeightCm, heightCm);
            MaxHeightCm = MathF.Max(MaxHeightCm, heightCm);
        }
    }

    private sealed class VisualTerrainErosionParameters
    {
        public float Scale { get; set; } = 0.15f;
        public float Strength { get; set; } = 0.22f;
        public float GullyWeight { get; set; } = 0.50f;
        public float Detail { get; set; } = 1.50f;
        public float RidgeRounding { get; set; } = 0.10f;
        public float CreaseRounding { get; set; } = 0.00f;
        public float InputRoundingMultiplier { get; set; } = 0.10f;
        public float OctaveRoundingMultiplier { get; set; } = 2.00f;
        public float InputOnset { get; set; } = 0.70f;
        public float OctaveOnset { get; set; } = 1.25f;
        public float RidgeMapInputOnset { get; set; } = 2.80f;
        public float RidgeMapOctaveOnset { get; set; } = 1.50f;
        public float AssumedSlopeValue { get; set; } = 0.70f;
        public float AssumedSlopeMix { get; set; } = 1.00f;
        public float CellScale { get; set; } = 0.70f;
        public float Normalization { get; set; } = 0.50f;
        public int Octaves { get; set; } = 5;
        public float Lacunarity { get; set; } = 2.00f;
        public float Gain { get; set; } = 0.50f;

        public void Reset()
        {
            Scale = 0.15f;
            Strength = 0.22f;
            GullyWeight = 0.50f;
            Detail = 1.50f;
            RidgeRounding = 0.10f;
            CreaseRounding = 0.00f;
            InputRoundingMultiplier = 0.10f;
            OctaveRoundingMultiplier = 2.00f;
            InputOnset = 0.70f;
            OctaveOnset = 1.25f;
            RidgeMapInputOnset = 2.80f;
            RidgeMapOctaveOnset = 1.50f;
            AssumedSlopeValue = 0.70f;
            AssumedSlopeMix = 1.00f;
            CellScale = 0.70f;
            Normalization = 0.50f;
            Octaves = 5;
            Lacunarity = 2.00f;
            Gain = 0.50f;
        }
    }
}
