using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using ItemSystemShowcaseMod.UI;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Scripting;
using Ludots.UI;

namespace ItemSystemShowcaseMod.Runtime;

internal sealed class ItemSystemShowcaseRuntime
{
    private readonly ItemSystemShowcasePanelController _panelController;
    private readonly List<string> _log = new();
    private readonly List<Entity> _items = new(64);

    private Entity _hero = Entity.Null;
    private Entity _dummy = Entity.Null;
    private Entity _vendor = Entity.Null;
    private Entity _equipment = Entity.Null;
    private Entity _stash = Entity.Null;
    private Entity _vendorGrid = Entity.Null;
    private Entity _backpackItem = Entity.Null;
    private Entity _secureItem = Entity.Null;
    private Entity _rifleItem = Entity.Null;

    private int _healthAttrId;
    private int _shieldAttrId;
    private int _moveSpeedAttrId;
    private int _attackAttrId;
    private int _armorAttrId;

    private int _bootsDefId;
    private int _amuletDefId;
    private int _bloodRingDefId;
    private int _duelistRingDefId;
    private int _charmDefId;
    private int _backpackDefId;
    private int _secureDefId;
    private int _rifleDefId;
    private int _magDefId;
    private int _redDotDefId;
    private int _suppressorDefId;
    private int _gripDefId;
    private int _ammoDefId;
    private int _apAmmoDefId;
    private int _creditDefId;
    private int _artifactDefId;

    public ItemSystemShowcaseRuntime()
    {
        _panelController = new ItemSystemShowcasePanelController(this);
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        if (ItemSystemShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            EnsureScenario(engine);
            RefreshPanel(engine);
        }
        else
        {
            ClearPanel(engine);
        }

        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        if (context.GetEngine() is GameEngine engine &&
            ItemSystemShowcaseIds.IsShowcaseMap(context.Get(CoreServiceKeys.MapId).Value))
        {
            ClearPanel(engine);
            ResetScenarioRefs();
        }

        return Task.CompletedTask;
    }

