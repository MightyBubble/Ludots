using System.Numerics;
using System.Runtime.CompilerServices;
using Ludots.Client.WebGpu.Runtime;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Adapter.WebGpu.Tests;

[TestFixture]
public sealed class WorldAnchoredWebGpuHudStoreTests
{
    [Test]
    public void ColdBuild_WritesExactlyOneBarInstancePerBar_AndFullUploadsAllLanes()
    {
        Fixture context = CreateContext(capacity: 8);
        context.Hud.TryAdd(CreateBar(1, new Vector3(1f, 2f, 3f), value: 0.5f));
        context.Hud.TryAdd(CreateBar(2, new Vector3(4f, 5f, 6f), value: 1f));
        context.Hud.TryAdd(CreateText(101, new Vector3(1f, 3f, 3f), current: 50, bas: 100));
        context.Hud.TryAdd(CreateText(102, new Vector3(4f, 6f, 6f), current: 75, bas: 100));

        context.Store.Sync(context.Hud);

        Assert.Multiple(() =>
        {
            Assert.That(context.Store.BarCount, Is.EqualTo(2));
            Assert.That(context.Store.TextCount, Is.EqualTo(2));
            Assert.That(context.Store.BarInstances.Length, Is.EqualTo(2));
            Assert.That(context.Store.Anchors.Length, Is.EqualTo(4));
            Assert.That(context.Store.GlyphInstances.Length, Is.GreaterThan(0));
            Assert.That(context.Store.BarInstances[0].AnchorIndex, Is.EqualTo(0f));
            Assert.That(context.Store.BarInstances[1].AnchorIndex, Is.EqualTo(1f));
            Assert.That(context.Store.BuildDiagnostics.Path, Is.EqualTo(WebGpuHudBuildPath.FullRebuild));
            Assert.That(context.Store.UploadPlan.BarFullUpload, Is.True);
            Assert.That(context.Store.UploadPlan.AnchorFullUpload, Is.True);
            Assert.That(context.Store.UploadPlan.GlyphFullUpload, Is.True);
        });
    }

    [Test]
    public void CameraOnly_NoWorldHudRevision_UploadsNothing()
    {
        Fixture context = CreateContext(capacity: 8);
        context.Hud.TryAdd(CreateBar(1, new Vector3(1f, 2f, 3f), value: 1f));
        context.Hud.TryAdd(CreateText(101, new Vector3(1f, 3f, 3f), current: 100, bas: 100));
        context.Store.Sync(context.Hud);

        for (int i = 0; i < 3; i++)
        {
            context.Store.Sync(context.Hud);
            Assert.That(context.Store.BuildDiagnostics.Path, Is.EqualTo(WebGpuHudBuildPath.NoChange));
            Assert.That(context.Store.UploadPlan.BarUploadCount, Is.EqualTo(0));
            Assert.That(context.Store.UploadPlan.AnchorUploadCount, Is.EqualTo(0));
            Assert.That(context.Store.UploadPlan.GlyphUploadCount, Is.EqualTo(0));
            Assert.That(context.Store.BuildDiagnostics.TextsResolved, Is.EqualTo(0));
        }
    }

