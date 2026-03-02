using Ludots.Core.Navigation.GraphWorld;

namespace Ludots.Core.Map.Board
{
    /// <summary>
    /// Board backed by a chunked node graph.
    /// </summary>
    public interface INodeGraphBoard : IBoard
    {
        ChunkedNodeGraphStore GraphStore { get; }
    }
}
