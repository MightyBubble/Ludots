using DotRecast.Detour;
using DotRecast.Detour.Io;
using Ludots.Core.Navigation.NavMesh;

namespace Ludots.Editor.Bridge;

public static class EditorNavTileDebugPayload
{
    public static object BuildFromDetourTileBytes(byte[] detourTileBytes)
    {
        DtMeshData data = ReadDetourTileData(detourTileBytes);
        DtMeshHeader header = data.header ?? throw new InvalidOperationException("Detour tile is missing mesh header.");
        if (data.verts == null || data.polys == null)
            throw new InvalidOperationException("Detour tile is missing vertex or polygon data.");

        int vertexCount = header.vertCount;
        var vertexXcm = new int[vertexCount];
        var vertexYcm = new int[vertexCount];
        var vertexZcm = new int[vertexCount];
        int originXcm = ToCentimeters(header.bmin.X);
        int originZcm = ToCentimeters(header.bmin.Z);

        for (int i = 0; i < vertexCount; i++)
        {
            int src = i * 3;
            vertexXcm[i] = ToCentimeters(data.verts[src + 0]) - originXcm;
            vertexYcm[i] = ToCentimeters(data.verts[src + 1]);
            vertexZcm[i] = ToCentimeters(data.verts[src + 2]) - originZcm;
        }

        var triA = new List<int>(Math.Max(0, header.polyCount));
        var triB = new List<int>(Math.Max(0, header.polyCount));
        var triC = new List<int>(Math.Max(0, header.polyCount));
        var n0 = new List<int>(Math.Max(0, header.polyCount));
        var n1 = new List<int>(Math.Max(0, header.polyCount));
        var n2 = new List<int>(Math.Max(0, header.polyCount));
        var triAreaIds = new List<int>(Math.Max(0, header.polyCount));

        for (int i = 0; i < header.polyCount; i++)
        {
            DtPoly poly = data.polys[i];
            if (poly == null || poly.vertCount < 3) continue;

            int areaId = checked((byte)poly.GetArea());
            int first = poly.verts[0];
            for (int j = 1; j < poly.vertCount - 1; j++)
            {
                triA.Add(first);
                triB.Add(poly.verts[j]);
                triC.Add(poly.verts[j + 1]);
                n0.Add(-1);
                n1.Add(-1);
                n2.Add(-1);
                triAreaIds.Add(areaId);
            }
        }

        return new
        {
            tileId = new { chunkX = header.x, chunkY = header.y, layer = header.layer },
            tileVersion = NavTileBinary.FormatVersion,
            originXcm,
            originZcm,
            vertexXcm,
            vertexYcm,
            vertexZcm,
            triA = triA.ToArray(),
            triB = triB.ToArray(),
            triC = triC.ToArray(),
            n0 = n0.ToArray(),
            n1 = n1.ToArray(),
            n2 = n2.ToArray(),
            triAreaIds = triAreaIds.ToArray(),
            portals = Array.Empty<object>()
        };
    }

    private static DtMeshData ReadDetourTileData(byte[] detourTileBytes)
    {
        if (detourTileBytes == null || detourTileBytes.Length == 0)
            throw new InvalidOperationException("Detour tile payload is empty.");

        using var ms = new MemoryStream(detourTileBytes);
        using var br = new BinaryReader(ms);
        return new DtMeshDataReader().Read(br, DtDetour.DT_VERTS_PER_POLYGON);
    }

    private static int ToCentimeters(float meters)
    {
        return checked((int)Math.Round(meters * 100.0, MidpointRounding.AwayFromZero));
    }
}
