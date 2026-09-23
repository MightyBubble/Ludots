using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Runtime.Events;
using Ludots.WebUI.Browser;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;
using SanguoGrandStrategyMod;
using SanguoGrandStrategyMod.Runtime;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
public sealed class SanguoGrandStrategyPlayableAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod",
        "EntityCommandPanelMod",
        "SanguoGrandStrategyMod"
    };

    [Test]
    public void SanguoGrandStrategy_LoadsAndPlaysCoreLoop()
    {
        using GameEngine engine = CreateEngine();
        UIRoot uiRoot = AcceptanceUiHostInstaller.Install(engine);
        engine.Start();
        engine.LoadMap(SanguoGrandStrategyIds.ShowcaseMapId);
        Tick(engine, 20);

        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        Assert.That(CountMapEntities(engine.World, SanguoGrandStrategyIds.ShowcaseMap), Is.EqualTo(300));

        SanguoGrandStrategyRuntime runtime = ResolveRuntime(engine);
        Assert.That(runtime.CityCount, Is.EqualTo(300));
        Assert.That(runtime.UnitTypeCount, Is.EqualTo(100));
        Assert.That(runtime.FactionCount, Is.GreaterThanOrEqualTo(5));

        SanguoGrandStrategySnapshot initial = runtime.BuildDataPlaneSnapshot();
        Assert.That(initial.Cities, Has.Length.EqualTo(300));
        Assert.That(initial.UnitTypeCount, Is.EqualTo(100));
        Assert.That(initial.Factions, Has.Length.EqualTo(8));
        Assert.That(initial.WebUiStatus, Does.Contain("WebUI DataPlane"));
        Assert.That(initial.Graph.CityCount, Is.GreaterThan(0));
        Assert.That(initial.Graph.CityCount, Is.LessThanOrEqualTo(300));
        Assert.That(initial.Graph.Population, Is.GreaterThan(0));
        Assert.That(initial.Graph.BestProductionCity, Is.Not.EqualTo("n/a"));
        Assert.That(initial.EquipmentLine, Does.Contain("Green Dragon Blade"));
        Assert.That(initial.EquipmentLine, Does.Contain("Red Hare"));
        Assert.That(initial.CommanderLine, Does.Contain("Command"));
        AssertUiContains(uiRoot, "Sanguo Grand Strategy");
        AssertUiContains(uiRoot, "Conscript");
        AssertUiContains(uiRoot, "Resolve Battle");
        AssertUiContains(uiRoot, "Commander equipment");
        AssertUiContains(uiRoot, "Graph economy sample");
        AssertCommanderEquipment(engine, initial);
        AssertGraphOutputs(engine);

        SanguoGrandStrategyCityView before = SelectedCity(initial);
        EffectRequestQueue queue = engine.GetService(CoreServiceKeys.EffectRequestQueue)
            ?? throw new InvalidOperationException("EffectRequestQueue missing.");
        int queuedBefore = queue.Count;
        ClickButton(uiRoot, "Conscript");
        Assert.That(queue.Count, Is.GreaterThan(queuedBefore), "Conscript should publish a GAS effect request.");
        Tick(engine, 4);
        SanguoGrandStrategyCityView afterConscript = SelectedCity(runtime.BuildDataPlaneSnapshot());
        Assert.That(afterConscript.Troops, Is.GreaterThan(before.Troops));
        Assert.That(afterConscript.Food, Is.LessThan(before.Food));
        Assert.That(afterConscript.Gold, Is.LessThan(before.Gold));

        ClickButton(uiRoot, "Develop");
        Tick(engine, 12);
        SanguoGrandStrategyCityView afterDevelop = SelectedCity(runtime.BuildDataPlaneSnapshot());
        Assert.That(afterDevelop.Production, Is.GreaterThanOrEqualTo(afterConscript.Production + 5));

        string selectedUnitBefore = runtime.BuildDataPlaneSnapshot().SelectedUnitType;
        ClickButton(uiRoot, "Next Unit");
        Tick(engine, 2);
        Assert.That(runtime.BuildDataPlaneSnapshot().SelectedUnitType, Is.Not.EqualTo(selectedUnitBefore));

        ClickButton(uiRoot, "Harvest");
        ClickButton(uiRoot, "Research");
        ClickButton(uiRoot, "Diplomacy");
        Tick(engine, 8);
        SanguoGrandStrategySnapshot afterCivilActions = runtime.BuildDataPlaneSnapshot();
        Assert.That(afterCivilActions.LogLines.Any(line => line.Contains("research", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(afterCivilActions.LogLines.Any(line => line.Contains("Diplomacy", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(afterCivilActions.Graph.Executions, Is.GreaterThan(initial.Graph.Executions));
        Assert.That(afterCivilActions.Graph.CityCount, Is.EqualTo(initial.Graph.CityCount));

        int enemyCitiesBefore = afterCivilActions.Cities.Count(static city => city.TeamId != 1);
        ClickButton(uiRoot, "March");
        Tick(engine, 160);
        ClickButton(uiRoot, "Resolve Battle");
        Tick(engine, 8);
        SanguoGrandStrategySnapshot afterBattle = runtime.BuildDataPlaneSnapshot();
        Assert.That(afterBattle.LogLines.Any(line =>
            line.Contains("held", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("fell", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(afterBattle.Cities.Count(static city => city.TeamId != 1), Is.LessThanOrEqualTo(enemyCitiesBefore));

        AssertRegistries(engine);
    }

    [Test]
    public async Task SanguoGrandStrategy_WebUiDataPlanePublishesAndRoutesCommands()
    {
        using GameEngine engine = CreateEngine();
        AcceptanceUiHostInstaller.Install(engine);
        var browserRuntime = new TestBrowserRuntime();
        engine.SetService(
            new ServiceKey<IBrowserRuntime>(BrowserRuntimeServiceNames.BrowserRuntime),
            browserRuntime);

        engine.Start();
        engine.LoadMap(SanguoGrandStrategyIds.ShowcaseMapId);
        Tick(engine, 30);

        TestBrowserSurface surface = browserRuntime.LastSurface
            ?? throw new InvalidOperationException("Sanguo WebUI should create a browser surface when a browser runtime is present.");
        Assert.That(surface.LastNavigation?.Uri, Is.EqualTo(BrowserLocalAppUri.Root));

        TestBrowserMessageBridge bridge = surface.Bridge;
        bridge.Receive(CreateBrowserControlMessage("sanguo-grand-strategy", 1, "handshake", "system", new
        {
            requiredCapabilities = Array.Empty<string>()
        }));

        await WaitForBrowserWireMessageAsync(bridge, engine, static root =>
            string.Equals(root.GetProperty("kind").GetString(), WebUiPacketKind.Control.ToString(), StringComparison.Ordinal) &&
            string.Equals(root.GetProperty("payload").GetProperty("kind").GetString(), "handshakeAck", StringComparison.Ordinal));

        bridge.Receive(CreateBrowserControlMessage("sanguo-grand-strategy", 2, "subscribe", SanguoGrandStrategyIds.TopicName, new { }));
        string snapshotWire = await WaitForBrowserWireMessageAsync(bridge, engine, static root =>
            string.Equals(root.GetProperty("kind").GetString(), WebUiPacketKind.Snapshot.ToString(), StringComparison.Ordinal) &&
            string.Equals(root.GetProperty("topic").GetString(), SanguoGrandStrategyIds.TopicName, StringComparison.Ordinal));

        string selectedBefore;
        using (JsonDocument snapshot = JsonDocument.Parse(snapshotWire))
        {
            JsonElement payload = snapshot.RootElement.GetProperty("payload");
            Assert.That(payload.GetProperty("cities").GetArrayLength(), Is.EqualTo(300));
            Assert.That(payload.GetProperty("unitTypeCount").GetInt32(), Is.EqualTo(100));
            Assert.That(payload.GetProperty("factionCount").GetInt32(), Is.GreaterThanOrEqualTo(5));
            Assert.That(payload.GetProperty("webUiStatus").GetString(), Does.Contain("publishing"));
            selectedBefore = payload.GetProperty("selectedCity").GetString()
                ?? throw new InvalidOperationException("Snapshot should include selectedCity.");
        }

        bridge.Receive(CreateBrowserControlMessage("sanguo-grand-strategy", 3, "command", SanguoGrandStrategyIds.TopicName, new
        {
            name = "nextCity",
            clientSeq = 7,
            entityRefs = Array.Empty<WebUiEntityRef>(),
            payload = new { }
        }));

        await WaitForBrowserWireMessageAsync(bridge, engine, static root =>
            string.Equals(root.GetProperty("kind").GetString(), WebUiPacketKind.CommandAck.ToString(), StringComparison.Ordinal) &&
            root.GetProperty("payload").GetProperty("payload").GetProperty("clientSeq").GetInt64() == 7);

        SanguoGrandStrategySnapshot afterCommand = ResolveRuntime(engine).BuildDataPlaneSnapshot();
        Assert.That(afterCommand.SelectedCity, Is.Not.EqualTo(selectedBefore));
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        string assetsRoot = Path.Combine(repoRoot, "assets");
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods), assetsRoot);
        return engine;
    }

    private static SanguoGrandStrategyRuntime ResolveRuntime(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(SanguoGrandStrategyIds.RuntimeServiceKey, out object? runtimeObj) &&
               runtimeObj is SanguoGrandStrategyRuntime runtime
            ? runtime
            : throw new InvalidOperationException("SanguoGrandStrategyRuntime missing.");
    }

    private static void AssertRegistries(GameEngine engine)
    {
        Assert.That(AbilityIdRegistry.GetId("Ability.Sanguo.Conscript"), Is.GreaterThan(0));
        Assert.That(EffectTemplateIdRegistry.GetId("Effect.Sanguo.Conscript"), Is.GreaterThan(0));

        ItemDefinitionRegistry items = engine.GetService(CoreServiceKeys.ItemDefinitionRegistry)
            ?? throw new InvalidOperationException("ItemDefinitionRegistry missing.");
        Assert.That(items.GetId("itm_sanguo_red_hare"), Is.GreaterThan(0));

        GraphProgramRegistry graphs = engine.GetService(CoreServiceKeys.GraphProgramRegistry)
            ?? throw new InvalidOperationException("GraphProgramRegistry missing.");
        int graphId = GraphIdRegistry.GetId("sanguo.graph.cityEconomyQuery");
        Assert.That(graphId, Is.GreaterThan(0));
        Assert.That(graphs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program), Is.True);
        Assert.That(program.Length, Is.GreaterThan(0));
    }

    private static void AssertGraphOutputs(GameEngine engine)
    {
        Entity owner = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        Assert.That(engine.World.IsAlive(owner), Is.True, "Local player graph owner should be alive.");

        GraphOutputValueStore values = engine.GetService(CoreServiceKeys.GraphOutputValueStore)
            ?? throw new InvalidOperationException("GraphOutputValueStore missing.");
        Assert.That(values.TryGet(owner, "sanguo.summary.cityCount", out GraphOutputValueHandle cityHandle), Is.True);
        Assert.That(values.TryGetView(cityHandle, out GraphOutputValueView cityView), Is.True);
        Assert.That(cityView.IntValue, Is.GreaterThan(0));
        Assert.That(cityView.IntValue, Is.LessThanOrEqualTo(300));

        Assert.That(values.TryGet(owner, "sanguo.summary.population", out GraphOutputValueHandle populationHandle), Is.True);
        Assert.That(values.TryGetView(populationHandle, out GraphOutputValueView populationView), Is.True);
        Assert.That(populationView.FloatValue, Is.GreaterThan(0f));
    }

    private static void AssertCommanderEquipment(GameEngine engine, SanguoGrandStrategySnapshot snapshot)
    {
        Entity commander = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        Assert.That(engine.World.IsAlive(commander), Is.True);
        Assert.That(engine.World.Has<ActiveEffectContainer>(commander), Is.True);
        Assert.That(engine.World.Get<ActiveEffectContainer>(commander).Count, Is.GreaterThanOrEqualTo(5));
        Assert.That(ReadAttribute(engine.World, commander, "Command"), Is.GreaterThan(34));
        Assert.That(ReadAttribute(engine.World, commander, "Defense"), Is.GreaterThan(18));
        Assert.That(snapshot.CommanderLine, Does.Contain("Training"));
    }

    private static int ReadAttribute(World world, Entity entity, string attribute)
    {
        int id = AttributeRegistry.GetId(attribute);
        if (id <= AttributeRegistry.InvalidId || !world.IsAlive(entity) || !world.Has<AttributeBuffer>(entity))
        {
            return 0;
        }

        return (int)MathF.Round(world.Get<AttributeBuffer>(entity).GetCurrent(id));
    }

    private static SanguoGrandStrategyCityView SelectedCity(SanguoGrandStrategySnapshot snapshot)
    {
        return snapshot.Cities.First(static city => city.Selected);
    }

    private static int CountMapEntities(World world, MapId mapId)
    {
        int count = 0;
        var query = new QueryDescription().WithAll<MapEntity>();
        world.Query(in query, (ref MapEntity mapEntity) =>
        {
            if (mapEntity.MapId == mapId)
            {
                count++;
            }
        });
        return count;
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
        }
    }

    private static BrowserScriptMessage CreateBrowserControlMessage(
        string sessionId,
        long requestId,
        string kind,
        string topic,
        object payload)
    {
        byte[] envelope = WebUiDataPlaneProtocol.SerializeControlEnvelope(
            WebUiDataPlaneProtocol.CreateControlEnvelope(sessionId, requestId, kind, topic, payload));
        return new BrowserScriptMessage(
            BrowserMessageBridgeDataTransport.ControlChannel,
            Encoding.UTF8.GetString(envelope));
    }

    private static async Task<string> WaitForBrowserWireMessageAsync(
        TestBrowserMessageBridge bridge,
        GameEngine engine,
        Func<JsonElement, bool> predicate)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!cts.IsCancellationRequested)
        {
            Tick(engine, 1);
            while (bridge.TryDequeuePosted(out BrowserScriptMessage? message))
            {
                if (message == null)
                {
                    continue;
                }

                if (!string.Equals(message.Channel, BrowserMessageBridgeDataTransport.ControlChannel, StringComparison.Ordinal))
                {
                    continue;
                }

                using JsonDocument document = JsonDocument.Parse(message.Payload);
                if (predicate(document.RootElement))
                {
                    return message.Payload;
                }
            }

            await Task.Delay(10, cts.Token);
        }

        throw new TimeoutException("Timed out waiting for Sanguo WebUI DataPlane bridge message.");
    }

    private static void ClickButton(UIRoot root, string label)
    {
        UiScene scene = root.Scene ?? throw new InvalidOperationException("UI scene should be mounted before clicking buttons.");
        UiNode target = FindClickableNodeByLabel(scene.Root, label)
            ?? throw new InvalidOperationException($"Clickable node '{label}' was not found.");
        UiActionHandle handle = target.ActionHandles.FirstOrDefault();
        Assert.That(handle.IsValid, Is.True, $"Clickable node '{label}' should expose an action handle.");
        bool dispatched = scene.Dispatcher.Dispatch(
            handle,
            new UiActionContext(scene, new UiPointerEvent(UiPointerEventType.Up, 0, 0f, 0f, target.Id), target));
        Assert.That(dispatched, Is.True, $"Button '{label}' should dispatch.");
    }

    private static UiNode? FindClickableNodeByLabel(UiNode? root, string label)
    {
        if (root == null)
        {
            return null;
        }

        string? text = root.TextContent?.Trim();
        if (string.Equals(text, label, StringComparison.Ordinal))
        {
            UiNode? clickable = root;
            while (clickable != null && clickable.ActionHandles.Count == 0)
            {
                clickable = clickable.Parent;
            }

            if (clickable != null)
            {
                return clickable;
            }
        }

        for (int i = 0; i < root.Children.Count; i++)
        {
            UiNode? match = FindClickableNodeByLabel(root.Children[i], label);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private static void AssertUiContains(UIRoot root, string expected)
    {
        List<string> lines = ExtractUiText(root);
        Assert.That(lines.Any(line => line.Contains(expected, StringComparison.Ordinal)), Is.True,
            $"Expected UI to contain '{expected}'. Actual: {string.Join(" | ", lines.Take(40))}");
    }

    private static List<string> ExtractUiText(UIRoot root)
    {
        var lines = new List<string>();
        if (root.Scene?.Root != null)
        {
            CollectUiText(root.Scene.Root, lines);
        }

        return lines;
    }

    private static void CollectUiText(UiNode node, List<string> lines)
    {
        if (!string.IsNullOrWhiteSpace(node.TextContent))
        {
            lines.Add(node.TextContent.Trim());
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            CollectUiText(node.Children[i], lines);
        }
    }

    private static string FindRepoRoot()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            string candidate = Path.Combine(dir, "src", "Core", "Ludots.Core.csproj");
            if (File.Exists(candidate))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir) ?? string.Empty;
        }

        throw new InvalidOperationException("Could not locate repo root.");
    }

    private sealed class TestBrowserRuntime : IBrowserRuntime
    {
        public BrowserRuntimeInfo Info { get; } = new(
            BrowserEngineKind.Ultralight,
            "SanguoAcceptanceBrowser",
            "1.0",
            BrowserEngineCapabilityProfiles.Ultralight);

        public TestBrowserSurface? LastSurface { get; private set; }

        public ValueTask<IBrowserSurface> CreateSurfaceAsync(
            BrowserViewport viewport,
            IBrowserResourceResolver? resourceResolver = null,
            CancellationToken cancellationToken = default)
        {
            LastSurface = new TestBrowserSurface(viewport);
            return ValueTask.FromResult<IBrowserSurface>(LastSurface);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestBrowserSurface : IBrowserSurface
    {
        public TestBrowserSurface(BrowserViewport viewport)
        {
            Viewport = viewport;
        }

        public event EventHandler<BrowserFrameReadyEventArgs>? FrameReady
        {
            add { }
            remove { }
        }

        public BrowserSurfaceId Id { get; } = BrowserSurfaceId.New();
        public BrowserViewport Viewport { get; private set; }
        public TestBrowserMessageBridge Bridge { get; } = new();
        public IBrowserMessageBridge Messages => Bridge;
        public BrowserNavigationRequest? LastNavigation { get; private set; }

        public ValueTask NavigateAsync(BrowserNavigationRequest request, CancellationToken cancellationToken = default)
        {
            LastNavigation = request;
            return ValueTask.CompletedTask;
        }

        public ValueTask ResizeAsync(BrowserViewport viewport, CancellationToken cancellationToken = default)
        {
            Viewport = viewport;
            return ValueTask.CompletedTask;
        }

        public ValueTask SendInputAsync(BrowserInputEvent inputEvent, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public BrowserFrame? TryGetLatestFrame()
        {
            return null;
        }

        public bool TryReadLatestFrame<TState>(TState state, BrowserFrameReadAction<TState> readFrame)
        {
            return false;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestBrowserMessageBridge : IBrowserMessageBridge
    {
        private readonly ConcurrentQueue<BrowserScriptMessage> _posted = new();

        public event EventHandler<BrowserScriptMessage>? MessageReceived;

        public ValueTask PostMessageAsync(BrowserScriptMessage message, CancellationToken cancellationToken = default)
        {
            _posted.Enqueue(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public void Receive(BrowserScriptMessage message)
        {
            MessageReceived?.Invoke(this, message);
        }

        public bool TryDequeuePosted(out BrowserScriptMessage? message)
        {
            return _posted.TryDequeue(out message);
        }
    }
}
