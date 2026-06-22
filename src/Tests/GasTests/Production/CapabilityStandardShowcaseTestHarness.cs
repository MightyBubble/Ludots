using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

internal static class CapabilityStandardShowcaseTestHarness
{
    public const float DeltaTime = 1f / 60f;

    public static GameEngine CreateEngine(
        string repoRoot,
        IReadOnlyList<string> modIds,
        IInputBackend? inputBackend = null)
    {
        string assetsRoot = Path.Combine(repoRoot, "assets");
        List<string> modPaths = RepoModPaths.ResolveExplicit(repoRoot, modIds);

        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
        InstallInput(engine, inputBackend ?? new NullInputBackend());
        engine.Start();
        return engine;
    }

    public static void TickMeasured(GameEngine engine, int frames, List<double> frameTimesMs)
    {
        for (int i = 0; i < frames; i++)
        {
            long start = Stopwatch.GetTimestamp();
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
            frameTimesMs.Add((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
        }
    }

    public static void TickUntil(GameEngine engine, List<double> frameTimesMs, Func<bool> predicate, int maxFrames)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (predicate())
            {
                return;
            }

            TickMeasured(engine, 1, frameTimesMs);
        }

        Assert.That(predicate(), Is.True, $"Predicate was not satisfied within {maxFrames} frames.");
    }

    public static Entity FindSingleByTemplate(GameEngine engine, string mapId, string templateId)
    {
        EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("EntityTemplateKeyRegistry missing.");
        int templateKeyId = templateKeys.GetId(templateId);
        Assert.That(templateKeyId, Is.GreaterThan(0), $"Template '{templateId}' should be registered.");

        Entity found = Entity.Null;
        int count = 0;
        var expectedMap = new MapId(mapId);
        var query = new QueryDescription().WithAll<EntityTemplateKeyRef, MapEntity>();
        engine.World.Query(in query, (Entity entity, ref EntityTemplateKeyRef keyRef, ref MapEntity mapEntity) =>
        {
            if (keyRef.TemplateKeyId == templateKeyId && mapEntity.MapId == expectedMap)
            {
                found = entity;
                count++;
            }
        });

        Assert.That(count, Is.EqualTo(1), $"Expected exactly one entity for template '{templateId}'.");
        return found;
    }

    public static Physics2DPerfStats ReadPhysicsPerfStats(World world)
    {
        var query = new QueryDescription().WithAll<Physics2DPerfStats>();
        Physics2DPerfStats stats = default;
        bool found = false;
        world.Query(in query, (Entity _, ref Physics2DPerfStats value) =>
        {
            if (found)
            {
                return;
            }

            stats = value;
            found = true;
        });

        Assert.That(found, Is.True, "Physics2DPerfStats should be published by the production physics system.");
        return stats;
    }

    public static T FindSystem<T>(GameEngine engine, SystemGroup group)
        where T : class, ISystem<float>
    {
        var field = typeof(GameEngine).GetField("_systemGroups", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);

        var systemGroups = field!.GetValue(engine) as Dictionary<SystemGroup, List<ISystem<float>>>;
        Assert.That(systemGroups, Is.Not.Null);
        Assert.That(systemGroups!.TryGetValue(group, out List<ISystem<float>>? systems), Is.True);

        for (int i = 0; i < systems!.Count; i++)
        {
            if (systems[i] is T typed)
            {
                return typed;
            }
        }

        throw new InvalidOperationException($"System '{typeof(T).Name}' was not registered in group '{group}'.");
    }

    public static string FindRepoRoot()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            var candidate = Path.Combine(dir, "src", "Core", "Ludots.Core.csproj");
            if (File.Exists(candidate))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not locate repo root.");
    }

    private static void InstallInput(GameEngine engine, IInputBackend inputBackend)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputHandler = new PlayerInputHandler(inputBackend, inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    public sealed class TestInputBackend : IInputBackend
    {
        private readonly HashSet<string> _buttons = new(StringComparer.Ordinal);

        public Vector2 MousePosition { get; set; }
        public float MouseWheel { get; set; }

        public float GetAxis(string devicePath) => 0f;

        public bool GetButton(string devicePath) => _buttons.Contains(devicePath);

        public Vector2 GetMousePosition() => MousePosition;

        public float GetMouseWheel()
        {
            float value = MouseWheel;
            MouseWheel = 0f;
            return value;
        }

        public void EnableIME(bool enable) { }

        public void SetIMECandidatePosition(int x, int y) { }

        public string GetCharBuffer() => string.Empty;

        public void SetButton(string devicePath, bool down)
        {
            if (down)
            {
                _buttons.Add(devicePath);
            }
            else
            {
                _buttons.Remove(devicePath);
            }
        }
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
