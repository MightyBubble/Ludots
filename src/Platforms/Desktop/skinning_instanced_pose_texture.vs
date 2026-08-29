#version 330

// 姿势纹理蒙皮（#1387）：骨骼矩阵经 RGBA32F 调色板纹理按 (poseRow, boneIndex) texelFetch，
// 姿势行与 tint 经实例表纹理按 gl_InstanceID 2D 寻址——每 draw 覆盖全部姿势与颜色。
// 纹理布局：调色板宽 = boneCount*4 texel（每骨骼 mat4 = 4 texel，列主序），高 = 姿势数；
// 实例表每实例 1 texel：x=poseRow（float 精确到 2^24 行内），y-z-w=tint RGB。

#define MAX_BONE_NUM 128
#define INSTANCE_TABLE_WIDTH 1024

layout (location = 0) in vec3 vertexPosition;
layout (location = 1) in vec2 vertexTexCoord;
layout (location = 2) in vec3 vertexNormal;
layout (location = 3) in vec4 vertexColor;
// raylib 5.5 defaults: boneIds=7, boneWeights=8, instance mat starts at 9
layout (location = 7) in vec4 vertexBoneIds;
layout (location = 8) in vec4 vertexBoneWeights;
layout (location = 9) in mat4 instanceTransform;

uniform mat4 mvp;
uniform sampler2D uBonePalette;
uniform sampler2D uInstanceTable;
uniform int uBonePaletteWidth;   // texel 宽 = boneCount*4
uniform int uInstanceBase;       // 本 draw 首实例在实例表中的全局序号

out vec2 fragTexCoord;
out vec4 fragColor;
out vec3 fragNormal;
out vec3 fragPos;

mat4 FetchBoneMatrix(int poseRow, int boneIndex)
{
    int baseX = boneIndex * 4;
    vec4 c0 = texelFetch(uBonePalette, ivec2(baseX + 0, poseRow), 0);
    vec4 c1 = texelFetch(uBonePalette, ivec2(baseX + 1, poseRow), 0);
    vec4 c2 = texelFetch(uBonePalette, ivec2(baseX + 2, poseRow), 0);
    vec4 c3 = texelFetch(uBonePalette, ivec2(baseX + 3, poseRow), 0);
    return mat4(c0, c1, c2, c3);
}

void main()
{
    int globalInstance = uInstanceBase + gl_InstanceID;
    int tableX = globalInstance % INSTANCE_TABLE_WIDTH;
    int tableY = globalInstance / INSTANCE_TABLE_WIDTH;
    vec4 instance = texelFetch(uInstanceTable, ivec2(tableX, tableY), 0);
    int poseRow = int(instance.x + 0.5);
    vec3 tint = vec3(instance.y, instance.z, instance.w);

    mat4 skin = mat4(0.0);
    if (vertexBoneWeights.x > 0.0)
    {
        skin += FetchBoneMatrix(poseRow, int(vertexBoneIds.x)) * vertexBoneWeights.x;
    }

    if (vertexBoneWeights.y > 0.0)
    {
        skin += FetchBoneMatrix(poseRow, int(vertexBoneIds.y)) * vertexBoneWeights.y;
    }

    if (vertexBoneWeights.z > 0.0)
    {
        skin += FetchBoneMatrix(poseRow, int(vertexBoneIds.z)) * vertexBoneWeights.z;
    }

    if (vertexBoneWeights.w > 0.0)
    {
        skin += FetchBoneMatrix(poseRow, int(vertexBoneIds.w)) * vertexBoneWeights.w;
    }

    vec4 skinnedPosition = skin * vec4(vertexPosition, 1.0);
    vec3 skinnedNormal = mat3(skin) * vertexNormal;
    vec4 worldPos = instanceTransform * skinnedPosition;
    fragTexCoord = vertexTexCoord;
    fragColor = vec4(vertexColor.rgb * tint, vertexColor.a);
    fragNormal = normalize(mat3(instanceTransform) * skinnedNormal);
    fragPos = worldPos.xyz;
    gl_Position = mvp * worldPos;
}
