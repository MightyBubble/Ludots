using Ludots.Core.Navigation.NavMesh;

namespace Ludots.Core.Map.Board
{
    /// <summary>
    /// Board with navigation support.
    /// </summary>
    public interface INavigableBoard : IBoard
    {
        NavQueryServiceRegistry NavServices { get; }
    }
}
