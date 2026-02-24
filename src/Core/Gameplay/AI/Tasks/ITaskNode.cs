using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.AI.Tasks
{
    public interface ITaskNode
    {
        TaskNodeStatus Tick(World world, Entity entity, ref BlackboardIntBuffer ints, ref BlackboardEntityBuffer entities);
    }
}

