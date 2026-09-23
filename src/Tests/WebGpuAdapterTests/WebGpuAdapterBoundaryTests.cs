using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Arch.Core;
using Ludots.Adapter.WebGpu;
using Ludots.Client.WebGpu.Input;
using Ludots.Client.WebGpu.Rendering;
using Ludots.Client.WebGpu.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Hosting;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.Launcher.Backend;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;
using Silk.NET.WebGPU;

namespace Ludots.Tests.WebGpuAdapter;

[TestFixture]
public class WebGpuAdapterBoundaryTests
{
    [Test]
    public void Launcher_ResolvesWebGpuAdapter_ToBrowserWebGpuClient()
    {
        string repoRoot = FindRepoRoot();
        string tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-webgpu-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string preferencesPath = Path.Combine(tempDirectory, "preferences.json");
            string userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
            File.WriteAllText(preferencesPath, "{}");
            File.WriteAllText(userConfigPath, "{}");

            var launcher = new LauncherService(
                repoRoot,
                Path.Combine(repoRoot, "launcher.config.json"),
                Path.Combine(repoRoot, "launcher.presets.json"),
                preferencesPath,
                userConfigPath);

            LauncherResolveResult resolve = launcher.Resolve(
                new[] { "mod:LudotsCoreMod" },
                LauncherPlatformIds.WebGpu,
                LauncherBuildMode.Never);

            Assert.That(resolve.Plan.AdapterId, Is.EqualTo(LauncherPlatformIds.WebGpu));
            Assert.That(resolve.Plan.Adapter.Id, Is.EqualTo(LauncherPlatformIds.WebGpu));
            Assert.That(resolve.Plan.Adapter.HostKind, Is.EqualTo("web"));
            Assert.That(resolve.Plan.Adapter.BuildPipeline, Is.EqualTo("dotnet+npm"));
            Assert.That(resolve.Plan.Adapter.AppProjectPath.Replace('\\', '/'), Does.Contain("Apps/Web/Ludots.App.Web"));
            Assert.That(resolve.Plan.AppAssemblyPath.Replace('\\', '/'), Does.Contain("Ludots.App.Web.dll"));
            Assert.That(resolve.Plan.Adapter.ClientProjectDirectory.Replace('\\', '/'), Does.Contain("Client/WebGpu"));
            Assert.That(resolve.Plan.Adapter.ClientDistributionDirectory.Replace('\\', '/'), Does.Contain("Client/WebGpu/dist"));
            Assert.That(resolve.Plan.LaunchUrl, Is.EqualTo("http://localhost:5201"));
            Assert.That(resolve.Plan.OrderedModIds, Is.Not.Empty);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Test]
    public void Launcher_RejectsUnknownAdapter_WithoutFallback()
    {
        string repoRoot = FindRepoRoot();
        string tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-unknown-adapter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string preferencesPath = Path.Combine(tempDirectory, "preferences.json");
            string userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
            File.WriteAllText(preferencesPath, "{}");
            File.WriteAllText(userConfigPath, "{}");

            var launcher = new LauncherService(
                repoRoot,
                Path.Combine(repoRoot, "launcher.config.json"),
                Path.Combine(repoRoot, "launcher.presets.json"),
                preferencesPath,
                userConfigPath);

            var ex = Assert.Throws<InvalidOperationException>(
                () => launcher.Resolve(new[] { "mod:LudotsCoreMod" }, "not-a-real-adapter", LauncherBuildMode.Never));

            Assert.That(ex!.Message, Does.Contain("Unknown adapter"));
            Assert.That(ex.Message, Does.Not.Contain("falling back"));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Test]
    public void BrowserWebGpuClient_RequiresNavigatorGpu_WithoutWebGlRenderer()
    {
        string repoRoot = FindRepoRoot();
        string clientRoot = Path.Combine(repoRoot, "src", "Client", "WebGpu");
        Assert.That(Directory.Exists(clientRoot), Is.True, $"Missing WebGPU browser client: {clientRoot}");

        string source = string.Join(
            Environment.NewLine,
            new[] { Path.Combine(clientRoot, "index.html") }
                .Concat(Directory.EnumerateFiles(Path.Combine(clientRoot, "src"), "*.ts", SearchOption.AllDirectories))
                .Where(path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText));

        Assert.That(source, Does.Contain("navigator.gpu"));
        Assert.That(source, Does.Not.Contain("WebGL"));
        Assert.That(source, Does.Not.Contain("WebGLRenderer"));
    }

