using System;
using Ludots.Core.Physics2D;

namespace Ludots.Core.Movement.Physics2DBridge
{
    /// <summary>
    /// 物理步后（同一帧、同一 SystemGroup 内紧随 Physics2DSimulationSystem）Drain
    /// <see cref="ContactEventQueue2D"/> 并交给 <see cref="ContactEventRouter2D"/> 分发。
    /// 这是碰撞事件的唯一生产路径消费点：queue 合同要求每帧清空，未消费会溢出抛异常。
    /// </summary>
    public sealed class ContactEventRoutingSystem2D : ISystem<float>
    {
        private readonly ContactEventQueue2D _queue;
        private readonly ContactEventRouter2D _router;

        public ContactEventRoutingSystem2D(ContactEventQueue2D queue, ContactEventRouter2D router)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _router = router ?? throw new ArgumentNullException(nameof(router));
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float deltaTime)
        {
        }

        public void Update(in float deltaTime)
        {
            _router.Dispatch(_queue.DrainEvents());
        }

        public void AfterUpdate(in float deltaTime)
        {
        }

        public void Dispose()
        {
        }
    }
}
