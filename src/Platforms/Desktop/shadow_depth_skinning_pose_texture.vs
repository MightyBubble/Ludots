#version 330

// 姿势纹理蒙皮的深度 pass：与主 pass 共用同一骨骼调色板与实例表，
// 只计算位置（无法线/颜色变换）——阴影与主 pass 的蒙皮位置严格一致。
// texel 布局与 addressing 合同见 skinning_instanced_pose_texture.vs 顶部注释。

layout(location = 0) in vec3 vertexPosition;
layout(location = 7) in vec4 vertexBoneIds;
layout(location = 8) in vec4 vertexBoneWeights;
layout(location = 9) in mat4 instanceTransform;

uniform mat4 mvp;
uniform sampler2D uBonePalette;
uniform sampler2D uInstanceTable;
uniform float uInstanceBase;
uniform float uBoneBase;

const int BONE_TEXELS_PER_SLOT = 4;

vec4 FetchBoneTexel(int linearTexel, int paletteWidth)
{
    return texelFetch(uBonePalette, ivec2(linearTexel % paletteWidth, linearTexel / paletteWidth), 0);
}

mat4 FetchBoneMatrix(int poseTextureRow, int boneSlot)
{
    int paletteWidth = textureSize(uBonePalette, 0).x;
    int baseTexel = poseTextureRow * paletteWidth + boneSlot * BONE_TEXELS_PER_SLOT;
    vec4 c0 = FetchBoneTexel(baseTexel + 0, paletteWidth);
    vec4 c1 = FetchBoneTexel(baseTexel + 1, paletteWidth);
    vec4 c2 = FetchBoneTexel(baseTexel + 2, paletteWidth);
    vec4 c3 = FetchBoneTexel(baseTexel + 3, paletteWidth);
    return mat4(c0, c1, c2, c3);
}

void main()
{
    int instanceTexel = (int(uInstanceBase) + gl_InstanceID) * 2;
    vec4 instance = texelFetch(uInstanceTable, ivec2(instanceTexel % 1024, instanceTexel / 1024), 0);
    int poseTextureRow = int(instance.x + 0.5);

    mat4 skin = mat4(0.0);
    if (vertexBoneWeights.x > 0.0)
    {
        skin += FetchBoneMatrix(poseTextureRow, int(vertexBoneIds.x) + int(uBoneBase)) * vertexBoneWeights.x;
    }

    if (vertexBoneWeights.y > 0.0)
    {
        skin += FetchBoneMatrix(poseTextureRow, int(vertexBoneIds.y) + int(uBoneBase)) * vertexBoneWeights.y;
    }

    if (vertexBoneWeights.z > 0.0)
    {
        skin += FetchBoneMatrix(poseTextureRow, int(vertexBoneIds.z) + int(uBoneBase)) * vertexBoneWeights.z;
    }

    if (vertexBoneWeights.w > 0.0)
    {
        skin += FetchBoneMatrix(poseTextureRow, int(vertexBoneIds.w) + int(uBoneBase)) * vertexBoneWeights.w;
    }

    vec4 skinnedPosition = skin * vec4(vertexPosition, 1.0);
    gl_Position = mvp * instanceTransform * skinnedPosition;
}
