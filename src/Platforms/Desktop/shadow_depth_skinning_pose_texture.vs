#version 330

// 姿势纹理蒙皮的深度 pass（#1387）：与主 pass 共用同一骨骼调色板与实例表，
// 只计算位置（无法线/颜色变换）——阴影与主 pass 的蒙皮位置严格一致。

#define INSTANCE_TABLE_WIDTH 1024

layout(location = 0) in vec3 vertexPosition;
layout(location = 7) in vec4 vertexBoneIds;
layout(location = 8) in vec4 vertexBoneWeights;
layout(location = 9) in mat4 instanceTransform;

uniform mat4 mvp;
uniform sampler2D uBonePalette;
uniform sampler2D uInstanceTable;
uniform int uBonePaletteWidth;
uniform int uInstanceBase;

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
    gl_Position = mvp * instanceTransform * skinnedPosition;
}
