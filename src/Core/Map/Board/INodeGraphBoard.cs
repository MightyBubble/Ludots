using Ludots.Core.Navigation.GraphWorld;

namespace Ludots.Core.Map.Board
{
    public interface INodeGraphBoard : IBoard
    {
        ChunkedNodeGraphStore GraphStore { get; }
        LoadedGraphRuntime GraphRuntime { get; }
    }
}
