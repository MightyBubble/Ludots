#version 430
#extension GL_ARB_shader_storage_buffer_object : require

// Imposter 图集 capture VS：把姿势行 p 的预蒙皮低模以正交视角画进图集 cell。
// 实例变换恒等（图集存"原点朝 +Z 的人"）；世界法线直接透传（运行时按实例朝向旋转）。
layout(std430, binding = 8) readonly buffer SkinnedVerts { vec4 skinned[]; };

uniform mat4 mvp;
uniform int uPreskinVertexCount;
uniform int uPhaseRow;

out vec3 captureNormal;

void main()
{
    int skinnedBase = (uPhaseRow * uPreskinVertexCount + gl_VertexID) * 2;
    vec3 skinnedPos = skinned[skinnedBase + 0].xyz;
    captureNormal = skinned[skinnedBase + 1].xyz;
    gl_Position = mvp * vec4(skinnedPos, 1.0);
}