    [Test]
    public void BrowserWebGpuClient_MassNavigationPresentation_RemainsStableAcrossNetworkFrames()
    {
        string repoRoot = FindRepoRoot();
        string frameStream = File.ReadAllText(Path.Combine(
            repoRoot, "src", "Client", "WebGpu", "src", "core", "WebGpuFrameStream.ts"));
        string hudRenderer = File.ReadAllText(Path.Combine(
            repoRoot, "src", "Client", "WebGpu", "src", "rendering", "WebGpuHudRenderer.ts"));
        string minimapRenderer = File.ReadAllText(Path.Combine(
            repoRoot, "src", "Client", "Web", "src", "rendering", "MinimapMarkerRenderer.ts"));
        string presentationExtractor = File.ReadAllText(Path.Combine(
            repoRoot, "src", "Adapters", "Web", "Ludots.Adapter.Web", "Streaming", "PresentationExtractor.cs"));

        Assert.That(frameStream, Does.Contain("const PRIMITIVE_WIRE_BYTES = 48"));
        Assert.That(frameStream, Does.Contain("identityMatches"));
        Assert.That(frameStream, Does.Contain("_previousStableIds[index] === stableId"));
        Assert.That(hudRenderer, Does.Contain("previousOrigin"));
        Assert.That(hudRenderer, Does.Contain("view.getInt32(offset + 9, true)"));
        Assert.That(hudRenderer, Does.Contain("setInterpolationFactor"));

        int orientationLayer = minimapRenderer.IndexOf("this.drawOrientationLayer(context, count)", StringComparison.Ordinal);
        int markerLayer = minimapRenderer.IndexOf("this.drawMarkerLayer(context, count)", StringComparison.Ordinal);
        Assert.That(orientationLayer, Is.GreaterThanOrEqualTo(0));
        Assert.That(markerLayer, Is.GreaterThan(orientationLayer));
        Assert.That(presentationExtractor, Does.Not.Contain("MassNavigationPresentationProxyCollector"));
    }

