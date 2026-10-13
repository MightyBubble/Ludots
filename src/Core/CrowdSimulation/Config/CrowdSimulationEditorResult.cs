using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ludots.Core.CrowdSimulation.Config;

public sealed class CrowdSimulationAuthoringIssue
{
    public required string Path { get; init; }
    public required string Message { get; init; }
    public string? SourceMod { get; init; }
    public string? SourceFile { get; init; }

    public JsonObject ToJson()
    {
        var node = new JsonObject
        {
            ["path"] = Path,
            ["message"] = Message,
        };
        if (!string.IsNullOrEmpty(SourceMod))
        {
            node["sourceMod"] = SourceMod;
        }

        if (!string.IsNullOrEmpty(SourceFile))
        {
            node["sourceFile"] = SourceFile;
        }

        return node;
    }
}

public sealed class CrowdSimulationEditorResult
{
    public bool Ok { get; init; }
    public bool Rebuilt { get; init; }
    public IReadOnlyList<CrowdSimulationAuthoringIssue> Issues { get; init; } = Array.Empty<CrowdSimulationAuthoringIssue>();
    public IReadOnlyList<string> Written { get; init; } = Array.Empty<string>();

    public static CrowdSimulationEditorResult Succeeded(IReadOnlyList<string>? written = null, bool rebuilt = false)
        => new()
        {
            Ok = true,
            Rebuilt = rebuilt,
            Written = written ?? Array.Empty<string>(),
        };

    public static CrowdSimulationEditorResult Failed(IReadOnlyList<CrowdSimulationAuthoringIssue> issues)
        => new()
        {
            Ok = false,
            Issues = issues,
        };

    public JsonObject ToJson()
    {
        var issues = new JsonArray();
        for (int i = 0; i < Issues.Count; i++)
        {
            issues.Add(Issues[i].ToJson());
        }

        var written = new JsonArray();
        for (int i = 0; i < Written.Count; i++)
        {
            written.Add(Written[i]);
        }

        var node = new JsonObject
        {
            ["ok"] = Ok,
            ["rebuilt"] = Rebuilt,
            ["issues"] = issues,
            ["written"] = written,
        };
        if (Rebuilt)
        {
            node["lost"] = new JsonArray("commands", "runtimePlacements", "explorationMemory");
        }

        return node;
    }
}

public static class CrowdSimulationAuthoringIssues
{
    private static readonly Regex FieldPath = new(
        @"[A-Za-z_][\w]*(?:(?:\.[A-Za-z_][\w]*)|\[\d+\])+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<CrowdSimulationAuthoringIssue> From(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        string message = ex.Message;
        string? sourceFile = null;
        string? sourceMod = null;
        int mark = message.IndexOf("\n配置路径:", StringComparison.Ordinal);
        string body = mark >= 0 ? message[..mark] : message;
        if (mark >= 0)
        {
            string tail = message[mark..];
            sourceFile = WinnerUri(tail) ?? LastFragment(tail);
            sourceMod = ModIdOf(sourceFile);
        }

        return new[]
        {
            new CrowdSimulationAuthoringIssue
            {
                Path = FieldPath.Match(body).Success ? FieldPath.Match(body).Value : "",
                Message = body.Trim(),
                SourceMod = sourceMod,
                SourceFile = sourceFile,
            },
        };
    }

    public static IReadOnlyList<CrowdSimulationAuthoringIssue> One(string path, string message, string? sourceFile = null)
        => new[]
        {
            new CrowdSimulationAuthoringIssue
            {
                Path = path,
                Message = message,
                SourceMod = ModIdOf(sourceFile),
                SourceFile = sourceFile,
            },
        };

    public static string? ModIdOf(string? sourceUri)
    {
        if (string.IsNullOrEmpty(sourceUri))
        {
            return null;
        }

        int colon = sourceUri.IndexOf(':');
        return colon <= 0 ? sourceUri : sourceUri[..colon];
    }

    private static string? WinnerUri(string tail)
    {
        const string mark = "胜出: ";
        int at = tail.IndexOf(mark, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        string rest = tail[(at + mark.Length)..].Trim();
        int line = rest.IndexOf('\n');
        if (line >= 0)
        {
            rest = rest[..line].Trim();
        }

        if (rest.Length == 0 || rest.StartsWith("（", StringComparison.Ordinal))
        {
            return null;
        }

        int eq = rest.IndexOf('=');
        if (eq >= 0 && rest.Contains(':'))
        {
            return rest[(eq + 1)..].Trim();
        }

        return rest;
    }

    private static string? LastFragment(string tail)
    {
        const string mark = "合并顺序: ";
        int at = tail.IndexOf(mark, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        string rest = tail[(at + mark.Length)..];
        int line = rest.IndexOf('\n');
        if (line >= 0)
        {
            rest = rest[..line];
        }

        string[] parts = rest.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? null : parts[^1];
    }
}
