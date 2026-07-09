using System.Numerics;
using Arch.Core;
using CapabilityStandardGraphOpProviderMod;
using CapabilityStandardGraphOpProviderMod.Runtime;
using CapabilityStandardModExtensibleRuntimeShowcaseShared;
using Ludots.Core.Engine;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;

namespace CapabilityStandardGraphOpExtensionShowcaseMod;

public sealed class CapabilityStandardGraphOpExtensionShowcaseModEntry : IMod
{
    private const string MapId = "capability_standard_graph_op_extension_showcase";
    private const string GraphId = "Graph.CapabilityStandard.GraphOpExtension.ScoreThreat";
    private Entity _sourceEntity = Entity.Null;
    private Entity _leftTarget = Entity.Null;
    private Entity _rightTarget = Entity.Null;

    public void OnLoad(IModContext context)
    {
        var runtime = new ExtensibleRuntimeShowcaseRuntime(new ExtensibleRuntimeShowcaseScenario
        {
            MapId = MapId,
            PanelElementId = "capability-standard-graph-op-extension-panel",
            PrimaryButtonElementId = "capability-standard-graph-op-extension-rescore",
            SurfaceOwnerId = "Showcase.CapabilityStandardGraphOpExtension.Panel",
            Title = "Graph Op Extension",
            FeatureLabel = "Provider op",
            PrimaryButtonLabel = "Re-score Threat",
            AccentColor = "#B794FF",
            ReadyText = "Threat scores are calculated by a reusable provider formula.",
            ProofLines =
            [
                $"Provider op: {CapabilityStandardGraphOpProviderModEntry.QueryThreatKey}",
                $"Consumer graph: {GraphId}",
                "The root mod consumes the provider key; it does not register that namespace."
            ],
            OnActivated = VerifyGraph,
            OnPrimaryAction = ExecuteThreatGraph
        });

        ExtensibleRuntimeShowcaseBootstrap.Install(context, runtime, nameof(CapabilityStandardGraphOpExtensionShowcaseMod));
    }

    public void OnUnload()
    {
    }

    private void VerifyGraph(ExtensibleRuntimeShowcaseRuntime runtime, GameEngine engine)
    {
        EnsureTargets(engine);
        int graphId = GraphIdRegistry.GetId(GraphId);
        var registry = engine.GetService(CoreServiceKeys.GraphProgramRegistry)
            ?? throw new InvalidOperationException("Graph op extension showcase requires GraphProgramRegistry.");
        bool graphLoaded = graphId > 0 && registry.TryGetProgram(graphId, out _);
        if (!graphLoaded)
        {
            throw new InvalidOperationException($"Graph op extension showcase requires graph '{GraphId}'.");
        }

        runtime.SetMetricA("Provider", "ready");
        runtime.SetMetricB("Graph", "compiled");
        runtime.SetLastEvent("Threat scoring is ready for the left and right targets.");
    }

    private void ExecuteThreatGraph(ExtensibleRuntimeShowcaseRuntime runtime, GameEngine engine)
    {
        int graphId = GraphIdRegistry.GetId(GraphId);
        var registry = engine.GetService(CoreServiceKeys.GraphProgramRegistry)
            ?? throw new InvalidOperationException("Graph op extension showcase requires GraphProgramRegistry.");
        var handlers = engine.GetService(CoreServiceKeys.GasGraphOpHandlerTable)
            ?? throw new InvalidOperationException("Graph op extension showcase requires GasGraphOpHandlerTable.");
        if (!registry.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program))
        {
            throw new InvalidOperationException($"Graph '{GraphId}' is not registered.");
        }

        EnsureTargets(engine);
        bool rightWins = runtime.PrimaryActionCount % 2 == 1;
        SetThreatScore(engine, _leftTarget, rightWins ? 35f : 92f);
        SetThreatScore(engine, _rightTarget, rightWins ? 96f : 41f);

        float leftScore = Ludots.Core.NodeLibraries.GASGraph.GraphExecutor.ExecuteScore(engine.World, _sourceEntity, _leftTarget, IntVector2.Zero, program, api: null!, handlers);
        float rightScore = Ludots.Core.NodeLibraries.GASGraph.GraphExecutor.ExecuteScore(engine.World, _sourceEntity, _rightTarget, IntVector2.Zero, program, api: null!, handlers);

        runtime.SetHighlightRight(rightScore > leftScore);
        runtime.SetMetricA("Left", $"{leftScore:0}");
        runtime.SetMetricB("Right", $"{rightScore:0}");
        runtime.SetLastEvent("Threat scores were recalculated and the higher target was highlighted.");
    }

    private void EnsureTargets(GameEngine engine)
    {
        if (_sourceEntity == Entity.Null || !engine.World.IsAlive(_sourceEntity))
        {
            _sourceEntity = engine.World.Create(new VisualTransform
            {
                Position = new Vector3(5.2f, 0f, 7.4f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One
            });
        }

        if (_leftTarget == Entity.Null || !engine.World.IsAlive(_leftTarget))
        {
            _leftTarget = CreateTarget(engine, new Vector3(7.4f, 0f, 5.2f), 35f);
        }

        if (_rightTarget == Entity.Null || !engine.World.IsAlive(_rightTarget))
        {
            _rightTarget = CreateTarget(engine, new Vector3(11.4f, 0f, 5.2f), 96f);
        }
    }

    private static Entity CreateTarget(GameEngine engine, Vector3 position, float threatScore)
    {
        return engine.World.Create(
            new VisualTransform
            {
                Position = position,
                Rotation = Quaternion.Identity,
                Scale = Vector3.One
            },
            new CapabilityStandardGraphOpThreatScore
            {
                Value = threatScore
            });
    }

    private static void SetThreatScore(GameEngine engine, Entity entity, float value)
    {
        if (!engine.World.IsAlive(entity) || !engine.World.Has<CapabilityStandardGraphOpThreatScore>(entity))
        {
            throw new InvalidOperationException("Graph op extension showcase target is missing CapabilityStandardGraphOpThreatScore.");
        }

        ref var score = ref engine.World.Get<CapabilityStandardGraphOpThreatScore>(entity);
        score.Value = value;
    }
}
