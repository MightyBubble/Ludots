using System;
using System.Numerics;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;

namespace Ludots.Client.Raylib.Rendering
{
    /// <summary>
    /// IBoneTransformProvider 的 raylib 实现：骨骼挂点（Attachment target=Bone）的 CPU 侧世界变换求值。
    /// boneId 语义 = 蒙皮模型骨骼数组索引（BoneInfo 顺序，与 GLTF skin joints 一致）。
    /// 求值链：父 presenter stableId → 定义里的 SkinnedMesh 资产槽组合出 visual stableId →
    /// SkinnedVisualBatchBuffer 里取该父 presenter 已发射的蒙皮可视项（与渲染器同源，含 meshAssetId、
    /// 世界 TRS、AnimatorPackedState）→ clip/frame 解析 → framePoses[frame][bone]。
    /// raylib 5.5 的 GLTF loader 装载时已用 BuildPoseFromParentJoints 把父链乘进 framePoses，
    /// 即 framePoses 条目本身是模型空间 pose，此处不再沿 BoneInfo.parent 链乘。
    /// </summary>
    public sealed unsafe class RaylibBoneTransformProvider : IBoneTransformProvider
    {
        private readonly SkinnedVisualBatchBuffer _skinnedBatch;
        private readonly PresenterDefinitionRegistry _definitions;
        private readonly IRenderMeshAssets _meshAssets;
        private readonly EntryResolver _resolveEntry;
        private readonly TryEntryResolver? _tryResolveEntry;

        /// <summary>
        /// 蒙皮模型条目解析（meshAssetId+descriptor → Model/Animations）。
        /// 生产接线为 RaylibGpuSkinnedModelCache.GetOrLoad；无 GL 环境的装配测试可接
        /// LoadModelAnimations 直载的纯 CPU 源——provider 只读 Animations/AnimCount，不触碰 Model。
        /// </summary>
        public delegate RaylibGpuSkinnedModelCache.Entry EntryResolver(int meshAssetId, MeshAssetDescriptor descriptor);

        public delegate RaylibGpuSkinnedModelAcquireOutcome TryEntryResolver(
            int meshAssetId,
            MeshAssetDescriptor descriptor,
            out RaylibGpuSkinnedModelCache.Entry entry,
            out string? status);

        public RaylibBoneTransformProvider(
            SkinnedVisualBatchBuffer skinnedBatch,
            PresenterDefinitionRegistry definitions,
            IRenderMeshAssets meshAssets,
            EntryResolver resolveEntry)
            : this(skinnedBatch, definitions, meshAssets, resolveEntry, null)
        {
        }

        public RaylibBoneTransformProvider(
            SkinnedVisualBatchBuffer skinnedBatch,
            PresenterDefinitionRegistry definitions,
            IRenderMeshAssets meshAssets,
            EntryResolver resolveEntry,
            TryEntryResolver? tryResolveEntry)
        {
            _skinnedBatch = skinnedBatch ?? throw new ArgumentNullException(nameof(skinnedBatch));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _meshAssets = meshAssets ?? throw new ArgumentNullException(nameof(meshAssets));
            _resolveEntry = resolveEntry ?? throw new ArgumentNullException(nameof(resolveEntry));
            _tryResolveEntry = tryResolveEntry;
        }

        public bool TryGetBoneWorldTransform(
            int presenterStableId,
            int boneId,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            position = Vector3.Zero;
            rotation = Quaternion.Identity;
            scale = Vector3.One;
            if (presenterStableId <= 0 || !TryFindSkinnedVisual(presenterStableId, out SkinnedVisualBatchItem item))
            {
                return false;
            }

            if (!_meshAssets.TryGetDescriptor(item.MeshAssetId, out MeshAssetDescriptor descriptor))
            {
                return false;
            }

            RaylibGpuSkinnedModelCache.Entry entry;
            if (_tryResolveEntry != null)
            {
                RaylibGpuSkinnedModelAcquireOutcome outcome = _tryResolveEntry(
                    item.MeshAssetId,
                    descriptor,
                    out entry,
                    out string? status);
                if (outcome == RaylibGpuSkinnedModelAcquireOutcome.InFlight)
                {
                    return false;
                }

                if (outcome == RaylibGpuSkinnedModelAcquireOutcome.Failed)
                {
                    string uris = descriptor.SourceUris == null
                        ? string.Empty
                        : string.Join("|", descriptor.SourceUris);
                    throw new InvalidOperationException(
                        $"{nameof(RaylibBoneTransformProvider)} meshAssetId={item.MeshAssetId} uris='{uris}' failed to resolve bone transform: {status ?? "backend reported Failed"}");
                }
            }
            else
            {
                entry = _resolveEntry(item.MeshAssetId, descriptor);
            }
            AnimatorPackedState animator = item.Animator;
            RaylibSkinnedPlayback.ResolveFromAnimator(
                in animator,
                entry.Animations,
                entry.AnimCount,
                stateToClipMap: null,
                out int clipIndex,
                out int frameIndex);

            ModelAnimation animation = entry.Animations[clipIndex];
            if ((uint)boneId >= (uint)animation.boneCount)
            {
                return false;
            }

            Transform bonePose = animation.framePoses[frameIndex][boneId];
            ComposeBoneWorldTransform(
                item.Position,
                item.Rotation,
                item.Scale,
                bonePose.translation,
                ToQuaternion(bonePose.rotation),
                bonePose.scale,
                out position,
                out rotation,
                out scale);
            return true;
        }

