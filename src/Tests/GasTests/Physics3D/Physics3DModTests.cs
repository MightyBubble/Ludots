using System;
using System.IO;
using Ludots.Core.Engine;
using Ludots.Core.Physics3D;
using Ludots.Core.Scripting;
using NUnit.Framework;
using Physics3DMod;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class Physics3DModTests
{
    [Test]
    public void Runtime_InstallsIdempotentlyAndUnloadsOwnedState()
    {
        using GameEngine engine = CreateEngine();
        var context = new ScriptContext();
        context.Set(CoreServiceKeys.Engine, engine);
        var runtime = new Physics3DRuntime();

        runtime.EnsureInstalledAsync(context).GetAwaiter().GetResult();
        IPhysics3DWorld world = engine.GetService(Physics3DServiceKeys.World);
        Physics3DSimulationSystem system = engine.GetService(Physics3DServiceKeys.SimulationSystem);
        runtime.EnsureInstalledAsync(context).GetAwaiter().GetResult();

        Assert.That(engine.GetService(Physics3DServiceKeys.World), Is.SameAs(world));
        Assert.That(engine.GetService(Physics3DServiceKeys.SimulationSystem), Is.SameAs(system));

        runtime.Dispose();
        Assert.That(engine.TryGetService(Physics3DServiceKeys.World, out _), Is.False);
        Assert.That(engine.TryGetService(Physics3DServiceKeys.SimulationSystem, out _), Is.False);
    }

    [Test]
    public void Runtime_RejectsExistingServiceWithoutOverwritingIt()
    {
        using GameEngine engine = CreateEngine();
        var context = new ScriptContext();
        context.Set(CoreServiceKeys.Engine, engine);
        using var existing = new Physics3DWorld(Physics3DWorldTests.CreateConfig(1, 0, workerCount: 1));
        engine.SetService(Physics3DServiceKeys.World, (IPhysics3DWorld)existing);
        var runtime = new Physics3DRuntime();

        Assert.Throws<InvalidOperationException>(() =>
            runtime.EnsureInstalledAsync(context).GetAwaiter().GetResult());
        Assert.That(engine.GetService(Physics3DServiceKeys.World), Is.SameAs(existing));

        runtime.Dispose();
        Assert.That(engine.RemoveService(Physics3DServiceKeys.World), Is.True);
    }

    [Test]
    public void Runtime_RefusesToUnloadStateItNoLongerOwns()
    {
        using GameEngine engine = CreateEngine();
        var context = new ScriptContext();
        context.Set(CoreServiceKeys.Engine, engine);
        var runtime = new Physics3DRuntime();
        runtime.EnsureInstalledAsync(context).GetAwaiter().GetResult();
        IPhysics3DWorld ownedWorld = engine.GetService(Physics3DServiceKeys.World);
        using var replacement = new Physics3DWorld(Physics3DWorldTests.CreateConfig(1, 0, workerCount: 1));
        engine.SetService(Physics3DServiceKeys.World, (IPhysics3DWorld)replacement);

        Assert.Throws<InvalidOperationException>(() => runtime.Dispose());
        Assert.That(engine.GetService(Physics3DServiceKeys.World), Is.SameAs(replacement));

        engine.SetService(Physics3DServiceKeys.World, ownedWorld);
        runtime.Dispose();
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "Physics3DMod" }),
            Path.Combine(repoRoot, "assets"));
        return engine;
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from the Physics3D test output directory.");
    }
}
