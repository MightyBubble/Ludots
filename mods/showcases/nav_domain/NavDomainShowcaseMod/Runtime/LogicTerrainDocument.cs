using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Spatial;

namespace NavDomainShowcaseMod.Runtime;

internal enum TerrainBrushMode : byte
{
    RaiseHeight = 0,
    LowerHeight = 1,
    Block = 2,
    Unblock = 3
}

internal sealed class LogicTerrainDocument
{
    private const float MinBrushRadiusMeters = 2f;
    private const float MaxBrushRadiusMeters = 48f;

    private readonly Dictionary<long, ChunkMeshState> _chunkMeshes = new();
    private readonly HashSet<long> _dirtyChunks = new();

    public LogicTerrainDocument(
        int widthCells,
        int heightCells,
        int cellSizeCm,
        float heightScaleMeters)
    {
        Field = new MutableGridLogicTerrainField(widthCells, heightCells, cellSizeCm);
        HeightScaleMeters = heightScaleMeters;
    }

    private sealed class ChunkMeshState
    {
        public ProceduralMeshAssetData Mesh = null!;
        public int Generation;
    }

    public MutableGridLogicTerrainField Field { get; }

    public float HeightScaleMeters { get; }

    public int WidthChunks => Field.WidthChunks;

    public int HeightChunks => Field.HeightChunks;

    public int ChunkSizeCells => Field.ChunkSizeCells;

    public int CellSizeCm => Field.CellSizeCm;

    public int ChunkWorldSizeCm => Field.ChunkSizeCells * Field.CellSizeCm;

    public int WorldWidthCm => Field.WidthCells * Field.CellSizeCm;

    public int WorldHeightCm => Field.HeightCells * Field.CellSizeCm;

    public TerrainBrushMode BrushMode { get; private set; } = TerrainBrushMode.RaiseHeight;

    public float BrushRadiusMeters { get; private set; } = 12f;

    public int DirtyChunkCount => _dirtyChunks.Count;

    public int PaintedChunkCount => _chunkMeshes.Count;

    public void SetBrushMode(TerrainBrushMode mode)
    {
        BrushMode = mode;
    }

    public void AdjustBrushRadius(float deltaMeters)
    {
        BrushRadiusMeters = Math.Clamp(BrushRadiusMeters + deltaMeters, MinBrushRadiusMeters, MaxBrushRadiusMeters);
    }

    public bool IsChunkDirty(int chunkX, int chunkY)
    {
        return _dirtyChunks.Contains(PackChunk(chunkX, chunkY));
    }

    public string BuildDirtyJson()
    {
        if (_dirtyChunks.Count == 0)
        {
            return "[]";
        }

        var keys = new List<string>(_dirtyChunks.Count);
        foreach (long packed in _dirtyChunks)
        {
            UnpackChunk(packed, out int cx, out int cy);
            keys.Add($"{cx},{cy}");
        }

        return JsonSerializer.Serialize(keys);
    }

    public void ClearDirty()
    {
        _dirtyChunks.Clear();
    }

    public void PaintWorldCm(int worldXCm, int worldZCm)
    {
        float radiusCm = BrushRadiusMeters * 100f;
        int minCol = Math.Max(0, (int)MathF.Floor((worldXCm - radiusCm) / CellSizeCm));
        int maxCol = Math.Min(Field.WidthCells - 1, (int)MathF.Ceiling((worldXCm + radiusCm) / CellSizeCm));
        int minRow = Math.Max(0, (int)MathF.Floor((worldZCm - radiusCm) / CellSizeCm));
        int maxRow = Math.Min(Field.HeightCells - 1, (int)MathF.Ceiling((worldZCm + radiusCm) / CellSizeCm));

        List<long>? touchedChunks = null;
        for (int row = minRow; row <= maxRow; row++)
        {
            for (int col = minCol; col <= maxCol; col++)
            {
                int cellMinXCm = col * CellSizeCm;
                int cellMinZCm = row * CellSizeCm;
                float dx = (cellMinXCm + (CellSizeCm * 0.5f)) - worldXCm;
                float dz = (cellMinZCm + (CellSizeCm * 0.5f)) - worldZCm;
                if ((dx * dx) + (dz * dz) > (radiusCm * radiusCm))
                {
                    continue;
                }

                if (ApplyBrushToCell(col, row))
                {
                    long packed = PackChunk(col / ChunkSizeCells, row / ChunkSizeCells);
                    _dirtyChunks.Add(packed);
                    touchedChunks ??= new List<long>();
                    if (!touchedChunks.Contains(packed))
                    {
                        touchedChunks.Add(packed);
                    }
                }
            }
        }

        if (touchedChunks != null)
        {
            for (int i = 0; i < touchedChunks.Count; i++)
            {
                RebuildChunkMesh(touchedChunks[i]);
            }
        }
    }

