using System.Numerics;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 群体演示的地表拾取:指针位置走已绑定的 PointerPos 动作,射线走 ScreenRayProvider 与高度图。
/// 群体 HUD 面板只显示不收指针，被界面捕获的点击在输入层就已拦下。
/// </summary>
internal static class CrowdSurfacePick
{
    public static bool TryPick(
        GameEngine engine,
        IInputActionReader actions,
        out double xCm,
        out double yCm)
    {
        xCm = 0;
        yCm = 0;
        if (engine.GetService(CoreServiceKeys.UiCaptured))
        {
            return false;
        }

        Vector3 pointer = actions.ReadAction<Vector3>(ReservedInputActionIds.PointerPos);

        if (!engine.TryGetService(CoreServiceKeys.ContinuousHeightmap, out IContinuousHeightmap? heightmap) ||
            heightmap == null ||
            !engine.TryGetService(CoreServiceKeys.ScreenRayProvider, out IScreenRayProvider? rays) ||
            rays == null)
        {
            return false;
        }

        if (!TryRaycast(rays, heightmap, pointer.X, pointer.Y, out float worldX, out float worldY, out _)) return false;
        xCm = worldX;
        yCm = worldY;
        return true;
    }

    public static bool TryRaycast(IScreenRayProvider rays, IContinuousHeightmap height, float x, float y, out float worldX, out float worldY, out float heightCm)
    {
        var ray = rays.GetRay(new Vector2(x, y));
        if (!height.TryRaycastGround(in ray, out var hit))
        {
            worldX = worldY = heightCm = 0f;
            return false;
        }

        worldX = hit.WorldXCm;
        worldY = hit.WorldYCm;
        heightCm = hit.HeightCm;
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
