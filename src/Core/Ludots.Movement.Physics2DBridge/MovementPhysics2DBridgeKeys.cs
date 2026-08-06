using Ludots.Core.Scripting;

namespace Ludots.Core.Movement.Physics2DBridge
{
    /// <summary>massnav→kinematic 桥的服务键（消费者注册与可观测状态查询入口）。</summary>
    public static class MovementPhysics2DBridgeKeys
    {
        public static readonly ServiceKey<ContactEventRouter2D> ContactEventRouter =
            new("MovementPhysics2DBridge.ContactEventRouter");

        public static readonly ServiceKey<MassNavKinematicPoseFeedSystem2D> KinematicPoseFeedSystem =
            new("MovementPhysics2DBridge.KinematicPoseFeedSystem");
    }
}
