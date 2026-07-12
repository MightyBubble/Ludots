using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.Orders;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace FormationCapabilityShowcaseMod.Systems;

internal sealed class FormationCapabilityLocalOrderSourceSystem : ISystem<float>
{
    private readonly World _world;
    private readonly Dictionary<string, object> _globals;
    private readonly IModContext _context;
    private readonly LocalOrderSourceHelper _helper;
    private InputOrderMappingSystem? _mapping;
    private ControlDomainQuery? _controlDomains;
    private int _massNavigationMoveOrderTypeId;
    private bool _initialized;

    public FormationCapabilityLocalOrderSourceSystem(
        World world,
        Dictionary<string, object> globals,
        OrderQueue orders,
        IModContext context)
    {
        _world = world;
        _globals = globals;
        _context = context;
        _helper = new LocalOrderSourceHelper(world, globals, orders);
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
        if (_helper.TrySetLocalPlayer(_mapping, actor))
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
            _helper.BeforeOrderSubmit = CanLocalPlayerSubmitOrder;
            _globals[SkillBarOverlaySystem.SkillBarKeyLabelsKey] = new[] { "Q", "W", "E", "R" };
        }
    }

    private bool CanLocalPlayerSubmitOrder(in Order order)
    {
        if (!IsMassNavigationMoveOrder(in order))
        {
            return true;
        }

        if (!TryResolveLocalPlayerEntity(out Entity localPlayer))
        {
            return false;
        }

        _controlDomains ??= _globals.TryGetValue(CoreServiceKeys.ControlDomainQuery.Name, out object? domainsObj) &&
            domainsObj is ControlDomainQuery domains
                ? domains
                : throw new InvalidOperationException("Formation Capability order source requires ControlDomainQuery.");
        if (!_world.IsAlive(order.Actor) ||
            !_controlDomains.TryResolveControlDomain(order.Actor, out Entity domain) ||
            domain != localPlayer)
        {
            return false;
        }

        return true;
    }

    private bool IsMassNavigationMoveOrder(in Order order)
    {
        if (_massNavigationMoveOrderTypeId == 0)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.OrderTypeRegistry.Name, out object? orderTypesObj) ||
                orderTypesObj is not OrderTypeRegistry orderTypes ||
                !orderTypes.TryGetId(MassNavigationOrderKeys.Move, out _massNavigationMoveOrderTypeId))
            {
                return false;
            }
        }

        return order.OrderTypeId == _massNavigationMoveOrderTypeId;
    }

    private bool TryResolveLocalPlayerEntity(out Entity localPlayer)
    {
        localPlayer = Entity.Null;
        return _globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) &&
               localObj is Entity local &&
               _world.IsAlive(local) &&
               (localPlayer = local) != Entity.Null;
    }
}