    public void Reset()
    {
        Field.Fill(new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.None));
        _dirtyChunks.Clear();
        foreach (long packed in _chunkMeshes.Keys)
        {
            RebuildChunkMesh(packed);
        }
    }

    public bool TryGetChunkMesh(int chunkX, int chunkY, out ProceduralMeshAssetData mesh, out int generation)
    {
        mesh = null!;
        generation = 0;
        if (!_chunkMeshes.TryGetValue(PackChunk(chunkX, chunkY), out ChunkMeshState? state))
        {
            return false;
        }

        mesh = state.Mesh;
        generation = state.Generation;
        return true;
    }

    private bool ApplyBrushToCell(int col, int row)
    {
        LogicTerrainCell cell = Field.GetCell(col, row);
        LogicTerrainCell next = BrushMode switch
        {
            TerrainBrushMode.RaiseHeight => new LogicTerrainCell(
                (byte)Math.Min(cell.HeightLevel + 1, SpatialScaleDefaults.LogicTerrainMaxHeightLevel),
                cell.WaterHeightLevel,
                cell.SurfaceFlags,
                cell.AreaId,
                cell.Cost),
            TerrainBrushMode.LowerHeight => new LogicTerrainCell(
                (byte)Math.Max(cell.HeightLevel - 1, 0),
                cell.WaterHeightLevel,
                cell.SurfaceFlags,
                cell.AreaId,
                cell.Cost),
            TerrainBrushMode.Block when !cell.IsBlocked => new LogicTerrainCell(
                cell.HeightLevel,
                cell.WaterHeightLevel,
                cell.SurfaceFlags | LogicTerrainSurfaceFlags.Blocked,
                cell.AreaId,
                cell.Cost),
            TerrainBrushMode.Unblock when cell.IsBlocked => new LogicTerrainCell(
                cell.HeightLevel,
                cell.WaterHeightLevel,
                cell.SurfaceFlags & ~LogicTerrainSurfaceFlags.Blocked,
                cell.AreaId,
                cell.Cost),
            _ => cell
        };

        if (next.HeightLevel == cell.HeightLevel &&
            next.WaterHeightLevel == cell.WaterHeightLevel &&
            next.SurfaceFlags == cell.SurfaceFlags)
        {
            return false;
        }

        Field.SetCell(col, row, next);
        return true;
    }

    private void RebuildChunkMesh(long packed)
    {
        UnpackChunk(packed, out int chunkX, out int chunkY);
        if (!_chunkMeshes.TryGetValue(packed, out ChunkMeshState? state))
        {
            int vertsPerSide = ChunkSizeCells + 1;
            state = new ChunkMeshState
            {
                Mesh = new ProceduralMeshAssetData(
                    maxVertexCount: vertsPerSide * vertsPerSide,
                    maxIndexCount: ChunkSizeCells * ChunkSizeCells * 6,
                    maxSubmeshCount: 1,
                    includeColors32: true)
            };
            _chunkMeshes[packed] = state;
        }

        WriteChunkVertices(state.Mesh, chunkX, chunkY);
        int triangleCount = ChunkSizeCells * ChunkSizeCells * 2;
        state.Mesh.Commit(
            vertexCount: (ChunkSizeCells + 1) * (ChunkSizeCells + 1),
            indexCount: triangleCount * 3,
            submeshes: stackalloc[]
            {
                new ProceduralSubmeshDescriptor(0, triangleCount * 3, 1)
            },
            localBounds: new ProceduralMeshBounds(
                new System.Numerics.Vector3(0f, 8f, 0f),
                new System.Numerics.Vector3(ChunkWorldSizeCm * 0.01f * 0.5f, 24f, ChunkWorldSizeCm * 0.01f * 0.5f)),
            usageHint: ProceduralMeshUsageHint.Dynamic);
        state.Generation++;
    }

    private void WriteChunkVertices(ProceduralMeshAssetData mesh, int chunkX, int chunkY)
    {
        int vertsPerSide = ChunkSizeCells + 1;
        int chunkOriginCol = chunkX * ChunkSizeCells;
        int chunkOriginRow = chunkY * ChunkSizeCells;
        float chunkHalfMeters = (ChunkWorldSizeCm * 0.5f) * 0.01f;
        float cellMeters = CellSizeCm * 0.01f;

        int vertexIndex = 0;
        for (int localRow = 0; localRow <= ChunkSizeCells; localRow++)
        {
            for (int localCol = 0; localCol <= ChunkSizeCells; localCol++)
            {
                int col = chunkOriginCol + localCol;
                int row = chunkOriginRow + localRow;
                float xMeters = (localCol * cellMeters) - chunkHalfMeters;
                float zMeters = (localRow * cellMeters) - chunkHalfMeters;
                float yMeters = HeightAtCorner(col, row) * HeightScaleMeters;

                int offset = vertexIndex * 3;
                mesh.Positions[offset + 0] = xMeters;
                mesh.Positions[offset + 1] = yMeters;
                mesh.Positions[offset + 2] = zMeters;
                mesh.Normals[offset + 0] = 0f;
                mesh.Normals[offset + 1] = 1f;
                mesh.Normals[offset + 2] = 0f;

                int tangentOffset = vertexIndex * 4;
                mesh.Tangents[tangentOffset + 0] = 1f;
                mesh.Tangents[tangentOffset + 1] = 0f;
                mesh.Tangents[tangentOffset + 2] = 0f;
                mesh.Tangents[tangentOffset + 3] = 1f;

                int uvOffset = vertexIndex * 2;
                mesh.Uv0[uvOffset + 0] = localCol / (float)ChunkSizeCells;
                mesh.Uv0[uvOffset + 1] = localRow / (float)ChunkSizeCells;

                WriteVertexColor(mesh, vertexIndex, col, row);
                vertexIndex++;
            }
        }

        int indexCursor = 0;
        for (int localRow = 0; localRow < ChunkSizeCells; localRow++)
        {
            for (int localCol = 0; localCol < ChunkSizeCells; localCol++)
            {
                int v0 = (localRow * vertsPerSide) + localCol;
                int v1 = v0 + 1;
                int v2 = v0 + vertsPerSide;
                int v3 = v2 + 1;
                mesh.Indices[indexCursor++] = v0;
                mesh.Indices[indexCursor++] = v2;
                mesh.Indices[indexCursor++] = v1;
                mesh.Indices[indexCursor++] = v1;
                mesh.Indices[indexCursor++] = v2;
                mesh.Indices[indexCursor++] = v3;
            }
        }
    }

    private byte HeightAtCorner(int col, int row)
    {
        if (col >= Field.WidthCells || row >= Field.HeightCells)
        {
            return Field.GetCell(Math.Min(col, Field.WidthCells - 1), Math.Min(row, Field.HeightCells - 1)).HeightLevel;
        }

        return Field.GetCell(col, row).HeightLevel;
    }

    private void WriteVertexColor(ProceduralMeshAssetData mesh, int vertexIndex, int col, int row)
    {
        bool blocked = col < Field.WidthCells && row < Field.HeightCells && Field.GetCell(col, row).IsBlocked;
        byte level = HeightAtCorner(col, row);
        float t = level / (float)SpatialScaleDefaults.LogicTerrainMaxHeightLevel;

        byte r = blocked ? (byte)(120 + (byte)(80 * t)) : (byte)(38 + (150 * t));
        byte g = blocked ? (byte)44 : (byte)(92 + (110 * t));
        byte b = blocked ? (byte)46 : (byte)(66 + (60 * t));

        int colorOffset = vertexIndex * 4;
        mesh.Colors32![colorOffset + 0] = r;
        mesh.Colors32![colorOffset + 1] = g;
        mesh.Colors32![colorOffset + 2] = b;
        mesh.Colors32![colorOffset + 3] = 255;
    }

    public void GetChunkStatus(int chunkX, int chunkY, out bool painted, out bool dirty)
    {
        long packed = PackChunk(chunkX, chunkY);
        painted = _chunkMeshes.ContainsKey(packed);
        dirty = _dirtyChunks.Contains(packed);
    }

    private static long PackChunk(int chunkX, int chunkY)
    {
        return ((long)chunkX << 32) | (uint)chunkY;
    }

    private static void UnpackChunk(long packed, out int chunkX, out int chunkY)
    {
        chunkX = (int)(packed >> 32);
        chunkY = (int)(packed & 0xFFFFFFFF);
    }
}
