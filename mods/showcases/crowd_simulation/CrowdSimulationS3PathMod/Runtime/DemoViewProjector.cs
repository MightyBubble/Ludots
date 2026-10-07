using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.Presentation.Fields;
using Ludots.Core.Presentation.Rendering;

namespace CrowdSimulationS3PathMod.Runtime;

/// <summary>按演示视图模式分发的投影器:路线 = 方向场,可走区域 = 遮罩层,NavMesh 模式不写场(线框走路线通道)。</summary>
internal sealed class DemoViewProjector : IGlobalFieldVisualProjector
{
    private readonly S3PathDemoRuntime _runtime;
    private readonly CrowdFlowFieldVisualProjector _flow;
    private readonly CrowdWalkableVisualProjector _walkable;

    public DemoViewProjector(S3PathDemoRuntime runtime, CrowdFlowFieldVisualProjector flow, CrowdWalkableVisualProjector walkable)
    {
        _runtime = runtime;
        _flow = flow;
        _walkable = walkable;
    }

    public void Project(GlobalFieldVisualBuffer buffer)
    {
        switch (_runtime.ViewMode)
        {
            case 0: _flow.Project(buffer); break;
            case 1: _walkable.Project(buffer); break;
        }
    }
}
