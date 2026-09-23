using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using Ludots.UI.Browser;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
public sealed class RtsStarCraftLikeShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "rts_starcraft_like";

    private static readonly string[] AcceptanceMods =
    [
        "LudotsCoreMod",
        "CoreInputMod",
        "EntityCommandPanelMod",
        "RtsDemoMod",
        "BrowserRtsProductionShowcaseMod",
        "RtsStarCraftLikeShowcaseMod"
    ];

    [Test]
    public void StarCraftLikeMap_LoadsHundredUnitRoster_AndInstallsItemGraphGasContent()
    {
        var frameTimesMs = new List<double>();
        using var engine = CreateEngine();
        LoadMap(engine, frameTimesMs);

        var roster = SnapshotMapRoster(engine.World);
        Assert.That(roster.Count, Is.EqualTo(100), "The StarCraft-like map should expose 100 visible unit/structure types.");
        Assert.That(roster.Select(row => row.TeamId).Distinct().Order().ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(roster.Count(row => row.TeamId == 1), Is.EqualTo(34));
        Assert.That(roster.Count(row => row.TeamId == 2), Is.EqualTo(33));
        Assert.That(roster.Count(row => row.TeamId == 3), Is.EqualTo(33));

        Assert.That(FindEntity(engine.World, "Terran Command Center"), Is.Not.EqualTo(Entity.Null));
        Assert.That(FindEntity(engine.World, "Zerg Hatchery"), Is.Not.EqualTo(Entity.Null));
        Assert.That(FindEntity(engine.World, "Protoss Nexus"), Is.Not.EqualTo(Entity.Null));

        AssertRegisteredAbility(engine, "Ability.Rts.StarCraft.Terran.TrainMarine");
        AssertRegisteredAbility(engine, "Ability.Rts.StarCraft.Zerg.CreepSurge");
        AssertRegisteredAbility(engine, "Ability.Rts.StarCraft.Protoss.ChronoBoost");
        Assert.That(GraphIdRegistry.GetId("Graph.Rts.StarCraft.Terran.ScanPulse"), Is.GreaterThan(0));
        Assert.That(GraphIdRegistry.GetId("Graph.Rts.StarCraft.Zerg.CreepSurge"), Is.GreaterThan(0));
        Assert.That(GraphIdRegistry.GetId("Graph.Rts.StarCraft.Protoss.ChronoBoost"), Is.GreaterThan(0));

        var itemDefinitions = engine.GetService(CoreServiceKeys.ItemDefinitionRegistry)
            ?? throw new InvalidOperationException("ItemDefinitionRegistry missing.");
        Assert.That(itemDefinitions.GetId("rts_sc_item_terran_stimpack"), Is.GreaterThan(0));
        Assert.That(itemDefinitions.GetId("rts_sc_item_zerg_adrenal_glands"), Is.GreaterThan(0));
        Assert.That(itemDefinitions.GetId("rts_sc_item_protoss_psi_matrix"), Is.GreaterThan(0));
        Assert.That(CountComponents<ItemContainerCm>(engine.World), Is.GreaterThanOrEqualTo(3));
        Assert.That(CountComponents<ItemInstanceCm>(engine.World), Is.GreaterThanOrEqualTo(3));

        Assert.That(
            SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity selected),
            Is.True);
        Assert.That(engine.World.Get<Name>(selected).Value, Is.EqualTo("Terran Command Center"));
    }

    [Test]
    public void TerranProductionAbility_QueuesThroughOrderGAS_AndSpawnsMarine()
    {
        var frameTimesMs = new List<double>();
        using var engine = CreateEngine();
        LoadMap(engine, frameTimesMs);

        Entity commandCenter = FindEntity(engine.World, "Terran Command Center");
        int mineralsId = EnsureAttribute("Minerals");
        float mineralsBefore = ReadAttribute(engine.World, commandCenter, mineralsId);
        int marinesBefore = CountEntitiesByName(engine.World, "Terran Marine");

        CastAbility(engine, commandCenter, commandCenter, slot: 0);
        for (int i = 0; i < 240 && CountEntitiesByName(engine.World, "Terran Marine") != marinesBefore + 1; i++)
        {
            Tick(engine, 1, frameTimesMs);
        }

        Assert.That(
            CountEntitiesByName(engine.World, "Terran Marine"),
            Is.EqualTo(marinesBefore + 1),
            "Terran Command Center should train a Marine through the normal castAbility order path. " +
            DescribeProducerState(engine, commandCenter, mineralsId));

        Assert.That(ReadAttribute(engine.World, commandCenter, mineralsId), Is.EqualTo(mineralsBefore - 50f).Within(0.01f));
    }

    [Test]
    public void GraphCommandAndItemGrantedSlot_AreLiveOnHeadquarters()
    {
        var frameTimesMs = new List<double>();
        using var engine = CreateEngine();
        LoadMap(engine, frameTimesMs);

        Entity commandCenter = FindEntity(engine.World, "Terran Command Center");
        Tick(engine, 4, frameTimesMs);

        Assert.That(engine.World.Has<ItemGrantedSlotBuffer>(commandCenter), Is.True, "Race doctrine item should grant a command slot.");
        ItemGrantedSlotBuffer grantedSlots = engine.World.Get<ItemGrantedSlotBuffer>(commandCenter);
        Assert.That(grantedSlots.HasOverride(4), Is.True);

        int energyId = EnsureAttribute("Energy");
        float energyBefore = ReadAttribute(engine.World, commandCenter, energyId);
        CastAbility(engine, commandCenter, commandCenter, slot: 2);
        Tick(engine, 8, frameTimesMs);

        Assert.That(ReadAttribute(engine.World, commandCenter, energyId), Is.EqualTo(energyBefore - 25f).Within(0.01f));
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        string assetsRoot = Path.Combine(repoRoot, "assets");
        var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
        InstallDummyInput(engine);
        AcceptanceUiHostInstaller.Install(engine);
        InstallBrowserRuntime(engine);
        engine.Start();
        return engine;
    }

    private static void InstallDummyInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    private static void InstallBrowserRuntime(GameEngine engine)
    {
        var key = new ServiceKey<IBrowserRuntime>(BrowserRuntimeServiceNames.BrowserRuntime);
        engine.SetService(key, new TestBrowserRuntime());
    }

    private static void LoadMap(GameEngine engine, List<double> frameTimesMs)
    {
        engine.LoadMap(MapId);
        Tick(engine, 8, frameTimesMs);
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
    }

    private static void CastAbility(GameEngine engine, Entity actor, Entity target, int slot)
    {
        var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue) as OrderQueue
            ?? throw new InvalidOperationException("OrderQueue service is missing.");

        bool enqueued = orderQueue.TryEnqueue(new Order
        {
            OrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["castAbility"],
            PlayerId = 1,
            Actor = actor,
            Target = target,
            Args = new OrderArgs { I0 = slot },
            SubmitMode = OrderSubmitMode.Immediate
        });

        Assert.That(enqueued, Is.True, "Ability order should enqueue.");
    }

    private static void TickUntil(GameEngine engine, List<double> frameTimesMs, Func<bool> condition, int maxFrames, string because)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (condition())
            {
                return;
            }

            Tick(engine, 1, frameTimesMs);
        }

        Assert.That(condition(), Is.True, because);
    }

    private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
    {
        var stepPolicy = engine.GetService(CoreServiceKeys.GasClockStepPolicy);
        for (int i = 0; i < frames; i++)
        {
            if (stepPolicy.Mode == GasStepMode.Manual)
            {
                stepPolicy.RequestStep(1);
            }

            var stopwatch = Stopwatch.StartNew();
            engine.Tick(DeltaTime);
            stopwatch.Stop();
            frameTimesMs.Add(stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static void AssertRegisteredAbility(GameEngine engine, string abilityId)
    {
        int id = AbilityIdRegistry.GetId(abilityId);
        Assert.That(id, Is.GreaterThan(0), $"{abilityId} should be registered.");
        var abilities = engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry)
            ?? throw new InvalidOperationException("AbilityDefinitionRegistry missing.");
        Assert.That(abilities.TryGet(id, out _), Is.True, $"{abilityId} should compile into AbilityDefinitionRegistry.");
    }

    private static List<(string Name, int TeamId)> SnapshotMapRoster(World world)
    {
        var rows = new List<(string Name, int TeamId)>(128);
        var query = new QueryDescription().WithAll<Name, Team, MapEntity>();
        world.Query(in query, (ref Name name, ref Team team, ref MapEntity _) =>
        {
            if (!string.IsNullOrWhiteSpace(name.Value))
            {
                rows.Add((name.Value, team.Id));
            }
        });

        return rows;
    }

    private static Entity FindEntity(World world, string nameToFind)
    {
        Entity result = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity entity, ref Name name) =>
        {
            if (result == Entity.Null && string.Equals(name.Value, nameToFind, StringComparison.OrdinalIgnoreCase))
            {
                result = entity;
            }
        });

        return result;
    }

    private static int CountEntitiesByName(World world, string nameToFind)
    {
        int count = 0;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (ref Name name) =>
        {
            if (string.Equals(name.Value, nameToFind, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        });

        return count;
    }

    private static int CountComponents<T>(World world)
    {
        int count = 0;
        var query = new QueryDescription().WithAll<T>();
        world.Query(in query, (Entity _) => count++);
        return count;
    }

    private static int EnsureAttribute(string attributeName)
    {
        int id = AttributeRegistry.GetId(attributeName);
        return id > 0 ? id : AttributeRegistry.Register(attributeName);
    }

    private static float ReadAttribute(World world, Entity entity, int attributeId)
    {
        Assert.That(world.IsAlive(entity), Is.True);
        Assert.That(world.Has<AttributeBuffer>(entity), Is.True);
        AttributeBuffer attributes = world.Get<AttributeBuffer>(entity);
        return attributes.HasAttribute(attributeId) ? attributes.GetCurrent(attributeId) : 0f;
    }

    private static string DescribeProducerState(GameEngine engine, Entity producer, int resourceAttributeId)
    {
        var parts = new List<string>
        {
            $"resource={ReadAttribute(engine.World, producer, resourceAttributeId):0.##}",
            $"marineCount={CountEntitiesByName(engine.World, "Terran Marine")}"
        };

        if (engine.World.Has<AbilityStateBuffer>(producer))
        {
            ref readonly var abilities = ref engine.World.Get<AbilityStateBuffer>(producer);
            parts.Add($"abilityCount={abilities.Count}");
            for (int i = 0; i < abilities.Count; i++)
            {
                AbilitySlotState slot = abilities.Get(i);
                parts.Add($"slot{i}={AbilityIdRegistry.GetName(slot.AbilityId)}#{slot.AbilityId}");
            }
        }

        if (engine.World.Has<OrderBuffer>(producer))
        {
            ref readonly var orders = ref engine.World.Get<OrderBuffer>(producer);
            parts.Add($"orders(active={orders.HasActive},queued={orders.QueuedCount},pending={orders.HasPending})");
            if (orders.HasActive)
            {
                parts.Add($"activeOrder(type={orders.ActiveOrder.Order.OrderTypeId},slot={orders.ActiveOrder.Order.Args.I0})");
            }
        }

        if (engine.World.Has<AbilityExecInstance>(producer))
        {
            ref readonly var exec = ref engine.World.Get<AbilityExecInstance>(producer);
            parts.Add($"exec(state={exec.State},slot={exec.AbilitySlot},ability={AbilityIdRegistry.GetName(exec.AbilityId)}#{exec.AbilityId},tick={exec.CurrentTick},next={exec.NextItemIndex})");
        }
        else
        {
            parts.Add("exec=none");
        }

        RuntimeEntitySpawnQueue? spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue);
        parts.Add($"spawnQueue={spawnQueue?.Count ?? -1}");
        parts.Add($"triggerErrors={engine.TriggerManager.Errors.Count}");
        return string.Join("; ", parts);
    }

    private static string FindRepoRoot()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            string candidate = Path.Combine(dir, "src", "Core", "Ludots.Core.csproj");
            if (File.Exists(candidate))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not locate repository root.");
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

    private sealed class TestBrowserRuntime : IBrowserRuntime
    {
        public BrowserRuntimeInfo Info { get; } = new(
            BrowserEngineKind.Ultralight,
            "AcceptanceTest",
            "1.0",
            BrowserEngineCapabilities.JavaScript |
            BrowserEngineCapabilities.Dom |
            BrowserEngineCapabilities.Css |
            BrowserEngineCapabilities.OffscreenRendering |
            BrowserEngineCapabilities.TransparentBackground |
            BrowserEngineCapabilities.LocalResourceResolver |
            BrowserEngineCapabilities.LightweightGameUi);

        public ValueTask<IBrowserSurface> CreateSurfaceAsync(
            BrowserViewport viewport,
            IBrowserResourceResolver? resourceResolver = null,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IBrowserSurface>(new TestBrowserSurface(viewport));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestBrowserSurface : IBrowserSurface
    {
        private BrowserFrame _frame;

        public TestBrowserSurface(BrowserViewport viewport)
        {
            Id = BrowserSurfaceId.New();
            Viewport = viewport;
            Messages = new TestBrowserMessageBridge();
            _frame = CreateTransparentFrame(viewport);
        }

        public event EventHandler<BrowserFrameReadyEventArgs>? FrameReady;

        public BrowserSurfaceId Id { get; }

        public BrowserViewport Viewport { get; private set; }

        public IBrowserMessageBridge Messages { get; }

        public ValueTask NavigateAsync(BrowserNavigationRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return ValueTask.CompletedTask;
        }

        public ValueTask ResizeAsync(BrowserViewport viewport, CancellationToken cancellationToken = default)
        {
            Viewport = viewport;
            _frame = CreateTransparentFrame(viewport);
            FrameReady?.Invoke(this, new BrowserFrameReadyEventArgs(
                viewport,
                _frame.PixelFormat,
                _frame.DirtyRects,
                _frame.Sequence));
            return ValueTask.CompletedTask;
        }

        public ValueTask SendInputAsync(BrowserInputEvent inputEvent, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(inputEvent);
            return ValueTask.CompletedTask;
        }

        public BrowserFrame? TryGetLatestFrame() => _frame;

        public bool TryReadLatestFrame<TState>(TState state, BrowserFrameReadAction<TState> readFrame)
        {
            ArgumentNullException.ThrowIfNull(readFrame);
            readFrame(BrowserFrameAccess.FromFrame(_frame), state);
            return true;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static BrowserFrame CreateTransparentFrame(BrowserViewport viewport)
        {
            int width = Math.Max(1, viewport.Width);
            int height = Math.Max(1, viewport.Height);
            byte[] pixels = new byte[width * height * BrowserFrameBuffer.BytesPerPixel];
            return new BrowserFrame(
                new BrowserViewport(width, height),
                BrowserPixelFormat.Bgra8888Premultiplied,
                pixels,
                width * BrowserFrameBuffer.BytesPerPixel,
                new[] { new BrowserDirtyRect(0, 0, width, height) },
                1);
        }
    }

    private sealed class TestBrowserMessageBridge : IBrowserMessageBridge
    {
        public event EventHandler<BrowserScriptMessage>? MessageReceived;

        public ValueTask PostMessageAsync(BrowserScriptMessage message, CancellationToken cancellationToken = default)
        {
            MessageReceived?.Invoke(this, message);
            return ValueTask.CompletedTask;
        }

        public ValueTask ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(script);
            return ValueTask.CompletedTask;
        }
    }
}
