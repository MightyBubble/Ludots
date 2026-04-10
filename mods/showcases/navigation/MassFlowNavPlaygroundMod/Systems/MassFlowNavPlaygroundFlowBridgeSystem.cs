using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Navigation2D.Components;
using MassFlowNavPlaygroundMod.Components;
using MassFlowNavPlaygroundMod.Runtime;

namespace MassFlowNavPlaygroundMod.Systems
{
    internal sealed class MassFlowNavPlaygroundFlowBridgeSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly GameEngine _engine;
        private readonly CommandBuffer _commandBuffer = new();
        private static readonly QueryDescription FlowAssignmentQuery = new QueryDescription().WithAll<MassFlowNavTeamFlowAssignment>();

        public MassFlowNavPlaygroundFlowBridgeSystem(World world, GameEngine engine)
        {
            _world = world;
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose()
        {
            _commandBuffer.Dispose();
        }

        public void Update(in float t)
        {
            if (_engine.GetService(MassFlowNavPlaygroundServiceKeys.State) is not MassFlowNavPlaygroundState state ||
                !state.IsActive)
            {
                return;
            }

            foreach (ref var chunk in _world.Query(in FlowAssignmentQuery))
            {
                if (chunk.Count <= 0)
                {
                    continue;
                }

                var assignments = chunk.GetSpan<MassFlowNavTeamFlowAssignment>();
                bool hasManualTag = chunk.Has<MassFlowNavManualGoalTag>();
                bool hasBinding = chunk.Has<NavFlowBinding2D>();
                var bindings = hasBinding ? chunk.GetSpan<NavFlowBinding2D>() : default;
                ref Entity entityFirst = ref chunk.Entity(0);
                foreach (int i in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, i);
                    if (hasManualTag)
                    {
                        if (hasBinding)
                        {
                            _commandBuffer.Remove<NavFlowBinding2D>(entity);
                        }

                        continue;
                    }

                    MassFlowNavTeamFlowAssignment assignment = assignments[i];
                    var next = new NavFlowBinding2D
                    {
                        SurfaceId = assignment.SurfaceId,
                        FlowId = assignment.FlowId
                    };

                    if (!hasBinding)
                    {
                        _commandBuffer.Add(entity, next);
                        continue;
                    }

                    NavFlowBinding2D binding = bindings[i];
                    if (binding.SurfaceId != next.SurfaceId || binding.FlowId != next.FlowId)
                    {
                        _commandBuffer.Set(entity, next);
                    }
                }
            }

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(_world, dispose: true);
            }
        }
    }
}
