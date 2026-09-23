using System;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;

namespace RtsStarCraftLikeShowcaseMod;

internal static class RtsStarCraftLikeShowcaseRuntime
{
    private const string MapId = "rts_starcraft_like";
    private const string InstalledKey = "RtsStarCraftLikeShowcaseMod.ArmoriesInstalled";

    private static readonly (string Headquarters, string ItemId, string ItemName)[] RaceLoadouts =
    [
        ("Terran Command Center", "rts_sc_item_terran_stimpack", "Terran Stimpack Doctrine"),
        ("Zerg Hatchery", "rts_sc_item_zerg_adrenal_glands", "Zerg Adrenal Glands"),
        ("Protoss Nexus", "rts_sc_item_protoss_psi_matrix", "Protoss Psi Matrix")
    ];

    public static Task InstallOnMapLoadedAsync(ScriptContext context)
    {
        GameEngine engine = context.GetEngine()
            ?? throw new InvalidOperationException("RtsStarCraftLikeShowcaseMod requires GameEngine.");

        if (!IsStarCraftLikeMap(engine))
        {
            return Task.CompletedTask;
        }

        EnsureDefaultSelection(engine);
        EnsureRaceLoadoutItems(engine);
        return Task.CompletedTask;
    }

    private static bool IsStarCraftLikeMap(GameEngine engine)
    {
        string? currentMapId = engine.CurrentMapSession?.MapConfig?.Id;
        if (string.Equals(currentMapId, MapId, StringComparison.Ordinal))
        {
            return true;
        }

        var tags = engine.CurrentMapSession?.MapConfig?.Tags;
        if (tags == null)
        {
            return false;
        }

        for (int i = 0; i < tags.Count; i++)
        {
            if (string.Equals(tags[i], "starcraft_like", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tags[i], "sc2", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureDefaultSelection(GameEngine engine)
    {
        Entity target = FindFirstByName(engine.World, "Terran Command Center");
        if (target == Entity.Null)
        {
            target = FindFirstAbilityTarget(engine.World);
        }

        if (target == Entity.Null || !engine.World.IsAlive(target))
        {
            return;
        }

        var selection = engine.GetService(CoreServiceKeys.SelectionRuntime);
        Entity owner = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        if (selection == null || !engine.World.IsAlive(owner))
        {
            return;
        }

        Span<Entity> next = stackalloc Entity[1];
        next[0] = target;
        selection.ReplaceSelection(owner, SelectionSetKeys.LivePrimary, next);
        selection.TryBindView(owner, SelectionViewKeys.Primary, owner, SelectionSetKeys.LivePrimary);
        engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = owner;
        engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
    }

    private static void EnsureRaceLoadoutItems(GameEngine engine)
    {
        if (engine.GlobalContext.ContainsKey(InstalledKey))
        {
            return;
        }

        InventoryRuntimeService inventory = engine.GetService(CoreServiceKeys.InventoryRuntimeService)
            ?? throw new InvalidOperationException("RtsStarCraftLikeShowcaseMod requires InventoryRuntimeService.");
        ItemLayoutRegistry layouts = engine.GetService(CoreServiceKeys.ItemLayoutRegistry)
            ?? throw new InvalidOperationException("RtsStarCraftLikeShowcaseMod requires ItemLayoutRegistry.");
        ItemDefinitionRegistry items = engine.GetService(CoreServiceKeys.ItemDefinitionRegistry)
            ?? throw new InvalidOperationException("RtsStarCraftLikeShowcaseMod requires ItemDefinitionRegistry.");

        int layoutId = layouts.GetId("rts_sc_armory_loadout");
        if (layoutId <= 0)
        {
            throw new InvalidOperationException("Item layout 'rts_sc_armory_loadout' is not registered.");
        }

        for (int i = 0; i < RaceLoadouts.Length; i++)
        {
            var loadout = RaceLoadouts[i];
            Entity headquarters = FindFirstByName(engine.World, loadout.Headquarters);
            if (headquarters == Entity.Null || !engine.World.IsAlive(headquarters))
            {
                throw new InvalidOperationException($"StarCraft-like headquarters '{loadout.Headquarters}' was not found.");
            }

            int itemId = items.GetId(loadout.ItemId);
            if (itemId <= 0)
            {
                throw new InvalidOperationException($"Item definition '{loadout.ItemId}' is not registered.");
            }

            Entity container = inventory.CreateContainer(headquarters, layoutId, ItemContainerPurpose.Equipment);
            Entity item = inventory.CreateItem(itemId, stackCount: 1, charges: 0, durability: 100);
            if (!engine.World.Has<Name>(item))
            {
                engine.World.Add(item, new Name { Value = loadout.ItemName });
            }

            if (!inventory.TryAutoPlaceItem(item, container))
            {
                throw new InvalidOperationException($"Could not place StarCraft-like item '{loadout.ItemId}' into '{loadout.Headquarters}' loadout.");
            }
        }

        engine.GlobalContext[InstalledKey] = true;
    }

    private static Entity FindFirstByName(World world, string value)
    {
        Entity result = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity entity, ref Name name) =>
        {
            if (result == Entity.Null && string.Equals(name.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                result = entity;
            }
        });

        return result;
    }

    private static Entity FindFirstAbilityTarget(World world)
    {
        Entity result = Entity.Null;
        var query = new QueryDescription().WithAll<Name, Ludots.Core.Gameplay.GAS.Components.AbilityStateBuffer>();
        world.Query(in query, (Entity entity, ref Name _, ref Ludots.Core.Gameplay.GAS.Components.AbilityStateBuffer _) =>
        {
            if (result == Entity.Null)
            {
                result = entity;
            }
        });

        return result;
    }
}
