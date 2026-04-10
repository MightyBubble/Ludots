using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Navigation2D.Components;
using MassFlowNavPlaygroundMod.Components;

namespace MassFlowNavPlaygroundMod.Runtime
{
    internal enum MassFlowFormationMode : byte
    {
        None = 0,
        Line = 1,
        Square = 2,
        Circle = 3,
        Wedge = 4
    }

    internal sealed class MassFlowFormationGroup
    {
        public int GroupId { get; init; }
        public MassFlowFormationMode Mode { get; init; }
        public List<Entity> Members { get; } = new();
        public List<Vector2> BaseOffsetsCm { get; } = new();
        public List<Vector2> OffsetsCm { get; } = new();
        public Vector2 DestinationCm;
        public Vector2 CentroidCm;
        public float RotationRad;
        public bool Arrived;

        public void RecomputeOffsets()
        {
            OffsetsCm.Clear();
            float cos = MathF.Cos(RotationRad);
            float sin = MathF.Sin(RotationRad);
            for (int i = 0; i < BaseOffsetsCm.Count; i++)
            {
                Vector2 offset = BaseOffsetsCm[i];
                OffsetsCm.Add(new Vector2(
                    offset.X * cos - offset.Y * sin,
                    offset.X * sin + offset.Y * cos));
            }
        }
    }

    internal sealed class MassFlowNavPlaygroundState
    {
        private static readonly string[] KnownNavGapLines =
        {
            "Flow runtime still hardcodes exactly 2 shared flows.",
            "Flow-bound point goals feed back into shared flow and can pollute lane intent.",
            "Static obstacle avoidance is mostly flow stamping; ORCA/Sonar still do not model an explicit obstacle array like the external reference.",
            "Flow obstacle and crowd stamps use createTilesIfMissing=false, so inactive tiles can miss obstacle or density pressure.",
            "Hybrid ORCA switching and current acceptance coverage are still too weak for 20k-scale regressions."
        };

        public string ActiveMapId { get; private set; } = string.Empty;
        public bool IsActive { get; private set; }
        public int DesiredUnitCount { get; set; } = 20000;
        public int SelectedTeamFlowId { get; set; }
        public int FriendlyCount { get; private set; }
        public int EnemyCount { get; private set; }
        public int ManualCount { get; private set; }
        public int PanelDirtyVersion { get; private set; }
        public MassFlowFormationMode FormationMode { get; set; } = MassFlowFormationMode.None;
        public float RotationSpeedRadPerSec { get; set; } = 2.5f;
        public float FormationSpacingCm { get; set; } = 180f;
        public int FormationFrameIndex { get; set; }
        public string LastCommandDebug { get; set; } = "cmd:init";
        public string LastCommandInputDebug { get; set; } = "input:init";
        public string LastMotionProbeDebug { get; set; } = "probe:init";
        public Entity MotionProbeEntity { get; private set; }
        public int MotionProbeFramesRemaining { get; private set; }
        public Entity ControllerEntity { get; set; }
        public Entity SceneRootEntity { get; set; }
        public Entity Team0FlowGoalEntity { get; set; }
        public Entity Team1FlowGoalEntity { get; set; }
        public Dictionary<int, MassFlowFormationGroup> Groups { get; } = new();
        public int NextGroupId { get; private set; } = 1;
        public IReadOnlyList<string> KnownNavGaps => KnownNavGapLines;
        private int[] _affectedGroupScratch = Array.Empty<int>();

        public void Activate(string mapId)
        {
            ActiveMapId = mapId ?? string.Empty;
            IsActive = true;
            MarkPanelDirty();
        }

        public void Deactivate()
        {
            ActiveMapId = string.Empty;
            IsActive = false;
            ResetSceneState();
        }

        public void ResetSceneState()
        {
            ControllerEntity = Entity.Null;
            SceneRootEntity = Entity.Null;
            Team0FlowGoalEntity = Entity.Null;
            Team1FlowGoalEntity = Entity.Null;
            FriendlyCount = 0;
            EnemyCount = 0;
            ManualCount = 0;
            Groups.Clear();
            NextGroupId = 1;
            FormationFrameIndex = 0;
            LastCommandDebug = "cmd:scene-reset";
            LastCommandInputDebug = "input:scene-reset";
            LastMotionProbeDebug = "probe:scene-reset";
            MotionProbeEntity = Entity.Null;
            MotionProbeFramesRemaining = 0;
            MarkPanelDirty();
        }

        public void SetPopulationCounts(int friendlyCount, int enemyCount)
        {
            FriendlyCount = Math.Max(0, friendlyCount);
            EnemyCount = Math.Max(0, enemyCount);
            MarkPanelDirty();
        }

