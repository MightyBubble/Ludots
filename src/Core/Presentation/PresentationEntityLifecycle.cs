using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation
{
    /// <summary>
    /// 世界是否会跑呈现生命周期（发布销毁事件，再由收尾系统 Destroy）。
    /// 不声明时保持原合同：ConsumeEntity 只挂 PresentationDestroyPending，等下一帧收尾。
    /// 声明没有消费者时，ConsumeEntity 在同一次调用里走收尾销毁。
    /// </summary>
    public static class PresentationLifecycleConsumption
    {
        private static readonly ConditionalWeakTable<World, object> NoConsumer = new();

        public static void DeclareNoConsumer(World world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            NoConsumer.GetValue(world, static _ => new object());
        }

        public static bool HasNoConsumer(World world)
        {
            return world != null && NoConsumer.TryGetValue(world, out _);
        }
    }

    public static class PresentationEntityLifecycle
    {
        public static void RequestDestroy(World world, Entity entity, string diagnosticLabel)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (!world.IsAlive(entity))
            {
                return;
            }

            if (!world.Has<PresentationStableId>(entity))
            {
                throw new InvalidOperationException($"{diagnosticLabel} cannot be destroyed through presentation lifecycle without PresentationStableId.");
            }

            if (!world.Has<PresentationDestroyPending>(entity))
            {
                world.Add(entity, new PresentationDestroyPending());
            }

            if (world.Has<PresentationDestroyEventPublished>(entity))
            {
                world.Remove<PresentationDestroyEventPublished>(entity);
            }
        }
    }
}
