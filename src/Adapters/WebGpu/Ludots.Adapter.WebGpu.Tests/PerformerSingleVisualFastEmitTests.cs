using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using NUnit.Framework;

namespace Ludots.Adapter.WebGpu.Tests;

[TestFixture]
public sealed class PerformerSingleVisualFastEmitTests
{
    [Test]
    public void StaticSingleSkinnedBinding_UsesDirectBatchPath_WithEquivalentPayload()
    {
        using World world = World.Create();
        var definitions = new PerformerDefinitionRegistry();
        Vector4 expectedColor = new(0.15f, 0.7f, 0.35f, 1f);
        int definitionId = definitions.Register(
            "webgpu.single-static-skinned",
            new PerformerDefinition
            {
                DefaultColor = expectedColor,
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.SkinnedMesh,
                            AssetId = 17,
                            MaterialId = 29,
                            RenderPath = VisualRenderPath.GpuSkinnedInstance,
                            Mobility = VisualMobility.Movable,
                            LocalOffset = new Vector3(0f, 0.25f, 0f),
                            LocalRotation = Quaternion.Identity,
                            LocalScale = new Vector3(0.5f, 0.75f, 0.5f),
                        },
                    },
                ],
            });

        var runtime = new PerformerEntityRuntime(world);
        runtime.BindDefinitions(definitions);
        Assert.That(definitions.TryGet(definitionId, out PerformerDefinition definition), Is.True);
        Entity owner = world.Create(
            new PresentationStableId { Value = 101 },
            new VisualTransform
            {
                Position = new Vector3(3f, 2f, 5f),
                Rotation = Quaternion.Identity,
                Scale = new Vector3(2f, 2f, 2f),
            },
            new CullState { IsVisible = true, LOD = LODLevel.High });
        Entity performer = runtime.Create(
            definitionId,
            owner,
            scopeId: 1,
            PresentationAnchorKind.Entity,
            Vector3.Zero,
            stableId: 1001,
            Entity.Null,
            definition);
        world.Get<PerformerState>(performer).BehaviorActiveMask = 1u;

        var requests = new PresentationRequestBuffer();
        var skinnedBatch = new SkinnedVisualBatchBuffer(4);
        var timings = new PresentationTimingDiagnostics();
        using var emit = new PerformerEmitSystem(
            world,
            runtime,
            definitions,
            requests,
            new Dictionary<string, object>(),
            timingDiagnostics: timings,
            skinnedVisualBatchBuffer: skinnedBatch);

        emit.Update(0.016f);

        Assert.That(timings.PerformerEmitSingleVisualFastCountLastFrame, Is.EqualTo(1));
        Assert.That(requests.Count, Is.Zero);
        Assert.That(skinnedBatch.Count, Is.EqualTo(1));
        SkinnedVisualBatchItem item = skinnedBatch.GetSpan()[0];
        Assert.Multiple(() =>
        {
            Assert.That(item.MeshAssetId, Is.EqualTo(17));
            Assert.That(item.MaterialId, Is.EqualTo(29));
            Assert.That(item.TemplateId, Is.EqualTo(definitionId));
            Assert.That(item.AnimationProfileId, Is.Zero);
            Assert.That(item.RenderPath, Is.EqualTo(VisualRenderPath.GpuSkinnedInstance));
            Assert.That(item.AssetKind, Is.EqualTo(AssetKind.SkinnedMesh));
            Assert.That(item.Color, Is.EqualTo(expectedColor));
            Assert.That(item.Scale, Is.EqualTo(new Vector3(0.5f, 0.75f, 0.5f)));
            Assert.That(item.Visibility, Is.EqualTo(VisualVisibility.Visible));
            Assert.That(item.LOD, Is.EqualTo(LODLevel.High));
        });
    }
}