    [Test]
    public void WebGpuHostComposer_RegistersRequiredUiServices_AndValidatesMissingInput()
    {
        string repoRoot = FindRepoRoot();
        string tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"webgpu-composer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        string testModId = "LudotsCoreMod";
        string modRoot = CreateAssetOnlyCoreMod(repoRoot, tempDirectory);
        string graphPath = Path.Combine(tempDirectory, "webgpu.launch.graph.json");
        string bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

        try
        {
            File.WriteAllText(
                graphPath,
                $$"""
                {
                  "schemaVersion": 1,
                  "generatedAtUtc": "2026-04-01T00:00:00.0000000Z",
                  "planFingerprint": "webgpu-composer-fingerprint",
                  "adapter": {
                    "id": "webgpu",
                    "name": "WebGPU",
                    "hostKind": "desktop",
                    "buildPipeline": "dotnet",
                    "runtimeBootstrapSchema": "launcher.runtime.v1",
                    "appProjectPath": "src/Apps/WebGpu/Ludots.App.WebGpu/Ludots.App.WebGpu.csproj",
                    "outputDirectory": "src/Apps/WebGpu/Ludots.App.WebGpu/bin/Release/net8.0",
                    "clientProjectDirectory": "",
                    "clientDistributionDirectory": "",
                    "launchUrl": "",
                    "runtimeBootstrapFileName": "launcher.runtime.json"
                  },
                  "buildMode": "never",
                  "selectors": [ "mod:{{testModId}}" ],
                  "rootModIds": [ "{{testModId}}" ],
                  "orderedModIds": [ "{{testModId}}" ],
                  "plannedMods": [
                    {
                      "id": "{{testModId}}",
                      "rootPath": "{{modRoot.Replace("\\", "\\\\")}}",
                      "projectPath": "",
                      "mainAssemblyPath": "",
                      "kind": 0,
                      "buildState": 4,
                      "bindingNames": []
                    }
                  ],
                  "runtimeArtifacts": {
                    "bootstrapArtifactStrategy": "file",
                    "bootstrapArtifactPath": "{{bootstrapPath.Replace("\\", "\\\\")}}",
                    "graphArtifactPath": "{{graphPath.Replace("\\", "\\\\")}}",
                    "appOutputDirectory": "src/Apps/WebGpu/Ludots.App.WebGpu/bin/Release/net8.0",
                    "appAssemblyPath": "src/Apps/WebGpu/Ludots.App.WebGpu/bin/Release/net8.0/Ludots.App.WebGpu.dll",
                    "launchUrl": ""
                  },
                  "diagnostics": {
                    "settings": [],
                    "warnings": []
                  }
                }
                """);

            File.WriteAllText(
                bootstrapPath,
                $$"""
                {
                  "LaunchGraphPath": "webgpu.launch.graph.json",
                  "PlanSelectors": [ "mod:{{testModId}}" ],
                  "PlanRootModIds": [ "{{testModId}}" ],
                  "PlanOrderedModIds": [ "{{testModId}}" ],
                  "PlanFingerprint": "webgpu-composer-fingerprint",
                  "PlanSchemaVersion": 1
                }
                """);

            WebGpuHostSetup setup = WebGpuHostComposer.Compose(tempDirectory, "launcher.runtime.json");
            try
            {
                Assert.That(setup.Engine.TryGetService(Ludots.Core.Scripting.CoreServiceKeys.UIRoot, out _), Is.True);
                Assert.That(setup.Engine.TryGetService(Ludots.Core.Scripting.CoreServiceKeys.UiSurfaceHost, out _), Is.True);
                Assert.That(setup.Engine.TryGetService(Ludots.Core.Scripting.CoreServiceKeys.UISystem, out _), Is.True);

                var missingInput = Assert.Throws<InvalidOperationException>(
                    () => WebGpuHostComposer.ValidateRequiredContextBeforeLoop(setup.Engine));
                Assert.That(missingInput!.Message, Does.Contain("InputHandler").Or.Contain("InputBackend").Or.Contain("ViewController"));
            }
            finally
            {
                setup.Engine.Dispose();
            }
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Test]
    public void WebGpuNativeRuntimeGuard_FailsFast_WhenLibraryMissing()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"webgpu-missing-native-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => WebGpuNativeRuntimeGuard.EnsureNativeLibraryLoadable(tempDirectory));

            Assert.That(ex!.Message, Does.Contain("WebGPU native runtime is missing"));
            Assert.That(ex.Message, Does.Contain(WebGpuNativeRuntimeGuard.ResolveExpectedLibraryFileName()));
            Assert.That(ex.Message, Does.Contain(WebGpuNativeRuntimeGuard.ResolveExpectedRuntimeNativeDirectoryName()));
            Assert.That(ex.Message, Does.Contain("No Raylib/WebGL/OpenGL/Vulkan/Direct3D fallback"));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Test]
    public void WebGpuNativeRuntimeGuard_ResolvesCurrentRidRuntime_WhenMultipleRidsExist()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"webgpu-native-rid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string libraryFileName = WebGpuNativeRuntimeGuard.ResolveExpectedLibraryFileName();
            string currentRid = WebGpuNativeRuntimeGuard.ResolveExpectedRuntimeNativeDirectoryName();
            string wrongRid = currentRid.EndsWith("-x64", StringComparison.Ordinal)
                ? currentRid[..^4] + "-arm64"
                : currentRid.EndsWith("-arm64", StringComparison.Ordinal)
                    ? currentRid[..^6] + "-x64"
                    : currentRid.EndsWith("-x86", StringComparison.Ordinal)
                        ? currentRid[..^4] + "-x64"
                        : currentRid + "-alt";

            string wrongPath = Path.Combine(tempDirectory, "runtimes", wrongRid, "native", libraryFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(wrongPath)!);
            File.WriteAllText(wrongPath, "wrong architecture placeholder");

            string currentPath = Path.Combine(tempDirectory, "runtimes", currentRid, "native", libraryFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(currentPath)!);
            File.WriteAllText(currentPath, "current architecture placeholder");

