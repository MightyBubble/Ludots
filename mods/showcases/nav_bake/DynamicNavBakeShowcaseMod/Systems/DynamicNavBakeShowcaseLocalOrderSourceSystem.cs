using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using DynamicNavBakeShowcaseMod.Runtime;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace DynamicNavBakeShowcaseMod.Systems;

internal sealed class DynamicNavBakeShowcaseLocalOrderSourceSystem : ISystem<float>
{
    private readonly World _world;
    private readonly Dictionary<string, object> _globals;
    private readonly IModContext _context;
    private readonly LocalOrderSourceHelper _helper;
    private readonly DynamicNavBakeShowcaseRuntime _runtime;
    private InputOrderMappingSystem? _mapping;

    public DynamicNavBakeShowcaseLocalOrderSourceSystem(
        World world,
        Dictionary<string, object> globals,
        OrderQueue orders,
        IModContext context,
        DynamicNavBakeShowcaseRuntime runtime)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _globals = globals ?? throw new ArgumentNullException(nameof(globals));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _helper = new LocalOrderSourceHelper(world, globals, orders);
    }

    public void Initialize()
    {
        _mapping = _helper.TryCreateMapping(_context)
            ?? throw new InvalidOperationException(
                "DynamicNavBakeShowcaseMod requires AuthoritativeInput and assets/Input/input_order_mappings.json before local player orders are installed.");
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        // Construction mode owns Confirm / Command / Cancel; do not issue massNavigationMove.
        if (_runtime.ConstructionMode)
        {
            return;
        }

        InputOrderMappingSystem mapping = _mapping
            ?? throw new InvalidOperationException(
                "DynamicNavBakeShowcaseMod local player order mapping is not initialized.");
        Entity actor = _helper.GetControlledActor();
        if (_helper.TrySetLocalPlayer(mapping, actor))
        {
            mapping.Update(dt);
        }
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
        if (_mapping != null &&
            _globals.TryGetValue(CoreServiceKeys.ActiveInputOrderMapping.Name, out object? active) &&
            ReferenceEquals(active, _mapping))
        {
            _globals.Remove(CoreServiceKeys.ActiveInputOrderMapping.Name);
        }

        _mapping = null;
    }
}
