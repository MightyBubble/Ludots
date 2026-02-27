using Ludots.Core.Map.Hex;

namespace Ludots.Core.Map.Board
{
    /// <summary>
    /// Board with terrain data (VertexMap).
    /// </summary>
    public interface ITerrainBoard : IBoard
    {
        VertexMap VertexMap { get; set; }
    }
}
