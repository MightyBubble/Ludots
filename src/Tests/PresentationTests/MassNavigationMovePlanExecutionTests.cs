using System.Numerics;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Layers;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MassNavigation.Systems;
using Ludots.Core.Mathematics;
using Ludots.Core.MovePlanning;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class MassNavigationMovePlanExecutionTests
{
    [Test]
    public void Execution_ConsumesTypedIntentWithoutOrderBuffer()
    {
        using var engine = new GameEngine();
        string repoRoot = FindRepoRoot();
        engine.InitializeWithConfigPipeline(
            new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") },
            Path.Combine(repoRoot, "assets"));
        MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
        var simulation = new MassNavigationSimulationRuntime(config);
        simulation.BindBoardWorld(
            new WorldSizeSpec(new WorldAabbCm(0, 0, 25_000, 25_000), 100),
            MassNavigationOrderChainTests.CreateLoadedChunksForTests(simulation));

        int profileId = MassNavigationProfileRegistry.Register("light");
        var layer = new MassNavigationAgentLayer(1u, 1u);
        Entity agent = engine.World.Create(
            new MassNavigationAgent { ProfileId = profileId },
            WorldPositionCm.FromCmFloat(1_000f, 1_000f),
            new EntityLayer(layer.CategoryMask, layer.InteractionMask),
            new FacingDirection { AngleRad = 0f });
        var seed = new MassNavigationAgentSeed(
            teamId: MassNavigationOrderChainTests.LocalTeamId,
            localPositionXCm: 1_000f,
            localPositionYCm: 1_000f,
            heavy: false,
            navMass: 1f,
            bodyRadiusCm: 20f,
            speedCmPerSecond: 800f,
            layer);
        simulation.RebuildFromAuthoredAgents(engine.World, new[] { agent }, new[] { seed }, new[] { true });
        engine.World.Set(agent, new MovePlanExecutionIntent
        {
            CommandGroupToken = 31,
            TargetWorldCm = new Vector2(2_000f, 1_500f),
            StopRadiusCm = 25f,
            HasTarget = 1,
            Mode = MovePlanExecutionMode.CommandGroup,
        });

        var mapId = new MapId(config.MapId);
        engine.SetCurrentMapSessionForTests(new MapSession(mapId, new MapConfig { Id = config.MapId }));
        var binding = new MassNavigationRuntimeBinding();
        binding.Activate(mapId, simulation);
        binding.MarkPrepared(mapId, simulation);
        engine.SetService(MassNavigationKeys.RuntimeBinding, binding);

        var system = new MassNavigationMovePlanExecutionSystem(engine, config);
        system.Update(0f);

        Assert.Multiple(() =>
        {
            Assert.That(engine.World.Has<OrderBuffer>(agent), Is.False);
            Assert.That(simulation.NavGroupRuntime.ActiveOrderGroupCount, Is.EqualTo(1));
            Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(0, out _, out _), Is.True);
            Assert.That(engine.World.Get<MovePlanExecutionResult>(agent).Kind, Is.EqualTo(MovePlanExecutionResultKind.None));
        });

        engine.SetService(CoreServiceKeys.PathStore, new PathStore(maxPaths: 4, maxPointsPerPath: 64));
        engine.SetService(CoreServiceKeys.PathService, (IPathService)new RejectingPathService());
        engine.SetService(CoreServiceKeys.PathingConfig, new PathingConfig());
        engine.World.Set(agent, new MovePlanExecutionIntent
        {
            CommandGroupToken = 32,
            TargetWorldCm = new Vector2(3_000f, 2_000f),
            HasTarget = 1,
            Mode = MovePlanExecutionMode.CommandGroup,
        });

        system.Update(0f);

        MovePlanExecutionResult failed = engine.World.Get<MovePlanExecutionResult>(agent);
        Assert.Multiple(() =>
        {
            Assert.That(simulation.NavGroupRuntime.ActiveOrderGroupCount, Is.Zero);
            Assert.That(failed.CommandGroupToken, Is.EqualTo(32));
            Assert.That(failed.Kind, Is.EqualTo(MovePlanExecutionResultKind.Failed));
            Assert.That(failed.FailureReason, Is.EqualTo(MovePlanFailureReason.ExecutionUnavailable));
        });
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

    private sealed class RejectingPathService : IPathService
    {
        public bool TrySolve(in PathRequest request, out PathResult result)
        {
            result = default;
            return false;
        }

        public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
        {
            count = 0;
            return false;
        }
    }
}
