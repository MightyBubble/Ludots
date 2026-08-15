using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PrimitiveDrawBufferIncrementalStaticTests
    {
        [Test]
        public void ApplyStaticMeshDelta_UpdatesChangedStaticInPlace_PreservingOtherItems()
        {
            var buffer = new PrimitiveDrawBuffer(64);
            buffer.TryAdd(CreateStatic(101, posX: 1f));
            buffer.TryAdd(CreateSurface(202));
            buffer.TryAdd(CreateStatic(303, posX: 3f));
            int countBefore = buffer.Count;

            var changed = new[] { CreateStatic(101, posX: 99f) };
            buffer.ApplyStaticMeshDelta(changed, System.ReadOnlySpan<int>.Empty);

            Assert.That(buffer.Count, Is.EqualTo(countBefore));
            Dictionary<int, PrimitiveDrawItem> byId = IndexById(buffer);
            Assert.That(byId[101].Position.X, Is.EqualTo(99f).Within(0.001f));
            Assert.That(byId[303].Position.X, Is.EqualTo(3f).Within(0.001f));
            Assert.That(byId.ContainsKey(202), Is.True, "Non-static surface item must be preserved.");
        }

        [Test]
        public void ApplyStaticMeshDelta_RemovesStatic_AndAppendsNewStatic()
        {
            var buffer = new PrimitiveDrawBuffer(64);
            buffer.TryAdd(CreateStatic(101, posX: 1f));
            buffer.TryAdd(CreateStatic(202, posX: 2f));

            var changed = new[] { CreateStatic(303, posX: 7f) };
            var removed = new[] { 101 };
            buffer.ApplyStaticMeshDelta(changed, removed);

            Dictionary<int, PrimitiveDrawItem> byId = IndexById(buffer);
            Assert.That(byId.ContainsKey(101), Is.False, "Removed static must be gone.");
            Assert.That(byId.ContainsKey(202), Is.True);
            Assert.That(byId[303].Position.X, Is.EqualTo(7f).Within(0.001f));
            Assert.That(buffer.Count, Is.EqualTo(2));
            Assert.That(buffer.StaticMeshLaneItemCount, Is.EqualTo(2));
        }

        private static Dictionary<int, PrimitiveDrawItem> IndexById(PrimitiveDrawBuffer buffer)
        {
            var map = new Dictionary<int, PrimitiveDrawItem>();
            System.ReadOnlySpan<PrimitiveDrawItem> span = buffer.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                map[span[i].StableId] = span[i];
            }

            return map;
        }

        private static PrimitiveDrawItem CreateStatic(int stableId, float posX)
        {
            return new PrimitiveDrawItem
            {
                MeshAssetId = 10,
                Position = new Vector3(posX, 0f, 0f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
                Color = new Vector4(1f, 1f, 1f, 1f),
                StableId = stableId,
                MaterialId = 1,
                TemplateId = 1000 + stableId,
                RenderPath = VisualRenderPath.InstancedStaticMesh,
                Mobility = VisualMobility.Static,
                Flags = VisualRuntimeFlags.Visible,
                Visibility = VisualVisibility.Visible,
            };
        }

        private static PrimitiveDrawItem CreateSurface(int stableId)
        {
            return new PrimitiveDrawItem
            {
                MeshAssetId = 20,
                Position = new Vector3(5f, 0f, 0f),
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
                Color = new Vector4(1f, 1f, 1f, 1f),
                StableId = stableId,
                MaterialId = 2,
                TemplateId = 5000 + stableId,
                RenderPath = VisualRenderPath.StaticMesh,
                AssetKind = AssetKind.Surface,
                Mobility = VisualMobility.Static,
                Flags = VisualRuntimeFlags.Visible,
                Visibility = VisualVisibility.Visible,
            };
        }
    }
}
