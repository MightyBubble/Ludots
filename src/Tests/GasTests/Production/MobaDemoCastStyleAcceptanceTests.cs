using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

/// <summary>
/// MOBA demo, played through its own interaction contexts and graphs: smart cast hits the
/// enemy under the cursor, F3 switches to aim-first (Q opens a range state, left-click an
/// enemy casts, right-click cancels), and right-click on the ground walks the champion.
/// </summary>
[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class MobaDemoCastStyleAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string SmartCastContext = "interaction.context.moba_demo.smart_cast";
    private const string AimCastContext = "interaction.context.moba_demo.aim_cast";
    private const string AimingContext = "interaction.context.moba_demo.aiming";

    [Test]
    public void SmartCast_QHitsTheEnemyUnderTheCursor_EmptyGroundCastsNothing()
    {
        using GameEngine engine = CreateEngine(out TestInputBackend input);
        Entity hero = FindEntity(engine.World, "Hero");
        Entity enemy1 = FindEntity(engine.World, "Enemy1");
        Entity enemy2 = FindEntity(engine.World, "Enemy2");
        Assert.That(IsDerivedContextActive(engine, hero, SmartCastContext), Is.True,
            "The champion starts in smart cast.");

        AimPointerAtWorldCm(engine, input, new Vector2(300f, 900f));
        Press(engine, input, "<Keyboard>/q");
        Tick(engine, 20);
        Assert.Multiple(() =>
        {
            Assert.That(ReadHealth(engine.World, enemy1), Is.EqualTo(100f).Within(0.001f));
            Assert.That(ReadHealth(engine.World, enemy2), Is.EqualTo(100f).Within(0.001f));
        });

        AimPointerAt(engine, input, enemy2);
        Press(engine, input, "<Keyboard>/q");
        Tick(engine, 20);
        Assert.Multiple(() =>
        {
            Assert.That(ReadHealth(engine.World, enemy2), Is.EqualTo(80f).Within(0.001f), Diagnostics(engine));
            Assert.That(ReadHealth(engine.World, enemy1), Is.EqualTo(100f).Within(0.001f));
        });
    }

    [Test]
    public void AimFirst_QOpensRangeState_LeftClickEnemyCasts_RightClickCancels()
    {
        using GameEngine engine = CreateEngine(out TestInputBackend input);
        Entity hero = FindEntity(engine.World, "Hero");
        Entity enemy1 = FindEntity(engine.World, "Enemy1");

        Press(engine, input, "<Keyboard>/F3");
        Assert.Multiple(() =>
        {
            Assert.That(IsDerivedContextActive(engine, hero, AimCastContext), Is.True, Diagnostics(engine));
            Assert.That(IsDerivedContextActive(engine, hero, SmartCastContext), Is.False);
        });

        AimPointerAt(engine, input, enemy1);
        Press(engine, input, "<Keyboard>/q");
        Assert.That(IsDerivedContextActive(engine, hero, AimingContext), Is.True, Diagnostics(engine));
        Tick(engine, 20);
        Assert.That(ReadHealth(engine.World, enemy1), Is.EqualTo(100f).Within(0.001f),
            "Aim-first never casts on the key press alone.");

        Press(engine, input, "<Mouse>/LeftButton");
        Tick(engine, 20);
        Assert.Multiple(() =>
        {
            Assert.That(ReadHealth(engine.World, enemy1), Is.EqualTo(80f).Within(0.001f), Diagnostics(engine));
            Assert.That(IsDerivedContextActive(engine, hero, AimingContext), Is.False);
        });

        Tick(engine, 120);
        Fix64Vec2 heroBefore = engine.World.Get<WorldPositionCm>(hero).Value;
        Press(engine, input, "<Keyboard>/e");
        Assert.That(IsDerivedContextActive(engine, hero, AimingContext), Is.True);
        AimPointerAtWorldCm(engine, input, new Vector2(300f, 900f));
        Press(engine, input, "<Mouse>/RightButton");
        Tick(engine, 30);
        Fix64Vec2 heroAfter = engine.World.Get<WorldPositionCm>(hero).Value;
        Assert.Multiple(() =>
        {
            Assert.That(IsDerivedContextActive(engine, hero, AimingContext), Is.False);
            Assert.That(ReadHealth(engine.World, enemy1), Is.EqualTo(80f).Within(0.001f));
            Assert.That(heroAfter.X.ToFloat(), Is.EqualTo(heroBefore.X.ToFloat()).Within(0.01f),
                "Right-click while aiming only cancels; the champion must not walk.");
            Assert.That(heroAfter.Y.ToFloat(), Is.EqualTo(heroBefore.Y.ToFloat()).Within(0.01f));
        });

        Press(engine, input, "<Keyboard>/r");
        Assert.That(IsDerivedContextActive(engine, hero, AimingContext), Is.True);
        Press(engine, input, "<Keyboard>/Escape");
        Assert.That(IsDerivedContextActive(engine, hero, AimingContext), Is.False, "Esc also cancels aiming.");

        Press(engine, input, "<Keyboard>/F2");
        Assert.Multiple(() =>
        {
            Assert.That(IsDerivedContextActive(engine, hero, SmartCastContext), Is.True);
            Assert.That(IsDerivedContextActive(engine, hero, AimCastContext), Is.False);
        });
    }

    [Test]
    public void RightClickGround_WalksTheChampion()
    {
        using GameEngine engine = CreateEngine(out TestInputBackend input);
        Entity hero = FindEntity(engine.World, "Hero");
        var destination = new Vector2(-400f, 300f);

        AimPointerAtWorldCm(engine, input, destination);
        Press(engine, input, "<Mouse>/RightButton");
        Tick(engine, 120);

        Fix64Vec2 position = engine.World.Get<WorldPositionCm>(hero).Value;
        Assert.Multiple(() =>
        {
            Assert.That(position.X.ToFloat(), Is.EqualTo(destination.X).Within(1f), Diagnostics(engine));
            Assert.That(position.Y.ToFloat(), Is.EqualTo(destination.Y).Within(1f));
        });
    }

    private static GameEngine CreateEngine(out TestInputBackend backend)
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "MobaDemoMod" }),
            Path.Combine(repoRoot, "assets"));

        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        backend = new TestInputBackend();
        var inputHandler = new PlayerInputHandler(backend, inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        engine.SetService(CoreServiceKeys.ScreenProjector, (IScreenProjector)new CentimeterScreenProjector());
        engine.SetService(CoreServiceKeys.ScreenRayProvider, (IScreenRayProvider)new CentimeterScreenRayProvider());
        AcceptanceUiHostInstaller.Install(engine);
        engine.Start();
        engine.LoadStartupMap();
        Tick(engine, 5);
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), Diagnostics(engine));
        return engine;
    }

    private static bool IsDerivedContextActive(GameEngine engine, Entity rep, string contextId)
    {
        var profiles = engine.GetService(CoreServiceKeys.InteractionContextProfileRegistry)
            ?? throw new InvalidOperationException("InteractionContextProfileRegistry missing.");
        int profileId = profiles.ProfileIdRegistry.GetId(contextId);
        return engine.World.TryGet(rep, out InteractionContextInstances instances) &&
               instances.IndexOf(profileId) >= 0;
    }

    private static void AimPointerAt(GameEngine engine, TestInputBackend backend, Entity entity)
    {
        Fix64Vec2 position = engine.World.Get<WorldPositionCm>(entity).Value;
        AimPointerAtWorldCm(engine, backend, new Vector2(position.X.ToFloat(), position.Y.ToFloat()));
    }

    private static void AimPointerAtWorldCm(GameEngine engine, TestInputBackend backend, Vector2 worldCm)
    {
        IScreenProjector projector = engine.GetService(CoreServiceKeys.ScreenProjector)
            ?? throw new InvalidOperationException("ScreenProjector missing.");
        backend.MousePosition = projector.WorldToScreen(
            WorldUnitsFix64.WorldCmToVisualMeters(Fix64Vec2.FromFloat(worldCm.X, worldCm.Y)));
    }

    private static void Press(GameEngine engine, TestInputBackend backend, string path)
    {
        backend.SetButton(path, true);
        Tick(engine, 2);
        backend.SetButton(path, false);
        Tick(engine, 2);
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
        }
    }

    private static float ReadHealth(World world, Entity entity)
    {
        int id = AttributeRegistry.GetId("Health");
        return world.Get<AttributeBuffer>(entity).GetCurrent(id);
    }

    private static string Diagnostics(GameEngine engine)
    {
        return "errors=" + string.Join(" | ", engine.TriggerManager.Errors);
    }

    private static Entity FindEntity(World world, string entityName)
    {
        Entity result = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity entity, ref Name name) =>
        {
            if (result == Entity.Null && string.Equals(name.Value, entityName, StringComparison.Ordinal))
            {
                result = entity;
            }
        });

        return result != Entity.Null
            ? result
            : throw new InvalidOperationException($"Missing entity '{entityName}'.");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "mods")) &&
                File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root.");
    }

    private sealed class CentimeterScreenProjector : IScreenProjector
    {
        public Vector2 WorldToScreen(Vector3 worldPosition) =>
            new(960f + worldPosition.X * 100f, 540f + worldPosition.Z * 100f);
    }

    private sealed class CentimeterScreenRayProvider : IScreenRayProvider
    {
        public ScreenRay GetRay(Vector2 screenPosition) =>
            new(new Vector3((screenPosition.X - 960f) / 100f, 10f, (screenPosition.Y - 540f) / 100f), -Vector3.UnitY);
    }

    private sealed class TestInputBackend : IInputBackend
    {
        private readonly Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);

        public Vector2 MousePosition { get; set; }

        public float GetAxis(string devicePath) => 0f;

        public bool GetButton(string devicePath) =>
            _buttons.TryGetValue(devicePath, out bool isDown) && isDown;

        public Vector2 GetMousePosition() => MousePosition;

        public float GetMouseWheel() => 0f;

        public void SetButton(string path, bool isDown) => _buttons[path] = isDown;

        public void EnableIME(bool enable)
        {
        }

        public void SetIMECandidatePosition(int x, int y)
        {
        }

        public string GetCharBuffer() => string.Empty;
    }
}
