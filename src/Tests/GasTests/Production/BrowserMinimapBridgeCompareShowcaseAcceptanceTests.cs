using System.Buffers.Binary;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserMinimapBridgeCompareShowcaseMod;
using BrowserMinimapPerformanceShowcaseMod;
using Ludots.Core.Presentation.Minimap;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Production;

[TestFixture]
public sealed class BrowserMinimapBridgeCompareShowcaseAcceptanceTests
{
	private const string BindingName = "browser_minimap_bridge_compare_showcase";
	private const string PresetId = "browser_minimap_bridge_compare_cef_raylib";
	private const string LargeWorldMapId = "performer_blacksmith_minimap_marker_large_world_showcase";
	private static readonly (string Binding, string Preset, string ModPath, string Project, string Mode)[] SinglePathShowcases =
	{
		(
			"browser_minimap_read_copy_showcase",
			"browser_minimap_read_copy_cef_raylib",
			"mods/showcases/browser_minimap_read_copy/BrowserMinimapReadCopyShowcaseMod",
			"BrowserMinimapReadCopyShowcaseMod.csproj",
			"read-copy"),
		(
			"browser_minimap_browser_arraybuffer_showcase",
			"browser_minimap_browser_arraybuffer_cef_raylib",
			"mods/showcases/browser_minimap_browser_arraybuffer/BrowserMinimapBrowserArrayBufferShowcaseMod",
			"BrowserMinimapBrowserArrayBufferShowcaseMod.csproj",
			"browser-arraybuffer"),
		(
			"browser_minimap_true_v8_showcase",
			"browser_minimap_true_v8_cef_raylib",
			"mods/showcases/browser_minimap_true_v8/BrowserMinimapTrueV8ShowcaseMod",
			"BrowserMinimapTrueV8ShowcaseMod.csproj",
			"true-v8")
	};
	private static readonly (string Binding, string Preset, string ModPath, string Project)[] LargeWorldMinimapShowcases =
	{
		(
			"browser_minimap_compact_buffer_showcase",
			"browser_minimap_compact_buffer_cef_raylib",
			"mods/showcases/browser_minimap_compact_buffer/BrowserMinimapCompactBufferShowcaseMod",
			"BrowserMinimapCompactBufferShowcaseMod.csproj"),
		(
			"browser_minimap_composited_overlay_showcase",
			"browser_minimap_composited_overlay_cef_raylib",
			"mods/showcases/browser_minimap_composited_overlay/BrowserMinimapCompositedOverlayShowcaseMod",
			"BrowserMinimapCompositedOverlayShowcaseMod.csproj"),
		(
			"browser_minimap_performance_showcase",
			"browser_minimap_performance_cef_raylib",
			"mods/showcases/browser_minimap_performance/BrowserMinimapPerformanceShowcaseMod",
			"BrowserMinimapPerformanceShowcaseMod.csproj")
	};

	[Test]
	public void LauncherBindingAndPreset_RegisterCefComparisonShowcase()
	{
		string repoRoot = FindRepoRoot();
		string launcherConfig = File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json"));
		string launcherPresets = File.ReadAllText(Path.Combine(repoRoot, "launcher.presets.json"));

		Assert.That(launcherConfig, Does.Contain($"\"name\": \"{BindingName}\""));
		Assert.That(launcherConfig, Does.Contain("mods/showcases/browser_minimap_bridge_compare/BrowserMinimapBridgeCompareShowcaseMod"));
		Assert.That(launcherPresets, Does.Contain($"\"id\": \"{PresetId}\""));
		Assert.That(launcherPresets, Does.Contain("\"$browser_cef_runtime\""));
		Assert.That(launcherPresets, Does.Contain($"\"${BindingName}\""));
	}

	[Test]
	public void LauncherBindingsAndPresets_RegisterSinglePathMinimapShowcases()
	{
		string repoRoot = FindRepoRoot();
		string launcherConfig = File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json"));
		string launcherPresets = File.ReadAllText(Path.Combine(repoRoot, "launcher.presets.json"));

		foreach (var showcase in SinglePathShowcases)
		{
			Assert.That(launcherConfig, Does.Contain($"\"name\": \"{showcase.Binding}\""));
			Assert.That(launcherConfig, Does.Contain(showcase.ModPath));
			Assert.That(launcherConfig, Does.Contain(showcase.Project));
			Assert.That(launcherPresets, Does.Contain($"\"id\": \"{showcase.Preset}\""));
			Assert.That(launcherPresets, Does.Contain("\"$browser_cef_runtime\""));
			Assert.That(launcherPresets, Does.Contain($"\"${showcase.Binding}\""));
		}
	}

