#version 430

// GPU 姿势求值：皮肤矩阵 = 关键帧全局 TRS ∘ bindPose⁻¹，按 raylib 5.5 rmodels.c 的
// TRS 分解式逐位复刻（离散帧采样，无插值；父子链在 LoadModelAnimations 期已展平进 framePoses）。
// boneT = rotate(animScale*invT, animR) + animT；boneR = animR*invR；boneS = animS*invS；
// matrix = QuaternionToMatrix(boneR) * Translate(boneT) * Scale(boneS)。
// 每个 poseRow 一个 workgroup.y；invocation.x 以 64 为步长覆盖该行全部骨骼槽位。
// 布局全部以 vec4/ivec4 为单位（std430 下 16 字节对齐无歧义）：
//   binding 0 模型数据（关键帧 + bindPose⁻¹，随模型驻留一次性上传）
//   binding 1 行描述（每行 1×ivec4：keyframeVec4Base, bindInvVec4Base, boneCount, 0）
//   binding 2 姿势输出（每行 uPoseStride 个 mat4；stride = 容量 MaxBoneSlots）

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

layout(std430, binding = 0) readonly buffer ModelDataBlock { vec4 modelData[]; };
layout(std430, binding = 1) readonly buffer RowMetaBlock { ivec4 rowMeta[]; };
layout(std430, binding = 2) writeonly buffer PoseMatrixBlock { mat4 poseMatrices[]; };

uniform int uPoseStride;

vec3 RotateByQuat(vec3 v, vec4 q)
{
    return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v);
}

vec4 MulQuat(vec4 a, vec4 b)
{
    return vec4(a.w * b.xyz + b.w * a.xyz + cross(a.xyz, b.xyz),
                a.w * b.w - dot(a.xyz, b.xyz));
}

void main()
{
    uint row = gl_GlobalInvocationID.y;
    ivec4 meta = rowMeta[row];
    int keyframeBase = meta.x;
    int bindInvBase = meta.y;
    int boneCount = meta.z;
    for (int b = int(gl_LocalInvocationID.x); b < boneCount; b += 64)
    {
        vec4 animT = modelData[keyframeBase + b * 3 + 0];
        vec4 animR = modelData[keyframeBase + b * 3 + 1];
        vec4 animS = modelData[keyframeBase + b * 3 + 2];
        vec4 invT = modelData[bindInvBase + b * 3 + 0];
        vec4 invR = modelData[bindInvBase + b * 3 + 1];
        vec4 invS = modelData[bindInvBase + b * 3 + 2];

        vec3 boneT = RotateByQuat(animS.xyz * invT.xyz, animR) + animT.xyz;
        vec4 boneR = MulQuat(animR, invR);
        vec3 boneS = animS.xyz * invS.xyz;

        float a2 = boneR.x * boneR.x;
        float b2 = boneR.y * boneR.y;
        float c2 = boneR.z * boneR.z;
        float ac = boneR.x * boneR.z;
        float ab = boneR.x * boneR.y;
        float bc = boneR.y * boneR.z;
        float ad = boneR.w * boneR.x;
        float bd = boneR.w * boneR.y;
        float cd = boneR.w * boneR.z;

        // 合同（由 RaylibGpuSkinnedSsboEquivalenceTests 与 raylib CPU 求值逐元素对账锁定，
        // 并与 RaylibPoseTexturePalette.PackAffineBoneMatrix 的已验证屏幕路径一致）：
        // 列 0..2 = boneS ∘ R_col（逐分量缩放标准四元数矩阵的列），
        // 列 3 = boneS ∘ boneT（平移不随旋转）。
        mat3 rotation = mat3(
            1.0 - 2.0 * (b2 + c2), 2.0 * (ab + cd), 2.0 * (ac - bd),
            2.0 * (ab - cd), 1.0 - 2.0 * (a2 + c2), 2.0 * (bc + ad),
            2.0 * (ac + bd), 2.0 * (bc - ad), 1.0 - 2.0 * (a2 + b2));

        mat4 m = mat4(vec4(boneS * rotation[0], 0.0),
                      vec4(boneS * rotation[1], 0.0),
                      vec4(boneS * rotation[2], 0.0),
                      vec4(boneS * boneT, 1.0));
        poseMatrices[row * uPoseStride + b] = m;
    }
}
