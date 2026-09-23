using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Modding;
using RoadNetworkShowcaseMod.Gameplay;

namespace ThreeKingdomsScenarioMod.Systems;

internal sealed class ThreeKingdomsScenarioLocalOrderSourceSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly World _world;
    private readonly LocalOrderSourceHelper _helper;
    private readonly IModContext _context;
    private readonly RoadMoveOrderExpander _expander;
    private InputOrderMappingSystem? _mapping;
    private bool _initialized;

    public ThreeKingdomsScenarioLocalOrderSourceSystem(GameEngine engine, OrderQueue orders, IModContext context)
    {
        _engine = engine;
        _world = engine.World;
        _context = context;
        _helper = new LocalOrderSourceHelper(engine.World, engine.GlobalContext, orders);
        _expander = new RoadMoveOrderExpander(engine.World, engine.GlobalContext, orders, ThreeKingdomsScenarioIds.RoadColumnPlannerAgentTypeId);
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        if (!IsScenarioActive())
        {
            return;
        }

        EnsureInitialized();
        if (_mapping == null)
        {
            return;
        }

        Entity actor = _helper.GetControlledActor();
        if (!_world.IsAlive(actor))
        {
            return;
        }

        _mapping.SetLocalPlayer(actor, 1);
        _mapping.Update(dt);
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _mapping = _helper.TryCreateMapping(_context);
        if (_mapping == null)
        {
            return;
        }

        _mapping.SetOrderSubmitHandler((in Order order) =>
        {
            _engine.GlobalContext[LocalOrderSourceHelper.LastOrderDebugKey] =
                $"type:{order.OrderTypeId},player:{order.PlayerId},actor:{order.Actor.Id}:{order.Actor.WorldId}:{order.Actor.Version},submit:{order.SubmitMode}";
            _expander.TrySubmit(in order);
        });
    }

    private bool IsScenarioActive()
    {
        return ThreeKingdomsScenarioIds.IsScenarioMap(_engine.CurrentMapSession?.MapId.Value);
    }
}
