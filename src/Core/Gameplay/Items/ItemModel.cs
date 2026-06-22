using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.Items
{
    public enum ItemContainerPurpose : byte
    {
        None = 0,
        Equipment = 1,
        Backpack = 2,
        Stash = 3,
        SecureStorage = 4,
        Vendor = 5,
        Trade = 6,
        WeaponAttachment = 7,
        WeaponInternal = 8
    }

    public sealed class ItemShapeRotation
    {
        private readonly bool[] _occupiedMask;

        public ItemShapeRotation(int width, int height, bool[] occupiedMask)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (occupiedMask == null) throw new ArgumentNullException(nameof(occupiedMask));
            if (occupiedMask.Length != width * height)
            {
                throw new ArgumentException("Occupied mask length must equal width * height.", nameof(occupiedMask));
            }

            Width = width;
            Height = height;
            _occupiedMask = occupiedMask;
        }

        public int Width { get; }

        public int Height { get; }

        public bool IsOccupied(int x, int y)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            {
                return false;
            }

            return _occupiedMask[(y * Width) + x];
        }
    }

    public sealed class ItemShapeDefinition
    {
        public string Id { get; init; } = string.Empty;

        public ItemShapeRotation[] Rotations { get; init; } = Array.Empty<ItemShapeRotation>();

        public ItemShapeRotation GetRotation(int rotationQuarterTurns)
        {
            if (Rotations.Length == 0)
            {
                throw new InvalidOperationException($"Shape '{Id}' has no rotations.");
            }

            int normalized = rotationQuarterTurns % Rotations.Length;
            if (normalized < 0)
            {
                normalized += Rotations.Length;
            }

            return Rotations[normalized];
        }
    }

    public sealed class ItemNamedSlotDefinition
    {
        public string Id { get; init; } = string.Empty;

        public string Label { get; init; } = string.Empty;

        public GameplayTagContainer RequiredAll { get; init; }

        public GameplayTagContainer BlockedAny { get; init; }

        public bool SingleItemOnly { get; init; } = true;
    }

    public sealed class ItemLayoutDefinition
    {
        private bool[] _blockedMask = Array.Empty<bool>();
        private readonly Dictionary<string, int> _namedSlotIndices = new(StringComparer.OrdinalIgnoreCase);

        public string Id { get; init; } = string.Empty;

        public ItemContainerPurpose Purpose { get; init; }

        public int Width { get; init; }

        public int Height { get; init; }

        public bool GrantsEquipmentBonuses { get; init; }

        public ItemNamedSlotDefinition[] NamedSlots { get; init; } = Array.Empty<ItemNamedSlotDefinition>();

        public ItemLayoutDefinition InitializeBlockedMask(bool[] blockedMask)
        {
            if (blockedMask == null)
            {
                throw new ArgumentNullException(nameof(blockedMask));
            }

            if (Width > 0 && Height > 0 && blockedMask.Length != Width * Height)
            {
                throw new ArgumentException("Blocked mask length must equal width * height.", nameof(blockedMask));
            }

            Array.Copy(blockedMask, _blockedMask = new bool[blockedMask.Length], blockedMask.Length);
            return this;
        }

        public ItemLayoutDefinition InitializeSlotIndices()
        {
            _namedSlotIndices.Clear();
            for (int i = 0; i < NamedSlots.Length; i++)
            {
                string id = NamedSlots[i].Id;
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new InvalidOperationException($"Layout '{Id}' contains an empty named slot id.");
                }

                _namedSlotIndices[id] = i;
            }

            return this;
        }

        public bool HasGrid => Width > 0 && Height > 0;

        public bool IsBlockedCell(int x, int y)
        {
            if (!HasGrid)
            {
                return true;
            }

            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            {
                return true;
            }

            if (_blockedMask.Length == 0)
            {
                return false;
            }

            return _blockedMask[(y * Width) + x];
        }

        public bool TryGetNamedSlotIndex(string slotId, out int index)
        {
            return _namedSlotIndices.TryGetValue(slotId ?? string.Empty, out index);
        }

        public string GetNamedSlotId(int index)
        {
            return (uint)index < (uint)NamedSlots.Length ? NamedSlots[index].Id : string.Empty;
        }

        public ItemNamedSlotDefinition? GetNamedSlot(int index)
        {
            return (uint)index < (uint)NamedSlots.Length ? NamedSlots[index] : null;
        }
    }

    public sealed class ItemAbilityGrant
    {
        public int SlotIndex { get; init; }

        public int AbilityId { get; init; }
    }

    public sealed class ItemMountedContainerDefinition
    {
        public string Id { get; init; } = string.Empty;

        public string Label { get; init; } = string.Empty;

        public int LayoutId { get; init; }

        public ItemContainerPurpose Purpose { get; init; }
    }

    public sealed class ItemDefinition
    {
        public string Id { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public int ShapeId { get; init; }

        public int MaxStack { get; init; } = 1;

        public GameplayTagContainer Tags { get; init; }

        public string[] AllowedNamedSlots { get; init; } = Array.Empty<string>();

        public int[] EquipEffectTemplateIds { get; init; } = Array.Empty<int>();

        public ItemAbilityGrant[] AbilityGrants { get; init; } = Array.Empty<ItemAbilityGrant>();

        public ItemMountedContainerDefinition[] MountedContainers { get; init; } = Array.Empty<ItemMountedContainerDefinition>();

        public bool AllowsNamedSlot(string slotId)
        {
            if (AllowedNamedSlots.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < AllowedNamedSlots.Length; i++)
            {
                if (string.Equals(AllowedNamedSlots[i], slotId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public readonly struct ItemPlacementReservation
    {
        public ItemPlacementReservation(
            Entity container,
            int definitionId,
            ItemPlacementKind kind,
            short gridX,
            short gridY,
            short namedSlotIndex,
            byte rotationQuarterTurns)
        {
            Container = container;
            DefinitionId = definitionId;
            Kind = kind;
            GridX = gridX;
            GridY = gridY;
            NamedSlotIndex = namedSlotIndex;
            RotationQuarterTurns = rotationQuarterTurns;
        }

        public Entity Container { get; }

        public int DefinitionId { get; }

        public ItemPlacementKind Kind { get; }

        public short GridX { get; }

        public short GridY { get; }

        public short NamedSlotIndex { get; }

        public byte RotationQuarterTurns { get; }
    }

    public sealed class ItemShapeRegistry
    {
        private readonly Dictionary<string, int> _nameToId = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ItemShapeDefinition?> _definitions = new() { null };

        public void Clear()
        {
            _nameToId.Clear();
            _definitions.Clear();
            _definitions.Add(null);
        }

        public int Register(string id, ItemShapeDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Shape id is required.", nameof(id));
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            if (_nameToId.TryGetValue(id, out int existing))
            {
                _definitions[existing] = definition;
                return existing;
            }

            int next = _definitions.Count;
            _nameToId[id] = next;
            _definitions.Add(definition);
            return next;
        }

        public int GetId(string id)
        {
            return _nameToId.TryGetValue(id, out int value) ? value : 0;
        }

        public bool TryGet(int id, out ItemShapeDefinition definition)
        {
            if ((uint)id < (uint)_definitions.Count && _definitions[id] != null)
            {
                definition = _definitions[id]!;
                return true;
            }

            definition = null!;
            return false;
        }
    }

    public sealed class ItemLayoutRegistry
    {
        private readonly Dictionary<string, int> _nameToId = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ItemLayoutDefinition?> _definitions = new() { null };

        public void Clear()
        {
            _nameToId.Clear();
            _definitions.Clear();
            _definitions.Add(null);
        }

        public int Register(string id, ItemLayoutDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Layout id is required.", nameof(id));
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            definition.InitializeSlotIndices();

            if (_nameToId.TryGetValue(id, out int existing))
            {
                _definitions[existing] = definition;
                return existing;
            }

            int next = _definitions.Count;
            _nameToId[id] = next;
            _definitions.Add(definition);
            return next;
        }

        public int GetId(string id)
        {
            return _nameToId.TryGetValue(id, out int value) ? value : 0;
        }

        public bool TryGet(int id, out ItemLayoutDefinition definition)
        {
            if ((uint)id < (uint)_definitions.Count && _definitions[id] != null)
            {
                definition = _definitions[id]!;
                return true;
            }

            definition = null!;
            return false;
        }
    }

    public sealed class ItemDefinitionRegistry
    {
        private readonly Dictionary<string, int> _nameToId = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ItemDefinition?> _definitions = new() { null };

        public void Clear()
        {
            _nameToId.Clear();
            _definitions.Clear();
            _definitions.Add(null);
        }

        public int Register(string id, ItemDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Item id is required.", nameof(id));
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            if (_nameToId.TryGetValue(id, out int existing))
            {
                _definitions[existing] = definition;
                return existing;
            }

            int next = _definitions.Count;
            _nameToId[id] = next;
            _definitions.Add(definition);
            return next;
        }

        public int GetId(string id)
        {
            return _nameToId.TryGetValue(id, out int value) ? value : 0;
        }

        public bool TryGet(int id, out ItemDefinition definition)
        {
            if ((uint)id < (uint)_definitions.Count && _definitions[id] != null)
            {
                definition = _definitions[id]!;
                return true;
            }

            definition = null!;
            return false;
        }
    }
}
