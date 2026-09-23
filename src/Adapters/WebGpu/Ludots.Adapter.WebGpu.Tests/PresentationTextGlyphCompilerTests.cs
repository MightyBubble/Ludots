using System.Numerics;
using Ludots.Client.WebGpu.Runtime;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Adapter.WebGpu.Tests;

[TestFixture]
public sealed class PresentationTextGlyphCompilerTests
{
    [Test]
    public void PresentationPacketCompilesDirectlyToGlyphInstances()
    {
        TestContext context = CreateContext();
        PresentationTextPacket packet = PresentationTextPacket.FromToken(context.TokenId);
        packet.SetArg(0, PresentationTextArg.FromInt32(100));
        packet.SetArg(1, PresentationTextArg.FromInt32(260));

        WebGpuGlyphRun run = context.Compiler.Resolve(in packet);
        var codePoints = new char[run.GlyphCount];
        for (int i = 0; i < codePoints.Length; i++)
        {
            codePoints[i] = checked((char)run.Glyphs[i].CodePoint);
        }

        Assert.That(new string(codePoints), Is.EqualTo("100/260"));
        var instances = new WebGpuGlyphInstance[run.GlyphCount];
        int written = context.Compiler.WriteInstances(
            run,
            12f,
            34f,
            16,
            Vector4.One,
            default,
            instances);
        Assert.That(written, Is.EqualTo(7));
        Assert.That(instances[0].CenterPx.X, Is.GreaterThan(12f));
        Assert.That(instances[0].CenterPx.Y, Is.GreaterThan(34f));
    }

    [Test]
    public void StablePacketCacheAndInstanceExpansionDoNotAllocateAgain()
    {
        TestContext context = CreateContext();
        PresentationTextPacket packet = PresentationTextPacket.FromToken(context.TokenId);
        packet.SetArg(0, PresentationTextArg.FromInt32(100));
        packet.SetArg(1, PresentationTextArg.FromInt32(100));
        WebGpuGlyphRun first = context.Compiler.Resolve(in packet);
        var instances = new WebGpuGlyphInstance[first.GlyphCount];
        context.Compiler.WriteInstances(first, 0f, 0f, 11, Vector4.One, default, instances);

        long before = GC.GetAllocatedBytesForCurrentThread();
        WebGpuGlyphRun? last = null;
        for (int i = 0; i < 1_000; i++)
        {
            last = context.Compiler.Resolve(in packet);
            context.Compiler.WriteInstances(last, i, i, 11, Vector4.One, default, instances);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(last, Is.SameAs(first));
        Assert.That(context.Compiler.CompilationCount, Is.EqualTo(1));
        Assert.That(context.Compiler.CachedRunCount, Is.EqualTo(1));
        Assert.That(allocated, Is.Zero);
    }

    [Test]
    public void WorldHudAndOverlayStringsUseTheirFormalBuffers()
    {
        TestContext context = CreateContext();
        int worldStringId = context.WorldStrings.Register("READY");
        ScreenHudTextItem hudItem = new()
        {
            StableId = 42,
            Id0 = worldStringId,
        };
        WebGpuGlyphRun hudRun = context.Compiler.Resolve(in hudItem);

        var overlay = new ScreenOverlayBuffer();
        Assert.That(overlay.AddText(10, 20, "SELECT", 16, Vector4.One), Is.True);
        ScreenOverlayItem overlayItem = overlay.GetSpan()[0];
        WebGpuGlyphRun overlayRun = context.Compiler.Resolve(in overlayItem, overlay);

        Assert.That(ToString(hudRun), Is.EqualTo("READY"));
        Assert.That(ToString(overlayRun), Is.EqualTo("SELECT"));
    }

    [Test]
    public void UnknownPresentationTokenFailsExplicitly()
    {
        TestContext context = CreateContext();
        PresentationTextPacket packet = PresentationTextPacket.FromToken(999);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => context.Compiler.Resolve(in packet))!;
        Assert.That(exception.Message, Does.Contain("unknown presentation token id 999"));
    }

    private static string ToString(WebGpuGlyphRun run)
    {
        var characters = new char[run.GlyphCount];
        for (int i = 0; i < characters.Length; i++)
        {
            characters[i] = checked((char)run.Glyphs[i].CodePoint);
        }

        return new string(characters);
    }

    private static TestContext CreateContext()
    {
        var tokenIds = new StringIntRegistry(4, 1, 0, StringComparer.Ordinal);
        int tokenId = tokenIds.Register("hud.attribute.current_over_base");
        var localeIds = new StringIntRegistry(4, 1, 0, StringComparer.Ordinal);
        int localeId = localeIds.Register("en-US");
        var definitions = new PresentationTextTokenDefinition[tokenId + 1];
        definitions[tokenId] = new PresentationTextTokenDefinition
        {
            TokenId = tokenId,
            Key = "hud.attribute.current_over_base",
            ArgCount = 2,
        };
        var templates = new PresentationTextTemplate[tokenId + 1];
        templates[tokenId] = new PresentationTextTemplate(
            "{0}/{1}",
            [
                new PresentationTextTemplatePart(PresentationTextTemplatePartKind.Argument, string.Empty, 0),
                new PresentationTextTemplatePart(PresentationTextTemplatePartKind.Literal, "/", -1),
                new PresentationTextTemplatePart(PresentationTextTemplatePartKind.Argument, string.Empty, 1),
            ]);
        var locales = new PresentationTextLocaleTable[localeId + 1];
        locales[localeId] = new PresentationTextLocaleTable(localeId, "en-US", templates);
        var catalog = new PresentationTextCatalog(tokenIds, definitions, localeIds, locales, localeId);
        var localeSelection = new PresentationTextLocaleSelection(catalog);
        var worldStrings = new WorldHudStringTable(32);
        WebGpuFontAtlas atlas = CreateAsciiAtlas();
        var compiler = new PresentationTextGlyphCompiler(atlas, catalog, localeSelection, worldStrings);
        return new TestContext(compiler, worldStrings, tokenId);
    }

    private static WebGpuFontAtlas CreateAsciiAtlas()
    {
        const int first = 32;
        const int last = 126;
        var glyphs = new WebGpuFontGlyph[last - first + 1];
        for (int codePoint = first; codePoint <= last; codePoint++)
        {
            int index = codePoint - first;
            glyphs[index] = new WebGpuFontGlyph(
                codePoint,
                index,
                0,
                codePoint == ' ' ? 0 : 1,
                codePoint == ' ' ? 0 : 1,
                0f,
                -1f,
                1f);
        }

        return new WebGpuFontAtlas(
            128,
            1,
            1f,
            1f,
            new byte[128],
            glyphs,
            "TEST");
    }

    private sealed record TestContext(
        PresentationTextGlyphCompiler Compiler,
        WorldHudStringTable WorldStrings,
        int TokenId);
}