    public void Update(GameEngine engine, float dt)
    {
        if (!ItemSystemShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        EnsureScenario(engine);
        RefreshPanel(engine);
    }

    public ItemSystemShowcasePanelState BuildState(GameEngine engine)
    {
        EnsureScenario(engine);
        InventoryRuntimeService inventory = Inventory(engine);
        Entity backpack = Mounted(engine, _backpackItem, "storage");
        Entity secure = Mounted(engine, _secureItem, "vault");

        return new ItemSystemShowcasePanelState(
            Header: "Ludots Item / Equip / Backpack Showcase",
            HeroSummary: $"Loadout Pilot | passive effects {ActiveEffects(engine, _hero)} | item actor #{_hero.Id}",
            CreditsSummary: $"Credits {inventory.CountStackUnits(_hero, _creditDefId)} | FMJ {inventory.CountStackUnits(_hero, _ammoDefId)} | AP {inventory.CountStackUnits(_hero, _apAmmoDefId)}",
            DummySummary: $"Target Dummy health {ReadAttr(engine, _dummy, _healthAttrId):0.0}",
            StatLines: BuildStats(engine),
            AbilityLines: BuildAbilities(engine),
            BuffLines: BuildBuffs(engine),
            EquipmentLines: BuildEquipment(engine),
            BackpackLines: BuildContainer(engine, backpack, "Backpack"),
            SecureLines: BuildContainer(engine, secure, "Secure"),
            StashLines: BuildContainer(engine, _stash, "Stash"),
            VendorLines: BuildContainer(engine, _vendorGrid, "Vendor"),
            LogLines: _log.ToArray());
    }

    public void ToggleBoots(GameEngine engine)
    {
        EnsureScenario(engine);
        Entity equipped = SlotItem(engine, _equipment, "feet");
        if (equipped != Entity.Null)
        {
            Log(Inventory(engine).TryTransferItem(equipped, _stash)
                ? "Boots moved from feet to stash."
                : "Boot transfer to stash failed.");
            return;
        }

        Entity boots = FindByDef(engine, _bootsDefId, _stash);
        Log(boots != Entity.Null && Inventory(engine).TryMoveItemToNamedSlot(boots, _equipment, "feet")
            ? "Boots re-equipped to feet."
            : "Boot equip failed.");
    }

    public void EquipRing(GameEngine engine)
    {
        EnsureScenario(engine);
        if (SlotItem(engine, _equipment, "ring_right") != Entity.Null)
        {
            Log("Right ring slot is already occupied.");
            return;
        }

        Entity ring = FindByDef(engine, _duelistRingDefId, _stash);
        Log(ring != Entity.Null && Inventory(engine).TryMoveItemToNamedSlot(ring, _equipment, "ring_right")
            ? "Duelist Ring equipped into ring_right."
            : "Ring equip failed.");
    }

    public void AttachGrip(GameEngine engine)
    {
        EnsureScenario(engine);
        Entity sockets = Mounted(engine, _rifleItem, "sockets");
        Entity existing = SlotItem(engine, sockets, "underbarrel");
        if (existing != Entity.Null)
        {
            Log(Inventory(engine).TryTransferItem(existing, _stash)
                ? "Vertical Grip detached into stash."
                : "Could not detach grip.");
            return;
        }

        Entity grip = FindPlayerItem(engine, _gripDefId);
        Log(grip != Entity.Null && Inventory(engine).TryMoveItemToNamedSlot(grip, sockets, "underbarrel")
            ? "Vertical Grip attached to rifle."
            : "Grip attachment failed.");
    }

    public void Reload(GameEngine engine)
    {
        EnsureScenario(engine);
        Entity mag = SlotItem(engine, Mounted(engine, _rifleItem, "sockets"), "magwell");
        if (mag == Entity.Null || !engine.World.Has<ItemInstanceCm>(mag))
        {
            Log("Reload blocked: no magazine in magwell.");
            return;
        }

        ref ItemInstanceCm magInstance = ref engine.World.Get<ItemInstanceCm>(mag);
        int need = 30 - magInstance.Charges;
        if (need <= 0)
        {
            Log("Magazine already full at 30/30.");
            return;
        }

        int available = Inventory(engine).CountStackUnits(_hero, _ammoDefId);
        if (available <= 0)
        {
            Log("Reload blocked: no FMJ ammo in player containers.");
            return;
        }

        int load = Math.Min(need, available);
        if (!Inventory(engine).ConsumeStackUnits(_hero, _ammoDefId, load))
        {
            Log("Reload failed while consuming FMJ.");
            return;
        }

        magInstance.Charges += load;
        Log($"Reloaded {load} rounds. Magazine is now {magInstance.Charges}/30.");
    }

    public void FirePrimary(GameEngine engine)
    {
        EnsureScenario(engine);
        Entity mag = SlotItem(engine, Mounted(engine, _rifleItem, "sockets"), "magwell");
        if (mag == Entity.Null || !engine.World.Has<ItemInstanceCm>(mag))
        {
            Log("Primary fire blocked: no magazine.");
            return;
        }

        ref ItemInstanceCm magInstance = ref engine.World.Get<ItemInstanceCm>(mag);
        if (magInstance.Charges <= 0)
        {
            Log("Primary fire blocked: magazine empty.");
            return;
        }

        bool activated = Ability(engine).TryActivateAbility(_hero, 0, _dummy);
        if (!activated)
        {
            Log("Slot 0 rifle fire failed to activate.");
            return;
        }

        magInstance.Charges -= 1;
        Log(activated
            ? $"Slot 0 rifle fire resolved and spent one round. Magazine {magInstance.Charges}/30."
            : "Slot 0 rifle fire failed to activate.");
    }

    public void CastMythicPulse(GameEngine engine)
    {
        EnsureScenario(engine);
        Log(Ability(engine).TryActivateAbility(_hero, 1, _hero)
            ? "Mythic Pulse cast through slot 1."
            : "Mythic Pulse failed.");
    }

    public void CastSecondWind(GameEngine engine)
    {
        EnsureScenario(engine);
        Log(Ability(engine).TryActivateAbility(_hero, 4, _hero)
            ? "Second Wind cast through slot 4."
            : "Second Wind failed.");
    }

    public void StoreArtifact(GameEngine engine)
    {
        EnsureScenario(engine);
        Entity secure = Mounted(engine, _secureItem, "vault");
        Entity artifact = FindByDef(engine, _artifactDefId, _stash);
        if (artifact != Entity.Null)
        {
            Log(Inventory(engine).TryTransferItem(artifact, secure)
                ? "Raid Artifact moved from stash into secure storage."
                : "Could not move artifact into secure storage.");
            return;
        }

        artifact = FindByDef(engine, _artifactDefId, secure);
        Log(artifact != Entity.Null && Inventory(engine).TryTransferItem(artifact, _stash)
            ? "Raid Artifact moved back out of secure storage."
            : "No Raid Artifact found to move.");
    }

    public void BuyApAmmo(GameEngine engine)
    {
        EnsureScenario(engine);
        if (Inventory(engine).CountStackUnits(_hero, _creditDefId) < 20)
        {
            Log("Vendor purchase blocked: insufficient credits.");
            return;
        }

        Entity ammo = FindByDef(engine, _apAmmoDefId, _vendorGrid);
        if (ammo == Entity.Null)
        {
            Log("Vendor is out of AP ammo.");
            return;
        }

        if (!Inventory(engine).TryTransferItem(ammo, _stash))
        {
            Log("Could not place purchased AP ammo into stash.");
            return;
        }

        Inventory(engine).ConsumeStackUnits(_hero, _creditDefId, 20);
        Log("Bought one AP ammo stack for 20 credits.");
    }

    public void SellArtifact(GameEngine engine)
    {
        EnsureScenario(engine);
        Entity artifact = FindPlayerItem(engine, _artifactDefId);
        if (artifact == Entity.Null)
        {
            Log("No Raid Artifact available to sell.");
            return;
        }

        if (!Inventory(engine).TryTransferItem(artifact, _vendorGrid))
        {
            Log("Could not transfer artifact to vendor.");
            return;
        }

        GrantToStash(engine, _creditDefId, 45);
        Log("Sold Raid Artifact and received 45 credits.");
    }

    public void SplitAmmo(GameEngine engine)
    {
        EnsureScenario(engine);
        Entity ammo = FindByDef(engine, _ammoDefId, _stash);
        if (ammo == Entity.Null || !engine.World.Has<ItemInstanceCm>(ammo) || engine.World.Get<ItemInstanceCm>(ammo).StackCount < 30)
        {
            Log("No large FMJ stack available in stash for split demo.");
            return;
        }

        if (!Inventory(engine).TrySplitStack(ammo, 30, out Entity split) || split == Entity.Null)
        {
            Log("Ammo split failed.");
            return;
        }

        TrackItemTree(engine, split);
        Entity backpack = Mounted(engine, _backpackItem, "storage");
        Log(Inventory(engine).TryTransferItem(split, backpack)
            ? "Split 30 FMJ into backpack storage."
            : "Split succeeded but backpack transfer failed.");
    }

    private void EnsureScenario(GameEngine engine)
    {
        if (engine.World.IsAlive(_hero) && engine.World.IsAlive(_stash) && engine.World.IsAlive(_rifleItem))
        {
            return;
        }

        ResolveIds(engine);
        Seed(engine);
    }

    private void ResolveIds(GameEngine engine)
    {
        var definitions = engine.GetService(CoreServiceKeys.ItemDefinitionRegistry)
            ?? throw new InvalidOperationException("ItemDefinitionRegistry missing.");

        _healthAttrId = AttributeRegistry.Register("Health");
        _shieldAttrId = AttributeRegistry.Register("Shield");
        _moveSpeedAttrId = AttributeRegistry.Register("MoveSpeed");
        _attackAttrId = AttributeRegistry.Register("AttackDamage");
        _armorAttrId = AttributeRegistry.Register("Armor");

        _bootsDefId = definitions.GetId("itm_boots_haste");
        _amuletDefId = definitions.GetId("itm_mythic_amulet");
        _bloodRingDefId = definitions.GetId("itm_ring_blood");
        _duelistRingDefId = definitions.GetId("itm_ring_duelist");
        _charmDefId = definitions.GetId("itm_charm_ward");
        _backpackDefId = definitions.GetId("itm_backpack_assault");
        _secureDefId = definitions.GetId("itm_secure_case");
        _rifleDefId = definitions.GetId("itm_rifle_modular");
        _magDefId = definitions.GetId("itm_mag_556");
        _redDotDefId = definitions.GetId("itm_scope_red_dot");
        _suppressorDefId = definitions.GetId("itm_suppressor_s");
        _gripDefId = definitions.GetId("itm_grip_vertical");
        _ammoDefId = definitions.GetId("itm_ammo_556");
        _apAmmoDefId = definitions.GetId("itm_ammo_556_ap");
        _creditDefId = definitions.GetId("itm_credit_chip");
        _artifactDefId = definitions.GetId("itm_extraction_artifact");
    }

    private void Seed(GameEngine engine)
    {
        World world = engine.World;
        _hero = world.Create(new Name { Value = "Loadout Pilot" });
        TrackMapEntity(engine, _hero);
        world.Add(_hero, WorldPositionCm.FromCm(1200, 900));
        world.Add(_hero, new AttributeBuffer());
        world.Add(_hero, new ActiveEffectContainer());
        world.Add(_hero, new GameplayTagContainer());
        world.Add(_hero, new TagCountContainer());
        world.Add(_hero, new TimedTagBuffer());
        world.Add(_hero, new DirtyFlags());
        world.Add(_hero, new AbilityStateBuffer());
        InitAttributes(ref world.Get<AttributeBuffer>(_hero), true);

        _dummy = world.Create(new Name { Value = "Target Dummy" });
        TrackMapEntity(engine, _dummy);
        world.Add(_dummy, WorldPositionCm.FromCm(1600, 900));
        world.Add(_dummy, new AttributeBuffer());
        world.Add(_dummy, new ActiveEffectContainer());
        world.Add(_dummy, new GameplayTagContainer());
        world.Add(_dummy, new TagCountContainer());
        world.Add(_dummy, new TimedTagBuffer());
        world.Add(_dummy, new DirtyFlags());
        InitAttributes(ref world.Get<AttributeBuffer>(_dummy), false);

        _vendor = world.Create(new Name { Value = "Quartermaster" });
        TrackMapEntity(engine, _vendor);
        _equipment = CreateTrackedContainer(engine, _hero, ItemContainerOwnerKind.Actor, "layout_equipment_actor");
        _stash = CreateTrackedContainer(engine, _hero, ItemContainerOwnerKind.Actor, "layout_stash_grid");
        _vendorGrid = CreateTrackedContainer(engine, _vendor, ItemContainerOwnerKind.Vendor, "layout_vendor_grid");

        EquipNamed(engine, CreateTrackedItem(engine, _bootsDefId), "feet");
        EquipNamed(engine, CreateTrackedItem(engine, _amuletDefId), "amulet");
        EquipNamed(engine, CreateTrackedItem(engine, _bloodRingDefId), "ring_left");
        EquipNamed(engine, CreateTrackedItem(engine, _charmDefId), "charm");
        _backpackItem = CreateTrackedItem(engine, _backpackDefId);
        EquipNamed(engine, _backpackItem, "back");
        _secureItem = CreateTrackedItem(engine, _secureDefId);
        EquipNamed(engine, _secureItem, "secure");
        _rifleItem = CreateTrackedItem(engine, _rifleDefId);
        EquipNamed(engine, _rifleItem, "primary_weapon");

        Entity sockets = Mounted(engine, _rifleItem, "sockets");
        Inventory(engine).TryMoveItemToNamedSlot(CreateTrackedItem(engine, _magDefId, charges: 18), sockets, "magwell");
        Inventory(engine).TryMoveItemToNamedSlot(CreateTrackedItem(engine, _redDotDefId), sockets, "optic");
        Inventory(engine).TryMoveItemToNamedSlot(CreateTrackedItem(engine, _suppressorDefId), sockets, "muzzle");

        Put(engine, CreateTrackedItem(engine, _ammoDefId, stackCount: 90), Mounted(engine, _backpackItem, "storage"), 0, 0);
        Put(engine, CreateTrackedItem(engine, _gripDefId), Mounted(engine, _backpackItem, "storage"), 2, 0);
        Put(engine, CreateTrackedItem(engine, _creditDefId, stackCount: 75), _stash, 0, 0);
        Put(engine, CreateTrackedItem(engine, _ammoDefId, stackCount: 60), _stash, 2, 0);
        Put(engine, CreateTrackedItem(engine, _artifactDefId), _stash, 0, 2);
        Put(engine, CreateTrackedItem(engine, _duelistRingDefId), _stash, 3, 0);
        Put(engine, CreateTrackedItem(engine, _artifactDefId), Mounted(engine, _secureItem, "vault"), 0, 0);
        Put(engine, CreateTrackedItem(engine, _apAmmoDefId, stackCount: 45), _vendorGrid, 0, 0);
        Put(engine, CreateTrackedItem(engine, _duelistRingDefId), _vendorGrid, 2, 0);

        if (!world.Has<InventoryEquipmentDirtyTag>(_hero))
        {
            world.Add(_hero, new InventoryEquipmentDirtyTag());
        }

        _log.Clear();
        Log("Scenario seeded from config-driven shapes, layouts, and definitions.");
        Log("Coverage live: MOBA boots/mythic, ARPG rings/charm, extraction stash/secure, rifle sockets, ammo stacks, vendor trade.");
    }

    private void InitAttributes(ref AttributeBuffer attrs, bool hero)
    {
        attrs.SetBase(_healthAttrId, hero ? 100f : 140f);
        attrs.SetBase(_shieldAttrId, hero ? 20f : 0f);
        attrs.SetBase(_moveSpeedAttrId, hero ? 100f : 0f);
        attrs.SetBase(_attackAttrId, hero ? 12f : 0f);
        attrs.SetBase(_armorAttrId, hero ? 5f : 1f);
    }

    private void EquipNamed(GameEngine engine, Entity item, string slotId)
    {
        if (!Inventory(engine).TryMoveItemToNamedSlot(item, _equipment, slotId))
        {
            throw new InvalidOperationException($"Failed to equip {slotId}.");
        }
    }

    private static void Put(GameEngine engine, Entity item, Entity container, int x, int y)
    {
        if (!(engine.GetService(CoreServiceKeys.InventoryRuntimeService)?.TryMoveItemToGrid(item, container, x, y) ?? false))
        {
            throw new InvalidOperationException($"Failed to place item {item.Id}.");
        }
    }

    private void RefreshPanel(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.MountOrRefresh(root, engine);
        }
    }

