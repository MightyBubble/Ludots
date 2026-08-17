using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace UxPrototypeMod.Systems;

internal sealed class UxPrototypeLocalOrderSourceSystem : ISystem<float>
{
    private readonly World _world;
    private readonly Dictionary<string, object> _globals;
    private readonly LocalOrderSourceHelper _helper;
    private readonly IModContext _context;
    private InputOrderMappingSystem? _mapping;
    private bool _initialized;

    public UxPrototypeLocalOrderSourceSystem(World world, Dictionary<string, object> globals, OrderQueue orders, IModContext context)
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
        if (!_world.IsAlive(actor))
        {
            return;
        }

        if (!_helper.TryBindSoleSeatActor(_mapping, actor))
        {
            return;
        }

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

        _globals[SkillBarOverlaySystem.SkillBarEnabledKey] = false;
        _mapping.SetQueueModifierProvider(() =>
        {
            return _globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) &&
                   inputObj is IInputActionReader input &&
                   input.IsDown("QueueModifier");
        });
    }
}
