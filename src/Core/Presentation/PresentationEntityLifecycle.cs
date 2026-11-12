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
    /// 声明和安装互斥，没有撤销。系统 Dispose 不清除已安装标记。
    /// </summary>
    public static class PresentationLifecycleConsumption
    {
        private static readonly ConditionalWeakTable<World, object> NoConsumer = new();
        private static readonly ConditionalWeakTable<World, object> ConsumerInstalled = new();

        public static void DeclareNoConsumer(World world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (ConsumerInstalled.TryGetValue(world, out _))
            {
                throw new InvalidOperationException(
                    "LIFECYCLE.ERR.ConsumerAlreadyInstalled: DeclareNoConsumer cannot run after a presentation lifecycle consumer is installed on this world. There is no revoke.");
            }

            NoConsumer.GetValue(world, static _ => new object());
        }

        public static bool HasNoConsumer(World world)
        {
            return world != null && NoConsumer.TryGetValue(world, out _);
        }

        internal static void InstallConsumer(World world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (NoConsumer.TryGetValue(world, out _))
            {
                throw new InvalidOperationException(
                    "LIFECYCLE.ERR.NoConsumerDeclared: this world declared no presentation lifecycle consumer. Installing a lifecycle system is rejected. There is no revoke.");
            }

            ConsumerInstalled.GetValue(world, static _ => new object());
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
