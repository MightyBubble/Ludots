using System;
using System.Numerics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PrimitiveDrawBufferFailFastTests
    {
        [Test]
        public void TryAdd_WhenFull_ReturnsFalse_TracksDropped_AndNeverResizes()
        {
            var buffer = new PrimitiveDrawBuffer(capacity: 2);

            Assert.That(buffer.TryAdd(CreateItem(1)), Is.True);
            Assert.That(buffer.TryAdd(CreateItem(2)), Is.True);
            Assert.That(buffer.TryAdd(CreateItem(3)), Is.False);

            Assert.That(buffer.Count, Is.EqualTo(2));
            Assert.That(buffer.Capacity, Is.EqualTo(2), "Overflow must never trigger dynamic resizing.");
            Assert.That(buffer.DroppedSinceClear, Is.EqualTo(1), "Non-throwing TryAdd must keep tracking dropped items.");
            Assert.That(buffer.DroppedTotal, Is.EqualTo(1));
        }

        [Test]
        public void Add_WhenFull_ThrowsStableOverflowError_WithoutSilentDropCounts_AndNeverResizes()
        {
            var buffer = new PrimitiveDrawBuffer(capacity: 1);
            buffer.Add(CreateItem(1));

            var ex = Assert.Throws<InvalidOperationException>(() => buffer.Add(CreateItem(2)));

            Assert.That(ex!.Message, Does.Contain(PrimitiveDrawBuffer.OverflowErrorCode));
            Assert.That(ex.Message, Does.Contain("stableId=2"));
            Assert.That(buffer.Count, Is.EqualTo(1), "Overflow must not append the rejected item.");
            Assert.That(buffer.Capacity, Is.EqualTo(1), "Overflow must never trigger dynamic resizing.");
            Assert.That(buffer.DroppedSinceClear, Is.EqualTo(0), "Throwing overflow is a loud failure, not a silent drop.");
            Assert.That(buffer.DroppedTotal, Is.EqualTo(0));
        }

        [Test]
        public void ApplyStaticMeshDelta_WhenFull_ThrowsOverflowInsteadOfDroppingNewStatic()
        {
            var buffer = new PrimitiveDrawBuffer(capacity: 1);
            buffer.TryAdd(CreateStaticLaneItem(1));

            var changed = new[] { CreateStaticLaneItem(2) };
            var ex = Assert.Throws<InvalidOperationException>(
                () => buffer.ApplyStaticMeshDelta(changed, ReadOnlySpan<int>.Empty));

            Assert.That(ex!.Message, Does.Contain(PrimitiveDrawBuffer.OverflowErrorCode));
            Assert.That(buffer.Count, Is.EqualTo(1));
            Assert.That(buffer.DroppedSinceClear, Is.EqualTo(0), "Delta overflow is a loud failure, not a silent drop.");
        }

        [Test]
        public void Emit_WhenDrawBufferFull_ThrowsOverflow_AndCommitsNothingToSiblingBuffers()
        {
            var drawBuffer = new PrimitiveDrawBuffer(capacity: 1);
            var snapshotBuffer = new PrimitiveDrawBuffer(capacity: 8);
            var proxyBuffer = new PresentationVisualProxyBuffer(capacity: 8);
            var emitter = new PresentationVisualProxyEmitter(drawBuffer, snapshotBuffer, proxyBuffer);

            emitter.Emit(CreateProxy(1, visible: true));
            Assert.That(drawBuffer.Count, Is.EqualTo(1));

            var ex = Assert.Throws<InvalidOperationException>(() => emitter.Emit(CreateProxy(2, visible: true)));

            Assert.That(ex!.Message, Does.Contain(PrimitiveDrawBuffer.OverflowErrorCode));
            Assert.That(drawBuffer.Count, Is.EqualTo(1), "Rejected item must not enter the draw buffer.");
            Assert.That(drawBuffer.DroppedSinceClear, Is.EqualTo(0), "Emitter path must fail loudly, not silently drop.");
            Assert.That(proxyBuffer.Count, Is.EqualTo(1), "No half-commit: rejected proxy must not enter the proxy buffer.");
            Assert.That(snapshotBuffer.Count, Is.EqualTo(1), "No half-commit: rejected proxy must not enter the snapshot buffer.");
        }

        [Test]
        public void Emit_WhenSnapshotBufferFull_ThrowsOverflow_AndCommitsNothingToSiblingBuffers()
        {
            var drawBuffer = new PrimitiveDrawBuffer(capacity: 8);
            var snapshotBuffer = new PrimitiveDrawBuffer(capacity: 1);
            var proxyBuffer = new PresentationVisualProxyBuffer(capacity: 8);
            var emitter = new PresentationVisualProxyEmitter(drawBuffer, snapshotBuffer, proxyBuffer);

            emitter.Emit(CreateProxy(1, visible: true));

            var ex = Assert.Throws<InvalidOperationException>(() => emitter.Emit(CreateProxy(2, visible: true)));

            Assert.That(ex!.Message, Does.Contain("Presentation visual snapshot buffer overflowed"));
            Assert.That(drawBuffer.Count, Is.EqualTo(1), "No half-commit: rejected proxy must not enter the draw buffer.");
            Assert.That(proxyBuffer.Count, Is.EqualTo(1), "No half-commit: rejected proxy must not enter the proxy buffer.");
            Assert.That(snapshotBuffer.Count, Is.EqualTo(1), "No half-commit: rejected proxy must not enter the snapshot buffer.");
        }

        [Test]
        public void Emit_WhenProxyBufferFull_ThrowsOverflow_AndCommitsNothingToSiblingBuffers()
        {
            var drawBuffer = new PrimitiveDrawBuffer(capacity: 8);
            var snapshotBuffer = new PrimitiveDrawBuffer(capacity: 8);
            var proxyBuffer = new PresentationVisualProxyBuffer(capacity: 1);
            var emitter = new PresentationVisualProxyEmitter(drawBuffer, snapshotBuffer, proxyBuffer);

            emitter.Emit(CreateProxy(1, visible: true));

            var ex = Assert.Throws<InvalidOperationException>(() => emitter.Emit(CreateProxy(2, visible: true)));

            Assert.That(ex!.Message, Does.Contain("Presentation visual proxy buffer overflowed"));
            Assert.That(drawBuffer.Count, Is.EqualTo(1), "No half-commit: rejected proxy must not enter the draw buffer.");
            Assert.That(proxyBuffer.Count, Is.EqualTo(1), "No half-commit: rejected proxy must not enter the proxy buffer.");
            Assert.That(snapshotBuffer.Count, Is.EqualTo(1), "No half-commit: rejected proxy must not enter the snapshot buffer.");
        }

        [Test]
        public void Emit_WhenSkinnedBatchBufferFull_ThrowsOverflow_AndCommitsNothingToSiblingBuffers()
        {
            var drawBuffer = new PrimitiveDrawBuffer(capacity: 8);
            var snapshotBuffer = new PrimitiveDrawBuffer(capacity: 8);
            var proxyBuffer = new PresentationVisualProxyBuffer(capacity: 8);
            var skinnedBatchBuffer = new SkinnedVisualBatchBuffer(capacity: 1);
            var emitter = new PresentationVisualProxyEmitter(drawBuffer, snapshotBuffer, proxyBuffer, skinnedBatchBuffer);

            emitter.Emit(CreateProxy(1, visible: true, renderPath: VisualRenderPath.SkinnedMesh));

            var ex = Assert.Throws<InvalidOperationException>(
                () => emitter.Emit(CreateProxy(2, visible: true, renderPath: VisualRenderPath.SkinnedMesh)));

            Assert.That(ex!.Message, Does.Contain("Skinned visual batch buffer overflowed"));
            Assert.That(drawBuffer.Count, Is.EqualTo(1), "No half-commit: rejected proxy must not enter the draw buffer.");
            Assert.That(proxyBuffer.Count, Is.EqualTo(1), "No half-commit: rejected proxy must not enter the proxy buffer.");
            Assert.That(snapshotBuffer.Count, Is.EqualTo(1), "No half-commit: rejected proxy must not enter the snapshot buffer.");
            Assert.That(skinnedBatchBuffer.Count, Is.EqualTo(1), "No half-commit: rejected proxy must not enter the skinned batch buffer.");
        }

        [Test]
        public void Emit_InvisibleProxy_DoesNotConsumeDrawBufferCapacity()
        {
            var drawBuffer = new PrimitiveDrawBuffer(capacity: 1);
            var emitter = new PresentationVisualProxyEmitter(drawBuffer);

            emitter.Emit(CreateProxy(1, visible: false));
            emitter.Emit(CreateProxy(2, visible: false));

            Assert.That(drawBuffer.Count, Is.EqualTo(0));
            Assert.That(drawBuffer.DroppedSinceClear, Is.EqualTo(0));
        }

        [Test]
        public void Emit_ApplyStaticInstanceDelta_WhenDrawBufferFull_ThrowsOverflow()
        {
            var drawBuffer = new PrimitiveDrawBuffer(capacity: 1);
            var emitter = new PresentationVisualProxyEmitter(drawBuffer);
            emitter.Emit(CreateProxy(1, visible: true));

            var changed = new[] { CreateStaticLaneItem(2) };
            var ex = Assert.Throws<InvalidOperationException>(
                () => emitter.ApplyStaticInstanceDelta(changed, ReadOnlySpan<int>.Empty));

            Assert.That(ex!.Message, Does.Contain(PrimitiveDrawBuffer.OverflowErrorCode));
            Assert.That(drawBuffer.Count, Is.EqualTo(1));
        }

        private static PrimitiveDrawItem CreateItem(int stableId) => CreateStaticLaneItem(stableId);

        private static PrimitiveDrawItem CreateStaticLaneItem(int stableId)
        {
            return new PrimitiveDrawItem
            {
                MeshAssetId = 10,
                Position = new Vector3(stableId, 0f, 0f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
                Color = Vector4.One,
                StableId = stableId,
                MaterialId = 1,
                TemplateId = 1000 + stableId,
                RenderPath = VisualRenderPath.InstancedStaticMesh,
                Mobility = VisualMobility.Static,
                Flags = VisualRuntimeFlags.Visible,
                Visibility = VisualVisibility.Visible,
            };
        }

        private static PresentationVisualProxy CreateProxy(int stableId, bool visible, VisualRenderPath renderPath = VisualRenderPath.StaticMesh)
        {
            return new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Presenter,
                MeshAssetId = 10,
                MaterialId = 20,
                StableId = stableId,
                TemplateId = 101 + stableId,
                Position = new Vector3(stableId, 0f, 0f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
                Color = Vector4.One,
                RenderPath = renderPath,
                Visibility = visible ? VisualVisibility.Visible : VisualVisibility.Hidden,
                LOD = LODLevel.High,
            };
        }
    }
}
