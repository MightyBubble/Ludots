using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace Ludots.Tests.GAS.Production;

internal sealed record StageEvidenceActor(
    string Name,
    float PositionX,
    float PositionY,
    float Health,
    float MaxHealth,
    string Tone,
    bool IsSelected,
    string Detail);

internal sealed record StageEvidenceFrame(
    string Title,
    string Step,
    string Subtitle,
    string SelectedEntity,
    string ModeId,
    string MetaLine,
    IReadOnlyList<string> SummaryLines,
    IReadOnlyList<string> DetailLines,
    IReadOnlyList<StageEvidenceActor> Actors);

internal static class AcceptanceStageEvidenceWriter
{
    private const int ImageWidth = 1600;
    private const int ImageHeight = 900;
    private const float StageLeft = 48f;
    private const float StageTop = 108f;
    private const float StageWidth = 960f;
    private const float StageHeight = 744f;

    public static void WriteFrame(StageEvidenceFrame frame, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException("Missing screenshot directory."));

        using var surface = SKSurface.Create(new SKImageInfo(ImageWidth, ImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(7, 11, 18));

        using var titlePaint = CreatePaint(new SKColor(250, 251, 255), 30f, true);
        using var subtitlePaint = CreatePaint(new SKColor(244, 212, 108), 18f);
        using var bodyPaint = CreatePaint(new SKColor(224, 232, 240), 16f);
        using var faintPaint = CreatePaint(new SKColor(153, 171, 189), 14f);
        using var cardFill = new SKPaint { Color = new SKColor(19, 27, 38), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var cardStroke = new SKPaint { Color = new SKColor(52, 75, 102), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var stageFill = new SKPaint { Color = new SKColor(13, 20, 31), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var gridPaint = new SKPaint { Color = new SKColor(36, 49, 66), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };

        canvas.DrawRect(new SKRect(0f, 0f, ImageWidth, ImageHeight), stageFill);
        canvas.DrawText(frame.Title, 48f, 48f, titlePaint);
        canvas.DrawText($"{frame.Step} | {Trim(frame.Subtitle, 116)}", 48f, 76f, subtitlePaint);

        SKRect stageRect = new(StageLeft, StageTop, StageLeft + StageWidth, StageTop + StageHeight);
        canvas.DrawRoundRect(stageRect, 20f, 20f, cardFill);
        canvas.DrawRoundRect(stageRect, 20f, 20f, cardStroke);
        DrawStageGrid(canvas, stageRect, gridPaint);
        DrawStageActors(canvas, stageRect, frame.Actors);

        float panelLeft = stageRect.Right + 28f;
        SKRect summaryRect = new(panelLeft, StageTop, ImageWidth - 42f, StageTop + 314f);
        SKRect detailRect = new(panelLeft, summaryRect.Bottom + 18f, ImageWidth - 42f, StageTop + StageHeight);
        DrawPanel(canvas, summaryRect, "Runtime Snapshot", new[]
        {
            $"Selected: {frame.SelectedEntity}",
            $"Mode: {frame.ModeId}",
            frame.MetaLine
        }.Concat(frame.SummaryLines).ToArray(), bodyPaint, faintPaint, cardFill, cardStroke);
        DrawPanel(canvas, detailRect, "Evidence Details", frame.DetailLines, bodyPaint, faintPaint, cardFill, cardStroke);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static void DrawStageGrid(SKCanvas canvas, SKRect rect, SKPaint paint)
    {
        for (int i = 1; i < 8; i++)
        {
            float x = rect.Left + rect.Width * i / 8f;
            canvas.DrawLine(x, rect.Top + 20f, x, rect.Bottom - 20f, paint);
        }

        for (int i = 1; i < 6; i++)
        {
            float y = rect.Top + rect.Height * i / 6f;
            canvas.DrawLine(rect.Left + 20f, y, rect.Right - 20f, y, paint);
        }
    }

    private static void DrawStageActors(SKCanvas canvas, SKRect rect, IReadOnlyList<StageEvidenceActor> actors)
    {
        if (actors.Count == 0)
        {
            return;
        }

        (float minX, float maxX, float minY, float maxY) = ComputeBounds(actors);

        using var labelPaint = CreatePaint(new SKColor(242, 246, 250), 14f, true);
        using var detailPaint = CreatePaint(new SKColor(183, 197, 213), 12f);
        using var healthBack = new SKPaint { Color = new SKColor(21, 26, 34), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var selectionPaint = new SKPaint { Color = new SKColor(247, 211, 109), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f };

        for (int i = 0; i < actors.Count; i++)
        {
            StageEvidenceActor actor = actors[i];
            SKPoint point = ToStagePoint(actor, rect, minX, maxX, minY, maxY);
            float radius = actor.IsSelected ? 15f : 11f;
            SKColor color = ResolveTone(actor.Tone);
            using var actorPaint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
            using var actorStroke = new SKPaint { Color = color.WithAlpha(220), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
            using var healthFill = new SKPaint { Color = actor.Health > 0f ? new SKColor(111, 223, 149) : new SKColor(211, 88, 88), IsAntialias = true, Style = SKPaintStyle.Fill };

            if (actor.IsSelected)
            {
                canvas.DrawCircle(point.X, point.Y, radius + 8f, selectionPaint);
            }

            canvas.DrawCircle(point.X, point.Y, radius, actorPaint);
            canvas.DrawCircle(point.X, point.Y, radius, actorStroke);

            float barWidth = 76f;
            float rawHealthRatio = actor.MaxHealth > 0f
                ? actor.Health / actor.MaxHealth
                : actor.Health > 0f ? 1f : 0f;
            float healthRatio = actor.Health <= 0f
                ? 0f
                : Math.Clamp(rawHealthRatio, 0.06f, 1f);
            SKRect barRect = new(point.X - barWidth * 0.5f, point.Y - radius - 16f, point.X + barWidth * 0.5f, point.Y - radius - 9f);
            canvas.DrawRoundRect(barRect, 3f, 3f, healthBack);
            canvas.DrawRoundRect(new SKRect(barRect.Left, barRect.Top, barRect.Left + barWidth * healthRatio, barRect.Bottom), 3f, 3f, healthFill);

            float labelY = point.Y + radius + (i % 2 == 0 ? 20f : 34f);
            canvas.DrawText(Trim(actor.Name, 22), point.X - 10f, labelY, labelPaint);
            canvas.DrawText(Trim(actor.Detail, 32), point.X - 10f, labelY + 16f, detailPaint);
        }
    }

    private static void DrawPanel(
        SKCanvas canvas,
        SKRect rect,
        string title,
        IReadOnlyList<string> lines,
        SKPaint bodyPaint,
        SKPaint faintPaint,
        SKPaint fillPaint,
        SKPaint strokePaint)
    {
        canvas.DrawRoundRect(rect, 18f, 18f, fillPaint);
        canvas.DrawRoundRect(rect, 18f, 18f, strokePaint);

        using var titlePaint = CreatePaint(new SKColor(248, 250, 252), 20f, true);
        canvas.DrawText(title, rect.Left + 20f, rect.Top + 30f, titlePaint);

        float y = rect.Top + 62f;
        for (int i = 0; i < lines.Count; i++)
        {
            if (y > rect.Bottom - 18f)
            {
                break;
            }

            string line = Trim(lines[i], 74);
            canvas.DrawText(line, rect.Left + 20f, y, i < 3 ? bodyPaint : faintPaint);
            y += (i < 3 ? bodyPaint.TextSize : faintPaint.TextSize) + 11f;
        }
    }

    private static (float MinX, float MaxX, float MinY, float MaxY) ComputeBounds(IReadOnlyList<StageEvidenceActor> actors)
    {
        float minX = actors.Min(actor => actor.PositionX);
        float maxX = actors.Max(actor => actor.PositionX);
        float minY = actors.Min(actor => actor.PositionY);
        float maxY = actors.Max(actor => actor.PositionY);
        float padX = Math.Max(180f, (maxX - minX) * 0.18f);
        float padY = Math.Max(140f, (maxY - minY) * 0.22f);
        return (minX - padX, maxX + padX, minY - padY, maxY + padY);
    }

    private static SKPoint ToStagePoint(StageEvidenceActor actor, SKRect rect, float minX, float maxX, float minY, float maxY)
    {
        float normalizedX = maxX - minX <= 1f ? 0.5f : (actor.PositionX - minX) / (maxX - minX);
        float normalizedY = maxY - minY <= 1f ? 0.5f : (actor.PositionY - minY) / (maxY - minY);
        float x = rect.Left + 42f + normalizedX * (rect.Width - 84f);
        float y = rect.Bottom - 42f - normalizedY * (rect.Height - 84f);
        return new SKPoint(x, y);
    }

    private static SKColor ResolveTone(string tone)
    {
        return tone switch
        {
            "ally" => new SKColor(101, 191, 255),
            "enemy" => new SKColor(255, 128, 110),
            "summon" => new SKColor(243, 190, 82),
            "neutral" => new SKColor(170, 181, 195),
            "support" => new SKColor(126, 231, 164),
            _ => new SKColor(187, 198, 211)
        };
    }

    private static SKPaint CreatePaint(SKColor color, float textSize, bool bold = false)
    {
        return new SKPaint
        {
            Color = color,
            TextSize = textSize,
            IsAntialias = true,
            FakeBoldText = bold
        };
    }

    private static string Trim(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..Math.Max(0, maxLength - 3)] + "...";
    }
}
