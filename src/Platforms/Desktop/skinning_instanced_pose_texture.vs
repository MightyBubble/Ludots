#version 330

// 姿势纹理蒙皮（#1395）：骨骼矩阵经 RGBA32F 调色板纹理按 (poseRow, boneSlot) texelFetch，
// 姿势行与 RGBA tint 经实例表纹理按 gl_InstanceID 寻址——每 draw 覆盖全部姿势与颜色。
// 调色板 texel 布局与 raylib 原生上传语义一致（glUniformMatrix4fv/GL_FALSE 列主序）：
// 第 b 根骨骼占 4 texel，texel[k] = 该矩阵内存中第 k 组 4 个 float（即 mat4 第 k 列）。
// 多 mesh 模型：骨骼槽位 = mesh 局部 boneId + uBoneBase（各 mesh 按 boneCount 累计）。
// 实例表每实例占 2 texel：texelA = (poseRow, tint.r, tint.g, tint.b)，texelB = (tint.a, 0, 0, 0)；
// 实例表宽度固定 1024（与 RaylibPoseTexturePalette.InstanceTableWidth 一致）。
// uInstanceBase/uBoneBase 走 float：SetShaderValue 以 glUniform1fv 上传，
// 声明为 int 会因类型不符触发 GL_INVALID_OPERATION 且 uniform 保持默认值 0。

layout (location = 0) in vec3 vertexPosition;
layout (location = 1) in vec2 vertexTexCoord;
layout (location = 2) in vec3 vertexNormal;
layout (location = 3) in vec4 vertexColor;
// raylib 5.5 defaults: boneIds=7, boneWeights=8, instance mat starts at 9
// （与 GetShaderLocationAttrib 动态查询路径兼容；实测 crowd_anim 渲染正确）
layout (location = 7) in vec4 vertexBoneIds;
layout (location = 8) in vec4 vertexBoneWeights;
layout (location = 9) in mat4 instanceTransform;

uniform mat4 mvp;
uniform sampler2D uBonePalette;
uniform sampler2D uInstanceTable;
uniform float uInstanceBase;     // 本 draw 首实例在实例表中的全局序号（texel 对编号）
uniform float uBoneBase;         // 本 mesh 首骨骼在调色板槽位中的全局序号

out vec2 fragTexCoord;
out vec4 fragColor;
out vec3 fragNormal;
out vec3 fragPos;

mat4 FetchBoneMatrix(int poseRow, int boneSlot)
{
    int baseX = boneSlot * 4;
    vec4 c0 = texelFetch(uBonePalette, ivec2(baseX + 0, poseRow), 0);
    vec4 c1 = texelFetch(uBonePalette, ivec2(baseX + 1, poseRow), 0);
    vec4 c2 = texelFetch(uBonePalette, ivec2(baseX + 2, poseRow), 0);
    vec4 c3 = texelFetch(uBonePalette, ivec2(baseX + 3, poseRow), 0);
    return mat4(c0, c1, c2, c3);
}

void main()
{
    int instanceTexel = (int(uInstanceBase) + gl_InstanceID) * 2;
    vec4 instance = texelFetch(uInstanceTable, ivec2(instanceTexel % 1024, instanceTexel / 1024), 0);
    vec4 alphaTexel = texelFetch(uInstanceTable, ivec2((instanceTexel + 1) % 1024, (instanceTexel + 1) / 1024), 0);
    int poseRow = int(instance.x + 0.5);
    vec3 tint = vec3(instance.y, instance.z, instance.w);
    float alpha = alphaTexel.x;

    mat4 skin = mat4(0.0);
    if (vertexBoneWeights.x > 0.0)
    {
        skin += FetchBoneMatrix(poseRow, int(vertexBoneIds.x) + int(uBoneBase)) * vertexBoneWeights.x;
    }

    if (vertexBoneWeights.y > 0.0)
    {
        skin += FetchBoneMatrix(poseRow, int(vertexBoneIds.y) + int(uBoneBase)) * vertexBoneWeights.y;
    }

    if (vertexBoneWeights.z > 0.0)
    {
        skin += FetchBoneMatrix(poseRow, int(vertexBoneIds.z) + int(uBoneBase)) * vertexBoneWeights.z;
    }

    if (vertexBoneWeights.w > 0.0)
    {
        skin += FetchBoneMatrix(poseRow, int(vertexBoneIds.w) + int(uBoneBase)) * vertexBoneWeights.w;
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
