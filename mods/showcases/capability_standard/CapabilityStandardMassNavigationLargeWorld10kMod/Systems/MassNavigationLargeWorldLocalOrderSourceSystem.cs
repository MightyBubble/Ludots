using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Modding;

namespace CapabilityStandardMassNavigationLargeWorld10kMod.Systems;

internal sealed class MassNavigationLargeWorldLocalOrderSourceSystem : ISystem<float>
{
    private readonly World _world;
    private readonly Dictionary<string, object> _globals;
    private readonly IModContext _context;
    private readonly LocalOrderSourceHelper _helper;
    private InputOrderMappingSystem? _mapping;
    private bool _initialized;

    public MassNavigationLargeWorldLocalOrderSourceSystem(
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

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        EnsureInitialized();
        if (_mapping == null)
        {
            return;
        }

        // Sole-seat input attribution binds the possessed seat rep itself: this showcase
        // has no single player-owned avatar, so CommandSource-primary resolution can
        // never supply an actor here.
        if (Ludots.Core.Client.ClientLocalSeatAccess.TryGetSolePossessedRep(_globals, out Entity seatRep) &&
            _world.IsAlive(seatRep) &&
            _helper.TryBindSoleSeatActor(_mapping, seatRep))
        {
            _mapping.Update(dt);
        }
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

    }
}
