using System.Collections.Generic;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Fields;

/// <summary>全场视觉投影器:每帧把己方数据源写进共享场缓冲(迷雾/影响/离散权属同款管道)。</summary>
public interface IGlobalFieldVisualProjector
{
    void Project(GlobalFieldVisualBuffer buffer);
}

/// <summary>
/// 全场视觉投影器注册表:能力 / Mod 在此登记自己的投影器,宿主每帧在 BeginFrame 后
/// 逐个调用——宿主不需要知道有哪些场(流的"发布到共享缓冲,再补渲染契约"的另一半)。
/// 注册在 GameStart 期完成,运行期不增删(投影是纯函数,顺序只影响同格覆写序)。
/// </summary>
public sealed class GlobalFieldVisualProjectorRegistry
{
    private readonly List<IGlobalFieldVisualProjector> _projectors = new();

    public IReadOnlyList<IGlobalFieldVisualProjector> Projectors => _projectors;

    public void Register(IGlobalFieldVisualProjector projector)
    {
        ArgumentNullException.ThrowIfNull(projector);
        _projectors.Add(projector);
    }
}
