using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    public static class RelationOps
    {
        public const string ChildrenCapacityExceededError = "GAS.RELATION.ERR.ChildrenCapacityExceeded";
        public const string CycleDetectedError = "GAS.RELATION.ERR.CycleDetected";

        public static void SetParent(World world, Entity child, Entity parent)
        {
            if (!world.IsAlive(child) || !world.IsAlive(parent)) return;
            if (child == parent)
            {
                throw new InvalidOperationException("GAS.RELATION.ERR.SelfParent");
            }

            if (WouldCreateCycle(world, child, parent))
            {
                throw new InvalidOperationException(
                    $"{CycleDetectedError}: child={child.Id}, parent={parent.Id}.");
            }

            if (world.Has<ChildOf>(child))
            {
                ref var old = ref world.Get<ChildOf>(child);
                if (old.Parent.Equals(parent)) return;
            }

            if (world.Has<ChildrenBuffer>(parent))
            {
                ref ChildrenBuffer destination = ref world.Get<ChildrenBuffer>(parent);
                if (!destination.Contains(in child) &&
                    destination.Count >= GasConstants.MAX_CHILDREN_BUFFER_CAPACITY)
                {
                    throw new InvalidOperationException(
                        $"{ChildrenCapacityExceededError}: parent={parent.Id}, capacity={GasConstants.MAX_CHILDREN_BUFFER_CAPACITY}.");
                }
            }

            if (world.Has<ChildOf>(child))
            {
                RemoveParent(world, child);
            }

            if (!world.Has<ChildrenBuffer>(parent)) world.Add(parent, new ChildrenBuffer());
            ref var children = ref world.Get<ChildrenBuffer>(parent);
            if (!children.Add(in child))
            {
                throw new InvalidOperationException(
                    $"{ChildrenCapacityExceededError}: parent={parent.Id}, capacity={GasConstants.MAX_CHILDREN_BUFFER_CAPACITY}.");
            }
            world.Add(child, new ChildOf { Parent = parent });
        }

        /// <summary>
        /// 环检测：从候选 parent 沿 ChildOf 向上走，途中遇到 child 即成环。
        /// Floyd 双指针同时检测既有病态环，不以合法关系深度作为失败条件。
        /// </summary>
        public static bool WouldCreateCycle(World world, Entity child, Entity parent)
        {
            Entity current = parent;
            Entity slow = parent;
            Entity fast = parent;
            while (world.IsAlive(current) && world.Has<ChildOf>(current))
            {
                if (current == child)
                {
                    return true;
                }

                current = world.Get<ChildOf>(current).Parent;
                slow = NextParent(world, slow);
                fast = NextParent(world, NextParent(world, fast));
                if (slow != Entity.Null && slow == fast)
                {
                    throw new InvalidOperationException(
                        $"{CycleDetectedError}: existing ChildOf graph contains a cycle.");
                }
            }

            return current == child;
        }

        private static Entity NextParent(World world, Entity entity)
        {
            return world.IsAlive(entity) && world.Has<ChildOf>(entity)
                ? world.Get<ChildOf>(entity).Parent
                : Entity.Null;
        }

        public static void RemoveParent(World world, Entity child)
        {
            if (!world.IsAlive(child)) return;
            if (!world.Has<ChildOf>(child)) return;

            var parent = world.Get<ChildOf>(child).Parent;
            if (world.IsAlive(parent) && world.Has<ChildrenBuffer>(parent))
            {
                ref var children = ref world.Get<ChildrenBuffer>(parent);
                children.Remove(in child);
            }

            world.Remove<ChildOf>(child);
        }
    }
}
