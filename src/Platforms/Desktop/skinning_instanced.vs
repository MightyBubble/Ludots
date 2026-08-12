#version 330

#define MAX_BONE_NUM 128

layout (location = 0) in vec3 vertexPosition;
layout (location = 1) in vec2 vertexTexCoord;
layout (location = 3) in vec4 vertexColor;
// raylib 5.5 defaults: boneIds=7, boneWeights=8, instance mat starts at 9
layout (location = 7) in vec4 vertexBoneIds;
layout (location = 8) in vec4 vertexBoneWeights;
layout (location = 9) in mat4 instanceTransform;

uniform mat4 mvp;
uniform mat4 boneMatrices[MAX_BONE_NUM];

out vec2 fragTexCoord;
out vec4 fragColor;

void main()
{
    int boneIndex0 = int(vertexBoneIds.x);
    int boneIndex1 = int(vertexBoneIds.y);
    int boneIndex2 = int(vertexBoneIds.z);
    int boneIndex3 = int(vertexBoneIds.w);

    mat4 skin =
        boneMatrices[boneIndex0] * vertexBoneWeights.x +
        boneMatrices[boneIndex1] * vertexBoneWeights.y +
        boneMatrices[boneIndex2] * vertexBoneWeights.z +
        boneMatrices[boneIndex3] * vertexBoneWeights.w;

    vec4 skinnedPosition = skin * vec4(vertexPosition, 1.0);
    fragTexCoord = vertexTexCoord;
    fragColor = vertexColor;
    gl_Position = mvp * instanceTransform * skinnedPosition;
}
