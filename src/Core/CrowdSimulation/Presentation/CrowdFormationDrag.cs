using System;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 右键拖线：中点是目标，长度是队宽，朝向是拖动方向旋转 −90°。
/// </summary>
public static class CrowdFormationDrag
{
    public static void FromLine(
        float x0,
        float y0,
        float x1,
        float y1,
        out float midX,
        out float midY,
        out int widthCm,
        out float faceX,
        out float faceY)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float len = MathF.Sqrt((dx * dx) + (dy * dy));
        midX = (x0 + x1) * 0.5f;
        midY = (y0 + y1) * 0.5f;
        if (len < 1e-3f)
        {
            widthCm = 0;
            faceX = 1f;
            faceY = 0f;
            return;
        }

        widthCm = (int)MathF.Round(len);
        faceX = dy / len;
        faceY = -dx / len;
    }
}