    [Test]
    public void Movement_UploadsOnlyAnchors_BarAndGlyphStaticStayZero_AndResolveIsZero()
    {
        Fixture context = CreateContext(capacity: 8);
        context.Hud.TryAdd(CreateBar(1, new Vector3(1f, 2f, 3f), value: 1f));
        context.Hud.TryAdd(CreateText(101, new Vector3(1f, 3f, 3f), current: 100, bas: 100));
        context.Store.Sync(context.Hud);
        int compilationAfterCold = context.Compiler.CompilationCount;
        Vector2 glyphLocalBefore = context.Store.GlyphInstances[0].LocalOffsetPx;
        float barAnchorIndex = context.Store.BarInstances[0].AnchorIndex;
        Vector2 barHalfSizeBefore = context.Store.BarInstances[0].HalfSizePx;

        Assert.That(context.Hud.TryAdd(CreateBar(1, new Vector3(10f, 20f, 30f), value: 1f)), Is.True);
        Assert.That(context.Hud.TryAdd(CreateText(101, new Vector3(10f, 30f, 30f), current: 100, bas: 100)), Is.True);
        context.Store.Sync(context.Hud);

        Assert.Multiple(() =>
        {
            Assert.That(context.Store.BuildDiagnostics.Path, Is.EqualTo(WebGpuHudBuildPath.Delta));
            Assert.That(context.Store.BuildDiagnostics.PositionOnlyBarsUpdated, Is.EqualTo(1));
            Assert.That(context.Store.BuildDiagnostics.PositionOnlyTextsUpdated, Is.EqualTo(1));
            Assert.That(context.Store.BuildDiagnostics.TextsResolved, Is.EqualTo(0));
            Assert.That(context.Compiler.CompilationCount, Is.EqualTo(compilationAfterCold));
            Assert.That(context.Store.GlyphInstances[0].LocalOffsetPx, Is.EqualTo(glyphLocalBefore));
            Assert.That(context.Store.BarInstances[0].HalfSizePx, Is.EqualTo(barHalfSizeBefore));
            Assert.That(context.Store.BarInstances[0].AnchorIndex, Is.EqualTo(barAnchorIndex));
            Assert.That(context.Store.Anchors[(int)barAnchorIndex].WorldPosition, Is.EqualTo(new Vector3(10f, 20f, 30f)));
            Assert.That(context.Store.UploadPlan.BarUploadCount, Is.EqualTo(0));
            Assert.That(context.Store.UploadPlan.BarUploadBytes, Is.EqualTo(0));
            Assert.That(context.Store.UploadPlan.GlyphUploadCount, Is.EqualTo(0));
            Assert.That(context.Store.UploadPlan.GlyphUploadBytes, Is.EqualTo(0));
            Assert.That(context.Store.UploadPlan.AnchorUploadCount, Is.GreaterThan(0));
            Assert.That(context.Store.UploadPlan.AnchorUploadBytes, Is.GreaterThan(0));
        });
    }

