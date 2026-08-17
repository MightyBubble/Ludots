using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace BrowserRtsProductionShowcaseMod;

public sealed class BrowserRtsProductionLocalOrderSourceSystem : ISystem<float>
{
    private readonly World _world;
    private readonly Dictionary<string, object> _globals;
    private readonly LocalOrderSourceHelper _helper;
    private readonly IModContext _context;
    private InputOrderMappingSystem? _mapping;

    public BrowserRtsProductionLocalOrderSourceSystem(
        World world,
        Dictionary<string, object> globals,
        OrderQueue orders,
        IModContext context)
    {
        _world = world;
        _globals = globals;
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _helper = new LocalOrderSourceHelper(world, globals, orders);
    }

    public void Initialize()
    {
        KeepBrowserInputSurfaceActive();
        EnsureInitialized();
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        EnsureInitialized();
        KeepBrowserInputSurfaceActive();
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
        KeepBrowserInputSurfaceActive();
        if (_mapping != null)
        {
            return;
        }

        _mapping = _helper.TryCreateMapping(_context);
        if (_mapping == null)
        {
            return;
        }

        KeepBrowserInputSurfaceActive();
        _mapping.SetQueueModifierProvider(() =>
        {
            return _globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out object? inputObj) &&
                   inputObj is IInputActionReader input &&
                   input.IsDown("QueueModifier");
        });
    }

    private void KeepBrowserInputSurfaceActive()
    {
        _globals[SkillBarOverlaySystem.SkillBarEnabledKey] = false;
        _globals[SkillBarOverlaySystem.SkillBarKeyLabelsKey] = new[] { "Q", "W", "E", "R" };
        if (_mapping != null)
        {
            _globals[CoreServiceKeys.ActiveInputOrderMapping.Name] = _mapping;
        }
    }

}
