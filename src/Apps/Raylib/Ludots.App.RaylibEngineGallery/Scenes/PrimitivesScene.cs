using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery.Scenes
{
    /// <summary>
    /// 图元群体渲染：RaylibPrimitiveRenderer 直接模式消费纯数据 PrimitiveDrawItem——
    /// 彩色图元阵随时间波动，坦克/人形原型走 AnimatorPackedState 驱动的 locomotion/aim 通道动效。
    /// </summary>
    public sealed class PrimitivesScene : IEngineScene
    {
        private readonly GalleryMeshAssets _meshes = new();
        private readonly GalleryPrimitiveSnapshot _snapshot = new();

        private RaylibPrimitiveRenderer _primitives = null!;
        private RaylibFrameLighting _lighting = null!;
        private bool _disposed;

        public string Id => "primitives";
        public string Title => "图元群体渲染";
        public string Summary => "RaylibPrimitiveRenderer 纯数据图元阵 + 原型动效";

        public void Load()
        {
            _meshes.Register("gallery.cube", MeshAssetDescriptor.Primitive(101, PrimitiveMeshKind.Cube));
            _meshes.Register("gallery.sphere", MeshAssetDescriptor.Primitive(102, PrimitiveMeshKind.Sphere));
            _lighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: 0.55f);
            _primitives = new RaylibPrimitiveRenderer(
                RaylibPrimitiveRenderMode.Immediate,
                vfs: null,
                materials: null,
                channelRegistrar: GalleryAnimationChannels.Register);
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera)
        {
            GalleryCamera.EnforceDistance(ref camera, 64f);
            float t = (float)totalTimeSeconds;

            _lighting.SetDayPhase(0.55f);

            Rl.ClearBackground(new Color(13, 15, 22, 255));
            Rl.BeginMode3D(camera);
            Rl.DrawGrid(36, 3f);

            _primitives.ApplyFrameLighting(_lighting, camera.position);
            _snapshot.BeginFrame();

            AppendColorField(t);
            AppendPrototypeSquad(t);

            _primitives.Draw(_snapshot, camera, _meshes, timeSeconds: totalTimeSeconds);
            Rl.EndMode3D();

            Rl.DrawText($"primitive visuals {_primitives.LastMeshVisualCount}", 12, 28, 20, GalleryColors.RayWhite);
        }

        private void AppendColorField(float t)
        {
            for (int i = 0; i < 48; i++)
            {
                int x = i % 12;
                int z = i / 12;
                float wave = MathF.Sin((x * 0.55f) + (z * 0.8f) + (t * 1.8f));
                float hue = (x / 12f * 0.6f) + (z / 4f * 0.4f);
                Vector4 tint = new(0.35f + hue, 0.85f - (hue * 0.5f), 0.95f - hue, 1f);
                Vector3 position = new(
                    -28f + (x * 4.4f),
                    1.1f + (wave * 1.3f),
                    -24f + (z * 5.2f));
                _snapshot.Add(GalleryItems.Mesh(
                    ((x + z) & 1) == 0 ? 101 : 102,
                    1000 + i,
                    position,
                    new Vector3(1.7f, 1.7f + (wave * 0.6f), 1.7f),
                    tint));
            }
        }

        private void AppendPrototypeSquad(float t)
        {
            for (int i = 0; i < 3; i++)
            {
                var animator = AnimatorPackedState.Create(controllerId: 1);
                animator.SetPrimaryStateIndex(0);
                animator.SetNormalizedTime01((t * 0.35f + (i / 3f)) % 1f);
                animator.SetFlags(AnimatorPackedStateFlags.Active | AnimatorPackedStateFlags.Looping);

                var overlay = new AnimationOverlayRequest
                {
                    BaseClip = AnimationChannelState.Create(GalleryAnimationChannels.Locomotion, (t * 0.35f + (i / 3f)) % 1f, 1f),
                    LayerClip = AnimationChannelState.Create(
                        GalleryAnimationChannels.AimYaw,
                        0.5f,
                        weight01: 0.65f,
                        scalar0: MathF.Sin(t * 0.8f + i) * 0.9f),
                };

                _snapshot.Add(new PrimitiveDrawItem
                {
                    MeshAssetId = 101,
                    StableId = 2000 + i,
                    Position = new Vector3(-14f + (i * 3.4f), 0f, 14f),
                    Rotation = Quaternion.Identity,
                    Scale = new Vector3(3.4f),
                    Color = new Vector4(0.62f, 0.70f, 0.82f, 1f),
                    RenderPath = VisualRenderPath.SkinnedMesh,
                    AssetKind = AssetKind.SkinnedMesh,
                    Mobility = VisualMobility.Movable,
                    Visibility = VisualVisibility.Visible,
                    Animator = animator,
                    AnimationOverlay = overlay,
                });

                var humanoidAnimator = AnimatorPackedState.Create(controllerId: 1);
                humanoidAnimator.SetPrimaryStateIndex(0);
                humanoidAnimator.SetNormalizedTime01((t * 0.5f + (i * 0.33f)) % 1f);
                humanoidAnimator.SetFlags(AnimatorPackedStateFlags.Active | AnimatorPackedStateFlags.Looping);
                _snapshot.Add(new PrimitiveDrawItem
                {
                    MeshAssetId = 102,
                    StableId = 2100 + i,
                    Position = new Vector3(12f + (i * 2.8f), 0f, 15f - (i * 2.6f)),
                    Rotation = Quaternion.Identity,
                    Scale = new Vector3(2.6f),
                    Color = new Vector4(0.88f, 0.62f, 0.40f, 1f),
                    RenderPath = VisualRenderPath.SkinnedMesh,
                    AssetKind = AssetKind.SkinnedMesh,
                    Mobility = VisualMobility.Movable,
                    Visibility = VisualVisibility.Visible,
                    Animator = humanoidAnimator,
                    AnimationOverlay = new AnimationOverlayRequest
                    {
                        BaseClip = AnimationChannelState.Create(GalleryAnimationChannels.Locomotion, (t * 0.5f + (i * 0.33f)) % 1f, 1f),
                    },
                });
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _primitives?.Dispose();
            _disposed = true;
        }
    }
}
