using System;
using System.IO;
using System.Text.Json;

namespace Ludots.Core.Input.Orders
{
    /// <summary>
    /// Loader for input-order mapping configurations.
    /// </summary>
    public static class InputOrderMappingLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        
        /// <summary>
        /// Load configuration from JSON content.
        /// </summary>
        public static InputOrderMappingConfig LoadFromJson(string jsonContent)
        {
            if (string.IsNullOrEmpty(jsonContent))
                throw new ArgumentException("Input order mapping JSON content must not be null or empty.", nameof(jsonContent));

            var config = JsonSerializer.Deserialize<InputOrderMappingConfig>(jsonContent, JsonOptions);
            return config ?? throw new InvalidOperationException("Deserialized null from input_order_mappings JSON.");
        }

        /// <summary>
        /// Load configuration from a stream (e.g. from VFS).
        /// </summary>
        public static InputOrderMappingConfig LoadFromStream(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            var config = JsonSerializer.Deserialize<InputOrderMappingConfig>(stream, JsonOptions);
            return config ?? throw new InvalidOperationException("Deserialized null from input_order_mappings stream.");
        }
        
        /// <summary>
        /// Load configuration from a file path.
        /// </summary>
        public static InputOrderMappingConfig LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Input order mapping file not found: {filePath}", filePath);

            var content = File.ReadAllText(filePath);
            return LoadFromJson(content);
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
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(filePath, json);
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
                        SelectionType = OrderSelectionType.None,  // W is self-cast
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
                        IsSkillMapping = false  // movement is not affected by InteractionMode
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
