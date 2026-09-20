#version 430

// SSBO 蒙皮的深度 pass：与主 pass 共用姿势/实例 SSBO，只计算位置——
// 阴影与主 pass 的蒙皮位置严格一致。数据合同见 skinning_instanced_ssbo.vs 顶部注释。

layout (location = 0) in vec3 vertexPosition;
layout (location = 7) in vec4 vertexBoneIds;
layout (location = 8) in vec4 vertexBoneWeights;

layout(std430, binding = 2) readonly buffer PoseMatrixBlock { mat4 poseMatrices[]; };
layout(std430, binding = 3) readonly buffer InstanceBlock { vec4 instanceData[]; };

uniform mat4 mvp;
uniform float uInstanceBase;
uniform float uBoneBase;
uniform float uPoseStride;

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
    gl_Position = mvp * instanceTransform * skinnedPosition;
}