        public void IncrementManualCount()
        {
            AddManualCount(1);
        }

        public void DecrementManualCount()
        {
            AddManualCount(-1);
        }

        public void AddManualCount(int delta)
        {
            if (delta == 0)
            {
                return;
            }

            int next = ManualCount + delta;
            ManualCount = next <= 0 ? 0 : next;
            MarkPanelDirty();
        }

        public void MarkPanelDirty()
        {
            PanelDirtyVersion++;
        }

        public bool TryGetFlowGoalEntity(int flowId, out Entity entity)
        {
            entity = flowId == 0 ? Team0FlowGoalEntity : Team1FlowGoalEntity;
            return entity != Entity.Null;
        }

        public MassFlowFormationGroup CreateGroup(
            ReadOnlySpan<Entity> members,
            ReadOnlySpan<Vector2> baseOffsetsCm,
            Vector2 destinationCm,
            float preservedRotationRad,
            MassFlowFormationMode mode)
        {
            var group = new MassFlowFormationGroup
            {
                GroupId = NextGroupId++,
                DestinationCm = destinationCm,
                RotationRad = preservedRotationRad,
                Mode = mode
            };

            for (int i = 0; i < members.Length; i++)
            {
                group.Members.Add(members[i]);
                group.BaseOffsetsCm.Add(baseOffsetsCm[i]);
            }

            group.RecomputeOffsets();
            Groups[group.GroupId] = group;
            return group;
        }

        public bool TryGetGroup(int groupId, out MassFlowFormationGroup group)
        {
            return Groups.TryGetValue(groupId, out group!);
        }

        public void ArmMotionProbe(Entity entity, int frames)
        {
            if (entity == Entity.Null || frames <= 0)
            {
                return;
            }

            MotionProbeEntity = entity;
            MotionProbeFramesRemaining = Math.Max(MotionProbeFramesRemaining, frames);
            LastMotionProbeDebug = $"probe:armed #{entity.Id}";
        }

        public void AdvanceMotionProbe()
        {
            if (MotionProbeFramesRemaining <= 0)
            {
                MotionProbeEntity = Entity.Null;
                MotionProbeFramesRemaining = 0;
                return;
            }

            MotionProbeFramesRemaining--;
            if (MotionProbeFramesRemaining <= 0)
            {
                MotionProbeEntity = Entity.Null;
                MotionProbeFramesRemaining = 0;
            }
        }

        public void DissolveGroup(World world, int groupId)
        {
            if (!Groups.Remove(groupId, out MassFlowFormationGroup? group))
            {
                return;
            }

            for (int i = 0; i < group.Members.Count; i++)
            {
                Entity member = group.Members[i];
                if (!world.IsAlive(member))
                {
                    continue;
                }

                if (world.Has<MassFlowNavFormationMember>(member))
                {
                    world.Remove<MassFlowNavFormationMember>(member);
                }

                TryRemoveManualTag(world, member);

                if (world.TryGet(member, out NavGoal2D goal))
                {
                    goal.Kind = NavGoalKind2D.None;
                    world.Set(member, goal);
                }
            }
        }

        public float RemoveEntitiesFromGroups(World world, ReadOnlySpan<Entity> selectedEntities)
        {
            float preservedRotation = 0f;
            EnsureAffectedGroupCapacity(selectedEntities.Length);
            int affectedGroupCount = 0;
            for (int i = 0; i < selectedEntities.Length; i++)
            {
                Entity entity = selectedEntities[i];
                if (!world.IsAlive(entity) || !world.TryGet(entity, out MassFlowNavFormationMember member))
                {
                    continue;
                }

                if (Groups.TryGetValue(member.GroupId, out MassFlowFormationGroup? existing))
                {
                    preservedRotation = existing.RotationRad;
                }

                if (!ContainsAffectedGroup(affectedGroupCount, member.GroupId))
                {
                    _affectedGroupScratch[affectedGroupCount++] = member.GroupId;
                }

                if (world.Has<MassFlowNavFormationMember>(entity))
                {
                    world.Remove<MassFlowNavFormationMember>(entity);
                }
            }

            for (int affectedIndex = 0; affectedIndex < affectedGroupCount; affectedIndex++)
            {
                int groupId = _affectedGroupScratch[affectedIndex];
                if (!Groups.TryGetValue(groupId, out MassFlowFormationGroup? group))
                {
                    continue;
                }

                int removedManualCount = 0;
                for (int index = group.Members.Count - 1; index >= 0; index--)
                {
                    Entity member = group.Members[index];
                    if (!ContainsSortedEntity(selectedEntities, member))
                    {
                        continue;
                    }

                    group.Members.RemoveAt(index);
                    group.BaseOffsetsCm.RemoveAt(index);
                    if (index < group.OffsetsCm.Count)
                    {
                        group.OffsetsCm.RemoveAt(index);
                    }

                    if (world.IsAlive(member) && TryRemoveManualTag(world, member))
                    {
                        removedManualCount++;
                    }

                    if (world.IsAlive(member) && world.TryGet(member, out NavGoal2D goal))
                    {
                        goal.Kind = NavGoalKind2D.None;
                        world.Set(member, goal);
                    }
                }

                if (group.Members.Count <= 1)
                {
                    DissolveGroup(world, groupId);
                    continue;
                }

                group.RecomputeOffsets();
                for (int i = 0; i < group.Members.Count; i++)
                {
                    Entity member = group.Members[i];
                    if (!world.IsAlive(member) || !world.Has<MassFlowNavFormationMember>(member))
                    {
                        continue;
                    }

                    ref var formationMember = ref world.Get<MassFlowNavFormationMember>(member);
                    formationMember.GroupId = groupId;
                    formationMember.SlotIndex = i;
                    world.Set(member, formationMember);
                }

                if (removedManualCount > 0)
                {
                    AddManualCount(-removedManualCount);
                }
            }

            return preservedRotation;
        }

