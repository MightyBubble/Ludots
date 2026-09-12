#version 430

// Imposter billboard FS：图集 RGB = 世界法线（capture 时人物朝向 yaw 0），
// 按实例朝向旋回世界后走与 gpu_crowd_pbr 同合同的 PBR 光照 + PCF 阴影 + 距离雾。

in vec2 fragAtlasUv;
in vec3 fragColor;
in vec3 fragWorldPos;
in vec2 fragYawCosSin;

uniform sampler2D uImposterAtlas;
uniform float uRoughness;
uniform float uMetallic;
uniform vec3 uLightDir;
uniform vec3 uLightColor;
uniform float uLightIntensity;
uniform vec4 uAmbient;
uniform vec3 uViewPos;
uniform vec3 uFogColor;
uniform vec4 uFogParams;
uniform vec3 uSkyZenith;
uniform vec3 uSkyGround;
uniform samplerCube uPrefilteredEnv;
uniform sampler2D uBrdfLut;
uniform float uEnvSpecular;
// ludo:include pbr_terms.glsl.inc
// ludo:include shadow_sampling.glsl.inc

out vec4 finalColor;

const float MIN_ROUGHNESS = 0.04;

void main()
{
    vec4 cell = texture(uImposterAtlas, fragAtlasUv);
    if (cell.a < 0.5)
    {
        discard;
    }

    // 图集法线（yaw 0 空间）→ 世界：绕 Y 旋转 fragYawCosSin
    vec3 n0 = cell.rgb * 2.0 - 1.0;
    vec3 N = normalize(vec3(
        fragYawCosSin.y * n0.x + fragYawCosSin.x * n0.z,
        n0.y,
        -fragYawCosSin.y * n0.z + fragYawCosSin.x * n0.x));

    vec3 albedo = fragColor;
    float roughness = clamp(uRoughness, MIN_ROUGHNESS, 1.0);
    float metallic = clamp(uMetallic, 0.0, 1.0);

    vec3 V = normalize(uViewPos - fragWorldPos);
    vec3 L = normalize(uLightDir);
    vec3 H = normalize(V + L);

    float NdotL = max(dot(N, L), 0.0);
    vec3 F0 = mix(vec3(0.04), albedo, metallic);
    float D = DistributionGGX(N, H, roughness);
    float G = GeometrySmith(N, V, L, roughness);
    vec3 F = FresnelSchlick(max(dot(H, V), 0.0), F0);
    vec3 specular = (D * G * F) / max(4.0 * max(dot(N, V), 0.0) * NdotL, 1e-5);

    // 远景合同：环境光走半球近似（IBL specular 与 BRDF LUT 在 120m+ 的 16px billboard 上不可辨，省两次采样）
    float hemisphere = N.y * 0.5 + 0.5;
    vec3 skyIrradiance = mix(uSkyGround, uSkyZenith, hemisphere);
    vec3 ambient = skyIrradiance * albedo * (1.0 - metallic) + uAmbient.rgb * uAmbient.a * albedo;

    vec3 kS = F;
    vec3 kD = (vec3(1.0) - kS) * (1.0 - metallic);
    vec3 radiance = uLightColor * uLightIntensity;
    // 远景合同：单 tap 阴影（9-tap PCF 在该尺度不可辨）
    float shadow = 1.0;
    if (uShadowEnabled >= 0.5)
    {
        vec3 offsetPos = fragWorldPos + N * uShadowTexelWorld;
        vec4 lightSpace = uLightSpaceMatrix * vec4(offsetPos, 1.0);
        vec3 proj = lightSpace.xyz / max(lightSpace.w, 1e-6);
        proj = proj * 0.5 + 0.5;
        if (proj.x > 0.0 && proj.x < 1.0 && proj.y > 0.0 && proj.y < 1.0 && proj.z <= 1.0)
        {
            float stored = UnpackDepth(texture(uShadowMap, proj.xy));
            shadow = proj.z <= stored + uShadowBias ? 1.0 : 0.35;
        }
    }
    vec3 lit = ambient + (kD * albedo / PI + specular) * radiance * NdotL * shadow;

    float fogAmount = DistanceFogAmount(length(fragWorldPos - uViewPos));
    vec3 fogged = mix(lit, uFogColor, fogAmount);
    finalColor = vec4(clamp(fogged, 0.0, 1.0), 1.0);
}
