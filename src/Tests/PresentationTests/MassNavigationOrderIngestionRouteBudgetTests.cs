using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MassNavigation.Systems;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class MassNavigationOrderIngestionRouteBudgetTests
{
    private const int MoveOrderTypeId = 182;

    [Test]
    public void OrderIngestion_UsesRuntimeCapacityForRoutePathBudget()
    {
        MassNavigationProfileRegistry.Reset();
        const string routedProfileId = "test.massNavigation.routeBudget";
        const int expectedMaxExpanded = 17;
        const int expectedMaxPoints = 7;

        using var engine = CreateEngine();
        MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
        config.ScenarioRuntime.RuntimeCapacity.RouteMaxExpandedPerRequest = expectedMaxExpanded;
        config.ScenarioRuntime.RuntimeCapacity.RouteWaypointCapacityPerAgent = expectedMaxPoints;

        var simulation = new MassNavigationSimulationRuntime(config);
        simulation.BindBoardWorld(
            new WorldSizeSpec(new WorldAabbCm(0, 0, 25_000, 25_000), 100),
            new Ludots.Core.Navigation.GraphWorld.WorldGridLoadedChunks(simulation.WorldConfig.StreamingChunkSizeCm));
        PublishPreparedRuntime(engine, config.MapId, simulation);
        RegisterMoveOrderServices(engine);

        int profileId = MassNavigationProfileRegistry.Register(routedProfileId);
        Entity agent = engine.World.Create(
            new MassNavigationAgent { ProfileId = profileId },
            new FacingDirection { AngleRad = 0f },
            OrderBuffer.CreateEmpty());
        var move = new Order
        {
            OrderId = 682,
            OrderTypeId = MoveOrderTypeId,
            Actor = agent,
            SubmitMode = OrderSubmitMode.Immediate,
            Args = MassNavigationMoveOrderArgs.Encode(new Vector2(2_500f, 2_500f)),
        };
        engine.World.Get<OrderBuffer>(agent).SetActiveDirect(in move, priority: 100);

        simulation.RebuildFromAuthoredAgents(
            engine.World,
            new[] { agent },
            new[]
            {
                new MassNavigationAgentSeed(
                    teamId: 1,
                    localPositionXCm: simulation.ToLocalXCm(1_000f),
                    localPositionYCm: simulation.ToLocalYCm(1_000f),
                    heavy: false,
                    navMass: 1f,
                    visualScale: 1f,
                    bodyRadiusCm: 20f,
                    speedCmPerSecond: 800f,
                    new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u)),
            },
            new[] { true });

        var store = new PathStore(maxPaths: 4, maxPointsPerPath: expectedMaxPoints);
        var pathService = new CapturingPathService(store);
        engine.SetService(CoreServiceKeys.PathStore, store);
        engine.SetService(CoreServiceKeys.PathService, pathService);
        engine.SetService(CoreServiceKeys.PathingConfig, CreatePathingConfig(routedProfileId));

        var ingestion = new MassNavigationOrderIngestionSystem(engine, simulation.Config);
        UpdateSystem(ingestion);
        MassNavigationRouteExecutionSink routeSink = engine.GetService(MassNavigationKeys.RouteExecutionSink)
            ?? throw new InvalidOperationException("Expected MassNavigation order ingestion to publish a route execution sink.");

        MassNavigationRouteSinkResult result = routeSink.TryApplyTrackedRouteTargets(simulation, engine.World);

        Assert.That(result.Applied, Is.True);
        Assert.That(pathService.LastMaxExpanded, Is.EqualTo(expectedMaxExpanded));
        Assert.That(pathService.LastMaxPoints, Is.EqualTo(expectedMaxPoints));
    }

    private static GameEngine CreateEngine()
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            new List<string> { Path.Combine(FindRepoRoot(), "mods", "LudotsCoreMod") },
            Path.Combine(FindRepoRoot(), "assets"));
        return engine;
    }

    private static void PublishPreparedRuntime(
        GameEngine engine,
        string mapIdText,
        MassNavigationSimulationRuntime simulation)
    {
        var mapId = new MapId(mapIdText);
        engine.SetCurrentMapSessionForTests(new MapSession(mapId, new MapConfig { Id = mapIdText }));
        var binding = new MassNavigationRuntimeBinding();
        binding.Activate(mapId, simulation);
        binding.MarkPrepared(mapId, simulation);
        engine.SetService(MassNavigationKeys.RuntimeBinding, binding);
    }

    private static void RegisterMoveOrderServices(GameEngine engine)
    {
        var orderTypes = new OrderTypeRegistry();
        orderTypes.Register(new OrderTypeConfig
        {
            Key = MassNavigationOrderKeys.Move,
            OrderTypeId = MoveOrderTypeId,
            Priority = 100,
            CanInterruptSelf = true,
        });
        var orderBufferSystem = new OrderBufferSystem(
            engine.World,
            new DiscreteClock(),
            orderTypes,
            new OrderRuleRegistry(),
            new OrderQueue(capacity: 16));
        engine.SetService(CoreServiceKeys.OrderTypeRegistry, orderTypes);
        engine.SetService(CoreServiceKeys.OrderBufferSystem, orderBufferSystem);
    }

    private static PathingConfig CreatePathingConfig(string routedProfileId)
    {
        return new PathingConfig
        {
            AgentTypes =
            {
                new PathingAgentTypeConfig
                {
                    Id = "test.massNavigation.routeBudget.agent",
                    ProfileId = routedProfileId,
                    Selection = new PathingSelectionConfig { Mode = PathSelectionMode.PreferGraph },
                },
            },
        };
    }

    private static void UpdateSystem(ISystem<float> system)
    {
        float dt = 0f;
        system.Update(in dt);
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

    private sealed class CapturingPathService : IPathService
    {
        private readonly PathStore _store;

        public CapturingPathService(PathStore store)
        {
            _store = store;
        }

        public int LastMaxExpanded { get; private set; }
        public int LastMaxPoints { get; private set; }

        public bool TrySolve(in PathRequest request, out PathResult result)
        {
            LastMaxExpanded = request.Budget.MaxExpanded;
            LastMaxPoints = request.Budget.MaxPoints;

            if (!_store.TryAllocate(2, out PathHandle handle))
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.BudgetExceeded, default, 0, 4);
                return true;
            }

            Span<int> x = stackalloc int[2];
            Span<int> y = stackalloc int[2];
            x[0] = request.Start.Xcm;
            y[0] = request.Start.Ycm;
            x[1] = request.Goal.Xcm;
            y[1] = request.Goal.Ycm;
            _store.TryWrite(in handle, x, y, 2);
            result = new PathResult(
                request.RequestId,
                request.Actor,
                PathStatus.Found,
                handle,
                expanded: 2,
                errorCode: 0,
                resolvedDomain: PathDomain.NodeGraph);
            return true;
        }

        public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
        {
            return _store.TryCopy(in handle, xcmOut, ycmOut, out count);
        }
    }
}
