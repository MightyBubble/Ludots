using System;
using Ludots.Core.Engine;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Ticking;
using Ludots.Core.Scripting;

namespace Ludots.Core.Movement.Physics2DBridge
{
    /// <summary>
    /// massnav→kinematic 桥安装入口。GameEngine 在注册 Physics2D 系统后反射调用
    /// （Physics2D 在场时桥为必装项，缺装即启动失败——不允许静默缺喂送）。
    ///
    /// 系统落位：
    /// - <see cref="MassNavKinematicPoseFeedSystem2D"/> 插到 InputCollection 组
    ///   Physics2DSimulationSystem 之前：喂送发生在上一固定步全部写权系统
    ///   （PostMovement 的 massnav entity-sync、EffectProcessing 的位移窗口）提交之后、
    ///   本固定步物理消费之前，同帧即被 KinematicDriveSystem2D 消费。
    /// - <see cref="ContactEventRoutingSystem2D"/> 追加在 InputCollection 组
    ///   Physics2DSimulationSystem 之后：满足「物理写、同帧 gameplay Drain」的 queue 合同。
    /// </summary>
    public static class MovementPhysics2DBridgeInstaller
    {
        public static void Install(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);

            KinematicTargetPoseBuffer2D poseBuffer = engine.GetService(CoreServiceKeys.Physics2DKinematicPoseBuffer)
                ?? throw new InvalidOperationException("massnav→kinematic bridge requires the Physics2D kinematic pose buffer service.");
            ContactEventQueue2D contactEvents = engine.GetService(CoreServiceKeys.Physics2DContactEvents)
                ?? throw new InvalidOperationException("massnav→kinematic bridge requires the Physics2D contact event queue service.");
            var kinematicConfig = engine.GetService(CoreServiceKeys.Physics2DKinematicConfig)
                ?? throw new InvalidOperationException("massnav→kinematic bridge requires the Physics2D kinematic config service.");
            if (engine.GetService(CoreServiceKeys.Physics2DShapeStorage) is not ShapeDataStorage2D shapeStorage)
            {
                throw new InvalidOperationException("massnav→kinematic bridge requires the Physics2D shape storage service.");
            }

            var feedSystem = new MassNavKinematicPoseFeedSystem2D(
                engine.World,
                () => MassNavigationIds.TryGetCurrentNavigationRuntime(engine, out MassNavigationSimulationRuntime simulation)
                    ? simulation
                    : null,
                poseBuffer,
                shapeStorage);
            engine.InsertSystemBeforeRequired<Physics2DSimulationSystem>(feedSystem, SystemGroup.InputCollection);
            engine.SetService(MovementPhysics2DBridgeKeys.KinematicPoseFeedSystem, feedSystem);

            var router = new ContactEventRouter2D(kinematicConfig.ContactEventEmitterLayers);
            engine.RegisterSystem(new ContactEventRoutingSystem2D(contactEvents, router), SystemGroup.InputCollection);
            engine.SetService(MovementPhysics2DBridgeKeys.ContactEventRouter, router);
        }
    }
}
