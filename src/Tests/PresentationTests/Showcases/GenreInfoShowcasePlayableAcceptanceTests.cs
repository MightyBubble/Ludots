using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Arch.Core;
using GenreInfoShowcaseMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Runtime.Events;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
[NonParallelizable]
public sealed class GenreInfoShowcasePlayableAcceptanceTests
{
    private const string MapId = GenreInfoShowcaseIds.MapId;

    private static readonly string[] ShowcaseMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod",
        "FourXDemoMod",
        "EntityCommandPanelMod",
        "RtsDemoMod",
        "MobaDemoMod",
        "EntityInfoPanelsMod",
        "GenreInfoShowcaseMod"
    };

    [Test]
    public void GenreInfoShowcase_PlayableAcceptance_WritesLocalizedScreensAndArtifacts()
    {
        string repoRoot = FindRepoRoot();
        string artifactRoot = Path.Combine(repoRoot, "artifacts", "acceptance", "genre-info-showcase");
        string screenRoot = Path.Combine(artifactRoot, "screens");
        Directory.CreateDirectory(screenRoot);

        var timeline = new List<string>();
        var trace = new List<object>();

        using var engine = CreateEngine(ShowcaseMods);
        var uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot service is missing.");

        LoadMap(engine, MapId);
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));

        UiScene scene = RequireScene(uiRoot);
        scene.Layout(uiRoot.Width, uiRoot.Height);
        string initialText = ExtractUiSceneText(scene);
        Assert.That(initialText, Does.Contain("Genre Info Panel Showcase"));
        Assert.That(initialText, Does.Contain("Formation Capability unit cards"));
        Assert.That(ReadSelectedEntityName(engine), Is.EqualTo("Marine 01"));
        Assert.That(scene.TryGetVirtualWindow(GenreInfoShowcaseIds.SelectionGridHostId, out UiVirtualWindow initialWindow), Is.True);
        Assert.That(initialWindow.TotalCount, Is.GreaterThan(initialWindow.VisibleCount));

        string rtsScreen = Path.Combine(screenRoot, "01-rts-squad-en.png");
        ExportScene(scene, rtsScreen, (int)uiRoot.Width, (int)uiRoot.Height);
        timeline.Add("[T+001] Loaded genre_info_showcase, seeded RTS squad as the active live selection, and rendered the English deck + insight layout.");
        trace.Add(new
        {
            step = "rts_loaded_en",
            locale = ReadActiveLocale(engine),
            selected = ReadSelectedEntityName(engine),
            total_rows = initialWindow.TotalCount,
            visible_rows = initialWindow.VisibleCount,
            screenshot = "screens/01-rts-squad-en.png"
        });

        int scrolledStart = ScrollElement(uiRoot, GenreInfoShowcaseIds.SelectionGridHostId, 420f);
        Assert.That(scrolledStart, Is.GreaterThan(initialWindow.StartIndex));
        timeline.Add("[T+002] Scrolled the Formation Capability unit-card grid and advanced the virtual window without remounting the scene.");
        trace.Add(new
        {
            step = "rts_virtualized_scroll",
            locale = ReadActiveLocale(engine),
            start_index_before = initialWindow.StartIndex,
            start_index_after = scrolledStart
        });

        ClickElement(uiRoot, $"{GenreInfoShowcaseIds.RecallGroupPrefix}2");
        Tick(engine, 4);
        scene = RequireScene(uiRoot);
        scene.Layout(uiRoot.Width, uiRoot.Height);
        string heroText = ExtractUiSceneText(scene);
        Assert.That(heroText, Does.Contain("Captain Nyx"));
        Assert.That(heroText, Does.Contain("Void Lance"));
        Assert.That(ReadSelectedEntityName(engine), Is.EqualTo("Captain Nyx"));

        string heroScreen = Path.Combine(screenRoot, "02-moba-hero-en.png");
        ExportScene(scene, heroScreen, (int)uiRoot.Width, (int)uiRoot.Height);
        timeline.Add("[T+003] Recalled the MOBA hero control group and captured the portrait-first single-select treatment in English.");
        trace.Add(new
        {
            step = "moba_single_en",
            locale = ReadActiveLocale(engine),
            selected = ReadSelectedEntityName(engine),
            screenshot = "screens/02-moba-hero-en.png"
        });

        ClickElement(uiRoot, GenreInfoShowcaseIds.LocaleChineseButtonId);
        Tick(engine, 4);
        Assert.That(ReadActiveLocale(engine), Is.EqualTo("zh-CN"));

        ClickElement(uiRoot, $"{GenreInfoShowcaseIds.RecallGroupPrefix}1");
        Tick(engine, 4);
        scene = RequireScene(uiRoot);
        scene.Layout(uiRoot.Width, uiRoot.Height);
        string governorText = ExtractUiSceneText(scene);
        Assert.That(governorText, Does.Contain("题材信息面板 Showcase"));
        Assert.That(governorText, Does.Contain("殖民扩展"));
        Assert.That(ReadSelectedEntityName(engine), Is.EqualTo("Governor Aurelia"));

        string governorScreen = Path.Combine(screenRoot, "03-fourx-governor-zh.png");
        ExportScene(scene, governorScreen, (int)uiRoot.Width, (int)uiRoot.Height);
        timeline.Add("[T+004] Switched locale to zh-CN, recalled the 4X governor control group, and captured the localized strategic portrait card.");
        trace.Add(new
        {
            step = "fourx_governor_zh",
            locale = ReadActiveLocale(engine),
            selected = ReadSelectedEntityName(engine),
            screenshot = "screens/03-fourx-governor-zh.png"
        });

        ClickElement(uiRoot, $"{GenreInfoShowcaseIds.RecallGroupPrefix}4");
        Tick(engine, 4);
        scene = RequireScene(uiRoot);
        scene.Layout(uiRoot.Width, uiRoot.Height);
        string barracksText = ExtractUiSceneText(scene);
        Assert.That(barracksText, Does.Contain("Field Barracks"));
        Assert.That(barracksText, Does.Contain("兵营"));
        Assert.That(ReadSelectedEntityName(engine), Is.EqualTo("Field Barracks"));

        string barracksScreen = Path.Combine(screenRoot, "04-rts-barracks-zh.png");
        ExportScene(scene, barracksScreen, (int)uiRoot.Width, (int)uiRoot.Height);
        timeline.Add("[T+005] Recalled the RTS barracks control group under zh-CN and captured the structure-oriented info panel variant.");
        trace.Add(new
        {
            step = "rts_barracks_zh",
            locale = ReadActiveLocale(engine),
            selected = ReadSelectedEntityName(engine),
            screenshot = "screens/04-rts-barracks-zh.png"
        });

        File.WriteAllText(Path.Combine(artifactRoot, "trace.jsonl"), BuildTraceJsonl(trace));
        File.WriteAllText(Path.Combine(artifactRoot, "battle-report.md"), BuildBattleReport(timeline));
        File.WriteAllText(Path.Combine(artifactRoot, "path.mmd"), BuildPathMermaid());
    }

    private static GameEngine CreateEngine(params string[] modIds)
    {
        string repoRoot = FindRepoRoot();
        string assetsRoot = Path.Combine(repoRoot, "assets");
        List<string> modPaths = RepoModPaths.ResolveExplicit(repoRoot, modIds);

        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
        InstallInput(engine);

        PresentationAcceptanceUiHostInstaller.Install(engine, 1920f, 1080f);
        engine.Start();
        return engine;
    }

    private static void LoadMap(GameEngine engine, string mapId, int frames = 8)
    {
        engine.LoadMap(mapId);
        Tick(engine, frames);
    }

    private static void InstallInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.AuthoritativeInput, inputHandler);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(1f / 60f);
        }
    }

    private static UiScene RequireScene(UIRoot uiRoot)
    {
        return uiRoot.Scene ?? throw new InvalidOperationException("Expected showcase UI scene to be mounted.");
    }

    private static void ClickElement(UIRoot uiRoot, string elementId)
    {
        UiScene scene = RequireScene(uiRoot);
        UiNode node = FindNode(uiRoot, elementId);
        Assert.That(node.ActionHandles.Count, Is.GreaterThan(0), $"Expected element '{elementId}' to expose at least one action handle.");

        float x = node.LayoutRect.X + (node.LayoutRect.Width * 0.5f);
        float y = node.LayoutRect.Y + (node.LayoutRect.Height * 0.5f);
        var clickEvent = new UiPointerEvent(UiPointerEventType.Click, 0, x, y, node.Id);

        bool handled = false;
        for (int i = 0; i < node.ActionHandles.Count; i++)
        {
            handled |= scene.Dispatcher.Dispatch(node.ActionHandles[i], new UiActionContext(scene, clickEvent, node));
        }

        Assert.That(handled, Is.True, $"Expected element '{elementId}' action dispatch to succeed.");
    }

    private static int ScrollElement(UIRoot uiRoot, string elementId, float deltaY)
    {
        UiNode node = FindNode(uiRoot, elementId);
        Assert.That(node.MaxScrollY, Is.GreaterThan(0f), $"Expected scroll host '{elementId}' to be vertically scrollable.");

        MethodInfo setScrollOffset = typeof(UiNode).GetMethod("SetScrollOffset", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(UiNode).FullName, "SetScrollOffset");
        MethodInfo refreshReactiveRuntime = typeof(UiScene).GetMethod("TryRefreshReactiveRuntimeDependencies", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(UiScene).FullName, "TryRefreshReactiveRuntimeDependencies");

        float nextOffsetY = MathF.Min(node.MaxScrollY, node.ScrollOffsetY + deltaY);
        bool changed = (bool)(setScrollOffset.Invoke(node, new object[] { node.ScrollOffsetX, nextOffsetY }) ?? false);
        Assert.That(changed, Is.True, $"Expected scroll host '{elementId}' to accept a larger scroll offset.");

        UiScene scene = RequireScene(uiRoot);
        _ = refreshReactiveRuntime.Invoke(scene, Array.Empty<object>());
        scene.Layout(uiRoot.Width, uiRoot.Height);
        Assert.That(scene.TryGetVirtualWindow(elementId, out UiVirtualWindow window), Is.True);
        return window.StartIndex;
    }

    private static UiNode FindNode(UIRoot uiRoot, string elementId)
    {
        UiScene scene = RequireScene(uiRoot);
        scene.Layout(uiRoot.Width, uiRoot.Height);
        return scene.FindByElementId(elementId)
            ?? throw new InvalidOperationException($"Failed to find UI element '{elementId}'.");
    }

    private static void ExportScene(UiScene scene, string outputPath, int width, int height)
    {
        var renderer = new SkiaUiRenderer();
        renderer.ExportPng(scene, outputPath, width, height);
        Assert.That(File.Exists(outputPath), Is.True, $"Expected screenshot '{outputPath}' to exist.");
    }

    private static string ReadSelectedEntityName(GameEngine engine)
    {
        return Ludots.Tests.EntityCollectionTestAccess.TryGetCommandSourcePrimary(engine, out Entity selected) &&
               engine.World.TryGet(selected, out Name name)
            ? name.Value
            : string.Empty;
    }

    private static string ReadActiveLocale(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.PresentationTextLocaleSelection) is PresentationTextLocaleSelection localeSelection
            ? localeSelection.ActiveLocaleKey
            : string.Empty;
    }

    private static string ExtractUiSceneText(UiScene scene)
    {
        if (scene.Root == null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        AppendUiNodeText(scene.Root, builder);
        return builder.ToString();
    }

    private static void AppendUiNodeText(UiNode node, StringBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(node.TextContent))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(node.TextContent);
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            AppendUiNodeText(node.Children[i], builder);
        }
    }

    private static string BuildTraceJsonl(IReadOnlyList<object> trace)
    {
        var lines = new string[trace.Count];
        for (int i = 0; i < trace.Count; i++)
        {
            lines[i] = JsonSerializer.Serialize(trace[i]);
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string BuildBattleReport(IReadOnlyList<string> timeline)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Genre Info Showcase Acceptance");
        builder.AppendLine();
        builder.AppendLine("## Scenario Card");
        builder.AppendLine("- Goal: validate one localized cross-genre info-panel runtime for 4X, RTS, and MOBA selection surfaces.");
        builder.AppendLine("- Map: `genre_info_showcase`");
        builder.AppendLine("- Mods: `LudotsCoreMod`, `CoreInputMod`, `CameraProfilesMod`, `FourXDemoMod`, `EntityCommandPanelMod`, `RtsDemoMod`, `MobaDemoMod`, `EntityInfoPanelsMod`, `GenreInfoShowcaseMod`");
        builder.AppendLine("- Viewport: `1920x1080` headless Skia export.");
        builder.AppendLine("- Evidence: four screenshots plus trace, battle report, and path mermaid.");
        builder.AppendLine();
        builder.AppendLine("## Timeline");
        for (int i = 0; i < timeline.Count; i++)
        {
            builder.AppendLine($"- {timeline[i]}");
        }

        builder.AppendLine();
        builder.AppendLine("## Outcome");
        builder.AppendLine("- success: yes");
        builder.AppendLine("- verdict: one insight runtime now covers portrait blend, SC2/War3-style single-select portrait focus, Formation Capability-style unit cards, localized copy, and control-group driven selection recall.");
        builder.AppendLine("- screenshots:");
        builder.AppendLine("  - `screens/01-rts-squad-en.png`");
        builder.AppendLine("  - `screens/02-moba-hero-en.png`");
        builder.AppendLine("  - `screens/03-fourx-governor-zh.png`");
        builder.AppendLine("  - `screens/04-rts-barracks-zh.png`");
        return builder.ToString();
    }

    private static string BuildPathMermaid()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart TD",
            "    A[Load genre_info_showcase] --> B[Seed control groups and bind live selection]",
            "    B --> C[Render RTS multi-select deck in English]",
            "    C --> D[Scroll virtualized unit-card grid]",
            "    D --> E[Recall MOBA hero portrait card]",
            "    E --> F[Switch locale to zh-CN]",
            "    F --> G[Recall 4X governor strategic card]",
            "    G --> H[Recall RTS barracks structure card]",
            "    H --> I[Export screenshots and acceptance artifacts]"
        }) + Environment.NewLine;
    }

    private static string FindRepoRoot()
    {
        string current = TestContext.CurrentContext.WorkDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "mods")) &&
                File.Exists(Path.Combine(current, "AGENTS.md")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current)!;
        }

        throw new DirectoryNotFoundException("Repository root not found from test work directory.");
    }

    private sealed class NullInputBackend : IInputBackend
    {
        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => false;
        public Vector2 GetMousePosition() => Vector2.Zero;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }
}