    [Test]
    public void UnchangedText_DoesNotResolveOrUploadGlyphs()
    {
        Fixture context = CreateContext(capacity: 8);
        context.Hud.TryAdd(CreateText(101, new Vector3(1f, 3f, 3f), current: 100, bas: 100, dirtySerial: 1));
        context.Store.Sync(context.Hud);
        int compilationAfterCold = context.Compiler.CompilationCount;

        Assert.That(
            context.Hud.TryAdd(CreateText(101, new Vector3(1f, 3f, 3f), current: 100, bas: 100, dirtySerial: 1)),
            Is.True);
        context.Store.Sync(context.Hud);

        Assert.Multiple(() =>
        {
            Assert.That(context.Compiler.CompilationCount, Is.EqualTo(compilationAfterCold));
            Assert.That(context.Store.BuildDiagnostics.TextsResolved, Is.EqualTo(0));
            Assert.That(context.Store.UploadPlan.GlyphUploadCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void ChangedRenderedText_RebuildsGlyphMetadataAndUploads()
    {
        Fixture context = CreateContext(capacity: 8);
        context.Hud.TryAdd(CreateText(101, new Vector3(1f, 3f, 3f), current: 100, bas: 100, dirtySerial: 1));
        context.Store.Sync(context.Hud);
        int compilationAfterCold = context.Compiler.CompilationCount;

        Assert.That(
            context.Hud.TryAdd(CreateText(101, new Vector3(1f, 3f, 3f), current: 42, bas: 100, dirtySerial: 2)),
            Is.True);
        context.Store.Sync(context.Hud);

        Assert.Multiple(() =>
        {
            Assert.That(context.Compiler.CompilationCount, Is.GreaterThan(compilationAfterCold));
            Assert.That(context.Store.BuildDiagnostics.TextsResolved, Is.EqualTo(1));
            Assert.That(context.Store.UploadPlan.GlyphUploadCount, Is.GreaterThan(0));
            Assert.That(context.Store.UploadPlan.AnchorUploadCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void ContentChange_ResolvesOnlyDirtyText_AndPreservesUnchangedGlyphLocals()
    {
        Fixture context = CreateContext(capacity: 8);
        context.Hud.TryAdd(CreateText(101, new Vector3(1f, 3f, 3f), current: 10, bas: 100));
        context.Hud.TryAdd(CreateText(102, new Vector3(4f, 6f, 6f), current: 20, bas: 100));
        context.Store.Sync(context.Hud);
        Vector2 unchangedBefore = FindFirstGlyphLocal(context.Store, labelIndex: 1);

        Assert.That(context.Hud.TryAdd(CreateText(101, new Vector3(1f, 3f, 3f), current: 99, bas: 100, dirtySerial: 2)), Is.True);
        context.Store.Sync(context.Hud);

        Assert.Multiple(() =>
        {
            Assert.That(context.Store.BuildDiagnostics.ContentDirtyTexts, Is.EqualTo(1));
            Assert.That(context.Store.BuildDiagnostics.TextsResolved, Is.EqualTo(1));
            Assert.That(FindFirstGlyphLocal(context.Store, labelIndex: 1), Is.EqualTo(unchangedBefore));
        });
    }

    [Test]
    public void SimultaneousContentAndPositionChange_UpdatesBarStaticAndAnchor_WithoutGlyphRewrite()
    {
        Fixture context = CreateContext(capacity: 8);
        context.Hud.TryAdd(CreateBar(1, new Vector3(1f, 2f, 3f), value: 1f, dirtySerial: 1));
        context.Hud.TryAdd(CreateText(101, new Vector3(1f, 3f, 3f), current: 100, bas: 100, dirtySerial: 1));
        context.Store.Sync(context.Hud);
        int compilationAfterCold = context.Compiler.CompilationCount;
        Vector2 glyphLocalBefore = context.Store.GlyphInstances[0].LocalOffsetPx;
        int barAnchor = (int)context.Store.BarInstances[0].AnchorIndex;

        Assert.That(
            context.Hud.TryAdd(CreateBar(1, new Vector3(9f, 9f, 9f), value: 0.25f, dirtySerial: 2)),
            Is.True);
        Assert.That(
            context.Hud.TryAdd(CreateText(101, new Vector3(9f, 10f, 9f), current: 100, bas: 100, dirtySerial: 1)),
            Is.True);
        context.Store.Sync(context.Hud);

        Assert.Multiple(() =>
        {
            Assert.That(context.Store.Anchors[barAnchor].WorldPosition, Is.EqualTo(new Vector3(9f, 9f, 9f)));
            Assert.That(context.Store.BarInstances[0].HealthRatio, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(context.Store.UploadPlan.BarUploadCount, Is.GreaterThan(0));
            Assert.That(context.Store.UploadPlan.AnchorUploadCount, Is.GreaterThan(0));
            Assert.That(context.Store.GlyphInstances[0].LocalOffsetPx, Is.EqualTo(glyphLocalBefore));
            Assert.That(context.Compiler.CompilationCount, Is.EqualTo(compilationAfterCold));
            Assert.That(context.Store.UploadPlan.GlyphUploadCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void Removal_TriggersDeterministicFullRebuildAndRepairsSwapMaps()
    {
        Fixture context = CreateContext(capacity: 8);
        context.Hud.TryAdd(CreateBar(1, new Vector3(1f, 2f, 3f), value: 1f));
        context.Hud.TryAdd(CreateBar(2, new Vector3(2f, 2f, 3f), value: 1f));
        context.Hud.TryAdd(CreateBar(3, new Vector3(3f, 2f, 3f), value: 1f));
        context.Hud.TryAdd(CreateText(101, new Vector3(1f, 3f, 3f), current: 1, bas: 1));
        context.Hud.TryAdd(CreateText(102, new Vector3(2f, 3f, 3f), current: 2, bas: 2));
        context.Hud.TryAdd(CreateText(103, new Vector3(3f, 3f, 3f), current: 3, bas: 3));
        context.Store.Sync(context.Hud);

        context.Hud.Remove(2);
        context.Hud.Remove(102);
        context.Store.Sync(context.Hud);

        Assert.Multiple(() =>
        {
            Assert.That(context.Store.BarCount, Is.EqualTo(2));
            Assert.That(context.Store.TextCount, Is.EqualTo(2));
            Assert.That(context.Store.BarInstances.Length, Is.EqualTo(2));
            Assert.That(context.Store.Anchors.Length, Is.EqualTo(4));
            Assert.That(context.Store.BuildDiagnostics.Path, Is.EqualTo(WebGpuHudBuildPath.FullRebuild));
            Assert.That(context.Store.BuildDiagnostics.RemovedCount, Is.EqualTo(2));
            Assert.That(context.Store.UploadPlan.BarFullUpload, Is.True);
            Assert.That(context.Store.UploadPlan.AnchorFullUpload, Is.True);
            Assert.That(context.Store.UploadPlan.GlyphFullUpload, Is.True);
            Assert.That(context.Hud.TryGetByStableId(2, out _), Is.False);
            Assert.That(context.Hud.TryGetByStableId(102, out _), Is.False);
            Assert.That(context.Hud.TryGetByStableId(1, out WorldHudItem bar1), Is.True);
            Assert.That(context.Hud.TryGetByStableId(3, out WorldHudItem bar3), Is.True);
            Assert.That(context.Hud.TryGetByStableId(101, out WorldHudItem text101), Is.True);
            Assert.That(context.Hud.TryGetByStableId(103, out WorldHudItem text103), Is.True);

            // Swap-remove in WorldHudBatchBuffer must rebuild store maps/anchor indices so survivors stay consistent.
            Assert.That(FindAnchorPosition(context.Store, bar1.WorldPosition), Is.GreaterThanOrEqualTo(0));
            Assert.That(FindAnchorPosition(context.Store, bar3.WorldPosition), Is.GreaterThanOrEqualTo(0));
            Assert.That(FindAnchorPosition(context.Store, text101.WorldPosition), Is.GreaterThanOrEqualTo(0));
            Assert.That(FindAnchorPosition(context.Store, text103.WorldPosition), Is.GreaterThanOrEqualTo(0));
            Assert.That(context.Store.Anchors[0].Visibility, Is.EqualTo(1f));
            AssertDistinctAnchorIndices(context.Store.BarInstances, expectedDistinct: 2);
            AssertDistinctGlyphAnchorIndices(context.Store.GlyphInstances, expectedDistinct: 2);
        });
    }

    [Test]
    public void Growth_BeyondWarmCapacity_MarksFullUpload()
    {
        Fixture context = CreateContext(capacity: 128);
        context.Hud.TryAdd(CreateBar(1, new Vector3(1f, 2f, 3f), value: 1f));
        context.Store.Sync(context.Hud);

        for (int i = 2; i <= 96; i++)
        {
            context.Hud.TryAdd(CreateBar(i, new Vector3(i, 2f, 3f), value: 1f));
        }

        context.Store.Sync(context.Hud);

        Assert.Multiple(() =>
        {
            Assert.That(context.Store.BarCount, Is.EqualTo(96));
            Assert.That(context.Store.BarInstances.Length, Is.EqualTo(96));
            Assert.That(context.Store.BuildDiagnostics.Path, Is.EqualTo(WebGpuHudBuildPath.FullRebuild));
            Assert.That(context.Store.UploadPlan.BarFullUpload, Is.True);
            Assert.That(context.Store.UploadPlan.AnchorFullUpload, Is.True);
        });
    }

    [Test]
    public void SteadyState_ThreeConsecutiveMovementRuns_AllocateZeroAfterWarmup()
    {
        Fixture context = CreateContext(capacity: 64);
        for (int i = 0; i < 32; i++)
        {
            context.Hud.TryAdd(CreateBar(i + 1, new Vector3(i, 2f, 3f), value: 1f));
            context.Hud.TryAdd(CreateText(1000 + i, new Vector3(i, 3f, 3f), current: 100, bas: 100));
        }

        WorldAnchoredWebGpuHudStore store = context.Store;
        WorldHudBatchBuffer hud = context.Hud;
        store.Sync(hud);

        for (int warm = 0; warm < 32; warm++)
        {
            _ = MeasureMovementSyncs(store, hud, frameCount: 64, origin: warm);
        }

        Assert.That(MeasureMovementSyncs(store, hud, frameCount: 512, origin: 100), Is.EqualTo(0));
        Assert.That(MeasureMovementSyncs(store, hud, frameCount: 512, origin: 200), Is.EqualTo(0));
        Assert.That(MeasureMovementSyncs(store, hud, frameCount: 512, origin: 300), Is.EqualTo(0));
    }

    [Test]
    public void UploadPlanByteSizes_MatchWorldInstanceLayouts()
    {
        Fixture context = CreateContext(capacity: 4);
        context.Hud.TryAdd(CreateBar(1, new Vector3(1f, 2f, 3f), value: 1f));
        context.Hud.TryAdd(CreateText(101, new Vector3(1f, 3f, 3f), current: 1, bas: 1));
        context.Store.Sync(context.Hud);

        WebGpuHudUploadPlan plan = context.Store.UploadPlan;
        Assert.That(plan.BarUploadBytes, Is.EqualTo(plan.BarUploadCount * Unsafe.SizeOf<WebGpuWorldBarInstance>()));
        Assert.That(plan.AnchorUploadBytes, Is.EqualTo(plan.AnchorUploadCount * Unsafe.SizeOf<WebGpuWorldHudAnchor>()));
        Assert.That(plan.GlyphUploadBytes, Is.EqualTo(plan.GlyphUploadCount * Unsafe.SizeOf<WebGpuWorldGlyphInstance>()));
        Assert.That(Unsafe.SizeOf<WebGpuWorldBarInstance>(), Is.EqualTo(64));
        Assert.That(Unsafe.SizeOf<WebGpuWorldHudAnchor>(), Is.EqualTo(16));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureMovementSyncs(
        WorldAnchoredWebGpuHudStore store,
        WorldHudBatchBuffer hud,
        int frameCount,
        int origin)
    {
        // Drive movement outside the measured window so only Sync is attributed.
        AdvancePositions(hud, origin);
        store.Sync(hud);

        int nonzeroBarUploadFrames = 0;
        int nonzeroGlyphUploadFrames = 0;
        int resolveFrames = 0;
        int zeroAnchorUploadFrames = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < frameCount; frame++)
        {
            AdvancePositions(hud, origin + frame + 1);
            store.Sync(hud);
            WebGpuHudUploadPlan plan = store.UploadPlan;
            WebGpuHudBuildDiagnostics diagnostics = store.BuildDiagnostics;
            nonzeroBarUploadFrames += plan.BarUploadBytes == 0 ? 0 : 1;
            nonzeroGlyphUploadFrames += plan.GlyphUploadBytes == 0 ? 0 : 1;
            resolveFrames += diagnostics.TextsResolved == 0 ? 0 : 1;
            zeroAnchorUploadFrames += plan.AnchorUploadBytes == 0 ? 1 : 0;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (nonzeroBarUploadFrames != 0 ||
            nonzeroGlyphUploadFrames != 0 ||
            resolveFrames != 0 ||
            zeroAnchorUploadFrames != 0)
        {
            throw new InvalidOperationException(
                $"Movement sync violated upload contract: bars={nonzeroBarUploadFrames}, glyphs={nonzeroGlyphUploadFrames}, resolve={resolveFrames}, zeroAnchors={zeroAnchorUploadFrames}.");
        }

        return allocated;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AdvancePositions(WorldHudBatchBuffer hud, int origin)
    {
        ReadOnlySpan<WorldHudItem> items = hud.GetSpan();
        for (int i = 0; i < items.Length; i++)
        {
            WorldHudItem item = items[i];
            item.WorldPosition = new Vector3(
                item.WorldPosition.X + 0.01f,
                item.WorldPosition.Y,
                item.WorldPosition.Z + (origin * 0.0001f));
            hud.TryAdd(in item);
        }
    }

    private static Vector2 FindFirstGlyphLocal(WorldAnchoredWebGpuHudStore store, int labelIndex)
    {
        const int glyphsPerLabel = 6;
        ReadOnlySpan<WebGpuWorldGlyphInstance> glyphs = store.GlyphInstances;
        int start = labelIndex * glyphsPerLabel;
        Assert.That(start, Is.LessThan(glyphs.Length));
        return glyphs[start].LocalOffsetPx;
    }

    private static int FindAnchorPosition(WorldAnchoredWebGpuHudStore store, Vector3 worldPosition)
    {
        ReadOnlySpan<WebGpuWorldHudAnchor> anchors = store.Anchors;
        for (int i = 0; i < anchors.Length; i++)
        {
            if (anchors[i].WorldPosition == worldPosition && anchors[i].Visibility == 1f)
            {
                return i;
            }
        }

        return -1;
    }

    private static void AssertDistinctAnchorIndices(ReadOnlySpan<WebGpuWorldBarInstance> bars, int expectedDistinct)
    {
        Span<int> seen = stackalloc int[bars.Length];
        int distinct = 0;
        for (int i = 0; i < bars.Length; i++)
        {
            int anchor = (int)bars[i].AnchorIndex;
            bool found = false;
            for (int j = 0; j < distinct; j++)
            {
                if (seen[j] == anchor)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                seen[distinct++] = anchor;
            }
        }

        Assert.That(distinct, Is.EqualTo(expectedDistinct));
    }

    private static void AssertDistinctGlyphAnchorIndices(ReadOnlySpan<WebGpuWorldGlyphInstance> glyphs, int expectedDistinct)
    {
        Span<int> seen = stackalloc int[Math.Max(expectedDistinct, 1)];
        int distinct = 0;
        for (int i = 0; i < glyphs.Length; i++)
        {
            int anchor = (int)glyphs[i].AnchorIndex;
            bool found = false;
            for (int j = 0; j < distinct; j++)
            {
                if (seen[j] == anchor)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                if (distinct >= seen.Length)
                {
                    Assert.Fail($"Expected at most {expectedDistinct} distinct glyph anchors.");
                }

                seen[distinct++] = anchor;
            }
        }

        Assert.That(distinct, Is.EqualTo(expectedDistinct));
    }

    private static WorldHudItem CreateBar(int stableId, Vector3 worldPosition, float value, int dirtySerial = 1) => new()
    {
        StableId = stableId,
        DirtySerial = dirtySerial,
        Kind = WorldHudItemKind.Bar,
        WorldPosition = worldPosition,
        Width = 40f,
        Height = 6f,
        Value0 = value,
        Color0 = new Vector4(0.1f, 0.1f, 0.1f, 1f),
        Color1 = new Vector4(0.2f, 0.8f, 0.3f, 1f),
    };

    private static WorldHudItem CreateText(
        int stableId,
        Vector3 worldPosition,
        int current,
        int bas,
        int dirtySerial = 1)
    {
        PresentationTextPacket packet = PresentationTextPacket.FromToken(1);
        packet.SetArg(0, PresentationTextArg.FromInt32(current));
        packet.SetArg(1, PresentationTextArg.FromInt32(bas));
        return new WorldHudItem
        {
            StableId = stableId,
            DirtySerial = dirtySerial,
            Kind = WorldHudItemKind.Text,
            WorldPosition = worldPosition,
            FontSize = 11,
            Color0 = Vector4.One,
            Text = packet,
        };
    }

    private static Fixture CreateContext(int capacity)
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
        return new Fixture(new WorldHudBatchBuffer(capacity), compiler, new WorldAnchoredWebGpuHudStore(compiler));
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

    private sealed class Fixture
    {
        public Fixture(WorldHudBatchBuffer hud, PresentationTextGlyphCompiler compiler, WorldAnchoredWebGpuHudStore store)
        {
            Hud = hud;
            Compiler = compiler;
            Store = store;
        }

        public WorldHudBatchBuffer Hud { get; }
        public PresentationTextGlyphCompiler Compiler { get; }
        public WorldAnchoredWebGpuHudStore Store { get; }
    }
}
