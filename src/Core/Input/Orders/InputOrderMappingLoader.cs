using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.Input.Orders
{
    /// <summary>
    /// Loader for input-order mapping configurations.
    /// </summary>
    public sealed class InputOrderMappingLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public InputOrderMappingLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        /// <summary>
        /// Load configuration from ConfigPipeline.
        /// </summary>
        public InputOrderMappingConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Input/input_order_mappings.json")
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);

            if (mergedObject == null)
                return CreateDefaultMobaConfig();

            var config = mergedObject.Deserialize<InputOrderMappingConfig>(JsonOptions);
            return config ?? CreateDefaultMobaConfig();
        }

        /// <summary>
        /// Load configuration from a file path (for user overrides/preferences).
        /// </summary>
        public static InputOrderMappingConfig LoadFromFile(string filePath)
        {
            if (!System.IO.File.Exists(filePath))
                return new InputOrderMappingConfig();

            var content = System.IO.File.ReadAllText(filePath);
            var config = JsonSerializer.Deserialize<InputOrderMappingConfig>(content, JsonOptions);
            return config ?? new InputOrderMappingConfig();
        }

        /// <summary>
        /// Load configuration from a stream (for VFS access).
        /// </summary>
        public static InputOrderMappingConfig LoadFromStream(System.IO.Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            var config = JsonSerializer.Deserialize<InputOrderMappingConfig>(stream, JsonOptions);
            return config ?? throw new InvalidOperationException("Deserialized null from input_order_mappings stream.");
        }

        /// <summary>
        /// Save configuration to JSON.
        /// </summary>
        public static string SaveToJson(InputOrderMappingConfig config)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            return JsonSerializer.Serialize(config, options);
        }

        /// <summary>
        /// Save configuration to a file.
        /// </summary>
        public static void SaveToFile(string filePath, InputOrderMappingConfig config)
        {
            var json = SaveToJson(config);
            var directory = System.IO.Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }
            System.IO.File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Create default mappings for MOBA-style gameplay.
        /// </summary>
        public static InputOrderMappingConfig CreateDefaultMobaConfig()
        {
            return new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.TargetFirst,
                Mappings = new()
                {
                    new InputOrderMapping
                    {
                        ActionId = "SkillQ",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTagKey = "castAbility",
                        ArgsTemplate = new OrderArgsTemplate { I0 = 0 },
                        RequireSelection = false,
                        SelectionType = OrderSelectionType.Entity,
                        IsSkillMapping = true
                    },
                    new InputOrderMapping
                    {
                        ActionId = "SkillW",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTagKey = "castAbility",
                        ArgsTemplate = new OrderArgsTemplate { I0 = 1 },
                        RequireSelection = false,
                        SelectionType = OrderSelectionType.None,
                        IsSkillMapping = true
                    },
                    new InputOrderMapping
                    {
                        ActionId = "SkillE",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTagKey = "castAbility",
                        ArgsTemplate = new OrderArgsTemplate { I0 = 2 },
                        RequireSelection = false,
                        SelectionType = OrderSelectionType.Entity,
                        IsSkillMapping = true
                    },
                    new InputOrderMapping
                    {
                        ActionId = "SkillR",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTagKey = "castAbility",
                        ArgsTemplate = new OrderArgsTemplate { I0 = 3 },
                        RequireSelection = false,
                        SelectionType = OrderSelectionType.Entity,
                        IsSkillMapping = true
                    },
                    new InputOrderMapping
                    {
                        ActionId = "Command",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTagKey = "castAbility",
                        ArgsTemplate = new OrderArgsTemplate { I0 = 4 },
                        RequireSelection = true,
                        SelectionType = OrderSelectionType.Ground,
                        IsSkillMapping = false
                    }
                },
                UserOverrides = new UserOverrideSettings
                {
                    Enabled = true,
                    PersistPath = "user://input_preferences.json"
                }
            };
        }
    }
}
