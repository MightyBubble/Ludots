using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using DynamicNavBakeShowcaseMod;
using DynamicNavBakeShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.NavBake.Recast;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

/// <summary>
/// Shared production-path helpers for Dynamic NavBake showcase acceptance and performance evidence.
/// </summary>
internal static class DynamicNavBakeShowcaseAcceptanceHarness
{
    public const float DeltaTime = 1f / 60f;

    public static readonly string[] SharedMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod",
        "MassNavigationMod",
        "DynamicNavBakeShowcaseMod",
    };

    public static GameEngine CreateEngine(string sceneModId, bool registerRecast, bool installUi = false)
    {
        string repoRoot = FindRepoRoot();
        var mods = new List<string>(SharedMods) { sceneModId };
        List<string> modPaths = RepoModPaths.ResolveExplicit(repoRoot, mods);
        var engine = new GameEngine();
        if (registerRecast)
        {
            // Test host composition only — production showcase never news RecastNavBakeAlgorithm.
            engine.RegisterExternalNavBakeAdapters(new RecastNavBakeAlgorithm());
        }

        engine.InitializeWithConfigPipeline(modPaths, Path.Combine(repoRoot, "assets"));
        InstallInput(engine);
        engine.RegisterPresentationAdapterCapabilities(
            new PresentationAdapterCapabilities(PresentationVisualCapabilities.NavMeshTileGeometry));
        if (installUi)
        {
            AcceptanceUiHostInstaller.Install(engine);
        }

        engine.Start();
        return engine;
    }

    public static DynamicNavBakeShowcaseActions WaitForActions(GameEngine engine, string mapId)
    {
        for (int i = 0; i < 30; i++)
        {
            if (engine.GlobalContext.TryGetValue(DynamicNavBakeShowcaseIds.RuntimeServiceKey, out object? value) &&
                value is DynamicNavBakeShowcaseActions actions)
            {
                return actions;
            }

            engine.Tick(DeltaTime);
        }

        throw new InvalidOperationException($"DynamicNavBakeShowcaseActions was not registered for map '{mapId}'.");
    }

    public static void EnsureCaptureViewController(GameEngine engine, DynamicNavBakeShowcaseActions actions)
    {
        if (engine.TryGetService(CoreServiceKeys.ViewController, out IViewController _))
        {
            return;
        }

        DynamicNavBakeShowcasePlayerFramingConfig framing = actions.ActiveConfig.RaylibAutoTimeline.PlayerFraming;
        engine.SetService(
            CoreServiceKeys.ViewController,
            (IViewController)new FixedCaptureViewController(
                framing.CaptureWidthPx,
                framing.CaptureHeightPx,
                engine.GameSession.Camera.State.FovYDeg));
    }

    public static void DrainSpawnAndNavBootstrap(GameEngine engine, DynamicNavBakeShowcaseActions actions)
    {
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)!;
        float fixedDt = Time.FixedDeltaTime;
        if (fixedDt <= 0f)
        {
            throw new InvalidOperationException(
                $"DynamicNavBake bootstrap drain requires Time.FixedDeltaTime > 0; got {fixedDt}.");
        }

        // Spawn + obstacle bridge + dirty capture all run on FixedStep. Tick(1/60) with
        // FixedDeltaTime=0.02 only sometimes advances simulation and can leave structural
        // first-capture pending when DrainUntilIdle first observes Idle.
        for (int i = 0; i < 240 && spawnQueue.Count > 0; i++)
        {
            engine.Tick(fixedDt);
        }

        Assert.That(spawnQueue.Count, Is.EqualTo(0));
        actions.DrainUntilIdle(engine, maxTicks: 8192);
    }

    public static void AssertAlgorithmSwitch(GameEngine engine, DynamicNavBakeShowcaseActions actions, NavBakeAlgorithmKind algorithm)
    {
        RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)!;
        if (queue.CurrentAlgorithm == algorithm && !queue.HasRequestedAlgorithm && queue.Status == RuntimeNavMeshRebuildStatus.Idle)
        {
            return;
        }

        if (!actions.TrySwitchAlgorithm(engine, algorithm, out string error))
        {
            Assert.Fail($"Algorithm switch failed for '{NavBakeNames.FormatAlgorithm(algorithm)}': {error}");
        }

        actions.DrainUntilIdle(engine, maxTicks: 8192);
        Assert.That(queue.CurrentAlgorithm, Is.EqualTo(algorithm));
        Assert.That(queue.HasRequestedAlgorithm, Is.False);
    }

    public static RuntimeNavMeshTelemetryService RequireTelemetry(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.RuntimeNavMeshTelemetry)
            ?? throw new InvalidOperationException("RuntimeNavMeshTelemetry is required for DynamicNavBake evidence epochs.");
    }

    public static void BeginEvidenceEpoch(GameEngine engine)
    {
        RuntimeNavMeshTelemetryService telemetry = RequireTelemetry(engine);
        if (telemetry.HasOpenGeneration)
        {
            throw new InvalidOperationException(
                "Cannot begin a DynamicNavBake evidence epoch while a dirty generation is still open.");
        }

        telemetry.ResetSamples();
    }

    public static void InstallInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline!).Load();
        var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.AuthoritativeInput, inputHandler);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    public static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Ludots.sln")) || File.Exists(Path.Combine(dir, "showcase.registry.json")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }

    private sealed class NullInputBackend : IInputBackend
    {
        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => false;
        public System.Numerics.Vector2 GetMousePosition() => System.Numerics.Vector2.Zero;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }

    private sealed class FixedCaptureViewController : IViewController
    {
        public FixedCaptureViewController(int widthPx, int heightPx, float fov)
        {
            Resolution = new Vector2(widthPx, heightPx);
            Fov = fov;
            AspectRatio = (float)widthPx / heightPx;
        }

        public Vector2 Resolution { get; }
        public float Fov { get; }
        public float AspectRatio { get; }
    }
}
