using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
public sealed class BrowserMinimapWebShellShowcaseAcceptanceTests
{
    private const string BindingName = "browser_minimap_composited_overlay_showcase";
    private const string PresetId = "browser_minimap_composited_overlay_cef_raylib";
    private const string ShowcasePath = "mods/showcases/browser_minimap_composited_overlay/BrowserMinimapCompositedOverlayShowcaseMod";

    [Test]
    public void Launcher_ExposesOnlyThePlayableCompositedMinimapShowcaseForWpk8()
    {
        string repoRoot = FindRepoRoot();

        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json")));
        JsonElement binding = config.RootElement
            .GetProperty("bindings")
            .EnumerateArray()
            .Single(item => string.Equals(item.GetProperty("name").GetString(), BindingName, StringComparison.Ordinal));
        JsonElement target = binding.GetProperty("target");
        Assert.That(target.GetProperty("type").GetString(), Is.EqualTo("path"));
        Assert.That(target.GetProperty("value").GetString(), Is.EqualTo(ShowcasePath));
        Assert.That(target.GetProperty("projectPath").GetString(), Is.EqualTo("BrowserMinimapCompositedOverlayShowcaseMod.csproj"));

        using JsonDocument presets = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "launcher.presets.json")));
        JsonElement preset = presets.RootElement
            .GetProperty("presets")
            .EnumerateArray()
            .Single(item => string.Equals(item.GetProperty("id").GetString(), PresetId, StringComparison.Ordinal));
        string[] selectors = preset.GetProperty("selectors").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
        Assert.That(selectors, Is.EqualTo(new[] { "$performer_blacksmith_large_world_nohud", $"${BindingName}" }));
        Assert.That(preset.GetProperty("adapterId").GetString(), Is.EqualTo("raylib"));
        Assert.That(preset.GetProperty("browserRuntime").GetProperty("enabled").GetBoolean(), Is.True);
        Assert.That(preset.GetProperty("browserRuntime").GetProperty("required").GetBoolean(), Is.True);
        Assert.That(preset.GetProperty("browserRuntime").GetProperty("provider").GetString(), Is.EqualTo("cef"));

        string configText = File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json"));
        string presetsText = File.ReadAllText(Path.Combine(repoRoot, "launcher.presets.json"));
        Assert.That(configText + presetsText, Does.Not.Contain("browser_minimap_true_v8"));
        Assert.That(configText + presetsText, Does.Not.Contain("browser_minimap_bridge_compare"));
        Assert.That(configText + presetsText, Does.Not.Contain("browser_minimap_performance"));
        Assert.That(configText + presetsText, Does.Not.Contain("browser_minimap_read_copy"));
        Assert.That(configText + presetsText, Does.Not.Contain("browser_minimap_browser_arraybuffer"));
    }

    [Test]
    public void WebShell_OwnsThePanelFrameButDoesNotRenderGameplayMarkers()
    {
        string modRoot = Path.Combine(FindRepoRoot(), ShowcasePath.Replace('/', Path.DirectorySeparatorChar));
        string index = File.ReadAllText(Path.Combine(modRoot, "Assets", "overlay-app", "index.html"));
        string script = File.ReadAllText(Path.Combine(modRoot, "Assets", "overlay-app", "main.js"));
        string styles = File.ReadAllText(Path.Combine(modRoot, "Assets", "overlay-app", "styles.css"));
        string entry = File.ReadAllText(Path.Combine(modRoot, "BrowserMinimapCompositedOverlayShowcaseModEntry.cs"));
        string manifest = File.ReadAllText(Path.Combine(modRoot, "mod.json"));

        Assert.That(index, Does.Contain("id=\"minimap-widget\""));
        Assert.That(index, Does.Contain("id=\"minimap-viewport\""));
        Assert.That(styles, Does.Contain("pointer-events: auto"));
        Assert.That(styles, Does.Contain("clip-path: circle"));
        Assert.That(script, Does.Contain("type: 'ludots.minimapOverlay.rect'"));
        Assert.That(script, Does.Contain("clip"));
        Assert.That(script, Does.Contain("kind: NATIVE_CLIP_KIND"));
        Assert.That(script, Does.Contain("postDragDelta"));
        Assert.That(script, Does.Contain("window.ludotsDataplane"));
        Assert.That(script, Does.Contain("window.ludotsBrowser"));

        Assert.That(script, Does.Not.Contain("CefSharp"));
        Assert.That(script, Does.Not.Contain("acquireV8Buffer"));
        Assert.That(script, Does.Not.Contain("readSharedBuffer"));
        Assert.That(script, Does.Not.Contain("heatmap"));
        Assert.That(script, Does.Not.Contain("markersJson"));
        Assert.That(script, Does.Not.Contain("for (const marker"));
        Assert.That(manifest, Does.Not.Contain("\"cef\""));
        Assert.That(manifest, Does.Not.Contain("Cef"));
        Assert.That(entry, Does.Contain("BrowserRuntimeServiceNames.BrowserRuntime"));
        Assert.That(entry, Does.Not.Contain("Ludots.UI.Browser.Cef"));
        Assert.That(entry, Does.Not.Contain("CefSharp"));
    }

    [Test]
    public void NativePath_StillOwnsMarkerProjectionAndSkiaClipping()
    {
        string repoRoot = FindRepoRoot();
        string modRoot = Path.Combine(repoRoot, ShowcasePath.Replace('/', Path.DirectorySeparatorChar));
        string bridge = File.ReadAllText(Path.Combine(modRoot, "BrowserMinimapCompositedOverlayNativeMarkerBridgeSystem.cs"));
        string layoutState = File.ReadAllText(Path.Combine(modRoot, "BrowserMinimapCompositedOverlayLayoutState.cs"));
        string minimapRuntime = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Presentation", "Minimap", "MinimapRuntime.cs"));
        string screenMarkers = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Presentation", "Minimap", "MinimapScreenMarkerBuffer.cs"));
        string clipShape = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Presentation", "PresentationClipShape.cs"));
        string skiaRenderer = File.ReadAllText(Path.Combine(repoRoot, "src", "Libraries", "Ludots.Presentation.Skia", "SkiaOverlayRenderer.cs"));
        string browserCanvas = File.ReadAllText(Path.Combine(repoRoot, "src", "Libraries", "Ludots.UI.Browser", "BrowserSurfaceCanvasContent.cs"));

        Assert.That(bridge, Does.Contain("MinimapMarkerBuffer"));
        Assert.That(bridge, Does.Contain("KnowledgeProjectionStore"));
        Assert.That(bridge, Does.Contain("NativeChromeVisible = false"));
        Assert.That(bridge, Does.Contain("SetExternalFieldRect"));
        Assert.That(bridge, Does.Contain("SetFieldClipShape"));
        Assert.That(bridge, Does.Not.Contain("acquireV8Buffer"));
        Assert.That(bridge, Does.Not.Contain("BrowserSharedMemory"));

        Assert.That(layoutState, Does.Contain("ClampCanvasRect"));
        Assert.That(layoutState, Does.Contain("dragDeltaUiX"));
        Assert.That(minimapRuntime, Does.Contain("public bool NativeChromeVisible"));
        Assert.That(minimapRuntime, Does.Contain("SetExternalFieldRect"));
        Assert.That(minimapRuntime, Does.Contain("ResolveFieldClipShape"));
        Assert.That(minimapRuntime, Does.Contain("screenMarkers.SetClipShape"));
        Assert.That(screenMarkers, Does.Contain("public PresentationClipShape ClipShape"));
        Assert.That(screenMarkers, Does.Contain("SetClipShape"));
        Assert.That(clipShape, Does.Contain("PresentationClipShapeKind.Circle"));
        Assert.That(skiaRenderer, Does.Contain("TrySaveClipShape"));
        Assert.That(skiaRenderer, Does.Contain("DrawMinimapMarkersBatched"));
        Assert.That(browserCanvas, Does.Contain("public virtual UiRect GetContentRect"));
        Assert.That(browserCanvas, Does.Contain("_activePointerContentRect"));
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
