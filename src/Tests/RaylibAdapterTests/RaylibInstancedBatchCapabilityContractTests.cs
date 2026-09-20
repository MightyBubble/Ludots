using System;
using Arch.Core;
using Ludots.Adapter.Raylib;
using Ludots.Core.Presentation.Instancing;
using Ludots.Core.Presentation.Rendering;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter
{
    [TestFixture]
    public sealed class RaylibInstancedBatchCapabilityContractTests
    {
        [Test]
        public void ComposerCapabilities_DeclareFlatInstancedBatchOnly()
        {
            PresentationVisualCapabilities visuals = RaylibHostComposer.ComposePresentationVisualCapabilities();

            Assert.That(visuals.HasFlag(PresentationVisualCapabilities.InstancedStaticMeshBatch), Is.True);
            Assert.That(visuals.HasFlag(PresentationVisualCapabilities.HierarchicalInstancedStaticMeshBatch), Is.False);
        }

        [Test]
        public void ComposerCapabilities_PassTickTailValidatorForFlatTypedRequests()
        {
            var requests = new InstancedBatchRequestBuffer();
            var operations = new InstancedBatchOperationBuffer();
            requests.Add(BuildTypedRequest(VisualRenderPath.InstancedStaticMesh));

            var capabilities = new PresentationAdapterCapabilities(RaylibHostComposer.ComposePresentationVisualCapabilities());

            Assert.DoesNotThrow(() => InstancedBatchCapabilityValidator.Validate(requests, operations, capabilities));
        }

        [Test]
        public void ComposerCapabilities_KeepHierarchicalTypedRequestsFailLoud()
        {
            var requests = new InstancedBatchRequestBuffer();
            var operations = new InstancedBatchOperationBuffer();
            requests.Add(BuildTypedRequest(VisualRenderPath.HierarchicalInstancedStaticMesh));

            var capabilities = new PresentationAdapterCapabilities(RaylibHostComposer.ComposePresentationVisualCapabilities());

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => InstancedBatchCapabilityValidator.Validate(requests, operations, capabilities))!;
            Assert.That(ex.Message, Does.Contain("HierarchicalInstancedStaticMesh"));
        }

        [Test]
        public void EnsureInstancedBatchLaneSourceBound_FailsWhenCapabilityDeclaredWithoutLaneSource()
        {
            PresentationVisualCapabilities visuals = RaylibHostComposer.ComposePresentationVisualCapabilities();

            Assert.Throws<InvalidOperationException>(
                () => RaylibHostComposer.EnsureInstancedBatchLaneSourceBound(visuals, laneSourceBound: false));
            Assert.DoesNotThrow(() => RaylibHostComposer.EnsureInstancedBatchLaneSourceBound(visuals, laneSourceBound: true));
            Assert.DoesNotThrow(() => RaylibHostComposer.EnsureInstancedBatchLaneSourceBound(
                PresentationVisualCapabilities.Decal,
                laneSourceBound: false));
        }

        private static InstancedBatchRequest BuildTypedRequest(VisualRenderPath renderPath)
        {
            return new InstancedBatchRequest(
                InstancedBatchRequestKind.CreateOrUpdate,
                1,
                100,
                default,
                default,
                new InstancedBatchAddress(1, new InstancedBatchOwnerId(1), new InstancedBatchGroupId(1), new InstancedBatchBucketId(1), new InstancedBatchSpanId(1)),
                renderPath,
                10,
                20,
                0,
                1,
                finalChunk: true);
        }
    }
}
