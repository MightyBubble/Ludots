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
            if (!_loggedNullMapping)
            {
                _loggedNullMapping = true;
                Console.Error.WriteLine("ORDERSRC mapping null");
            }
            return;
        }

        // Input attribution binds the sole seat rep via the InputMod helper: this showcase
        // has no single player-owned avatar, so CommandSource-primary resolution can
        // never supply an actor here. The helper owns seat access; it is not exposed here.
        Entity actor = _helper.ResolveSoleSeatActor();
        if (_helper.TryBindSoleSeatActor(_mapping, actor))
        {
            _mapping.Update(dt);
        }
        else if (!_loggedBindFailure)
        {
            _loggedBindFailure = true;
            Console.Error.WriteLine("ORDERSRC bind failed actor=" + actor.ToString());
        }
    }

    private bool _loggedNullMapping;
    private bool _loggedBindFailure;

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
