using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Engine;
using NUnit.Framework;

namespace Ludots.Tests.Architecture.Governance;

[Category("arch-guard")]
public sealed class ClockCalendarLayeringArchitectureGuardTests
{
    private static readonly string[] OfficialClockJsonPaths =
    {
        "assets/Engine/clock.json",
        "assets/GAS/clock.json",
        "assets/Physics2D/clock.json",
    };

    private static readonly string[] ClockLoaderPaths =
    {
        "src/Core/Engine/EngineClockConfig.cs",
        "src/Core/Gameplay/GAS/Config/GasClockConfig.cs",
        "src/Core/Engine/Physics2D/Physics2DClockConfig.cs",
        "src/Core/Engine/ClockFoundation.cs",
    };

    private static readonly string[] ForbiddenDayTokens =
    {
        "dayIndex",
        "ticksPerDay",
        "dayPhases",
        "DayPermille",
        "minutesPerDay",
        "startDayIndex",
        "Calendar.Day",
        "Clock.Day",
    };

    [Test]
    public void ClockDomainId_DoesNotIncludeDay()
    {
        Assert.That(Enum.GetNames<ClockDomainId>(), Is.EquivalentTo(new[]
        {
            "FixedFrame",
            "Step",
            "PhysicsStep",
            "NavigationStep",
        }));
    }

    [Test]
    public void OfficialClockJson_RejectsDayAndCalendarKeys()
    {
        string repoRoot = FindRepoRoot();
        foreach (string relativePath in OfficialClockJsonPaths)
        {
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(repoRoot, relativePath)));
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                Assert.That(
                    LooksLikeDayOrCalendarKey(property.Name),
                    Is.False,
                    $"{relativePath} must not declare '{property.Name}'.");
            }
        }
    }

    [Test]
    public void ConfigCatalog_RegistersCalendarWorldInsteadOfCalendarClock()
    {
        string catalogPath = Path.Combine(FindRepoRoot(), "assets", "config_catalog.json");
        JsonArray catalog = JsonNode.Parse(File.ReadAllText(catalogPath))!.AsArray();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? node in catalog)
        {
            string? path = node?["Path"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }

        Assert.That(paths.Contains("Calendar/world.json"), Is.True);
        Assert.That(paths.Contains("Calendar/calendars.json"), Is.True);
        Assert.That(paths.Contains("Calendar/clock.json"), Is.False);
    }

    [Test]
    public void ClockLoaders_DoNotMentionDayBusinessFields()
    {
        string repoRoot = FindRepoRoot();
        var hits = new List<string>();
        foreach (string relativePath in ClockLoaderPaths)
        {
            string[] lines = File.ReadAllLines(Path.Combine(repoRoot, relativePath));
            for (int i = 0; i < lines.Length; i++)
            {
                for (int t = 0; t < ForbiddenDayTokens.Length; t++)
                {
                    if (lines[i].Contains(ForbiddenDayTokens[t], StringComparison.Ordinal))
                    {
                        hits.Add($"{relativePath}:{i + 1}: {lines[i].Trim()}");
                        break;
                    }
                }
            }
        }

        Assert.That(hits, Is.Empty, "Clock loaders must not grow day or calendar fields.");
    }

    private static bool LooksLikeDayOrCalendarKey(string key)
    {
        return key.Contains("day", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("calendar", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("season", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("year", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
    }
}
