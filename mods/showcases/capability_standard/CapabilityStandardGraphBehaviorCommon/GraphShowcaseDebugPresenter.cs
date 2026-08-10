using System.Numerics;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphBehaviorCommon;

public static class GraphShowcaseDebugPresenter
{
    /// <summary>World units are meters (cm * 0.01) to match Raylib debug draw.</summary>
    public static void DrawAgentDotsAtPositions(
        DebugDrawCommandBuffer buffer,
        int agentCount,
        float[] posX,
        float[] posY,
        System.Func<int, byte> statusOf,
        int maxDots = 800)
    {
        int n = agentCount < maxDots ? agentCount : maxDots;
        for (int i = 0; i < n; i++)
        {
            byte st = statusOf(i);
            DebugDrawColor color = st switch
            {
                1 => DebugDrawColor.Yellow,
                2 => DebugDrawColor.Green,
                3 => DebugDrawColor.Red,
                _ => DebugDrawColor.Cyan
            };
            buffer.Circles.Add(new DebugDrawCircle2D
            {
                Center = new Vector2(posX[i], posY[i]),
                Radius = 0.45f,
                Thickness = 0.08f,
                Color = color
            });
        }
    }
}
