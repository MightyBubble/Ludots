using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using InteractionShowcaseMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Events;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
[NonParallelizable]
public sealed class CommandSourceEntityCollectionPanelAcceptanceTests
{
    private const string InteractionShowcaseHubMapId = "interaction_showcase_hub";

    private static readonly string[] ShowcaseMods =
    {
        "EntityInfoPanelsMod",
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod",
        "InteractionShowcaseMod",
    };

    [Test]
    public void InteractionShowcase_CommandSourceEntityCollectionPanel_VirtualizesAndWritesScreenshotArtifacts()
    {
        string repoRoot = FindRepoRoot();
        string artifactRoot = Path.Combine(repoRoot, "artifacts", "acceptance", "command-source-entity-collection-panel");
        string screenRoot = Path.Combine(artifactRoot, "screens");
        Directory.CreateDirectory(screenRoot);

        using var engine = CreateEngine(ShowcaseMods);
        var uiRoot = new UIRoot(new SkiaUiRenderer());
        uiRoot.Resize(1600f, 900f);
        engine.SetService(CoreServiceKeys.UIRoot, uiRoot);

        LoadMap(engine, InteractionShowcaseHubMapId);

        Entity local = engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) && localObj is Entity entity
            ? entity
            : throw new InvalidOperationException("Local player entity is missing.");
        EntityCollectionStore collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("EntityCollectionStore service is missing.");

        const int commandSourceCount = 72;
        int healthId = AttributeRegistry.Register("Acceptance.CommandSource.Health");
        int manaId = AttributeRegistry.Register("Acceptance.CommandSource.Mana");
        var selected = new Entity[commandSourceCount];
        for (int i = 0; i < commandSourceCount; i++)
        {
            string unitName = (i % 3) switch
            {
                0 => $"Spearman {i / 3:00}",
                1 => $"Archer {i / 3:00}",
                _ => $"Knight {i / 3:00}",
            };
            var attributes = AttributeBuffer.CreateAttached();
            attributes.SetBase(healthId, 100f + i);
            attributes.SetCurrent(healthId, 60f + i);
            attributes.SetBase(manaId, 80f);
            attributes.SetCurrent(manaId, 20f + (i % 25));
            selected[i] = engine.World.Create(
                new Name { Value = unitName },
                attributes);
        }

        ReplaceCommandSource(collections, local, selected);
        Tick(engine, 4);

        UiScene scene = uiRoot.Scene ?? throw new InvalidOperationException("Interaction showcase scene should be mounted.");
        scene.Layout(uiRoot.Width, uiRoot.Height);

        UiNode host = FindNodeByIdPrefix(scene.Root, "entity-info-collection-grid-")
            ?? throw new InvalidOperationException("Command-source entity collection host should exist.");
        string hostId = host.ElementId ?? throw new InvalidOperationException("Command-source entity collection host should carry a stable element id.");
        Assert.That(scene.TryGetVirtualWindow(hostId, out UiVirtualWindow initialWindow), Is.True);
        Assert.That(initialWindow.TotalCount, Is.LessThan(commandSourceCount));
        Assert.That(initialWindow.VisibleCount, Is.GreaterThan(0));
        Assert.That(initialWindow.VisibleCount, Is.LessThan(initialWindow.TotalCount), "Large selection panels should virtualize instead of composing every row.");

        string sceneText = ExtractUiSceneText(scene);
        Assert.That(sceneText, Does.Contain("Acceptance command source"));
        Assert.That(sceneText, Does.Contain("Spearman x24 *"));
        Assert.That(sceneText, Does.Contain("LIVE 72"));
        Assert.That(sceneText, Does.Contain("Acceptance.CommandSource.Health"));

        float x = host.LayoutRect.X + (host.LayoutRect.Width * 0.5f);
        float y = host.LayoutRect.Y + (host.LayoutRect.Height * 0.5f);
        bool handledScroll = uiRoot.HandleInput(new PointerEvent
        {
            DeviceType = InputDeviceType.Mouse,
            PointerId = 0,
            Action = PointerAction.Scroll,
            X = x,
            Y = y,
            DeltaY = 320f,
        });
        scene.Layout(uiRoot.Width, uiRoot.Height);

        Assert.That(handledScroll, Is.True);
        Assert.That(scene.TryGetVirtualWindow(hostId, out UiVirtualWindow scrolledWindow), Is.True);
        Assert.That(scrolledWindow.StartIndex, Is.GreaterThan(initialWindow.StartIndex));

        string screenshotPath = Path.Combine(screenRoot, "interaction-selection-entity-collection.png");
        new SkiaUiRenderer().ExportPng(scene, screenshotPath, 1600, 900);
        Assert.That(File.Exists(screenshotPath), Is.True);

