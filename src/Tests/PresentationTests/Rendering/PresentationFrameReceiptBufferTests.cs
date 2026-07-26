using System;
using System.Linq;
using System.Numerics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Diagnostics;
using Ludots.Core.Presentation.Performers;
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
                ownerStableId: 11,
                visualStableId: 1011,
                templateId: 7,
                worldPosition: new Vector3(1f, 0f, 2f),
                position: new Vector3(-0.2f, 0f, 0f),
                rotation: Quaternion.Identity,
                scale: new Vector3(0.2f),
                localBounds: UnitBounds);
            receipts.RecordSubmitted(
                ownerStableId: 11,
                visualStableId: 1011,
                templateId: 7,
                worldPosition: new Vector3(1f, 0f, 2f),
                position: new Vector3(0.2f, 0f, 0f),
                rotation: Quaternion.Identity,
                scale: new Vector3(0.2f),
                localBounds: UnitBounds);
            receipts.RecordSubmitted(
                ownerStableId: 12,
                visualStableId: 1012,
                templateId: 7,
                worldPosition: new Vector3(2f, 0f, 2f),
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
                ownerStableId: 21,
                visualStableId: 2021,
                templateId: 8,
                worldPosition: Vector3.Zero,
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
        public void BuildOnscreenStateReceipt_IsOrderIndependentAndChangesWhenVisibleWorldMoves()
        {
            var receipts = new PresentationFrameReceiptBuffer(capacity: 2);
            var projection = new ProjectionSnapshot(Matrix4x4.Identity, new Vector2(1000f, 500f));
            Record(receipts, stableId: 11, templateId: 7, x: -0.2f);
            Record(receipts, stableId: 12, templateId: 7, x: 0.2f);
            PresentationOnscreenStateReceipt original = receipts.BuildOnscreenStateReceipt(in projection);
            PresentationOnscreenStateReceipt originalTemplate = receipts.BuildOnscreenStateReceipt(in projection, templateId: 7);

            receipts.BeginFrame();
            Record(receipts, stableId: 12, templateId: 7, x: 0.2f);
            Record(receipts, stableId: 11, templateId: 7, x: -0.2f);
            PresentationOnscreenStateReceipt reordered = receipts.BuildOnscreenStateReceipt(in projection);

            receipts.BeginFrame();
            Record(receipts, stableId: 11, templateId: 7, x: -0.2f);
            Record(receipts, stableId: 12, templateId: 7, x: 0.4f);
            PresentationOnscreenStateReceipt moved = receipts.BuildOnscreenStateReceipt(in projection);
            PresentationOnscreenStateReceipt movedTemplate = receipts.BuildOnscreenStateReceipt(in projection, templateId: 7);

            Assert.Multiple(() =>
            {
                Assert.That(original.SubmissionCount, Is.EqualTo(2));
                Assert.That(original.StateSha256, Has.Length.EqualTo(64));
                Assert.That(reordered, Is.EqualTo(original));
                Assert.That(moved.StateSha256, Is.Not.EqualTo(original.StateSha256));
                Assert.That(movedTemplate.StateSha256, Is.Not.EqualTo(originalTemplate.StateSha256));
            });
        }

        [Test]
        public void BuildOnscreenInstanceReceipts_MergesPartsAndKeepsTheEntityWorldPosition()
        {
            var receipts = new PresentationFrameReceiptBuffer(capacity: 3);
            var worldPosition = new Vector3(1.23f, 0f, -4.56f);
            receipts.RecordSubmitted(
                ownerStableId: 11,
                visualStableId: 1011,
                templateId: 7,
                worldPosition,
                position: new Vector3(-0.2f, 0f, 0f),
                rotation: Quaternion.Identity,
                scale: new Vector3(0.2f),
                localBounds: UnitBounds);
            receipts.RecordSubmitted(
                ownerStableId: 11,
                visualStableId: 1011,
                templateId: 7,
                worldPosition,
                position: new Vector3(0.2f, 0f, 0f),
                rotation: Quaternion.Identity,
                scale: new Vector3(0.2f),
                localBounds: UnitBounds);
            receipts.RecordSubmitted(
                ownerStableId: 12,
                visualStableId: 1012,
                templateId: 7,
                worldPosition: new Vector3(2f, 0f, 2f),
                position: new Vector3(2f, 0f, 0f),
                rotation: Quaternion.Identity,
                scale: new Vector3(0.2f),
                localBounds: UnitBounds);

            var projection = new ProjectionSnapshot(Matrix4x4.Identity, new Vector2(1000f, 500f));
            PresentationOnscreenInstanceReceipt[] result =
                receipts.BuildOnscreenInstanceReceipts(in projection);

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(result[0].OwnerStableId, Is.EqualTo(11));
                Assert.That(result[0].VisualStableId, Is.EqualTo(1011));
                Assert.That(result[0].TemplateId, Is.EqualTo(7));
                Assert.That(result[0].WorldXCm, Is.EqualTo(123));
                Assert.That(result[0].WorldYCm, Is.EqualTo(-456));
                Assert.That(result[0].ScreenLeftPx, Is.EqualTo(350f).Within(0.01f));
                Assert.That(result[0].ScreenRightPx, Is.EqualTo(650f).Within(0.01f));
                Assert.That(result[0].ShortEdgePx, Is.EqualTo(50f).Within(0.01f));
                Assert.That(result[0].AreaPx2, Is.EqualTo(15000f).Within(0.01f));
            });
        }

        [Test]
        public void BuildOnscreenInstanceReceipts_KeepsDerivedVisualIdsAssociatedWithTheirOwner()
        {
            const int ownerStableId = 37;
            const int templateId = 7;
            int firstVisualStableId = PerformerBehaviorRuntimeUtility.ComposeVisualStableId(
                ownerStableId,
                slotIndex: 0,
                AssetKind.Mesh,
                discriminator: templateId);
            int secondVisualStableId = PerformerBehaviorRuntimeUtility.ComposeVisualStableId(
                ownerStableId,
                slotIndex: 1,
                AssetKind.Mesh,
                discriminator: templateId);
            var receipts = new PresentationFrameReceiptBuffer(capacity: 2);
            receipts.RecordSubmitted(
                ownerStableId,
                firstVisualStableId,
                templateId,
                worldPosition: Vector3.Zero,
                position: new Vector3(-0.2f, 0f, 0f),
                rotation: Quaternion.Identity,
                scale: new Vector3(0.2f),
                localBounds: UnitBounds);
            receipts.RecordSubmitted(
                ownerStableId,
                secondVisualStableId,
                templateId,
                worldPosition: Vector3.Zero,
                position: new Vector3(0.2f, 0f, 0f),
                rotation: Quaternion.Identity,
                scale: new Vector3(0.2f),
                localBounds: UnitBounds);

            var projection = new ProjectionSnapshot(Matrix4x4.Identity, new Vector2(1000f, 500f));
            PresentationOnscreenInstanceReceipt[] result =
                receipts.BuildOnscreenInstanceReceipts(in projection);

            Assert.Multiple(() =>
            {
                Assert.That(firstVisualStableId, Is.Not.EqualTo(ownerStableId));
                Assert.That(secondVisualStableId, Is.Not.EqualTo(ownerStableId));
                Assert.That(result, Has.Length.EqualTo(2));
                Assert.That(result, Has.All.Property(nameof(PresentationOnscreenInstanceReceipt.OwnerStableId)).EqualTo(ownerStableId));
                Assert.That(
                    result.Select(static item => item.VisualStableId),
                    Is.EqualTo(new[] { firstVisualStableId, secondVisualStableId }.OrderBy(static value => value)));
            });
        }

        [Test]
        public void BuildOnscreenInstanceReceipts_RejectsPartsWithConflictingEntityPositions()
        {
            var receipts = new PresentationFrameReceiptBuffer(capacity: 2);
            receipts.RecordSubmitted(
                ownerStableId: 11,
                visualStableId: 1011,
                templateId: 7,
                worldPosition: Vector3.Zero,
                position: new Vector3(-0.2f, 0f, 0f),
                rotation: Quaternion.Identity,
                scale: new Vector3(0.2f),
                localBounds: UnitBounds);
            receipts.RecordSubmitted(
                ownerStableId: 11,
                visualStableId: 1011,
                templateId: 7,
                worldPosition: Vector3.One,
                position: new Vector3(0.2f, 0f, 0f),
                rotation: Quaternion.Identity,
                scale: new Vector3(0.2f),
                localBounds: UnitBounds);
            var projection = new ProjectionSnapshot(Matrix4x4.Identity, new Vector2(1000f, 500f));

            Assert.That(
                () => receipts.BuildOnscreenInstanceReceipts(in projection),
                Throws.InvalidOperationException);
        }

        [TestCase(-2f)]
        [TestCase(2f)]
        public void BuildOnscreenInstanceReceipts_ExcludesInstancesOutsideTheProjectionDepthRange(float z)
        {
            var receipts = new PresentationFrameReceiptBuffer(capacity: 1);
            receipts.RecordSubmitted(
                ownerStableId: 11,
                visualStableId: 1011,
                templateId: 7,
                worldPosition: new Vector3(0f, 0f, z),
                position: new Vector3(0f, 0f, z),
                rotation: Quaternion.Identity,
                scale: new Vector3(0.2f),
                localBounds: UnitBounds);
            var projection = new ProjectionSnapshot(Matrix4x4.Identity, new Vector2(1000f, 500f));

            PresentationOnscreenInstanceReceipt[] result =
                receipts.BuildOnscreenInstanceReceipts(in projection);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void BuildOnscreenInstanceReceipts_KeepsTheVisiblePartOfBoundsCrossingTheNearPlane()
        {
            var receipts = new PresentationFrameReceiptBuffer(capacity: 1);
            receipts.RecordSubmitted(
                ownerStableId: 11,
                visualStableId: 1011,
                templateId: 7,
                worldPosition: Vector3.Zero,
                position: Vector3.Zero,
                rotation: Quaternion.Identity,
                scale: Vector3.One,
                localBounds: UnitBounds);
            var projection = new ProjectionSnapshot(Matrix4x4.Identity, new Vector2(1000f, 500f));

            PresentationOnscreenInstanceReceipt[] result =
                receipts.BuildOnscreenInstanceReceipts(in projection);

            Assert.That(result, Has.Length.EqualTo(1));
        }

        [Test]
        public void RecordSubmitted_FailsFastForInvalidIdentityAndCapacityOverflow()
        {
            var receipts = new PresentationFrameReceiptBuffer(capacity: 1);

            Assert.That(
                () => receipts.RecordSubmitted(
                    ownerStableId: 0,
                    visualStableId: 1,
                    templateId: 1,
                    worldPosition: Vector3.Zero,
                    position: Vector3.Zero,
                    rotation: Quaternion.Identity,
                    scale: Vector3.One,
                    localBounds: UnitBounds),
                Throws.InvalidOperationException);

            receipts.RecordSubmitted(
                ownerStableId: 1,
                visualStableId: 101,
                templateId: 1,
                worldPosition: Vector3.Zero,
                position: Vector3.Zero,
                rotation: Quaternion.Identity,
                scale: Vector3.One,
                localBounds: UnitBounds);

            Assert.That(
                () => receipts.RecordSubmitted(
                    ownerStableId: 2,
                    visualStableId: 102,
                    templateId: 1,
                    worldPosition: Vector3.Zero,
                    position: Vector3.Zero,
                    rotation: Quaternion.Identity,
                    scale: Vector3.One,
                    localBounds: UnitBounds),
                Throws.InvalidOperationException);

            receipts.BeginFrame();
            Assert.That(receipts.Count, Is.Zero);
        }

        [Test]
        public void RecordSubmitted_IgnoresUnidentifiedPrimitivesButRejectsPartialIdentity()
        {
            var receipts = new PresentationFrameReceiptBuffer(capacity: 1);

            receipts.RecordSubmitted(
                ownerStableId: 0,
                visualStableId: 0,
                templateId: 0,
                worldPosition: Vector3.Zero,
                position: Vector3.Zero,
                rotation: Quaternion.Identity,
                scale: Vector3.One,
                localBounds: UnitBounds);

            Assert.That(receipts.Count, Is.Zero);
            Assert.That(
                () => receipts.RecordSubmitted(
                    ownerStableId: 1,
                    visualStableId: 101,
                    templateId: 0,
                    worldPosition: Vector3.Zero,
                    position: Vector3.Zero,
                    rotation: Quaternion.Identity,
                    scale: Vector3.One,
                    localBounds: UnitBounds),
                Throws.InvalidOperationException);
            Assert.That(
                () => receipts.RecordSubmitted(
                    ownerStableId: 0,
                    visualStableId: 0,
                    templateId: 1,
                    worldPosition: Vector3.Zero,
                    position: Vector3.Zero,
                    rotation: Quaternion.Identity,
                    scale: Vector3.One,
                    localBounds: UnitBounds),
                Throws.InvalidOperationException);
        }

        [Test]
        public void RecordSubmitted_RejectsMissingBoundsInsteadOfInventingVisibilityEvidence()
        {
            var receipts = new PresentationFrameReceiptBuffer(capacity: 1);

            Assert.That(
                () => receipts.RecordSubmitted(
                    ownerStableId: 1,
                    visualStableId: 101,
                    templateId: 1,
                    worldPosition: Vector3.Zero,
                    position: Vector3.Zero,
                    rotation: Quaternion.Identity,
                    scale: Vector3.One,
                    localBounds: default),
                Throws.InvalidOperationException);
            Assert.That(receipts.Count, Is.Zero);
        }

        private static void Record(
            PresentationFrameReceiptBuffer receipts,
            int stableId,
            int templateId,
            float x)
        {
            receipts.RecordSubmitted(
                stableId,
                stableId + 1000,
                templateId,
                worldPosition: new Vector3(x, 0f, 0f),
                position: new Vector3(x, 0f, 0f),
                rotation: Quaternion.Identity,
                scale: new Vector3(0.2f),
                localBounds: UnitBounds);
        }
    }
}