    private void ClearPanel(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.ClearIfOwned(root);
        }
    }

    private void ResetScenarioRefs()
    {
        _hero = Entity.Null;
        _dummy = Entity.Null;
        _vendor = Entity.Null;
        _equipment = Entity.Null;
        _stash = Entity.Null;
        _vendorGrid = Entity.Null;
        _backpackItem = Entity.Null;
        _secureItem = Entity.Null;
        _rifleItem = Entity.Null;
    }

    private AbilitySystem Ability(GameEngine engine)
    {
        return new AbilitySystem(
            engine.World,
            engine.GetService(CoreServiceKeys.EffectRequestQueue),
            engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry),
            engine.GetService(CoreServiceKeys.TagOps));
    }

    private InventoryRuntimeService Inventory(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.InventoryRuntimeService)
            ?? throw new InvalidOperationException("InventoryRuntimeService missing.");
    }

    private int Layout(GameEngine engine, string id)
    {
        int layoutId = engine.GetService(CoreServiceKeys.ItemLayoutRegistry)?.GetId(id) ?? 0;
        if (layoutId <= 0) throw new InvalidOperationException($"Missing layout '{id}'.");
        return layoutId;
    }

    private Entity Mounted(GameEngine engine, Entity item, string mountId)
    {
        if (Inventory(engine).TryFindMountedContainer(item, mountId, out Entity container))
        {
            return container;
        }

        throw new InvalidOperationException($"Mounted container '{mountId}' missing.");
    }

    private float ReadAttr(GameEngine engine, Entity entity, int attrId)
    {
        return engine.World.Has<AttributeBuffer>(entity) ? engine.World.Get<AttributeBuffer>(entity).GetCurrent(attrId) : 0f;
    }

    private int ActiveEffects(GameEngine engine, Entity entity)
    {
        return engine.World.Has<ActiveEffectContainer>(entity) ? engine.World.Get<ActiveEffectContainer>(entity).Count : 0;
    }

    private string[] BuildStats(GameEngine engine)
    {
        return new[]
        {
            $"Hero Health {ReadAttr(engine, _hero, _healthAttrId):0.0}",
            $"Hero Shield {ReadAttr(engine, _hero, _shieldAttrId):0.0}",
            $"Hero MoveSpeed {ReadAttr(engine, _hero, _moveSpeedAttrId):0.0}",
            $"Hero AttackDamage {ReadAttr(engine, _hero, _attackAttrId):0.0}",
            $"Hero Armor {ReadAttr(engine, _hero, _armorAttrId):0.0}",
            $"Dummy Health {ReadAttr(engine, _dummy, _healthAttrId):0.0}"
        };
    }

    private string[] BuildAbilities(GameEngine engine)
    {
        var lines = new List<string>();
        if (engine.World.Has<ItemGrantedSlotBuffer>(_hero))
        {
            ItemGrantedSlotBuffer buffer = engine.World.Get<ItemGrantedSlotBuffer>(_hero);
            for (int slot = 0; slot < ItemGrantedSlotBuffer.CAPACITY; slot++)
            {
                if (!buffer.HasOverride(slot)) continue;
                lines.Add($"Slot {slot}: {AbilityIdRegistry.GetName(buffer.GetOverride(slot).AbilityId)} <- {ItemLabel(engine, buffer.GetSourceItem(slot))}");
            }
        }

        if (lines.Count == 0) lines.Add("No item-granted abilities active.");
        return lines.ToArray();
    }

    private string[] BuildBuffs(GameEngine engine)
    {
        var lines = new List<string>();
        if (engine.World.Has<ActiveEffectContainer>(_hero))
        {
            ActiveEffectContainer active = engine.World.Get<ActiveEffectContainer>(_hero);
            for (int i = 0; i < active.Count; i++)
            {
                Entity effect = active.GetEntity(i);
                if (!engine.World.IsAlive(effect) || !engine.World.Has<EffectTemplateRef>(effect) || !engine.World.Has<EffectContext>(effect))
                {
                    continue;
                }

                int templateId = engine.World.Get<EffectTemplateRef>(effect).TemplateId;
                Entity sourceItem = engine.World.Get<EffectContext>(effect).TargetContext;
                lines.Add($"{EffectTemplateIdRegistry.GetName(templateId)} <- {ItemLabel(engine, sourceItem)}");
            }
        }

        if (engine.World.Has<GameplayTagContainer>(_hero))
        {
            GameplayTagContainer tags = engine.World.Get<GameplayTagContainer>(_hero);
            for (int tagId = 1; tagId <= GameplayTagContainer.MAX_TAG_ID; tagId++)
            {
                if (tags.HasTag(tagId)) lines.Add($"Tag {TagRegistry.GetName(tagId)}");
            }
        }

        if (lines.Count == 0) lines.Add("No passive effects active.");
        return lines.ToArray();
    }

    private string[] BuildEquipment(GameEngine engine)
    {
        Entity sockets = Mounted(engine, _rifleItem, "sockets");
        return new[]
        {
            $"feet: {SlotLabel(engine, _equipment, "feet")}",
            $"amulet: {SlotLabel(engine, _equipment, "amulet")}",
            $"ring_left: {SlotLabel(engine, _equipment, "ring_left")}",
            $"ring_right: {SlotLabel(engine, _equipment, "ring_right")}",
            $"charm: {SlotLabel(engine, _equipment, "charm")}",
            $"back: {SlotLabel(engine, _equipment, "back")}",
            $"primary_weapon: {SlotLabel(engine, _equipment, "primary_weapon")}",
            $"secure: {SlotLabel(engine, _equipment, "secure")}",
            $"magwell: {SlotLabel(engine, sockets, "magwell")}",
            $"optic: {SlotLabel(engine, sockets, "optic")}",
            $"muzzle: {SlotLabel(engine, sockets, "muzzle")}",
            $"underbarrel: {SlotLabel(engine, sockets, "underbarrel")}"
        };
    }

    private string[] BuildContainer(GameEngine engine, Entity container, string label)
    {
        var lines = new List<string> { $"{label} #{container.Id}" };
        _items.Clear();
        Inventory(engine).CollectItemsInContainer(container, _items);
        _items.Sort((a, b) => a.Id.CompareTo(b.Id));
        for (int i = 0; i < _items.Count; i++) lines.Add(ItemPlacement(engine, _items[i]));
        if (_items.Count == 0) lines.Add("(empty)");
        return lines.ToArray();
    }

    private Entity SlotItem(GameEngine engine, Entity container, string slotId)
    {
        if (!engine.World.Has<ItemContainerCm>(container)) return Entity.Null;
        var layouts = engine.GetService(CoreServiceKeys.ItemLayoutRegistry) ?? throw new InvalidOperationException("ItemLayoutRegistry missing.");
        int layoutId = engine.World.Get<ItemContainerCm>(container).LayoutId;
        if (!layouts.TryGet(layoutId, out ItemLayoutDefinition layout) || !layout.TryGetNamedSlotIndex(slotId, out int index)) return Entity.Null;
        _items.Clear();
        Inventory(engine).CollectItemsInContainer(container, _items);
        for (int i = 0; i < _items.Count; i++)
        {
            Entity item = _items[i];
            if (engine.World.Has<ItemLocationCm>(item))
            {
                ItemLocationCm location = engine.World.Get<ItemLocationCm>(item);
                if (location.PlacementKind == ItemPlacementKind.NamedSlot && location.NamedSlotIndex == index) return item;
            }
        }
        return Entity.Null;
    }

    private Entity FindByDef(GameEngine engine, int defId, Entity container)
    {
        _items.Clear();
        Inventory(engine).CollectItemsInContainer(container, _items);
        for (int i = 0; i < _items.Count; i++)
        {
            Entity item = _items[i];
            if (engine.World.Has<ItemInstanceCm>(item) && engine.World.Get<ItemInstanceCm>(item).DefinitionId == defId) return item;
        }
        return Entity.Null;
    }

    private Entity FindPlayerItem(GameEngine engine, int defId)
    {
        Entity backpack = Mounted(engine, _backpackItem, "storage");
        Entity secure = Mounted(engine, _secureItem, "vault");
        return FindByDef(engine, defId, _stash) is var a && a != Entity.Null ? a :
               FindByDef(engine, defId, backpack) is var b && b != Entity.Null ? b :
               FindByDef(engine, defId, secure);
    }

    private void GrantToStash(GameEngine engine, int defId, int amount)
    {
        Entity existing = FindByDef(engine, defId, _stash);
        if (existing != Entity.Null && engine.World.Has<ItemInstanceCm>(existing))
        {
            ref ItemInstanceCm instance = ref engine.World.Get<ItemInstanceCm>(existing);
            instance.StackCount += amount;
            return;
        }

        Put(engine, CreateTrackedItem(engine, defId, stackCount: amount), _stash, 5, 0);
    }

    private Entity CreateTrackedContainer(GameEngine engine, Entity owner, ItemContainerOwnerKind ownerKind, string layoutId)
    {
        Entity container = Inventory(engine).CreateContainer(owner, ownerKind, Layout(engine, layoutId));
        TrackMapEntity(engine, container);
        return container;
    }

    private Entity CreateTrackedItem(GameEngine engine, int definitionId, int stackCount = 1, int charges = 0, int durability = 0)
    {
        Entity item = Inventory(engine).CreateItem(definitionId, stackCount, charges, durability);
        TrackItemTree(engine, item);
        return item;
    }

    private void TrackItemTree(GameEngine engine, Entity item)
    {
        TrackMapEntity(engine, item);
        var mountedContainers = new List<Entity>(4);
        Inventory(engine).CollectMountedContainers(item, mountedContainers);
        for (int i = 0; i < mountedContainers.Count; i++)
        {
            TrackMapEntity(engine, mountedContainers[i]);
        }
    }

    private void TrackMapEntity(GameEngine engine, Entity entity)
    {
        if (!engine.World.IsAlive(entity) || engine.CurrentMapSession == null)
        {
            return;
        }

        if (engine.World.Has<MapEntity>(entity))
        {
            engine.World.Set(entity, new MapEntity { MapId = engine.CurrentMapSession.MapId });
            return;
        }

        engine.World.Add(entity, new MapEntity { MapId = engine.CurrentMapSession.MapId });
    }

    private string SlotLabel(GameEngine engine, Entity container, string slotId)
    {
        Entity item = SlotItem(engine, container, slotId);
        return item == Entity.Null ? "(empty)" : ItemLabel(engine, item);
    }

    private string ItemLabel(GameEngine engine, Entity item)
    {
        if (!engine.World.IsAlive(item) || !engine.World.Has<ItemInstanceCm>(item)) return "(missing)";
        ItemInstanceCm instance = engine.World.Get<ItemInstanceCm>(item);
        var definitions = engine.GetService(CoreServiceKeys.ItemDefinitionRegistry) ?? throw new InvalidOperationException("ItemDefinitionRegistry missing.");
        return definitions.TryGet(instance.DefinitionId, out ItemDefinition definition) ? definition.DisplayName : $"Item#{item.Id}";
    }

    private string ItemPlacement(GameEngine engine, Entity item)
    {
        string label = ItemLabel(engine, item);
        ItemInstanceCm instance = engine.World.Get<ItemInstanceCm>(item);
        ItemLocationCm location = engine.World.Get<ItemLocationCm>(item);
        string stack = instance.StackCount > 1 ? $" x{instance.StackCount}" : string.Empty;
        string charges = instance.Charges > 0 ? $" [{instance.Charges}]" : string.Empty;
        return location.PlacementKind == ItemPlacementKind.NamedSlot
            ? $"{label}{stack}{charges} @slot#{location.NamedSlotIndex}"
            : $"{label}{stack}{charges} @({location.GridX},{location.GridY}) rot={location.RotationQuarterTurns}";
    }

    private void Log(string text)
    {
        _log.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {text}");
        if (_log.Count > 10) _log.RemoveRange(10, _log.Count - 10);
    }
}
