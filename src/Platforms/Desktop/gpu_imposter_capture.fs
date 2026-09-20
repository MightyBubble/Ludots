#version 430

// Imposter 图集 capture FS：RGB = 世界法线（0..1 编码），A = 覆盖率（背景 0）。
// 运行时按 (phase, viewYaw) 选 cell，覆盖率做 alpha-test，法线按实例朝向旋回世界参与 PBR。
in vec3 captureNormal;

out vec4 finalColor;

void main()
{
    vec3 n = normalize(captureNormal);
    finalColor = vec4(n * 0.5 + 0.5, 1.0);
}
