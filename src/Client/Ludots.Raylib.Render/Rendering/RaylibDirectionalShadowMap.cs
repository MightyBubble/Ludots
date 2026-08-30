using System;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// 方向光 shadow map：深度打包进颜色 RT（RGBA 256 进位），接收端经 uLightSpaceMatrix
    /// （MAT4 uniform，native ≥5.5）投影 + 3x3 PCF。深度 pass 与接收端共用同一份手工
    /// lookAt/ortho 矩阵保证 NDC 深度严格一致；RT 由 shader 直接采样，不做屏幕绘制路径的 Y 翻转。
    /// </summary>
    public sealed unsafe class RaylibDirectionalShadowMap : IDisposable
    {
        public const int DefaultMapSize = 2048;
        public const float DefaultReceiverBiasWorld = 0.04f;

        private readonly RenderTexture2D _rt;
        private readonly Shader _depthShader;
        private readonly Shader _depthInstancedShader;
        private readonly Shader _depthSkinningPoseTextureShader;
        private readonly Shader _depthCutoutShader;
        private Material _depthMaterial;
        private Material _depthInstancedMaterial;
        private Material _depthSkinningPoseTextureMaterial;
        private Material _depthCutoutMaterial;
        private readonly int _locDepthSkinningInstanceBase;
        private readonly int _locDepthSkinningBoneBase;
        private readonly int _locCutoutAlphaCutoff;
        private RaylibMatrix _lightView;
        private RaylibMatrix _lightProjection;
        private float _depthRange;
        private bool _frameActive;
        private bool _disposed;

        public RaylibDirectionalShadowMap(RaylibShadowConfig? config = null)
        {
            RaylibShadowConfig effective = config?.Validate() ?? RaylibShadowConfig.CreateDefault();
            MapSize = effective.MapSize;
            ReceiverBiasWorld = effective.ReceiverBiasWorld;

            _rt = RaylibNativeResources.LoadRenderTexture(MapSize, MapSize);
            Rl.SetTextureFilter(_rt.texture, Rl.TextureFilter.TEXTURE_FILTER_POINT);
            Rl.SetTextureWrap(_rt.texture, Rl.TextureWrap.TEXTURE_WRAP_CLAMP);
            string baseDir = AppContext.BaseDirectory;
            string fsPath = System.IO.Path.Combine(baseDir, "shadow_depth.fs");
            _depthShader = RaylibNativeResources.LoadShader(System.IO.Path.Combine(baseDir, "shadow_depth.vs"), fsPath);
            if (_depthShader.id == 0)
            {
                throw new InvalidOperationException("Failed to load shadow_depth shader (shader.id == 0).");
            }

            _depthInstancedShader = RaylibNativeResources.LoadShader(System.IO.Path.Combine(baseDir, "shadow_depth_instanced.vs"), fsPath);
            if (_depthInstancedShader.id == 0)
            {
                throw new InvalidOperationException("Failed to load shadow_depth_instanced shader (shader.id == 0).");
            }

            _depthSkinningPoseTextureShader = RaylibNativeResources.LoadShader(System.IO.Path.Combine(baseDir, "shadow_depth_skinning_pose_texture.vs"), fsPath);
            if (_depthSkinningPoseTextureShader.id == 0)
            {
                throw new InvalidOperationException("Failed to load shadow_depth_skinning_pose_texture shader (shader.id == 0).");
            }

            _depthCutoutShader = RaylibNativeResources.LoadShader(
                System.IO.Path.Combine(baseDir, "shadow_depth_cutout.vs"),
                System.IO.Path.Combine(baseDir, "shadow_depth_cutout.fs"));
            if (_depthCutoutShader.id == 0)
            {
                throw new InvalidOperationException("Failed to load shadow_depth_cutout shader (shader.id == 0).");
            }

            ConfigureDepthShader(_depthShader, "shadow_depth");
            ConfigureInstancedDepthShader(_depthInstancedShader, "shadow_depth_instanced");
            (_locDepthSkinningInstanceBase, _locDepthSkinningBoneBase) = ConfigurePoseTextureSkinningDepthShader(
                _depthSkinningPoseTextureShader,
                "shadow_depth_skinning_pose_texture");
            _locCutoutAlphaCutoff = ConfigureCutoutDepthShader(_depthCutoutShader, "shadow_depth_cutout");

            _depthMaterial = RaylibNativeResources.LoadMaterialDefault();
            _depthMaterial.shader = _depthShader;
            _depthInstancedMaterial = RaylibNativeResources.LoadMaterialDefault();
            _depthInstancedMaterial.shader = _depthInstancedShader;
            _depthSkinningPoseTextureMaterial = RaylibNativeResources.LoadMaterialDefault();
            _depthSkinningPoseTextureMaterial.shader = _depthSkinningPoseTextureShader;
            _depthCutoutMaterial = RaylibNativeResources.LoadMaterialDefault();
            _depthCutoutMaterial.shader = _depthCutoutShader;
        }

        public Texture2D DepthTexture => _rt.texture;

        public int MapSize { get; }

        public float ReceiverBiasWorld { get; }

        public bool HasFrame { get; private set; }

        public RaylibMatrix LightViewProjection => Multiply(_lightView, _lightProjection);

        public float DepthRange => _depthRange;

        public void BeginFrame(Vector3 lightDirectionToward, Vector3 sceneCenter, float sceneRadius)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RaylibDirectionalShadowMap));
            }

            if (_frameActive)
            {
                throw new InvalidOperationException("Shadow frame already active; call EndFrame first.");
            }

            Vector3 forward = Vector3.Normalize(lightDirectionToward);
            if (forward.LengthSquared() < 0.5f)
            {
                forward = -Vector3.UnitY;
            }

            Vector3 upHint = MathF.Abs(Vector3.Dot(forward, Vector3.UnitY)) > 0.95f
                ? Vector3.UnitX
                : Vector3.UnitY;
            float eyeDistance = MathF.Max(sceneRadius * 1.8f, 8f);
            Vector3 eye = sceneCenter + (forward * eyeDistance);

            _lightView = BuildLookAt(eye, sceneCenter, upHint);

            float halfExtent = MathF.Max(sceneRadius * 1.35f, 4f);
            float farPlane = eyeDistance + (sceneRadius * 2.2f);
            _depthRange = farPlane - 0.1f;
            _lightProjection = BuildOrtho(
                -halfExtent, halfExtent, -halfExtent, halfExtent,
                0.1f, farPlane);

            Rl.BeginTextureMode(_rt);
            Rl.ClearBackground(new Color(255, 255, 255, 255));
            Rl.rlEnableDepthTest();
            Rl.rlEnableDepthMask();
            Rl.rlMatrixMode((int)RlMatrixMode.RL_PROJECTION);
            Rl.rlLoadIdentity();
            MultMatrix(ref _lightProjection);
            Rl.rlMatrixMode((int)RlMatrixMode.RL_MODELVIEW);
            Rl.rlLoadIdentity();
            MultMatrix(ref _lightView);
            _frameActive = true;
            HasFrame = true;
        }

        public void DrawMeshShadow(Mesh mesh, RaylibMatrix transform)
        {
            EnsureFrameActive();
            Rl.DrawMesh(mesh, _depthMaterial, transform);
        }

        /// <summary>镂空深度：采样 albedo alpha 低于 cutoff 的纹素 discard，其余与实体深度同编码。</summary>
        public void DrawMeshShadowCutout(Mesh mesh, RaylibMatrix transform, Texture2D albedo, float alphaCutoff)
        {
            EnsureFrameActive();
            if (albedo.id == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(DrawMeshShadowCutout)} requires a loaded albedo texture (texture.id == 0).");
            }

            _depthCutoutMaterial.maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO].texture = albedo;
            float cutoff = alphaCutoff;
            Rl.SetShaderValue(_depthCutoutShader, _locCutoutAlphaCutoff, &cutoff, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.DrawMesh(mesh, _depthCutoutMaterial, transform);
        }

        public void DrawMeshInstancedShadow(Mesh mesh, RaylibMatrix* transforms, int count)
        {
            EnsureFrameActive();
            if (transforms == null)
            {
                throw new ArgumentNullException(nameof(transforms));
            }

            if (count <= 0)
            {
                return;
            }

            Rl.DrawMeshInstanced(mesh, _depthInstancedMaterial, transforms, count);
        }

        /// <summary>姿势纹理蒙皮的深度绘制（#1395）：与主 pass 共用骨骼调色板与实例表，
        /// 采样器经 OCCLUSION/HEIGHT 材质槽由 DrawMeshInstanced 自动绑定（locs 已在配置期写入）。</summary>
        public void DrawSkinnedMeshPoseTextureShadow(
            Mesh mesh,
            RaylibMatrix* transforms,
            int count,
            Texture2D bonePalette,
            Texture2D instanceTable,
            float instanceBase,
            float boneBase)
        {
            EnsureFrameActive();
            if (transforms == null)
            {
                throw new ArgumentNullException(nameof(transforms));
            }

            if (count <= 0)
            {
                return;
            }

            if (bonePalette.id == 0 || instanceTable.id == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(DrawSkinnedMeshPoseTextureShadow)} requires created pose palette textures (id == 0).");
            }

            Rl.SetMaterialTexture(ref _depthSkinningPoseTextureMaterial, (int)Rl.MaterialMapIndex.MATERIAL_MAP_OCCLUSION, bonePalette);
            Rl.SetMaterialTexture(ref _depthSkinningPoseTextureMaterial, (int)Rl.MaterialMapIndex.MATERIAL_MAP_HEIGHT, instanceTable);
            Rl.SetShaderValue(_depthSkinningPoseTextureShader, _locDepthSkinningInstanceBase, &instanceBase, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_depthSkinningPoseTextureShader, _locDepthSkinningBoneBase, &boneBase, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.DrawMeshInstanced(mesh, _depthSkinningPoseTextureMaterial, transforms, count);
        }

        /// <summary>模型深度：换装深度材质经 DrawModelEx 原生路径绘制后还原。</summary>
        public void DrawModelShadow(Model model, Vector3 position, float rotationAngleY, Vector3 scale)
        {
            DrawModelShadow(model, position, Vector3.UnitY, rotationAngleY, scale);
        }

        public void DrawModelShadow(Model model, Vector3 position, Vector3 rotationAxis, float rotationAngleDegrees, Vector3 scale)
        {
            EnsureFrameActive();
            Span<Shader> original = stackalloc Shader[model.materialCount];
            for (int i = 0; i < model.materialCount; i++)
            {
                original[i] = model.materials[i].shader;
                model.materials[i].shader = _depthShader;
            }

            Rl.DrawModelEx(model, position, rotationAxis, rotationAngleDegrees, scale, new Color(255, 255, 255, 255));

            for (int i = 0; i < model.materialCount; i++)
            {
                model.materials[i].shader = original[i];
            }
        }

        public void EndFrame()
        {
            if (!_frameActive)
            {
                return;
            }

            Rl.rlMatrixMode((int)RlMatrixMode.RL_MODELVIEW);
            Rl.rlLoadIdentity();
            Rl.rlMatrixMode((int)RlMatrixMode.RL_PROJECTION);
            Rl.rlLoadIdentity();
            Rl.EndTextureMode();
            _frameActive = false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            EndFrame();
            _depthMaterial.shader = default;
            _depthInstancedMaterial.shader = default;
            _depthSkinningPoseTextureMaterial.shader = default;
            _depthCutoutMaterial.shader = default;
            RaylibNativeResources.UnloadMaterial(_depthMaterial);
            RaylibNativeResources.UnloadMaterial(_depthInstancedMaterial);
            RaylibNativeResources.UnloadMaterial(_depthSkinningPoseTextureMaterial);
            RaylibNativeResources.UnloadMaterial(_depthCutoutMaterial);
            RaylibNativeResources.UnloadShader(_depthShader);
            RaylibNativeResources.UnloadShader(_depthInstancedShader);
            RaylibNativeResources.UnloadShader(_depthSkinningPoseTextureShader);
            RaylibNativeResources.UnloadShader(_depthCutoutShader);
            RaylibNativeResources.UnloadRenderTexture(_rt);
            _disposed = true;
        }

        private void EnsureFrameActive()
        {
            if (!_frameActive)
            {
                throw new InvalidOperationException("Shadow draws require BeginFrame first.");
            }
        }

        private static void MultMatrix(ref RaylibMatrix matrix)
        {
            RaylibMatrix local = matrix;
            float* values = stackalloc float[16]
            {
                local.m0, local.m1, local.m2, local.m3,
                local.m4, local.m5, local.m6, local.m7,
                local.m8, local.m9, local.m10, local.m11,
                local.m12, local.m13, local.m14, local.m15
            };
            Rl.rlMultMatrixf(values);
        }

        private static void ConfigureDepthShader(Shader shader, string name)
        {
            int locVertexPosition = Rl.GetShaderLocationAttrib(shader, "vertexPosition");
            int locMvp = Rl.GetShaderLocation(shader, "mvp");
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_POSITION] = locVertexPosition;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = locMvp;
            if (locVertexPosition < 0)
            {
                throw new InvalidOperationException($"{name} shader attrib 'vertexPosition' not found.");
            }

            if (locMvp < 0)
            {
                throw new InvalidOperationException($"{name} shader uniform 'mvp' not found.");
            }
        }

        private static void ConfigureInstancedDepthShader(Shader shader, string name)
        {
            ConfigureDepthShader(shader, name);
            int locInstance = Rl.GetShaderLocationAttrib(shader, "instanceTransform");
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] = locInstance;
            if (locInstance < 0)
            {
                throw new InvalidOperationException($"{name} shader attrib 'instanceTransform' not found.");
            }
        }

        private static (int InstanceBase, int BoneBase) ConfigurePoseTextureSkinningDepthShader(Shader shader, string name)
        {
            ConfigureInstancedDepthShader(shader, name);
            int locBoneIds = Rl.GetShaderLocationAttrib(shader, "vertexBoneIds");
            int locBoneWeights = Rl.GetShaderLocationAttrib(shader, "vertexBoneWeights");
            int locBonePalette = Rl.GetShaderLocation(shader, "uBonePalette");
            int locInstanceTable = Rl.GetShaderLocation(shader, "uInstanceTable");
            int locInstanceBase = Rl.GetShaderLocation(shader, "uInstanceBase");
            int locBoneBase = Rl.GetShaderLocation(shader, "uBoneBase");
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_BONEIDS] = locBoneIds;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_BONEWEIGHTS] = locBoneWeights;
            // 采样器经材质槽绑定：DrawMeshInstanced 按 locs[MAP_ALBEDO+i] 赋 sampler 值=槽位号，
            // 因此必须把自定义采样器名写进对应槽位 loc（OCCLUSION=调色板，HEIGHT=实例表）
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_OCCLUSION] = locBonePalette;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_HEIGHT] = locInstanceTable;
            if (locBoneIds < 0)
            {
                throw new InvalidOperationException($"{name} shader attrib 'vertexBoneIds' not found.");
            }

            if (locBoneWeights < 0)
            {
                throw new InvalidOperationException($"{name} shader attrib 'vertexBoneWeights' not found.");
            }

            if (locBonePalette < 0)
            {
                throw new InvalidOperationException($"{name} shader uniform 'uBonePalette' not found.");
            }

            if (locInstanceTable < 0)
            {
                throw new InvalidOperationException($"{name} shader uniform 'uInstanceTable' not found.");
            }

            if (locInstanceBase < 0)
            {
                throw new InvalidOperationException($"{name} shader uniform 'uInstanceBase' not found.");
            }

            if (locBoneBase < 0)
            {
                throw new InvalidOperationException($"{name} shader uniform 'uBoneBase' not found.");
            }

            return (locInstanceBase, locBoneBase);
        }

        private static int ConfigureCutoutDepthShader(Shader shader, string name)
        {
            ConfigureDepthShader(shader, name);
            int locTexCoord = Rl.GetShaderLocationAttrib(shader, "vertexTexCoord");
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TEXCOORD01] = locTexCoord;
            int locAlbedo = Rl.GetShaderLocation(shader, "texture0");
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ALBEDO] = locAlbedo;
            int locCutoff = Rl.GetShaderLocation(shader, "alphaCutoff");
            if (locTexCoord < 0)
            {
                throw new InvalidOperationException($"{name} shader attrib 'vertexTexCoord' not found.");
            }

            if (locAlbedo < 0)
            {
                throw new InvalidOperationException($"{name} shader uniform 'texture0' not found.");
            }

            if (locCutoff < 0)
            {
                throw new InvalidOperationException($"{name} shader uniform 'alphaCutoff' not found.");
            }

            return locCutoff;
        }

        private static RaylibMatrix BuildLookAt(Vector3 eye, Vector3 target, Vector3 up)
        {
            Vector3 f = Vector3.Normalize(target - eye);
            Vector3 s = Vector3.Normalize(Vector3.Cross(f, up));
            Vector3 u = Vector3.Cross(s, f);

            return new RaylibMatrix
            {
                m0 = s.X, m4 = s.Y, m8 = s.Z, m12 = -Vector3.Dot(s, eye),
                m1 = u.X, m5 = u.Y, m9 = u.Z, m13 = -Vector3.Dot(u, eye),
                m2 = -f.X, m6 = -f.Y, m10 = -f.Z, m14 = Vector3.Dot(f, eye),
                m3 = 0f, m7 = 0f, m11 = 0f, m15 = 1f,
            };
        }

        private static RaylibMatrix BuildOrtho(float left, float right, float bottom, float top, float near, float far)
        {
            float rl = 1f / (right - left);
            float tb = 1f / (top - bottom);
            float fn = 1f / (far - near);

            return new RaylibMatrix
            {
                m0 = 2f * rl, m4 = 0f, m8 = 0f, m12 = -(right + left) * rl,
                m1 = 0f, m5 = 2f * tb, m9 = 0f, m13 = -(top + bottom) * tb,
                m2 = 0f, m6 = 0f, m10 = -2f * fn, m14 = -(far + near) * fn,
                m3 = 0f, m7 = 0f, m11 = 0f, m15 = 1f,
            };
        }

        private static RaylibMatrix Multiply(in RaylibMatrix a, in RaylibMatrix b)
        {
            return new RaylibMatrix
            {
                m0 = (a.m0 * b.m0) + (a.m1 * b.m4) + (a.m2 * b.m8) + (a.m3 * b.m12),
                m1 = (a.m0 * b.m1) + (a.m1 * b.m5) + (a.m2 * b.m9) + (a.m3 * b.m13),
                m2 = (a.m0 * b.m2) + (a.m1 * b.m6) + (a.m2 * b.m10) + (a.m3 * b.m14),
                m3 = (a.m0 * b.m3) + (a.m1 * b.m7) + (a.m2 * b.m11) + (a.m3 * b.m15),
                m4 = (a.m4 * b.m0) + (a.m5 * b.m4) + (a.m6 * b.m8) + (a.m7 * b.m12),
                m5 = (a.m4 * b.m1) + (a.m5 * b.m5) + (a.m6 * b.m9) + (a.m7 * b.m13),
                m6 = (a.m4 * b.m2) + (a.m5 * b.m6) + (a.m6 * b.m10) + (a.m7 * b.m14),
                m7 = (a.m4 * b.m3) + (a.m5 * b.m7) + (a.m6 * b.m11) + (a.m7 * b.m15),
                m8 = (a.m8 * b.m0) + (a.m9 * b.m4) + (a.m10 * b.m8) + (a.m11 * b.m12),
                m9 = (a.m8 * b.m1) + (a.m9 * b.m5) + (a.m10 * b.m9) + (a.m11 * b.m13),
                m10 = (a.m8 * b.m2) + (a.m9 * b.m6) + (a.m10 * b.m10) + (a.m11 * b.m14),
                m11 = (a.m8 * b.m3) + (a.m9 * b.m7) + (a.m10 * b.m11) + (a.m11 * b.m15),
                m12 = (a.m12 * b.m0) + (a.m13 * b.m4) + (a.m14 * b.m8) + (a.m15 * b.m12),
                m13 = (a.m12 * b.m1) + (a.m13 * b.m5) + (a.m14 * b.m9) + (a.m15 * b.m13),
                m14 = (a.m12 * b.m2) + (a.m13 * b.m6) + (a.m14 * b.m10) + (a.m15 * b.m14),
                m15 = (a.m12 * b.m3) + (a.m13 * b.m7) + (a.m14 * b.m11) + (a.m15 * b.m15),
            };
        }
    }
}
