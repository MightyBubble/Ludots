#version 330

in vec2 fragTexCoord;
in vec3 fragNormal;
in vec3 fragPos;
out vec4 finalColor;

uniform sampler2D texture0;
uniform sampler2D texture1;
uniform sampler2D texture3;
uniform vec4 colDiffuse;
uniform vec4 tint;
uniform vec3 uLightDir;
uniform vec4 uAmbient;
uniform vec3 uLightColor;
uniform float uLightIntensity;
uniform vec3 uViewPos;
uniform vec3 uFogColor;
uniform vec4 uFogParams;
uniform float uRoughness;
uniform float uMetallic;
uniform int uHasRoughnessMap;
uniform int uHasMetallicMap;
uniform vec3 uSkyZenith;
uniform vec3 uSkyGround;
uniform samplerCube uPrefilteredEnv;
uniform sampler2D uBrdfLut;
uniform float uEnvSpecular;
uniform sampler2D uShadowMap;
uniform mat4 uLightSpaceMatrix;
uniform float uShadowEnabled;
uniform float uShadowTexelWorld;

const float PI = 3.14159265359;
const float MIN_ROUGHNESS = 0.04;

float DistanceFogAmount(float dist)
{
    if (uFogParams.w < 0.5)
    {
        return 0.0;
    }

    float start = uFogParams.y;
    float end = uFogParams.z;
    float linear = clamp((dist - start) / max(end - start, 1e-5), 0.0, 1.0);
    float density = uFogParams.x;
    if (density <= 0.0)
    {
        return linear;
    }

    float d = max(dist - start, 0.0);
    float expFog = 1.0 - exp(-density * d);
    return clamp(max(linear, expFog), 0.0, 1.0);
}

float DistributionGGX(vec3 N, vec3 H, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;
    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    return a2 / max(PI * denom * denom, 1e-6);
}

float GeometrySchlickGGX(float NdotX, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) / 8.0;
    return NdotX / max(NdotX * (1.0 - k) + k, 1e-6);
}

float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
{
    return GeometrySchlickGGX(max(dot(N, V), 0.0), roughness) *
           GeometrySchlickGGX(max(dot(N, L), 0.0), roughness);
}

vec3 FresnelSchlick(float cosTheta, vec3 F0)
{
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

float UnpackDepth(vec4 packed)
{
    return dot(packed.rgb, vec3(1.0, 1.0 / 255.0, 1.0 / 65025.0));
}

float SampleShadow(vec3 worldPos, vec3 N)
{
    if (uShadowEnabled < 0.5)
    {
        return 1.0;
    }

    vec3 offsetPos = worldPos + N * uShadowTexelWorld;
    vec4 lightSpace = uLightSpaceMatrix * vec4(offsetPos, 1.0);
    vec3 proj = lightSpace.xyz / max(lightSpace.w, 1e-6);
    proj = proj * 0.5 + 0.5;
    if (proj.x < 0.0 || proj.x > 1.0 || proj.y < 0.0 || proj.y > 1.0 || proj.z > 1.0)
    {
        return 1.0;
    }

    float receiverDepth = proj.z;
    float texel = 1.0 / 2048.0;
    vec2 shadowUv = proj.xy;
    float lit = 0.0;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float stored = UnpackDepth(texture(uShadowMap, shadowUv + vec2(x, y) * texel));
            lit += receiverDepth <= stored + 0.004 ? 1.0 : 0.0;
        }
    }

    return lit / 9.0;
}

void main()
{
    vec4 albedoSample = texture(texture0, fragTexCoord) * colDiffuse * tint;
    vec3 albedo = albedoSample.rgb;

    float roughness = uRoughness;
    if (uHasRoughnessMap == 1)
    {
        roughness = texture(texture3, fragTexCoord).r;
    }
    roughness = clamp(roughness, MIN_ROUGHNESS, 1.0);

    float metallic = uMetallic;
    if (uHasMetallicMap == 1)
    {
        metallic = texture(texture1, fragTexCoord).r;
    }
    metallic = clamp(metallic, 0.0, 1.0);

    // Normal maps may be host-bound (texture2 / MATERIAL_MAP_NORMAL) but are not sampled here:
    // instancing.vs and GenMeshCube/Sphere ISM meshes do not provide tangents/TBN.
    vec3 N = normalize(fragNormal);
    vec3 V = normalize(uViewPos - fragPos);
    vec3 L = normalize(uLightDir);
    vec3 H = normalize(V + L);

    float NdotL = max(dot(N, L), 0.0);
    vec3 F0 = mix(vec3(0.04), albedo, metallic);
    float D = DistributionGGX(N, H, roughness);
    float G = GeometrySmith(N, V, L, roughness);
    vec3 F = FresnelSchlick(max(dot(H, V), 0.0), F0);
    vec3 specular = (D * G * F) / max(4.0 * max(dot(N, V), 0.0) * NdotL, 1e-5);

    // split-sum IBL：半球近似环境漫反射（天顶/地面按法线混合）+ 预滤波环境立方图
    // （CPU 烘焙 GGX mip 链，roughness→lod=6 级）× BRDF LUT 环境镜面；与 model_lit 同合同。
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
    float shadow = SampleShadow(fragPos, N);
    vec3 lit = ambient + (kD * albedo / PI + specular) * radiance * NdotL * shadow;

    float fogAmount = DistanceFogAmount(length(fragPos - uViewPos));
    vec3 fogged = mix(lit, uFogColor, fogAmount);

    finalColor = vec4(clamp(fogged, 0.0, 1.0), albedoSample.a);
}