        File.WriteAllText(
            Path.Combine(artifactRoot, "battle-report.md"),
            string.Join(Environment.NewLine, new[]
            {
                "# Command Source Entity Collection Panel Battle Report",
                string.Empty,
                "## Scenario Card",
                "- Goal: prove the command-source entity collection panel reads the active collection and virtualizes a large entity list.",
                "- Viewport: 1600x900 headless Skia UI capture.",
                "- Command source: 72 deterministic entities across Spearman / Archer / Knight categories with id, name, and GAS attributes.",
                string.Empty,
                "## Outcome",
                "- success: yes",
                "- verdict: the interaction showcase mounts a bottom-left command roster that renders category chips and dense unit tiles without composing every entity tile each frame.",
                $"- screenshot: `screens/{Path.GetFileName(screenshotPath)}`",
            }));

        File.WriteAllText(
            Path.Combine(artifactRoot, "trace.jsonl"),
            string.Join(Environment.NewLine, new[]
            {
                "{\"step\":\"load-map\",\"action\":\"capture\",\"detail\":\"Loaded interaction_showcase_hub with UIRoot enabled.\"}",
                $"{{\"step\":\"seed-command-source\",\"action\":\"mutate\",\"detail\":\"Bound {commandSourceCount} categorized entities to collection.command.source.\"}}",
                "{\"step\":\"scroll-panel\",\"action\":\"scroll\",\"detail\":\"Scrolled the entity collection panel and advanced the virtual window.\"}",
                $"{{\"step\":\"export-screenshot\",\"action\":\"capture\",\"detail\":\"Wrote screens/{Path.GetFileName(screenshotPath)}.\"}}",
            }));

        File.WriteAllText(
            Path.Combine(artifactRoot, "path.mmd"),
            string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[Load interaction_showcase_hub] --> B[Seed 72 categorized command-source entities]",
                "    B --> C[Publish collection.command.source]",
                "    C --> D[Mount command roster dock in Interaction showcase UI]",
                "    D --> E[Verify virtual window total rows > visible rows]",
                "    E --> F[Scroll collection host]",
                "    F --> G[Export screenshot artifact]",
            }));
    }

    private static void ReplaceCommandSource(EntityCollectionStore collections, Entity owner, ReadOnlySpan<Entity> members)
    {
        var descriptor = EntityCollectionDescriptor.Create(
            EntityCollectionKeys.CommandSource,
            EntityCollectionSourceKind.Explicit,
            EntityCollectionRoleKind.CommandSource,
            owner,
            members.Length > 0 ? members[0] : Entity.Null,
            "Acceptance command source",
            $"{members.Length} entities ready for commands");
        collections.Replace(owner, descriptor, members, owner);
    }

    private static GameEngine CreateEngine(params string[] modIds)
    {
        string repoRoot = FindRepoRoot();
        string assetsRoot = Path.Combine(repoRoot, "assets");
        var modPaths = RepoModPaths.ResolveExplicit(repoRoot, modIds);

        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
        InstallInput(engine);
        engine.SetService(CoreServiceKeys.UiTextMeasurer, new SkiaTextMeasurer());
        engine.SetService(CoreServiceKeys.UiImageSizeProvider, new SkiaImageSizeProvider());
        engine.Start();
        return engine;
    }

    private static void LoadMap(GameEngine engine, string mapId, int frames = 6)
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
            engine.Tick(1f / 60f);
        }
    }

    private static string ExtractUiSceneText(UiScene scene)
    {
        if (scene.Root == null)
        {
            return string.Empty;
        }

        var writer = new System.Text.StringBuilder();
        AppendUiNodeText(scene.Root, writer);
        return writer.ToString();
    }

    private static void AppendUiNodeText(UiNode node, System.Text.StringBuilder writer)
    {
        if (!string.IsNullOrWhiteSpace(node.TextContent))
        {
            if (writer.Length > 0)
            {
                writer.AppendLine();
            }

            writer.Append(node.TextContent);
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            AppendUiNodeText(node.Children[i], writer);
        }
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

    private static UiNode? FindNodeByIdPrefix(UiNode? node, string prefix)
    {
        return FindBestMatch(node, prefix, null);
    }

    private static UiNode? FindBestMatch(UiNode? node, string prefix, UiNode? currentBest)
    {
        if (node == null)
        {
            return currentBest;
        }

        if (!string.IsNullOrWhiteSpace(node.ElementId) &&
            node.ElementId.StartsWith(prefix, StringComparison.Ordinal) &&
            (currentBest == null || node.ElementId.Length < currentBest.ElementId!.Length))
        {
            currentBest = node;
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            currentBest = FindBestMatch(node.Children[i], prefix, currentBest);
        }

        return currentBest;
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
