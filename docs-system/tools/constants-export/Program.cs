using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ludots.DocsTools.ConstantsExport;

/// <summary>
/// 反射导出器：读取 allowlist.json 列出的静态类，把其 public const / static readonly 基元字段
/// 导出为 docs-system/generated/constants.json。CI 重新运行并要求 git diff 干净。
/// 用法: dotnet run -- [outputPath]
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var toolDir = AppContext.BaseDirectory;
        var allowlistPath = Path.Combine(toolDir, "allowlist.json");
        if (!File.Exists(allowlistPath))
        {
            Console.Error.WriteLine($"allowlist.json not found at {allowlistPath}");
            return 1;
        }

        var output = args.Length > 0
            ? args[0]
            : ResolveRepoRelative("docs-system/generated/constants.json");

        var allow = JsonNode.Parse(File.ReadAllText(allowlistPath))!.AsObject();
        var constants = new JsonObject();

        foreach (var entry in allow["types"]!.AsArray())
        {
            var typeName = entry!["type"]!.GetValue<string>();
            var declaredIn = entry["declaredIn"]?.GetValue<string>() ?? "";
            var defaultUnit = entry["defaultUnit"]?.GetValue<string>() ?? "";

            var type = ResolveType(typeName);
            if (type is null)
            {
                Console.Error.WriteLine($"Type not found: {typeName}");
                return 1;
            }

            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!(f.IsLiteral || f.IsInitOnly)) continue;
                object? value = f.IsLiteral ? f.GetRawConstantValue() : f.GetValue(null);
                if (value is null) continue;
                if (!IsPrimitiveLike(value)) continue;

                var key = $"{type.Name}.{f.Name}";
                var unit = ReadDocConstantUnit(f) ?? defaultUnit;
                var node = new JsonObject
                {
                    ["value"] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
                    ["type"] = TypeKeyword(f.FieldType)
                };
                if (!string.IsNullOrEmpty(unit)) node["unit"] = unit;
                if (!string.IsNullOrEmpty(declaredIn)) node["declaredIn"] = declaredIn;
                constants[key] = node;
            }
        }

        var manifest = new JsonObject
        {
            ["schemaVersion"] = "1.0",
            ["generatedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["generator"] = "ludots-doc-constants-exporter",
            ["constants"] = constants
        };

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(output, manifest.ToJsonString(opts) + "\n");
        Console.WriteLine($"Wrote {constants.Count} constants -> {output}");
        return 0;
    }

    private static bool IsPrimitiveLike(object v) =>
        v is bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or string or char;

    private static string TypeKeyword(Type t) => t.Name switch
    {
        "Int32" => "int", "Int64" => "long", "Single" => "float", "Double" => "double",
        "Boolean" => "bool", "String" => "string", "Byte" => "byte", "Int16" => "short",
        _ => t.Name
    };

    private static string? ReadDocConstantUnit(FieldInfo f)
    {
        foreach (var a in f.GetCustomAttributesData())
        {
            if (a.AttributeType.Name != "DocConstantAttribute") continue;
            foreach (var na in a.NamedArguments)
                if (na.MemberName == "Unit") return na.TypedValue.Value as string;
        }
        return null;
    }

    private static Type? ResolveType(string fullName)
    {
        var t = FindType(fullName);
        if (t is not null) return t;
        // Force-load every assembly shipped next to the tool, then retry.
        foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
        {
            try { Assembly.LoadFrom(dll); } catch { /* ignore non-managed/duplicate */ }
        }
        return FindType(fullName);
    }

    private static Type? FindType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(fullName);
            if (t is not null) return t;
        }
        return null;
    }

    private static string ResolveRepoRelative(string rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, ".gitbook.yaml")))
            dir = dir.Parent;
        var root = dir?.FullName ?? Directory.GetCurrentDirectory();
        return Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
    }
}
