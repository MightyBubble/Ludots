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
            var containers = new List<Entity>(16);
            CollectOwnedContainers(owner, containers);
            int total = 0;
            for (int i = 0; i < containers.Count; i++)
            {
                total += CountStackUnitsInContainer(containers[i], definitionId);
            }

            return total;
        }

        public bool ConsumeStackUnits(Entity owner, int definitionId, int amount)
        {
            if (amount <= 0)
            {
                return true;
            }

            var items = new List<Entity>(64);
            var containers = new List<Entity>(16);
            CollectOwnedContainers(owner, containers);
            for (int i = 0; i < containers.Count; i++)
            {
                CollectItemsInContainer(containers[i], items);
            }

            items.Sort((a, b) => a.Id.CompareTo(b.Id));
            int remaining = amount;
            for (int i = 0; i < items.Count && remaining > 0; i++)
            {
                Entity item = items[i];
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

        public bool TryFindOwnedContainer(Entity owner, ItemContainerPurpose purpose, out Entity container)
        {
            Entity found = Entity.Null;
            _world.Query(in ContainerQuery, (Entity entity, ref ItemContainerCm data) =>
            {
                if (found != Entity.Null)
                {
                    return;
                }

                if (data.Owner == owner && data.OwnerKind == ItemContainerOwnerKind.Actor && data.Purpose == purpose)
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

            var containers = new List<Entity>(8);
            CollectOwnedContainers(actor, containers);
            for (int i = 0; i < containers.Count; i++)
            {
                CollectGrantItemsRecursive(containers[i], output);
            }

            output.Sort((a, b) => a.Id.CompareTo(b.Id));
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
            if (data.OwnerKind == ItemContainerOwnerKind.Actor)
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

        private void CollectGrantItemsRecursive(Entity container, List<Entity> output)
        {
            if (!_world.IsAlive(container) || !_world.Has<ItemContainerCm>(container))
            {
                return;
            }

            ItemContainerCm containerData = _world.Get<ItemContainerCm>(container);
            if (!_layouts.TryGet(containerData.LayoutId, out ItemLayoutDefinition layout))
            {
                return;
            }

            if (!layout.GrantsEquipmentBonuses)
            {
                return;
            }

            var items = new List<Entity>(16);
            CollectItemsInContainer(container, items);
            items.Sort((a, b) => a.Id.CompareTo(b.Id));
            for (int i = 0; i < items.Count; i++)
            {
                Entity item = items[i];
                output.Add(item);

                var mountedContainers = new List<Entity>(4);
                CollectMountedContainers(item, mountedContainers);
                for (int j = 0; j < mountedContainers.Count; j++)
                {
                    CollectGrantItemsRecursive(mountedContainers[j], output);
                }
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
            return occupant == Entity.Null;
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
                }
            }

            return true;
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
}
