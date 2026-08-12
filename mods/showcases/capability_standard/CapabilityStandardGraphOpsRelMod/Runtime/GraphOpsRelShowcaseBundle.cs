using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.GraphRuntime;

namespace CapabilityStandardGraphOpsRelMod.Runtime;

public sealed class GraphOpsRelShowcaseBundle
{
    public required GraphProgramRegistry Programs { get; init; }
    public required GraphOpsRelFunctionIndex Functions { get; init; }
    public required RelationshipTypeRegistry Types { get; init; }
    public required RelationshipMetricRegistry Metrics { get; init; }
    public required RelationshipFlagRegistry Flags { get; init; }
    public required RelationshipReasonRegistry Reasons { get; init; }
}
