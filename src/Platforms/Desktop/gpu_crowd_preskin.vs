#version 430
#extension GL_ARB_shader_storage_buffer_object : require

// GPU crowd 预蒙皮主 pass：骨骼蒙皮已由 gpu_preskin.comp 按姿势行烘焙（binding 8），
// 顶点着色器只取 2×vec4 预蒙皮顶点 + 实例变换（binding 3）。
// 片元合同见 gpu_crowd_pbr.fs（PBR + tint + 阴影 PCF 接收）。
layout (location = 1) in vec2 vertexTexCoord;

layout(std430, binding = 3) readonly buffer InstanceBlock { vec4 instanceData[]; };
layout(std430, binding = 8) readonly buffer SkinnedVerts { vec4 skinned[]; };

uniform mat4 mvp;
uniform int uPreskinVertexCount;
uniform float uTime;
uniform float uWanderScale;   // 0 = 模拟层已写最终变换，1 = GPU 确定性游走

// ludo:include crowd_wander.glsl.inc

out vec2 fragTexCoord;
out vec3 fragNormal;
out vec3 fragColor;
out vec3 fragWorldPos;

void main()
{
    int instanceVec4 = gl_InstanceID * 4;
    vec4 tC0 = instanceData[instanceVec4 + 0];
    vec4 tC1 = instanceData[instanceVec4 + 1];
    vec4 tC2 = instanceData[instanceVec4 + 2];
    vec4 inst = instanceData[instanceVec4 + 3];
    mat4 instanceTransform = mat4(
        vec4(tC0.xyz, 0.0), vec4(tC1.xyz, 0.0), vec4(tC2.xyz, 0.0),
        vec4(tC0.w, tC1.w, tC2.w, 1.0));

    // 游走：位置偏移 + 朝向对齐轨迹切线（实例表第 4 个 vec4 的 .w = 源索引）
    float heading;
    vec3 wander = CrowdWanderOffset(int(inst.w + 0.5), uTime, heading) * uWanderScale;
    float baseYaw = atan(tC2.x, tC2.z);
    float dy = (heading - baseYaw) * uWanderScale;
    float cs = cos(dy), sn = sin(dy);
    mat3 rotY = mat3(cs, 0.0, -sn, 0.0, 1.0, 0.0, sn, 0.0, cs);
    mat3 rotatedTransform = rotY * mat3(instanceTransform);
    instanceTransform = mat4(
        vec4(rotatedTransform[0], 0.0),
        vec4(rotatedTransform[1], 0.0),
        vec4(rotatedTransform[2], 0.0),
        vec4(tC0.w + wander.x, tC1.w + wander.y, tC2.w + wander.z, 1.0));

    int poseRow = int(inst.x + 0.5);
    int packedRgb = int(inst.y + 0.5);
    vec3 tint = vec3(
        float(packedRgb % 256),
        float((packedRgb / 256) % 256),
        float((packedRgb / 65536) % 256)) / 255.0;

    int skinnedBase = (poseRow * uPreskinVertexCount + gl_VertexID) * 2;
    vec3 skinnedPos = skinned[skinnedBase + 0].xyz;
    vec3 skinnedNrm = skinned[skinnedBase + 1].xyz;

    vec4 worldPos = instanceTransform * vec4(skinnedPos, 1.0);
    fragTexCoord = vertexTexCoord;
    fragNormal = normalize(mat3(instanceTransform) * skinnedNrm);
    fragColor = tint;
    fragWorldPos = worldPos.xyz;
    gl_Position = mvp * worldPos;
}
