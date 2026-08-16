#version 330

// Shares the lit directional MR uniform contract with instancing.fs when locs exist
// (uRoughness / uMetallic / uHasRoughnessMap / uHasMetallicMap + texture1/texture3).
// Normal maps are host-bindable but not sampled without mesh tangents/TBN.

in vec2 fragTexCoord;
in vec4 fragColor;
in vec3 fragNormal;
in vec3 fragPos;

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

out vec4 finalColor;

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

void main()
{
    vec4 texel = texture(texture0, fragTexCoord);
    vec4 color = fragColor;
    if (color.a <= 0.001)
    {
        color = vec4(1.0);
    }
    vec4 albedoSample = texel * colDiffuse * tint * color;
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

    vec3 kS = F;
    vec3 kD = (vec3(1.0) - kS) * (1.0 - metallic);
    vec3 radiance = uLightColor * uLightIntensity;
    vec3 ambient = uAmbient.rgb * uAmbient.a * albedo;
    vec3 lit = ambient + (kD * albedo / PI + specular) * radiance * NdotL;

    float fogAmount = DistanceFogAmount(length(fragPos - uViewPos));
    vec3 fogged = mix(lit, uFogColor, fogAmount);
    finalColor = vec4(clamp(fogged, 0.0, 1.0), albedoSample.a);
}
