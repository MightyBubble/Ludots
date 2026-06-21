using System;
using System.IO;
using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Browser.Skia;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using SkiaSharp;

namespace BrowserUiShowcaseMod;

public sealed class BrowserUiShowcaseModEntry : IMod
{
    private const string BrowserServiceKey = "BrowserRuntime";

    private IBrowserSurface? _surface;
    private BrowserCanvasContent? _browserContent;

    public void OnLoad(IModContext context)
    {
        context.Log("[BrowserUiShowcaseMod] Loaded.");
        context.OnEvent(GameEvents.GameStart, OnGameStartAsync);
    }

    public void OnUnload()
    {
        _browserContent?.Dispose();
        _browserContent = null;
        _surface?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _surface = null;
    }

    private async Task OnGameStartAsync(ScriptContext context)
    {
        IUiTextMeasurer textMeasurer = (IUiTextMeasurer)context.Get(CoreServiceKeys.UiTextMeasurer);
        IUiImageSizeProvider imageSizeProvider = (IUiImageSizeProvider)context.Get(CoreServiceKeys.UiImageSizeProvider);
        BrowserSurfaceAttachment attachment = await TryCreateBrowserSurfaceAsync(context).ConfigureAwait(false);
        _surface = attachment.Surface;
        _browserContent = attachment.Content;
        UiScene scene = BuildLandingScene(context, textMeasurer, imageSizeProvider, attachment);
        UIRoot root = context.Get(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot service is missing from ScriptContext.");
        root.MountScene(scene);
        root.IsDirty = true;
    }

    private static UiScene BuildLandingScene(
        ScriptContext context,
        IUiTextMeasurer textMeasurer,
        IUiImageSizeProvider imageSizeProvider,
        BrowserSurfaceAttachment attachment)
    {
        IBrowserSurface? surface = attachment.Surface;
        UiElementBuilder root = Ui.Column(
                Ui.Text("Browser UI Showcase").Class("skin-header").FontSize(34).Bold(),
                Ui.Text("Raylib mounts the showcase through Skia UI today; CEF or Ultralight can later render the same packaged web app as a browser surface.").Class("page-copy"),
                Ui.Row(
                    BuildHeroCard("browser-landing", "Showcase bundle", "HTML / CSS / JS packaged as a Ludots mod", attachment.StatusLabel),
                    BuildHeroCard("browser-runtime", "Runtime status", attachment.StatusLabel, surface == null ? "Diagnostic preview only" : "Browser surface attached"),
                    BuildHeroCard("browser-path", "Host path", "Raylib today, CEF or Ultralight provider later", "Same surface contract"))
                    .Class("page-grid-row")
                    .Gap(12f)
                    .FlexGrow(1f),
                Ui.Row(
                    BuildBrowserPane(surface, attachment.Content),
                    BuildInspectorPane(context, attachment.StatusLabel))
                    .Class("page-grid-row")
                    .Gap(12f)
                    .FlexGrow(1f))
            .Classes("skin-root", "theme-dark", "density-cozy")
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Gap(12f);

        return UiSceneComposer.Compose(textMeasurer, imageSizeProvider, root, null, BuildBrowserStyleSheet());
    }

    private static UiElementBuilder BuildHeroCard(string id, string title, string subtitle, string body)
    {
        return Ui.Card(
                Ui.Text(title).Class("page-card-title"),
                Ui.Text(subtitle).Class("page-copy"),
                Ui.Text(body).Class("muted"))
            .Id(id)
            .Class("skin-card")
            .FlexGrow(1f)
            .FlexBasis(0f);
    }

    private static UiElementBuilder BuildBrowserPane(IBrowserSurface? surface, BrowserCanvasContent? content)
    {
        if (surface != null && content != null)
        {
            return Ui.Card(
                    Ui.Text("Live browser surface").Class("page-card-title"),
                    Ui.Text("The browser frame can be embedded through the same canvas path as other Skia content.").Class("page-copy"),
                    Ui.Canvas(content).Class("browser-canvas").WidthPercent(100f).Height(320f))
                .Class("skin-card")
                .FlexGrow(2f)
                .FlexShrink(1f)
                .FlexBasis(0f);
        }

        return Ui.Card(
                Ui.Text("Diagnostic preview").Class("page-card-title"),
                Ui.Text("No browser runtime is registered yet, so Raylib renders this native probe instead of pretending to be a browser.").Class("page-copy"),
                Ui.Canvas(new UiCanvasContent(DrawDiagnosticPreview)).Class("browser-canvas").WidthPercent(100f).Height(320f))
            .Class("skin-card")
            .FlexGrow(2f)
            .FlexShrink(1f)
            .FlexBasis(0f);
    }

    private static UiElementBuilder BuildInspectorPane(ScriptContext context, string statusLabel)
    {
        string browserState = TryGetBrowserRuntime(context, out IBrowserRuntime runtime)
            ? $"{runtime.Info.EngineKind} / {runtime.Info.EngineName} {runtime.Info.EngineVersion}"
            : "Browser runtime service missing";

        return Ui.Card(
                Ui.Text("Inspector").Class("page-card-title"),
                Ui.Text(statusLabel).Class("page-copy"),
                Ui.Text(browserState).Class("muted"),
                Ui.Text("Assets live in the mod bundle and are served through packaged-resource resolution.").Class("muted"),
                Ui.Text("When Raylib boots this mod without a provider, it verifies the UI mount path. When CEF or Ultralight is registered, the packaged bundle becomes the browser app.").Class("muted"))
            .Class("skin-card")
            .FlexGrow(1f)
            .FlexShrink(1f)
            .FlexBasis(0f);
    }

    private async Task<BrowserSurfaceAttachment> TryCreateBrowserSurfaceAsync(ScriptContext context)
    {
        if (TryGetBrowserRuntime(context, out IBrowserRuntime runtime))
        {
            string statusLabel = $"Browser runtime detected: {runtime.Info.EngineKind} / {runtime.Info.EngineName}";
            string root = ResolveAssetRoot(context);
            var resolver = new BrowserAppResourceResolver(root);
            IBrowserSurface surface = await runtime.CreateSurfaceAsync(new BrowserViewport(960, 360), resolver).ConfigureAwait(false);
            UIRoot? uiRoot = context.Get(CoreServiceKeys.UIRoot) as UIRoot;
            surface.FrameReady += (_, _) =>
            {
                if (uiRoot != null)
                {
                    uiRoot.IsDirty = true;
                }
            };
            surface.Messages.MessageReceived += (_, message) =>
            {
                _ = surface.Messages.PostMessageAsync(new BrowserScriptMessage(
                    "host",
                    $"Host ack received {DateTime.Now:HH:mm:ss}: {message.Payload}"));
            };
            await surface.NavigateAsync(new BrowserNavigationRequest(new Uri("ludots-app://app/"))).ConfigureAwait(false);
            await surface.Messages.PostMessageAsync(new BrowserScriptMessage(
                "host",
                "CEF browser surface is live inside Raylib with transparent background enabled.")).ConfigureAwait(false);
            return new BrowserSurfaceAttachment(surface, new BrowserCanvasContent(surface), statusLabel);
        }

        return new BrowserSurfaceAttachment(null, null, "Browser runtime missing: showing native diagnostic preview.");
    }

    private static bool TryGetBrowserRuntime(ScriptContext context, out IBrowserRuntime runtime)
    {
        var key = new ServiceKey<IBrowserRuntime>(BrowserServiceKey);
        if (context.TryGet(key, out runtime))
        {
            return true;
        }

        if (context.TryGet(CoreServiceKeys.Engine, out Ludots.Core.Engine.GameEngine? engine) &&
            engine != null &&
            engine.TryGetService(key, out runtime))
        {
            context.Set(key, runtime);
            return true;
        }

        runtime = null!;
        return false;
    }

    private static string ResolveAssetRoot(ScriptContext context)
    {
        if (context.TryGet(CoreServiceKeys.Engine, out Ludots.Core.Engine.GameEngine? engine) &&
            engine?.VFS != null &&
            engine.VFS.TryResolveFullPath("BrowserUiShowcaseMod:Assets/browser-app/index.html", out string indexPath))
        {
            string? root = Path.GetDirectoryName(indexPath);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                return root;
            }
        }

        return AppContext.BaseDirectory;
    }

