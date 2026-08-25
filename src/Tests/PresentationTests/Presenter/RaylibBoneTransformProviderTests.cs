using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Diagnostics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Ludots.Tests.TestCommon;
using NUnit.Framework;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// #1153：IBoneTransformProvider 的 raylib 实现契约——CPU 侧骨骼世界变换求值 + Attachment(Bone) 挂点打通。
    /// 纯矩阵合成走无 native 单测；Knight.glb 走 LoadModelAnimations 直载（纯 CPU，无 GL 上下文）。
    /// </summary>
    [TestFixture]
    public sealed unsafe class RaylibBoneTransformProviderTests
    {
        private RecordingLogBackend _log = null!;
        private ModelAnimation* _animations;
        private int _animCount;

        [SetUp]
        public void SetUp()
        {
            _log = new RecordingLogBackend();
            Log.Initialize(_log);
        }

        [TearDown]
        public void TearDown()
        {
            if (_animations != null && _animCount > 0)
            {
                Rl.UnloadModelAnimations(_animations, _animCount);
                _animations = null;
                _animCount = 0;
            }

            Log.Initialize(NullLogBackend.Instance);
            _log.Dispose();
        }

        [Test]
        public void ComposeBoneWorldTransform_IdentityPresenter_PassesBonePoseThrough()
        {
            RaylibBoneTransformProvider.ComposeBoneWorldTransform(
                Vector3.Zero, Quaternion.Identity, Vector3.One,
                new Vector3(1f, 2f, 3f), Quaternion.Identity, Vector3.One,
                out Vector3 position, out Quaternion rotation, out Vector3 scale);

            Assert.That(position, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(rotation, Is.EqualTo(Quaternion.Identity));
            Assert.That(scale, Is.EqualTo(Vector3.One));
        }

        [Test]
        public void ComposeBoneWorldTransform_PresenterTrs_WrapsBoneModelPose()
        {
            Quaternion yaw90 = Quaternion.CreateFromYawPitchRoll(MathF.PI / 2f, 0f, 0f);

            RaylibBoneTransformProvider.ComposeBoneWorldTransform(
                new Vector3(5f, 0f, 0f), yaw90, new Vector3(2f, 2f, 2f),
                new Vector3(1f, 0f, 0f), Quaternion.Identity, new Vector3(1f, 1f, 1f),
                out Vector3 position, out Quaternion rotation, out Vector3 scale);

            // presenter 缩放 2：骨骼模型空间 (1,0,0) → (2,0,0)，再被 yaw90 旋转到 -Z，最后平移 (5,0,0)。
            Assert.That(position.X, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(position.Y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(position.Z, Is.EqualTo(-2f).Within(0.0001f));
            Assert.That(scale.X, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(scale.Y, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(scale.Z, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(Quaternion.Dot(rotation, yaw90), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void ComposeBoneWorldTransform_NonFiniteBoneScale_FailsLoud()
        {
            // presenter 侧零缩放走 VisualMath.NormalizeScale 的既有语义归一；能到达守卫的是骨骼 pose 的非有限缩放。
            Assert.That(() => RaylibBoneTransformProvider.ComposeBoneWorldTransform(
                Vector3.Zero, Quaternion.Identity, Vector3.One,
                Vector3.One, Quaternion.Identity, new Vector3(float.NaN, 1f, 1f),
                out _, out _, out _), Throws.InvalidOperationException);
        }

        [Test]
        public void TryGetBoneWorldTransform_UnknownPresenterStableId_ReturnsFalse()
        {
            using var world = World.Create();
            var provider = CreateProvider(world, definitions: new PresenterDefinitionRegistry(), entryResolver: null!);

            bool resolved = provider.TryGetBoneWorldTransform(
                9901, 1, out Vector3 position, out Quaternion rotation, out Vector3 scale);

            Assert.That(resolved, Is.False);
            Assert.That(position, Is.EqualTo(Vector3.Zero));
            Assert.That(rotation, Is.EqualTo(Quaternion.Identity));
            Assert.That(scale, Is.EqualTo(Vector3.One));
        }

        [Test]
        public void Attachment_FollowsKnightBoneAcrossAnimationFrames()
        {
            LoadKnightAnimations();
            Assert.That(_animCount, Is.GreaterThan(0), "Knight.glb 必须携带动画（GpuSkinnedInstance 合同）");

            if (!TryFindMovingBone(out int clipIndex, out int boneId))
            {
                Assert.Fail("Knight.glb 所有 clip 首末帧无骨骼位移，无法验证挂点跟随动画帧移动。");
                return;
            }

            Assert.That(boneId, Is.GreaterThan(0), "消费端合同要求 boneId>0，选中的验证骨骼必须满足。");

            using var world = World.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            Entity owner = world.Create();

            var parentDefinition = new PresenterDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.SkinnedMesh,
                            RenderPath = VisualRenderPath.GpuSkinnedInstance,
                        },
                    },
                ],
            };
            int parentDefId = definitions.Register("boneprovider.knight.parent", parentDefinition);
            var childDefinition = new PresenterDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.Attachment,
                        ActiveByDefault = true,
                        Attachment = new AttachmentConfig
                        {
                            Target = AttachmentTarget.Bone,
                            BoneId = boneId,
                            Offset = Vector3.Zero,
                            RotationOffset = Quaternion.Identity,
                            InheritScale = false,
                        },
                    },
                ],
            };
            int childDefId = definitions.Register("boneprovider.knight.child", childDefinition);

            const int ParentStableId = 7300;
            Entity parentPresenter = instances.Create(parentDefId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, ParentStableId, Entity.Null, parentDefinition);
            Entity childPresenter = instances.Create(childDefId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7301, parentPresenter, childDefinition);
            world.Get<PresenterState>(childPresenter).BehaviorActiveMask = 1u;

            var skinnedBatch = new SkinnedVisualBatchBuffer(8);
            var meshAssets = new MeshAssetRegistry();
            int meshAssetId = meshAssets.Register("boneprovider.knight.mesh", MeshAssetDescriptor.Model(0));
            var provider = new RaylibBoneTransformProvider(
                skinnedBatch,
                definitions,
                meshAssets,
                (_, _) => new RaylibGpuSkinnedModelCache.Entry(default, _animations, _animCount, string.Empty, loaded: true));
            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer(),
                heightmap: null,
                boneTransformProvider: provider);

            var presenterRotation = Quaternion.CreateFromYawPitchRoll(MathF.PI / 2f, 0f, 0f);
            var presenterScale = new Vector3(2f, 2f, 2f);
            var presenterPosition = new Vector3(5f, 0f, 0f);
            int visualStableId = PresenterBehaviorRuntimeUtility.ComposeVisualStableId(ParentStableId, 0, AssetKind.SkinnedMesh, parentDefId);
            ModelAnimation animation = _animations[clipIndex];
            int lastFrame = animation.frameCount - 1;

            // 帧 0：t=0 → frameIndex 0。
            SubmitSkinnedItem(skinnedBatch, visualStableId, meshAssetId, clipIndex, normalizedTime01: 0f, presenterPosition, presenterRotation, presenterScale);
            system.Update(0.016f);
            Vector3 expectedFrame0 = ComposeExpectedWorld(animation, 0, boneId, presenterPosition, presenterRotation, presenterScale);
            Vector3 childAtFrame0 = world.Get<PresenterWorldPosition>(childPresenter).Value;
            Assert.That(world.Get<PresenterTransformSource>(childPresenter).Value, Is.EqualTo(TransformSource.BoneAttached));
            Assert.That(childAtFrame0.X, Is.EqualTo(expectedFrame0.X).Within(0.0001f));
            Assert.That(childAtFrame0.Y, Is.EqualTo(expectedFrame0.Y).Within(0.0001f));
            Assert.That(childAtFrame0.Z, Is.EqualTo(expectedFrame0.Z).Within(0.0001f));

            // 末帧：t=1 → frameIndex frameCount-1，挂点必须随动画帧移动。
            SubmitSkinnedItem(skinnedBatch, visualStableId, meshAssetId, clipIndex, normalizedTime01: 1f, presenterPosition, presenterRotation, presenterScale);
            system.Update(0.016f);
            Vector3 expectedLast = ComposeExpectedWorld(animation, lastFrame, boneId, presenterPosition, presenterRotation, presenterScale);
            Vector3 childAtLast = world.Get<PresenterWorldPosition>(childPresenter).Value;
            Assert.That(childAtLast.X, Is.EqualTo(expectedLast.X).Within(0.0001f));
            Assert.That(childAtLast.Y, Is.EqualTo(expectedLast.Y).Within(0.0001f));
            Assert.That(childAtLast.Z, Is.EqualTo(expectedLast.Z).Within(0.0001f));
            Assert.That(Vector3.Distance(childAtFrame0, childAtLast), Is.GreaterThan(0.001f), "挂点位置必须随动画帧移动");
        }

        [Test]
        public void Attachment_WithoutProvider_WarnsOnceAndSkipsBoneSubstitution()
        {
            using var world = World.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            Entity owner = world.Create();

            var definition = new PresenterDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.Attachment,
                        ActiveByDefault = true,
                        Attachment = new AttachmentConfig
                        {
                            Target = AttachmentTarget.Bone,
                            BoneId = 1,
                            Offset = Vector3.Zero,
                            RotationOffset = Quaternion.Identity,
                            InheritScale = false,
                        },
                    },
                ],
            };
            int defId = definitions.Register("boneprovider.warn", definition);
            Entity parentPresenter = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7400, Entity.Null, definition);
            Entity childPresenter = instances.Create(defId, owner, 0, PresentationAnchorKind.Entity, Vector3.Zero, 7401, parentPresenter, definition);
            world.Get<PresenterState>(childPresenter).BehaviorActiveMask = 1u;
            world.Get<PresenterState>(parentPresenter).StableId = 7400;

            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer(),
                heightmap: null,
                boneTransformProvider: null);

            system.Update(0.016f);
            system.Update(0.016f);

            Assert.That(world.Has<PresenterTransformSource>(childPresenter) == false ||
                world.Get<PresenterTransformSource>(childPresenter).Value != TransformSource.BoneAttached,
                Is.True, "无 provider 时不得做骨骼挂点替换");
            Assert.That(_log.Warnings.Count, Is.EqualTo(1), "warn 只应发生一次（去重）");
            Assert.That(_log.Warnings[0], Does.Contain("bone provider is not registered"));
            Assert.That(_log.Warnings[0], Does.Contain("Parent-position substitution is not applied"));
        }

        private RaylibBoneTransformProvider CreateProvider(
            World world,
            PresenterDefinitionRegistry definitions,
            SkinnedVisualBatchBuffer? skinnedBatch = null,
            RaylibBoneTransformProvider.EntryResolver? entryResolver = null)
        {
            _ = world;
            var meshAssets = new MeshAssetRegistry();
            return new RaylibBoneTransformProvider(
                skinnedBatch ?? new SkinnedVisualBatchBuffer(8),
                definitions,
                meshAssets,
                entryResolver ?? ((_, _) => throw new InvalidOperationException("测试 entry 源不应被触碰。")));
        }

        private void SubmitSkinnedItem(
            SkinnedVisualBatchBuffer skinnedBatch,
            int visualStableId,
            int meshAssetId,
            int clipIndex,
            float normalizedTime01,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale)
        {
            skinnedBatch.Clear();
            var animator = AnimatorPackedState.Create(1);
            animator.SetPrimaryStateIndex(clipIndex);
            animator.SetNormalizedTime01(normalizedTime01);
            Assert.That(skinnedBatch.TryAdd(new SkinnedVisualBatchItem
            {
                StableId = visualStableId,
                MeshAssetId = meshAssetId,
                RenderPath = VisualRenderPath.GpuSkinnedInstance,
                AssetKind = AssetKind.SkinnedMesh,
                Animator = animator,
                Position = position,
                Rotation = rotation,
                Scale = scale,
            }), Is.True, "测试缓冲必须有容量容纳蒙皮可视项");
        }

        /// <summary>独立于 provider 的期望值合成：直接用 System.Numerics 矩阵链构造骨骼世界变换。</summary>
        private static Vector3 ComposeExpectedWorld(
            in ModelAnimation animation,
            int frameIndex,
            int boneId,
            Vector3 presenterPosition,
            Quaternion presenterRotation,
            Vector3 presenterScale)
        {
            Transform pose = animation.framePoses[frameIndex][boneId];
            var boneRotation = new Quaternion(pose.rotation.X, pose.rotation.Y, pose.rotation.Z, pose.rotation.W);
            Matrix4x4 world =
                Matrix4x4.CreateScale(pose.scale) *
                Matrix4x4.CreateFromQuaternion(boneRotation) *
                Matrix4x4.CreateTranslation(pose.translation) *
                Matrix4x4.CreateScale(presenterScale) *
                Matrix4x4.CreateFromQuaternion(presenterRotation) *
                Matrix4x4.CreateTranslation(presenterPosition);
            return world.Translation;
        }

        private void LoadKnightAnimations()
        {
            string repoRoot = FindRepoRoot();
            string knight = Path.Combine(repoRoot, "mods", "fixtures", "raylib_platform_meshes", "RaylibPlatformMeshesMod", "assets", "Models", "Knight.glb");
            Assert.That(File.Exists(knight), Is.True, $"Knight.glb 不存在：{knight}");
            _animations = Rl.LoadModelAnimations(knight, out _animCount);
            Assert.That(_animations != null, Is.True);
            Assert.That(_animCount, Is.GreaterThan(0));
        }

        private bool TryFindMovingBone(out int clipIndex, out int boneId)
        {
            for (int c = 0; c < _animCount; c++)
            {
                ModelAnimation animation = _animations[c];
                if (animation.frameCount < 2 || animation.boneCount <= 1)
                {
                    continue;
                }

                Transform* first = animation.framePoses[0];
                Transform* last = animation.framePoses[animation.frameCount - 1];
                for (int b = 1; b < animation.boneCount; b++)
                {
                    if (Vector3.Distance(first[b].translation, last[b].translation) > 0.0001f)
                    {
                        clipIndex = c;
                        boneId = b;
                        return true;
                    }
                }
            }

            clipIndex = -1;
            boneId = -1;
            return false;
        }

        private static string FindRepoRoot()
        {
            string current = TestContext.CurrentContext.WorkDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "mods")) &&
                    File.Exists(Path.Combine(current, "AGENTS.md")))
                {
                    return current;
                }

                current = Path.GetDirectoryName(current) ?? string.Empty;
            }

            throw new InvalidOperationException("无法从测试工作目录定位仓库根（mods + AGENTS.md）。");
        }

        private sealed class RecordingLogBackend : ILogBackend, IDisposable
        {
            public readonly List<string> Warnings = new();

            public void Write(LogLevel level, in LogChannel channel, string message)
            {
                if (level == LogLevel.Warning)
                {
                    Warnings.Add(message);
                }
            }

            public void Flush() { }

            public void Dispose() { }
        }
    }
}
