using System;
using System.Collections.Generic;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;

namespace Ludots.Core.Navigation2D.Systems
{
    public sealed class NavGroupMaintenanceSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription MemberQuery = new QueryDescription().WithAll<NavGroupMember>();
        private static readonly QueryDescription GroupQuery = new QueryDescription().WithAll<NavGroupTag, NavGroupIdentity, NavGroupRuntimeState>();

        private readonly NavGroupRuntimeService _groups;
        private readonly Dictionary<int, int> _memberCounts = new();
        private readonly CommandBuffer _commandBuffer = new();

        public NavGroupMaintenanceSystem(World world, NavGroupRuntimeService groups) : base(world)
        {
            _groups = groups ?? throw new ArgumentNullException(nameof(groups));
        }

        public override void Update(in float dt)
        {
            _memberCounts.Clear();

            foreach (ref var chunk in World.Query(in MemberQuery))
            {
                Span<NavGroupMember> members = chunk.GetSpan<NavGroupMember>();
                foreach (int index in chunk)
                {
                    int groupId = members[index].GroupId;
                    if (groupId <= 0)
                    {
                        continue;
                    }

                    _memberCounts.TryGetValue(groupId, out int current);
                    _memberCounts[groupId] = current + 1;
                }
            }

            foreach (ref var chunk in World.Query(in GroupQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                Span<NavGroupIdentity> identities = chunk.GetSpan<NavGroupIdentity>();
                Span<NavGroupRuntimeState> runtimeStates = chunk.GetSpan<NavGroupRuntimeState>();

                foreach (int index in chunk)
                {
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    int groupId = identities[index].GroupId;
                    _memberCounts.TryGetValue(groupId, out int memberCount);

                    if (memberCount <= 0)
                    {
                        _groups.ForgetGroup(groupId);
                        _commandBuffer.Destroy(entity);
                        continue;
                    }

                    ref NavGroupRuntimeState runtimeState = ref runtimeStates[index];
                    if (runtimeState.MemberCount != memberCount)
                    {
                        runtimeState.MemberCount = memberCount;
                        runtimeState.IsDirty = 1;
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