        /// <summary>
        /// presenter stableId → 本帧蒙皮可视项。SkinnedVisualBatchItem 携带的是组合 visual stableId
        /// （ComposeVisualStableId(presenterStableId, slotIndex, assetKind, defId)），此处用同一组合函数
        /// 对定义的 SkinnedMesh 资产槽枚举出候选 visual stableId 后在缓冲区内匹配。
        /// </summary>
        private bool TryFindSkinnedVisual(int presenterStableId, out SkinnedVisualBatchItem item)
        {
            IReadOnlyList<int> registeredIds = _definitions.RegisteredIds;
            for (int i = 0; i < registeredIds.Count; i++)
            {
                int definitionId = registeredIds[i];
                if (!_definitions.TryGet(definitionId, out PresenterDefinition definition))
                {
                    continue;
                }

                BehaviorSlot[] behaviors = definition.Behaviors;
                for (int j = 0; j < behaviors.Length; j++)
                {
                    ref readonly BehaviorSlot slot = ref behaviors[j];
                    if (slot.Kind != BehaviorKind.AssetBinding ||
                        slot.AssetBinding.AssetKind != AssetKind.SkinnedMesh)
                    {
                        continue;
                    }

                    int visualStableId = PresenterBehaviorRuntimeUtility.ComposeVisualStableId(
                        presenterStableId, slot.SlotIndex, AssetKind.SkinnedMesh, definitionId);
                    if (TryFindSkinnedVisualByStableId(visualStableId, out item))
                    {
                        return true;
                    }
                }
            }

            item = default;
            return false;
        }

        private bool TryFindSkinnedVisualByStableId(int visualStableId, out SkinnedVisualBatchItem item)
        {
            ReadOnlySpan<SkinnedVisualBatchItem> span = _skinnedBatch.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].StableId == visualStableId && span[i].RenderPath.IsSkinnedLane())
                {
                    item = span[i];
                    return true;
                }
            }

            item = default;
            return false;
        }

        /// <summary>
        /// 骨骼模型空间 pose ∘ presenter 世界 TRS → 世界变换分解。
        /// 与 GPU 蒙皮实例矩阵同构：boneTRS * presenterTRS（System.Numerics 行向量约定，先骨骼后父）。
        /// </summary>
        public static void ComposeBoneWorldTransform(
            in Vector3 presenterPosition,
            Quaternion presenterRotation,
            in Vector3 presenterScale,
            in Vector3 boneTranslation,
            Quaternion boneRotation,
            in Vector3 boneScale,
            out Vector3 worldPosition,
            out Quaternion worldRotation,
            out Vector3 worldScale)
        {
            Matrix4x4 world =
                Matrix4x4.CreateScale(boneScale) *
                Matrix4x4.CreateFromQuaternion(VisualMath.NormalizeOrIdentity(boneRotation)) *
                Matrix4x4.CreateTranslation(boneTranslation) *
                Matrix4x4.CreateScale(VisualMath.NormalizeScale(presenterScale)) *
                Matrix4x4.CreateFromQuaternion(VisualMath.NormalizeOrIdentity(presenterRotation)) *
                Matrix4x4.CreateTranslation(presenterPosition);

            worldPosition = world.Translation;
            float sx = MathF.Sqrt((world.M11 * world.M11) + (world.M12 * world.M12) + (world.M13 * world.M13));
            float sy = MathF.Sqrt((world.M21 * world.M21) + (world.M22 * world.M22) + (world.M23 * world.M23));
            float sz = MathF.Sqrt((world.M31 * world.M31) + (world.M32 * world.M32) + (world.M33 * world.M33));
            if (!float.IsFinite(sx) || !float.IsFinite(sy) || !float.IsFinite(sz) ||
                sx <= float.Epsilon || sy <= float.Epsilon || sz <= float.Epsilon)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibBoneTransformProvider)} composed bone world matrix has degenerate scale " +
                    $"({sx}, {sy}, {sz}); bone attachment cannot derive a world transform.");
            }

            worldScale = new Vector3(sx, sy, sz);
            worldRotation = Quaternion.CreateFromRotationMatrix(new Matrix4x4(
                world.M11 / sx, world.M12 / sx, world.M13 / sx, 0f,
                world.M21 / sy, world.M22 / sy, world.M23 / sy, 0f,
                world.M31 / sz, world.M32 / sz, world.M33 / sz, 0f,
                0f, 0f, 0f, 1f));
        }

        private static Quaternion ToQuaternion(System.Numerics.Vector4 raylibRotation)
        {
            return new Quaternion(raylibRotation.X, raylibRotation.Y, raylibRotation.Z, raylibRotation.W);
        }
    }
}
