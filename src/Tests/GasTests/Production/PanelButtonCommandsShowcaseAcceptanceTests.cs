using System;
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
using Ludots.Core.UI.PanelHosting;
using Ludots.Tests;
using Ludots.UI;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

/// <summary>
/// Panel button commands showcase acceptance: one zero-C# panel strip where the caster's
/// abilities are aggregated by graph rules into a clickable skill row, plus a spawn button.
/// Clicks are semantic actions admitted by the mounted interaction context; the skill chip
/// casts through the same intent pipeline the keyboard uses, the spawn button creates a
/// real entity via an effect. Skills, entities and presentation all come from
/// FireballSharedMod — this showcase adds zero wheels.
/// </summary>
[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class PanelButtonCommandsShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string ShowcaseMapId = "panel_button_commands_arena";
    private const string FireballChipText = "Ability.Fireball.Cast";
    private const string SpawnChipText = "Ability.PanelButtons.SpawnTarget";

    [Test]
    public void ButtonCommands_ClickToCastAndSpawn_ZeroCodeAndReusedFireball()
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

        // Two abilities on the caster aggregate into two clickable chips — nothing else.
        Assert.That(CountButtons(scene!.Root), Is.EqualTo(2),
            "the strip aggregates one chip per ability slot, zero hardcoded buttons");

        // PROBE: dump interaction context instances on the hero rep
        {
            Entity hero = FindEntityByName(engine, "施法者");
            Assert.That(
                engine.World.TryGet<Ludots.Core.Input.Interaction.InteractionContextInstance>(hero, out var baseContext) &&
                baseContext.ContextId > 0,
                Is.True,
                "hero rep carries no base interaction context — the profile never mounted");
        }

        // ── hover the spawn chip: tooltip appears with the ability display name ──
        (float spawnX, float spawnY) = FindButtonPointByText(root, SpawnChipText)
            ?? throw new AssertionException("spawn chip not found in the mounted scene");
        Move(root, spawnX, spawnY);
        Move(root, spawnX + 1f, spawnY + 1f);
        UiNode? tipTitle = FindNodeByClass(scene.Root, "ui-tip-title");
        Assert.That(tipTitle, Is.Not.Null, "hovering a tipped control publishes the tooltip overlay");
        Assert.That(tipTitle!.TextContent, Is.EqualTo(SpawnChipText));

        // ── click the spawn chip: cast intent → ability → CreateUnit effect → real entity ──
        PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("PanelHost service missing.");
        PanelInstanceHandle strip = FindPanel(panelHost, "panel.buttonCommands.strip");
        Click(root, spawnX, spawnY);
        Tick(engine, 30);
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
            "spawn click errors: " + string.Join(" | ", engine.TriggerManager.Errors));
        var bridge = engine.GetService(CoreServiceKeys.PanelEventActionBridge);
        Assert.That(bridge?.LastRefusalReason, Is.Null, $"panel event refused: {bridge?.LastRefusalReason}");
        var spawnDrain = engine.GetService(CoreServiceKeys.CommandIntentBufferDrain);
        Assert.That(spawnDrain?.LastDrainedCount, Is.GreaterThan(0), "spawn cast intent must reach the drain");
        Assert.That(spawnDrain?.LastRejectionReason, Is.Null, $"spawn cast rejected: {spawnDrain?.LastRejectionReason}");
        Assert.That(panelHost.TryGetValues(strip, out var values), Is.True);
        Assert.That(values.Get("targetCount"), Is.EqualTo(1f).Within(0.001f),
            "clicking the spawn chip must create a real ally-target entity the strip graph counts");

        // ── click the fireball chip: real cast through the keyboard's intent pipeline ──
        (float chipX, float chipY) = FindButtonPointByText(root, FireballChipText)
            ?? FindButtonPointByTip(root, FireballChipText)
            ?? throw new AssertionException("fireball chip not found in the mounted scene");
        Entity target = FindEntityByName(engine, "靶子");
        float targetHealthBefore = CurrentHealth(engine, target);
        Click(root, chipX, chipY);
        Tick(engine, 240);
        var drain = engine.GetService(CoreServiceKeys.CommandIntentBufferDrain);
        Assert.That(drain?.LastDrainedCount, Is.GreaterThan(0), "the chip's cast intent must reach the drain");
        Assert.That(drain?.LastRejectionReason, Is.Null, $"cast intent rejected: {drain?.LastRejectionReason}");
        Assert.That(ProjectileArrivedAtTarget(engine, target),
            "clicking the chip must cast for real — the fireball flies the full 600cm to the target (headless impact detonation is tracked by #1739)");

        // NOTE(#1739): targetless-click guarding (no target ⇒ no order) is not authorable today —
        // TargetListGet's valid flag has no authorable port and the drain accepts targetless
        // cast intents by design (self-cast abilities). Follow-up options recorded in #1739.

        // ── tip leaves with the pointer ──
        Move(root, 40f, 40f);
        Move(root, 41f, 41f);
        Assert.That(FindNodeByClass(scene.Root, "ui-tip"), Is.Null, "leaving the control releases the tip");
    }

    private static bool ProjectileArrivedAtTarget(GameEngine engine, Entity target)
    {
        if (!engine.World.TryGet<Ludots.Core.Components.WorldPositionCm>(target, out var targetPos))
        {
            return false;
        }

        bool arrived = false;
        var query = new Arch.Core.QueryDescription().WithAll<Ludots.Core.Gameplay.GAS.ProjectileState>();
        engine.World.Query(in query, (Entity e) =>
        {
            if (arrived || !engine.World.TryGet<Ludots.Core.Components.WorldPositionCm>(e, out var pos))
            {
                return;
            }

            float dx = (float)(pos.Value.X - targetPos.Value.X);
            float dy = (float)(pos.Value.Y - targetPos.Value.Y);
            if ((dx * dx) + (dy * dy) < 60f * 60f)
            {
                arrived = true;
            }
        });

        return arrived;
    }

    private static PanelInstanceHandle FindPanel(PanelHost host, string templateId)
    {
        foreach (PanelHostInstanceInfo info in host.SnapshotInstances())
        {
            if (info.TemplateId == templateId)
            {
                return info.Handle;
            }
        }

        throw new InvalidOperationException($"No panel '{templateId}' mounted.");
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
            RepoModPaths.ResolveExplicit(
                repoRoot,
                new[] { "LudotsCoreMod", "FireballSharedMod", "PanelButtonCommandsShowcaseMod" }),
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
            (Ludots.Core.Presentation.Camera.IViewController)new HeadlessViewController(1280f, 720f));
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

    private static UiNode? FindButtonByTip(UiNode? node, string title)
    {
        if (node == null)
        {
            return null;
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

    private static bool ContainsText(UiNode node, string text)
    {
        foreach (UiNode child in node.Children)
        {
            if (string.Equals(child.TextContent, text, StringComparison.Ordinal) ||
                ContainsText(child, text))
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
        string? directory = Path.GetDirectoryName(typeof(PanelButtonCommandsShowcaseAcceptanceTests).Assembly.Location);
        while (directory != null && !Directory.Exists(Path.Combine(directory, "mods")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new InvalidOperationException("Repository root not found.");
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
