using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.Core.Modding
{
    public static class ModManifestJson
    {
        private static readonly HashSet<string> AllowedFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "name",
            "version",
            "description",
            "main",
            "priority",
            "dependencies"
        };

        private static readonly JsonSerializerOptions CanonicalJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static ModManifest ParseStrict(string json, string manifestPath)
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new Exception($"Invalid mod.json (root is not object): {manifestPath}");
            }

            var root = doc.RootElement;
            foreach (var prop in root.EnumerateObject())
            {
                if (!AllowedFields.Contains(prop.Name))
                {
                    throw new Exception($"Invalid mod.json (unknown/forbidden field '{prop.Name}'): {manifestPath}");
                }
            }

            if (!root.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            {
                throw new Exception($"Invalid mod.json (missing 'name'): {manifestPath}");
            }

            if (!root.TryGetProperty("version", out var verEl) || verEl.ValueKind != JsonValueKind.String)
            {
                throw new Exception($"Invalid mod.json (missing 'version'): {manifestPath}");
            }

            var manifest = new ModManifest
            {
                Name = nameEl.GetString(),
                Version = verEl.GetString()
            };

            if (root.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String)
            {
                manifest.Description = descEl.GetString();
            }

            if (root.TryGetProperty("main", out var mainEl) && mainEl.ValueKind == JsonValueKind.String)
            {
                manifest.Main = mainEl.GetString();
            }

            if (root.TryGetProperty("priority", out var priEl))
            {
                if (priEl.ValueKind != JsonValueKind.Number || !priEl.TryGetInt32(out var pri))
                {
                    throw new Exception($"Invalid mod.json ('priority' must be int): {manifestPath}");
                }
                manifest.Priority = pri;
            }

            if (root.TryGetProperty("dependencies", out var depsEl))
            {
                if (depsEl.ValueKind != JsonValueKind.Object)
                {
                    throw new Exception($"Invalid mod.json ('dependencies' must be object): {manifestPath}");
                }

                foreach (var depProp in depsEl.EnumerateObject())
                {
                    if (depProp.Value.ValueKind != JsonValueKind.String)
                    {
                        throw new Exception($"Invalid mod.json ('dependencies.{depProp.Name}' must be string): {manifestPath}");
                    }

                    manifest.Dependencies[depProp.Name] = depProp.Value.GetString() ?? string.Empty;
                }
            }

            if (string.IsNullOrWhiteSpace(manifest.Name))
            {
                throw new Exception($"Invalid mod.json ('name' is empty): {manifestPath}");
            }

            if (string.IsNullOrWhiteSpace(manifest.Version))
            {
                throw new Exception($"Invalid mod.json ('version' is empty): {manifestPath}");
            }

            return manifest;
        }

        public static string ToCanonicalJson(ModManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (manifest.Dependencies == null) manifest.Dependencies = new Dictionary<string, string>();

            return JsonSerializer.Serialize(manifest, CanonicalJsonOptions);
        }
    }
}
