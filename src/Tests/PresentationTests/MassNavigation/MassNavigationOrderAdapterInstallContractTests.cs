using System;
using System.Collections.Generic;
using System.IO;
using Arch.System;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MovePlanning;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class MassNavigationOrderAdapterInstallContractTests
{
    private const string ParticipantMapId = "capability_standard_participant_views";

    [Test]
    public void InstallMovePlanOrderAdapter_FailsWithTypeError_WhenMoveOrderTypeIsMissing()
    {
        using GameEngine engine = CreateEngine();
        engine.RegisterSystem(new StubMovePlanCommandGroupExecutionSystem(), SystemGroup.AbilityActivation);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => MassNavigationRuntime.InstallMovePlanOrderAdapter(engine))!;

        Assert.That(ex.Message, Does.Contain(MassNavigationOrderKeys.Move));
        Assert.That(
            CountSystems<MovePlanOrderProjectionSystem>(engine, SystemGroup.AbilityActivation),
            Is.Zero,
            "A missing move order type must fail fast and must not install the adapter.");
    }

    [Test]
    public void InstallMovePlanOrderAdapter_InsertsProjectionBeforeAnchorAndLifecycleAfter()
    {
        using GameEngine engine = CreateEngine();
        RegisterMoveOrderType(engine);
        engine.RegisterSystem(new StubMovePlanCommandGroupExecutionSystem(), SystemGroup.AbilityActivation);

        MassNavigationRuntime.InstallMovePlanOrderAdapter(engine);

        var group = GetSystems(engine, SystemGroup.AbilityActivation);
        int anchorIndex = FindIndex<StubMovePlanCommandGroupExecutionSystem>(group);
        int projectionIndex = FindIndex<MovePlanOrderProjectionSystem>(group);
        int lifecycleIndex = FindIndex<MovePlanOrderLifecycleSystem>(group);
        Assert.That(anchorIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(projectionIndex, Is.EqualTo(anchorIndex - 1),
            "Projection must run directly before the MovePlan execution anchor.");
        Assert.That(lifecycleIndex, Is.GreaterThan(anchorIndex),
            "Lifecycle completion must run after the MovePlan execution anchor.");
    }

    [Test]
    public void HandleMapFocused_NonNavigationMap_DoesNotInstallAdapter()
    {
        using GameEngine engine = CreateEngine();
        RegisterMoveOrderType(engine);
        var runtime = new MassNavigationRuntime();
        engine.SetCurrentMapSessionForTests(new MapSession(
            new MapId(ParticipantMapId),
            new MapConfig { Id = ParticipantMapId }));

        bool activated = runtime.HandleMapFocused(engine, new MapId(ParticipantMapId));

        Assert.That(activated, Is.False,
            "Maps without a matching MassNavigationConfig.mapId must not activate the mass-navigation runtime.");
        Assert.That(CountSystems<MovePlanOrderProjectionSystem>(engine, SystemGroup.AbilityActivation), Is.Zero);
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") },
            Path.Combine(repoRoot, "assets"));
        return engine;
    }

    private static void RegisterMoveOrderType(GameEngine engine)
    {
        OrderTypeRegistry orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("OrderTypeRegistry missing after LudotsCoreMod initialize.");
        if (orderTypes.TryGetId(MassNavigationOrderKeys.Move, out _))
        {
            return;
        }

        orderTypes.Register(new OrderTypeConfig
        {
            Key = MassNavigationOrderKeys.Move,
            OrderTypeId = 17,
            Priority = 100,
            SameTypePolicy = SameTypePolicy.Replace,
            CanInterruptSelf = true,
        });
    }

    private static List<ISystem<float>> GetSystems(GameEngine engine, SystemGroup group)
    {
        var field = typeof(GameEngine).GetField("_systemGroups", ReflectionBindingFlags)!;
        var groups = (Dictionary<SystemGroup, List<ISystem<float>>>)field.GetValue(engine)!;
        return groups.TryGetValue(group, out var systems) ? systems : new List<ISystem<float>>();
    }

    private static int CountSystems<T>(GameEngine engine, SystemGroup group)
    {
        var systems = GetSystems(engine, group);
        int count = 0;
        for (int i = 0; i < systems.Count; i++)
        {
            if (systems[i] is T)
            {
                count++;
            }
        }

        return count;
    }

    private static int FindIndex<T>(List<ISystem<float>> systems)
    {
        for (int i = 0; i < systems.Count; i++)
        {
            if (systems[i] is T)
            {
                return i;
            }
        }

        return -1;
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

            DirectoryInfo? parent = Directory.GetParent(current);
            current = parent?.FullName ?? string.Empty;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root.");
    }

    private const System.Reflection.BindingFlags ReflectionBindingFlags =
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

    private sealed class StubMovePlanCommandGroupExecutionSystem : ISystem<float>, IMovePlanCommandGroupExecutionSystem
    {
        public void Initialize()
        {
        }

        public void BeforeUpdate(in float deltaTime)
        {
        }

        public void Update(in float deltaTime)
        {
        }

        public void AfterUpdate(in float deltaTime)
        {
        }

        public void Dispose()
        {
        }
    }
}
