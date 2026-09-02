using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Sango.Content
{
    /// <summary>
    /// Headless inventory of the Sango JSON tables under a SangoContentMod install's assets/.
    /// Key = asset path relative to assets/ with '/' separators (e.g. "Data/Common/TroopTypes.json").
    /// Value = row count: array root uses its length; object roots take the largest direct
    /// collection field (array length, or property count for sango's id-keyed table objects).
    /// </summary>
    public static class SangoContentCatalog
    {
        public static IReadOnlyDictionary<string, int> CountRows(string modRoot)
        {
            if (string.IsNullOrWhiteSpace(modRoot))
                throw new ArgumentException("Mod root directory is required.", nameof(modRoot));

            var assetsRoot = Path.Combine(Path.GetFullPath(modRoot), "assets");
            if (!Directory.Exists(assetsRoot))
                throw new DirectoryNotFoundException($"SangoContentMod assets directory was not found: {assetsRoot}");

            var rowsByTable = new Dictionary<string, int>(StringComparer.Ordinal);
            CollectTableRows(Path.Combine(assetsRoot, "Data"), assetsRoot, rowsByTable);
            CollectTableRows(Path.Combine(assetsRoot, "Scenario"), assetsRoot, rowsByTable);
            return rowsByTable;
        }

        private static void CollectTableRows(string tableRoot, string assetsRoot, Dictionary<string, int> rowsByTable)
        {
            if (!Directory.Exists(tableRoot)) return;

            foreach (string file in Directory.EnumerateFiles(tableRoot, "*.json", SearchOption.AllDirectories))
            {
                var fullPath = Path.GetFullPath(file);
                using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
                rowsByTable[MakeAssetKey(assetsRoot, fullPath)] = CountTopLevelRows(document.RootElement);
            }
        }

        private static int CountTopLevelRows(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
                return root.GetArrayLength();

            if (root.ValueKind != JsonValueKind.Object)
                return 0;

            int max = 0;
            foreach (JsonProperty field in root.EnumerateObject())
            {
                int rows = field.Value.ValueKind switch
                {
                    JsonValueKind.Array => field.Value.GetArrayLength(),
                    JsonValueKind.Object => CountProperties(field.Value),
                    _ => 0,
                };
                if (rows > max) max = rows;
            }
            return max;
        }

        private static int CountProperties(JsonElement element)
        {
            int count = 0;
            foreach (JsonProperty _ in element.EnumerateObject())
                count++;
            return count;
        }

        private static string MakeAssetKey(string assetsRoot, string fullPath)
        {
            string rootWithSep = assetsRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? assetsRoot
                : assetsRoot + Path.DirectorySeparatorChar;
            return fullPath.Substring(rootWithSep.Length)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
        }
    }
}
