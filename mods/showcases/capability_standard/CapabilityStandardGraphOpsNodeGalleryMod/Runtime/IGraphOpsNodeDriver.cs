using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.DebugDraw;

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
    public Entity Caster { get; set; }
    public Entity Target { get; set; }
    public Entity TargetContext { get; set; }
    public IGraphRuntimeApi? RuntimeApiOverride { get; set; }
    public Entity[] SimActors { get; set; } = Array.Empty<Entity>();
    public Entity[] StageProxies { get; set; } = Array.Empty<Entity>();
    public float[] ActorHealth { get; set; } = Array.Empty<float>();
    public int Wave { get; set; }
    public Dictionary<string, string> CaptionValues { get; } = new(StringComparer.Ordinal);

    public GraphOpsNodeExecuteResult ExecuteFeaturedGraph()
    {
        Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
        Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
        Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
        Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
        Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
        Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
        var targetList = new GraphTargetList(targets);

        var state = new GraphExecutionState
        {
            World = SimWorld,
            Caster = Caster,
            ExplicitTarget = Target,
            TargetContext = TargetContext,
            Api = RuntimeApiOverride ?? Api,
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

        return new GraphOpsNodeExecuteResult(
            floats[FeaturedDest],
            ints[FeaturedDest],
            bools[FeaturedDest] != 0,
            entities[FeaturedDest],
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
