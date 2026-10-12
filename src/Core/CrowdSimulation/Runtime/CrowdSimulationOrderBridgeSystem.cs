using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Scripting;

namespace Ludots.Core.CrowdSimulation.Runtime;

/// <summary>
/// 把 <c>crowdSimulationMove</c> 从 OrderQueue 收成一条带成员句柄的群体指令。
/// 成员是提交时钉在订单上的演员，不再回读选中集合。
/// 排在会话步进之前，本 tick 的现场下令在运动前落地。
/// </summary>
public sealed class CrowdSimulationOrderBridgeSystem : ISystem<float>
{
    public const string OrderTypeKey = "crowdSimulationMove";

    private readonly GameEngine _engine;
    private readonly CrowdSimulationRuntime _runtime;
    private Order[] _drained = Array.Empty<Order>();
    private uint[] _handles = Array.Empty<uint>();
    private int _orderTypeId;
    private bool _typeResolved;

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
        var session = _runtime.Session;
        if (session == null) return;
        var queue = _engine.GetService(CoreServiceKeys.OrderQueue);
        if (queue == null || queue.Count == 0) return;
        if (!ResolveOrderType()) return;
        if (_drained.Length < queue.Capacity) _drained = new Order[queue.Capacity];
        int drained = queue.DrainOrderType(_orderTypeId, _drained);
        for (int start = 0; start < drained;)
        {
            int batch = _drained[start].AdmissionBatchId > 0 ? _drained[start].AdmissionBatchSize : 1;
            SubmitBatch(session, start, batch);
            start += batch;
        }
    }

    private bool ResolveOrderType()
    {
        if (_typeResolved) return _orderTypeId > 0;
        _typeResolved = true;
        var registry = _engine.GetService(CoreServiceKeys.OrderTypeRegistry);
        if (registry != null && registry.TryGetId(OrderTypeKey, out int id)) _orderTypeId = id;
        return _orderTypeId > 0;
    }

    private void SubmitBatch(Units.CrowdSimSession session, int start, int count)
    {
        if (count <= 0) return;
        ref readonly Order first = ref _drained[start];
        if (_handles.Length < count) _handles = new uint[count];
        int player = first.PlayerId;
        var world = _engine.World;
        for (int i = 0; i < count; i++)
        {
            Order row = _drained[start + i];
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

            _handles[i] = handle;
        }

        var members = new uint[count];
        _handles.AsSpan(0, count).CopyTo(members);
        var ground = first.Args.Spatial.WorldCm;
        _runtime.NoteLiveCommand();
        session.Commands.Submit<object?>(session, new Units.CrowdSimCommand
        {
            Kind = Units.CrowdSimCommandKind.Order,
            Player = player,
            XCm = (int)MathF.Round(ground.X),
            YCm = (int)MathF.Round(ground.Y),
            ShapeId = "box",
            Members = members,
        }, Units.CrowdSimCommands.Exec);
    }
}