	[Test]
	public void LauncherBindingsAndPresets_RegisterLargeWorldMinimapShowcases()
	{
		string repoRoot = FindRepoRoot();
		string launcherConfig = File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json"));
		string launcherPresets = File.ReadAllText(Path.Combine(repoRoot, "launcher.presets.json"));

		foreach (var showcase in LargeWorldMinimapShowcases)
		{
			Assert.That(launcherConfig, Does.Contain($"\"name\": \"{showcase.Binding}\""));
			Assert.That(launcherConfig, Does.Contain(showcase.ModPath));
			Assert.That(launcherConfig, Does.Contain(showcase.Project));
			Assert.That(launcherPresets, Does.Contain($"\"id\": \"{showcase.Preset}\""));
			Assert.That(launcherPresets, Does.Contain("\"$browser_cef_runtime\""));
			Assert.That(launcherPresets, Does.Contain("\"$performer_blacksmith_large_world_nohud\""));
			Assert.That(launcherPresets, Does.Contain($"\"${showcase.Binding}\""));
		}
	}

	[Test]
	public void LargeWorldShowcases_OverrideMapMovementMetadataForPlayerVisibleMarkers()
	{
		string repoRoot = FindRepoRoot();
		string[] requiredMetadataKeys =
		{
			"minimapMarkerShowcaseTotal",
			"minimapMarkerScatterPaddingCm",
			"minimapMarkerScatterJitterCm",
			"minimapMarkerVisibleClusterCount",
			"minimapMarkerVisibleClusterCenterXCm",
			"minimapMarkerVisibleClusterCenterYCm",
			"minimapMarkerVisibleClusterRadiusCm",
			"minimapMarkerScatterSeed",
			"minimapMarkerMovementPaddingCm",
			"minimapMarkerMovementSpeedCmPerSecond",
			"minimapMarkerMovementTurnPeriodSeconds"
		};

		foreach (var showcase in LargeWorldMinimapShowcases)
		{
			string modRoot = Path.Combine(repoRoot, showcase.ModPath.Replace('/', Path.DirectorySeparatorChar));
			string mapPath = Path.Combine(modRoot, "Assets", "Maps", $"{LargeWorldMapId}.json");
			Assert.That(File.Exists(mapPath), Is.True, $"{showcase.Binding} must provide a local map fragment for visible marker movement.");

			JsonNode root = JsonNode.Parse(File.ReadAllText(mapPath))!;
			Assert.That(root["Id"]?.GetValue<string>(), Is.EqualTo(LargeWorldMapId));
			JsonObject metadata = root["Metadata"]?["performerBlacksmith"]?.AsObject()
				?? throw new AssertionException($"{showcase.Binding} map fragment must declare Metadata.performerBlacksmith.");

			foreach (string key in requiredMetadataKeys)
			{
				Assert.That(metadata.ContainsKey(key), Is.True, $"{showcase.Binding} metadata must include {key} because map metadata fragments replace the whole performerBlacksmith section.");
			}

			Assert.That(metadata["minimapMarkerShowcaseTotal"]?.GetValue<int>(), Is.EqualTo(30000));
			Assert.That(metadata["minimapMarkerMovementSpeedCmPerSecond"]?.GetValue<float>(), Is.GreaterThanOrEqualTo(80000f));
			Assert.That(metadata["minimapMarkerMovementTurnPeriodSeconds"]?.GetValue<float>(), Is.LessThanOrEqualTo(3.5f));
		}
	}

