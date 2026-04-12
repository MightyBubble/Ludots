using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;

namespace Ludots.Core.Navigation2D.Systems
{
    public sealed class NavGroupFlowDomainAssignmentSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription GroupQuery = new QueryDescription()
            .WithAll<NavGroupTag, NavGroupIdentity, NavGroupTarget2D, NavGroupRuntimeState>();

        private static readonly QueryDescription MemberQuery = new QueryDescription()
            .WithAll<NavGroupMember>();

        private readonly Navigation2DRuntime _runtime;
        private readonly List<Navigation2DFlowDomainRequest> _requests = new(128);
        private readonly Dictionary<int, int> _flowIdByGroup = new();
        private readonly CommandBuffer _commandBuffer = new();
        private int _assignmentTick;

        public NavGroupFlowDomainAssignmentSystem(World world, Navigation2DRuntime runtime) : base(world)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public override void Update(in float dt)
        {
            _requests.Clear();
            _flowIdByGroup.Clear();

            Navigation2DFlowDomainPool? pool = _runtime.FlowDomains;
            if (_runtime.FlowEnabled &&
                pool != null &&
                pool.Enabled)
            {
                foreach (ref var chunk in World.Query(in GroupQuery))
                {
                    Span<NavGroupIdentity> identities = chunk.GetSpan<NavGroupIdentity>();
                    Span<NavGroupTarget2D> targets = chunk.GetSpan<NavGroupTarget2D>();
                    Span<NavGroupRuntimeState> states = chunk.GetSpan<NavGroupRuntimeState>();

                    foreach (int index in chunk)
                    {
                        NavGroupRuntimeState state = states[index];
                        if (state.SolverMode == NavSolverMode.PreciseOrca)
                        {
                            continue;
                        }

                        _requests.Add(new Navigation2DFlowDomainRequest(
                            identities[index].GroupId,
                            targets[index].TargetCm,
                            pool.DefaultProfileIndex,
                            Math.Max(1, state.MemberCount)));
                    }
                }

                pool.ResolveAssignments(CollectionsMarshal.AsSpan(_requests), ++_assignmentTick);
                for (int i = 0; i < _requests.Count; i++)
                {
                    Navigation2DFlowDomainRequest request = _requests[i];
                    if (pool.TryGetAssignedFlowId(request.OwnerId, out int flowId))
                    {
                        _flowIdByGroup[request.OwnerId] = flowId;
                    }
                }
            }
            else
            {
                _assignmentTick++;
            }

            foreach (ref var chunk in World.Query(in MemberQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                Span<NavGroupMember> members = chunk.GetSpan<NavGroupMember>();
                bool hasFlowBinding = chunk.Has<NavFlowBinding2D>();
                Span<NavFlowBinding2D> flowBindings = hasFlowBinding
                    ? chunk.GetSpan<NavFlowBinding2D>()
                    : default;

                foreach (int index in chunk)
                {
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    NavGroupMember member = members[index];
                    if (_flowIdByGroup.TryGetValue(member.GroupId, out int flowId))
                    {
                        var binding = new NavFlowBinding2D
                        {
                            SurfaceId = 0,
                            FlowId = flowId,
                        };

                        if (!hasFlowBinding)
                        {
                            _commandBuffer.Add(entity, binding);
                        }
                        else
                        {
                            flowBindings[index] = binding;
                        }
                    }
                    else if (hasFlowBinding)
                    {
                        _commandBuffer.Remove<NavFlowBinding2D>(entity);
                    }
                }
            }

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }
    }
}
