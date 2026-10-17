using Ludots.Core.Navigation.NavMesh;

namespace Ludots.Core.Map.Board
{
    public interface INavigableBoard : IBoard
    {
        NavQueryServiceRegistry NavServices { get; set; }
    }
}