            Assert.That(
                WebGpuNativeRuntimeGuard.ResolveNativeLibraryPath(tempDirectory),
                Is.EqualTo(currentPath));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Test]
    public unsafe void WebGpuDeviceBootstrap_FailsFast_WhenAdapterOrDeviceMissing()
    {
        var adapterEx = Assert.Throws<InvalidOperationException>(
            () => WebGpuDeviceBootstrap.EnsureAdapter(null, "adapter unavailable"));
        Assert.That(adapterEx!.Message, Does.Contain("WebGPU adapter request failed"));
        Assert.That(adapterEx.Message, Does.Contain("adapter unavailable"));

        var deviceEx = Assert.Throws<InvalidOperationException>(
            () => WebGpuDeviceBootstrap.EnsureDevice(null, "device unavailable"));
        Assert.That(deviceEx!.Message, Does.Contain("WebGPU device request failed"));
        Assert.That(deviceEx.Message, Does.Contain("device unavailable"));

        var surfaceEx = Assert.Throws<InvalidOperationException>(
            () => WebGpuDeviceBootstrap.EnsureSurface(null));
        Assert.That(surfaceEx!.Message, Does.Contain("WebGPU surface creation failed"));
    }

    [Test]
    public void WebGpuHostLoop_ShouldCaptureWorldPointer_MatchesRaylibSemantics()
    {
        Assert.That(WebGpuHostLoop.ShouldCaptureWorldPointer(false, false, false), Is.False);
        Assert.That(WebGpuHostLoop.ShouldCaptureWorldPointer(true, false, false), Is.True);
        Assert.That(WebGpuHostLoop.ShouldCaptureWorldPointer(false, true, false), Is.True);
        Assert.That(WebGpuHostLoop.ShouldCaptureWorldPointer(false, false, true), Is.True);
    }

    [Test]
    public unsafe void WebGpuInstanceDraw_PacksStorageBufferLayout_ForWgsl()
    {
        var draw = new WebGpuInstanceDraw
        {
            Position = new Vector3(1f, 2f, 3f),
            Scale = new Vector3(4f, 5f, 6f),
            Color = new Vector4(0.1f, 0.2f, 0.3f, 0.4f)
        };

        Assert.That(sizeof(WebGpuInstanceDraw), Is.EqualTo(48));
        Assert.That(Marshal.OffsetOf<WebGpuInstanceDraw>(nameof(WebGpuInstanceDraw.PositionScaleX)).ToInt32(), Is.EqualTo(0));
        Assert.That(Marshal.OffsetOf<WebGpuInstanceDraw>(nameof(WebGpuInstanceDraw.ScaleYzColorRg)).ToInt32(), Is.EqualTo(16));
        Assert.That(Marshal.OffsetOf<WebGpuInstanceDraw>(nameof(WebGpuInstanceDraw.ColorBaUnused)).ToInt32(), Is.EqualTo(32));
        Assert.That(draw.Position, Is.EqualTo(new Vector3(1f, 2f, 3f)));
        Assert.That(draw.Scale, Is.EqualTo(new Vector3(4f, 5f, 6f)));
        Assert.That(draw.Color, Is.EqualTo(new Vector4(0.1f, 0.2f, 0.3f, 0.4f)));
    }

    [Test]
    public void WebGpuHostLoop_BuildViewProjection_MatchesCoreProjectionMatrix()
    {
        var camera = new CameraRenderState3D(
            new Vector3(8f, 7f, 9f),
            new Vector3(1f, 0.5f, -2f),
            Vector3.UnitY,
            55f);
        const float aspect = 16f / 9f;

        Matrix4x4 actual = InvokeBuildViewProjection(camera, aspect);
        CameraClipPlanes clipPlanes = CameraViewportUtil.ResolveClipPlanes(in camera);
        Matrix4x4 expected =
            Matrix4x4.CreateLookAt(camera.Position, camera.Target, camera.Up) *
            Matrix4x4.CreatePerspectiveFieldOfView(
                camera.FovYDeg * (MathF.PI / 180f),
                aspect,
                clipPlanes.NearMeters,
                clipPlanes.FarMeters);

        AssertMatrixNearlyEqual(actual, expected);
    }

    [Test]
    public void WebGpuMassNavigationProxyCollector_DrawsBoundAgent_WhenPrimitiveBufferIsEmpty()
    {
        var world = World.Create();
        world.Create(
            new MassNavigationAgent { ProfileId = 7 },
            new MassNavigationAgentIndex { Value = 3 },
            new MassNavigationAgentProfile { ProfileId = 7, Heavy = true, VisualScale = 1.2f },
            WorldPositionCm.FromCm(1200, -3400));
        var destination = new WebGpuInstanceDraw[4];

        WebGpuMassNavigationProxyStats stats = WebGpuMassNavigationProxyCollector.Collect(world, destination, 0);

        Assert.That(stats.AgentCount, Is.EqualTo(1));
        Assert.That(stats.MarkerCount, Is.EqualTo(0));
        Assert.That(stats.Written, Is.EqualTo(1));
        Assert.That(stats.Dropped, Is.EqualTo(0));
        Assert.That(destination[0].Position.X, Is.EqualTo(12f).Within(0.0001f));
        Assert.That(destination[0].Position.Z, Is.EqualTo(-34f).Within(0.0001f));
        Assert.That(destination[0].Position.Y, Is.GreaterThan(0f));
        Assert.That(destination[0].Scale.X, Is.GreaterThan(0.5f));
        Assert.That(destination[0].Color.W, Is.EqualTo(1f));
    }

    [Test]
    public void WebGpuMassNavigationProxyCollector_ReportsDroppedInstances_WhenDestinationIsFull()
    {
        var world = World.Create();
        for (int i = 0; i < 4; i++)
        {
            world.Create(
                new MassNavigationAgent { ProfileId = i + 1 },
                new MassNavigationAgentIndex { Value = i },
                WorldPositionCm.FromCm(i * 100, 0));
        }

        var destination = new WebGpuInstanceDraw[2];
        destination[0] = new WebGpuInstanceDraw
        {
            Position = Vector3.One,
            Scale = Vector3.One,
            Color = Vector4.One
        };

        WebGpuMassNavigationProxyStats stats = WebGpuMassNavigationProxyCollector.Collect(world, destination, 1);

        Assert.That(stats.AgentCount, Is.EqualTo(4));
        Assert.That(stats.Written, Is.EqualTo(1));
        Assert.That(stats.Dropped, Is.EqualTo(3));
        Assert.That(destination[0].Position, Is.EqualTo(Vector3.One));
    }

    [Test]
    public void WebGpuMassNavigationProxyCollector_PrefersVisualTransform_WhenPresentationPositionExists()
    {
        var world = World.Create();
        world.Create(
            new MassNavigationAgent { ProfileId = 2 },
            new MassNavigationAgentIndex { Value = 9 },
            WorldPositionCm.FromCm(1200, 3400),
            new VisualTransform { Position = new Vector3(-5f, 2f, 8f), Rotation = Quaternion.Identity, Scale = Vector3.One });
        var destination = new WebGpuInstanceDraw[2];

        WebGpuMassNavigationProxyStats stats = WebGpuMassNavigationProxyCollector.Collect(world, destination, 0);

        Assert.That(stats.Written, Is.EqualTo(1));
        Assert.That(destination[0].Position.X, Is.EqualTo(-5f));
        Assert.That(destination[0].Position.Y, Is.GreaterThan(2f));
        Assert.That(destination[0].Position.Z, Is.EqualTo(8f));
    }

    [Test]
    public void WebGpuMassNavigationProxyCollector_DrawsHotspotMarkers()
    {
        var world = World.Create();
        world.Create(
            new MassNavigationHotspotMarker(),
            WorldPositionCm.FromCm(500, 600));
        var destination = new WebGpuInstanceDraw[2];

        WebGpuMassNavigationProxyStats stats = WebGpuMassNavigationProxyCollector.Collect(world, destination, 0);

        Assert.That(stats.AgentCount, Is.EqualTo(0));
        Assert.That(stats.MarkerCount, Is.EqualTo(1));
        Assert.That(stats.Written, Is.EqualTo(1));
        Assert.That(stats.Dropped, Is.EqualTo(0));
        Assert.That(destination[0].Position.X, Is.EqualTo(5f).Within(0.0001f));
        Assert.That(destination[0].Position.Z, Is.EqualTo(6f).Within(0.0001f));
        Assert.That(destination[0].Color.X, Is.EqualTo(1f));
    }

    [Test]
    public void WebGpuUiRasterLayer_ReturnsNoContent_WhenOverlayAndRetainedUiAreEmpty()
    {
        using var layer = new WebGpuUiRasterLayer(new SkiaUiRenderer());
        var root = new UIRoot(new SkiaUiRenderer());
        var screenHud = new ScreenHudBatchBuffer(8);
        var screenOverlay = new ScreenOverlayBuffer();
        var builder = new PresentationOverlaySceneBuilder(screenHud, null, null, null, screenOverlay);
        var scene = new PresentationOverlayScene(16);

        builder.Build(scene);

        bool rendered = layer.TryRender(scene, root, 96, 64, out WebGpuUiRasterStats stats);

        Assert.That(rendered, Is.False);
        Assert.That(stats.HasContent, Is.False);
        Assert.That(stats.OverlayItemCount, Is.EqualTo(0));
        Assert.That(stats.MinimapMarkerCount, Is.EqualTo(0));
    }

    [Test]
    public void WebGpuUiRasterLayer_RendersScreenOverlayPixels_ForWebGpuUiComposite()
    {
        using var layer = new WebGpuUiRasterLayer(new SkiaUiRenderer());
        var root = new UIRoot(new SkiaUiRenderer());
        var screenHud = new ScreenHudBatchBuffer(8);
        var screenOverlay = new ScreenOverlayBuffer();
        screenOverlay.AddRect(
            12,
            10,
            44,
            28,
            new Vector4(0f, 0.92f, 0.55f, 1f),
            new Vector4(1f, 1f, 1f, 1f));
        screenOverlay.AddText(16, 18, "READY", 14, new Vector4(1f, 1f, 1f, 1f));
        var builder = new PresentationOverlaySceneBuilder(screenHud, null, null, null, screenOverlay);
        var scene = new PresentationOverlayScene(16);

        builder.Build(scene);

        bool rendered = layer.TryRender(scene, root, 96, 64, out WebGpuUiRasterStats stats);

        Assert.That(rendered, Is.True);
        Assert.That(stats.HasContent, Is.True);
        Assert.That(stats.OverlayItemCount, Is.EqualTo(2));
        Assert.That(stats.TopMostItemCount, Is.EqualTo(2));
        Assert.That(layer.Width, Is.EqualTo(96));
        Assert.That(layer.Height, Is.EqualTo(64));
        Assert.That(CountNonTransparentPixels(layer.UploadBytes, layer.Width, layer.Height, layer.BytesPerRow, 8, 8, 60, 36), Is.GreaterThan(800));
    }

    [Test]
    public void WebGpuUiRasterLayer_RendersMinimapMarkerPixels_FromSharedOverlayScene()
    {
        using var layer = new WebGpuUiRasterLayer(new SkiaUiRenderer());
        var root = new UIRoot(new SkiaUiRenderer());
        var screenHud = new ScreenHudBatchBuffer(8);
        var screenOverlay = new ScreenOverlayBuffer();
        var minimapMarkers = new MinimapScreenMarkerBuffer(8);
        minimapMarkers.BeginFrame();
        minimapMarkers.TryAdd(301, 40f, 32f, new Vector4(1f, 0.2f, 0.05f, 1f), 12f);
        var builder = new PresentationOverlaySceneBuilder(screenHud, null, null, null, screenOverlay, minimapMarkers);
        var scene = new PresentationOverlayScene(16);

        builder.Build(scene);

        bool rendered = layer.TryRender(scene, root, 96, 64, out WebGpuUiRasterStats stats);

        Assert.That(rendered, Is.True);
        Assert.That(stats.MinimapMarkerCount, Is.EqualTo(1));
        Assert.That(stats.TopMostItemCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(CountNonTransparentPixels(layer.UploadBytes, layer.Width, layer.Height, layer.BytesPerRow, 30, 22, 24, 24), Is.GreaterThan(40));
    }

    [Test]
    public void WebGpuInputPathParser_MapsCoreDevicePaths()
    {
        Assert.That(WebGpuInputPathParser.ParseKeyboardKey("<Keyboard>/w"), Is.EqualTo(Silk.NET.Input.Key.W));
        Assert.That(WebGpuInputPathParser.ParseKeyboardKey("<Keyboard>/space"), Is.EqualTo(Silk.NET.Input.Key.Space));
        Assert.That(WebGpuInputPathParser.ParseMouseButton("<Mouse>/leftButton"), Is.EqualTo(Silk.NET.Input.MouseButton.Left));
        Assert.That(WebGpuInputPathParser.ParseMouseButton("<Mouse>/rightButton"), Is.EqualTo(Silk.NET.Input.MouseButton.Right));
    }

    [Test]
    public void WebGpuWindowsInputMap_MapsConfiguredKeys_ToWin32VirtualKeys()
    {
        Assert.That(WebGpuWindowsInputMap.TryMapKeyboardVirtualKey(Silk.NET.Input.Key.W, out int w), Is.True);
        Assert.That(w, Is.EqualTo(0x57));
        Assert.That(WebGpuWindowsInputMap.TryMapKeyboardVirtualKey(Silk.NET.Input.Key.F6, out int f6), Is.True);
        Assert.That(f6, Is.EqualTo(0x75));
        Assert.That(WebGpuWindowsInputMap.TryMapMouseVirtualKey(Silk.NET.Input.MouseButton.Left, out int left), Is.True);
        Assert.That(left, Is.EqualTo(0x01));
        Assert.That(WebGpuWindowsInputMap.TryMapMouseVirtualKey(Silk.NET.Input.MouseButton.Right, out int right), Is.True);
        Assert.That(right, Is.EqualTo(0x02));
    }

    [Test]
    public void WebGpuUiRenderer_AlignsUploadRows_ForQueueWriteTexture()
    {
        Assert.That(WebGpuUiRenderer.AlignBytesPerRow(64), Is.EqualTo(256));
        Assert.That(WebGpuUiRenderer.AlignBytesPerRow(65), Is.EqualTo(512));
        Assert.That(WebGpuUiRenderer.CalculateUploadByteCount(65, 3), Is.EqualTo(1536));
    }

    [Test]
    public void Launcher_CameraAcceptanceHotpathBinding_ResolvesPlayableStartupMap()
    {
        string repoRoot = FindRepoRoot();
        string tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-webgpu-camera-hotpath-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string preferencesPath = Path.Combine(tempDirectory, "preferences.json");
            string userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
            File.WriteAllText(preferencesPath, "{}");
            File.WriteAllText(userConfigPath, "{}");

            var launcher = new LauncherService(
                repoRoot,
                Path.Combine(repoRoot, "launcher.config.json"),
                Path.Combine(repoRoot, "launcher.presets.json"),
                preferencesPath,
                userConfigPath);

            LauncherResolveResult resolve = launcher.Resolve(
                new[] { "$camera_acceptance_hotpath" },
                LauncherPlatformIds.WebGpu,
                LauncherBuildMode.Never);

            Assert.That(resolve.Plan.RootModIds, Is.EqualTo(new[] { "CameraAcceptanceHotpathEntryMod" }));
            Assert.That(resolve.Plan.OrderedModIds, Does.Contain("CameraAcceptanceMod"));
            Assert.That(resolve.Plan.OrderedModIds, Does.Contain("CameraAcceptanceHotpathEntryMod"));
            var startupMap = resolve.Plan.Diagnostics.Settings
                .First(setting => string.Equals(setting.Key, "startupMapId", StringComparison.Ordinal));
            Assert.That(startupMap.EffectiveValue?.GetValue<string>(), Is.EqualTo("camera_acceptance_hotpath"));
            Assert.That(startupMap.EffectiveSource, Does.Contain("CameraAcceptanceHotpathEntryMod"));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Test]
    public void Launcher_CameraAcceptanceBinding_ResolvesRtsStartupMapForWasdSmoke()
    {
        string repoRoot = FindRepoRoot();
        string tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-webgpu-camera-rts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string preferencesPath = Path.Combine(tempDirectory, "preferences.json");
            string userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
            File.WriteAllText(preferencesPath, "{}");
            File.WriteAllText(userConfigPath, "{}");

            var launcher = new LauncherService(
                repoRoot,
                Path.Combine(repoRoot, "launcher.config.json"),
                Path.Combine(repoRoot, "launcher.presets.json"),
                preferencesPath,
                userConfigPath);

            LauncherResolveResult resolve = launcher.Resolve(
                new[] { "$camera_acceptance" },
                LauncherPlatformIds.WebGpu,
                LauncherBuildMode.Never);

            Assert.That(resolve.Plan.RootModIds, Is.EqualTo(new[] { "CameraAcceptanceMod" }));
            var startupMap = resolve.Plan.Diagnostics.Settings
                .First(setting => string.Equals(setting.Key, "startupMapId", StringComparison.Ordinal));
            Assert.That(startupMap.EffectiveValue?.GetValue<string>(), Is.EqualTo("camera_acceptance_rts"));
            Assert.That(startupMap.EffectiveSource, Does.Contain("CameraAcceptanceMod"));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Test]
    public void CapabilityDiagnostics_DeclaresUiCompositeWithoutPretendingCompleteSupport()
    {
        string report = WebGpuCapabilityDiagnostics.BuildStartupReport();
        Assert.That(report, Does.Contain("webgpu").IgnoreCase);
        Assert.That(report, Does.Contain("Not yet"));
        Assert.That(report, Does.Contain("Skia UI raster upload into WebGPU texture composite"));
        Assert.That(report, Does.Contain("Fail-fast"));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
    }

    private static string CreateAssetOnlyCoreMod(string repoRoot, string root)
    {
        const string modId = "LudotsCoreMod";
        string modRoot = Path.Combine(root, modId);
        string assetsRoot = Path.Combine(modRoot, "assets");
        Directory.CreateDirectory(modRoot);
        File.WriteAllText(
            Path.Combine(modRoot, "mod.json"),
            $$"""
            {
              "name": "{{modId}}",
              "version": "1.0.0",
              "description": "Asset-only test mod for WebGPU host composition.",
              "main": "",
              "priority": 0,
              "dependencies": {}
            }
            """);
        CopyDirectory(
            Path.Combine(repoRoot, "mods", "LudotsCoreMod", "assets"),
            assetsRoot);
        return modRoot;
    }

    private static Matrix4x4 InvokeBuildViewProjection(CameraRenderState3D camera, float aspect)
    {
        MethodInfo method = typeof(WebGpuHostLoop).GetMethod(
            "BuildViewProjection",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(WebGpuHostLoop), "BuildViewProjection");

        return (Matrix4x4)method.Invoke(null, new object[] { camera, aspect })!;
    }

    private static int CountNonTransparentPixels(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        int bytesPerRow,
        int x,
        int y,
        int regionWidth,
        int regionHeight)
    {
        int count = 0;
        int minX = Math.Clamp(x, 0, width);
        int minY = Math.Clamp(y, 0, height);
        int maxX = Math.Clamp(x + regionWidth, 0, width);
        int maxY = Math.Clamp(y + regionHeight, 0, height);
        for (int row = minY; row < maxY; row++)
        {
            int rowOffset = row * bytesPerRow;
            for (int col = minX; col < maxX; col++)
            {
                if (pixels[rowOffset + (col * WebGpuUiRenderer.BytesPerPixel) + 3] > 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void AssertMatrixNearlyEqual(Matrix4x4 actual, Matrix4x4 expected)
    {
        Assert.That(actual.M11, Is.EqualTo(expected.M11).Within(0.00001f));
        Assert.That(actual.M12, Is.EqualTo(expected.M12).Within(0.00001f));
        Assert.That(actual.M13, Is.EqualTo(expected.M13).Within(0.00001f));
        Assert.That(actual.M14, Is.EqualTo(expected.M14).Within(0.00001f));
        Assert.That(actual.M21, Is.EqualTo(expected.M21).Within(0.00001f));
        Assert.That(actual.M22, Is.EqualTo(expected.M22).Within(0.00001f));
        Assert.That(actual.M23, Is.EqualTo(expected.M23).Within(0.00001f));
        Assert.That(actual.M24, Is.EqualTo(expected.M24).Within(0.00001f));
        Assert.That(actual.M31, Is.EqualTo(expected.M31).Within(0.00001f));
        Assert.That(actual.M32, Is.EqualTo(expected.M32).Within(0.00001f));
        Assert.That(actual.M33, Is.EqualTo(expected.M33).Within(0.00001f));
        Assert.That(actual.M34, Is.EqualTo(expected.M34).Within(0.00001f));
        Assert.That(actual.M41, Is.EqualTo(expected.M41).Within(0.00001f));
        Assert.That(actual.M42, Is.EqualTo(expected.M42).Within(0.00001f));
        Assert.That(actual.M43, Is.EqualTo(expected.M43).Within(0.00001f));
        Assert.That(actual.M44, Is.EqualTo(expected.M44).Within(0.00001f));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(
                file,
                Path.Combine(destination, Path.GetRelativePath(source, file)),
                overwrite: true);
        }
    }
}
