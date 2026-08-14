using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[Category("acceptance")]
public sealed class CncTriNationShowcaseAcceptanceTests
{
    private const string BindingName = "cnc_tri_nation_showcase";
    private const string PresetId = "cnc_tri_nation_raylib";
    private const string RegistryId = "cnc_tri_nation";
    private const string ShowcasePath = "mods/showcases/cnc_tri_nation/CncTriNationFullGameMod";
    private const string ProjectPath = "CncTriNationFullGameMod.csproj";
    private const string Topic = "ludots.cnc.triNation.world";

    [Test]
    public void CncTriNation_PlayerEntry_IsRegisteredAndRequiresBrowserHud()
    {
        string repoRoot = FindRepoRoot();
        AssertLauncherBinding(repoRoot);
        AssertLauncherPreset(repoRoot);
        AssertShowcaseRegistry(repoRoot);

        string modRoot = Path.Combine(repoRoot, ShowcasePath.Replace('/', Path.DirectorySeparatorChar));
        string readme = File.ReadAllText(Path.Combine(modRoot, "README.md"));
        string entry = File.ReadAllText(Path.Combine(modRoot, "CncTriNationFullGameModEntry.cs"));
        string gameJson = File.ReadAllText(Path.Combine(modRoot, "assets", "game.json"));

        using JsonDocument game = JsonDocument.Parse(gameJson);
        Assert.That(game.RootElement.GetProperty("startupMapId").GetString(), Is.EqualTo("cnc_tri_nation_war"));
        Assert.That(game.RootElement.GetProperty("startupLocalPlayerId").GetInt32(), Is.EqualTo(1));
        JsonElement browserRuntime = game.RootElement.GetProperty("browserRuntime");
        Assert.That(browserRuntime.GetProperty("enabled").GetBoolean(), Is.True);
        Assert.That(browserRuntime.GetProperty("required").GetBoolean(), Is.True);
        Assert.That(browserRuntime.GetProperty("provider").GetString(), Is.EqualTo("cef"));

        Assert.That(entry, Does.Contain("assets/cnc-tri-nation-app/index.html"));
        Assert.That(entry, Does.Contain("requires IBrowserRuntime"));
        Assert.That(entry, Does.Not.Contain("raylib-only mode"));
        Assert.That(readme, Does.Contain("scripts\\run-mod-launcher.cmd cli launch cnc_tri_nation_showcase --adapter raylib"));
        Assert.That(readme, Does.Contain(Topic));
    }

