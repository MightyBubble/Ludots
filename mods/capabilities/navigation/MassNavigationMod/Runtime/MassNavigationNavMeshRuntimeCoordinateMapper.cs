using System;
using System.Numerics;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.LogicHeightmap;

namespace MassNavigationMod.Runtime;

internal readonly struct MassNavigationNavMeshRuntimeCoordinateMapper
{
    private MassNavigationNavMeshRuntimeCoordinateMapper(
        int worldMinXcm,
        int worldMinYcm,
        int runtimeWorldWidthCm,
        int runtimeWorldHeightCm,
        int bakedWorldWidthCm,
        int bakedWorldHeightCm,
        int runtimeTileWidthCm,
        int runtimeTileHeightCm,
        int bakedTileWidthCm,
        int bakedTileHeightCm,
        int columns,
        int rows)
    {
        WorldMinXcm = worldMinXcm;
        WorldMinYcm = worldMinYcm;
        RuntimeWorldWidthCm = runtimeWorldWidthCm;
        RuntimeWorldHeightCm = runtimeWorldHeightCm;
        BakedWorldWidthCm = bakedWorldWidthCm;
        BakedWorldHeightCm = bakedWorldHeightCm;
        RuntimeTileWidthCm = runtimeTileWidthCm;
        RuntimeTileHeightCm = runtimeTileHeightCm;
        BakedTileWidthCm = bakedTileWidthCm;
        BakedTileHeightCm = bakedTileHeightCm;
        Columns = columns;
        Rows = rows;
        Available = runtimeWorldWidthCm > 0 &&
            runtimeWorldHeightCm > 0 &&
            bakedWorldWidthCm > 0 &&
            bakedWorldHeightCm > 0 &&
            runtimeTileWidthCm > 0 &&
            runtimeTileHeightCm > 0 &&
            bakedTileWidthCm > 0 &&
            bakedTileHeightCm > 0 &&
            columns > 0 &&
            rows > 0;
    }

    public bool Available { get; }
    public int WorldMinXcm { get; }
    public int WorldMinYcm { get; }
    public int RuntimeWorldWidthCm { get; }
    public int RuntimeWorldHeightCm { get; }
    public int BakedWorldWidthCm { get; }
    public int BakedWorldHeightCm { get; }
    public int RuntimeTileWidthCm { get; }
    public int RuntimeTileHeightCm { get; }
    public int BakedTileWidthCm { get; }
    public int BakedTileHeightCm { get; }
    public int Columns { get; }
    public int Rows { get; }

    public static bool TryCreate(
        MassNavigationBakeDataDiagnostics? diagnostics,
        LogicHeightmapFileReader reader,
        out MassNavigationNavMeshRuntimeCoordinateMapper mapper)
    {
        mapper = default;
        if (diagnostics == null || reader == null)
        {
            return false;
        }

        int columns = Math.Max(1, reader.WidthInChunks);
        int rows = Math.Max(1, reader.HeightInChunks);
        int bakedTileWidthCm = Math.Max(1, reader.CellSizeXCm * LogicHeightmapChunk.ChunkSize);
        int bakedTileHeightCm = Math.Max(1, reader.CellSizeZCm * LogicHeightmapChunk.ChunkSize);
        mapper = Create(
            diagnostics,
            columns,
            rows,
            bakedTileWidthCm,
            bakedTileHeightCm);
        return mapper.Available;
    }

    public static MassNavigationNavMeshRuntimeCoordinateMapper Create(
        MassNavigationBakeDataDiagnostics diagnostics,
        int columns,
        int rows,
        int bakedTileWidthCm,
        int bakedTileHeightCm)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);
        int runtimeTileWidthCm = ResolveTileExtent(diagnostics.WorldWidthCm, columns, diagnostics.MacroChunkSizeXCm);
        int runtimeTileHeightCm = ResolveTileExtent(diagnostics.WorldHeightCm, rows, diagnostics.MacroChunkSizeYCm);
        bakedTileWidthCm = Math.Max(1, bakedTileWidthCm);
        bakedTileHeightCm = Math.Max(1, bakedTileHeightCm);

        return new MassNavigationNavMeshRuntimeCoordinateMapper(
            diagnostics.WorldMinXCm,
            diagnostics.WorldMinYCm,
            Math.Max(1, diagnostics.WorldWidthCm),
            Math.Max(1, diagnostics.WorldHeightCm),
            checked(bakedTileWidthCm * columns),
            checked(bakedTileHeightCm * rows),
            runtimeTileWidthCm,
            runtimeTileHeightCm,
            bakedTileWidthCm,
            bakedTileHeightCm,
            columns,
            rows);
    }

    public static MassNavigationNavMeshRuntimeCoordinateMapper CreateFromNavTile(
        MassNavigationBakeDataDiagnostics diagnostics,
        NavTile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        int columns = Math.Max(1, diagnostics.MacroChunkColumns);
        int rows = Math.Max(1, diagnostics.MacroChunkRows);
        int bakedTileWidthCm = ResolveBakedTileExtent(
            tile.OriginXcm,
            tile.TileId.ChunkX,
            ResolveBakedTileExtentFromGeometry(tile, axisX: true),
            diagnostics.MacroChunkSizeXCm);
        int bakedTileHeightCm = ResolveBakedTileExtent(
            tile.OriginZcm,
            tile.TileId.ChunkY,
            ResolveBakedTileExtentFromGeometry(tile, axisX: false),
            diagnostics.MacroChunkSizeYCm);
        return Create(diagnostics, columns, rows, bakedTileWidthCm, bakedTileHeightCm);
    }

    public int TileMinWorldXcm(int chunkX)
    {
        return WorldMinXcm + checked(chunkX * RuntimeTileWidthCm);
    }

    public int TileMinWorldYcm(int chunkY)
    {
        return WorldMinYcm + checked(chunkY * RuntimeTileHeightCm);
    }

    public Vector2 BakedTileLocalToWorldCm(NavTile tile, int localXcm, int localZcm)
    {
        return new Vector2(
            BakedAbsoluteToWorldXcm(tile.OriginXcm + localXcm),
            BakedAbsoluteToWorldYcm(tile.OriginZcm + localZcm));
    }

    public int BakedAbsoluteToWorldXcm(int bakedXcm)
    {
        return WorldMinXcm + ScaleCoordinate(bakedXcm, BakedWorldWidthCm, RuntimeWorldWidthCm);
    }

    public int BakedAbsoluteToWorldYcm(int bakedYcm)
    {
        return WorldMinYcm + ScaleCoordinate(bakedYcm, BakedWorldHeightCm, RuntimeWorldHeightCm);
    }

    public int WorldToBakedAbsoluteXcm(float worldXcm)
    {
        return ScaleCoordinate(
            (int)MathF.Round(worldXcm - WorldMinXcm),
            RuntimeWorldWidthCm,
            BakedWorldWidthCm);
    }

    public int WorldToBakedAbsoluteYcm(float worldYcm)
    {
        return ScaleCoordinate(
            (int)MathF.Round(worldYcm - WorldMinYcm),
            RuntimeWorldHeightCm,
            BakedWorldHeightCm);
    }

    private static int ResolveTileExtent(int worldExtentCm, int chunkCount, int fallbackCm)
    {
        if (worldExtentCm > 0 && chunkCount > 0)
        {
            return Math.Max(1, worldExtentCm / chunkCount);
        }

        return Math.Max(1, fallbackCm);
    }

    private static int ResolveBakedTileExtent(int originCm, int chunkIndex, int geometryExtentCm, int fallbackCm)
    {
        if (originCm > 0 && chunkIndex > 0)
        {
            return Math.Max(1, originCm / chunkIndex);
        }

        if (geometryExtentCm > 0)
        {
            return geometryExtentCm;
        }

        return Math.Max(1, fallbackCm);
    }

    private static int ResolveBakedTileExtentFromGeometry(NavTile tile, bool axisX)
    {
        int max = 0;
        int[] vertices = axisX ? tile.VertexXcm : tile.VertexZcm;
        for (int i = 0; i < vertices.Length; i++)
        {
            max = Math.Max(max, vertices[i]);
        }

        for (int i = 0; i < tile.Portals.Length; i++)
        {
            NavBorderPortal portal = tile.Portals[i];
            max = Math.Max(max, axisX ? portal.LeftXcm : portal.LeftZcm);
            max = Math.Max(max, axisX ? portal.RightXcm : portal.RightZcm);
        }

        return max;
    }

    private static int ScaleCoordinate(int value, int fromExtentCm, int toExtentCm)
    {
        if (fromExtentCm <= 0 || toExtentCm <= 0 || fromExtentCm == toExtentCm)
        {
            return value;
        }

        long scaled = (long)value * toExtentCm;
        long half = fromExtentCm / 2;
        return scaled >= 0
            ? (int)((scaled + half) / fromExtentCm)
            : (int)((scaled - half) / fromExtentCm);
    }
}