	[Test]
	public void SinglePathWebApps_RenderOneMinimapAndExposeModeSpecificPipelines()
	{
		string repoRoot = FindRepoRoot();
		foreach (var showcase in SinglePathShowcases)
		{
			string appRoot = Path.Combine(repoRoot, showcase.ModPath.Replace('/', Path.DirectorySeparatorChar), "Assets", "minimap-single-app");
			string html = File.ReadAllText(Path.Combine(appRoot, "index.html"));
			string script = File.ReadAllText(Path.Combine(appRoot, "main.js"));

			Assert.That(html, Does.Contain("minimap-canvas"));
			Assert.That(html, Does.Not.Contain("compact-canvas"));
			Assert.That(html, Does.Not.Contain("browser-canvas"));
			Assert.That(html, Does.Not.Contain("v8-canvas"));
			Assert.That(script, Does.Contain($"'{showcase.Mode}'"));
			Assert.That(script, Does.Contain("processReadCopyDescriptor"));
			Assert.That(script, Does.Contain("processBrowserArrayBufferDescriptor"));
			Assert.That(script, Does.Contain("processTrueV8Descriptor"));
			Assert.That(script, Does.Contain("new ArrayBuffer"));
			Assert.That(script, Does.Contain("acquireV8Buffer"));
			Assert.That(script, Does.Not.Contain("heatmap"));
		}
	}

