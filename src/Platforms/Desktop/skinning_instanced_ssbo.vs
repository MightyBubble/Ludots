#version 430

// SSBO 蒙皮主 pass：骨骼矩阵由 compute 求值写入姿势 SSBO（binding 2），
// 实例数据（仿射变换 3×vec4 + (poseRow, RGB24, alpha, 0)）驻实例 SSBO（binding 3），
// 按 uInstanceBase + gl_InstanceID 寻址——每 draw 零逐实例 CPU 上传。
// 骨骼矩阵列合同与 RaylibGpuSkinnedSsboPipeline/等价测试锁定：
// 列 0..2 = boneS ∘ R_col，列 3 = boneS ∘ boneT（平移不随旋转）。
// 属性布局沿用 raylib 5.5 默认 VAO：boneIds=7, boneWeights=8（location 9 实例矩阵已退役）。

layout (location = 0) in vec3 vertexPosition;
layout (location = 1) in vec2 vertexTexCoord;
layout (location = 2) in vec3 vertexNormal;
layout (location = 3) in vec4 vertexColor;
layout (location = 7) in vec4 vertexBoneIds;
layout (location = 8) in vec4 vertexBoneWeights;

layout(std430, binding = 2) readonly buffer PoseMatrixBlock { mat4 poseMatrices[]; };
layout(std430, binding = 3) readonly buffer InstanceBlock { vec4 instanceData[]; };

uniform mat4 mvp;
uniform float uInstanceBase;  // 本 draw 首实例的全局 vec4 组编号（SetShaderValue 走 glUniform1fv，float 合同）
uniform float uBoneBase;      // 本 mesh 首骨骼在姿势行内的槽位基址
uniform float uPoseStride;    // 每姿势行的 mat4 槽位数（容量 MaxBoneSlots）

out vec2 fragTexCoord;
out vec4 fragColor;
out vec3 fragNormal;
out vec3 fragPos;

void main()
{
    int instanceVec4 = (int(uInstanceBase) + gl_InstanceID) * 4;
    vec4 transformC0 = instanceData[instanceVec4 + 0];
    vec4 transformC1 = instanceData[instanceVec4 + 1];
    vec4 transformC2 = instanceData[instanceVec4 + 2];
    vec4 instance = instanceData[instanceVec4 + 3];
    mat4 instanceTransform = mat4(
        vec4(transformC0.xyz, 0.0),
        vec4(transformC1.xyz, 0.0),
        vec4(transformC2.xyz, 0.0),
        vec4(transformC0.w, transformC1.w, transformC2.w, 1.0));

    int poseRow = int(instance.x + 0.5);
    int packedRgb = int(instance.y + 0.5);
    vec3 tint = vec3(
        packedRgb % 256,
        (packedRgb / 256) % 256,
        (packedRgb / 65536) % 256) / 255.0;
    float alpha = instance.z;

    int poseBase = poseRow * int(uPoseStride + 0.5) + int(uBoneBase);
    mat4 skin = mat4(0.0);
    if (vertexBoneWeights.x > 0.0)
    {
        skin += poseMatrices[poseBase + int(vertexBoneIds.x)] * vertexBoneWeights.x;
    }

    if (vertexBoneWeights.y > 0.0)
    {
        skin += poseMatrices[poseBase + int(vertexBoneIds.y)] * vertexBoneWeights.y;
    }

    if (vertexBoneWeights.z > 0.0)
    {
        skin += poseMatrices[poseBase + int(vertexBoneIds.z)] * vertexBoneWeights.z;
    }

    if (vertexBoneWeights.w > 0.0)
    {
        skin += poseMatrices[poseBase + int(vertexBoneIds.w)] * vertexBoneWeights.w;
    }

    vec4 skinnedPosition = skin * vec4(vertexPosition, 1.0);
    vec3 skinnedNormal = mat3(skin) * vertexNormal;
    vec4 worldPos = instanceTransform * skinnedPosition;
    fragTexCoord = vertexTexCoord;
    // tint 只在 VS 乘一次（FS 不再持有 tint uniform，修复双重染色）
    fragColor = vec4(vertexColor.rgb * tint, vertexColor.a * alpha);
    fragNormal = normalize(mat3(instanceTransform) * skinnedNormal);
    fragPos = worldPos.xyz;
    gl_Position = mvp * worldPos;
}
