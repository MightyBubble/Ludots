using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Scripting;

namespace Ludots.Core.CrowdSimulation.Runtime;

/// <summary>
/// 把 <c>crowdSimulationMove</c> 收成一条带成员句柄的群体指令。
/// 成员是提交时钉在订单上的演员。普通右键只有一个地面点，成员共用这一点，形状固定为 <see cref="PlainClickShapeId"/>。
/// <see cref="OrderArgs.Spatial"/> 的 WorldCm 写成 (X, 0, Y)，平面坐标取 X 与 Z，按整数截断。
/// 排在会话步进之前：本 tick 的现场下令在运动前落地，并由 <see cref="OrderBufferSystem"/> 配上 EntityIntake。
/// </summary>
public sealed class CrowdSimulationOrderBridgeSystem : ISystem<float>
{
    public const string OrderTypeKey = "crowdSimulationMove";
    public const string PlainClickShapeId = "box";

    private readonly GameEngine _engine;
    private readonly CrowdSimulationRuntime _runtime;
    private bool _consumerRegistered;

    public CrowdSimulationOrderBridgeSystem(GameEngine engine, CrowdSimulationRuntime runtime)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (_runtime.Session == null) return;
        var buffer = _engine.GetService(CoreServiceKeys.OrderBufferSystem);
        if (buffer == null) return;
        EnsureConsumer(buffer);
        buffer.DispatchExternalOrderConsumers();
    }

    /// <summary>
    /// 成员数组在这里一次成形。地面点取 WorldCm.X 与 WorldCm.Z。
    /// </summary>
    public static void Accept(Units.CrowdSimSession session, ReadOnlySpan<Order> batch)
    {
        if (batch.IsEmpty) return;
        ref readonly Order first = ref batch[0];
        int player = first.PlayerId;
        var world = session.World;
        var members = new uint[batch.Length];
        for (int i = 0; i < batch.Length; i++)
        {
            Order row = batch[i];
            if (row.PlayerId != player)
            {
                throw new InvalidOperationException("crowdSimulationMove: 同一批次的演员不属于同一玩家。");
            }

            if (!session.Units.TryHandleOf(row.Actor, out uint handle))
            {
                throw new InvalidOperationException("crowdSimulationMove: 演员不是本会话的单位。");
            }

            if (!world.IsAlive(row.Actor) || world.Get<PlayerOwner>(row.Actor).PlayerId != player)
            {
                throw new InvalidOperationException("order: 成员不属于该玩家。");
            }

            members[i] = handle;
        }

        var ground = first.Args.Spatial.WorldCm;
        session.Commands.Submit<object?>(session, new Units.CrowdSimCommand
        {
            Kind = Units.CrowdSimCommandKind.Order,
            Player = player,
            XCm = (int)ground.X,
            YCm = (int)ground.Z,
            ShapeId = PlainClickShapeId,
            Members = members,
        }, Units.CrowdSimCommands.Exec);
    }

    private void EnsureConsumer(OrderBufferSystem buffer)
    {
        if (_consumerRegistered) return;
        var registry = _engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("crowdSimulationMove: 缺少 OrderTypeRegistry。");
        if (!registry.TryGetId(OrderTypeKey, out int id) || id <= 0)
        {
            throw new InvalidOperationException("crowdSimulationMove 未登记。");
        }

        buffer.RegisterExternalOrderConsumer(id, batch =>
        {
            var session = _runtime.Session
                ?? throw new InvalidOperationException("crowdSimulationMove: 会话未激活。");
            Accept(session, batch);
            _runtime.NoteLiveCommand();
        });
        _consumerRegistered = true;
    }
}
