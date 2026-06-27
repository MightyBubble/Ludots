using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class LiveMapEditorLauncherContractTests
{
    [Test]
    public void LauncherPreset_StacksCefRuntimeUatMapAndLiveEditorCapability()
    {
        string repoRoot = FindRepoRoot();
        JsonObject presets = ReadObject(Path.Combine(repoRoot, "launcher.presets.json"));
        JsonArray presetArray = presets["presets"] as JsonArray
            ?? throw new InvalidDataException("launcher.presets.json must contain a presets array.");
        JsonObject preset = presetArray
            .OfType<JsonObject>()
            .Single(obj => string.Equals(obj["id"]?.GetValue<string>(), "live_map_editor_nav_grid_cef_raylib", StringComparison.Ordinal));
        string[] selectors = (preset["selectors"] as JsonArray
                ?? throw new InvalidDataException("live map editor preset must declare selectors."))
            .Select(node => node?.GetValue<string>() ?? string.Empty)
            .ToArray();

        Assert.That(selectors, Is.EqualTo(new[]
        {
            "$browser_cef_runtime",
            "$live_map_editor_nav_grid_uat",
            "$live_map_editor"
        }));
        Assert.That(preset["adapterId"]?.GetValue<string>(), Is.EqualTo("raylib"));
    }

    [Test]
    public void LauncherConfig_RegistersLiveMapEditorModsByStrictPath()
    {
        string repoRoot = FindRepoRoot();
        JsonObject config = ReadObject(Path.Combine(repoRoot, "launcher.config.json"));
        JsonArray bindings = config["bindings"] as JsonArray
            ?? throw new InvalidDataException("launcher.config.json must contain a bindings array.");

        AssertModPath(
            bindings,
            "live_map_editor",
            "mods/capabilities/live_map_editor/LiveMapEditorMod",
            "LiveMapEditorMod.csproj");
        AssertModPath(
            bindings,
            "live_map_editor_nav_grid_uat",
            "mods/capabilities/live_map_editor/LiveMapEditorNavGridUatMod",
            expectedProjectPath: null);
    }

    [Test]
    public void UatMapFragment_DeclaresExplicitLiveEditorSaveTarget()
    {
        string repoRoot = FindRepoRoot();
        JsonObject map = ReadObject(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorNavGridUatMod",
            "assets",
            "Maps",
            "live_editor_nav_grid.json"));

        Assert.That(map["Metadata"]?["liveMapEditor"]?["saveTarget"]?.GetValue<bool>(), Is.True);
        Assert.That(map["ParentId"]?.GetValue<string>(), Is.EqualTo("nav_editor_grid"));
        Assert.That(map["Boards"] as JsonArray, Is.Not.Null.And.Count.GreaterThan(0));
        foreach (JsonObject board in (map["Boards"] as JsonArray)!.OfType<JsonObject>())
        {
            Assert.That(board["DataFile"], Is.Null);
        }
    }

    [Test]
    public void PanelAssetPath_UsesLowercaseMountedAssetsPath()
    {
        string repoRoot = FindRepoRoot();
        string ids = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "LiveMapEditorIds.cs"));

        Assert.That(ids, Does.Contain("LiveMapEditorMod:assets/live-map-editor-app/index.html"));
        Assert.That(ids, Does.Not.Contain("LiveMapEditorMod:Assets/"));
        Assert.That(File.Exists(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "assets",
            "live-map-editor-app",
            "index.html")), Is.True);
    }

    [Test]
    public void WebPanel_DoesNotFallbackBrushNumbers()
    {
        string repoRoot = FindRepoRoot();
        string appJs = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "capabilities",
            "live_map_editor",
            "LiveMapEditorMod",
            "assets",
            "live-map-editor-app",
            "app.js"));

        Assert.That(appJs, Does.Not.Contain("fallback"));
        Assert.That(appJs, Does.Contain("checkValidity"));
    }

    private static void AssertModPath(
        JsonArray mods,
        string name,
        string expectedPath,
        string? expectedProjectPath)
    {
        JsonObject mod = mods
            .OfType<JsonObject>()
            .Single(obj => string.Equals(obj["name"]?.GetValue<string>(), name, StringComparison.Ordinal));
        JsonObject target = mod["target"] as JsonObject
            ?? throw new InvalidDataException($"Launcher mod '{name}' target must be an object.");

        Assert.That(target["type"]?.GetValue<string>(), Is.EqualTo("path"));
        Assert.That(target["value"]?.GetValue<string>(), Is.EqualTo(expectedPath));
        if (expectedProjectPath == null)
        {
            Assert.That(target["projectPath"], Is.Null);
        }
        else
        {
            Assert.That(target["projectPath"]?.GetValue<string>(), Is.EqualTo(expectedProjectPath));
        }
    }

    private static JsonObject ReadObject(string path)
        => JsonNode.Parse(File.ReadAllText(path)) as JsonObject
           ?? throw new InvalidDataException($"{path} must contain a JSON object.");

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "launcher.config.json")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root containing launcher.config.json");
    }
}
