#version 430
#extension GL_ARB_shader_storage_buffer_object : require

// Imposter billboard VS：相机朝向四边形，按实例相位行 + 相对视角选图集 cell。
// 实例紧凑化表 binding 3（cull shader which=3 写出）；gl_VertexID 0..3 即四角
// （索引 0,1,2, 0,2,3 → 角序：左下/右下/右上/左上，脚在实例原点）。
layout(std430, binding = 3) readonly buffer InstanceBlock { vec4 instanceData[]; };

uniform mat4 mvp;
uniform vec3 uCamPos;
uniform vec3 uCamRight;
uniform vec3 uCamUp;
uniform vec2 uImposterSize;    // x = 半宽，y = 全高
uniform float uViewDirs;       // 视角桶数（图集行数）
uniform float uPhaseRows;      // 相位桶数（图集列数）
uniform float uTime;

// ludo:include crowd_wander.glsl.inc

out vec2 fragAtlasUv;
out vec3 fragColor;
out vec3 fragWorldPos;
out vec2 fragYawCosSin;

const vec2 CORNERS[4] = vec2[4](
    vec2(-0.5, 0.0),
    vec2(0.5, 0.0),
    vec2(0.5, 1.0),
    vec2(-0.5, 1.0));

void main()
{
    int instanceVec4 = gl_InstanceID * 4;
    vec4 tC0 = instanceData[instanceVec4 + 0];
    vec4 tC1 = instanceData[instanceVec4 + 1];
    vec4 tC2 = instanceData[instanceVec4 + 2];
    vec4 inst = instanceData[instanceVec4 + 3];
    vec3 pos = vec3(tC0.w, tC1.w, tC2.w);

    int poseRow = int(inst.x + 0.5);
    int packedRgb = int(inst.y + 0.5);
    vec3 tint = vec3(
        float(packedRgb % 256),
        float((packedRgb / 256) % 256),
        float((packedRgb / 65536) % 256)) / 255.0;

    // 朝向 = 游走轨迹切线（与网格路径旋转一致），位置 = 驻留点 + 游走
    float heading;
    vec3 wander = CrowdWanderOffset(int(inst.w + 0.5), uTime, heading);
    pos += wander;
    float yaw = heading;
    fragYawCosSin = vec2(cos(yaw), sin(yaw));

    // 相对视角 = 相机→实例方位角 - 实例朝向；量化到图集行（capture 时以 yaw 0 正对 θ_k 视角渲染）
    float viewAzimuth = atan(pos.x - uCamPos.x, pos.z - uCamPos.z);
    float relative = viewAzimuth - yaw;
    float dir = floor((relative / 6.28318530718 + 0.5) * uViewDirs + 0.5);
    int viewDir = int(mod(dir, uViewDirs));

    vec2 corner = CORNERS[gl_VertexID];
    vec3 worldPos = pos + uCamRight * (corner.x * uImposterSize.x * 2.0) + uCamUp * (corner.y * uImposterSize.y);

    // 图集 cell：列 = 相位行，行 = 视角桶；cell 内 uv（y=0 在 cell 底部 = 脚）。
    // 采样向 cell 中心收半 texel——双线性过滤不越界渗到相邻 cell（0.5/128 = 128px cell 的半 texel）
    const float halfTexel = 0.5 / 128.0;
    float cellU = mix(halfTexel, 1.0 - halfTexel, corner.x + 0.5);
    float cellV = mix(halfTexel, 1.0 - halfTexel, corner.y);
    fragAtlasUv = vec2(
        (float(poseRow) + cellU) / uPhaseRows,
        (float(viewDir) + cellV) / uViewDirs);
    fragColor = tint;
    fragWorldPos = worldPos;
    gl_Position = mvp * vec4(worldPos, 1.0);
}
