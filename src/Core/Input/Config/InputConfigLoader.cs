using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ludots.Core.Input.Config
{
    public static class InputConfigLoader
    {
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        public static InputConfigRoot Load(string jsonContent)
        {
            try
            {
                return JsonSerializer.Deserialize<InputConfigRoot>(jsonContent, _options) ?? new InputConfigRoot();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading input config: {ex.Message}");
                return new InputConfigRoot();
            }
        }

        public static async Task<InputConfigRoot> LoadFromFileAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Input config file not found: {filePath}");
                return new InputConfigRoot();
            }

            string json = await File.ReadAllTextAsync(filePath);
            return Load(json);
        }

        public static InputConfigRoot LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Input config file not found: {filePath}");
                return new InputConfigRoot();
            }

            var json = File.ReadAllText(filePath);
            return Load(json);
        }

        public static string Serialize(InputConfigRoot config)
        {
            return JsonSerializer.Serialize(config, _options);
        }
    }
}
