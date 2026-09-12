#version 430
#extension GL_ARB_shader_storage_buffer_object : require

// 阴影深度预蒙皮路径：位置取自预蒙皮 SSBO（与主 pass 严格同源），
// 只乘实例变换与光空间 mvp——packed 深度输出合同见 shadow_depth.fs。
layout(std430, binding = 3) readonly buffer InstanceBlock { vec4 instanceData[]; };
layout(std430, binding = 8) readonly buffer SkinnedVerts { vec4 skinned[]; };

uniform mat4 mvp;
uniform int uPreskinVertexCount;
uniform float uTime;

// ludo:include crowd_wander.glsl.inc

void main()
{
    int instanceVec4 = gl_InstanceID * 4;
    vec4 tC0 = instanceData[instanceVec4 + 0];
    vec4 tC1 = instanceData[instanceVec4 + 1];
    vec4 tC2 = instanceData[instanceVec4 + 2];
    int poseRow = int(instanceData[instanceVec4 + 3].x + 0.5);

    int skinnedBase = (poseRow * uPreskinVertexCount + gl_VertexID) * 2;
    vec3 skinnedPos = skinned[skinnedBase + 0].xyz;

    // 与主 pass 同一游走合同（阴影深度与可见位置严格一致，否则阴影脱靶）
    float heading;
    vec3 wander = CrowdWanderOffset(int(instanceData[instanceVec4 + 3].w + 0.5), uTime, heading);
    vec3 local = vec3(
        dot(tC0.xyz, skinnedPos),
        dot(tC1.xyz, skinnedPos),
        dot(tC2.xyz, skinnedPos));
    float baseYaw = atan(tC2.x, tC2.z);
    float dy = heading - baseYaw;
    float cs = cos(dy), sn = sin(dy);
    vec3 rotated = vec3(cs * local.x + sn * local.z, local.y, -sn * local.x + cs * local.z);
    vec3 worldPos = rotated + vec3(tC0.w + wander.x, tC1.w + wander.y, tC2.w + wander.z);
    gl_Position = mvp * vec4(worldPos, 1.0);
}
