using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.Orders;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using FormationCapabilityShowcaseMod.Runtime;

namespace FormationCapabilityShowcaseMod.Systems;

internal sealed class FormationCapabilityLocalOrderSourceSystem : ISystem<float>
{
    private readonly World _world;
    private readonly Dictionary<string, object> _globals;
    private readonly IModContext _context;
    private readonly LocalOrderSourceHelper _helper;
    private InputOrderMappingSystem? _mapping;
    private ControlDomainQuery? _controlDomains;
    private readonly FormationCommandActorExpander _commandActorExpander;
    private int _moveOrderTypeId;
    private bool _initialized;

    public FormationCapabilityLocalOrderSourceSystem(
        World world,
        Dictionary<string, object> globals,
        OrderQueue orders,
        IModContext context,
        int maxMembersPerFormation,
        int maxExpandedActorCount)
    {
        _world = world;
        _globals = globals;
        _context = context;
        _helper = new LocalOrderSourceHelper(world, globals, orders);
        _commandActorExpander = new FormationCommandActorExpander(
            world,
            maxMembersPerFormation,
            maxExpandedActorCount);
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        EnsureInitialized();
        if (_mapping == null)
        {
            return;
        }

        Entity actor = _helper.GetControlledActor();
        if (_helper.TryBindSoleSeatActor(_mapping, actor))
        {
            _mapping.Update(dt);
        }
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
        if (_mapping != null)
        {
            _helper.BeforeOrderSubmit = CanSolePossessedSubmitOrder;
            _mapping.SetCommandActorExpander(_commandActorExpander);
        }
    }

    private bool CanSolePossessedSubmitOrder(in Order order)
    {
        if (!IsMoveOrder(in order))
        {
            return true;
        }

        if (!TryResolveSolePossessedRep(out Entity solePossessedRep))
        {
            return false;
        }

        _controlDomains ??= _globals.TryGetValue(CoreServiceKeys.ControlDomainQuery.Name, out object? domainsObj) &&
            domainsObj is ControlDomainQuery domains
                ? domains
                : throw new InvalidOperationException("Formation Capability order source requires ControlDomainQuery.");
        if (!_world.IsAlive(order.Actor) ||
            !_controlDomains.TryResolveControlDomain(order.Actor, out Entity domain) ||
            domain != solePossessedRep)
        {
            return false;
        }

        return true;
    }

    private bool IsMoveOrder(in Order order)
    {
        if (_moveOrderTypeId == 0)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.OrderTypeRegistry.Name, out object? orderTypesObj) ||
                orderTypesObj is not OrderTypeRegistry orderTypes ||
                !orderTypes.TryGetId(MassNavigationOrderKeys.Move, out _moveOrderTypeId))
            {
                return false;
            }
        }

        return order.OrderTypeId == _moveOrderTypeId;
    }

    private bool TryResolveSolePossessedRep(out Entity solePossessedRep)
    {
        solePossessedRep = Entity.Null;
        return ClientLocalSeatAccess.TryGetSolePossessedRep(_globals, out Entity local) &&
               _world.IsAlive(local) &&
               (solePossessedRep = local) != Entity.Null;
    }

}
