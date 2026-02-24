using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.AI.Tasks;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.AI.Systems
{
    public sealed class TaskNodeExecutionSystem : BaseSystem<World, float>
    {
        private readonly TaskNodeRegistry _registry;

        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<AIAgent, AITaskNodeInstance, BlackboardIntBuffer, BlackboardEntityBuffer>();

        public TaskNodeExecutionSystem(World world, TaskNodeRegistry registry)
            : base(world)
        {
            _registry = registry;
        }

        public override void Update(in float dt)
        {
            var job = new TickJob(World, _registry);
            World.InlineEntityQuery<TickJob, AIAgent, AITaskNodeInstance, BlackboardIntBuffer, BlackboardEntityBuffer>(in _query, ref job);
        }

        private struct TickJob : IForEachWithEntity<AIAgent, AITaskNodeInstance, BlackboardIntBuffer, BlackboardEntityBuffer>
        {
            private readonly World _world;
            private readonly TaskNodeRegistry _registry;

            public TickJob(World world, TaskNodeRegistry registry)
            {
                _world = world;
                _registry = registry;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref AIAgent agent, ref AITaskNodeInstance task, ref BlackboardIntBuffer ints, ref BlackboardEntityBuffer entities)
            {
                if (task.Status != TaskNodeStatus.Running) return;
                if (!_registry.TryGet(task.NodeId, out var node))
                {
                    task.Status = TaskNodeStatus.Failure;
                    return;
                }
                task.Status = node.Tick(_world, entity, ref ints, ref entities);
            }
        }
    }
}