    [Test]
    public void CncTriNation_ContentAndHud_ArePlayerFacingAndDataPlaneBacked()
    {
        string modRoot = Path.Combine(FindRepoRoot(), ShowcasePath.Replace('/', Path.DirectorySeparatorChar));
        string assets = Path.Combine(modRoot, "assets");

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(modRoot, "mod.json")));
        JsonElement dependencies = manifest.RootElement.GetProperty("dependencies");
        Assert.That(dependencies.TryGetProperty("LudotsCoreMod", out _), Is.True);
        Assert.That(dependencies.TryGetProperty("CoreInputMod", out _), Is.True);
        Assert.That(dependencies.TryGetProperty("EntityCommandPanelMod", out _), Is.True);
        Assert.That(dependencies.TryGetProperty("RtsDemoMod", out _), Is.True);

        using JsonDocument roster = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "roster_catalog.json")));
        using JsonDocument templates = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "Entities", "templates.json")));
        using JsonDocument abilities = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "GAS", "abilities.json")));
        using JsonDocument effects = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "GAS", "effects.json")));
        using JsonDocument graphs = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "GAS", "graphs.json")));
        using JsonDocument presenters = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "Presentation", "presenters.json")));
        using JsonDocument map = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "Maps", "cnc_tri_nation_war.json")));

        Assert.That(roster.RootElement.GetProperty("unitCount").GetInt32(), Is.EqualTo(102));
        Assert.That(roster.RootElement.GetProperty("roster").GetArrayLength(), Is.EqualTo(102));
        Assert.That(templates.RootElement.GetArrayLength(), Is.GreaterThanOrEqualTo(100));
        Assert.That(abilities.RootElement.GetArrayLength(), Is.GreaterThanOrEqualTo(100));
        Assert.That(effects.RootElement.GetArrayLength(), Is.GreaterThanOrEqualTo(180));
        Assert.That(graphs.RootElement.GetArrayLength(), Is.GreaterThanOrEqualTo(3));
        Assert.That(presenters.RootElement.GetArrayLength(), Is.GreaterThanOrEqualTo(100));
        Assert.That(map.RootElement.GetProperty("Entities").GetArrayLength(), Is.GreaterThanOrEqualTo(18));

        string index = File.ReadAllText(Path.Combine(assets, "cnc-tri-nation-app", "index.html"));
        Assert.That(index, Does.Contain("Tri-Nation Command"));
        Assert.That(index, Does.Contain(Topic));
        Assert.That(index, Does.Contain("selectEntity"));
        Assert.That(index, Does.Contain("activateAbilitySlot"));
        Assert.That(index, Does.Contain("switchParticipantView"));
        Assert.That(index, Does.Contain("DataPlane transport missing"));
        Assert.That(index, Does.Contain("Field roster"));
        Assert.That(index, Does.Contain("Selected unit commands"));
        Assert.That(index, Does.Not.Contain("CefSharp"));
    }

    private static void AssertLauncherBinding(string repoRoot)
    {
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json")));
        JsonElement binding = config.RootElement
            .GetProperty("bindings")
            .EnumerateArray()
            .Single(item => string.Equals(item.GetProperty("name").GetString(), BindingName, StringComparison.Ordinal));

        JsonElement target = binding.GetProperty("target");
        Assert.That(target.GetProperty("type").GetString(), Is.EqualTo("path"));
        Assert.That(target.GetProperty("value").GetString(), Is.EqualTo(ShowcasePath));
        Assert.That(target.GetProperty("projectPath").GetString(), Is.EqualTo(ProjectPath));
    }

    private static void AssertLauncherPreset(string repoRoot)
    {
        using JsonDocument presets = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "launcher.presets.json")));
        JsonElement preset = presets.RootElement
            .GetProperty("presets")
            .EnumerateArray()
            .Single(item => string.Equals(item.GetProperty("id").GetString(), PresetId, StringComparison.Ordinal));

        string[] selectors = preset.GetProperty("selectors").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
        Assert.That(selectors, Is.EqualTo(new[] { $"${BindingName}" }));
        Assert.That(preset.GetProperty("adapterId").GetString(), Is.EqualTo("raylib"));
        Assert.That(preset.GetProperty("browserRuntime").GetProperty("enabled").GetBoolean(), Is.True);
        Assert.That(preset.GetProperty("browserRuntime").GetProperty("required").GetBoolean(), Is.True);
        Assert.That(preset.GetProperty("browserRuntime").GetProperty("provider").GetString(), Is.EqualTo("cef"));
    }

    private static void AssertShowcaseRegistry(string repoRoot)
    {
        using JsonDocument registry = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "showcase.registry.json")));
        JsonElement showcase = registry.RootElement
            .GetProperty("showcases")
            .EnumerateArray()
            .Single(item => string.Equals(item.GetProperty("id").GetString(), RegistryId, StringComparison.Ordinal));

        Assert.That(showcase.GetProperty("path").GetString(), Is.EqualTo(ShowcasePath));
        Assert.That(showcase.GetProperty("binding").GetString(), Is.EqualTo(BindingName));
        Assert.That(showcase.GetProperty("preset").GetString(), Is.EqualTo(PresetId));
        Assert.That(showcase.GetProperty("readmePath").GetString(), Is.EqualTo($"{ShowcasePath}/README.md"));
        Assert.That(showcase.GetProperty("acceptanceTest").GetString(), Is.EqualTo(nameof(CncTriNationShowcaseAcceptanceTests)));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(current.FullName, "src")) &&
                Directory.Exists(Path.Combine(current.FullName, "mods")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
