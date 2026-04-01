using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ludots.UI;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using SkiaSharp;

namespace Ludots.Tests.GAS.Production;

internal sealed record UiAcceptanceEvidenceFrame(
    string Step,
    string ScreenshotFileName,
    string When,
    string Who,
    string What,
    string Where,
    string Why,
    string How,
    IReadOnlyList<string> UiHead);

internal static class AcceptanceUiEvidenceWriter
{
    private const int ExportWidth = 1920;
    private const int ExportHeight = 1080;
    private const int TimelineColumns = 2;
    private const int TimelineTileWidth = 960;
    private const int TimelineTileHeight = 620;
    private const int ScreenshotMargin = 22;

    public static IReadOnlyList<string> ExtractUiText(UIRoot root)
    {
        if (root.Scene?.Root == null)
        {
            return Array.Empty<string>();
        }

        var lines = new List<string>();
        CollectUiText(root.Scene.Root, lines);
        return lines;
    }

    public static UiAcceptanceEvidenceFrame CaptureFrame(
        UIRoot root,
        string screensDir,
        int order,
        string step,
        string when,
        string who,
        string what,
        string where,
        string why,
        string how)
    {
        string fileName = $"{order:000}_{step}.png";
        string outputPath = Path.Combine(screensDir, fileName);
        ExportUiScene(root, outputPath);
        return new UiAcceptanceEvidenceFrame(
            step,
            fileName,
            when,
            who,
            what,
            where,
            why,
            how,
            ExtractUiText(root).Take(10).ToArray());
    }

    public static void ResetArtifactDirectory(string artifactDir, string screensDir)
    {
        Directory.CreateDirectory(artifactDir);
        Directory.CreateDirectory(screensDir);

        foreach (string file in Directory.GetFiles(screensDir, "*.png"))
        {
            File.Delete(file);
        }

        foreach (string fileName in new[] { "trace.jsonl", "battle-report.md", "path.mmd", "5w1h.md" })
        {
            string path = Path.Combine(artifactDir, fileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    public static void ExportUiScene(UIRoot root, string outputPath, string backgroundHex = "#060B12")
    {
        if (root.Scene == null)
        {
            throw new InvalidOperationException("UIRoot does not have a mounted scene.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException("Missing screenshot directory."));
        float sceneWidth = root.Width > 0f ? root.Width : 1920f;
        float sceneHeight = root.Height > 0f ? root.Height : 1080f;

        using var surface = SKSurface.Create(new SKImageInfo(ExportWidth, ExportHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(ParseColor(backgroundHex));

        var renderer = new SkiaUiRenderer();
        renderer.RenderToCanvas(root.Scene, canvas, sceneWidth, sceneHeight);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    public static void WriteTimelineSheet(IReadOnlyList<UiAcceptanceEvidenceFrame> frames, string screensDir, string outputPath, string title)
    {
        if (frames.Count == 0)
        {
            return;
        }

        int rows = (int)Math.Ceiling(frames.Count / (double)TimelineColumns);
        int width = TimelineColumns * TimelineTileWidth;
        int height = rows * TimelineTileHeight + 72;

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(8, 10, 16));

        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 28f };
        using var labelPaint = new SKPaint { Color = new SKColor(246, 212, 108), IsAntialias = true, TextSize = 20f };
        using var detailPaint = new SKPaint { Color = new SKColor(202, 214, 226), IsAntialias = true, TextSize = 16f };
        using var faintPaint = new SKPaint { Color = new SKColor(149, 166, 184), IsAntialias = true, TextSize = 14f };
        using var cardFill = new SKPaint { Color = new SKColor(16, 22, 32), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var cardStroke = new SKPaint { Color = new SKColor(42, 62, 84), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };

        canvas.DrawText(title, 24, 42, titlePaint);

        for (int i = 0; i < frames.Count; i++)
        {
            UiAcceptanceEvidenceFrame frame = frames[i];
            int column = i % TimelineColumns;
            int row = i / TimelineColumns;
            float originX = column * TimelineTileWidth + 16f;
            float originY = row * TimelineTileHeight + 72f;
            SKRect cardRect = new(originX, originY, originX + TimelineTileWidth - 32f, originY + TimelineTileHeight - 20f);
            canvas.DrawRoundRect(cardRect, 18f, 18f, cardFill);
            canvas.DrawRoundRect(cardRect, 18f, 18f, cardStroke);

            string screenshotPath = Path.Combine(screensDir, frame.ScreenshotFileName);
            if (File.Exists(screenshotPath))
            {
                using SKBitmap bitmap = SKBitmap.Decode(screenshotPath);
                SKRect imageRect = new(
                    cardRect.Left + ScreenshotMargin,
                    cardRect.Top + ScreenshotMargin,
                    cardRect.Right - ScreenshotMargin,
                    cardRect.Top + 340f);
                canvas.DrawBitmap(bitmap, imageRect);
            }

            float textX = cardRect.Left + 22f;
            float textY = cardRect.Top + 382f;
            canvas.DrawText($"{i + 1:000} {frame.When} | {frame.Step}", textX, textY, labelPaint);
            textY += 28f;
            DrawLine(canvas, detailPaint, textX, ref textY, $"Who: {Trim(frame.Who, 90)}");
            DrawLine(canvas, detailPaint, textX, ref textY, $"What: {Trim(frame.What, 90)}");
            DrawLine(canvas, faintPaint, textX, ref textY, $"Where: {Trim(frame.Where, 90)}");
            DrawLine(canvas, faintPaint, textX, ref textY, $"Why: {Trim(frame.Why, 90)}");
            DrawLine(canvas, faintPaint, textX, ref textY, $"How: {Trim(frame.How, 90)}");
        }

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    public static void WriteFiveWOneHMarkdown(string scenarioId, IReadOnlyList<UiAcceptanceEvidenceFrame> frames, string outputPath)
    {
        var lines = new List<string>(frames.Count * 10 + 4)
        {
            $"# 5W1H Flow: {scenarioId}",
            string.Empty
        };

        for (int i = 0; i < frames.Count; i++)
        {
            UiAcceptanceEvidenceFrame frame = frames[i];
            lines.Add($"## {i + 1:000} - {frame.When} - {frame.Step}");
            lines.Add($"- screenshot: `screens/{frame.ScreenshotFileName}`");
            lines.Add($"- who: {frame.Who}");
            lines.Add($"- what: {frame.What}");
            lines.Add($"- when: {frame.When}");
            lines.Add($"- where: {frame.Where}");
            lines.Add($"- why: {frame.Why}");
            lines.Add($"- how: {frame.How}");
            lines.Add($"- ui_head: `{string.Join(" | ", frame.UiHead.Take(5))}`");
            lines.Add(string.Empty);
        }

        File.WriteAllLines(outputPath, lines);
    }

    private static void CollectUiText(UiNode node, List<string> lines)
    {
        if (!string.IsNullOrWhiteSpace(node.TextContent))
        {
            lines.Add(node.TextContent.Trim());
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            CollectUiText(node.Children[i], lines);
        }
    }

    private static void DrawLine(SKCanvas canvas, SKPaint paint, float x, ref float y, string text)
    {
        canvas.DrawText(text, x, y, paint);
        y += paint.TextSize + 8f;
    }

    private static string Trim(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static SKColor ParseColor(string value)
    {
        return SKColor.TryParse(value, out SKColor color)
            ? color
            : new SKColor(6, 11, 18);
    }
}
