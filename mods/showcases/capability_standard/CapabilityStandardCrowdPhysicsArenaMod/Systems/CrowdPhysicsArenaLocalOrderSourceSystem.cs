using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Modding;

namespace CapabilityStandardCrowdPhysicsArenaMod.Systems;

internal sealed class CrowdPhysicsArenaLocalOrderSourceSystem : ISystem<float>
{
    private readonly World _world;
    private readonly IModContext _context;
    private readonly LocalOrderSourceHelper _helper;
    private InputOrderMappingSystem? _mapping;
    private bool _initialized;

    public CrowdPhysicsArenaLocalOrderSourceSystem(
        World world,
        Dictionary<string, object> globals,
        OrderQueue orders,
        IModContext context)
    {
        _world = world;
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

        Entity actor = _helper.GetControlledActor();
        if (_helper.TryBindSoleSeatActor(_mapping, actor))
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
    }
}
