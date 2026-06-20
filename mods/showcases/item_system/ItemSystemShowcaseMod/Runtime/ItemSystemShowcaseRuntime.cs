using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using ItemSystemShowcaseMod.UI;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Exchange;
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
    private Entity _forgeAmuletItem = Entity.Null;
    private Entity _selectedItem = Entity.Null;

    private int _healthAttrId;
    private int _shieldAttrId;
    private int _moveSpeedAttrId;
    private int _attackAttrId;
    private int _armorAttrId;

    private int _bootsDefId;
    private int _amuletDefId;
    private int _forgeAmuletDefId;
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
    private int _crimsonGemDefId;
    private int _azureGemDefId;
    private int _buyApAmmoExchangeId;
    private int _sellArtifactExchangeId;
    private int _forgeCrimsonGemExchangeId;
    private int _forgeAzureGemExchangeId;

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
        ItemSystemShowcaseSceneKind sceneKind = ItemSystemShowcaseIds.GetSceneKind(engine.CurrentMapSession?.MapId.Value);
        InventoryRuntimeService inventory = Inventory(engine);
        Entity backpack = Mounted(engine, _backpackItem, "storage");
        Entity secure = Mounted(engine, _secureItem, "vault");
        NormalizeSelection(engine);
        string heroSummary = $"Loadout Pilot | passive effects {ActiveEffects(engine, _hero)} | actor #{_hero.Id}";
        string creditsSummary = $"Credits {inventory.CountStackUnits(_hero, _creditDefId)} | FMJ {inventory.CountStackUnits(_hero, _ammoDefId)} | AP {inventory.CountStackUnits(_hero, _apAmmoDefId)}";
        string dummySummary = $"Target Dummy health {ReadAttr(engine, _dummy, _healthAttrId):0.0}";
        string selectionSummary = BuildSelectionSummary(engine);
        bool showSceneNavigation = !ItemSystemShowcaseFocusPack.IsFocused(ResolveFocusPack(engine));

        return sceneKind switch
        {
            ItemSystemShowcaseSceneKind.LoadoutGarage => new ItemSystemShowcasePanelState(
                SceneKind: sceneKind,
                Header: "Loadout Garage",
                SceneSummary: "Swap one slot at a time, then read the build as a player would: paper doll, stats, passives, and active buttons all move together.",
                ShowSceneNavigation: showSceneNavigation,
                HeroSummary: heroSummary,
                CreditsSummary: $"MoveSpeed {ReadAttr(engine, _hero, _moveSpeedAttrId):0.0} | Attack {ReadAttr(engine, _hero, _attackAttrId):0.0} | Armor {ReadAttr(engine, _hero, _armorAttrId):0.0}",
                DummySummary: "Use this room to understand what one equip change actually does before moving on.",
                SelectionSummary: selectionSummary,
                PrimaryLines: BuildStats(engine),
                SecondaryLines: BuildLoadoutEquipment(engine),
                TertiaryLines: BuildAbilities(engine),
                QuaternaryLines: BuildBuffs(engine),
                LogLines: _log.ToArray()),
            ItemSystemShowcaseSceneKind.WeaponBench => new ItemSystemShowcasePanelState(
                SceneKind: sceneKind,
                Header: "Weapon Bench",
                SceneSummary: "One rifle, one magazine, one target. Socket rules, ammo routing, reload, and firing all stay grounded in the same item entity tree.",
                ShowSceneNavigation: showSceneNavigation,
                HeroSummary: $"Primary weapon {SlotLabel(engine, _equipment, "primary_weapon")} | {dummySummary}",
                CreditsSummary: BuildWeaponSummary(engine),
                DummySummary: "Attach parts, top off the mag, then fire to see item-granted slot 0 resolve against the dummy.",
                SelectionSummary: selectionSummary,
                PrimaryLines: BuildWeaponReadout(engine),
                SecondaryLines: BuildAmmoSupply(engine, backpack),
                TertiaryLines: BuildWeaponSockets(engine),
                QuaternaryLines: BuildEquipment(engine),
                LogLines: _log.ToArray()),
            ItemSystemShowcaseSceneKind.RaidLoop => new ItemSystemShowcasePanelState(
                SceneKind: sceneKind,
                Header: "Raid Loop",
                SceneSummary: "This room is about extraction-facing decisions: what stays on the body, what moves into secure storage, what gets sold, and how ammo gets repacked.",
                ShowSceneNavigation: showSceneNavigation,
                HeroSummary: heroSummary,
                CreditsSummary: creditsSummary,
                DummySummary: "Think like a player between raids: keep value safe, move bulk to stash, and prep the next loadout.",
                SelectionSummary: selectionSummary,
                PrimaryLines: BuildContainer(engine, backpack, "Backpack"),
                SecondaryLines: BuildContainer(engine, secure, "Secure Case"),
                TertiaryLines: BuildContainer(engine, _stash, "Stash"),
                QuaternaryLines: BuildContainer(engine, _vendorGrid, "Vendor"),
                LogLines: _log.ToArray()),
            ItemSystemShowcaseSceneKind.ForgeSocketLab => new ItemSystemShowcasePanelState(
                SceneKind: sceneKind,
                Header: "Forge & Socket Lab",
                SceneSummary: "Craft socket gems from live resources, then click them into a socketed amulet to watch GAS passives update from nested item containers.",
                ShowSceneNavigation: showSceneNavigation,
                HeroSummary: heroSummary,
                CreditsSummary: $"Forge amulet {SlotLabel(engine, _equipment, "amulet")} | Credits {inventory.CountStackUnits(_hero, _creditDefId)} | Artifact {inventory.CountStackUnits(_hero, _artifactDefId)}",
                DummySummary: "This room proves that non-weapon sockets and crafting still stay inside the same item entity architecture.",
                SelectionSummary: selectionSummary,
                PrimaryLines: BuildForgeReadout(engine),
                SecondaryLines: BuildContainer(engine, Mounted(engine, _forgeAmuletItem, "gems"), "Gem Sockets"),
                TertiaryLines: BuildForgeRecipeLines(engine),
                QuaternaryLines: BuildContainer(engine, _stash, "Forge Stash"),
                LogLines: _log.ToArray()),
            _ => new ItemSystemShowcasePanelState(
                SceneKind: sceneKind,
                Header: "Ludots Item Demo Pack",
                SceneSummary: "Four short rooms teach one shared architecture. Pick the player problem you care about instead of reading a mega dashboard first.",
                ShowSceneNavigation: showSceneNavigation,
                HeroSummary: heroSummary,
                CreditsSummary: "Start with a focused route: build, bench, raid loop, or forge. The same ECS / GAS rules stay underneath every room.",
                DummySummary: string.Empty,
                SelectionSummary: selectionSummary,
                PrimaryLines: BuildHubLoadoutLines(),
                SecondaryLines: BuildHubWeaponLines(),
                TertiaryLines: BuildHubRaidLines(),
                QuaternaryLines: BuildHubForgeLines(),
                LogLines: _log.ToArray())
        };
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
        ExchangeExecutionResult result = ExecuteExchange(engine, _buyApAmmoExchangeId);
        Log(result.Status switch
        {
            ExchangeExecutionStatus.Success => "Bought one AP ammo stack for 20 credits.",
            ExchangeExecutionStatus.InsufficientInput => "Vendor purchase blocked: insufficient credits.",
            ExchangeExecutionStatus.MissingSourceItem => "Vendor is out of AP ammo.",
            ExchangeExecutionStatus.OutputBlocked => "Could not place purchased AP ammo into stash.",
            _ => $"Vendor purchase failed: {result.Status}."
        });
    }

    public void SellArtifact(GameEngine engine)
    {
        EnsureScenario(engine);
        ExchangeExecutionResult result = ExecuteExchange(engine, _sellArtifactExchangeId);
        Log(result.Status switch
        {
            ExchangeExecutionStatus.Success => "Sold Raid Artifact and received 45 credits.",
            ExchangeExecutionStatus.MissingSourceItem => "No Raid Artifact available to sell.",
            ExchangeExecutionStatus.OutputBlocked when result.DetailIndex == 0 => "Could not transfer artifact to vendor.",
            ExchangeExecutionStatus.OutputBlocked => "Could not place sale credits into stash.",
            _ => $"Artifact sale failed: {result.Status}."
        });
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

    public ItemSystemShowcaseBoardModel[] BuildBoards(GameEngine engine, ItemSystemShowcaseSceneKind sceneKind)
    {
        EnsureScenario(engine);
        NormalizeSelection(engine);
        Entity backpack = Mounted(engine, _backpackItem, "storage");
        Entity secure = Mounted(engine, _secureItem, "vault");

        return sceneKind switch
        {
            ItemSystemShowcaseSceneKind.LoadoutGarage => new[]
            {
                BuildSlotBoard(engine, _equipment, "Loadout Slots", "#F0C36B"),
                BuildGridBoard(engine, _stash, "Stash Grid", "#7DD3FC"),
                BuildGridBoard(engine, backpack, "Backpack Grid", "#8DE3AE")
            },
            ItemSystemShowcaseSceneKind.WeaponBench => new[]
            {
                BuildSlotBoard(engine, Mounted(engine, _rifleItem, "sockets"), "Weapon Attachments", "#F0C36B"),
                BuildGridBoard(engine, backpack, "Bench Backpack", "#7DD3FC"),
                BuildGridBoard(engine, _stash, "Bench Stash", "#8DE3AE")
            },
            ItemSystemShowcaseSceneKind.RaidLoop => new[]
            {
                BuildGridBoard(engine, backpack, "Backpack", "#F0C36B"),
                BuildGridBoard(engine, secure, "Secure Case", "#7DD3FC"),
                BuildGridBoard(engine, _stash, "Stash", "#8DE3AE"),
                BuildGridBoard(engine, _vendorGrid, "Vendor", "#FFB38A")
            },
            ItemSystemShowcaseSceneKind.ForgeSocketLab => new[]
            {
                BuildSlotBoard(engine, Mounted(engine, _forgeAmuletItem, "gems"), "Gem Sockets", "#F0C36B"),
                BuildGridBoard(engine, _stash, "Forge Stash", "#7DD3FC"),
                BuildRecipeBoard()
            },
            _ => Array.Empty<ItemSystemShowcaseBoardModel>()
        };
    }

    public void HandleBoardClick(GameEngine engine, ItemSystemShowcaseClickTarget target)
    {
        EnsureScenario(engine);
        NormalizeSelection(engine);

        switch (target.Kind)
        {
            case ItemSystemShowcaseClickTargetKind.GridCell:
                HandleGridCellClick(engine, target);
                break;
            case ItemSystemShowcaseClickTargetKind.SlotCell:
                HandleSlotCellClick(engine, target);
                break;
            case ItemSystemShowcaseClickTargetKind.Recipe:
                ExecuteRecipe(engine, target.Id);
                break;
        }
    }

    private void HandleGridCellClick(GameEngine engine, ItemSystemShowcaseClickTarget target)
    {
        if (target.Container == _vendorGrid)
        {
            Log("Vendor inventory is read-only. Use Buy AP Ammo / Sell Artifact buttons.");
            return;
        }

        if (target.Item != Entity.Null && engine.World.IsAlive(target.Item))
        {
            SelectItem(engine, target.Item);
            return;
        }

        if (_selectedItem == Entity.Null || !engine.World.IsAlive(_selectedItem))
        {
            Log("Select an item first, then click an empty grid cell to place it.");
            return;
        }

        bool moved = Inventory(engine).TryMoveItemToGrid(_selectedItem, target.Container, target.GridX, target.GridY);
        Log(moved
            ? $"Moved {ItemLabel(engine, _selectedItem)} to {ContainerLabel(engine, target.Container)} ({target.GridX},{target.GridY})."
            : $"Could not place {ItemLabel(engine, _selectedItem)} into that grid cell.");
    }

    private void HandleSlotCellClick(GameEngine engine, ItemSystemShowcaseClickTarget target)
    {
        if (target.Item != Entity.Null && engine.World.IsAlive(target.Item) && (_selectedItem == Entity.Null || _selectedItem == target.Item))
        {
            SelectItem(engine, target.Item);
            return;
        }

        if (_selectedItem == Entity.Null || !engine.World.IsAlive(_selectedItem))
        {
            if (target.Item != Entity.Null && engine.World.IsAlive(target.Item))
            {
                SelectItem(engine, target.Item);
            }
            else
            {
                Log($"Select an item before targeting slot '{target.Id}'.");
            }

            return;
        }

        bool moved = Inventory(engine).TryMoveItemToNamedSlot(_selectedItem, target.Container, target.Id);
        Log(moved
            ? $"Placed {ItemLabel(engine, _selectedItem)} into slot '{target.Id}'."
            : $"Could not place {ItemLabel(engine, _selectedItem)} into slot '{target.Id}'.");
    }

    private void SelectItem(GameEngine engine, Entity item)
    {
        if (!engine.World.IsAlive(item) || !engine.World.Has<ItemInstanceCm>(item))
        {
            _selectedItem = Entity.Null;
            Log("Selection cleared because the item no longer exists.");
            return;
        }

        _selectedItem = item;
        Log($"Selected {ItemLabel(engine, item)}.");
    }

    private void ExecuteRecipe(GameEngine engine, string recipeId)
    {
        int operationId = recipeId switch
        {
            "forge_crimson_gem" => _forgeCrimsonGemExchangeId,
            "forge_azure_gem" => _forgeAzureGemExchangeId,
            _ => 0
        };

        if (operationId <= 0)
        {
            Log($"Unknown recipe '{recipeId}'.");
            return;
        }

        ExchangeExecutionResult result = ExecuteExchange(engine, operationId);
        Log((recipeId, result.Status) switch
        {
            ("forge_crimson_gem", ExchangeExecutionStatus.Success) => "Forged one Crimson Gem from artifact salvage and credits.",
            ("forge_crimson_gem", ExchangeExecutionStatus.OutputBlocked) => "Crimson Gem recipe blocked: need one free stash placement for the crafted gem.",
            ("forge_crimson_gem", ExchangeExecutionStatus.InsufficientInput) => "Crimson Gem recipe blocked: need 1 Raid Artifact and 20 credits.",
            ("forge_azure_gem", ExchangeExecutionStatus.Success) => "Forged one Azure Gem from FMJ scrap and credits.",
            ("forge_azure_gem", ExchangeExecutionStatus.OutputBlocked) => "Azure Gem recipe blocked: need one free stash placement for the crafted gem.",
            ("forge_azure_gem", ExchangeExecutionStatus.InsufficientInput) => "Azure Gem recipe blocked: need 30 FMJ and 10 credits.",
            _ => $"Recipe execution failed: {result.Status}."
        });
    }

    private void EnsureScenario(GameEngine engine)
    {
        if (engine.World.IsAlive(_hero) && engine.World.IsAlive(_stash) && engine.World.IsAlive(_rifleItem))
        {
            return;
        }

        ResolveIds(engine);
        Seed(engine, ItemSystemShowcaseIds.GetSceneKind(engine.CurrentMapSession?.MapId.Value));
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
        _forgeAmuletDefId = definitions.GetId("itm_forge_amulet");
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
        _crimsonGemDefId = definitions.GetId("itm_gem_crimson");
        _azureGemDefId = definitions.GetId("itm_gem_azure");

        var exchanges = engine.GetService(CoreServiceKeys.ExchangeOperationRegistry)
            ?? throw new InvalidOperationException("ExchangeOperationRegistry missing.");
        _buyApAmmoExchangeId = exchanges.GetId("item_showcase.buy_ap_ammo");
        _sellArtifactExchangeId = exchanges.GetId("item_showcase.sell_artifact");
        _forgeCrimsonGemExchangeId = exchanges.GetId("item_showcase.forge_crimson_gem");
        _forgeAzureGemExchangeId = exchanges.GetId("item_showcase.forge_azure_gem");
        if (_buyApAmmoExchangeId <= 0 ||
            _sellArtifactExchangeId <= 0 ||
            _forgeCrimsonGemExchangeId <= 0 ||
            _forgeAzureGemExchangeId <= 0)
        {
            throw new InvalidOperationException("Item showcase Exchange operations are missing from config.");
        }
    }

    private void Seed(GameEngine engine, ItemSystemShowcaseSceneKind sceneKind)
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
        _forgeAmuletItem = sceneKind == ItemSystemShowcaseSceneKind.ForgeSocketLab
            ? CreateTrackedItem(engine, _forgeAmuletDefId)
            : Entity.Null;
        EquipNamed(engine, sceneKind == ItemSystemShowcaseSceneKind.ForgeSocketLab ? _forgeAmuletItem : CreateTrackedItem(engine, _amuletDefId), "amulet");
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

        if (sceneKind == ItemSystemShowcaseSceneKind.ForgeSocketLab)
        {
            Put(engine, CreateTrackedItem(engine, _azureGemDefId), _stash, 5, 0);
            Put(engine, CreateTrackedItem(engine, _artifactDefId), _stash, 6, 0);
            Put(engine, CreateTrackedItem(engine, _artifactDefId), _stash, 8, 0);
        }

        if (!world.Has<InventoryEquipmentDirtyTag>(_hero))
        {
            world.Add(_hero, new InventoryEquipmentDirtyTag());
        }

        _selectedItem = Entity.Null;
        _log.Clear();
        Log("Scenario seeded from config-driven shapes, layouts, and definitions.");
        Log("Coverage live: MOBA boots/mythic, ARPG rings/charm, extraction stash/secure, rifle sockets, ammo stacks, vendor trade, and forge sockets.");
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
        _forgeAmuletItem = Entity.Null;
        _selectedItem = Entity.Null;
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

    private ExchangeExecutionResult ExecuteExchange(GameEngine engine, int operationId, int scopeKey = 0)
    {
        var exchange = engine.GetService(CoreServiceKeys.ExchangeRuntime)
            ?? throw new InvalidOperationException("ExchangeRuntime missing.");
        var context = new ExchangeExecutionContext(_hero, _vendor, Entity.Null, scopeKey);
        return exchange.TryExecute(new ExchangeOperationKey(operationId, scopeKey), in context);
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

    private string[] BuildLoadoutEquipment(GameEngine engine)
    {
        return new[]
        {
            $"feet: {SlotLabel(engine, _equipment, "feet")}",
            $"amulet: {SlotLabel(engine, _equipment, "amulet")}",
            $"ring_left: {SlotLabel(engine, _equipment, "ring_left")}",
            $"ring_right: {SlotLabel(engine, _equipment, "ring_right")}",
            $"charm: {SlotLabel(engine, _equipment, "charm")}",
            $"back: {SlotLabel(engine, _equipment, "back")}",
            $"secure: {SlotLabel(engine, _equipment, "secure")}"
        };
    }

    private string BuildWeaponSummary(GameEngine engine)
    {
        Entity sockets = Mounted(engine, _rifleItem, "sockets");
        Entity mag = SlotItem(engine, sockets, "magwell");
        int magCharges = mag != Entity.Null && engine.World.Has<ItemInstanceCm>(mag) ? engine.World.Get<ItemInstanceCm>(mag).Charges : 0;
        return $"Magazine {magCharges}/30 | FMJ {Inventory(engine).CountStackUnits(_hero, _ammoDefId)} | AP {Inventory(engine).CountStackUnits(_hero, _apAmmoDefId)}";
    }

    private string[] BuildWeaponReadout(GameEngine engine)
    {
        return new[]
        {
            $"Dummy Health {ReadAttr(engine, _dummy, _healthAttrId):0.0}",
            $"Hero AttackDamage {ReadAttr(engine, _hero, _attackAttrId):0.0}",
            $"Hero Armor {ReadAttr(engine, _hero, _armorAttrId):0.0}",
            $"Primary weapon: {SlotLabel(engine, _equipment, "primary_weapon")}"
        };
    }

    private string[] BuildAmmoSupply(GameEngine engine, Entity backpack)
    {
        return new[]
        {
            $"Hero credits: {Inventory(engine).CountStackUnits(_hero, _creditDefId)}",
            $"Hero FMJ total: {Inventory(engine).CountStackUnits(_hero, _ammoDefId)}",
            $"Hero AP total: {Inventory(engine).CountStackUnits(_hero, _apAmmoDefId)}",
            $"Backpack line: {FirstNonHeaderLine(BuildContainer(engine, backpack, "Backpack"))}",
            $"Stash line: {FirstNonHeaderLine(BuildContainer(engine, _stash, "Stash"))}"
        };
    }

    private string[] BuildWeaponSockets(GameEngine engine)
    {
        Entity sockets = Mounted(engine, _rifleItem, "sockets");
        return new[]
        {
            $"magwell: {SlotLabel(engine, sockets, "magwell")}",
            $"optic: {SlotLabel(engine, sockets, "optic")}",
            $"muzzle: {SlotLabel(engine, sockets, "muzzle")}",
            $"underbarrel: {SlotLabel(engine, sockets, "underbarrel")}"
        };
    }

    private string[] BuildForgeReadout(GameEngine engine)
    {
        Entity gems = Mounted(engine, _forgeAmuletItem, "gems");
        return new[]
        {
            $"Hero Shield {ReadAttr(engine, _hero, _shieldAttrId):0.0}",
            $"Hero AttackDamage {ReadAttr(engine, _hero, _attackAttrId):0.0}",
            $"Hero Armor {ReadAttr(engine, _hero, _armorAttrId):0.0}",
            $"socket_alpha: {SlotLabel(engine, gems, "socket_alpha")}",
            $"socket_beta: {SlotLabel(engine, gems, "socket_beta")}"
        };
    }

    private string[] BuildForgeRecipeLines(GameEngine engine)
    {
        return new[]
        {
            "Forge Crimson Gem: consume 1 Raid Artifact + 20 credits.",
            "Forge Azure Gem: consume 30 FMJ + 10 credits.",
            $"Current credits: {Inventory(engine).CountStackUnits(_hero, _creditDefId)}",
            $"Current artifacts: {Inventory(engine).CountStackUnits(_hero, _artifactDefId)}",
            $"Current FMJ: {Inventory(engine).CountStackUnits(_hero, _ammoDefId)}"
        };
    }

    private string BuildSelectionSummary(GameEngine engine)
    {
        NormalizeSelection(engine);
        if (_selectedItem == Entity.Null)
        {
            return "Selection: none. Click an item to inspect it, then click a slot or empty cell to move it.";
        }

        ItemInstanceCm instance = engine.World.Get<ItemInstanceCm>(_selectedItem);
        ItemLocationCm location = engine.World.Get<ItemLocationCm>(_selectedItem);
        string stack = instance.StackCount > 1 ? $" x{instance.StackCount}" : string.Empty;
        string charges = instance.Charges > 0 ? $" | charges {instance.Charges}" : string.Empty;
        return $"Selection: {ItemLabel(engine, _selectedItem)}{stack} in {ContainerLabel(engine, location.Container)}{charges}.";
    }

    private static string FirstNonHeaderLine(IReadOnlyList<string> lines)
    {
        return lines.Count > 1 ? lines[1] : "(empty)";
    }

    private string ResolveFocusPack(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(ItemSystemShowcaseFocusPack.Key, out object? focus) && focus is string focusPack
            ? ItemSystemShowcaseFocusPack.Normalize(focusPack)
            : ItemSystemShowcaseFocusPack.AllInOne;
    }

    private static string[] BuildHubLoadoutLines()
    {
        return new[]
        {
            "Feel the hero build change one slot at a time.",
            "Toggle boots to drop MoveSpeed and remove the granted active.",
            "Equip the Duelist Ring to raise AttackDamage.",
            "Cast Mythic Pulse and Second Wind from item-granted slots."
        };
    }

    private static string[] BuildHubWeaponLines()
    {
        return new[]
        {
            "Treat the rifle like one object players care about.",
            "Attach the vertical grip into the underbarrel socket.",
            "Reload from shared FMJ stacks without leaving the item runtime.",
            "Fire through slot 0 and watch dummy HP drop."
        };
    }

    private static string[] BuildHubRaidLines()
    {
        return new[]
        {
            "Run a compact extraction prep loop.",
            "Move the artifact between stash and secure case.",
            "Buy AP ammo from the vendor with credits.",
            "Sell loot back and split FMJ into the backpack."
        };
    }

    private static string[] BuildHubForgeLines()
    {
        return new[]
        {
            "Craft and socket without leaving the item runtime.",
            "Forge gems from live resources in the stash.",
            "Insert gems into a socketed amulet and watch passives update.",
            "Use the same nested container logic as weapon attachments."
        };
    }

    private static string[] BuildHubSharedRules()
    {
        return new[]
        {
            "One architecture underneath all four rooms.",
            "Equipment, backpack, secure case, vendor, rifle sockets, and gem sockets are all containers plus item placements.",
            "Passive bonuses stay in GAS as real effects and tags.",
            "Active item powers flow through item-granted ability slots instead of a second runtime."
        };
    }

    private ItemSystemShowcaseBoardModel BuildGridBoard(GameEngine engine, Entity container, string title, string accent)
    {
        ItemLayoutDefinition layout = LayoutDef(engine, container);
        ItemSystemShowcaseBoardCellModel[][] rows = new ItemSystemShowcaseBoardCellModel[layout.Height][];
        for (int y = 0; y < layout.Height; y++)
        {
            rows[y] = new ItemSystemShowcaseBoardCellModel[layout.Width];
            for (int x = 0; x < layout.Width; x++)
            {
                rows[y][x] = new ItemSystemShowcaseBoardCellModel(
                    PrimaryText: layout.IsBlockedCell(x, y) ? "XX" : "·",
                    SecondaryText: string.Empty,
                    FillColor: layout.IsBlockedCell(x, y) ? "#2B1620" : "#13202D",
                    BorderColor: "#29435A",
                    IsSelected: false,
                    Target: new ItemSystemShowcaseClickTarget(ItemSystemShowcaseClickTargetKind.GridCell, Entity.Null, container, string.Empty, x, y));
            }
        }

        var placedItems = new List<Entity>(32);
        Inventory(engine).CollectItemsInContainer(container, placedItems);
        placedItems.Sort((a, b) => a.Id.CompareTo(b.Id));
        for (int i = 0; i < placedItems.Count; i++)
        {
            Entity item = placedItems[i];
            if (!engine.World.Has<ItemLocationCm>(item) || !engine.World.Has<ItemInstanceCm>(item))
            {
                continue;
            }

            ItemLocationCm location = engine.World.Get<ItemLocationCm>(item);
            if (location.PlacementKind != ItemPlacementKind.Grid)
            {
                continue;
            }

            ItemShapeRotation shape = ShapeFor(engine, item, location.RotationQuarterTurns);
            for (int dy = 0; dy < shape.Height; dy++)
            {
                for (int dx = 0; dx < shape.Width; dx++)
                {
                    if (!shape.IsOccupied(dx, dy))
                    {
                        continue;
                    }

                    int px = location.GridX + dx;
                    int py = location.GridY + dy;
                    if ((uint)px >= (uint)layout.Width || (uint)py >= (uint)layout.Height)
                    {
                        continue;
                    }

                    bool anchor = dx == 0 && dy == 0;
                    rows[py][px] = new ItemSystemShowcaseBoardCellModel(
                        PrimaryText: anchor ? ShortLabel(engine, item) : "■",
                        SecondaryText: anchor ? CompactSuffix(engine, item) : string.Empty,
                        FillColor: ColorForItem(engine, item),
                        BorderColor: _selectedItem == item ? "#F0C36B" : "#3D5A72",
                        IsSelected: _selectedItem == item,
                        Target: new ItemSystemShowcaseClickTarget(ItemSystemShowcaseClickTargetKind.GridCell, item, container, string.Empty, location.GridX, location.GridY));
                }
            }
        }

        return new ItemSystemShowcaseBoardModel(title, accent, ItemSystemShowcaseBoardKind.Grid, rows);
    }

    private ItemSystemShowcaseBoardModel BuildSlotBoard(GameEngine engine, Entity container, string title, string accent)
    {
        ItemLayoutDefinition layout = LayoutDef(engine, container);
        int columns = Math.Min(3, Math.Max(1, layout.NamedSlots.Length));
        int rowsCount = (layout.NamedSlots.Length + columns - 1) / columns;
        ItemSystemShowcaseBoardCellModel[][] rows = new ItemSystemShowcaseBoardCellModel[rowsCount][];
        for (int row = 0; row < rowsCount; row++)
        {
            rows[row] = new ItemSystemShowcaseBoardCellModel[columns];
            for (int col = 0; col < columns; col++)
            {
                int slotIndex = (row * columns) + col;
                if (slotIndex >= layout.NamedSlots.Length)
                {
                    rows[row][col] = new ItemSystemShowcaseBoardCellModel(
                        PrimaryText: string.Empty,
                        SecondaryText: string.Empty,
                        FillColor: "#0E1823",
                        BorderColor: "#0E1823",
                        IsSelected: false,
                        Target: new ItemSystemShowcaseClickTarget(ItemSystemShowcaseClickTargetKind.None, Entity.Null, Entity.Null, string.Empty, 0, 0));
                    continue;
                }

                ItemNamedSlotDefinition slot = layout.NamedSlots[slotIndex];
                Entity item = SlotItem(engine, container, slot.Id);
                rows[row][col] = new ItemSystemShowcaseBoardCellModel(
                    PrimaryText: slot.Label,
                    SecondaryText: item == Entity.Null ? "(empty)" : SlotLabel(engine, item),
                    FillColor: item == Entity.Null ? "#13202D" : ColorForItem(engine, item),
                    BorderColor: _selectedItem == item && item != Entity.Null ? "#F0C36B" : "#3D5A72",
                    IsSelected: _selectedItem == item && item != Entity.Null,
                    Target: new ItemSystemShowcaseClickTarget(ItemSystemShowcaseClickTargetKind.SlotCell, item, container, slot.Id, 0, 0));
            }
        }

        return new ItemSystemShowcaseBoardModel(title, accent, ItemSystemShowcaseBoardKind.Slots, rows);
    }

    private ItemSystemShowcaseBoardModel BuildRecipeBoard()
    {
        return new ItemSystemShowcaseBoardModel(
            "Forge Recipes",
            "#FFB38A",
            ItemSystemShowcaseBoardKind.Recipes,
            new[]
            {
                new[]
                {
                    new ItemSystemShowcaseBoardCellModel(
                        "Forge Crimson Gem",
                        "1 Artifact + 20 Credits",
                        "#5E4518",
                        "#C0892F",
                        false,
                        new ItemSystemShowcaseClickTarget(ItemSystemShowcaseClickTargetKind.Recipe, Entity.Null, Entity.Null, "forge_crimson_gem", 0, 0))
                },
                new[]
                {
                    new ItemSystemShowcaseBoardCellModel(
                        "Forge Azure Gem",
                        "30 FMJ + 10 Credits",
                        "#23415C",
                        "#5DA3D8",
                        false,
                        new ItemSystemShowcaseClickTarget(ItemSystemShowcaseClickTargetKind.Recipe, Entity.Null, Entity.Null, "forge_azure_gem", 0, 0))
                }
            });
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

    private void NormalizeSelection(GameEngine engine)
    {
        if (_selectedItem != Entity.Null &&
            (!engine.World.IsAlive(_selectedItem) ||
             !engine.World.Has<ItemInstanceCm>(_selectedItem) ||
             !engine.World.Has<ItemLocationCm>(_selectedItem)))
        {
            _selectedItem = Entity.Null;
        }
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

    private bool TryPlaceFreshItem(GameEngine engine, int definitionId, Entity container, int stackCount = 1, int charges = 0, int durability = 0)
    {
        Entity item = CreateTrackedItem(engine, definitionId, stackCount, charges, durability);
        if (Inventory(engine).TryAutoPlaceItem(item, container))
        {
            return true;
        }

        if (engine.World.IsAlive(item))
        {
            engine.World.Destroy(item);
        }

        return false;
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

    private string ContainerLabel(GameEngine engine, Entity container)
    {
        if (!engine.World.IsAlive(container) || !engine.World.Has<ItemContainerCm>(container))
        {
            return "missing-container";
        }

        ItemContainerCm data = engine.World.Get<ItemContainerCm>(container);
        return data.Purpose switch
        {
            ItemContainerPurpose.Backpack => "backpack",
            ItemContainerPurpose.SecureStorage => "secure",
            ItemContainerPurpose.Stash => "stash",
            ItemContainerPurpose.Vendor => "vendor",
            ItemContainerPurpose.Equipment => "equipment",
            ItemContainerPurpose.WeaponAttachment => "sockets",
            _ => $"container#{container.Id}"
        };
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

    private ItemLayoutDefinition LayoutDef(GameEngine engine, Entity container)
    {
        if (!engine.World.Has<ItemContainerCm>(container))
        {
            throw new InvalidOperationException($"Container {container.Id} is missing ItemContainerCm.");
        }

        int layoutId = engine.World.Get<ItemContainerCm>(container).LayoutId;
        var layouts = engine.GetService(CoreServiceKeys.ItemLayoutRegistry) ?? throw new InvalidOperationException("ItemLayoutRegistry missing.");
        if (!layouts.TryGet(layoutId, out ItemLayoutDefinition layout))
        {
            throw new InvalidOperationException($"Missing layout id {layoutId}.");
        }

        return layout;
    }

    private ItemShapeRotation ShapeFor(GameEngine engine, Entity item, int rotationQuarterTurns)
    {
        ItemInstanceCm instance = engine.World.Get<ItemInstanceCm>(item);
        var definitions = engine.GetService(CoreServiceKeys.ItemDefinitionRegistry) ?? throw new InvalidOperationException("ItemDefinitionRegistry missing.");
        var shapes = engine.GetService(CoreServiceKeys.ItemShapeRegistry) ?? throw new InvalidOperationException("ItemShapeRegistry missing.");
        if (!definitions.TryGet(instance.DefinitionId, out ItemDefinition definition) || !shapes.TryGet(definition.ShapeId, out ItemShapeDefinition shape))
        {
            throw new InvalidOperationException($"Missing shape for item {item.Id}.");
        }

        return shape.GetRotation(rotationQuarterTurns);
    }

    private string ShortLabel(GameEngine engine, Entity item)
    {
        string label = ItemLabel(engine, item);
        return CompactBoardLabel(label, 7);
    }

    private string SlotLabel(GameEngine engine, Entity item)
    {
        string label = ItemLabel(engine, item);
        return CompactBoardLabel(label, 10);
    }

    private static string CompactBoardLabel(string label, int maxLength)
    {
        string[] parts = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return label.Length <= maxLength ? label : label[..maxLength];
        }

        string token = parts[0];
        if (token.Length <= 2 && parts.Length > 1)
        {
            token = parts[1];
        }

        return token.Length <= maxLength ? token : token[..maxLength];
    }

    private string CompactSuffix(GameEngine engine, Entity item)
    {
        if (!engine.World.Has<ItemInstanceCm>(item))
        {
            return string.Empty;
        }

        ItemInstanceCm instance = engine.World.Get<ItemInstanceCm>(item);
        if (instance.Charges > 0)
        {
            return $"{instance.Charges}";
        }

        return instance.StackCount > 1 ? $"x{instance.StackCount}" : string.Empty;
    }

    private string ColorForItem(GameEngine engine, Entity item)
    {
        if (!engine.World.Has<ItemInstanceCm>(item))
        {
            return "#13202D";
        }

        int definitionId = engine.World.Get<ItemInstanceCm>(item).DefinitionId;
        return definitionId switch
        {
            var id when id == _creditDefId => "#5E4518",
            var id when id == _ammoDefId || id == _apAmmoDefId => "#23415C",
            var id when id == _artifactDefId => "#7D3326",
            var id when id == _crimsonGemDefId => "#7D3326",
            var id when id == _azureGemDefId => "#20493A",
            var id when id == _rifleDefId || id == _magDefId => "#263544",
            _ => "#20493A"
        };
    }

    private void Log(string text)
    {
        _log.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {text}");
        if (_log.Count > 10) _log.RemoveRange(10, _log.Count - 10);
    }
}
