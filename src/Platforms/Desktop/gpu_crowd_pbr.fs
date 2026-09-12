#version 430

// GPU crowd PBR：与 skinning_instanced.fs 同合同（GGX 直射光 + split-sum IBL：
// 天顶/地面半球辐照 + 预滤波环境立方图×BRDF LUT + 距离雾 + 阴影 PCF）。
// albedo = GLB 贴图 × 实例 tint；无 UV/无贴图的 LOD（uHasAlbedoMap=0）走平色 albedo = tint。

in vec2 fragTexCoord;
in vec3 fragNormal;
in vec3 fragColor;
in vec3 fragWorldPos;

uniform sampler2D uAlbedoMap;
uniform int uHasAlbedoMap;
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
    vec4 texel = uHasAlbedoMap == 1 ? texture(uAlbedoMap, fragTexCoord) : vec4(1.0);
    vec3 albedo = texel.rgb * fragColor;
    float roughness = clamp(uRoughness, MIN_ROUGHNESS, 1.0);
    float metallic = clamp(uMetallic, 0.0, 1.0);

    vec3 N = normalize(fragNormal);
    vec3 V = normalize(uViewPos - fragWorldPos);
    vec3 L = normalize(uLightDir);
    vec3 H = normalize(V + L);

    float NdotL = max(dot(N, L), 0.0);
    vec3 F0 = mix(vec3(0.04), albedo, metallic);
    float D = DistributionGGX(N, H, roughness);
    float G = GeometrySmith(N, V, L, roughness);
    vec3 F = FresnelSchlick(max(dot(H, V), 0.0), F0);
    vec3 specular = (D * G * F) / max(4.0 * max(dot(N, V), 0.0) * NdotL, 1e-5);

    // split-sum IBL：半球近似环境漫反射 + 预滤波环境立方图（roughness→lod 6 级）× BRDF LUT
    float hemisphere = N.y * 0.5 + 0.5;
    vec3 skyIrradiance = mix(uSkyGround, uSkyZenith, hemisphere);
    vec3 ambientDiffuse = skyIrradiance * albedo * (1.0 - metallic);
    vec3 prefilteredEnv = textureLod(uPrefilteredEnv, reflect(-V, N), roughness * 6.0).rgb;
    vec2 brdf = texture(uBrdfLut, vec2(max(dot(N, V), 0.0), roughness)).rg;
    vec3 ambientSpecular = prefilteredEnv * (F0 * brdf.x + vec3(brdf.y)) * uEnvSpecular;
    vec3 ambient = ambientDiffuse + ambientSpecular + uAmbient.rgb * uAmbient.a * albedo;

    vec3 kS = F;
    vec3 kD = (vec3(1.0) - kS) * (1.0 - metallic);
    vec3 radiance = uLightColor * uLightIntensity;
    float shadow = SampleShadow(fragWorldPos, N);
    vec3 lit = ambient + (kD * albedo / PI + specular) * radiance * NdotL * shadow;

    float fogAmount = DistanceFogAmount(length(fragWorldPos - uViewPos));
    vec3 fogged = mix(lit, uFogColor, fogAmount);
    finalColor = vec4(clamp(fogged, 0.0, 1.0), 1.0);
}
