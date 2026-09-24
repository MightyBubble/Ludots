using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;
using Raylib_cs;

namespace Ludots.Adapter.Raylib.Tests;

public sealed class RaylibPrimitiveRendererEmitterConsumerTests
{
    [Test]
    public void EmitterDrawItemWithoutActiveConsumerFailsExplicitly()
    {
        using var renderer = new RaylibPrimitiveRenderer(emitterConsumerActive: false);
        PrimitiveDrawBuffer draw = EmitterDraw(AssetKind.RibbonEmitter, stableId: 101);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => renderer.Draw(draw, default(Camera3D), new MeshAssetRegistry()))!;

        Assert.That(error.Message, Does.Contain(nameof(AssetKind.RibbonEmitter)));
        Assert.That(error.Message, Does.Contain("stableId=101"));
        Assert.That(error.Message, Does.Contain("consumer is not active"));
    }

    [Test]
    public void EmitterDrawItemWithActiveConsumerRemainsDelegated()
    {
        using var renderer = new RaylibPrimitiveRenderer(emitterConsumerActive: true);
        PrimitiveDrawBuffer draw = EmitterDraw(AssetKind.TrackEmitter, stableId: 202);

        Assert.DoesNotThrow(
            () => renderer.Draw(draw, default(Camera3D), new MeshAssetRegistry()));
    }

    private static PrimitiveDrawBuffer EmitterDraw(AssetKind assetKind, int stableId)
    {
        var draw = new PrimitiveDrawBuffer(capacity: 1);
        bool added = draw.TryAdd(new PrimitiveDrawItem
        {
            StableId = stableId,
            AssetKind = assetKind,
            RenderPath = VisualRenderPath.Primitive,
        });
        Assert.That(added, Is.True);
        return draw;
    }
}
