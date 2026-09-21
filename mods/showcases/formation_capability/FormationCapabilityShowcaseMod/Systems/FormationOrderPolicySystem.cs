using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Client;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.Orders;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Scripting;
using FormationCapabilityShowcaseMod.Runtime;

namespace FormationCapabilityShowcaseMod.Systems;

/// <summary>
/// Formation order policy (migration slice 2): the local order mapping installs through the
/// CoreInputMod auto assembly; this policy system waits for that install and attaches the
/// control-domain move-order gate (helper BeforeOrderSubmit) plus the formation command actor
/// expander.
/// </summary>
internal sealed class FormationOrderPolicySystem : ISystem<float>
{
    private readonly World _world;
    private readonly Dictionary<string, object> _globals;
    private readonly FormationCommandActorExpander _commandActorExpander;
    private ControlDomainQuery? _controlDomains;
    private int _moveOrderTypeId;
    private bool _attached;

    public FormationOrderPolicySystem(
        World world,
        Dictionary<string, object> globals,
        int maxMembersPerFormation,
        int maxExpandedActorCount)
    {
        _world = world;
        _globals = globals;
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
        if (_attached ||
            !_globals.TryGetValue(CoreServiceKeys.ActiveInputOrderMapping.Name, out var mappingObj) ||
            mappingObj is not InputOrderMappingSystem mapping ||
            !_globals.TryGetValue(AutoInstalledLocalOrderSourceSystem.ServiceKey.Name, out var sourceObj) ||
            sourceObj is not AutoInstalledLocalOrderSourceSystem orderSource)
        {
            return;
        }

        _attached = true;
        orderSource.OrderSource.BeforeOrderSubmit = CanSolePossessedSubmitOrder;
        mapping.SetCommandActorExpander(_commandActorExpander);
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
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