    private static void DrawDiagnosticPreview(SKCanvas canvas, SKRect rect)
    {
        canvas.Clear(SKColor.Parse("#0b1120"));
        using var titlePaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        using var bodyPaint = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#cbd5e1") };
        using var titleFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 28f);
        using var bodyFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), 16f);
        using var accentPaint = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#38bdf8"), Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var fillPaint = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#1e293b"), Style = SKPaintStyle.Fill };

        var panelRect = new SKRect(rect.Left + 20f, rect.Top + 20f, rect.Right - 20f, rect.Bottom - 20f);
        var panel = new SKRoundRect(panelRect, 18f, 18f);
        canvas.DrawRoundRect(panel, fillPaint);
        canvas.DrawRoundRect(panel, accentPaint);
        canvas.DrawText("Browser runtime not injected", rect.Left + 40, rect.Top + 72, SKTextAlign.Left, titleFont, titlePaint);
        canvas.DrawText("This diagnostic confirms Raylib mounted the Skia UI path. A provider is required for real web rendering.", rect.Left + 40, rect.Top + 110, SKTextAlign.Left, bodyFont, bodyPaint);
        canvas.DrawLine(rect.Left + 40, rect.Top + 140, rect.Right - 40, rect.Top + 140, accentPaint);
    }

    private static UiStyleSheet BuildBrowserStyleSheet()
    {
        return Ludots.UI.HtmlEngine.Markup.UiCssParser.ParseStyleSheet("""
.browser-canvas {
  background: transparent;
  border-radius: 14px;
}
""");
    }

    private sealed record BrowserSurfaceAttachment(IBrowserSurface? Surface, BrowserCanvasContent? Content, string StatusLabel);
}
