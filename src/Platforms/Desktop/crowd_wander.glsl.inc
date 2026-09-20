// 实例游走合同：cull compute（视锥/LOD/阴影半径的包围球位置）与三个绘制 VS（主/阴影深度/billboard）
// 共用同一确定性游走——同一实例在同一帧的世界位置与朝向处处一致。
// 依赖：包含方提供 uniform float uTime；实例源索引来自实例表第 4 个 vec4 的 .w（CPU 打包）。

vec2 CrowdHash(int id)
{
    float h1 = fract(sin(float(id) * 12.9898) * 43758.5453);
    float h2 = fract(sin(float(id) * 78.233) * 24634.6345);
    return vec2(h1, h2);
}

// 圆周漫步：绕驻留点的小圆轨迹，朝向 = 轨迹切线（人群自然散布，无穿插大位移）
vec3 CrowdWanderOffset(int id, float t, out float heading)
{
    vec2 h = CrowdHash(id);
    float phase = h.x * 6.28318530718;
    float omega = 0.25 + h.y * 0.55;                    // 角速度 rad/s
    float radius = 1.2 + fract(h.x * 7.13) * 2.4;       // 半径 m
    float a = phase + t * omega;
    heading = a + 1.57079632679;
    return vec3(cos(a) * radius, 0.0, sin(a) * radius);
}
