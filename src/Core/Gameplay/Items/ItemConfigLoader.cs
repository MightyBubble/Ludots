using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Gameplay.Items
{
    public sealed class ItemConfigLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly ItemShapeRegistry _shapes;
        private readonly ItemLayoutRegistry _layouts;
        private readonly ItemDefinitionRegistry _definitions;

        public ItemConfigLoader(
            ConfigPipeline pipeline,
            ItemShapeRegistry shapes,
            ItemLayoutRegistry layouts,
            ItemDefinitionRegistry definitions)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _shapes = shapes ?? throw new ArgumentNullException(nameof(shapes));
            _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        }

        public void Load(ConfigCatalog? catalog = null, ConfigConflictReport? report = null)
        {
            LoadShapes(catalog, report);
            LoadLayouts(catalog, report);
            LoadDefinitions(catalog, report);
        }

        private void LoadShapes(ConfigCatalog? catalog, ConfigConflictReport? report)
        {
            _shapes.Clear();

            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Items/shapes.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var rowsById = new List<(string Id, ShapeConfig Cfg)>(merged.Count);

            for (int i = 0; i < merged.Count; i++)
            {
                var cfg = merged[i].Node.Deserialize<ShapeConfig>(options)
                    ?? throw new InvalidOperationException($"Failed to deserialize item shape '{merged[i].Id}'.");
                cfg.Id ??= merged[i].Id;
                rowsById.Add((cfg.Id, cfg));
            }

            rowsById.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Id, b.Id));

            for (int i = 0; i < rowsById.Count; i++)
            {
                var cfg = rowsById[i].Cfg;
                bool[] baseMask = CompileMask(cfg.Rows, cfg.Id!, "shape");
                int width = cfg.Rows?[0]?.Length ?? 0;
                int height = cfg.Rows?.Length ?? 0;
                bool rotatable = cfg.Rotatable && width != height || cfg.Rotatable;
                var rotations = rotatable
                    ? BuildRotations(width, height, baseMask)
                    : new[] { new ItemShapeRotation(width, height, baseMask) };
                _shapes.Register(cfg.Id!, new ItemShapeDefinition
                {
                    Id = cfg.Id!,
                    Rotations = rotations
                });
            }
        }

        private void LoadLayouts(ConfigCatalog? catalog, ConfigConflictReport? report)
        {
            _layouts.Clear();

            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Items/layouts.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var configs = new List<(string Id, LayoutConfig Cfg)>(merged.Count);

            for (int i = 0; i < merged.Count; i++)
            {
                var cfg = merged[i].Node.Deserialize<LayoutConfig>(options)
                    ?? throw new InvalidOperationException($"Failed to deserialize item layout '{merged[i].Id}'.");
                cfg.Id ??= merged[i].Id;
                configs.Add((cfg.Id, cfg));
            }

            configs.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Id, b.Id));

            for (int i = 0; i < configs.Count; i++)
            {
                LayoutConfig cfg = configs[i].Cfg;
                int width = cfg.Width;
                int height = cfg.Height;
                bool[] blockedMask = width > 0 && height > 0
                    ? CompileMask(cfg.BlockedRows, cfg.Id!, "layout", width, height, treatBlankAsFilled: false)
                    : Array.Empty<bool>();

                var namedSlots = CompileNamedSlots(cfg);
                var layout = new ItemLayoutDefinition
                {
                    Id = cfg.Id!,
                    Purpose = ParsePurpose(cfg.Purpose, cfg.Id!),
                    Width = width,
                    Height = height,
                    GrantsEquipmentBonuses = cfg.GrantsEquipmentBonuses,
                    NamedSlots = namedSlots
                }.InitializeBlockedMask(blockedMask);

                _layouts.Register(cfg.Id!, layout);
            }
        }

        private void LoadDefinitions(ConfigCatalog? catalog, ConfigConflictReport? report)
        {
            _definitions.Clear();

            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Items/definitions.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var configs = new List<(string Id, ItemConfig Cfg)>(merged.Count);

            for (int i = 0; i < merged.Count; i++)
            {
                var cfg = merged[i].Node.Deserialize<ItemConfig>(options)
                    ?? throw new InvalidOperationException($"Failed to deserialize item definition '{merged[i].Id}'.");
                cfg.Id ??= merged[i].Id;
                configs.Add((cfg.Id, cfg));
            }

            configs.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Id, b.Id));

            for (int i = 0; i < configs.Count; i++)
            {
                ItemConfig cfg = configs[i].Cfg;
                int shapeId = _shapes.GetId(cfg.Shape ?? string.Empty);
                if (shapeId <= 0)
                {
                    throw new InvalidOperationException($"Item '{cfg.Id}' references missing shape '{cfg.Shape}'.");
                }

                var definition = new ItemDefinition
                {
                    Id = cfg.Id!,
                    DisplayName = string.IsNullOrWhiteSpace(cfg.DisplayName) ? cfg.Id! : cfg.DisplayName!,
                    ShapeId = shapeId,
                    MaxStack = cfg.MaxStack <= 0 ? 1 : cfg.MaxStack,
                    Tags = CompileTags(cfg.Tags),
                    AllowedNamedSlots = cfg.AllowedNamedSlots ?? Array.Empty<string>(),
                    EquipEffectTemplateIds = CompileEffectIds(cfg.EquipEffects, cfg.Id!),
                    AbilityGrants = CompileAbilityGrants(cfg.AbilityGrants, cfg.Id!),
                    MountedContainers = CompileMountedContainers(cfg.MountedContainers, cfg.Id!)
                };

                _definitions.Register(cfg.Id!, definition);
            }
        }

        private static ItemShapeRotation[] BuildRotations(int width, int height, bool[] baseMask)
        {
            var rotations = new ItemShapeRotation[4];
            int currentWidth = width;
            int currentHeight = height;
            bool[] currentMask = baseMask;

            for (int i = 0; i < rotations.Length; i++)
            {
                rotations[i] = new ItemShapeRotation(currentWidth, currentHeight, currentMask);
                (currentMask, currentWidth, currentHeight) = RotateClockwise(currentMask, currentWidth, currentHeight);
            }

            return rotations;
        }

        private static (bool[] Mask, int Width, int Height) RotateClockwise(bool[] source, int width, int height)
        {
            bool[] output = new bool[source.Length];
            int rotatedWidth = height;
            int rotatedHeight = width;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!source[(y * width) + x])
                    {
                        continue;
                    }

                    int newX = height - 1 - y;
                    int newY = x;
                    output[(newY * rotatedWidth) + newX] = true;
                }
            }

            return (output, rotatedWidth, rotatedHeight);
        }

        private static bool[] CompileMask(
            string[]? rows,
            string id,
            string kind,
            int expectedWidth = 0,
            int expectedHeight = 0,
            bool treatBlankAsFilled = true)
        {
            if (rows == null || rows.Length == 0)
            {
                if (expectedWidth <= 0 || expectedHeight <= 0)
                {
                    return Array.Empty<bool>();
                }

                return new bool[expectedWidth * expectedHeight];
            }

            int height = rows.Length;
            int width = rows[0]?.Length ?? 0;
            if (expectedWidth > 0 && width != expectedWidth)
            {
                throw new InvalidOperationException($"{kind} '{id}' row width mismatch. expected={expectedWidth} actual={width}");
            }

            if (expectedHeight > 0 && height != expectedHeight)
            {
                throw new InvalidOperationException($"{kind} '{id}' row count mismatch. expected={expectedHeight} actual={height}");
            }

            var mask = new bool[width * height];
            for (int y = 0; y < height; y++)
            {
                string row = rows[y] ?? string.Empty;
                if (row.Length != width)
                {
                    throw new InvalidOperationException($"{kind} '{id}' contains inconsistent row lengths.");
                }

                for (int x = 0; x < width; x++)
                {
                    char ch = row[x];
                    mask[(y * width) + x] = treatBlankAsFilled
                        ? ch != '.' && ch != '0' && ch != ' '
                        : ch == '#' || ch == 'X' || ch == '1';
                }
            }

            return mask;
        }

        private static ItemNamedSlotDefinition[] CompileNamedSlots(LayoutConfig cfg)
        {
            if (cfg.NamedSlots == null || cfg.NamedSlots.Length == 0)
            {
                return Array.Empty<ItemNamedSlotDefinition>();
            }

            var output = new ItemNamedSlotDefinition[cfg.NamedSlots.Length];
            for (int i = 0; i < cfg.NamedSlots.Length; i++)
            {
                var slot = cfg.NamedSlots[i];
                if (slot == null || string.IsNullOrWhiteSpace(slot.Id))
                {
                    throw new InvalidOperationException($"Layout '{cfg.Id}' contains an invalid named slot entry.");
                }

                output[i] = new ItemNamedSlotDefinition
                {
                    Id = slot.Id!,
                    Label = string.IsNullOrWhiteSpace(slot.Label) ? slot.Id! : slot.Label!,
                    RequiredAll = CompileTags(slot.RequiredAll),
                    BlockedAny = CompileTags(slot.BlockedAny),
                    SingleItemOnly = slot.SingleItemOnly
                };
            }

            return output;
        }

        private static GameplayTagContainer CompileTags(string[]? tags)
        {
            GameplayTagContainer container = default;
            if (tags == null)
            {
                return container;
            }

            for (int i = 0; i < tags.Length; i++)
            {
                string? tag = tags[i];
                if (string.IsNullOrWhiteSpace(tag))
                {
                    continue;
                }

                container.AddTag(TagRegistry.Register(tag));
            }

            return container;
        }

        private static int[] CompileEffectIds(string[]? ids, string itemId)
        {
            if (ids == null || ids.Length == 0)
            {
                return Array.Empty<int>();
            }

            var output = new int[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                int templateId = EffectTemplateIdRegistry.GetId(ids[i] ?? string.Empty);
                if (templateId <= 0)
                {
                    throw new InvalidOperationException($"Item '{itemId}' references missing effect template '{ids[i]}'.");
                }

                output[i] = templateId;
            }

            return output;
        }

        private static ItemAbilityGrant[] CompileAbilityGrants(AbilityGrantConfig[]? configs, string itemId)
        {
            if (configs == null || configs.Length == 0)
            {
                return Array.Empty<ItemAbilityGrant>();
            }

            var output = new ItemAbilityGrant[configs.Length];
            for (int i = 0; i < configs.Length; i++)
            {
                AbilityGrantConfig? cfg = configs[i];
                if (cfg == null || cfg.SlotIndex < 0)
                {
                    throw new InvalidOperationException($"Item '{itemId}' contains an invalid ability grant.");
                }

                int abilityId = AbilityIdRegistry.GetId(cfg.Ability ?? string.Empty);
                if (abilityId <= 0)
                {
                    throw new InvalidOperationException($"Item '{itemId}' references missing ability '{cfg.Ability}'.");
                }

                output[i] = new ItemAbilityGrant
                {
                    SlotIndex = cfg.SlotIndex,
                    AbilityId = abilityId
                };
            }

            return output;
        }

        private ItemMountedContainerDefinition[] CompileMountedContainers(MountedContainerConfig[]? configs, string itemId)
        {
            if (configs == null || configs.Length == 0)
            {
                return Array.Empty<ItemMountedContainerDefinition>();
            }

            var output = new ItemMountedContainerDefinition[configs.Length];
            for (int i = 0; i < configs.Length; i++)
            {
                MountedContainerConfig? cfg = configs[i];
                if (cfg == null || string.IsNullOrWhiteSpace(cfg.Id))
                {
                    throw new InvalidOperationException($"Item '{itemId}' contains an invalid mounted container.");
                }

                int layoutId = _layouts.GetId(cfg.Layout ?? string.Empty);
                if (layoutId <= 0)
                {
                    throw new InvalidOperationException($"Item '{itemId}' references missing mounted layout '{cfg.Layout}'.");
                }

                output[i] = new ItemMountedContainerDefinition
                {
                    Id = cfg.Id!,
                    Label = string.IsNullOrWhiteSpace(cfg.Label) ? cfg.Id! : cfg.Label!,
                    LayoutId = layoutId,
                    Purpose = ParsePurpose(cfg.Purpose, itemId)
                };
            }

            return output;
        }

        private static ItemContainerPurpose ParsePurpose(string? value, string id)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return ItemContainerPurpose.None;
            }

            if (Enum.TryParse<ItemContainerPurpose>(value, ignoreCase: true, out ItemContainerPurpose purpose))
            {
                return purpose;
            }

            throw new InvalidOperationException($"Invalid item container purpose '{value}' in '{id}'.");
        }

        private sealed class ShapeConfig
        {
            public string? Id { get; set; }

            public string[]? Rows { get; set; }

            public bool Rotatable { get; set; } = true;
        }

        private sealed class LayoutConfig
        {
            public string? Id { get; set; }

            public string? Purpose { get; set; }

            public int Width { get; set; }

            public int Height { get; set; }

            public bool GrantsEquipmentBonuses { get; set; }

            public string[]? BlockedRows { get; set; }

            public NamedSlotConfig[]? NamedSlots { get; set; }
        }

        private sealed class NamedSlotConfig
        {
            public string? Id { get; set; }

            public string? Label { get; set; }

            public string[]? RequiredAll { get; set; }

            public string[]? BlockedAny { get; set; }

            public bool SingleItemOnly { get; set; } = true;
        }

        private sealed class ItemConfig
        {
            public string? Id { get; set; }

            public string? DisplayName { get; set; }

            public string? Shape { get; set; }

            public int MaxStack { get; set; } = 1;

            public string[]? Tags { get; set; }

            public string[]? AllowedNamedSlots { get; set; }

            public string[]? EquipEffects { get; set; }

            public AbilityGrantConfig[]? AbilityGrants { get; set; }

            public MountedContainerConfig[]? MountedContainers { get; set; }
        }

        private sealed class AbilityGrantConfig
        {
            public int SlotIndex { get; set; }

            public string? Ability { get; set; }
        }

        private sealed class MountedContainerConfig
        {
            public string? Id { get; set; }

            public string? Label { get; set; }

            public string? Layout { get; set; }

            public string? Purpose { get; set; }
        }
    }
}