        public bool CompactGroup(World world, int groupId)
        {
            if (!Groups.TryGetValue(groupId, out MassFlowFormationGroup? group))
            {
                return false;
            }

            for (int index = group.Members.Count - 1; index >= 0; index--)
            {
                Entity member = group.Members[index];
                if (world.IsAlive(member))
                {
                    continue;
                }

                group.Members.RemoveAt(index);
                group.BaseOffsetsCm.RemoveAt(index);
                if (index < group.OffsetsCm.Count)
                {
                    group.OffsetsCm.RemoveAt(index);
                }
            }

            if (group.Members.Count <= 1)
            {
                DissolveGroup(world, groupId);
                return false;
            }

            if (group.OffsetsCm.Count != group.BaseOffsetsCm.Count)
            {
                group.RecomputeOffsets();
            }

            return true;
        }

        private bool TryRemoveManualTag(World world, Entity entity)
        {
            if (!world.IsAlive(entity) || !world.Has<MassFlowNavManualGoalTag>(entity))
            {
                return false;
            }

            world.Remove<MassFlowNavManualGoalTag>(entity);
            RestoreSharedFlowBinding(world, entity);
            SetSmartStopSuppressed(world, entity, suppressed: false);
            return true;
        }

        private static void RestoreSharedFlowBinding(World world, Entity entity)
        {
            if (!world.IsAlive(entity) || !world.TryGet(entity, out Team team))
            {
                return;
            }

            var binding = new NavFlowBinding2D
            {
                SurfaceId = 0,
                FlowId = MassFlowNavPlaygroundIds.ResolveFlowIdForTeam(team.Id)
            };

            if (world.Has<NavFlowBinding2D>(entity))
            {
                world.Set(entity, binding);
            }
            else
            {
                world.Add(entity, binding);
            }
        }

        private static void SetSmartStopSuppressed(World world, Entity entity, bool suppressed)
        {
            if (!world.IsAlive(entity) || !world.Has<NavAgent2D>(entity))
            {
                return;
            }

            ref var navAgent = ref world.Get<NavAgent2D>(entity);
            navAgent.SmartStopSuppressed = suppressed ? (byte)1 : (byte)0;
        }

        private void EnsureAffectedGroupCapacity(int required)
        {
            if (required <= _affectedGroupScratch.Length)
            {
                return;
            }

            int nextSize = _affectedGroupScratch.Length == 0 ? 8 : _affectedGroupScratch.Length;
            while (nextSize < required)
            {
                nextSize *= 2;
            }

            Array.Resize(ref _affectedGroupScratch, nextSize);
        }

        private bool ContainsAffectedGroup(int count, int groupId)
        {
            for (int i = 0; i < count; i++)
            {
                if (_affectedGroupScratch[i] == groupId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsSortedEntity(ReadOnlySpan<Entity> selectedEntities, Entity target)
        {
            int left = 0;
            int right = selectedEntities.Length - 1;
            while (left <= right)
            {
                int mid = left + ((right - left) >> 1);
                int compare = CompareEntities(selectedEntities[mid], target);
                if (compare == 0)
                {
                    return true;
                }

                if (compare < 0)
                {
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

            return false;
        }

        private static int CompareEntities(Entity a, Entity b)
        {
            int worldCompare = a.WorldId.CompareTo(b.WorldId);
            return worldCompare != 0 ? worldCompare : a.Id.CompareTo(b.Id);
        }
    }
}
