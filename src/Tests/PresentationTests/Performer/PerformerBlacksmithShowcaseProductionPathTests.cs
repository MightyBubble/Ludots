using System;
using System.Linq;
using Ludots.Core.Presentation.AdapterSync;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Presentation.Skia;
using NUnit.Framework;
using PerformerBlacksmithShowcaseMod;
using SkiaSharp;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PerformerBlacksmithShowcaseProductionPathTests
    {
        [Test]
        public void BlacksmithShowcase_StaticStructuresFlowThroughPerformerEntity_IntoInstancedStaticMesh_AndSkiaHud()
        {
            using var engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            var hudProjection = PerformerBlacksmithShowcaseTestHarness.CreateHeadlessHudProjection(engine);

            PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, PerformerBlacksmithShowcaseIds.ShowcaseMapId, frames: 8);
            PerformerBlacksmithShowcaseTestHarness.TickWithHudProjection(engine, hudProjection, 16);

            var performers = engine.GetService(CoreServiceKeys.PerformerEntityRuntime)
                ?? throw new InvalidOperationException("PerformerEntityRuntime missing.");
            var definitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
                ?? throw new InvalidOperationException("PerformerDefinitionRegistry missing.");
            var snapshot = engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer)
                ?? throw new InvalidOperationException("PresentationVisualSnapshotBuffer missing.");
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)
                ?? throw new InvalidOperationException("PresentationScreenHudBuffer missing.");
            var screenOverlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("ScreenOverlayBuffer missing.");
            var worldHudStrings = engine.GetService(CoreServiceKeys.PresentationWorldHudStrings);
            var textCatalog = engine.GetService(CoreServiceKeys.PresentationTextCatalog);
            var locale = engine.GetService(CoreServiceKeys.PresentationTextLocaleSelection);

            Assert.That(performers.ActiveCount, Is.GreaterThan(0), "Showcase should bootstrap performer entities through the production rule/runtime path.");

            int workshopLeftId = definitions.GetId(PerformerBlacksmithShowcaseIds.WorkshopLeftDefinitionId);
            int workshopRightId = definitions.GetId(PerformerBlacksmithShowcaseIds.WorkshopRightDefinitionId);
            int chimneyId = definitions.GetId(PerformerBlacksmithShowcaseIds.ChimneyDefinitionId);

            PrimitiveDrawItem[] instancedStaticVisuals = snapshot.GetSpan()
                .ToArray()
                .Where(item => item.TemplateId == workshopLeftId || item.TemplateId == workshopRightId || item.TemplateId == chimneyId)
                .ToArray();

            Assert.That(instancedStaticVisuals.Length, Is.EqualTo(3),
                "Base blacksmith showcase should emit exactly left/right workshop meshes plus the chimney through the production snapshot path.");
            Assert.That(instancedStaticVisuals.All(item => item.RenderPath == VisualRenderPath.InstancedStaticMesh), Is.True,
                "Static showcase structures must enter the latest raylib ISM lane through performer AssetBinding config, not the legacy StaticMesh lane.");
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