	[Test]
	public void LargeWorldWebApps_AreSeparateMarkerFocusedShowcases()
	{
		string repoRoot = FindRepoRoot();
		string compactRoot = Path.Combine(
			repoRoot,
			"mods",
			"showcases",
			"browser_minimap_compact_buffer",
			"BrowserMinimapCompactBufferShowcaseMod",
			"Assets",
			"minimap-single-app");
		string performanceRoot = Path.Combine(
			repoRoot,
			"mods",
			"showcases",
			"browser_minimap_performance",
			"BrowserMinimapPerformanceShowcaseMod",
			"Assets",
			"minimap-single-app");
		string overlayRoot = Path.Combine(
			repoRoot,
			"mods",
			"showcases",
			"browser_minimap_composited_overlay",
			"BrowserMinimapCompositedOverlayShowcaseMod",
			"Assets",
			"overlay-app");

		string compactHtml = File.ReadAllText(Path.Combine(compactRoot, "index.html"));
		string compactScript = File.ReadAllText(Path.Combine(compactRoot, "main.js"));
		string performanceHtml = File.ReadAllText(Path.Combine(performanceRoot, "index.html"));
		string performanceScript = File.ReadAllText(Path.Combine(performanceRoot, "main.js"));
		string overlayHtml = File.ReadAllText(Path.Combine(overlayRoot, "index.html"));
		string overlayScript = File.ReadAllText(Path.Combine(overlayRoot, "main.js"));
		string overlayCss = File.ReadAllText(Path.Combine(overlayRoot, "styles.css"));
		string minimapRuntime = File.ReadAllText(Path.Combine(
			repoRoot,
			"src",
			"Core",
			"Presentation",
			"Minimap",
			"MinimapRuntime.cs"));
		string minimapScreenMarkerBuffer = File.ReadAllText(Path.Combine(
			repoRoot,
			"src",
			"Core",
			"Presentation",
			"Minimap",
			"MinimapScreenMarkerBuffer.cs"));
		string skiaOverlayRenderer = File.ReadAllText(Path.Combine(
			repoRoot,
			"src",
			"Libraries",
			"Ludots.Presentation.Skia",
			"SkiaOverlayRenderer.cs"));
		string overlayPerformerConfig = File.ReadAllText(Path.Combine(
			repoRoot,
			"mods",
			"showcases",
			"browser_minimap_composited_overlay",
			"BrowserMinimapCompositedOverlayShowcaseMod",
			"Assets",
			"Presentation",
			"performers.json"));
		string overlayEntry = File.ReadAllText(Path.Combine(
			repoRoot,
			"mods",
			"showcases",
			"browser_minimap_composited_overlay",
			"BrowserMinimapCompositedOverlayShowcaseMod",
			"BrowserMinimapCompositedOverlayShowcaseModEntry.cs"));
		string overlayBrowserCanvasContent = File.ReadAllText(Path.Combine(
			repoRoot,
			"mods",
			"showcases",
			"browser_minimap_composited_overlay",
			"BrowserMinimapCompositedOverlayShowcaseMod",
			"BrowserMinimapCompositedOverlayBrowserCanvasContent.cs"));
		string overlayBridge = File.ReadAllText(Path.Combine(
			repoRoot,
			"mods",
			"showcases",
			"browser_minimap_composited_overlay",
			"BrowserMinimapCompositedOverlayShowcaseMod",
			"BrowserMinimapCompositedOverlayNativeMarkerBridgeSystem.cs"));

		Assert.That(compactHtml, Does.Contain("minimap-canvas"));
		Assert.That(compactHtml, Does.Contain("Compact Buffer"));
		Assert.That(compactScript, Does.Contain("decodeWdmm"));
		Assert.That(compactScript, Does.Contain("fitBounds"));
		Assert.That(compactScript, Does.Contain("ctx.fillRect"));
		Assert.That(compactScript, Does.Not.Contain("heatmap"));
		Assert.That(compactScript, Does.Not.Contain("acquireV8Buffer"));

		Assert.That(performanceHtml, Does.Contain("minimap-canvas"));
		Assert.That(performanceHtml, Does.Contain("Raw Performance"));
		Assert.That(performanceScript, Does.Contain("decodeWrmm"));
		Assert.That(performanceScript, Does.Contain("const WRMM_MAGIC"));
		Assert.That(performanceScript, Does.Contain("const RAW_SCHEMA_ID = 1002"));
		Assert.That(performanceScript, Does.Contain("const BYTES_PER_MARKER = 36"));
		Assert.That(performanceScript, Does.Contain("fitBounds"));
		Assert.That(performanceScript, Does.Contain("ctx.fillRect"));
		Assert.That(performanceScript, Does.Not.Contain("heatmap"));
		Assert.That(performanceScript, Does.Not.Contain("acquireV8Buffer"));

		Assert.That(overlayHtml, Does.Contain("Minimap"));
		Assert.That(overlayHtml, Does.Not.Contain("web-back-panel"));
		Assert.That(overlayCss, Does.Contain("background: transparent"));
		Assert.That(overlayCss, Does.Contain("pointer-events: none"));
		Assert.That(overlayCss, Does.Not.Contain("mask-image"));
		Assert.That(overlayCss, Does.Not.Contain("-webkit-mask-image"));
		Assert.That(overlayCss, Does.Contain("clip-path: circle"));
		Assert.That(overlayScript, Does.Contain("__LUDOTS_MINIMAP_COMPOSITED_OVERLAY_READY__"));
		Assert.That(overlayHtml, Does.Contain("minimap-widget"));
		Assert.That(overlayHtml, Does.Contain("minimap-viewport"));
		Assert.That(overlayHtml, Does.Not.Contain("minimap-drag-handle"));
		Assert.That(overlayHtml, Does.Not.Contain("status"));
		Assert.That(overlayCss, Does.Contain(".minimap-widget"));
		Assert.That(overlayCss, Does.Contain("pointer-events: auto"));
		Assert.That(overlayScript, Does.Contain("ludots.minimapOverlay.rect"));
		Assert.That(overlayScript, Does.Contain("NATIVE_CLIP_KIND = 'circle'"));
		Assert.That(overlayScript, Does.Contain("postViewportRectImmediately"));
		Assert.That(overlayScript, Does.Contain("cancelAnimationFrame"));
		Assert.That(overlayScript, Does.Contain("dragDelta"));
		Assert.That(overlayScript, Does.Not.Contain("syncWebPanelHole"));
		Assert.That(overlayScript, Does.Contain("getBoundingClientRect"));
		Assert.That(overlayScript, Does.Contain("setPointerCapture"));
		Assert.That(overlayScript, Does.Not.Contain("readSharedBuffer"));
		Assert.That(overlayScript, Does.Not.Contain("heatmap"));
		Assert.That(overlayScript, Does.Not.Contain("refs.status"));
		Assert.That(overlayPerformerConfig, Does.Contain("\"sizePx\": 2"));
		Assert.That(overlayPerformerConfig, Does.Contain("\"orientationMode\": \"None\""));
		Assert.That(overlayPerformerConfig, Does.Not.Contain("\"orientationMode\": \"PerformerForward\""));
		Assert.That(overlayEntry, Does.Contain("InsertPresentationSystemBefore<MinimapPresentationSystem>"));
		Assert.That(overlayEntry, Does.Contain("MessageReceived += OnBrowserMessageReceived"));
		Assert.That(overlayEntry, Does.Contain("ludots.minimapOverlay.rect"));
		Assert.That(overlayEntry, Does.Contain("TryGetClipKind"));
		Assert.That(overlayEntry, Does.Contain("PresentationClipShapeKind.Circle"));
		Assert.That(overlayEntry, Does.Contain("BrowserCanvasSize = 288"));
		Assert.That(overlayEntry, Does.Contain("BrowserSurfaceHitTestOptions.Bounds"));
		Assert.That(overlayEntry, Does.Contain("dragDelta"));
		Assert.That(overlayEntry, Does.Contain(".WidthPercent(100f)"));
		Assert.That(overlayEntry, Does.Contain(".HeightPercent(100f)"));
		Assert.That(overlayEntry, Does.Not.Contain("UiSurfaceContribution.FromBuilder(() => BuildBrowserRoot(browserContent))"));
		Assert.That(overlayBrowserCanvasContent, Does.Contain("override UiRect GetContentRect"));
		Assert.That(overlayBrowserCanvasContent, Does.Contain("_layoutState.GetCanvasRect()"));
		Assert.That(overlayBridge, Does.Contain("KnowledgeProjectionStore"));
		Assert.That(overlayBridge, Does.Contain("KnowledgePresence.LiveVisible"));
		Assert.That(overlayBridge, Does.Contain("NativeChromeVisible = false"));
		Assert.That(overlayBridge, Does.Contain("SetExternalFieldRect"));
		Assert.That(overlayBridge, Does.Contain("SetFieldClipShape"));
		Assert.That(overlayBridge, Does.Contain("ClearFieldClipShape"));
		Assert.That(overlayBridge, Does.Not.Contain("ScreenOverlayBuffer"));
		Assert.That(minimapRuntime, Does.Contain("NativeChromeVisible"));
		Assert.That(minimapRuntime, Does.Contain("SetExternalFieldRect"));
		Assert.That(minimapRuntime, Does.Contain("SetFieldClipShape"));
		Assert.That(minimapRuntime, Does.Contain("ResolveFieldClipShape"));
		Assert.That(minimapRuntime, Does.Contain("PresentationClipShapeKind.Circle"));
		Assert.That(minimapScreenMarkerBuffer, Does.Contain("ClipShape"));
		Assert.That(skiaOverlayRenderer, Does.Contain("TrySaveClipShape"));
		Assert.That(skiaOverlayRenderer, Does.Contain("PresentationClipShapeKind.Circle"));
		Assert.That(skiaOverlayRenderer, Does.Contain("PresentationClipShapeKind.Diamond"));
	}

