using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Navigation2D.Components;
using RtsDemoMod.Components;

namespace RtsDemoMod.Systems
{
    public sealed class RtsSelectionCandidatePolicy : ISelectionCandidatePolicy
    {
        public bool IsSelectable(World world, Entity controller, Entity candidate)
        {
            if (!world.IsAlive(controller) || !world.IsAlive(candidate))
            {
                return false;
            }

            if (!world.Has<WorldPositionCm>(candidate) || !world.Has<NavAgent2D>(candidate) || world.Has<RtsControllerTag>(candidate))
            {
                return false;
            }

            if (!world.TryGet(controller, out PlayerOwner controllerOwner) || !world.TryGet(candidate, out PlayerOwner candidateOwner))
            {
                return false;
            }

            return controllerOwner.PlayerId == candidateOwner.PlayerId;
        }

        public bool IsSameSelectionClass(World world, Entity reference, Entity candidate)
        {
            if (!world.IsAlive(reference) || !world.IsAlive(candidate))
            {
                return false;
            }

            if (!world.TryGet(reference, out Name referenceName) || !world.TryGet(candidate, out Name candidateName))
            {
                return false;
            }

            return string.Equals(referenceName.Value, candidateName.Value, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
