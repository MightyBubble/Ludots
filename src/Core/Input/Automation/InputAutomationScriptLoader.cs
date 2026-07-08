using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.Core.Input.Automation
{
    public static class InputAutomationScriptLoader
    {
        public const string ScriptEnvironmentVariable = "LUDOTS_INPUT_AUTOMATION_SCRIPT";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
        };

        public static IReadOnlyList<InputAutomationCommand> LoadCommands(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Input automation script path is required.", nameof(path));
            }

            string fullPath = Path.GetFullPath(path);
            string json = File.ReadAllText(fullPath);
            var document = JsonSerializer.Deserialize<InputAutomationScriptDocument>(json, JsonOptions)
                ?? throw new InvalidDataException($"Input automation script '{fullPath}' is empty or invalid.");
            return document.Commands;
        }

        public static bool TryCreatePlayerFromEnvironment(out InputAutomationPlayer? player)
        {
            string? path = Environment.GetEnvironmentVariable(ScriptEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(path))
            {
                player = null;
                return false;
            }

            player = new InputAutomationPlayer(LoadCommands(path));
            return true;
        }

        private sealed class InputAutomationScriptDocument
        {
            public List<InputAutomationCommand> Commands { get; init; } = new();
        }
    }
}
