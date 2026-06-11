using System;
using System.Numerics;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;
using Raylib_cs;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class RaylibPrimitiveRendererContractTests
    {
        [Test]
        public void Draw_SkipsHostOwnedSurfaceItems_WithoutMeshFallback()
        {
            var draw = new PrimitiveDrawBuffer();
            Assert.That(draw.TryAdd(CreateItem(AssetKind.Surface, VisualRenderPath.Surface)), Is.True);

            using var renderer = new RaylibPrimitiveRenderer();

            Assert.DoesNotThrow(() => renderer.Draw(draw, default(Camera3D), new MeshAssetRegistry()));
        }

        [Test]
        public void Draw_Throws_WhenNonSurfaceItemUsesSurfacePath()
        {
            var draw = new PrimitiveDrawBuffer();
            Assert.That(draw.TryAdd(CreateItem(AssetKind.Mesh, VisualRenderPath.Surface)), Is.True);

            using var renderer = new RaylibPrimitiveRenderer();

            var ex = Assert.Throws<InvalidOperationException>(
                () => renderer.Draw(draw, default(Camera3D), new MeshAssetRegistry()));
            Assert.That(ex!.Message, Does.Contain("non-Surface"));
        }

        private static PrimitiveDrawItem CreateItem(AssetKind assetKind, VisualRenderPath renderPath)
        {
            return new PrimitiveDrawItem
            {
                AssetKind = assetKind,
                MeshAssetId = 999,
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
                Color = Vector4.One,
                StableId = 101,
                MaterialId = 1,
                TemplateId = 1,
                RenderPath = renderPath,
                Mobility = VisualMobility.Static,
                Flags = VisualRuntimeFlags.Visible,
                Visibility = VisualVisibility.Visible,
            };
        }
    }
}
