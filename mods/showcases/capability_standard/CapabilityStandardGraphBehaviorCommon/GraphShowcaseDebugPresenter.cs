using System.Numerics;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphBehaviorCommon;

public static class GraphShowcaseDebugPresenter
{
    /// <summary>World units are meters (cm * 0.01) to match Raylib debug draw.</summary>
    public static void DrawAgentDots(
        DebugDrawCommandBuffer buffer,
        int agentCount,
        System.Func<int, byte> statusOf,
        int maxDots = 400)
    {
        int n = agentCount < maxDots ? agentCount : maxDots;
        const int cols = 20;
        const float spacingM = 1.8f;
        float originX = -cols * spacingM * 0.5f;
        float originY = -cols * spacingM * 0.5f;
        for (int i = 0; i < n; i++)
        {
            int row = i / cols;
            int col = i % cols;
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
                Center = new Vector2(originX + col * spacingM, originY + row * spacingM),
                Radius = 0.55f,
                Thickness = 0.08f,
                Color = color
            });
        }

        // Budget pulse box above the field.
        buffer.Boxes.Add(new DebugDrawBox2D
        {
            Center = new Vector2(0f, originY - 3f),
            HalfWidth = 8f,
            HalfHeight = 0.6f,
            Thickness = 0.12f,
            Color = DebugDrawColor.White
        });
    }
}
