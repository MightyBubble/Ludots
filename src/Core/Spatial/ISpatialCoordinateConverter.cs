using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial
{
    public interface ISpatialCoordinateConverter
    {
        int GridCellSizeCm { get; }

        WorldCmInt2 GridToWorld(in IntVector2 grid);

        IntVector2 WorldToGrid(in WorldCmInt2 world);

        WorldCmInt2 HexToWorld(in HexCoordinates hex);

        HexCoordinates WorldToHex(in WorldCmInt2 world);
    }
}
