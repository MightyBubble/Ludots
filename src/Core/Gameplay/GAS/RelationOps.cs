using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    public static class RelationOps
    {
        public static void SetParent(World world, Entity child, Entity parent)
        {
            if (!world.IsAlive(child) || !world.IsAlive(parent)) return;

            if (world.Has<ChildOf>(child))
            {
                ref var old = ref world.Get<ChildOf>(child);
                if (old.Parent.Equals(parent)) return;
                RemoveParent(world, child);
            }

            if (!world.Has<ChildrenBuffer>(parent)) world.Add(parent, new ChildrenBuffer());
            ref var children = ref world.Get<ChildrenBuffer>(parent);
            if (children.Add(in child))
            {
                world.Add(child, new ChildOf { Parent = parent });
            }
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
