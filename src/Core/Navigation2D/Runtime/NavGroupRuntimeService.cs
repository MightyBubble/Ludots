using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;

namespace Ludots.Core.Navigation2D.Runtime
{
    public sealed class NavGroupRuntimeService
    {
        private readonly World _world;
        private readonly Dictionary<int, Entity> _groups = new();
        private int _nextGroupId = 1;

        public NavGroupRuntimeService(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public Entity IssueMoveCommand(Entity owner, ReadOnlySpan<Entity> members, int teamId, Fix64Vec2 targetCm, Fix64 radiusCm, int formationSpacingCm, Fix64 rotationRad)
        {
            int groupId = ResolveExistingUniformGroupId(members);
            if (groupId <= 0)
            {
                groupId = _nextGroupId++;
            }

            Entity group = EnsureGroupEntity(owner, groupId, teamId);
            var target = new NavGroupTarget2D
            {
                TargetCm = targetCm,
                RadiusCm = radiusCm,
                FormationSpacingCm = formationSpacingCm,
                RotationRad = rotationRad,
            };

            if (_world.Has<NavGroupTarget2D>(group))
            {
                _world.Set(group, target);
            }
            else
            {
                _world.Add(group, target);
            }

            var runtimeState = _world.Has<NavGroupRuntimeState>(group)
                ? _world.Get<NavGroupRuntimeState>(group)
                : default;
            runtimeState.IsDirty = 1;
            runtimeState.MemberCount = 0;
            _world.Set(group, runtimeState);

            int slotIndex = 0;
            for (int i = 0; i < members.Length; i++)
            {
                Entity member = members[i];
                if (member == Entity.Null || !_world.IsAlive(member))
                {
                    continue;
                }

                var groupMember = new NavGroupMember
                {
                    GroupId = groupId,
                    SlotIndex = slotIndex++,
                };

                if (_world.Has<NavGroupMember>(member))
                {
                    _world.Set(member, groupMember);
                }
                else
                {
                    _world.Add(member, groupMember);
                }
            }

            return group;
        }

        public bool TryResolveGroupEntity(int groupId, out Entity entity)
        {
            if (_groups.TryGetValue(groupId, out entity) && _world.IsAlive(entity))
            {
                return true;
            }

            entity = Entity.Null;
            return false;
        }

        public void ForgetGroup(int groupId)
        {
            _groups.Remove(groupId);
        }

        private Entity EnsureGroupEntity(Entity owner, int groupId, int teamId)
        {
            if (TryResolveGroupEntity(groupId, out Entity group))
            {
                UpdateGroupMetadata(group, owner, teamId, groupId);
                return group;
            }

            group = _world.Create(
                new NavGroupTag(),
                new NavGroupIdentity { GroupId = groupId },
                new NavGroupOwner { Value = owner },
                new NavGroupTeam { TeamId = teamId },
                new NavGroupRuntimeState
                {
                    SolverModeValue = (byte)NavSolverMode.Hybrid,
                    IsDirty = 1,
                });
            _groups[groupId] = group;
            return group;
        }

        private void UpdateGroupMetadata(Entity group, Entity owner, int teamId, int groupId)
        {
            _world.Set(group, new NavGroupIdentity { GroupId = groupId });
            _world.Set(group, new NavGroupOwner { Value = owner });
            _world.Set(group, new NavGroupTeam { TeamId = teamId });
        }

        private int ResolveExistingUniformGroupId(ReadOnlySpan<Entity> members)
        {
            int uniformGroupId = 0;
            bool foundAny = false;
            for (int i = 0; i < members.Length; i++)
            {
                Entity member = members[i];
                if (member == Entity.Null || !_world.IsAlive(member) || !_world.Has<NavGroupMember>(member))
                {
                    return 0;
                }

                int groupId = _world.Get<NavGroupMember>(member).GroupId;
                if (!foundAny)
                {
                    uniformGroupId = groupId;
                    foundAny = true;
                    continue;
                }

                if (uniformGroupId != groupId)
                {
                    return 0;
                }
            }

            return foundAny ? uniformGroupId : 0;
        }
    }
}
