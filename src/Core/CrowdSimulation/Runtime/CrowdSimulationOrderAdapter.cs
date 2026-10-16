using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Scripting;

namespace Ludots.Core.CrowdSimulation.Runtime;

/// <summary>
/// 会话首次激活时向 <see cref="OrderBufferSystem"/> 登记 <c>crowdSimulationMove</c> 的外部消费者。
/// 排空留在订单接入自己的阶段。成员在这次排空里钉死；指令记在当前 TickCount，下一拍会话步进才执行并写日志。
/// 普通右键只有一个地表点，形状固定为 <see cref="PlainClickShapeId"/>。
/// <see cref="OrderArgs.Spatial"/> 的 WorldCm 写成 (X, 0, Y)，平面坐标取 X 与 Z，按整数截断。
/// </summary>
public static class CrowdSimulationOrderAdapter
{
    public const string OrderTypeKey = "crowdSimulationMove";
    public const string PlainClickShapeId = "box";

    public static void Register(GameEngine engine, CrowdSimulationRuntime runtime)
    {
        var buffer = engine.GetService(CoreServiceKeys.OrderBufferSystem)
            ?? throw new InvalidOperationException("群体下令适配已安装，但缺少 OrderBufferSystem。");
        var registry = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("crowdSimulationMove: 缺少 OrderTypeRegistry。");
        if (!registry.TryGetId(OrderTypeKey, out int id) || id <= 0)
        {
            throw new InvalidOperationException("crowdSimulationMove 未登记。");
        }

        buffer.RegisterExternalOrderConsumer(id, batch =>
        {
            var session = runtime.Session
                ?? throw new InvalidOperationException("crowdSimulationMove: 会话未激活。");
            Accept(session, batch, runtime.OrderPrefs);
            runtime.NoteLiveCommand();
        });
    }

    /// <summary>
    /// 成员数组在这里一次成形。地表点取 WorldCm.X 与 WorldCm.Z。
    /// 指令排到当前 <see cref="Units.CrowdSimSession.TickCount"/>，由下一拍会话步进执行并记日志。
    /// </summary>
    public static void Accept(Units.CrowdSimSession session, ReadOnlySpan<Order> batch)
        => Accept(session, batch, null);

    public static void Accept(Units.CrowdSimSession session, ReadOnlySpan<Order> batch, CrowdPlayerOrderPrefs? prefs)
    {
        if (batch.IsEmpty) return;
        if (prefs != null && string.IsNullOrWhiteSpace(prefs.ShapeId))
        {
            throw new InvalidOperationException("crowdSimulationMove: 阵型 id 为空。");
        }
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

        var point = first.Args.Spatial.WorldCm;
        session.Commands.Schedule(session, new[]
        {
            new Units.CrowdCommand(session.TickCount, new Units.CrowdSimCommand
            {
                Kind = Units.CrowdSimCommandKind.Order,
                Player = player,
                XCm = (int)point.X,
                YCm = (int)point.Z,
                ShapeId = prefs == null ? PlainClickShapeId : prefs.ShapeId,
                HasAuto = prefs != null,
                Auto = prefs != null && prefs.MagicBox,
                Members = members,
            }),
        });
    }
}
