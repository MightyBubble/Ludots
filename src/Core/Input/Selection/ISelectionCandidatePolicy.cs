using Arch.Core;

namespace Ludots.Core.Input.Selection
{
    public interface ISelectionCandidatePolicy
    {
        bool IsSelectable(World world, Entity controller, Entity candidate);
        bool IsSameSelectionClass(World world, Entity reference, Entity candidate);
    }
}
