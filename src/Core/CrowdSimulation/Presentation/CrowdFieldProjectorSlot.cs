using Ludots.Core.Presentation.Fields;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 群体场投影的唯一槽位。引擎安装注册表时登记一次,之后不增删。
/// <see cref="Current"/> 为空(无会话或已卸载)时不写场。
/// </summary>
internal sealed class CrowdFieldProjectorSlot : IGlobalFieldVisualProjector
{
    public IGlobalFieldVisualProjector? Current { get; set; }

    public void Project(GlobalFieldVisualBuffer buffer) => Current?.Project(buffer);
}
