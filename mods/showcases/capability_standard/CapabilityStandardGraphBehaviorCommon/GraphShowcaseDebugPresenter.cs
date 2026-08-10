using System.Numerics;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphBehaviorCommon;

public static class GraphShowcaseDebugPresenter
{
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
                4 => DebugDrawColor.Cyan,
                5 => DebugDrawColor.Blue,
                _ => DebugDrawColor.Gray
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

    public static void DrawBudgetBar(DebugDrawCommandBuffer buffer, double lastThinkMs, float budgetMs = 5f)
    {
        float ratio = budgetMs <= 0 ? 1f : (float)(lastThinkMs / budgetMs);
        if (ratio < 0f) ratio = 0f;
        if (ratio > 1.5f) ratio = 1.5f;
        buffer.Boxes.Add(new DebugDrawBox2D
        {
            Center = new Vector2(0f, -18f),
            HalfWidth = 10f * ratio,
            HalfHeight = 0.5f,
            Thickness = 0.1f,
            Color = lastThinkMs < budgetMs ? DebugDrawColor.Green : DebugDrawColor.Red
        });
    }

    public static void DrawPhaseRings(DebugDrawCommandBuffer buffer, int phase)
    {
        for (int p = 1; p <= 3; p++)
        {
            buffer.Circles.Add(new DebugDrawCircle2D
            {
                Center = Vector2.Zero,
                Radius = 4f * p,
                Thickness = p == phase ? 0.25f : 0.08f,
                Color = p == phase ? DebugDrawColor.Yellow : DebugDrawColor.Gray
            });
        }
    }
}
