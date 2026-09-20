using System.Numerics;
using Ludots.Raylib.Render;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;

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
        public void Sync_IgnoresMovableInstancedMeshes()
        {
            var planner = new StaticMeshAdapterSyncPlanner();

            planner.Sync(new[]
            {
                CreateItem(
                    404,
                    VisualRenderPath.InstancedStaticMesh,
                    meshAssetId: 14,
                    materialId: 4,
                    posX: 3f,
                    mobility: VisualMobility.Movable),
            });

            Assert.That(planner.ActiveBindings.Count, Is.EqualTo(0));
            Assert.That(planner.Operations, Is.Empty);
            Assert.That(planner.TryGetBinding(404, out _), Is.False);
        }

        [Test]
        public void Sync_IgnoresSurfaceAssetKind_EvenWhenRenderPathLooksStatic()
        {
            var planner = new StaticMeshAdapterSyncPlanner();

            planner.Sync(new[]
            {
                CreateItem(
                    505,
                    VisualRenderPath.StaticMesh,
                    meshAssetId: 15,
                    materialId: 5,
                    assetKind: AssetKind.Surface),
            });

            Assert.That(planner.ActiveBindings.Count, Is.EqualTo(0));
            Assert.That(planner.Operations, Is.Empty);
            Assert.That(planner.TryGetBinding(505, out _), Is.False);
        }

        [Test]
        public void Sync_IgnoresVfxAssetKind_EvenWhenRenderPathLooksStatic()
        {
            var planner = new StaticMeshAdapterSyncPlanner();

            planner.Sync(new[]
            {
                CreateItem(
                    606,
                    VisualRenderPath.StaticMesh,
                    meshAssetId: 16,
                    materialId: 6,
                    assetKind: AssetKind.VFX),
            });

            Assert.That(planner.ActiveBindings.Count, Is.EqualTo(0));
            Assert.That(planner.Operations, Is.Empty);
            Assert.That(planner.TryGetBinding(606, out _), Is.False);
        }

        [Test]
        public void Sync_CustomDataChange_EmitsUpdate_WithoutChangingLaneSlot()
        {
            var planner = new StaticMeshAdapterSyncPlanner();
            var initialCustomData = new MaterialCustomDataPayload { Count = 1 };
            initialCustomData.SetSlot(0, new Vector4(1f, 0f, 0f, 0f));
            var updatedCustomData = new MaterialCustomDataPayload { Count = 1 };
            updatedCustomData.SetSlot(0, new Vector4(2f, 0f, 0f, 0f));

            planner.Sync(new[]
            {
                CreateItem(
                    606,
                    VisualRenderPath.InstancedStaticMesh,
                    meshAssetId: 16,
                    materialId: 6,
                    customData: initialCustomData),
            });
            Assert.That(planner.TryGetBinding(606, out var original), Is.True);

            planner.Sync(new[]
            {
                CreateItem(
                    606,
                    VisualRenderPath.InstancedStaticMesh,
                    meshAssetId: 16,
                    materialId: 6,
                    customData: updatedCustomData),
            });

            Assert.That(planner.Operations.Count, Is.EqualTo(1));
            Assert.That(planner.Operations[0].Kind, Is.EqualTo(StaticMeshAdapterSyncOpKind.Update));
            Assert.That(planner.TryGetBinding(606, out var updated), Is.True);
            Assert.That(updated.Slot, Is.EqualTo(original.Slot));
            Assert.That(updated.Generation, Is.EqualTo(original.Generation));
            Assert.That(updated.Item.MaterialCustomData.GetSlot(0).X, Is.EqualTo(2f).Within(0.001f));
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
        public void Sync_WhenProjectionGenerationChanges_EmitsFullResyncWithoutItemChanges()
        {
            var planner = new StaticMeshAdapterSyncPlanner();
            var first = CreateItem(101, VisualRenderPath.StaticMesh, meshAssetId: 10, materialId: 1, posX: 1f);
            var second = CreateItem(202, VisualRenderPath.InstancedStaticMesh, meshAssetId: 11, materialId: 2, posX: 5f);
            var snapshot = new[] { first, second };

            planner.Sync(snapshot, projectionGeneration: 1);
            Assert.That(planner.LastCreateCount, Is.EqualTo(2));
            Assert.That(planner.LastProjectionResyncCount, Is.EqualTo(0));

            planner.Sync(snapshot, projectionGeneration: 1);
            Assert.That(planner.Operations, Is.Empty);
            Assert.That(planner.LastProjectionResyncCount, Is.EqualTo(0));

            planner.Sync(snapshot, projectionGeneration: 2);

            Assert.That(planner.Operations.Count, Is.EqualTo(2));
            Assert.That(planner.LastCreateCount, Is.EqualTo(0));
            Assert.That(planner.LastUpdateCount, Is.EqualTo(0));
            Assert.That(planner.LastRemoveCount, Is.EqualTo(0));
            Assert.That(planner.LastProjectionResyncCount, Is.EqualTo(2));
            Assert.That(planner.Operations[0].Kind, Is.EqualTo(StaticMeshAdapterSyncOpKind.Resync));
            Assert.That(planner.Operations[1].Kind, Is.EqualTo(StaticMeshAdapterSyncOpKind.Resync));
            Assert.That(planner.TryGetBinding(101, out var firstBinding), Is.True);
            Assert.That(firstBinding.ProjectionGeneration, Is.EqualTo(2));
            Assert.That(planner.TryGetBinding(202, out var secondBinding), Is.True);
            Assert.That(secondBinding.ProjectionGeneration, Is.EqualTo(2));
        }

        [Test]
        public void SyncDeltas_WhenProjectionGenerationChanges_ResyncsChangedAndUnchangedBindingsOnce()
        {
            var planner = new StaticMeshAdapterSyncPlanner();
            var first = CreateItem(101, VisualRenderPath.StaticMesh, meshAssetId: 10, materialId: 1, posX: 1f);
            var second = CreateItem(202, VisualRenderPath.InstancedStaticMesh, meshAssetId: 11, materialId: 2, posX: 5f);
            planner.Sync(new[] { first, second }, projectionGeneration: 1);

            var changedFirst = CreateItem(101, VisualRenderPath.StaticMesh, meshAssetId: 10, materialId: 1, posX: 9f);
            planner.SyncDeltas(new[] { changedFirst }, System.Array.Empty<int>(), projectionGeneration: 2);

            Assert.That(planner.Operations.Count, Is.EqualTo(2));
            Assert.That(planner.LastUpdateCount, Is.EqualTo(0));
            Assert.That(planner.LastProjectionResyncCount, Is.EqualTo(2));
            Assert.That(planner.Operations[0].Kind, Is.EqualTo(StaticMeshAdapterSyncOpKind.Resync));
            Assert.That(planner.Operations[0].Binding.StableId, Is.EqualTo(101));
            Assert.That(planner.Operations[0].Binding.Item.Position.X, Is.EqualTo(9f).Within(0.001f));
            Assert.That(planner.Operations[1].Kind, Is.EqualTo(StaticMeshAdapterSyncOpKind.Resync));
            Assert.That(planner.Operations[1].Binding.StableId, Is.EqualTo(202));
            Assert.That(planner.TryGetBinding(101, out var firstBinding), Is.True);
            Assert.That(firstBinding.ProjectionGeneration, Is.EqualTo(2));
            Assert.That(planner.TryGetBinding(202, out var secondBinding), Is.True);
            Assert.That(secondBinding.ProjectionGeneration, Is.EqualTo(2));
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
        public void Sync_VisibleCulledVisible_KeepsBindingSlotWithoutRemoveCreate()
        {
            var planner = new StaticMeshAdapterSyncPlanner();
            var visible = CreateItem(111, VisualRenderPath.InstancedStaticMesh, meshAssetId: 10, materialId: 1);

            planner.Sync(new[] { visible });
            Assert.That(planner.TryGetBinding(111, out var original), Is.True);

            var culled = CreateItem(
                111,
                VisualRenderPath.InstancedStaticMesh,
                meshAssetId: 10,
                materialId: 1,
                visibility: VisualVisibility.Culled);

            planner.Sync(new[] { culled });
            Assert.That(planner.Operations.Count, Is.EqualTo(1));
            Assert.That(planner.Operations[0].Kind, Is.EqualTo(StaticMeshAdapterSyncOpKind.Update));
            Assert.That(planner.LastRemoveCount, Is.EqualTo(0));
            Assert.That(planner.LastCreateCount, Is.EqualTo(0));
            Assert.That(planner.TryGetBinding(111, out var culledBinding), Is.True);
            Assert.That(culledBinding.Slot, Is.EqualTo(original.Slot));
            Assert.That(culledBinding.Generation, Is.EqualTo(original.Generation));
            Assert.That(culledBinding.Item.Visibility, Is.EqualTo(VisualVisibility.Culled));

            planner.Sync(new[] { visible });
            Assert.That(planner.Operations.Count, Is.EqualTo(1));
            Assert.That(planner.Operations[0].Kind, Is.EqualTo(StaticMeshAdapterSyncOpKind.Update));
            Assert.That(planner.LastRemoveCount, Is.EqualTo(0));
            Assert.That(planner.LastCreateCount, Is.EqualTo(0));
            Assert.That(planner.TryGetBinding(111, out var visibleAgainBinding), Is.True);
            Assert.That(visibleAgainBinding.Slot, Is.EqualTo(original.Slot));
            Assert.That(visibleAgainBinding.Generation, Is.EqualTo(original.Generation));
            Assert.That(visibleAgainBinding.Item.Visibility, Is.EqualTo(VisualVisibility.Visible));
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
            VisualMobility mobility = VisualMobility.Static,
            AssetKind assetKind = default,
            MaterialCustomDataPayload customData = default)
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
                AssetKind = assetKind,
                MaterialCustomData = customData,
                Mobility = mobility,
                Flags = VisualRuntimeFlags.Visible,
                Animator = default,
                Visibility = visibility,
            };
        }
    }
}
