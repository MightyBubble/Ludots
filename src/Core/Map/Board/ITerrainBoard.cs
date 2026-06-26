using Ludots.Core.Map.Hex;
using Ludots.Core.Map.Fields;

namespace Ludots.Core.Map.Board
{
    /// <summary>
    /// Board with logic terrain data. VertexMap is retained for hex terrain consumers.
    /// </summary>
    public interface ITerrainBoard : IBoard
    {
        VertexMap VertexMap { get; set; }

        LogicTerrainField LogicTerrain { get; set; }
    }
}
