using System.Numerics;
using Ludots.Core.Presentation.AdapterSync;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class StaticMeshAdapterSyncPlannerTests
    {
        [Test]
        public void Sync_CreatesBindings_ForPersistentStaticLanes_Only()
        {
            var planner = new StaticMeshAdapterSyncPlanner();

            planner.Sync(new[]
            {
                CreateItem(101, VisualRenderPath.StaticMesh, meshAssetId: 10, materialId: 1, visibility: VisualVisibility.Visible),
                CreateItem(202, VisualRenderPath.InstancedStaticMesh, meshAssetId: 11, materialId: 2, visibility: VisualVisibility.Hidden),
                CreateItem(303, VisualRenderPath.SkinnedMesh, meshAssetId: 12, materialId: 3, visibility: VisualVisibility.Visible),
            });

            Assert.That(planner.ActiveBindings.Count, Is.EqualTo(2));
            Assert.That(planner.Operations.Count, Is.EqualTo(2));
            Assert.That(planner.LastCreateCount, Is.EqualTo(2));
            Assert.That(planner.LastUpdateCount, Is.EqualTo(0));
            Assert.That(planner.LastRemoveCount, Is.EqualTo(0));

            Assert.That(planner.TryGetBinding(101, out var staticBinding), Is.True);
            Assert.That(staticBinding.Lane.RenderPath, Is.EqualTo(VisualRenderPath.StaticMesh));
            Assert.That(staticBinding.Slot, Is.EqualTo(0));
            Assert.That(staticBinding.Generation, Is.EqualTo(1));
            Assert.That(staticBinding.Item.Visibility, Is.EqualTo(VisualVisibility.Visible));

            Assert.That(planner.TryGetBinding(202, out var instancedBinding), Is.True);
            Assert.That(instancedBinding.Lane.RenderPath, Is.EqualTo(VisualRenderPath.InstancedStaticMesh));
            Assert.That(instancedBinding.Item.Visibility, Is.EqualTo(VisualVisibility.Hidden));

            Assert.That(planner.TryGetBinding(303, out _), Is.False);
        }

        [Test]
        public void Sync_ReorderedSnapshot_DoesNotEmitDirtyOps()
        {
            var planner = new StaticMeshAdapterSyncPlanner();
            var first = CreateItem(101, VisualRenderPath.StaticMesh, meshAssetId: 10, materialId: 1, posX: 1f);
            var second = CreateItem(202, VisualRenderPath.StaticMesh, meshAssetId: 10, materialId: 1, posX: 5f);

            planner.Sync(new[] { first, second });
            Assert.That(planner.TryGetBinding(101, out var binding101Before), Is.True);
            Assert.That(planner.TryGetBinding(202, out var binding202Before), Is.True);

            planner.Sync(new[] { second, first });

            Assert.That(planner.Operations, Is.Empty);
            Assert.That(planner.LastCreateCount, Is.EqualTo(0));
            Assert.That(planner.LastUpdateCount, Is.EqualTo(0));
            Assert.That(planner.LastRemoveCount, Is.EqualTo(0));

            Assert.That(planner.TryGetBinding(101, out var binding101After), Is.True);
            Assert.That(planner.TryGetBinding(202, out var binding202After), Is.True);
            Assert.That(binding101After.Slot, Is.EqualTo(binding101Before.Slot));
            Assert.That(binding101After.Generation, Is.EqualTo(binding101Before.Generation));
            Assert.That(binding202After.Slot, Is.EqualTo(binding202Before.Slot));
            Assert.That(binding202After.Generation, Is.EqualTo(binding202Before.Generation));
        }

        [Test]
        public void Sync_VisibilityOrTransformChange_EmitsUpdate_WithoutReallocatingSlot()
        {
            var planner = new StaticMeshAdapterSyncPlanner();
            var visible = CreateItem(101, VisualRenderPath.StaticMesh, meshAssetId: 10, materialId: 1, posX: 1f);
            planner.Sync(new[] { visible });

            Assert.That(planner.TryGetBinding(101, out var original), Is.True);

            var hiddenMoved = CreateItem(
                101,
                VisualRenderPath.StaticMesh,
                meshAssetId: 10,
                materialId: 1,
                posX: 9f,
                visibility: VisualVisibility.Culled,
                rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.75f));

            planner.Sync(new[] { hiddenMoved });

            Assert.That(planner.Operations.Count, Is.EqualTo(1));
            Assert.That(planner.Operations[0].Kind, Is.EqualTo(StaticMeshAdapterSyncOpKind.Update));
            Assert.That(planner.TryGetBinding(101, out var updated), Is.True);
            Assert.That(updated.Slot, Is.EqualTo(original.Slot));
            Assert.That(updated.Generation, Is.EqualTo(original.Generation));
            Assert.That(updated.Item.Position.X, Is.EqualTo(9f).Within(0.001f));
            Assert.That(updated.Item.Visibility, Is.EqualTo(VisualVisibility.Culled));
        }

        [Test]
        public void Sync_RemoveAndReuse_RecyclesSlotWithIncrementedGeneration()
        {
            var planner = new StaticMeshAdapterSyncPlanner();
            var laneKey = new StaticMeshLaneKey(VisualRenderPath.StaticMesh, 10, 1, VisualMobility.Static);

            planner.Sync(new[]
            {
                CreateItem(101, laneKey.RenderPath, laneKey.MeshAssetId, laneKey.MaterialId, mobility: laneKey.Mobility),
                CreateItem(202, laneKey.RenderPath, laneKey.MeshAssetId, laneKey.MaterialId, mobility: laneKey.Mobility),
            });

            Assert.That(planner.TryGetBinding(101, out var removedCandidate), Is.True);
            Assert.That(removedCandidate.Slot, Is.EqualTo(0));
            Assert.That(removedCandidate.Generation, Is.EqualTo(1));

            planner.Sync(new[]
            {
                CreateItem(202, laneKey.RenderPath, laneKey.MeshAssetId, laneKey.MaterialId, mobility: laneKey.Mobility),
            });

            Assert.That(planner.Operations.Count, Is.EqualTo(1));
            Assert.That(planner.Operations[0].Kind, Is.EqualTo(StaticMeshAdapterSyncOpKind.Remove));
            Assert.That(planner.Operations[0].Binding.StableId, Is.EqualTo(101));

            planner.Sync(new[]
            {
                CreateItem(202, laneKey.RenderPath, laneKey.MeshAssetId, laneKey.MaterialId, mobility: laneKey.Mobility),
                CreateItem(303, laneKey.RenderPath, laneKey.MeshAssetId, laneKey.MaterialId, mobility: laneKey.Mobility),
            });

            Assert.That(planner.Operations.Count, Is.EqualTo(1));
            Assert.That(planner.Operations[0].Kind, Is.EqualTo(StaticMeshAdapterSyncOpKind.Create));
            Assert.That(planner.TryGetBinding(303, out var reused), Is.True);
            Assert.That(reused.Slot, Is.EqualTo(0));
            Assert.That(reused.Generation, Is.EqualTo(2));
        }

        [Test]
        public void Sync_WhenLaneKeyChanges_EmitsRemoveThenCreate()
        {
            var planner = new StaticMeshAdapterSyncPlanner();
            planner.Sync(new[]
            {
                CreateItem(101, VisualRenderPath.StaticMesh, meshAssetId: 10, materialId: 1),
            });

            planner.Sync(new[]
            {
                CreateItem(101, VisualRenderPath.HierarchicalInstancedStaticMesh, meshAssetId: 10, materialId: 2),
            });

            Assert.That(planner.Operations.Count, Is.EqualTo(2));
            Assert.That(planner.Operations[0].Kind, Is.EqualTo(StaticMeshAdapterSyncOpKind.Remove));
            Assert.That(planner.Operations[0].Binding.Lane.RenderPath, Is.EqualTo(VisualRenderPath.StaticMesh));
            Assert.That(planner.Operations[1].Kind, Is.EqualTo(StaticMeshAdapterSyncOpKind.Create));
            Assert.That(planner.Operations[1].Binding.Lane.RenderPath, Is.EqualTo(VisualRenderPath.HierarchicalInstancedStaticMesh));
            Assert.That(planner.TryGetBinding(101, out var binding), Is.True);
            Assert.That(binding.Lane.MaterialId, Is.EqualTo(2));
        }

        [Test]
        public void Sync_RejectsDuplicateOrInvalidStableIds()
        {
            var planner = new StaticMeshAdapterSyncPlanner();

            var duplicate = Assert.Throws<System.InvalidOperationException>(() => planner.Sync(new[]
            {
                CreateItem(101, VisualRenderPath.StaticMesh, meshAssetId: 10, materialId: 1),
                CreateItem(101, VisualRenderPath.StaticMesh, meshAssetId: 11, materialId: 2),
            }));
            Assert.That(duplicate!.Message, Does.Contain("duplicate"));

            var invalid = Assert.Throws<System.InvalidOperationException>(() => planner.Sync(new[]
            {
                CreateItem(0, VisualRenderPath.StaticMesh, meshAssetId: 10, materialId: 1),
            }));
            Assert.That(invalid!.Message, Does.Contain("positive PresentationStableId"));
        }

        private static PrimitiveDrawItem CreateItem(
            int stableId,
            VisualRenderPath renderPath,
            int meshAssetId,
            int materialId,
            float posX = 0f,
            VisualVisibility visibility = VisualVisibility.Visible,
            Quaternion rotation = default,
            VisualMobility mobility = VisualMobility.Static)
        {
            return new PrimitiveDrawItem
            {
                MeshAssetId = meshAssetId,
                Position = new Vector3(posX, 0f, 0f),
                Rotation = rotation == default ? Quaternion.Identity : rotation,
                Scale = Vector3.One,
                Color = new Vector4(1f, 1f, 1f, 1f),
                StableId = stableId,
                MaterialId = materialId,
                TemplateId = 1000 + stableId,
                RenderPath = renderPath,
                Mobility = mobility,
                Flags = VisualRuntimeFlags.Visible,
                Animator = default,
                Visibility = visibility,
            };
        }
    }
}
