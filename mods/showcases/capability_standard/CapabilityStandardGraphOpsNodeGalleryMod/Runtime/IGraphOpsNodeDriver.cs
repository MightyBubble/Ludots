using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Association;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.TagDisplay;
using Ludots.Core.Spatial;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime;

public interface IGraphOpsNodeDriver
{
    void Seed(GraphOpsNodeDriverContext ctx);
    void Tick(GraphOpsNodeDriverContext ctx);
    void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw);
}

public sealed class GraphOpsNodeDriverContext
{
    public required string AssetsRoot { get; init; }
    public required GraphOpsNodeVignette Vignette { get; init; }
    public required GraphControlFlowCompileResult Compiled { get; init; }
    public required GraphKind Kind { get; init; }
    public required byte FeaturedDest { get; init; }
    public required World SimWorld { get; init; }
    public required GasGraphRuntimeApi Api { get; init; }
    public required GraphShowcaseMetrics Metrics { get; init; }
    public GraphOpsStageVisuals? Stage { get; set; }
    public EffectRequestQueue? EffectRequests { get; set; }
    public RelationshipRuntime? Relationships { get; set; }
    public RelationshipTypeRegistry? RelationshipTypes { get; set; }
    public RelationshipMetricRegistry? RelationshipMetrics { get; set; }
    public RelationshipFlagRegistry? RelationshipFlags { get; set; }
    public EntityCollectionStore? Collections { get; set; }
    public TagOps? TagOps { get; set; }
    public TagDisplayTableRegistry? TagDisplay { get; set; }
    public GameplayEventBus? EventBus { get; set; }
    public OwnershipResolver? Ownership { get; set; }
    public KnowledgeProjectionStore? Knowledge { get; set; }
    public ISpatialCoordinateConverter? Coords { get; set; }
    public BuiltinHandlerRegistry? BuiltinHandlers { get; set; }
    public EffectTemplateRegistry? EffectTemplates { get; set; }
    public BuiltinHandlerExecutionContext? BuiltinRuntime { get; set; }
    public int ConfigEffectTemplateId { get; set; }
    public bool OwnsSimulationWorld { get; set; }
    public Entity LastMaterializedTarget { get; set; } = Entity.Null;
    public Entity Caster { get; set; } = Entity.Null;
    public Entity Target { get; set; } = Entity.Null;
    public Entity TargetContext { get; set; } = Entity.Null;
    public Entity Viewer { get; set; } = Entity.Null;
    public GraphProgramRegistry? Programs { get; set; }
    public GraphEventPayload EventPayload { get; set; }
    public IntVector2 TargetPosCm { get; set; }
    public bool HasTargetPosCm { get; set; }
    public Entity[] PrefillTargets { get; set; } = Array.Empty<Entity>();
    public int PrefillTargetCount { get; set; }
    public Entity[] HitTargets { get; set; } = new Entity[GraphVmLimits.MaxTargets];
    public int HitTargetCount { get; set; }
    public byte[] LastBoolRegisters { get; } = new byte[GraphVmLimits.MaxBoolRegisters];
    public Entity[] SimActors { get; set; } = Array.Empty<Entity>();
    public Entity[] StageProxies { get; set; } = Array.Empty<Entity>();
    public float[] ActorHealth { get; set; } = Array.Empty<float>();
    public bool[] ActorHudLit { get; set; } = Array.Empty<bool>();
    public int Wave { get; set; }
    public Dictionary<string, string> CaptionValues { get; } = new(StringComparer.Ordinal);