	[Test]
	public void WebApp_RendersMarkersOnlyAndDefinesTrueV8Gate()
	{
		string repoRoot = FindRepoRoot();
		string appRoot = Path.Combine(
			repoRoot,
			"mods",
			"showcases",
			"browser_minimap_bridge_compare",
			"BrowserMinimapBridgeCompareShowcaseMod",
			"Assets",
			"minimap-bridge-compare-app");
		string html = File.ReadAllText(Path.Combine(appRoot, "index.html"));
		string script = File.ReadAllText(Path.Combine(appRoot, "main.js"));

		Assert.That(html, Does.Contain("Read-Copy Buffer"));
		Assert.That(html, Does.Contain("compact-canvas"));
		Assert.That(html, Does.Contain("Browser ArrayBuffer"));
		Assert.That(html, Does.Contain("browser-owned ArrayBuffer"));
		Assert.That(html, Does.Contain("browser-canvas"));
		Assert.That(html, Does.Contain("True V8 Buffer"));
		Assert.That(html, Does.Contain("V8 backing-store ArrayBuffer"));
		Assert.That(html, Does.Contain("True V8 lane only accepts ArrayBuffer"));
		Assert.That(html, Does.Contain("No heatmap"));
		Assert.That(html, Does.Contain("v8-message"));
		Assert.That(html, Does.Not.Contain("json-canvas"));
		Assert.That(html, Does.Not.Contain("markersJson"));
		Assert.That(script, Does.Contain("ctx.fillRect"));
		Assert.That(script, Does.Contain("Math.min(1.35"));
		Assert.That(script, Does.Contain("latest queued"));
		Assert.That(script, Does.Contain("stale skipped"));
		Assert.That(script, Does.Contain("V8 backing store"));
		Assert.That(script, Does.Contain("isV8Active"));
		Assert.That(script, Does.Contain("refs.v8Message.textContent"));
		Assert.That(script, Does.Not.Contain("heatmap"));
		Assert.That(script, Does.Contain("const MINIMAP_SCHEMA_ID = 2"));
		Assert.That(script, Does.Contain("browser-canvas"));
		Assert.That(script, Does.Contain("new ArrayBuffer"));
		Assert.That(script, Does.Contain("acquireV8Buffer"));
		Assert.That(script, Does.Contain("value instanceof ArrayBuffer"));
		Assert.That(script, Does.Not.Contain("JSON_TOPIC"));
		Assert.That(script, Does.Not.Contain("markersJson"));
		Assert.That(script, Does.Not.Contain("json-canvas"));
	}

