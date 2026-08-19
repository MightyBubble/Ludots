using System.Collections.Generic;
using System;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Orders;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace FireballSharedMod;

public sealed class FireballSharedModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[FireballSharedMod] Loaded - fireball arena uses GAS abilities/effects and presenter rules");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.Get(CoreServiceKeys.Engine) is not GameEngine engine)
            {
                throw new InvalidOperationException("FireballSharedMod requires GameEngine on GameStart.");
            }

            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.OrderQueue.Name, out object? orderQueueObj) ||
                orderQueueObj is not OrderQueue orders)
            {
                throw new InvalidOperationException("FireballSharedMod requires OrderQueue before installing local fireball input.");
            }

            engine.RegisterSystem(
                new FireballLocalOrderSourceSystem(engine.World, engine.GlobalContext, orders, context),
                SystemGroup.InputCollection);

            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}

public sealed class FireballOpenStatusPanelTrigger : Trigger
{
    private const string MetadataSectionKey = "fireballPanel";
    private const string ActionKey = "openAction";
    private const string ScopeInstanceKey = "scopeInstanceId";
    private const int FireballPanelSliceBudgetSteps = 64;
    private readonly int[] _vmIntRegisters = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _vmBoolRegisters = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly int[] _vmCallStack = new int[GraphVmLimits.MaxCallStackDepth];

    public FireballOpenStatusPanelTrigger()
    {
        EventKey = GameEvents.MapLoaded;
        Priority = 0;
    }

    public override Task ExecuteAsync(ScriptContext context)
    {
        if (!CheckConditions(context))
        {
            return Task.CompletedTask;
        }

        FireballPanelTriggerDependencies dependencies = ResolveDependencies(context);

        FireballPanelTriggerConfig config = LoadConfig(dependencies.Session);
        Entity scope = ResolveScope(dependencies.Session, config.ScopeInstanceId);
        int graphId = dependencies.Actions.Require(config.OpenAction, GraphActionHost.Level);

        ResetExecutionState();
        var cursor = new GraphExecutionCursor();
        GraphSliceResult result = GraphExecutor.ExecuteRegisteredSlice(
            dependencies.Programs,
            graphId,
            _vmIntRegisters,
            _vmBoolRegisters,
            _vmCallStack,
            ref cursor,
            budgetSteps: FireballPanelSliceBudgetSteps,
            world: dependencies.Engine.World,
            caster: scope,
            explicitTarget: scope,
            api: dependencies.GraphApi);
        if (!result.Halted)
        {
            throw new InvalidOperationException(
                $"Fireball panel open action '{config.OpenAction}' must halt in one level-script slice (got {result.Status}).");
        }

        return Task.CompletedTask;
    }

    private static FireballPanelTriggerDependencies ResolveDependencies(ScriptContext context)
    {
        GameEngine engine = context.Get(CoreServiceKeys.Engine)
            ?? throw new InvalidOperationException("FireballOpenStatusPanelTrigger requires GameEngine.");
        MapSession session = context.Get(CoreServiceKeys.MapSession)
            ?? throw new InvalidOperationException("FireballOpenStatusPanelTrigger requires MapSession.");
        GraphActionCatalog actions = engine.GetService(CoreServiceKeys.GraphActionCatalog)
            ?? throw new InvalidOperationException("FireballOpenStatusPanelTrigger requires GraphActionCatalog.");
        GraphProgramRegistry programs = engine.GetService(CoreServiceKeys.GraphProgramRegistry)
            ?? throw new InvalidOperationException("FireballOpenStatusPanelTrigger requires GraphProgramRegistry.");
        GasGraphRuntimeApi graphApi = engine.GetService(CoreServiceKeys.GasGraphRuntimeApi)
            ?? throw new InvalidOperationException("FireballOpenStatusPanelTrigger requires GasGraphRuntimeApi.");
        if (graphApi.PanelHost == null)
        {
            throw new InvalidOperationException("FireballOpenStatusPanelTrigger requires GasGraphRuntimeApi bound to PanelHost.");
        }

        return new FireballPanelTriggerDependencies(engine, session, actions, programs, graphApi);
    }

    private void ResetExecutionState()
    {
        Array.Clear(_vmIntRegisters, 0, _vmIntRegisters.Length);
        Array.Clear(_vmBoolRegisters, 0, _vmBoolRegisters.Length);
        Array.Clear(_vmCallStack, 0, _vmCallStack.Length);
    }

    private static FireballPanelTriggerConfig LoadConfig(MapSession session)
    {
        if (session.MapConfig.Metadata == null ||
            !session.MapConfig.Metadata.TryGetValue(MetadataSectionKey, out JsonNode? sectionNode) ||
            sectionNode is not JsonObject section)
        {
            throw new InvalidOperationException(
                $"Map '{session.MapId.Value}' requires Metadata.{MetadataSectionKey} for the fireball panel trigger.");
        }

        return new FireballPanelTriggerConfig(
            ReadRequiredString(session, section, ActionKey),
            ReadRequiredString(session, section, ScopeInstanceKey));
    }

    private static string ReadRequiredString(MapSession session, JsonObject section, string property)
    {
        if (!section.TryGetPropertyValue(property, out JsonNode? node) || node is not JsonValue value)
        {
            throw new InvalidOperationException(
                $"Map '{session.MapId.Value}' Metadata.{MetadataSectionKey}.{property} is required.");
        }

        string? text = value.GetValue<string>();
        if (string.IsNullOrWhiteSpace(text) || !string.Equals(text, text.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Map '{session.MapId.Value}' Metadata.{MetadataSectionKey}.{property} must be a trimmed non-empty string.");
        }

        return text;
    }

    private static Entity ResolveScope(MapSession session, string scopeInstanceId)
    {
        MapLoadEntityIndex index = session.EntityIndex
            ?? throw new InvalidOperationException($"Map '{session.MapId.Value}' has no entity index.");
        Entity scope = index.GetRequired(
            session.MapId.Value,
            scopeInstanceId,
            $"Metadata.{MetadataSectionKey}.{ScopeInstanceKey}");
        return scope;
    }

    private readonly record struct FireballPanelTriggerConfig(string OpenAction, string ScopeInstanceId);
    private readonly record struct FireballPanelTriggerDependencies(
        GameEngine Engine,
        MapSession Session,
        GraphActionCatalog Actions,
        GraphProgramRegistry Programs,
        GasGraphRuntimeApi GraphApi);
}

internal sealed class FireballLocalOrderSourceSystem : ISystem<float>
{
    private readonly World _world;
    private readonly LocalOrderSourceHelper _helper;
    private readonly IModContext _context;
    private InputOrderMappingSystem? _mapping;
    private bool _initialized;

    public FireballLocalOrderSourceSystem(
        World world,
        Dictionary<string, object> globals,
        OrderQueue orders,
        IModContext context)
    {
        _world = world;
        _context = context;
        _helper = new LocalOrderSourceHelper(world, globals, orders);
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        EnsureInitialized();
        if (_mapping == null)
        {
            return;
        }

        Entity actor = _helper.GetControlledActor();
        if (_world.IsAlive(actor) && _helper.TryBindSoleSeatActor(_mapping, actor))
        {
            _mapping.Update(dt);
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _mapping = _helper.TryCreateMapping(_context);
        if (_mapping == null)
        {
            throw new InvalidOperationException(
                "FireballSharedMod could not install input_order_mappings.json; AuthoritativeInput and the VFS input config are required.");
        }
    }
}
