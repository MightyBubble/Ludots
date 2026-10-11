using System.Numerics;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 群体演示的地面拾取:指针位置走已绑定的 PointerPos 动作,射线走 ScreenRayProvider 与高度图。
/// HUD 矩形内不算世界点选。
/// </summary>
internal static class CrowdPointerGround
{
    public static bool TryPick(
        GameEngine engine,
        IInputActionReader actions,
        int hudX,
        int hudY,
        int hudW,
        int hudH,
        out double xCm,
        out double yCm)
    {
        xCm = 0;
        yCm = 0;
        Vector3 pointer = actions.ReadAction<Vector3>(ReservedInputActionIds.PointerPos);
        if (pointer.X >= hudX && pointer.X <= hudX + hudW &&
            pointer.Y >= hudY && pointer.Y <= hudY + hudH)
        {
            return false;
        }

        if (!engine.TryGetService(CoreServiceKeys.ContinuousHeightmap, out IContinuousHeightmap? heightmap) ||
            heightmap == null ||
            !engine.TryGetService(CoreServiceKeys.ScreenRayProvider, out IScreenRayProvider? rays) ||
            rays == null)
        {
            return false;
        }

        var ray = rays.GetRay(new Vector2(pointer.X, pointer.Y));
        if (!heightmap.TryRaycastGround(in ray, out VisualGroundHit hit)) return false;
        xCm = hit.WorldXCm;
        yCm = hit.WorldYCm;
        return true;
    }
}

internal static class CrowdAuthoritativeInput
{
    public static FrozenInputActionReader Require(GameEngine engine)
    {
        var input = engine.GetService(CoreServiceKeys.AuthoritativeInput);
        if (input is FrozenInputActionReader frozen) return frozen;
        throw new InvalidOperationException("固定步群体输入需要 AuthoritativeInput 的冻结快照。");
    }
}