	[Test]
	public void CompactProducer_EmitsWdmmUsingRegisteredMinimapSchema()
	{
		Type worldType = typeof(BrowserMinimapBridgeCompareShowcaseModEntry).Assembly
			.GetType("BrowserMinimapBridgeCompareShowcaseMod.BrowserMinimapBridgeCompareMarkerWorld", throwOnError: true)!;
		object world = Activator.CreateInstance(
			worldType,
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
			binder: null,
			args: new object[] { 4 },
			culture: null)!;
		MethodInfo createCompactPacket = worldType.GetMethod(
			"CreateCompactPacket",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
		var packet = (WebUiOutboundPacket)createCompactPacket.Invoke(world, new object[] { "session-a", 0L })!;

		Assert.That(packet.Topic, Is.EqualTo("webui.minimapMarkers"));
		Assert.That(packet.ContentType, Is.EqualTo(WebUiDataPlaneProtocol.BinaryContentType));
		ReadOnlySpan<byte> bytes = packet.Payload.Span;
		Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes), Is.EqualTo(0x4d4d4457));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(4)), Is.EqualTo(WebUiColumnarPacketSchemaRegistry.MinimapMarkersSchemaId));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8)), Is.EqualTo(4));
		Assert.That(packet.Payload.Length, Is.EqualTo(20 + (4 * 24)));
	}

	[Test]
	public void CompactProducer_DistributesMarkersAcrossTwoDimensionalMinimap()
	{
		Type worldType = typeof(BrowserMinimapBridgeCompareShowcaseModEntry).Assembly
			.GetType("BrowserMinimapBridgeCompareShowcaseMod.BrowserMinimapBridgeCompareMarkerWorld", throwOnError: true)!;
		object world = Activator.CreateInstance(
			worldType,
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
			binder: null,
			args: new object[] { 5_000 },
			culture: null)!;
		MethodInfo createCompactPacket = worldType.GetMethod(
			"CreateCompactPacket",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
		var packet = (WebUiOutboundPacket)createCompactPacket.Invoke(world, new object[] { "session-a", 0L })!;

		ReadOnlySpan<byte> bytes = packet.Payload.Span;
		int count = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8));
		float minX = float.MaxValue;
		float maxX = float.MinValue;
		float minY = float.MaxValue;
		float maxY = float.MinValue;
		int offset = 20;
		for (int i = 0; i < count; i++)
		{
			float x = BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(offset + 4));
			float y = BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(offset + 8));
			minX = MathF.Min(minX, x);
			maxX = MathF.Max(maxX, x);
			minY = MathF.Min(minY, y);
			maxY = MathF.Max(maxY, y);
			offset += 24;
		}

		Assert.That(maxX - minX, Is.GreaterThan(25_000f));
		Assert.That(maxY - minY, Is.GreaterThan(25_000f));
	}

	[Test]
	public void RawPerformanceProducer_EmitsWrmmThirtySixByteRows()
	{
		var markers = new MinimapMarkerBuffer(capacity: 4);
		markers.BeginFrame();
		Assert.That(markers.TryAdd(42, 1200f, -2400f, new Vector4(1f, 0.25f, 0.125f, 0.75f), 9f, flags: 7u), Is.True);
		Assert.That(markers.TryAdd(43, -3200f, 6400f, new Vector4(0.2f, 0.4f, 0.8f, 1f), 5f, flags: 0u), Is.True);

		Type producerType = typeof(BrowserMinimapPerformanceShowcaseModEntry).Assembly
			.GetType("BrowserMinimapPerformanceShowcaseMod.BrowserMinimapPerformanceRawMarkerTopicProducer", throwOnError: true)!;
		var producer = (IWebUiTopicProducer)Activator.CreateInstance(
			producerType,
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
			binder: null,
			args: new object[] { "topic.raw", markers },
			culture: null)!;
		var context = new WebUiTopicContext("session-a", producer.Topic, 17, JsonSerializer.SerializeToElement(new { }));

		Assert.That(producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
		Assert.That(packet.Topic, Is.EqualTo("topic.raw"));
		Assert.That(packet.ContentType, Is.EqualTo(WebUiDataPlaneProtocol.BinaryContentType));
		Assert.That(packet.Payload.Length, Is.EqualTo(20 + (2 * 36)));

		ReadOnlySpan<byte> bytes = packet.Payload.Span;
		Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes), Is.EqualTo(0x4d4d5257));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(4)), Is.EqualTo(1002));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8)), Is.EqualTo(2));
		Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(20)), Is.EqualTo(42));
		Assert.That(BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(24)), Is.EqualTo(1200f));
		Assert.That(BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(28)), Is.EqualTo(-2400f));
		Assert.That(BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(32)), Is.EqualTo(1f));
		Assert.That(BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(36)), Is.EqualTo(0.25f));
		Assert.That(BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(40)), Is.EqualTo(0.125f));
		Assert.That(BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(44)), Is.EqualTo(0.75f));
		Assert.That(BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(48)), Is.EqualTo(9f));
		Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(52)), Is.EqualTo(7u));
	}

	[Test]
	public void Readme_DefinesTrueV8BufferGateAndDoesNotClaimCurrentCefSharpPathSatisfiesIt()
	{
		string repoRoot = FindRepoRoot();
		string readme = File.ReadAllText(Path.Combine(
			repoRoot,
			"mods",
			"showcases",
			"browser_minimap_bridge_compare",
			"BrowserMinimapBridgeCompareShowcaseMod",
			"README.md"));

		Assert.That(readme, Does.Contain("Ludots-owned browser subprocess and native CEF/V8 provider"));
		Assert.That(readme, Does.Contain("CefV8BackingStore::Create(...)"));
		Assert.That(readme, Does.Contain("CefV8Value::CreateArrayBufferFromBackingStore(...)"));
		Assert.That(readme, Does.Contain("window.__ludotsCefV8.acquireV8Buffer"));
		Assert.That(readme, Does.Contain("Read-Copy Buffer"));
		Assert.That(readme, Does.Contain("current baseline, not shared memory"));
		Assert.That(readme, Does.Contain("Browser-owned ArrayBuffer"));
		Assert.That(readme, Does.Contain("new ArrayBuffer(...)"));
		Assert.That(readme, Does.Contain("lower bound"));
		Assert.That(readme, Does.Contain("not native"));
		Assert.That(readme, Does.Contain("True V8 Buffer"));
		Assert.That(readme, Does.Contain("native CEF render-process provider maps the descriptor payload"));
		Assert.That(readme, Does.Contain("fills a CEF V8 backing store"));
		Assert.That(readme, Does.Contain("Managed `byte[]`, array-like objects, `Uint8Array.from(...)` snapshots, and browser-created buffers are rejected"));
		Assert.That(readme, Does.Contain("V8 sandbox is enabled"));
		Assert.That(readme, Does.Contain("cannot expose an external memory-mapped pointer directly"));
		Assert.That(readme, Does.Contain("real native V8 ArrayBuffer path"));
		Assert.That(readme, Does.Contain("must not call `CreateArrayBufferWithCopy(...)`"));
		Assert.That(readme, Does.Not.Contain("LUDOTS_CEF_DISABLE_V8_SANDBOX_FOR_EXTERNAL_ARRAYBUFFER"));
		Assert.That(readme, Does.Not.Contain("JSON baseline"));
		Assert.That(readme, Does.Not.Contain("markersJson"));
		Assert.That(readme, Does.Not.Contain("zero-copy"));
	}

	private static string FindRepoRoot()
	{
		string? current = TestContext.CurrentContext.TestDirectory;
		while (!string.IsNullOrWhiteSpace(current))
		{
			if (File.Exists(Path.Combine(current, "launcher.config.json")) &&
				Directory.Exists(Path.Combine(current, "mods")))
			{
				return current;
			}

			current = Directory.GetParent(current)?.FullName;
		}

		throw new DirectoryNotFoundException("Could not find repository root.");
	}
}
