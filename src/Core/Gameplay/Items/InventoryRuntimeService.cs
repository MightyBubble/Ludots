using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.Items
{
    public sealed class InventoryRuntimeService
    {
        private static readonly QueryDescription ItemQuery = new QueryDescription().WithAll<ItemInstanceCm, ItemLocationCm>();
        private static readonly QueryDescription ContainerQuery = new QueryDescription().WithAll<ItemContainerCm>();
        private static readonly QueryDescription MountedContainerQuery = new QueryDescription().WithAll<ItemMountedContainerCm, ItemContainerCm>();

        private readonly World _world;
        private readonly ItemShapeRegistry _shapes;
        private readonly ItemLayoutRegistry _layouts;
        private readonly ItemDefinitionRegistry _definitions;
        private readonly List<Entity> _ownedContainerScratch = new(32);
        private readonly List<Entity> _ownedItemScratch = new(128);
        private readonly List<Entity> _destroyItemScratch = new(64);
        private readonly List<Entity> _destroyContainerScratch = new(32);
        private readonly List<Entity> _grantContainerScratch = new(16);
        private readonly List<Entity> _grantItemScratch = new(32);
        private readonly List<Entity> _grantMountedContainerScratch = new(8);

        public InventoryRuntimeService(
            World world,
            ItemShapeRegistry shapes,
            ItemLayoutRegistry layouts,
            ItemDefinitionRegistry definitions)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _shapes = shapes ?? throw new ArgumentNullException(nameof(shapes));
            _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        }

        public bool TryGetDefinition(int definitionId, out ItemDefinition definition)
        {
            return _definitions.TryGet(definitionId, out definition);
        }

        public Entity CreateContainer(
            Entity owner,
            ItemContainerOwnerKind ownerKind,
            int layoutId,
            ItemContainerPurpose purpose = ItemContainerPurpose.None)
        {
            if (!_layouts.TryGet(layoutId, out ItemLayoutDefinition layout))
            {
                throw new InvalidOperationException($"Missing item layout id {layoutId}.");
            }

            if (purpose == ItemContainerPurpose.None)
            {
                purpose = layout.Purpose;
            }

            return _world.Create(new ItemContainerCm
            {
                LayoutId = layoutId,
                Owner = owner,
                OwnerKind = ownerKind,
                Purpose = purpose
            });
        }

        public Entity CreateMountedContainer(Entity item, int mountIndex, int layoutId, ItemContainerPurpose purpose)
        {
            Entity container = CreateContainer(item, ItemContainerOwnerKind.Item, layoutId, purpose);
            _world.Add(container, new ItemMountedContainerCm
            {
                ParentItem = item,
                MountIndex = (short)mountIndex
            });
            return container;
        }

        public Entity CreateItem(int definitionId, int stackCount = 1, int charges = 0, int durability = 0)
        {
            if (!_definitions.TryGet(definitionId, out ItemDefinition definition))
            {
                throw new InvalidOperationException($"Missing item definition id {definitionId}.");
            }

            Entity item = _world.Create(new ItemInstanceCm
            {
                DefinitionId = definitionId,
                StackCount = stackCount <= 0 ? 1 : stackCount,
                Charges = charges,
                Durability = durability
            });

            for (int i = 0; i < definition.MountedContainers.Length; i++)
            {
                ItemMountedContainerDefinition mounted = definition.MountedContainers[i];
                CreateMountedContainer(item, i, mounted.LayoutId, mounted.Purpose);
            }

            return item;
        }

        public bool TryMoveItemToNamedSlot(Entity item, Entity container, string slotId)
        {
            if (!TryGetItemAndContainer(item, container, out ItemInstanceCm itemInstance, out ItemContainerCm containerComponent))
            {
                return false;
            }

            if (!_definitions.TryGet(itemInstance.DefinitionId, out ItemDefinition definition) ||
                !_layouts.TryGet(containerComponent.LayoutId, out ItemLayoutDefinition layout) ||
                !layout.TryGetNamedSlotIndex(slotId, out int slotIndex))
            {
                return false;
            }

            if (!CanPlaceInNamedSlot(item, definition, container, layout, slotIndex))
            {
                return false;
            }

            if (WouldCreateOwnershipCycle(item, container))
            {
                return false;
            }

            SetItemLocation(item, new ItemLocationCm
            {
                Container = container,
                PlacementKind = ItemPlacementKind.NamedSlot,
                NamedSlotIndex = (short)slotIndex
            });
            MarkEquipmentDirtyFromContainer(container);
            return true;
        }

        public bool TryMoveItemToGrid(Entity item, Entity container, int x, int y, int rotationQuarterTurns = 0)
        {
            if (!TryGetItemAndContainer(item, container, out ItemInstanceCm itemInstance, out ItemContainerCm containerComponent))
            {
                return false;
            }

            if (!_definitions.TryGet(itemInstance.DefinitionId, out ItemDefinition definition) ||
                !_layouts.TryGet(containerComponent.LayoutId, out ItemLayoutDefinition layout))
            {
                return false;
            }

            if (!CanPlaceInGrid(item, definition, container, layout, x, y, rotationQuarterTurns))
            {
                return false;
            }

            if (WouldCreateOwnershipCycle(item, container))
            {
                return false;
            }

            SetItemLocation(item, new ItemLocationCm
            {
                Container = container,
                PlacementKind = ItemPlacementKind.Grid,
                GridX = (short)x,
                GridY = (short)y,
                RotationQuarterTurns = (byte)rotationQuarterTurns
            });
            MarkEquipmentDirtyFromContainer(container);
            return true;
        }

        public bool TryAutoPlaceItem(Entity item, Entity container)
        {
            if (!TryGetItemAndContainer(item, container, out ItemInstanceCm itemInstance, out ItemContainerCm containerComponent))
            {
                return false;
            }

            if (!_definitions.TryGet(itemInstance.DefinitionId, out ItemDefinition definition) ||
                !_layouts.TryGet(containerComponent.LayoutId, out ItemLayoutDefinition layout))
            {
                return false;
            }

            for (int i = 0; i < layout.NamedSlots.Length; i++)
            {
                string slotId = layout.NamedSlots[i].Id;
                if (definition.AllowsNamedSlot(slotId) && CanPlaceInNamedSlot(item, definition, container, layout, i))
                {
                    return TryMoveItemToNamedSlot(item, container, slotId);
                }
            }

            if (!layout.HasGrid)
            {
                return false;
            }

            if (!_shapes.TryGet(definition.ShapeId, out ItemShapeDefinition shape))
            {
                return false;
            }

            for (int rotation = 0; rotation < shape.Rotations.Length; rotation++)
            {
                ItemShapeRotation rotated = shape.GetRotation(rotation);
                for (int y = 0; y <= layout.Height - rotated.Height; y++)
                {
                    for (int x = 0; x <= layout.Width - rotated.Width; x++)
                    {
                        if (CanPlaceInGrid(item, definition, container, layout, x, y, rotation))
                        {
                            return TryMoveItemToGrid(item, container, x, y, rotation);
                        }
                    }
                }
            }

            return false;
        }

        public bool TryTransferItem(Entity item, Entity destinationContainer)
        {
            return TryAutoPlaceItem(item, destinationContainer);
        }

        public bool CanAutoPlaceItem(Entity item, Entity container)
        {
            if (!TryGetItemAndContainer(item, container, out ItemInstanceCm itemInstance, out ItemContainerCm containerComponent))
            {
                return false;
            }

            if (!_definitions.TryGet(itemInstance.DefinitionId, out _) ||
                !_layouts.TryGet(containerComponent.LayoutId, out _))
            {
                return false;
            }

            return TryPlanAutoPlacement(container, itemInstance.DefinitionId, item, out _);
        }

        public bool CanAutoPlaceItem(
            Entity item,
            Entity container,
            List<ItemPlacementReservation> reservations,
            out ItemPlacementReservation reservation)
        {
            reservation = default;
            if (!TryGetItemAndContainer(item, container, out ItemInstanceCm itemInstance, out ItemContainerCm containerComponent))
            {
                return false;
            }

            if (!_definitions.TryGet(itemInstance.DefinitionId, out _) ||
                !_layouts.TryGet(containerComponent.LayoutId, out _))
            {
                return false;
            }

            return TryPlanAutoPlacement(container, itemInstance.DefinitionId, item, reservations, out reservation);
        }

        public bool CanAutoPlaceItemDefinition(Entity container, int definitionId)
        {
            return TryPlanAutoPlacement(container, definitionId, Entity.Null, out _);
        }

        public bool CanAutoPlaceItemDefinition(
            Entity container,
            int definitionId,
            List<ItemPlacementReservation> reservations,
            out ItemPlacementReservation reservation)
        {
            return TryPlanAutoPlacement(container, definitionId, Entity.Null, reservations, out reservation);
        }

        public bool TryCreateAndPlaceItem(
            Entity container,
            int definitionId,
            int stackCount,
            int charges,
            int durability,
            out Entity item)
        {
            item = Entity.Null;
            if (!TryPlanAutoPlacement(container, definitionId, Entity.Null, out _))
            {
                return false;
            }

            item = CreateItem(definitionId, stackCount, charges, durability);
            if (TryAutoPlaceItem(item, container))
            {
                return true;
            }

            if (_world.IsAlive(item))
            {
                _world.Destroy(item);
            }

            item = Entity.Null;
            return false;
        }

        public bool TrySplitStack(Entity item, int splitCount, out Entity splitItem)
        {
            splitItem = Entity.Null;
            if (!_world.IsAlive(item) || !_world.Has<ItemInstanceCm>(item) || !_world.Has<ItemLocationCm>(item))
            {
                return false;
            }

            ref ItemInstanceCm instance = ref _world.Get<ItemInstanceCm>(item);
            if (splitCount <= 0 || splitCount >= instance.StackCount)
            {
                return false;
            }

            ItemLocationCm location = _world.Get<ItemLocationCm>(item);
            splitItem = CreateItem(instance.DefinitionId, splitCount, instance.Charges, instance.Durability);
            instance.StackCount -= splitCount;

            if (location.PlacementKind == ItemPlacementKind.NamedSlot)
            {
                if (!TryAutoPlaceItem(splitItem, location.Container))
                {
                    _world.Destroy(splitItem);
                    splitItem = Entity.Null;
                    instance.StackCount += splitCount;
                    return false;
                }
            }
            else
            {
                if (!TryAutoPlaceItem(splitItem, location.Container))
                {
                    _world.Destroy(splitItem);
                    splitItem = Entity.Null;
                    instance.StackCount += splitCount;
                    return false;
                }
            }

            MarkEquipmentDirtyFromContainer(location.Container);
            return true;
        }

        public bool TryRotateInPlace(Entity item)
        {
            if (!_world.IsAlive(item) || !_world.Has<ItemLocationCm>(item) || !_world.Has<ItemInstanceCm>(item))
            {
                return false;
            }

            ItemLocationCm location = _world.Get<ItemLocationCm>(item);
            if (location.PlacementKind != ItemPlacementKind.Grid)
            {
                return false;
            }

            ItemInstanceCm instance = _world.Get<ItemInstanceCm>(item);
            if (!_definitions.TryGet(instance.DefinitionId, out ItemDefinition definition) ||
                !_world.IsAlive(location.Container) ||
                !_world.Has<ItemContainerCm>(location.Container))
            {
                return false;
            }

            ItemContainerCm container = _world.Get<ItemContainerCm>(location.Container);
            if (!_layouts.TryGet(container.LayoutId, out ItemLayoutDefinition layout) ||
                !_shapes.TryGet(definition.ShapeId, out ItemShapeDefinition shape))
            {
                return false;
            }

            int nextRotation = (location.RotationQuarterTurns + 1) % shape.Rotations.Length;
            if (!CanPlaceInGrid(item, definition, location.Container, layout, location.GridX, location.GridY, nextRotation))
            {
                return false;
            }

            location.RotationQuarterTurns = (byte)nextRotation;
            _world.Get<ItemLocationCm>(item) = location;
            MarkEquipmentDirtyFromContainer(location.Container);
            return true;
        }

        public int CountStackUnits(Entity owner, int definitionId)
        {
            _ownedContainerScratch.Clear();
            CollectOwnedContainers(owner, _ownedContainerScratch);
            int total = 0;
            for (int i = 0; i < _ownedContainerScratch.Count; i++)
            {
                total += CountStackUnitsInContainer(_ownedContainerScratch[i], definitionId);
            }

            return total;
        }

        public bool ConsumeStackUnits(Entity owner, int definitionId, int amount)
        {
            if (amount <= 0)
            {
                return true;
            }

            _ownedItemScratch.Clear();
            _ownedContainerScratch.Clear();
            CollectOwnedContainers(owner, _ownedContainerScratch);
            for (int i = 0; i < _ownedContainerScratch.Count; i++)
            {
                CollectItemsInContainer(_ownedContainerScratch[i], _ownedItemScratch);
            }

            _ownedItemScratch.Sort(CompareEntityId);
            int remaining = amount;
            for (int i = 0; i < _ownedItemScratch.Count && remaining > 0; i++)
            {
                Entity item = _ownedItemScratch[i];
                if (!_world.IsAlive(item) || !_world.Has<ItemInstanceCm>(item))
                {
                    continue;
                }

                ref ItemInstanceCm instance = ref _world.Get<ItemInstanceCm>(item);
                if (instance.DefinitionId != definitionId || instance.StackCount <= 0)
                {
                    continue;
                }

                int consumed = Math.Min(instance.StackCount, remaining);
                instance.StackCount -= consumed;
                remaining -= consumed;
                if (instance.StackCount <= 0)
                {
                    Entity container = _world.Has<ItemLocationCm>(item) ? _world.Get<ItemLocationCm>(item).Container : Entity.Null;
                    _world.Destroy(item);
                    if (container != Entity.Null)
                    {
                        MarkEquipmentDirtyFromContainer(container);
                    }
                }
            }

            return remaining == 0;
        }

        public bool ConsumeStackUnits(Entity owner, int definitionId, int amount, List<ItemConsumptionRecord> consumedRecords)
        {
            if (consumedRecords == null)
            {
                throw new ArgumentNullException(nameof(consumedRecords));
            }

            if (amount <= 0)
            {
                return true;
            }

            int startCount = consumedRecords.Count;
            _ownedItemScratch.Clear();
            _ownedContainerScratch.Clear();
            CollectOwnedContainers(owner, _ownedContainerScratch);
            for (int i = 0; i < _ownedContainerScratch.Count; i++)
            {
                CollectItemsInContainer(_ownedContainerScratch[i], _ownedItemScratch);
            }

            _ownedItemScratch.Sort(CompareEntityId);
            int remaining = amount;
            for (int i = 0; i < _ownedItemScratch.Count && remaining > 0; i++)
            {
                Entity item = _ownedItemScratch[i];
                if (!_world.IsAlive(item) || !_world.Has<ItemInstanceCm>(item))
                {
                    continue;
                }

                ref ItemInstanceCm instance = ref _world.Get<ItemInstanceCm>(item);
                if (instance.DefinitionId != definitionId || instance.StackCount <= 0)
                {
                    continue;
                }

                int consumed = Math.Min(instance.StackCount, remaining);
                bool hadLocation = _world.Has<ItemLocationCm>(item);
                ItemLocationCm location = hadLocation ? _world.Get<ItemLocationCm>(item) : default;
                Entity container = hadLocation ? location.Container : Entity.Null;
                consumedRecords.Add(new ItemConsumptionRecord
                {
                    Item = item,
                    DefinitionId = instance.DefinitionId,
                    Amount = consumed,
                    Charges = instance.Charges,
                    Durability = instance.Durability,
                    HadLocation = hadLocation,
                    Location = location
                });

                instance.StackCount -= consumed;
                remaining -= consumed;
                if (instance.StackCount <= 0)
                {
                    _world.Destroy(item);
                    if (container != Entity.Null)
                    {
                        MarkEquipmentDirtyFromContainer(container);
                    }
                }
            }

            if (remaining == 0)
            {
                return true;
            }

            RestoreConsumedUnits(consumedRecords, startCount);
            consumedRecords.RemoveRange(startCount, consumedRecords.Count - startCount);
            return false;
        }

        public void RestoreConsumedUnits(List<ItemConsumptionRecord> consumedRecords)
        {
            if (consumedRecords == null)
            {
                throw new ArgumentNullException(nameof(consumedRecords));
            }

            RestoreConsumedUnits(consumedRecords, 0);
        }

        private void RestoreConsumedUnits(List<ItemConsumptionRecord> consumedRecords, int startIndex)
        {
            for (int i = consumedRecords.Count - 1; i >= 0; i--)
            {
                if (i < startIndex)
                {
                    break;
                }

                RestoreConsumedItem(consumedRecords[i]);
            }
        }

        public bool TryFindOwnedItem(Entity owner, int definitionId, ItemContainerPurpose purpose, out Entity item)
        {
            item = Entity.Null;
            _ownedContainerScratch.Clear();
            _ownedItemScratch.Clear();
            CollectOwnedContainers(owner, _ownedContainerScratch);
            for (int i = 0; i < _ownedContainerScratch.Count; i++)
            {
                Entity container = _ownedContainerScratch[i];
                if (purpose != ItemContainerPurpose.None)
                {
                    if (!_world.IsAlive(container) || !_world.Has<ItemContainerCm>(container))
                    {
                        continue;
                    }

                    if (_world.Get<ItemContainerCm>(container).Purpose != purpose)
                    {
                        continue;
                    }
                }

                CollectItemsInContainer(container, _ownedItemScratch);
            }

            _ownedItemScratch.Sort(CompareEntityId);
            for (int i = 0; i < _ownedItemScratch.Count; i++)
            {
                Entity candidate = _ownedItemScratch[i];
                if (_world.IsAlive(candidate) &&
                    _world.Has<ItemInstanceCm>(candidate) &&
                    _world.Get<ItemInstanceCm>(candidate).DefinitionId == definitionId)
                {
                    item = candidate;
                    return true;
                }
            }

            return false;
        }

        public void RestoreItemLocation(Entity item, bool hadLocation, in ItemLocationCm location)
        {
            if (!_world.IsAlive(item))
            {
                return;
            }

            Entity previousContainer = _world.Has<ItemLocationCm>(item)
                ? _world.Get<ItemLocationCm>(item).Container
                : Entity.Null;

            if (hadLocation)
            {
                SetItemLocation(item, location);
            }
            else if (_world.Has<ItemLocationCm>(item))
            {
                _world.Remove<ItemLocationCm>(item);
            }

            if (previousContainer != Entity.Null)
            {
                MarkEquipmentDirtyFromContainer(previousContainer);
            }

            if (hadLocation && location.Container != Entity.Null)
            {
                MarkEquipmentDirtyFromContainer(location.Container);
            }
        }

        public void DestroyItemTree(Entity item)
        {
            if (!_world.IsAlive(item))
            {
                return;
            }

            _destroyItemScratch.Clear();
            _destroyContainerScratch.Clear();
            _destroyItemScratch.Add(item);

            int containerCursor = 0;
            for (int itemCursor = 0; itemCursor < _destroyItemScratch.Count; itemCursor++)
            {
                Entity currentItem = _destroyItemScratch[itemCursor];
                if (!_world.IsAlive(currentItem))
                {
                    continue;
                }

                CollectMountedContainers(currentItem, _destroyContainerScratch);
                while (containerCursor < _destroyContainerScratch.Count)
                {
                    Entity container = _destroyContainerScratch[containerCursor++];
                    if (_world.IsAlive(container))
                    {
                        CollectItemsInContainer(container, _destroyItemScratch);
                    }
                }
            }

            for (int i = _destroyItemScratch.Count - 1; i >= 0; i--)
            {
                Entity currentItem = _destroyItemScratch[i];
                if (!_world.IsAlive(currentItem))
                {
                    continue;
                }

                Entity container = _world.Has<ItemLocationCm>(currentItem) ? _world.Get<ItemLocationCm>(currentItem).Container : Entity.Null;
                _world.Destroy(currentItem);
                if (container != Entity.Null)
                {
                    MarkEquipmentDirtyFromContainer(container);
                }
            }

            for (int i = _destroyContainerScratch.Count - 1; i >= 0; i--)
            {
                Entity container = _destroyContainerScratch[i];
                if (_world.IsAlive(container))
                {
                    _world.Destroy(container);
                }
            }

            _destroyItemScratch.Clear();
            _destroyContainerScratch.Clear();
        }

        public bool TryFindOwnedContainer(Entity owner, ItemContainerPurpose purpose, out Entity container)
        {
            Entity found = Entity.Null;
            _world.Query(in ContainerQuery, (Entity entity, ref ItemContainerCm data) =>
            {
                if (found != Entity.Null)
                {
                    return;
                }

                if (data.Owner == owner &&
                    (data.OwnerKind == ItemContainerOwnerKind.Actor || data.OwnerKind == ItemContainerOwnerKind.Vendor) &&
                    data.Purpose == purpose)
                {
                    found = entity;
                }
            });
            container = found;
            return container != Entity.Null;
        }

        public bool TryFindMountedContainer(Entity item, string mountId, out Entity container)
        {
            Entity found = Entity.Null;
            if (!_world.IsAlive(item) || !_world.Has<ItemInstanceCm>(item))
            {
                container = Entity.Null;
                return false;
            }

            ItemInstanceCm instance = _world.Get<ItemInstanceCm>(item);
            if (!_definitions.TryGet(instance.DefinitionId, out ItemDefinition definition))
            {
                container = Entity.Null;
                return false;
            }

            int mountIndex = -1;
            for (int i = 0; i < definition.MountedContainers.Length; i++)
            {
                if (string.Equals(definition.MountedContainers[i].Id, mountId, StringComparison.OrdinalIgnoreCase))
                {
                    mountIndex = i;
                    break;
                }
            }

            if (mountIndex < 0)
            {
                container = Entity.Null;
                return false;
            }

            _world.Query(in MountedContainerQuery, (Entity entity, ref ItemMountedContainerCm mounted, ref ItemContainerCm _) =>
            {
                if (found != Entity.Null)
                {
                    return;
                }

                if (mounted.ParentItem == item && mounted.MountIndex == mountIndex)
                {
                    found = entity;
                }
            });
            container = found;
            return container != Entity.Null;
        }

        public void CollectItemsInContainer(Entity container, List<Entity> output)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            _world.Query(in ItemQuery, (Entity entity, ref ItemInstanceCm _, ref ItemLocationCm location) =>
            {
                if (location.Container == container)
                {
                    output.Add(entity);
                }
            });
        }

        public void CollectMountedContainers(Entity item, List<Entity> output)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            _world.Query(in MountedContainerQuery, (Entity entity, ref ItemMountedContainerCm mounted, ref ItemContainerCm _) =>
            {
                if (mounted.ParentItem == item)
                {
                    output.Add(entity);
                }
            });
        }

        public void CollectEquippedGrantItems(Entity actor, List<Entity> output)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            _grantContainerScratch.Clear();
            _world.Query(in ContainerQuery, (Entity entity, ref ItemContainerCm data) =>
            {
                if (data.Owner == actor &&
                    data.OwnerKind == ItemContainerOwnerKind.Actor &&
                    data.Purpose == ItemContainerPurpose.Equipment)
                {
                    _grantContainerScratch.Add(entity);
                }
            });

            _grantContainerScratch.Sort(CompareEntityId);
            CollectGrantItems(output);
            output.Sort(CompareEntityId);
        }

        public bool TryResolveOwningActorFromItem(Entity item, out Entity actor)
        {
            actor = Entity.Null;
            if (!_world.IsAlive(item) || !_world.Has<ItemLocationCm>(item))
            {
                return false;
            }

            ItemLocationCm location = _world.Get<ItemLocationCm>(item);
            return TryResolveOwningActorFromContainer(location.Container, out actor);
        }

        public bool TryResolveOwningActorFromContainer(Entity container, out Entity actor)
        {
            actor = Entity.Null;
            if (!_world.IsAlive(container) || !_world.Has<ItemContainerCm>(container))
            {
                return false;
            }

            ItemContainerCm data = _world.Get<ItemContainerCm>(container);
            if (data.OwnerKind == ItemContainerOwnerKind.Actor ||
                data.OwnerKind == ItemContainerOwnerKind.Vendor)
            {
                actor = data.Owner;
                return actor != Entity.Null && _world.IsAlive(actor);
            }

            if (data.OwnerKind == ItemContainerOwnerKind.Item)
            {
                return TryResolveOwningActorFromItem(data.Owner, out actor);
            }

            return false;
        }

        private void CollectOwnedContainers(Entity owner, List<Entity> output)
        {
            _world.Query(in ContainerQuery, (Entity entity, ref ItemContainerCm data) =>
            {
                if (TryResolveOwningActorFromContainer(entity, out Entity actor) && actor == owner)
                {
                    output.Add(entity);
                }
            });
        }

        private int CountStackUnitsInContainer(Entity container, int definitionId)
        {
            int total = 0;
            _world.Query(in ItemQuery, (Entity _, ref ItemInstanceCm item, ref ItemLocationCm location) =>
            {
                if (location.Container == container && item.DefinitionId == definitionId)
                {
                    total += item.StackCount;
                }
            });
            return total;
        }

        private bool TryPlanAutoPlacement(Entity container, int definitionId, Entity ignoreItem, out ItemPlacementPlan plan)
        {
            return TryPlanAutoPlacement(container, definitionId, ignoreItem, null, out plan);
        }

        private bool TryPlanAutoPlacement(
            Entity container,
            int definitionId,
            Entity ignoreItem,
            List<ItemPlacementReservation> reservations,
            out ItemPlacementPlan plan)
        {
            plan = default;
            if (!_world.IsAlive(container) || !_world.Has<ItemContainerCm>(container))
            {
                return false;
            }

            if (!_definitions.TryGet(definitionId, out ItemDefinition definition))
            {
                return false;
            }

            ItemContainerCm containerComponent = _world.Get<ItemContainerCm>(container);
            if (!_layouts.TryGet(containerComponent.LayoutId, out ItemLayoutDefinition layout))
            {
                return false;
            }

            for (int i = 0; i < layout.NamedSlots.Length; i++)
            {
                string slotId = layout.NamedSlots[i].Id;
                if (definition.AllowsNamedSlot(slotId) && CanPlaceInNamedSlot(ignoreItem, definition, container, layout, i, reservations))
                {
                    plan = new ItemPlacementPlan
                    {
                        Container = container,
                        Kind = ItemPlacementKind.NamedSlot,
                        NamedSlotIndex = (short)i
                    };
                    return true;
                }
            }

            if (!layout.HasGrid)
            {
                return false;
            }

            if (!_shapes.TryGet(definition.ShapeId, out ItemShapeDefinition shape))
            {
                return false;
            }

            for (int rotation = 0; rotation < shape.Rotations.Length; rotation++)
            {
                ItemShapeRotation rotated = shape.GetRotation(rotation);
                for (int y = 0; y <= layout.Height - rotated.Height; y++)
                {
                    for (int x = 0; x <= layout.Width - rotated.Width; x++)
                    {
                        if (CanPlaceInGrid(ignoreItem, definition, container, layout, x, y, rotation, reservations))
                        {
                            plan = new ItemPlacementPlan
                            {
                                Container = container,
                                Kind = ItemPlacementKind.Grid,
                                GridX = (short)x,
                                GridY = (short)y,
                                RotationQuarterTurns = (byte)rotation
                            };
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool TryPlanAutoPlacement(
            Entity container,
            int definitionId,
            Entity ignoreItem,
            List<ItemPlacementReservation> reservations,
            out ItemPlacementReservation reservation)
        {
            reservation = default;
            if (!TryPlanAutoPlacement(container, definitionId, ignoreItem, reservations, out ItemPlacementPlan plan))
            {
                return false;
            }

            reservation = plan.ToReservation(definitionId);
            return true;
        }

        private void CollectGrantItems(List<Entity> output)
        {
            int cursor = 0;
            while (cursor < _grantContainerScratch.Count)
            {
                Entity currentContainer = _grantContainerScratch[cursor++];
                if (!_world.IsAlive(currentContainer) || !_world.Has<ItemContainerCm>(currentContainer))
                {
                    continue;
                }

                ItemContainerCm containerData = _world.Get<ItemContainerCm>(currentContainer);
                if (!_layouts.TryGet(containerData.LayoutId, out ItemLayoutDefinition layout) ||
                    !layout.GrantsEquipmentBonuses)
                {
                    continue;
                }

                _grantItemScratch.Clear();
                CollectItemsInContainer(currentContainer, _grantItemScratch);
                _grantItemScratch.Sort(CompareEntityId);
                for (int i = 0; i < _grantItemScratch.Count; i++)
                {
                    Entity item = _grantItemScratch[i];
                    output.Add(item);

                    _grantMountedContainerScratch.Clear();
                    CollectMountedContainers(item, _grantMountedContainerScratch);
                    _grantMountedContainerScratch.Sort(CompareEntityId);
                    for (int j = 0; j < _grantMountedContainerScratch.Count; j++)
                    {
                        _grantContainerScratch.Add(_grantMountedContainerScratch[j]);
                    }
                }
            }
        }

        private static int CompareEntityId(Entity left, Entity right)
        {
            return left.Id.CompareTo(right.Id);
        }

        private void RestoreConsumedItem(in ItemConsumptionRecord record)
        {
            if (record.Amount <= 0)
            {
                return;
            }

            if (_world.IsAlive(record.Item) && _world.Has<ItemInstanceCm>(record.Item))
            {
                ref ItemInstanceCm instance = ref _world.Get<ItemInstanceCm>(record.Item);
                instance.StackCount += record.Amount;
                if (_world.Has<ItemLocationCm>(record.Item))
                {
                    MarkEquipmentDirtyFromContainer(_world.Get<ItemLocationCm>(record.Item).Container);
                }
                return;
            }

            Entity restored = CreateItem(record.DefinitionId, record.Amount, record.Charges, record.Durability);
            if (record.HadLocation)
            {
                SetItemLocation(restored, record.Location);
                MarkEquipmentDirtyFromContainer(record.Location.Container);
            }
        }

        private bool TryGetItemAndContainer(Entity item, Entity container, out ItemInstanceCm itemInstance, out ItemContainerCm containerComponent)
        {
            itemInstance = default;
            containerComponent = default;
            if (!_world.IsAlive(item) || !_world.Has<ItemInstanceCm>(item) || !_world.IsAlive(container) || !_world.Has<ItemContainerCm>(container))
            {
                return false;
            }

            itemInstance = _world.Get<ItemInstanceCm>(item);
            containerComponent = _world.Get<ItemContainerCm>(container);
            return true;
        }

        private bool CanPlaceInNamedSlot(
            Entity item,
            ItemDefinition definition,
            Entity container,
            ItemLayoutDefinition layout,
            int slotIndex)
        {
            return CanPlaceInNamedSlot(item, definition, container, layout, slotIndex, null);
        }

        private bool CanPlaceInNamedSlot(
            Entity item,
            ItemDefinition definition,
            Entity container,
            ItemLayoutDefinition layout,
            int slotIndex,
            List<ItemPlacementReservation> reservations)
        {
            ItemNamedSlotDefinition? slot = layout.GetNamedSlot(slotIndex);
            if (slot == null)
            {
                return false;
            }

            GameplayTagContainer requiredAll = slot.RequiredAll;
            GameplayTagContainer blockedAny = slot.BlockedAny;
            if (!definition.AllowsNamedSlot(slot.Id) ||
                !definition.Tags.ContainsAll(in requiredAll) ||
                definition.Tags.Intersects(in blockedAny))
            {
                return false;
            }

            Entity occupant = FindNamedSlotOccupant(container, slotIndex, item);
            return occupant == Entity.Null && !ReservationOccupiesNamedSlot(container, slotIndex, reservations);
        }

        private bool CanPlaceInGrid(
            Entity item,
            ItemDefinition definition,
            Entity container,
            ItemLayoutDefinition layout,
            int x,
            int y,
            int rotationQuarterTurns)
        {
            return CanPlaceInGrid(item, definition, container, layout, x, y, rotationQuarterTurns, null);
        }

        private bool CanPlaceInGrid(
            Entity item,
            ItemDefinition definition,
            Entity container,
            ItemLayoutDefinition layout,
            int x,
            int y,
            int rotationQuarterTurns,
            List<ItemPlacementReservation> reservations)
        {
            if (!layout.HasGrid || !_shapes.TryGet(definition.ShapeId, out ItemShapeDefinition shape))
            {
                return false;
            }

            ItemShapeRotation rotation = shape.GetRotation(rotationQuarterTurns);
            if (x < 0 || y < 0 || x + rotation.Width > layout.Width || y + rotation.Height > layout.Height)
            {
                return false;
            }

            for (int sy = 0; sy < rotation.Height; sy++)
            {
                for (int sx = 0; sx < rotation.Width; sx++)
                {
                    if (!rotation.IsOccupied(sx, sy))
                    {
                        continue;
                    }

                    int tx = x + sx;
                    int ty = y + sy;
                    if (layout.IsBlockedCell(tx, ty))
                    {
                        return false;
                    }

                    if (TryFindGridOccupant(container, tx, ty, item, out _))
                    {
                        return false;
                    }

                    if (ReservationOccupiesGridCell(container, tx, ty, reservations))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private struct ItemPlacementPlan
        {
            public Entity Container;
            public ItemPlacementKind Kind;
            public short GridX;
            public short GridY;
            public short NamedSlotIndex;
            public byte RotationQuarterTurns;

            public readonly ItemPlacementReservation ToReservation(int definitionId)
            {
                return new ItemPlacementReservation(
                    Container,
                    definitionId,
                    Kind,
                    GridX,
                    GridY,
                    NamedSlotIndex,
                    RotationQuarterTurns);
            }
        }

        private bool ReservationOccupiesNamedSlot(Entity container, int slotIndex, List<ItemPlacementReservation> reservations)
        {
            if (reservations == null)
            {
                return false;
            }

            for (int i = 0; i < reservations.Count; i++)
            {
                ItemPlacementReservation reservation = reservations[i];
                if (reservation.Container == container &&
                    reservation.Kind == ItemPlacementKind.NamedSlot &&
                    reservation.NamedSlotIndex == slotIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ReservationOccupiesGridCell(Entity container, int gridX, int gridY, List<ItemPlacementReservation> reservations)
        {
            if (reservations == null)
            {
                return false;
            }

            for (int i = 0; i < reservations.Count; i++)
            {
                ItemPlacementReservation reservation = reservations[i];
                if (reservation.Container != container ||
                    reservation.Kind != ItemPlacementKind.Grid ||
                    !_definitions.TryGet(reservation.DefinitionId, out ItemDefinition definition) ||
                    !_shapes.TryGet(definition.ShapeId, out ItemShapeDefinition shape))
                {
                    continue;
                }

                ItemShapeRotation rotation = shape.GetRotation(reservation.RotationQuarterTurns);
                for (int sy = 0; sy < rotation.Height; sy++)
                {
                    for (int sx = 0; sx < rotation.Width; sx++)
                    {
                        if (!rotation.IsOccupied(sx, sy))
                        {
                            continue;
                        }

                        if (reservation.GridX + sx == gridX && reservation.GridY + sy == gridY)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool TryFindGridOccupant(Entity container, int gridX, int gridY, Entity ignoreItem, out Entity occupant)
        {
            Entity found = Entity.Null;
            _world.Query(in ItemQuery, (Entity entity, ref ItemInstanceCm item, ref ItemLocationCm location) =>
            {
                if (found != Entity.Null ||
                    entity == ignoreItem ||
                    location.Container != container ||
                    location.PlacementKind != ItemPlacementKind.Grid)
                {
                    return;
                }

                if (!_definitions.TryGet(item.DefinitionId, out ItemDefinition definition) ||
                    !_shapes.TryGet(definition.ShapeId, out ItemShapeDefinition shape))
                {
                    return;
                }

                ItemShapeRotation rotation = shape.GetRotation(location.RotationQuarterTurns);
                for (int sy = 0; sy < rotation.Height; sy++)
                {
                    for (int sx = 0; sx < rotation.Width; sx++)
                    {
                        if (!rotation.IsOccupied(sx, sy))
                        {
                            continue;
                        }

                        if (location.GridX + sx == gridX && location.GridY + sy == gridY)
                        {
                            found = entity;
                            return;
                        }
                    }
                }
            });
            occupant = found;
            return occupant != Entity.Null;
        }

        private Entity FindNamedSlotOccupant(Entity container, int slotIndex, Entity ignoreItem)
        {
            Entity occupant = Entity.Null;
            _world.Query(in ItemQuery, (Entity entity, ref ItemInstanceCm _, ref ItemLocationCm location) =>
            {
                if (occupant != Entity.Null ||
                    entity == ignoreItem ||
                    location.Container != container ||
                    location.PlacementKind != ItemPlacementKind.NamedSlot)
                {
                    return;
                }

                if (location.NamedSlotIndex == slotIndex)
                {
                    occupant = entity;
                }
            });
            return occupant;
        }

        private void SetItemLocation(Entity item, ItemLocationCm nextLocation)
        {
            Entity previousContainer = Entity.Null;
            if (_world.Has<ItemLocationCm>(item))
            {
                previousContainer = _world.Get<ItemLocationCm>(item).Container;
                _world.Get<ItemLocationCm>(item) = nextLocation;
            }
            else
            {
                _world.Add(item, nextLocation);
            }

            if (previousContainer != Entity.Null)
            {
                MarkEquipmentDirtyFromContainer(previousContainer);
            }
        }

        private bool WouldCreateOwnershipCycle(Entity item, Entity container)
        {
            if (!_world.IsAlive(container) || !_world.Has<ItemContainerCm>(container))
            {
                return false;
            }

            ItemContainerCm containerData = _world.Get<ItemContainerCm>(container);
            if (containerData.OwnerKind != ItemContainerOwnerKind.Item)
            {
                return false;
            }

            if (containerData.Owner == item)
            {
                return true;
            }

            return TryIsContainedWithin(containerData.Owner, item);
        }

        private bool TryIsContainedWithin(Entity item, Entity possibleAncestor)
        {
            if (!_world.IsAlive(item) || !_world.Has<ItemLocationCm>(item))
            {
                return false;
            }

            ItemLocationCm location = _world.Get<ItemLocationCm>(item);
            if (!_world.IsAlive(location.Container) || !_world.Has<ItemContainerCm>(location.Container))
            {
                return false;
            }

            ItemContainerCm container = _world.Get<ItemContainerCm>(location.Container);
            if (container.OwnerKind != ItemContainerOwnerKind.Item)
            {
                return false;
            }

            if (container.Owner == possibleAncestor)
            {
                return true;
            }

            return TryIsContainedWithin(container.Owner, possibleAncestor);
        }

        private void MarkEquipmentDirtyFromContainer(Entity container)
        {
            if (!TryResolveOwningActorFromContainer(container, out Entity actor) || !_world.IsAlive(actor))
            {
                return;
            }

            if (!_world.Has<InventoryEquipmentDirtyTag>(actor))
            {
                _world.Add(actor, new InventoryEquipmentDirtyTag());
            }
        }
    }

    public struct ItemConsumptionRecord
    {
        public Entity Item;
        public int DefinitionId;
        public int Amount;
        public int Charges;
        public int Durability;
        public bool HadLocation;
        public ItemLocationCm Location;
    }
}
