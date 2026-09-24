using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.Tests;
using Ludots.UI;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

/// <summary>
/// UI command panels showcase acceptance: 0-encode panels driven end-to-end by the
/// generic panel host — the caster's ability slots as a clickable skill row (typed
/// collection projection + Button + tooltip) and a C&C-style right-side global command
/// panel. Clicks are semantic actions admitted by the mounted interaction context
/// (panel = view/controller, context permits), consumed by context-gated trigger
/// graphs writing map state.
/// </summary>
[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class UiCommandPanelsShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string ShowcaseMapId = "ucp_arena";

    [Test]
    public void CommandPanels_MountClickCastAndTip_AllZeroEncode()
    {
        string repoRoot = FindRepoRoot();
        var backend = new TestInputBackend();
        using GameEngine engine = CreateEngine(repoRoot, backend);
        engine.LoadMap(new MapLoadRequest(
            new MapId(ShowcaseMapId),
            MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1) })));
        Tick(engine, 8);

        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
            string.Join(" | ", engine.TriggerManager.Errors));

        UIRoot root = RequireUiRoot(engine);
        UiScene? scene = root.Scene;
        Assert.That(scene, Is.Not.Null, "panel presentation must have mounted the surface scene");

        // Two ability chips + two command buttons = four interactive affordances.
        Assert.That(CountButtons(scene!.Root), Is.GreaterThanOrEqualTo(4),
            "skill chips and command buttons both mount as interactive nodes");

        // ── hover a command button: generic tooltip appears ──
        (float buildX, float buildY) = FindButtonPointByText(root, "建造 兵营")
            ?? throw new AssertionException("build button not found in the mounted scene");
        Move(root, buildX, buildY);
        Move(root, buildX + 1f, buildY + 1f);
        UiNode? tipTitle = FindNodeByClass(scene.Root, "ui-tip-title");
        Assert.That(tipTitle, Is.Not.Null, "hovering a tipped control publishes the tooltip overlay");
        Assert.That(tipTitle!.TextContent, Is.EqualTo("兵营"));

        // ── click build: semantic action → context-gated graph → map state ──
        MapVariableStore? variables = engine.CurrentMapSession?.Variables;
        Assert.That(variables, Is.Not.Null);
        Click(root, buildX, buildY);
        Tick(engine, 4);
        Assert.That(variables!.ReadInt("ucp_built"), Is.EqualTo(1),
            "clicking the build button must run the context-mounted consumption graph");

        // ── click a skill chip: real cast through the same intent pipeline the keyboard uses ──
        (float chipX, float chipY) = FindButtonPointByText(root, "Ability.Ucp.Fireball")
            ?? FindButtonPointByTip(root, "Ability.Ucp.Fireball")
            ?? throw new AssertionException("fireball chip not found in the mounted scene");
        Entity dummy = FindEntityByName(engine, "靶子");
        float dummyHealthBefore = CurrentHealth(engine, dummy);
        Click(root, chipX, chipY);
        Tick(engine, 10);
        var drain = engine.GetService(CoreServiceKeys.CommandIntentBufferDrain);
        Assert.That(drain?.LastDrainedCount, Is.GreaterThan(0), "the chip's cast intent must reach the drain");
        Assert.That(drain?.LastRejectionReason, Is.Null, $"cast intent rejected: {drain?.LastRejectionReason}");
        // Full real-cast loop: chip payload carries the slot, the consumption graph resolves
        // the target from the maintained enemies collection (QueryFromCollection + team
        // filter + TargetListGet), SubmitCast drains accepted with target, the fireball's
        // InstantDamage lands on the dummy.
        Assert.That(CurrentHealth(engine, dummy), Is.LessThan(dummyHealthBefore),
            "clicking the chip must cast for real — target resolved in-graph, damage lands");

        // ── tip leaves with the pointer ──
        Move(root, 40f, 40f);
        Move(root, 41f, 41f);
        Assert.That(FindNodeByClass(scene.Root, "ui-tip"), Is.Null, "leaving the control releases the tip");
    }

    private static Entity FindEntityByName(GameEngine engine, string name)
    {
        Entity found = Entity.Null;
        var query = new Arch.Core.QueryDescription().WithAll<Ludots.Core.Components.Name>();
        engine.World.Query(in query, (Entity entity, ref Ludots.Core.Components.Name value) =>
        {
            if (found == Entity.Null && string.Equals(value.Value, name, StringComparison.Ordinal))
            {
                found = entity;
            }
        });
        if (found != Entity.Null)
        {
            return found;
        }

        throw new AssertionException($"No entity named '{name}' in the world.");
    }

    private static float CurrentHealth(GameEngine engine, Entity rep)
    {
        int healthId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId("Health");
        return engine.World.Get<Ludots.Core.Gameplay.GAS.Components.AttributeBuffer>(rep).GetCurrent(healthId);
    }

    private static UIRoot RequireUiRoot(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot service missing.");
    }

    private static GameEngine CreateEngine(string repoRoot, TestInputBackend backend)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "UiCommandPanelsShowcaseMod" }),
            Path.Combine(repoRoot, "assets"));
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputHandler = new PlayerInputHandler(backend, inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.InputBackend, backend);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        AcceptanceUiHostInstaller.Install(engine);
        engine.SetService(
            CoreServiceKeys.ViewController,
            (Ludots.Core.Presentation.Camera.IViewController)new HeadlessViewController(1600f, 900f));
        engine.Start();
        return engine;
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
        }
    }

    private static void Move(UIRoot root, float x, float y)
    {
        root.HandleInput(new PointerEvent
        {
            DeviceType = InputDeviceType.Mouse,
            PointerId = 0,
            Action = PointerAction.Move,
            X = x,
            Y = y,
        });
    }

    private static void Click(UIRoot root, float x, float y)
    {
        root.HandleInput(new PointerEvent
        {
            DeviceType = InputDeviceType.Mouse,
            PointerId = 0,
            Action = PointerAction.Down,
            Button = PointerButton.Left,
            X = x,
            Y = y,
        });
        root.HandleInput(new PointerEvent
        {
            DeviceType = InputDeviceType.Mouse,
            PointerId = 0,
            Action = PointerAction.Up,
            Button = PointerButton.Left,
            X = x,
            Y = y,
        });
    }

    private static int CountButtons(UiNode node)
    {
        int count = node.Kind == UiNodeKind.Button ? 1 : 0;
        foreach (UiNode child in node.Children)
        {
            count += CountButtons(child);
        }

        return count;
    }

    private static UiNode? FindNodeByClass(UiNode node, string className)
    {
        foreach (string token in node.ClassNames)
        {
            if (string.Equals(token, className, StringComparison.Ordinal))
            {
                return node;
            }
        }

        foreach (UiNode child in node.Children)
        {
            if (FindNodeByClass(child, className) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private static (float X, float Y)? FindButtonPointByText(UIRoot root, string text)
    {
        UiNode? button = FindButtonByText(root.Scene?.Root, text);
        return button == null ? null : CenterOf(root, button);
    }

    private static (float X, float Y)? FindButtonPointByTip(UIRoot root, string title)
    {
        UiNode? button = FindButtonByTip(root.Scene?.Root, title);
        return button == null ? null : CenterOf(root, button);
    }

    private static UiNode? FindButtonByText(UiNode? node, string text)
    {
        if (node == null)
        {
            return null;
        }

        if (node.Kind == UiNodeKind.Button &&
            (string.Equals(node.TextContent, text, StringComparison.Ordinal) ||
             ContainsText(node, text)))
        {
            return node;
        }

        foreach (UiNode child in node.Children)
        {
            if (FindButtonByText(child, text) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private static UiNode? FindButtonByTip(UiNode? node, string title)
    {
        if (node == null)
        {
            return null;
        }

        if (node.Kind == UiNodeKind.Button &&
            node.Attributes.Contains("data-tip-title") &&
            string.Equals(node.Attributes["data-tip-title"], title, StringComparison.Ordinal))
        {
            return node;
        }

        foreach (UiNode child in node.Children)
        {
            if (FindButtonByTip(child, title) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private static bool ContainsText(UiNode node, string text)
    {
        foreach (UiNode child in node.Children)
        {
            if (string.Equals(child.TextContent, text, StringComparison.Ordinal) || ContainsText(child, text))
            {
                return true;
            }
        }

        return false;
    }

    private static (float X, float Y) CenterOf(UIRoot root, UiNode node)
    {
        // Force layout, then hit-scan the node's box for a dispatchable point.
        Move(root, 1f, 1f);
        UiScene? scene = root.Scene;
        Assert.That(scene, Is.Not.Null);
        float x0 = node.LayoutRect.X;
        float y0 = node.LayoutRect.Y;
        float x1 = x0 + MathF.Max(4f, node.LayoutRect.Width);
        float y1 = y0 + MathF.Max(4f, node.LayoutRect.Height);
        for (float y = y0 + 2f; y < y1; y += 3f)
        {
            for (float x = x0 + 2f; x < x1; x += 3f)
            {
                UiNode? hit = scene!.HitTest(x, y);
                for (UiNode? current = hit; current != null; current = current.Parent)
                {
                    if (current == node)
                    {
                        return (x, y);
                    }
                }
            }
        }

        throw new AssertionException($"No dispatchable point inside button at ({x0},{y0},{x1},{y1}).");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "Core", "Ludots.Core.csproj")) &&
                Directory.Exists(Path.Combine(dir.FullName, "mods")))
            {
                return dir.FullName;
            }

            dir = dir.Parent!;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
    }

    private sealed class HeadlessViewController : Ludots.Core.Presentation.Camera.IViewController
    {
        public HeadlessViewController(float width, float height)
        {
            Resolution = new Vector2(width, height);
        }

        public Vector2 Resolution { get; }
        public float Fov => 50f;
        public float AspectRatio => Resolution.X / Resolution.Y;
    }

    private sealed class TestInputBackend : IInputBackend
    {
        private readonly HashSet<string> _buttons = new(StringComparer.Ordinal);

        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => _buttons.Contains(devicePath);
        public Vector2 GetMousePosition() => Vector2.Zero;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;

        public void SetButton(string path, bool down)
        {
            if (down)
            {
                _buttons.Add(path);
            }
            else
            {
                _buttons.Remove(path);
            }
        }
    }
}
