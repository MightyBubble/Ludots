using System;
using System.Linq;
using Ludots.Raylib.Render;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Presentation.Skia;
using NUnit.Framework;
using PresenterBlacksmithShowcaseMod;
using SkiaSharp;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PresenterBlacksmithShowcaseProductionPathTests
    {
        [Test]
        public void BlacksmithShowcase_StaticStructuresFlowThroughPresenterEntity_IntoInstancedStaticMesh_AndSkiaHud()
        {
            using var engine = PresenterBlacksmithShowcaseTestHarness.CreateEngine();
            var hudProjection = PresenterBlacksmithShowcaseTestHarness.CreateHeadlessHudProjection(engine);

            PresenterBlacksmithShowcaseTestHarness.LoadMap(engine, PresenterBlacksmithShowcaseIds.ShowcaseMapId, frames: 8);
            PresenterBlacksmithShowcaseTestHarness.TickWithHudProjection(engine, hudProjection, 16);

            var presenters = engine.GetService(CoreServiceKeys.PresenterEntityRuntime)
                ?? throw new InvalidOperationException("PresenterEntityRuntime missing.");
            var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
                ?? throw new InvalidOperationException("PresenterDefinitionRegistry missing.");
            var snapshot = engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer)
                ?? throw new InvalidOperationException("PresentationVisualSnapshotBuffer missing.");
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)
                ?? throw new InvalidOperationException("PresentationScreenHudBuffer missing.");
            var screenOverlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("ScreenOverlayBuffer missing.");
            var worldHudStrings = engine.GetService(CoreServiceKeys.PresentationWorldHudStrings);
            var textCatalog = engine.GetService(CoreServiceKeys.PresentationTextCatalog);
            var locale = engine.GetService(CoreServiceKeys.PresentationTextLocaleSelection);

            Assert.That(presenters.ActiveCount, Is.GreaterThan(0), "Showcase should bootstrap presenter entities through the production rule/runtime path.");

            int workshopLeftId = definitions.GetId(PresenterBlacksmithShowcaseIds.WorkshopLeftDefinitionId);
            int workshopRightId = definitions.GetId(PresenterBlacksmithShowcaseIds.WorkshopRightDefinitionId);
            int chimneyId = definitions.GetId(PresenterBlacksmithShowcaseIds.ChimneyDefinitionId);

            PrimitiveDrawItem[] instancedStaticVisuals = snapshot.GetSpan()
                .ToArray()
                .Where(item => item.TemplateId == workshopLeftId || item.TemplateId == workshopRightId || item.TemplateId == chimneyId)
                .ToArray();

            Assert.That(instancedStaticVisuals.Length, Is.EqualTo(3),
                "Base blacksmith showcase should emit exactly left/right workshop meshes plus the chimney through the production snapshot path.");
            Assert.That(instancedStaticVisuals.All(item => item.RenderPath == VisualRenderPath.InstancedStaticMesh), Is.True,
                "Static showcase structures must enter the latest raylib ISM lane through presenter AssetBinding config, not the legacy StaticMesh lane.");
            Assert.That(instancedStaticVisuals.All(item => item.Mobility == VisualMobility.Static), Is.True);
            Assert.That(instancedStaticVisuals.All(item => item.Visibility == VisualVisibility.Visible), Is.True);

            var planner = new StaticMeshAdapterSyncPlanner();
            planner.Sync(snapshot);

            StaticMeshAdapterBindingState[] instancedBindings = planner.ActiveBindings.Values
                .Where(binding => binding.Lane.RenderPath == VisualRenderPath.InstancedStaticMesh)
                .ToArray();

            Assert.That(instancedBindings.Length, Is.EqualTo(3),
                "Stable static-lane sync must see the showcase structures as persistent ISM bindings so raylib can reuse the latest adapter-side batching path.");
            Assert.That(instancedBindings.All(binding => binding.Lane.Mobility == VisualMobility.Static), Is.True);

            Assert.That(screenHud.GetBarSpan().Length, Is.GreaterThanOrEqualTo(1),
                "Durability HUD bar should flow through WorldHud -> ScreenHud production buffers.");
            Assert.That(screenHud.GetTextSpan().Length, Is.GreaterThanOrEqualTo(1),
                "Durability HUD text should flow through WorldHud -> ScreenHud production buffers.");

            var builder = new PresentationOverlaySceneBuilder(
                screenHud,
                worldHudStrings,
                textCatalog,
                locale,
                screenOverlay);
            var scene = new PresentationOverlayScene(screenHud.Capacity + ScreenOverlayBuffer.MaxItems);
            builder.Build(scene);

            Assert.That(scene.GetLaneSpan(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Bar).Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(scene.GetLaneSpan(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Text).Length, Is.GreaterThanOrEqualTo(1));

            using var renderer = new SkiaOverlayRenderer();
            using var surface = SKSurface.Create(new SKImageInfo(1280, 720));
            Assert.That(surface, Is.Not.Null, "Skia surface creation failed for showcase HUD verification.");

            renderer.ResetFrameStats();
            renderer.Render(scene, surface!.Canvas, PresentationOverlayLayer.UnderUi);
            int rebuiltFirstFrame = renderer.RebuiltLaneCountLastFrame;
            Assert.That(rebuiltFirstFrame, Is.GreaterThanOrEqualTo(1),
                "First HUD render should build the retained Skia under-UI lanes.");

            renderer.ResetFrameStats();
            renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.UnderUi);
            Assert.That(renderer.RebuiltLaneCountLastFrame, Is.EqualTo(0),
                "Second HUD render should reuse the retained Skia lane cache when nothing changed.");
            Assert.That(renderer.CachedTextLayoutCount, Is.GreaterThanOrEqualTo(1),
                "Skia HUD text should populate the shared text layout cache on the production overlay path.");
        }
    }
}
