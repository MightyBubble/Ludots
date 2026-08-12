using System.Threading.Tasks;
using CapabilityStandardGraphBehaviorCommon;
using CapabilityStandardGraphOpsQueryMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CapabilityStandardGraphOpsQueryMod;

public sealed class CapabilityStandardGraphOpsQueryModEntry : IMod
{
    public static readonly ServiceKey<GraphShowcaseMetrics> MetricsKey =
        new("CapabilityStandardGraphOpsQuery.Metrics");

    public void OnLoad(IModContext context)
    {
        var runtime = new GraphOpsQueryRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null) return Task.CompletedTask;
            runtime.EnsureWorld();
            engine.SetService(MetricsKey, runtime.Metrics);
            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}
