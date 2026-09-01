using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arch.System;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MovePlanning;
using Ludots.Core.Scripting;
using MassNavigationMod;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class MassNavigationModEntryMapGateContractTests
{
    private const string NavigationMapId = "mass_navigation";
    private const string ParticipantMapId = "capability_standard_participant_views";

    [Test]
    public async Task InstallMovePlanOrderAdapter_ReturnsWithoutInstalling_WhenCurrentMapIsNotMassNavigation()
    {
        using GameEngine engine = CreateEngine();
        engine.SetCurrentMapSessionForTests(new MapSession(
            new MapId(ParticipantMapId),
            new MapConfig { Id = ParticipantMapId }));

        // No MassNavigation runtime binding → IsCurrentNavigationMap is false.
        var context = new ScriptContext();
        context.Set(CoreServiceKeys.Engine, engine);

        await MassNavigationModEntry.InstallMovePlanOrderAdapterAsync(context);

        Assert.That(
            engine.GlobalContext.ContainsKey(MassNavigationModEntry.MovePlanOrderAdapterInstalledKey),
            Is.False,
            "Non-MassNavigation maps must skip adapter installation without throwing.");
    }

    [Test]
    public void InstallMovePlanOrderAdapter_KeepsInsertSystemBeforeRequiredStrict_OnMassNavigationMap()
    {
        using GameEngine engine = CreateEngine();
        BindCurrentNavigationMapWithoutMovePlanAnchor(engine);
        RegisterMoveOrderType(engine);

        var context = new ScriptContext();
        context.Set(CoreServiceKeys.Engine, engine);

        InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await MassNavigationModEntry.InstallMovePlanOrderAdapterAsync(context))!;

        Assert.That(ex.Message, Does.Contain(nameof(IMovePlanCommandGroupExecutionSystem)));
        Assert.That(ex.Message, Does.Contain("anchor is missing"));
        Assert.That(
            engine.GlobalContext.ContainsKey(MassNavigationModEntry.MovePlanOrderAdapterInstalledKey),
            Is.False,
            "A missing MovePlan anchor must fail fast and must not mark the adapter installed.");
    }

    [Test]
    public async Task InstallMovePlanOrderAdapter_InstallsWhenMassNavigationMapHasMovePlanAnchor()
    {
        using GameEngine engine = CreateEngine();
        BindCurrentNavigationMapWithoutMovePlanAnchor(engine);
        RegisterMoveOrderType(engine);
        engine.RegisterSystem(new StubMovePlanCommandGroupExecutionSystem(), SystemGroup.AbilityActivation);

        var context = new ScriptContext();
        context.Set(CoreServiceKeys.Engine, engine);

        await MassNavigationModEntry.InstallMovePlanOrderAdapterAsync(context);

        Assert.That(
            engine.GlobalContext.ContainsKey(MassNavigationModEntry.MovePlanOrderAdapterInstalledKey),
            Is.True);
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

    private static void BindCurrentNavigationMapWithoutMovePlanAnchor(GameEngine engine)
    {
        MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
        config.MapId = NavigationMapId;
        var simulation = new MassNavigationSimulationRuntime(config);
        var mapId = new MapId(NavigationMapId);
        engine.SetCurrentMapSessionForTests(new MapSession(mapId, new MapConfig { Id = NavigationMapId }));

        var binding = new MassNavigationRuntimeBinding();
        binding.Activate(mapId, simulation);
        engine.SetService(MassNavigationKeys.RuntimeBinding, binding);

        Assert.That(
            MassNavigationIds.IsCurrentNavigationMap(engine),
            Is.True,
            "Test harness must establish a MassNavigation current-map binding before asserting adapter install contracts.");
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
