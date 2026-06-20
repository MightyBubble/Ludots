using System;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Selection;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Scripting;

namespace MinimapShowcaseMod.Triggers;

internal sealed class ConfigureMinimapShowcaseOnMapFocusTrigger : Trigger
{
    private static readonly QueryDescription NamedEntityQuery = new QueryDescription()
        .WithAll<Name, MapEntity>();

    private readonly int _capitalTagId = TagRegistry.Register("State.Minimap.Capital");
    private readonly int _objectiveTagId = TagRegistry.Register("State.Minimap.Objective");
    private readonly int _resourceTagId = TagRegistry.Register("State.Minimap.Resource");
    private readonly int _hazardTagId = TagRegistry.Register("State.Minimap.Hazard");
    private readonly int _alertTagId = TagRegistry.Register("State.Minimap.Alert");
    private readonly int _frontierTagId = TagRegistry.Register("State.Minimap.Frontier");

    public ConfigureMinimapShowcaseOnMapFocusTrigger()
    {
        EventKey = GameEvents.MapLoaded;
    }

    public override Task ExecuteAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null || engine.CurrentMapSession?.MapId.Value != MinimapShowcaseIds.MapId)
        {
            return Task.CompletedTask;
        }

        if (!TryGetMinimapRuntime(engine, out MinimapRuntime runtime))
        {
            return Task.CompletedTask;
        }

        runtime.Visible = true;
        runtime.UseRtsFullMapPreset();
        runtime.SetViewport(1800f, -600f, 22000f);
        ConfigurePerspectiveAndTags(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine != null && TryGetMinimapRuntime(engine, out MinimapRuntime runtime))
        {
            runtime.Visible = false;
        }

        return Task.CompletedTask;
    }

    private static bool TryGetMinimapRuntime(GameEngine engine, out MinimapRuntime runtime)
    {
        if (engine.GetService(CoreServiceKeys.MinimapRuntime) is MinimapRuntime found)
        {
            runtime = found;
            return true;
        }

        runtime = null!;
        return false;
    }

    private void ConfigurePerspectiveAndTags(GameEngine engine)
    {
        MapId mapId = engine.CurrentMapSession?.MapId ?? default;
        TagOps? tagOps = engine.GetService(CoreServiceKeys.TagOps);
        SelectionRuntime? selection = engine.GetService(CoreServiceKeys.SelectionRuntime);
        if (tagOps == null || selection == null)
        {
            return;
        }

        Entity playerCapital = Entity.Null;
        foreach (ref var chunk in engine.World.Query(in NamedEntityQuery))
        {
            var names = chunk.GetSpan<Name>();
            var mapEntities = chunk.GetSpan<MapEntity>();
            for (int i = 0; i < chunk.Count; i++)
            {
                if (mapEntities[i].MapId != mapId)
                {
                    continue;
                }

                Entity entity = chunk.Entity(i);
                string name = names[i].Value ?? string.Empty;
                EnsureTagComponents(engine.World, entity);
                ref var tags = ref engine.World.Get<GameplayTagContainer>(entity);
                ref var counts = ref engine.World.Get<TagCountContainer>(entity);

                if (IsCapital(name)) tagOps.AddTag(ref tags, ref counts, _capitalTagId);
                if (string.Equals(name, "Ancient Gate", StringComparison.Ordinal)) tagOps.AddTag(ref tags, ref counts, _objectiveTagId);
                if (string.Equals(name, "Crystal Relay", StringComparison.Ordinal) || string.Equals(name, "Helium Well", StringComparison.Ordinal)) tagOps.AddTag(ref tags, ref counts, _resourceTagId);
                if (string.Equals(name, "Void Rift", StringComparison.Ordinal) || string.Equals(name, "Ember Storm", StringComparison.Ordinal)) tagOps.AddTag(ref tags, ref counts, _hazardTagId);
                if (name.Contains("Frontier", StringComparison.OrdinalIgnoreCase) || name.Contains("Carrier", StringComparison.OrdinalIgnoreCase) || name.Contains("Warpack", StringComparison.OrdinalIgnoreCase) || name.Contains("Wing", StringComparison.OrdinalIgnoreCase)) tagOps.AddTag(ref tags, ref counts, _alertTagId);
                if (name.Contains("Frontier", StringComparison.OrdinalIgnoreCase) || name.Contains("Border", StringComparison.OrdinalIgnoreCase)) tagOps.AddTag(ref tags, ref counts, _frontierTagId);

                if (playerCapital == Entity.Null && string.Equals(name, "Imperial Capital", StringComparison.Ordinal))
                {
                    playerCapital = entity;
                }
            }
        }

        if (playerCapital == Entity.Null)
        {
            return;
        }

        engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name] = playerCapital;
        if (engine.World.TryGet(playerCapital, out PlayerOwner playerOwner) && playerOwner.PlayerId > 0)
        {
            engine.GlobalContext[CoreServiceKeys.LocalPlayerId.Name] = playerOwner.PlayerId;
        }

        engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = playerCapital;
        engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
        selection.ReplaceSelection(playerCapital, SelectionSetKeys.LivePrimary, stackalloc[] { playerCapital });
        selection.TryBindView(playerCapital, SelectionViewKeys.Primary, playerCapital, SelectionSetKeys.LivePrimary);
    }

    private static void EnsureTagComponents(World world, Entity entity)
    {
        if (!world.Has<GameplayTagContainer>(entity)) world.Add(entity, new GameplayTagContainer());
        if (!world.Has<TagCountContainer>(entity)) world.Add(entity, new TagCountContainer());
        if (!world.Has<TimedTagBuffer>(entity)) world.Add(entity, new TimedTagBuffer());
    }

    private static bool IsCapital(string name)
    {
        return name.Contains("Capital", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Prime", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Nest", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Enclave", StringComparison.OrdinalIgnoreCase);
    }
}
