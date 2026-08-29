#version 330

// 外部文件模型（glTF/GLB/OBJ）逐材质 PBR 通道：在 model_lit 的 GGX + split-sum IBL + 阴影
// 词汇之外，采样 glTF 装载器放进材质槽的贴图——albedo(texture0)、法线(NORMAL 槽)、
// ORM(METALNESS/ROUGHNESS 槽同指 uOrmMap：raylib 5.5 把 metallicRoughness 贴图放在
// ROUGHNESS 槽)、自发光(EMISSION 槽)。阴影深度纹理走 HEIGHT 槽（glTF 不使用该槽；
// EMISSION 槽在 model_lit 中挂阴影的仓库约定在这里让位给 glTF 自发光贴图）。
// uViewMode/uScalarOverride 服务资产验收：拆开最终光照看单个通道，以及"贴图 PBR vs
// 引擎缺省标量 PBR"的消融对照。法线贴图无切线属性时用屏幕空间导数 TBN。

in vec2 fragTexCoord;
in vec3 fragNormal;
in vec3 fragPos;
out vec4 finalColor;

uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform vec4 tint;
uniform sampler2D uNormalMap;
uniform sampler2D uOrmMap;
uniform sampler2D uEmissiveMap;
uniform float uHasNormal;
uniform float uHasOrm;
uniform float uHasEmissive;
uniform float uRoughness;
uniform float uMetallic;
uniform float uScalarOverride;
uniform int uViewMode;
uniform float uAlphaCutoff;
uniform vec3 uLightDir;
uniform vec4 uAmbient;
uniform vec3 uLightColor;
uniform float uLightIntensity;
uniform vec3 uViewPos;
uniform vec3 uFogColor;
uniform vec4 uFogParams;
uniform vec3 uSkyZenith;
uniform vec3 uSkyGround;
uniform samplerCube uPrefilteredEnv;
uniform sampler2D uBrdfLut;
uniform float uEnvSpecular;
// ludo:include shadow_sampling.glsl.inc

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

vec3 PerturbNormalArb(vec3 N)
{
    // 屏幕空间导数 TBN（Followup: Normal Mapping Without Precomputed Tangents）：
    // glTF 装载器不保证顶点 tangent 属性，验收显示走无切线法线贴图路径。
    vec3 map = texture(uNormalMap, fragTexCoord).xyz * 2.0 - 1.0;
    vec3 dp1 = dFdx(fragPos);
    vec3 dp2 = dFdy(fragPos);
    vec2 duv1 = dFdx(fragTexCoord);
    vec2 duv2 = dFdy(fragTexCoord);
    vec3 dp2perp = cross(dp2, N);
    vec3 dp1perp = cross(N, dp1);
    vec3 T = dp2perp * duv1.x + dp1perp * duv2.x;
    vec3 B = dp2perp * duv1.y + dp1perp * duv2.y;
    float invmax = inversesqrt(max(dot(T, T) + dot(B, B), 1e-8));
    return normalize(mat3(T * invmax, B * invmax, N) * map);
}

void main()
{
    vec4 albedoSample = texture(texture0, fragTexCoord) * colDiffuse * tint;
    float alpha = albedoSample.a;
    if (alpha < uAlphaCutoff)
    {
        discard;
    }

    vec3 albedo = albedoSample.rgb;

    bool mapPbr = (uScalarOverride < 0.5) && (uHasOrm > 0.5);
    vec3 orm = mapPbr ? texture(uOrmMap, fragTexCoord).rgb : vec3(1.0, uRoughness, uMetallic);
    float occlusion = orm.r;
    float roughness = clamp(orm.g * uRoughness, MIN_ROUGHNESS, 1.0);
    float metallic = clamp(orm.b * uMetallic, 0.0, 1.0);

    vec3 N = normalize(fragNormal);
    if (!gl_FrontFacing)
    {
        N = -N;
    }

    vec3 V = normalize(uViewPos - fragPos);

    if (uViewMode == 1)
    {
        finalColor = vec4(albedo, alpha);
        return;
    }
    if (uViewMode == 2)
    {
        if ((uScalarOverride < 0.5) && (uHasNormal > 0.5))
        {
            N = PerturbNormalArb(N);
        }
        finalColor = vec4(N * 0.5 + 0.5, 1.0);
        return;
    }
    if (uViewMode == 3)
    {
        finalColor = vec4(vec3(metallic), 1.0);
        return;
    }
    if (uViewMode == 4)
    {
        finalColor = vec4(vec3(roughness), 1.0);
        return;
    }

    if ((uScalarOverride < 0.5) && (uHasNormal > 0.5))
    {
        N = PerturbNormalArb(N);
    }

    vec3 L = normalize(uLightDir);
    vec3 H = normalize(V + L);

    float NdotL = max(dot(N, L), 0.0);
    vec3 F0 = mix(vec3(0.04), albedo, metallic);
    float D = DistributionGGX(N, H, roughness);
    float G = GeometrySmith(N, V, L, roughness);
    vec3 F = FresnelSchlick(max(dot(H, V), 0.0), F0);
    vec3 specular = (D * G * F) / max(4.0 * max(dot(N, V), 0.0) * NdotL, 1e-5);

    float hemisphere = N.y * 0.5 + 0.5;
    vec3 skyIrradiance = mix(uSkyGround, uSkyZenith, hemisphere);
    vec3 ambientDiffuse = skyIrradiance * albedo * (1.0 - metallic);
    vec3 prefilteredEnv = textureLod(uPrefilteredEnv, reflect(-V, N), roughness * 6.0).rgb;
    vec2 brdf = texture(uBrdfLut, vec2(max(dot(N, V), 0.0), roughness)).rg;
    vec3 ambientSpecular = prefilteredEnv * (F0 * brdf.x + vec3(brdf.y)) * uEnvSpecular;
    vec3 ambient = (ambientDiffuse + ambientSpecular + uAmbient.rgb * uAmbient.a * albedo) * occlusion;

    vec3 kS = F;
    vec3 kD = (vec3(1.0) - kS) * (1.0 - metallic);
    vec3 radiance = uLightColor * uLightIntensity;
    float shadow = SampleShadow(fragPos, N);
    vec3 litColor = ambient + (kD * albedo / PI + specular) * radiance * NdotL * shadow;

    if ((uScalarOverride < 0.5) && (uHasEmissive > 0.5))
    {
        litColor += texture(uEmissiveMap, fragTexCoord).rgb;
    }

    float fogAmount = DistanceFogAmount(length(fragPos - uViewPos));
    vec3 fogged = mix(litColor, uFogColor, fogAmount);
    finalColor = vec4(clamp(fogged, 0.0, 1.0), alpha);
}
