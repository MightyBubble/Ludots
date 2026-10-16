using System.Reflection;
using Arch.Core;
using Arch.System;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using Ludots.Tests;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// HandleMapFocused 先把会话步进插在空间分区之前,再把输入插在会话步进之前。
/// 输入 Update 提交的现场指令必须在同一次引擎步进里执行。
/// </summary>
public sealed class SessionTickInputOrderTests
{
    [Test]
    public void InputSubmit_ExecutesInTheSameEngineStepAsTheSessionTick()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        using var engine = new GameEngine();
        BindHeadlessSession(engine.CrowdRuntime, session);

        var spatialWorld = World.Create();
        try
        {
            var spatial = new SpatialPartitionUpdateSystem(
                spatialWorld,
                new GridSpatialPartitionWorld(100),
                new WorldSizeSpec(new WorldAabbCm(0, 0, 1000, 1000), 100));
            engine.RegisterSystem(spatial, SystemGroup.PostMovement);
            engine.InsertSystemBeforeRequired<SpatialPartitionUpdateSystem>(
                new CrowdSimulationSessionTickSystem(engine.CrowdRuntime),
                SystemGroup.PostMovement);
            engine.InsertSystemBeforeRequired<CrowdSimulationSessionTickSystem>(
                new SameStepSpawnInput(session),
                SystemGroup.PostMovement);

            var groups = (Dictionary<SystemGroup, List<ISystem<float>>>)typeof(GameEngine)
                .GetField("_systemGroups", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(engine)!;
            Assert.That(new PhaseOrderedCooperativeSimulation(groups).Step(1f / 60f, 1000), Is.True);
        }
        finally
        {
            World.Destroy(spatialWorld);
        }

        Assert.That(session.TickCount, Is.EqualTo(1));
        Assert.That(session.Units.Count, Is.GreaterThan(0));
        Assert.That(session.Commands.Log, Has.Count.EqualTo(1));
        Assert.That(session.Commands.Log[0].Tick, Is.EqualTo(0),
            "输入若排在会话步进之后,现场指令会记在已经推进的 tick 上。");
        Assert.That(session.Commands.PendingCount, Is.EqualTo(0));
    }

    private static void BindHeadlessSession(CrowdSimulationRuntime runtime, CrowdSimSession session)
    {
        var relations = TeamRelationTestHarness.Create(session.World);
        var rules = CrowdPairRules.Matrices(1, new CrowdPushResponse[1], new bool[1]);
        var guard = new CrowdPairRulesSessionGuard(
            relations.Query,
            session.World,
            Array.Empty<Entity>(),
            Array.Empty<int>(),
            Array.Empty<int>(),
            relations.HostileTypeId,
            relations.FriendlyTypeId,
            rules,
            relations.Query.Revision);
        Set(runtime, "_session", session);
        Set(runtime, "_pairRulesGuard", guard);
    }

    private static void Set(CrowdSimulationRuntime runtime, string field, object value)
    {
        typeof(CrowdSimulationRuntime)
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(runtime, value);
    }

    private sealed class SameStepSpawnInput : ISystem<float>
    {
        private readonly CrowdSimSession _session;

        public SameStepSpawnInput(CrowdSimSession session) => _session = session;

        public void Initialize() { }

        public void BeforeUpdate(in float t) { }

        public void Update(in float dt)
        {
            _session.Commands.Submit<object?>(
                _session,
                new CrowdSimCommand { Kind = CrowdSimCommandKind.Spawn, Count = 1 },
                CrowdSimCommands.Exec);
        }

        public void AfterUpdate(in float t) { }

        public void Dispose() { }
    }
}
