using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Selection;

public sealed class MassNavigationPrimarySelectionViewBootstrapSystem : ISystem<float>
{
    private static readonly QueryDescription AuthoredPlayerOwnerQuery = new QueryDescription().WithAll<PlayerOwner>();

    private readonly GameEngine _engine;
    private bool _bootstrapped;

    public MassNavigationPrimarySelectionViewBootstrapSystem(GameEngine engine)
    {
        _engine = engine;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!Ludots.Core.MassNavigation.MassNavigationIds.IsCurrentNavigationRuntimeReady(_engine))
        {
            _bootstrapped = false;
            return;
        }

        if (_bootstrapped)
        {
            return;
        }

        SelectionRuntime selection = _engine.GetService(CoreServiceKeys.SelectionRuntime)
            ?? throw new InvalidOperationException("MassNavigation selection bootstrap requires SelectionRuntime.");
        Entity owner = RequireLocalSelectionOwner(_engine);
        EnsurePrimarySelectionView(_engine.World, owner, selection, _engine.GlobalContext);
        _bootstrapped = true;
    }

    internal static Entity RequireLocalSelectionOwner(GameEngine engine)
    {
        if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
            localObj is not Entity owner ||
            !engine.World.IsAlive(owner))
        {
            owner = ResolveSingleAuthoredPlayerOwner(engine);
            engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name] = owner;
        }

        if (!engine.World.Has<PlayerOwner>(owner))
        {
            throw new InvalidOperationException("MassNavigation selection bootstrap LocalPlayerEntity must author PlayerOwner.");
        }

        return owner;
    }

    private static Entity ResolveSingleAuthoredPlayerOwner(GameEngine engine)
    {
        Entity resolved = Entity.Null;
        int count = 0;
        engine.World.Query(in AuthoredPlayerOwnerQuery, (Entity entity, ref PlayerOwner _) =>
        {
            resolved = entity;
            count++;
        });

        return count switch
        {
            1 => resolved,
            0 => throw new InvalidOperationException("MassNavigation selection bootstrap requires the map to author exactly one PlayerOwner local player entity."),
            _ => throw new InvalidOperationException("MassNavigation selection bootstrap found multiple PlayerOwner entities before LocalPlayerEntity was resolved; author one local player or bind CoreServiceKeys.LocalPlayerEntity explicitly.")
        };
    }

    internal static void EnsurePrimarySelectionView(
        World world,
        Entity owner,
        SelectionRuntime selection,
        Dictionary<string, object> globals)
    {
        if (!world.Has<SelectionDragState>(owner))
        {
            throw new InvalidOperationException("MassNavigation selection bootstrap local player template must author SelectionDragState.");
        }

        if (!SelectionContextRuntime.TrySetCurrentView(
                world,
                globals,
                selection,
                owner,
                SelectionViewKeys.Primary,
                owner,
                SelectionSetKeys.LivePrimary,
                out _))
        {
            throw new InvalidOperationException("MassNavigation selection bootstrap failed to bind LivePrimary as the primary selection view.");
        }
    }
}
