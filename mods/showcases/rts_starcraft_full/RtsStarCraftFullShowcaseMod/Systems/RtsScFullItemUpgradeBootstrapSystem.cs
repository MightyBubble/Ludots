using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Scripting;

namespace RtsStarCraftFullShowcaseMod.Systems;

public sealed class RtsScFullItemUpgradeBootstrapSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private bool _terranEquipped;
    private bool _zergEquipped;
    private bool _protossEquipped;
    private int _layoutId;
    private int _terranItemId;
    private int _zergItemId;
    private int _protossItemId;

    public RtsScFullItemUpgradeBootstrapSystem(GameEngine engine)
        : base(engine.World)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public override void Update(in float dt)
    {
        if (_terranEquipped && _zergEquipped && _protossEquipped)
        {
            return;
        }

        InventoryRuntimeService? inventory = _engine.GetService(CoreServiceKeys.InventoryRuntimeService);
        ItemLayoutRegistry? layouts = _engine.GetService(CoreServiceKeys.ItemLayoutRegistry);
        ItemDefinitionRegistry? definitions = _engine.GetService(CoreServiceKeys.ItemDefinitionRegistry);
        if (inventory == null || layouts == null || definitions == null)
        {
            return;
        }

        EnsureIds(layouts, definitions);
        TryEquipUpgrade(inventory, "Command Center", _terranItemId, ref _terranEquipped);
        TryEquipUpgrade(inventory, "Hatchery", _zergItemId, ref _zergEquipped);
        TryEquipUpgrade(inventory, "Nexus", _protossItemId, ref _protossEquipped);
    }

    private void EnsureIds(ItemLayoutRegistry layouts, ItemDefinitionRegistry definitions)
    {
        if (_layoutId > 0)
        {
            return;
        }

        _layoutId = layouts.GetId("layout_scf_armory");
        _terranItemId = definitions.GetId("itm_scf_terran_stimpack");
        _zergItemId = definitions.GetId("itm_scf_zerg_metabolicboost");
        _protossItemId = definitions.GetId("itm_scf_protoss_shieldboost");

        if (_layoutId <= 0 || _terranItemId <= 0 || _zergItemId <= 0 || _protossItemId <= 0)
        {
            throw new InvalidOperationException("StarCraft full item bootstrap requires the SCF armory layout and three race upgrade item definitions.");
        }
    }

    private void TryEquipUpgrade(InventoryRuntimeService inventory, string actorName, int itemId, ref bool equipped)
    {
        if (equipped || !TryFindActor(actorName, out Entity actor))
        {
            return;
        }

        Entity container = inventory.CreateContainer(actor, _layoutId, ItemContainerPurpose.Equipment);
        Entity item = inventory.CreateItem(itemId);
        if (!inventory.TryMoveItemToNamedSlot(item, container, "upgrade"))
        {
            throw new InvalidOperationException($"Failed to equip SCF upgrade item {itemId} on '{actorName}'.");
        }

        equipped = true;
    }

    private bool TryFindActor(string actorName, out Entity actor)
    {
        Entity result = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        World.Query(in query, (Entity entity, ref Name name) =>
        {
            if (result == Entity.Null && string.Equals(name.Value, actorName, StringComparison.OrdinalIgnoreCase))
            {
                result = entity;
            }
        });

        actor = result;
        return actor != Entity.Null;
    }
}