    public GraphOpsNodeExecuteResult ExecuteFeaturedGraph()
    {
        if (BuiltinHandlers == null || EffectTemplates == null || BuiltinRuntime == null || ConfigEffectTemplateId <= 0)
        {
            throw new InvalidOperationException(
                $"Gallery '{Vignette.Op}' requires production builtin invocation (handlers, templates, config effect).");
        }

        if (EffectRequests == null)
        {
            throw new InvalidOperationException(
                $"Gallery '{Vignette.Op}' requires the production EffectRequestQueue to allocate a parent effect root.");
        }

        if (!EffectTemplates.TryGet(ConfigEffectTemplateId, out EffectTemplateData template))
        {
            throw new InvalidOperationException(
                $"Gallery '{Vignette.Op}' config effect id {ConfigEffectTemplateId} is not registered.");
        }

        var effectContext = new EffectContext
        {
            RootId = EffectRequests.AllocateRootId(),
            Source = Caster,
            Target = Target,
            TargetContext = TargetContext != Entity.Null ? TargetContext : Target
        };
        Api.BeginBuiltinInvocation(
            BuiltinHandlers,
            EffectTemplates,
            BuiltinRuntime,
            ConfigEffectTemplateId,
            in effectContext,
            in template.ConfigParams);
        try
        {
            GraphOpsNodeExecuteResult result = ExecuteFeaturedGraphBody();
            if (BuiltinRuntime.LifecycleTransaction is { HasMaterializedTarget: true } transaction)
            {
                LastMaterializedTarget = transaction.Target;
            }

            return result;
        }
        finally
        {
            Api.EndBuiltinInvocation();
        }
    }

    private GraphOpsNodeExecuteResult ExecuteFeaturedGraphBody()
    {
        Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
        Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
        Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
        Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
        Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
        Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
        var targetList = new GraphTargetList(targets);
        entities[0] = Caster;
        entities[1] = Target;
        entities[2] = Viewer != Entity.Null ? Viewer : TargetContext;
        if (PrefillTargetCount > 0)
        {
            if (PrefillTargetCount > targets.Length)
            {
                throw new InvalidOperationException(
                    $"Gallery '{Vignette.Op}' prefilled {PrefillTargetCount} targets, max {targets.Length}.");
            }

            for (int i = 0; i < PrefillTargetCount; i++)
            {
                targets[i] = PrefillTargets[i];
            }

            targetList.SetCount(PrefillTargetCount);
        }

        var state = new GraphExecutionState
        {
            World = SimWorld,
            Caster = Caster,
            ExplicitTarget = Target,
            TargetContext = TargetContext,
            Viewer = Viewer,
            EventPayload = EventPayload,
            TargetPosCm = HasTargetPosCm ? TargetPosCm : default,
            Api = Api,
            Programs = Programs,
            F = floats,
            I = ints,
            B = bools,
            E = entities,
            Targets = targets,
            TargetList = targetList,
            CallStack = callStack,
            RandomSeed = (uint)(0xA5A5A5A5u ^ (uint)Wave),
            Status = GraphExecutionStatus.Running
        };

        GasGraphOpHandlerTable.Execute(ref state, Compiled.Program, GasGraphOpHandlerTable.Instance);
        if (state.Status != GraphExecutionStatus.Halted)
        {
            throw new InvalidOperationException(
                $"Featured graph for {Vignette.Op} ended with status {state.Status}.");
        }

        HitTargetCount = state.TargetList.Count;
        ReadOnlySpan<Entity> hits = state.TargetList.Span;
        for (int i = 0; i < HitTargetCount; i++)
        {
            HitTargets[i] = hits[i];
        }

        for (int i = 0; i < LastBoolRegisters.Length; i++)
        {
            LastBoolRegisters[i] = bools[i];
        }

        TargetPosCm = state.TargetPosCm;
        byte dest = FeaturedDest == byte.MaxValue ? (byte)0 : FeaturedDest;
        return new GraphOpsNodeExecuteResult(
            dest < floats.Length ? floats[dest] : 0f,
            dest < ints.Length ? ints[dest] : 0,
            dest < bools.Length && bools[dest] != 0,
            dest < entities.Length ? entities[dest] : Entity.Null,
            state.ReturnInt,
            state.TargetList.Count);
    }
}

public readonly record struct GraphOpsNodeExecuteResult(
    float FloatValue,
    int IntValue,
    bool BoolValue,
    Entity EntityValue,
    int ReturnInt,
    int TargetCount);
