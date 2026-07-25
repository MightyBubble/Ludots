using System;
using System.Numerics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Diagnostics;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresentationFrameReceiptBufferTests
    {
        private static readonly ProceduralMeshBounds UnitBounds =
            new(Vector3.Zero, Vector3.One * 0.5f);

        [Test]
        public void BuildTemplateSummaries_MergesPartsByStableIdAndExcludesOffscreenInstances()
        {
            var receipts = new PresentationFrameReceiptBuffer(capacity: 4);
            receipts.RecordSubmitted(
                stableId: 11,
                templateId: 7,
                position: new Vector3(-0.2f, 0f, 0f),
                rotation: Quaternion.Identity,
                scale: new Vector3(0.2f),
                localBounds: UnitBounds);
            receipts.RecordSubmitted(
                stableId: 11,
                templateId: 7,
                position: new Vector3(0.2f, 0f, 0f),
                rotation: Quaternion.Identity,
                scale: new Vector3(0.2f),
                localBounds: UnitBounds);
            receipts.RecordSubmitted(
                stableId: 12,
                templateId: 7,
                position: new Vector3(2f, 0f, 0f),
                rotation: Quaternion.Identity,
                scale: new Vector3(0.2f),
                localBounds: UnitBounds);

            var projection = new ProjectionSnapshot(Matrix4x4.Identity, new Vector2(1000f, 500f));
            PresentationTemplateReceiptSummary[] summaries = receipts.BuildTemplateSummaries(in projection);

            Assert.That(summaries, Has.Length.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(summaries[0].TemplateId, Is.EqualTo(7));
                Assert.That(summaries[0].SubmittedCount, Is.EqualTo(2));
                Assert.That(summaries[0].OnscreenCount, Is.EqualTo(1));
                Assert.That(summaries[0].MinimumShortEdgePx, Is.EqualTo(50f).Within(0.01f));
                Assert.That(summaries[0].MinimumAreaPx2, Is.EqualTo(15000f).Within(0.01f));
            });
        }

        [Test]
        public void BuildTemplateSummaries_ReportsTinyProjectedInstancesWithoutHidingThem()
        {
            var receipts = new PresentationFrameReceiptBuffer(capacity: 1);
            receipts.RecordSubmitted(
                stableId: 21,
                templateId: 8,
                position: Vector3.Zero,
                rotation: Quaternion.Identity,
                scale: new Vector3(0.01f),
                localBounds: UnitBounds);

            var projection = new ProjectionSnapshot(Matrix4x4.Identity, new Vector2(1000f, 500f));
            PresentationTemplateReceiptSummary[] summaries = receipts.BuildTemplateSummaries(in projection);

            Assert.Multiple(() =>
            {
                Assert.That(summaries[0].OnscreenCount, Is.EqualTo(1));
                Assert.That(summaries[0].MinimumShortEdgePx, Is.EqualTo(2.5f).Within(0.01f));
                Assert.That(summaries[0].MinimumAreaPx2, Is.EqualTo(12.5f).Within(0.01f));
            });
        }

        [Test]
        public void RecordSubmitted_FailsFastForInvalidIdentityAndCapacityOverflow()
        {
            var receipts = new PresentationFrameReceiptBuffer(capacity: 1);

            Assert.That(
                () => receipts.RecordSubmitted(
                    stableId: 0,
                    templateId: 1,
                    position: Vector3.Zero,
                    rotation: Quaternion.Identity,
                    scale: Vector3.One,
                    localBounds: UnitBounds),
                Throws.InvalidOperationException);

            receipts.RecordSubmitted(
                stableId: 1,
                templateId: 1,
                position: Vector3.Zero,
                rotation: Quaternion.Identity,
                scale: Vector3.One,
                localBounds: UnitBounds);

            Assert.That(
                () => receipts.RecordSubmitted(
                    stableId: 2,
                    templateId: 1,
                    position: Vector3.Zero,
                    rotation: Quaternion.Identity,
                    scale: Vector3.One,
                    localBounds: UnitBounds),
                Throws.InvalidOperationException);

            receipts.BeginFrame();
            Assert.That(receipts.Count, Is.Zero);
        }
    }
}
