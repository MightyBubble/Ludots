using System.Collections.Generic;
using System;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace FireballSharedMod;

public sealed class FireballSharedModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[FireballSharedMod] Loaded - fireball arena uses GAS abilities/effects and presenter rules");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.Get(CoreServiceKeys.Engine) is not GameEngine engine)
            {
                throw new InvalidOperationException("FireballSharedMod requires GameEngine on GameStart.");
            }

            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.OrderQueue.Name, out object? orderQueueObj) ||
                orderQueueObj is not OrderQueue orders)
            {
                throw new InvalidOperationException("FireballSharedMod requires OrderQueue before installing local fireball input.");
            }

            engine.RegisterSystem(
                new FireballLocalOrderSourceSystem(engine.World, engine.GlobalContext, orders, context),
                SystemGroup.InputCollection);

            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}


internal sealed class FireballLocalOrderSourceSystem : ISystem<float>
{
    private readonly World _world;
    private readonly LocalOrderSourceHelper _helper;
    private readonly IModContext _context;
    private InputOrderMappingSystem? _mapping;
    private bool _initialized;

    public FireballLocalOrderSourceSystem(
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
        if (_world.IsAlive(actor) && _helper.TryBindSoleSeatActor(_mapping, actor))
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
            throw new InvalidOperationException(
                "FireballSharedMod could not install input_order_mappings.json; AuthoritativeInput and the VFS input config are required.");
        }
    }
}
