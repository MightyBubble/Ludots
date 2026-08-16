using System.Numerics;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardGraphBehaviorCommon;

/// <summary>Readable stage drawing for graph-behavior demos (paths, actors, aggro, gates).</summary>
public static class GraphShowcaseStagePresenter
{
    public static readonly DebugDrawColor GuardColor = DebugDrawColor.Green;
    public static readonly DebugDrawColor EnemyColor = DebugDrawColor.Red;
    public static readonly DebugDrawColor SentryIdle = DebugDrawColor.Cyan;
    public static readonly DebugDrawColor SentryAlert = DebugDrawColor.Yellow;
    public static readonly DebugDrawColor SentryCombat = DebugDrawColor.Red;
    public static readonly DebugDrawColor SentryRetreat = DebugDrawColor.Blue;
    public static readonly DebugDrawColor CasterColor = DebugDrawColor.Yellow;
    public static readonly DebugDrawColor PathColor = DebugDrawColor.Yellow;
    public static readonly DebugDrawColor GateColor = DebugDrawColor.White;
    public static readonly DebugDrawColor CrowdColor = DebugDrawColor.Gray;

    public static void Clear(DebugDrawCommandBuffer buffer) => buffer.Clear();

    public static void DrawPolyline(DebugDrawCommandBuffer buffer, Vector2[] points, DebugDrawColor color, float thickness = 0.12f)
    {
        for (int i = 0; i + 1 < points.Length; i++)
        {
            buffer.Lines.Add(new DebugDrawLine2D
            {
                A = points[i],
                B = points[i + 1],
                Thickness = thickness,
                Color = color
            });
        }

        if (points.Length > 2)
        {
            buffer.Lines.Add(new DebugDrawLine2D
            {
                A = points[^1],
                B = points[0],
                Thickness = thickness,
                Color = color
            });
        }
    }

    public static void DrawActor(DebugDrawCommandBuffer buffer, float x, float y, float radius, DebugDrawColor color, float thickness = 0.14f)
    {
        buffer.Circles.Add(new DebugDrawCircle2D
        {
            Center = new Vector2(x, y),
            Radius = radius,
            Thickness = thickness,
            Color = color
        });
    }

    public static void DrawAggroLine(DebugDrawCommandBuffer buffer, float ax, float ay, float bx, float by)
    {
        buffer.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(ax, ay),
            B = new Vector2(bx, by),
            Thickness = 0.08f,
            Color = DebugDrawColor.Red
        });
    }

    public static void DrawTriggerRing(DebugDrawCommandBuffer buffer, float x, float y, float radius, bool armed)
    {
        buffer.Circles.Add(new DebugDrawCircle2D
        {
            Center = new Vector2(x, y),
            Radius = radius,
            Thickness = armed ? 0.22f : 0.1f,
            Color = armed ? DebugDrawColor.Yellow : DebugDrawColor.Gray
        });
    }

    public static void DrawGateBar(DebugDrawCommandBuffer buffer, float y, float halfWidth, bool open)
    {
        if (open) return;
        buffer.Boxes.Add(new DebugDrawBox2D
        {
            Center = new Vector2(0f, y),
            HalfWidth = halfWidth,
            HalfHeight = 0.35f,
            Thickness = 0.12f,
            Color = GateColor
        });
    }

    public static void DrawPhasePips(DebugDrawCommandBuffer buffer, int phase, int maxPhase = 3)
    {
        for (int p = 0; p < maxPhase; p++)
        {
            bool on = p < phase;
            buffer.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(-6f + p * 2.2f, 10f),
                HalfWidth = 0.8f,
                HalfHeight = 0.35f,
                Thickness = 0.1f,
                Color = on ? DebugDrawColor.Yellow : DebugDrawColor.Gray
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
            Center = new Vector2(0f, -12.5f),
            HalfWidth = 4f * ratio,
            HalfHeight = 0.28f,
            Thickness = 0.08f,
            Color = lastThinkMs < budgetMs ? DebugDrawColor.Green : DebugDrawColor.Red
        });
    }

    public static void DrawPlayerCaption(ScreenOverlayBuffer overlay, string title, string detail)
    {
        string[] lines = string.IsNullOrEmpty(detail)
            ? Array.Empty<string>()
            : SplitCaptionLines(detail).ToArray();
        int height = 48 + Math.Max(1, lines.Length) * 22;
        overlay.AddRect(16, 16, 1568, height, new Vector4(0f, 0f, 0f, 0.72f), new Vector4(1f, 0.85f, 0.2f, 1f));
        if (!string.IsNullOrEmpty(title))
        {
            overlay.AddText(32, 24, title, 22, new Vector4(1f, 0.92f, 0.35f, 1f));
        }

        int lineY = 54;
        for (int i = 0; i < lines.Length; i++)
        {
            overlay.AddText(32, lineY, lines[i], 18, new Vector4(1f, 1f, 1f, 1f));
            lineY += 22;
        }
    }

    private static IEnumerable<string> SplitCaptionLines(string detail)
    {
        string[] parts = detail.Split('；', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length <= 1)
        {
            yield return detail;
            yield break;
        }

        for (int i = 0; i < parts.Length; i++)
        {
            yield return parts[i];
        }
    }

    public static void DrawCrowdBand(DebugDrawCommandBuffer buffer, int count, float y = -15f, int maxDots = 400)
    {
        int n = count < maxDots ? count : maxDots;
        for (int i = 0; i < n; i++)
        {
            float t = n <= 1 ? 0f : i / (float)(n - 1);
            float x = -14f + t * 28f;
            buffer.Circles.Add(new DebugDrawCircle2D
            {
                Center = new Vector2(x, y + (i % 3) * 0.25f),
                Radius = 0.12f,
                Thickness = 0.04f,
                Color = CrowdColor
            });
        }
    }
}
