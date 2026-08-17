using System.Globalization;
using System.Numerics;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;

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
    public static readonly DebugDrawColor GhostColor = DebugDrawColor.Gray;
    public static readonly DebugDrawColor OutlineDark = new DebugDrawColor(56, 56, 56);

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

    /// <summary>Draws a line with optional 45-degree arrowheads on either end.</summary>
    public static void DrawDirectedLine(DebugDrawCommandBuffer buffer, float ax, float ay, float bx, float by, float thickness, DebugDrawColor color, bool arrowStart = false, bool arrowEnd = true)
    {
        buffer.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(ax, ay),
            B = new Vector2(bx, by),
            Thickness = thickness,
            Color = color
        });
        DrawLineArrowheads(buffer, ax, ay, bx, by, thickness, color, arrowStart, arrowEnd);
    }

    private static void DrawLineArrowheads(DebugDrawCommandBuffer buffer, float ax, float ay, float bx, float by, float thickness, DebugDrawColor color, bool arrowStart, bool arrowEnd)
    {
        float dx = bx - ax;
        float dy = by - ay;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-4f) return;
        // Wing floor 0.3m: below ~3.75m of line the 8% ratio alone shrinks wings under one stroke width.
        float wing = MathF.Max(0.3f, len * 0.08f);
        if (arrowEnd) DrawArrowHead(buffer, bx, by, dx / len, dy / len, wing, thickness, color);
        if (arrowStart) DrawArrowHead(buffer, ax, ay, -dx / len, -dy / len, wing, thickness, color);
    }

    private static void DrawArrowHead(DebugDrawCommandBuffer buffer, float tipX, float tipY, float dirX, float dirY, float wingLength, float thickness, DebugDrawColor color)
    {
        // dir points along travel direction; wings rotate the reversed direction by +/-45 degrees.
        const float cos45 = 0.70710677f;
        float c = cos45 * wingLength;
        Line(buffer, tipX, tipY, tipX - dirX * c + dirY * c, tipY - dirX * c - dirY * c, thickness, color);
        Line(buffer, tipX, tipY, tipX - dirX * c - dirY * c, tipY + dirX * c - dirY * c, thickness, color);
    }

    /// <summary>Draws a dashed directed line (0.25m dash, 0.15m gap) with optional arrowheads on either end.</summary>
    public static void DrawDashedDirectedLine(DebugDrawCommandBuffer buffer, float ax, float ay, float bx, float by, float thickness, DebugDrawColor color, bool arrowStart = false, bool arrowEnd = true)
    {
        DrawDashedSegment(buffer, ax, ay, bx, by, thickness, color);
        DrawLineArrowheads(buffer, ax, ay, bx, by, thickness, color, arrowStart, arrowEnd);
    }

    private static void DrawDashedSegment(DebugDrawCommandBuffer buffer, float ax, float ay, float bx, float by, float thickness, DebugDrawColor color)
    {
        const float dash = 0.25f;
        const float gap = 0.15f;
        float dx = bx - ax;
        float dy = by - ay;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-4f) return;
        float ux = dx / len;
        float uy = dy / len;
        for (float start = 0f; start < len; start += dash + gap)
        {
            float end = MathF.Min(start + dash, len);
            Line(buffer, ax + ux * start, ay + uy * start, ax + ux * end, ay + uy * end, thickness, color);
            if (end >= len) break;
        }
    }

    /// <summary>Badge glyph kinds drawn by <see cref="DrawBadge"/> (footprint ~0.5m at scale 1).</summary>
    public enum BadgeKind
    {
        Flag,
        Eye,
        Bell,
        Diamond,
        Flame,
        Check,
        Cross,
        Ring
    }

    /// <summary>Draws a small status glyph centered at (x, y); the caller owns the overhead y offset.</summary>
    public static void DrawBadge(DebugDrawCommandBuffer buffer, float x, float y, BadgeKind kind, DebugDrawColor color, float scale = 1f)
    {
        switch (kind)
        {
            case BadgeKind.Flag: DrawFlagBadge(buffer, x, y, color, scale); break;
            case BadgeKind.Eye: DrawEyeBadge(buffer, x, y, color, scale); break;
            case BadgeKind.Bell: DrawBellBadge(buffer, x, y, color, scale); break;
            case BadgeKind.Diamond: DrawDiamondBadge(buffer, x, y, color, scale); break;
            case BadgeKind.Flame: DrawFlameBadge(buffer, x, y, color, scale); break;
            case BadgeKind.Check: DrawCheckBadge(buffer, x, y, color, scale); break;
            case BadgeKind.Cross: DrawCrossBadge(buffer, x, y, color, scale); break;
            case BadgeKind.Ring: DrawRingBadge(buffer, x, y, color, scale); break;
        }
    }

    private static void DrawFlagBadge(DebugDrawCommandBuffer buffer, float x, float y, DebugDrawColor color, float scale)
    {
        float poleX = x - 0.22f * scale;
        float thickness = 0.06f * scale;
        Line(buffer, poleX, y - 0.28f * scale, poleX, y + 0.28f * scale, thickness, color);
        Line(buffer, poleX, y + 0.28f * scale, x + 0.26f * scale, y + 0.17f * scale, thickness, color);
        Line(buffer, x + 0.26f * scale, y + 0.17f * scale, poleX, y + 0.06f * scale, thickness, color);
    }

    private static void DrawEyeBadge(DebugDrawCommandBuffer buffer, float x, float y, DebugDrawColor color, float scale)
    {
        Circle(buffer, x, y, 0.26f * scale, 0.06f * scale, color);
        Circle(buffer, x, y, 0.09f * scale, 0.06f * scale, color);
    }

    private static void DrawBellBadge(DebugDrawCommandBuffer buffer, float x, float y, DebugDrawColor color, float scale)
    {
        float r = 0.26f * scale;
        float thickness = 0.06f * scale;
        Arc(buffer, x, y, r, 0f, MathF.PI, thickness, color, segments: 6);
        Line(buffer, x - r, y, x + r, y, thickness, color);
        Line(buffer, x + 0.04f * scale, y, x + 0.18f * scale, y - 0.14f * scale, thickness, color);
    }

    private static void DrawDiamondBadge(DebugDrawCommandBuffer buffer, float x, float y, DebugDrawColor color, float scale)
    {
        buffer.Boxes.Add(new DebugDrawBox2D
        {
            Center = new Vector2(x, y),
            HalfWidth = 0.18f * scale,
            HalfHeight = 0.18f * scale,
            RotationRadians = MathF.PI / 4f,
            Thickness = 0.06f * scale,
            Color = color
        });
    }

    private static void DrawFlameBadge(DebugDrawCommandBuffer buffer, float x, float y, DebugDrawColor color, float scale)
    {
        DrawPolyline(buffer, new[]
        {
            new Vector2(x, y + 0.3f * scale),
            new Vector2(x + 0.22f * scale, y - 0.24f * scale),
            new Vector2(x - 0.22f * scale, y - 0.24f * scale)
        }, color, 0.06f * scale);
        Line(buffer, x - 0.08f * scale, y - 0.14f * scale, x, y + 0.08f * scale, 0.05f * scale, color);
        Line(buffer, x, y + 0.08f * scale, x + 0.08f * scale, y - 0.14f * scale, 0.05f * scale, color);
    }

    private static void DrawCheckBadge(DebugDrawCommandBuffer buffer, float x, float y, DebugDrawColor color, float scale)
    {
        float thickness = 0.08f * scale;
        Line(buffer, x - 0.22f * scale, y + 0.02f * scale, x - 0.06f * scale, y - 0.2f * scale, thickness, color);
        Line(buffer, x - 0.06f * scale, y - 0.2f * scale, x + 0.26f * scale, y + 0.24f * scale, thickness, color);
    }

    private static void DrawCrossBadge(DebugDrawCommandBuffer buffer, float x, float y, DebugDrawColor color, float scale)
    {
        float thickness = 0.08f * scale;
        Line(buffer, x - 0.2f * scale, y - 0.2f * scale, x + 0.2f * scale, y + 0.2f * scale, thickness, color);
        Line(buffer, x - 0.2f * scale, y + 0.2f * scale, x + 0.2f * scale, y - 0.2f * scale, thickness, color);
    }

    private static void DrawRingBadge(DebugDrawCommandBuffer buffer, float x, float y, DebugDrawColor color, float scale)
    {
        Circle(buffer, x, y, 0.26f * scale, 0.05f * scale, color);
        Circle(buffer, x, y, 0.14f * scale, 0.05f * scale, color);
    }

    /// <summary>Draws a ghost (afterimage) circle as 8 broken arc dashes at 60% duty cycle.</summary>
    public static void DrawGhostCircle(DebugDrawCommandBuffer buffer, float x, float y, float radius, DebugDrawColor color)
    {
        const int dashes = 8;
        float period = MathF.PI * 2f / dashes;
        for (int k = 0; k < dashes; k++)
        {
            Arc(buffer, x, y, radius, k * period, k * period + period * 0.6f, 0.06f, color);
        }
    }

    /// <summary>Draws a ghost (afterimage) segment as a thin dashed line (0.25m dash, 0.15m gap).</summary>
    public static void DrawGhostSegment(DebugDrawCommandBuffer buffer, float ax, float ay, float bx, float by, DebugDrawColor color)
    {
        DrawDashedSegment(buffer, ax, ay, bx, by, 0.06f, color);
    }

    private static void Arc(DebugDrawCommandBuffer buffer, float cx, float cy, float radius, float fromRad, float toRad, float thickness, DebugDrawColor color, int segments = 3)
    {
        float px = cx + MathF.Cos(fromRad) * radius;
        float py = cy + MathF.Sin(fromRad) * radius;
        for (int i = 1; i <= segments; i++)
        {
            float a = fromRad + (toRad - fromRad) * i / segments;
            float nx = cx + MathF.Cos(a) * radius;
            float ny = cy + MathF.Sin(a) * radius;
            Line(buffer, px, py, nx, ny, thickness, color);
            px = nx;
            py = ny;
        }
    }

    private const float DigitWidthRatio = 0.55f;
    private const float DigitAdvanceRatio = 0.72f;
    private const float DigitStrokeRatio = 0.1f;
    // Bit order a,b,c,d,e,f,g: a top bar, b upper-right, c lower-right, d bottom, e lower-left, f upper-left, g middle.
    private static readonly int[] SevenSegmentMasks = { 63, 6, 91, 79, 102, 109, 125, 7, 127, 111 };

    /// <summary>Draws one seven-segment digit (0-9) centered at (x, y); stroke thickness scales with heightMeters.</summary>
    public static void DrawDigit(DebugDrawCommandBuffer buffer, float x, float y, int digit, float heightMeters, DebugDrawColor color)
    {
        if ((uint)digit > 9u) return;
        float hw = heightMeters * DigitWidthRatio * 0.5f;
        float hh = heightMeters * 0.5f;
        float left = x - hw;
        float right = x + hw;
        float top = y + hh;
        float bottom = y - hh;
        float t = heightMeters * DigitStrokeRatio;
        int m = SevenSegmentMasks[digit];
        if ((m & 1) != 0) Line(buffer, left, top, right, top, t, color);
        if ((m & 2) != 0) Line(buffer, right, top, right, y, t, color);
        if ((m & 4) != 0) Line(buffer, right, y, right, bottom, t, color);
        if ((m & 8) != 0) Line(buffer, left, bottom, right, bottom, t, color);
        if ((m & 16) != 0) Line(buffer, left, y, left, bottom, t, color);
        if ((m & 32) != 0) Line(buffer, left, top, left, y, t, color);
        if ((m & 64) != 0) Line(buffer, left, y, right, y, t, color);
    }

    /// <summary>Draws an integer (including sign) in seven-segment glyphs, right-aligned with the right edge at x.</summary>
    public static void DrawNumber(DebugDrawCommandBuffer buffer, float x, float y, int value, float heightMeters, DebugDrawColor color)
    {
        string text = value.ToString(CultureInfo.InvariantCulture);
        float halfDigit = heightMeters * DigitWidthRatio * 0.5f;
        float advance = heightMeters * DigitAdvanceRatio;
        float rightEdge = x;
        for (int i = text.Length - 1; i >= 0; i--)
        {
            float cx = rightEdge - halfDigit;
            if (text[i] == '-')
            {
                Line(buffer, cx - halfDigit * 0.8f, y, cx + halfDigit * 0.8f, y, heightMeters * DigitStrokeRatio, color);
            }
            else
            {
                DrawDigit(buffer, cx, y, text[i] - '0', heightMeters, color);
            }

            rightEdge -= advance;
        }
    }

    /// <summary>Draws a panel/register board frame with horizontal separators splitting it into slots rows.</summary>
    public static void DrawPanelBox(DebugDrawCommandBuffer buffer, float x, float y, float widthMeters, float heightMeters, int slots, DebugDrawColor color)
    {
        float hw = widthMeters * 0.5f;
        float hh = heightMeters * 0.5f;
        DrawPolyline(buffer, new[]
        {
            new Vector2(x - hw, y - hh),
            new Vector2(x + hw, y - hh),
            new Vector2(x + hw, y + hh),
            new Vector2(x - hw, y + hh)
        }, color, 0.07f);
        int rows = Math.Max(1, slots);
        for (int i = 1; i < rows; i++)
        {
            float sy = y - hh + heightMeters * i / rows;
            Line(buffer, x - hw, sy, x + hw, sy, 0.05f, color);
        }
    }

    /// <summary>Draws rank pips as a centered horizontal row of 0.18m squares, one per rank.</summary>
    public static void DrawRankPips(DebugDrawCommandBuffer buffer, float x, float y, int rank, DebugDrawColor color)
    {
        const float pipHalf = 0.09f;
        const float pitch = 0.3f;
        for (int i = 0; i < rank; i++)
        {
            buffer.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(x + (i - (rank - 1) * 0.5f) * pitch, y),
                HalfWidth = pipHalf,
                HalfHeight = pipHalf,
                Thickness = 0.04f,
                Color = color
            });
        }
    }

    /// <summary>Draws a scan arc with an arrowhead at the toDeg end.</summary>
    public static void DrawArcArrow(DebugDrawCommandBuffer buffer, float cx, float cy, float radiusMeters, float fromDeg, float toDeg, DebugDrawColor color)
    {
        float from = fromDeg * MathF.PI / 180f;
        float to = toDeg * MathF.PI / 180f;
        if (MathF.Abs(to - from) < 1e-3f) return;
        int segments = Math.Max(2, (int)MathF.Ceiling(MathF.Abs(to - from) / (MathF.PI / 12f)));
        Arc(buffer, cx, cy, radiusMeters, from, to, 0.1f, color, segments);
        // Wings scale with radius, not arc length: short sweeps on large radii still need a visible head.
        float wing = MathF.Max(0.25f, radiusMeters * 0.15f);
        float sweep = to > from ? 1f : -1f;
        DrawArrowHead(buffer, cx + MathF.Cos(to) * radiusMeters, cy + MathF.Sin(to) * radiusMeters, -MathF.Sin(to) * sweep, MathF.Cos(to) * sweep, wing, 0.1f, color);
    }

    /// <summary>Draws a double-stroked circle: dark outer band behind a bright inner ring for contrast.</summary>
    public static void DrawThickOutlineCircle(DebugDrawCommandBuffer buffer, float x, float y, float radius, DebugDrawColor outerColor, DebugDrawColor innerColor)
    {
        // Same-radius strokes: the 1.8x outer band must fully contain the inner one to read as a dark halo after video compression.
        Circle(buffer, x, y, radius, 0.18f, outerColor);
        Circle(buffer, x, y, radius, 0.1f, innerColor);
    }

    private static void Line(DebugDrawCommandBuffer buffer, float ax, float ay, float bx, float by, float thickness, DebugDrawColor color)
    {
        buffer.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(ax, ay),
            B = new Vector2(bx, by),
            Thickness = thickness,
            Color = color
        });
    }

    private static void Circle(DebugDrawCommandBuffer buffer, float x, float y, float radius, float thickness, DebugDrawColor color)
    {
        buffer.Circles.Add(new DebugDrawCircle2D
        {
            Center = new Vector2(x, y),
            Radius = radius,
            Thickness = thickness,
            Color = color
        });
    }
}
