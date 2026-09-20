using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[Category("acceptance")]
public sealed class BrowserMinimapWebShellShowcaseAcceptanceTests
{
    private const string BindingName = "browser_minimap_composited_overlay_showcase";
    private const string PresetId = "browser_minimap_composited_overlay_cef_raylib";
    private const string ShowcasePath = "mods/showcases/browser_minimap_composited_overlay/BrowserMinimapCompositedOverlayShowcaseMod";
    private const string ManifestRelativePath = "Assets/panel-kit/minimap_panel_manifest.json";

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
        Assert.That(selectors, Is.EqualTo(new[] { "$presenter_blacksmith_large_world_nohud", $"${BindingName}" }));
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
        Assert.That(script, Does.Contain("routeParams.get('topic')"));

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
    public void PanelKitManifest_MountsTheMinimapWebShellAsOneDeclaredPanel()
    {
        string modRoot = Path.Combine(FindRepoRoot(), ShowcasePath.Replace('/', Path.DirectorySeparatorChar));
        string manifestPath = Path.Combine(modRoot, ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        string entry = File.ReadAllText(Path.Combine(modRoot, "BrowserMinimapCompositedOverlayShowcaseModEntry.cs"));
        string catalog = File.ReadAllText(Path.Combine(modRoot, "BrowserMinimapCompositedOverlayPanelKitCatalog.cs"));
        string ids = File.ReadAllText(Path.Combine(modRoot, "BrowserMinimapCompositedOverlayPanelKitIds.cs"));
        string project = File.ReadAllText(Path.Combine(modRoot, "BrowserMinimapCompositedOverlayShowcaseMod.csproj"));

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement root = manifest.RootElement;
        Assert.That(root.GetProperty("manifestId").GetString(), Is.EqualTo("wpk.minimap.composited-overlay"));
        Assert.That(root.GetProperty("hostOwnerId").GetString(), Is.EqualTo("BrowserMinimapCompositedOverlay.Showcase"));
        JsonElement panel = root.GetProperty("panels").EnumerateArray().Single();
        Assert.That(panel.GetProperty("panelId").GetString(), Is.EqualTo("panel.minimap.web-shell"));
        Assert.That(panel.GetProperty("panelType").GetString(), Is.EqualTo("minimap.web-shell"));
        Assert.That(panel.GetProperty("surfaceRegionId").GetString(), Is.EqualTo("region.minimap.overlay"));
        Assert.That(panel.GetProperty("surfaceSegment").GetString(), Is.EqualTo("Main"));
        Assert.That(panel.GetProperty("topic").GetString(), Is.EqualTo("wpk.minimap.shell"));
        Assert.That(panel.GetProperty("profileId").GetString(), Is.EqualTo("profile.minimap.composited-overlay"));
        Assert.That(panel.GetProperty("layoutId").GetString(), Is.EqualTo("layout.minimap.floating"));
        Assert.That(panel.GetProperty("inputCapabilityId").GetString(), Is.EqualTo("input.minimap.focus"));

        Assert.That(ids, Does.Contain(ManifestRelativePath));
        Assert.That(catalog, Does.Contain("WebUiPanelKitReferenceCatalog"));
        Assert.That(catalog, Does.Contain("isTopicRegistered"));
        Assert.That(entry, Does.Contain("WebUiPanelKitManifestLoader.LoadFromFile"));
        Assert.That(entry, Does.Contain("BrowserMinimapCompositedOverlayPanelKitCatalog.Create(runtime.IsTopicRegistered)"));
        Assert.That(entry, Does.Contain("new WebUiPanelKitSurfaceBinder(surfaceHost, manifest)"));
        Assert.That(entry, Does.Contain("_panelBinder.Bind(CreatePanelContribution)"));
        Assert.That(entry, Does.Contain("manifest.DeclaredTopics"));
        Assert.That(entry, Does.Not.Contain("surfaceHost.Acquire(new UiSurfaceLeaseRequest"));
        Assert.That(project, Does.Contain("Ludots.WebUI.PanelKit.csproj"));
        Assert.That(project, Does.Contain("Ludots.WebUI.DataPlane.csproj"));
    }

    [Test]
    public void WebShell_ClickFocus_UsesRegisteredDataPlaneCommand()
    {
        string modRoot = Path.Combine(FindRepoRoot(), ShowcasePath.Replace('/', Path.DirectorySeparatorChar));
        string script = File.ReadAllText(Path.Combine(modRoot, "Assets", "overlay-app", "main.js"));
        string entry = File.ReadAllText(Path.Combine(modRoot, "BrowserMinimapCompositedOverlayShowcaseModEntry.cs"));
        string command = File.ReadAllText(Path.Combine(modRoot, "BrowserMinimapCompositedOverlayFocusCommand.cs"));
        string topic = File.ReadAllText(Path.Combine(modRoot, "BrowserMinimapCompositedOverlayTopicProducer.cs"));

        Assert.That(script, Does.Contain("FOCUS_MINIMAP_COMMAND = 'focusMinimap'"));
        Assert.That(script, Does.Contain("postDataPlaneEnvelope('command', dataPlaneTopic"));
        Assert.That(script, Does.Contain("entityRefs: []"));
        Assert.That(script, Does.Contain("normalizedX"));
        Assert.That(script, Does.Contain("normalizedY"));
        Assert.That(script, Does.Contain("postFocusMinimapCommand(event)"));
        Assert.That(script, Does.Contain("window.__LUDOTS_MINIMAP_DATAPLANE__"));
        Assert.That(script, Does.Not.Contain("JumpCameraTo"));
        Assert.That(script, Does.Not.Contain("ApplyPose"));
        Assert.That(script, Does.Not.Contain("CameraPoseRequest"));

        Assert.That(entry, Does.Contain("new WebUiCommandRouter"));
        Assert.That(entry, Does.Contain("router.Register("));
        Assert.That(entry, Does.Contain("FocusMinimapCommand"));
        Assert.That(entry, Does.Contain("new WebUiQueuedCommandDispatcher(router)"));
        Assert.That(entry, Does.Contain("new WebUiDataPlaneRuntime(_commandDispatcher)"));
        Assert.That(entry, Does.Contain("new BrowserMessageBridgeDataTransport(surface.Messages)"));
        Assert.That(entry, Does.Contain("pump.TrackTopic(topic)"));
        Assert.That(command, Does.Contain("IWebUiCommandHandler"));
        Assert.That(command, Does.Contain("TryScreenToWorldClamped"));
        Assert.That(command, Does.Contain("runtime.JumpCameraTo(_engine, worldCm)"));
        Assert.That(command, Does.Contain("Minimap focus commands must not carry entity references."));
        Assert.That(topic, Does.Contain("nativeOwns"));
        Assert.That(topic, Does.Contain("marker-projection"));
        Assert.That(topic, Does.Not.Contain("markersJson"));
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
