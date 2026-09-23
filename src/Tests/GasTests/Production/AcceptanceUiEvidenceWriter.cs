using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
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

    public static UiAcceptanceEvidenceFrame CaptureCompositeFrame(
        GameEngine engine,
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
        ExportCompositeScene(engine, root, outputPath);
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

    public static void ExportCompositeScene(GameEngine engine, UIRoot root, string outputPath)
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
        DrawThreeKingdomsBattlefield(canvas, engine);

        var renderer = new SkiaUiRenderer();
        renderer.RenderToCanvas(root.Scene, canvas, sceneWidth, sceneHeight);
        DrawScreenOverlay(canvas, engine.GetService(CoreServiceKeys.ScreenOverlayBuffer));

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

    private static void DrawThreeKingdomsBattlefield(SKCanvas canvas, GameEngine engine)
    {
        canvas.Clear(new SKColor(9, 14, 22));
        using var fogPaint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0f, 0f),
                new SKPoint(ExportWidth, ExportHeight),
                new[] { new SKColor(14, 22, 34), new SKColor(8, 11, 18) },
                null,
                SKShaderTileMode.Clamp),
            IsAntialias = true
        };
        canvas.DrawRect(new SKRect(0, 0, ExportWidth, ExportHeight), fogPaint);

        var entities = CaptureNamedEntities(engine).ToArray();
        if (entities.Length == 0)
        {
            return;
        }

        WorldBounds bounds = ResolveBounds(entities, engine.GetService(CoreServiceKeys.RoadSplineBuffer));
        DrawBattlefieldBands(canvas, bounds);
        DrawRoadSplines(canvas, bounds, engine.GetService(CoreServiceKeys.RoadSplineBuffer));
        DrawAttachmentLinks(canvas, bounds, entities);
        DrawEntities(canvas, bounds, entities, ResolveSelectedEntity(engine));
    }

    private static void DrawScreenOverlay(SKCanvas canvas, ScreenOverlayBuffer? overlay)
    {
        if (overlay == null || overlay.Count == 0)
        {
            return;
        }

        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var strokePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var textPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        ReadOnlySpan<ScreenOverlayItem> span = overlay.GetSpan();
        for (int i = 0; i < span.Length; i++)
        {
            ref readonly ScreenOverlayItem item = ref span[i];
            switch (item.Kind)
            {
                case ScreenOverlayItemKind.Rect:
                {
                    SKRect rect = new(item.X, item.Y, item.X + item.Width, item.Y + item.Height);
                    fillPaint.Color = ToSkColor(item.BackgroundColor);
                    strokePaint.Color = ToSkColor(item.Color);
                    canvas.DrawRect(rect, fillPaint);
                    if (item.Color.W > 0.01f)
                    {
                        canvas.DrawRect(rect, strokePaint);
                    }

                    break;
                }

                case ScreenOverlayItemKind.Text:
                {
                    string? text = item.Text.HasValue ? null : overlay.GetString(item.StringId);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    textPaint.Color = ToSkColor(item.Color);
                    textPaint.TextSize = item.FontSize <= 0 ? 16 : item.FontSize;
                    canvas.DrawText(text, item.X, item.Y, textPaint);
                    break;
                }
            }
        }
    }

    private static IEnumerable<CapturedEntity> CaptureNamedEntities(GameEngine engine)
    {
        var world = engine.World;
        var query = new QueryDescription().WithAll<Name, WorldPositionCm>();
        var captured = new List<CapturedEntity>(64);
        world.Query(in query, (Entity entity, ref Name name, ref WorldPositionCm position) =>
        {
            int teamId = world.Has<Team>(entity) ? world.Get<Team>(entity).Id : 0;
            Entity parent = world.Has<ChildOf>(entity) ? world.Get<ChildOf>(entity).Parent : Entity.Null;
            string? parentName = parent != Entity.Null && world.Has<Name>(parent) ? world.Get<Name>(parent).Value : null;
            captured.Add(new CapturedEntity(
                entity,
                name.Value,
                position.Value.ToVector2(),
                teamId,
                parent,
                parentName));
        });

        return captured;
    }

    private static Entity ResolveSelectedEntity(GameEngine engine)
    {
        return SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity selected)
            ? selected
            : Entity.Null;
    }

    private static WorldBounds ResolveBounds(IReadOnlyList<CapturedEntity> entities, RoadSplineBuffer? roadSplines)
    {
        float minX = entities.Min(item => item.WorldCm.X);
        float maxX = entities.Max(item => item.WorldCm.X);
        float minY = entities.Min(item => item.WorldCm.Y);
        float maxY = entities.Max(item => item.WorldCm.Y);

        if (roadSplines != null)
        {
            for (int i = 0; i < roadSplines.Count; i++)
            {
                IncludePoint(ref minX, ref maxX, ref minY, ref maxY, roadSplines.P0X[i] * 100f, roadSplines.P0Z[i] * 100f);
                IncludePoint(ref minX, ref maxX, ref minY, ref maxY, roadSplines.P1X[i] * 100f, roadSplines.P1Z[i] * 100f);
                IncludePoint(ref minX, ref maxX, ref minY, ref maxY, roadSplines.P2X[i] * 100f, roadSplines.P2Z[i] * 100f);
                IncludePoint(ref minX, ref maxX, ref minY, ref maxY, roadSplines.P3X[i] * 100f, roadSplines.P3Z[i] * 100f);
            }
        }

        float paddingX = MathF.Max(2200f, (maxX - minX) * 0.12f);
        float paddingY = MathF.Max(1800f, (maxY - minY) * 0.18f);
        return new WorldBounds(minX - paddingX, maxX + paddingX, minY - paddingY, maxY + paddingY);
    }

    private static void IncludePoint(ref float minX, ref float maxX, ref float minY, ref float maxY, float x, float y)
    {
        minX = MathF.Min(minX, x);
        maxX = MathF.Max(maxX, x);
        minY = MathF.Min(minY, y);
        maxY = MathF.Max(maxY, y);
    }

    private static void DrawBattlefieldBands(SKCanvas canvas, WorldBounds bounds)
    {
        using var lanePaint = new SKPaint { Color = new SKColor(32, 42, 24, 120), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var siegePaint = new SKPaint { Color = new SKColor(53, 43, 24, 110), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var gridPaint = new SKPaint { Color = new SKColor(42, 58, 78, 90), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };

        float midY = bounds.MinY + (bounds.MaxY - bounds.MinY) * 0.5f;
        SKRect laneRect = RectFromWorld(bounds, bounds.MinX, midY - 2200f, bounds.MaxX, midY + 2200f);
        SKRect siegeRect = RectFromWorld(bounds, bounds.MinX, midY - 5200f, bounds.MaxX, midY + 5200f);
        canvas.DrawRect(siegeRect, siegePaint);
        canvas.DrawRect(laneRect, lanePaint);

        for (float x = bounds.MinX; x <= bounds.MaxX; x += 2000f)
        {
            SKPoint from = ToScreen(bounds, new Vector2(x, bounds.MinY));
            SKPoint to = ToScreen(bounds, new Vector2(x, bounds.MaxY));
            canvas.DrawLine(from, to, gridPaint);
        }

        for (float y = bounds.MinY; y <= bounds.MaxY; y += 2000f)
        {
            SKPoint from = ToScreen(bounds, new Vector2(bounds.MinX, y));
            SKPoint to = ToScreen(bounds, new Vector2(bounds.MaxX, y));
            canvas.DrawLine(from, to, gridPaint);
        }
    }

    private static void DrawRoadSplines(SKCanvas canvas, WorldBounds bounds, RoadSplineBuffer? roadSplines)
    {
        if (roadSplines == null || roadSplines.Count == 0)
        {
            return;
        }

        using var roadGlow = new SKPaint { Color = new SKColor(201, 155, 76, 90), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 10f };
        using var roadCore = new SKPaint { Color = new SKColor(240, 190, 96), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f };
        for (int i = 0; i < roadSplines.Count; i++)
        {
            using var path = new SKPath();
            path.MoveTo(ToScreen(bounds, new Vector2(roadSplines.P0X[i] * 100f, roadSplines.P0Z[i] * 100f)));
            path.CubicTo(
                ToScreen(bounds, new Vector2(roadSplines.P1X[i] * 100f, roadSplines.P1Z[i] * 100f)),
                ToScreen(bounds, new Vector2(roadSplines.P2X[i] * 100f, roadSplines.P2Z[i] * 100f)),
                ToScreen(bounds, new Vector2(roadSplines.P3X[i] * 100f, roadSplines.P3Z[i] * 100f)));
            canvas.DrawPath(path, roadGlow);
            canvas.DrawPath(path, roadCore);
        }
    }

    private static void DrawAttachmentLinks(SKCanvas canvas, WorldBounds bounds, IReadOnlyList<CapturedEntity> entities)
    {
        using var linkPaint = new SKPaint { Color = new SKColor(131, 183, 255, 150), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        var lookup = entities.ToDictionary(item => item.Entity, item => item);
        for (int i = 0; i < entities.Count; i++)
        {
            CapturedEntity entity = entities[i];
            if (entity.Parent == Entity.Null || !lookup.TryGetValue(entity.Parent, out CapturedEntity parent))
            {
                continue;
            }

            canvas.DrawLine(ToScreen(bounds, entity.WorldCm), ToScreen(bounds, parent.WorldCm), linkPaint);
        }
    }

    private static void DrawEntities(SKCanvas canvas, WorldBounds bounds, IReadOnlyList<CapturedEntity> entities, Entity selected)
    {
        using var labelPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 18f };
        using var labelBackdropPaint = new SKPaint { Color = new SKColor(6, 10, 16, 204), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var labelStrokePaint = new SKPaint { Color = new SKColor(63, 92, 118, 214), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var selectedStroke = new SKPaint { Color = new SKColor(114, 212, 255), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f };
        var occupiedLabels = new List<SKRect>(32);

        foreach (CapturedEntity entity in entities.OrderBy(ResolveDrawPriority).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            SKPoint point = ToScreen(bounds, entity.WorldCm);
            EntityVisualStyle style = ResolveStyle(entity);
            using var fill = new SKPaint { Color = style.Fill, IsAntialias = true, Style = SKPaintStyle.Fill };
            using var stroke = new SKPaint { Color = style.Stroke, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };

            if (style.Shape == EntityShape.Rect)
            {
                SKRect rect = new(point.X - style.SizeX, point.Y - style.SizeY, point.X + style.SizeX, point.Y + style.SizeY);
                canvas.DrawRoundRect(rect, 10f, 10f, fill);
                canvas.DrawRoundRect(rect, 10f, 10f, stroke);
            }
            else
            {
                canvas.DrawCircle(point, style.Radius, fill);
                canvas.DrawCircle(point, style.Radius, stroke);
            }

            if (entity.Entity == selected)
            {
                canvas.DrawCircle(point, style.Radius + 8f, selectedStroke);
            }

            string labelText = ResolveLabelText(entity);
            if (!ShouldDrawLabel(entity, selected, labelText))
            {
                continue;
            }

            float labelX = point.X + style.LabelOffsetX;
            float labelY = point.Y - style.LabelOffsetY;
            float labelWidth = Math.Max(24f, labelPaint.MeasureText(labelText));
            SKRect labelRect = new(labelX - 6f, labelY - labelPaint.TextSize - 4f, labelX + labelWidth + 8f, labelY + 6f);
            if (ShouldCullLabel(labelRect, occupiedLabels, entity, selected))
            {
                continue;
            }

            canvas.DrawRoundRect(labelRect, 8f, 8f, labelBackdropPaint);
            canvas.DrawRoundRect(labelRect, 8f, 8f, labelStrokePaint);
            canvas.DrawText(labelText, labelX, labelY, labelPaint);
            occupiedLabels.Add(labelRect);
        }
    }

    private static int ResolveDrawPriority(CapturedEntity entity)
    {
        string name = entity.Name;
        if (name.Contains("Wall", StringComparison.OrdinalIgnoreCase) || name.Contains("Gate", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (name.Contains("Trench", StringComparison.OrdinalIgnoreCase) || name.Contains("Tunnel", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (name.Contains("Ladder", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }

    private static bool ShouldDrawLabel(CapturedEntity entity, Entity selected, string labelText)
    {
        if (string.IsNullOrWhiteSpace(labelText))
        {
            return false;
        }

        if (entity.Entity == selected)
        {
            return true;
        }

        return ResolveLabelPriority(entity) > 0;
    }

    private static bool ShouldCullLabel(SKRect candidate, List<SKRect> occupied, CapturedEntity entity, Entity selected)
    {
        if (entity.Entity == selected || ResolveLabelPriority(entity) >= 4)
        {
            return false;
        }

        for (int i = 0; i < occupied.Count; i++)
        {
            if (candidate.IntersectsWith(occupied[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static int ResolveLabelPriority(CapturedEntity entity)
    {
        string name = entity.Name;
        if (name.Contains("Wall", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Gate", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Tunnel", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Trench", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Ladder", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Capital", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (name.Contains("Pass", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Ford", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Crossing", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Watch", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Camp", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Train", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (name.Contains("Column", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (name.Contains("Vanguard", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Team", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 0;
    }

    private static string ResolveLabelText(CapturedEntity entity)
    {
        string label = entity.Name;
        label = label.Replace("North Column", "N Column", StringComparison.OrdinalIgnoreCase);
        label = label.Replace("South Column", "S Column", StringComparison.OrdinalIgnoreCase);
        label = label.Replace("Main Gate Gate Guard", "Gate Guard", StringComparison.OrdinalIgnoreCase);
        return label;
    }

    private static EntityVisualStyle ResolveStyle(CapturedEntity entity)
    {
        SKColor teamFill = entity.TeamId switch
        {
            1 => new SKColor(77, 176, 255),
            2 => new SKColor(255, 122, 89),
            _ => new SKColor(183, 196, 212)
        };

        string name = entity.Name;
        if (name.Contains("Wall", StringComparison.OrdinalIgnoreCase))
        {
            return new EntityVisualStyle(EntityShape.Rect, new SKColor(71, 88, 104), new SKColor(176, 191, 208), 34f, 14f, 28f, 16f, 10f);
        }

        if (name.Contains("Gate", StringComparison.OrdinalIgnoreCase))
        {
            return new EntityVisualStyle(EntityShape.Rect, new SKColor(111, 77, 45), new SKColor(229, 191, 126), 28f, 18f, 28f, 16f, 10f);
        }

        if (name.Contains("Trench", StringComparison.OrdinalIgnoreCase))
        {
            return new EntityVisualStyle(EntityShape.Rect, new SKColor(65, 52, 39), new SKColor(173, 142, 105), 24f, 12f, 26f, 14f, 10f);
        }

        if (name.Contains("Tunnel", StringComparison.OrdinalIgnoreCase))
        {
            return new EntityVisualStyle(EntityShape.Rect, new SKColor(77, 59, 118), new SKColor(177, 152, 232), 22f, 14f, 24f, 16f, 10f);
        }

        if (name.Contains("Ladder", StringComparison.OrdinalIgnoreCase))
        {
            return new EntityVisualStyle(EntityShape.Rect, new SKColor(139, 112, 64), new SKColor(240, 207, 143), 14f, 28f, 22f, 14f, 12f);
        }

        if (name.Contains("Camp", StringComparison.OrdinalIgnoreCase) || name.Contains("Train", StringComparison.OrdinalIgnoreCase))
        {
            return new EntityVisualStyle(EntityShape.Rect, teamFill, new SKColor(245, 248, 252), 18f, 14f, 24f, 14f, 10f);
        }

        return new EntityVisualStyle(EntityShape.Circle, teamFill, SKColors.White, 12f, 12f, 0f, 18f, 12f);
    }

    private static SKPoint ToScreen(WorldBounds bounds, Vector2 worldCm)
    {
        float width = MathF.Max(1f, bounds.MaxX - bounds.MinX);
        float height = MathF.Max(1f, bounds.MaxY - bounds.MinY);
        float x = (worldCm.X - bounds.MinX) / width;
        float y = 1f - ((worldCm.Y - bounds.MinY) / height);
        return new SKPoint(96f + x * (ExportWidth - 192f), 80f + y * (ExportHeight - 160f));
    }

    private static SKRect RectFromWorld(WorldBounds bounds, float minX, float minY, float maxX, float maxY)
    {
        SKPoint a = ToScreen(bounds, new Vector2(minX, maxY));
        SKPoint b = ToScreen(bounds, new Vector2(maxX, minY));
        return new SKRect(a.X, a.Y, b.X, b.Y);
    }

    private static SKColor ToSkColor(in Vector4 color)
    {
        byte a = (byte)Math.Clamp(color.W * 255f, 0f, 255f);
        byte r = (byte)Math.Clamp(color.X * 255f, 0f, 255f);
        byte g = (byte)Math.Clamp(color.Y * 255f, 0f, 255f);
        byte b = (byte)Math.Clamp(color.Z * 255f, 0f, 255f);
        return new SKColor(r, g, b, a);
    }

    private readonly record struct CapturedEntity(Entity Entity, string Name, Vector2 WorldCm, int TeamId, Entity Parent, string? ParentName);
    private readonly record struct WorldBounds(float MinX, float MaxX, float MinY, float MaxY);
    private readonly record struct EntityVisualStyle(EntityShape Shape, SKColor Fill, SKColor Stroke, float Radius, float LabelOffsetX, float SizeX, float SizeY, float LabelOffsetY);
    private enum EntityShape : byte
    {
        Circle = 0,
        Rect = 1
    }
}
