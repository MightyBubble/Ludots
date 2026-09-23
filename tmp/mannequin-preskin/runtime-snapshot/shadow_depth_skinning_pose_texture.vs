#version 330

// 姿势纹理蒙皮的深度 pass（#1395）：与主 pass 共用同一骨骼调色板与实例表，
// 只计算位置（无法线/颜色变换）——阴影与主 pass 的蒙皮位置严格一致。
// texel 布局与 addressing 合同见 skinning_instanced_pose_texture.vs 顶部注释。

layout(location = 0) in vec3 vertexPosition;
layout(location = 7) in vec4 vertexBoneIds;
layout(location = 8) in vec4 vertexBoneWeights;
layout(location = 9) in mat4 instanceTransform;

uniform mat4 mvp;
uniform float uRigidBoneIndex; // -1: vertex weights, -2: static, >=0: rigid joint.
uniform float uUseSharedPose;
uniform mat4 uSharedBones[128];
uniform sampler2D uBonePalette;
uniform sampler2D uInstanceTable;
uniform float uInstanceBase;
uniform float uBoneBase;
uniform float uPaletteSlotsPerRow;
uniform float uPaletteSlotRows;

mat4 FetchBoneMatrix(int poseRow, int boneSlot)
{
    if (uUseSharedPose > 0.5) return uSharedBones[boneSlot - int(uBoneBase)];
    int slotsPerRow = int(uPaletteSlotsPerRow + 0.5);
    int slotRows = int(uPaletteSlotRows + 0.5);
    int slabRow = boneSlot / slotsPerRow;
    int slotInRow = boneSlot - slabRow * slotsPerRow;
    int baseX = slotInRow * 4;
    int y = poseRow * slotRows + slabRow;
    vec4 c0 = texelFetch(uBonePalette, ivec2(baseX + 0, y), 0);
    vec4 c1 = texelFetch(uBonePalette, ivec2(baseX + 1, y), 0);
    vec4 c2 = texelFetch(uBonePalette, ivec2(baseX + 2, y), 0);
    vec4 c3 = texelFetch(uBonePalette, ivec2(baseX + 3, y), 0);
    return mat4(c0, c1, c2, c3);
}

void main()
{
    int instanceTexel = (int(uInstanceBase) + gl_InstanceID) * 2;
    vec4 instance = texelFetch(uInstanceTable, ivec2(instanceTexel % 1024, instanceTexel / 1024), 0);
    int poseRow = int(instance.x + 0.5);

    mat4 skin;
    if (uRigidBoneIndex >= 0.0)
    {
        skin = FetchBoneMatrix(poseRow, int(uRigidBoneIndex) + int(uBoneBase));
    }
    else if (uRigidBoneIndex < -1.5)
    {
        skin = mat4(1.0);
    }
    else
    {
        skin = mat4(0.0);
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
    }

    vec4 skinnedPosition = skin * vec4(vertexPosition, 1.0);
    gl_Position = mvp * instanceTransform * skinnedPosition;
}
