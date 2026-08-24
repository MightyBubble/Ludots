#version 330

in vec3 fragPos;
in vec3 fragNormal;
in vec4 fragColor;
in float fragHeightBand;

uniform vec3 uLightDir;
uniform vec4 uAmbient;
uniform vec3 uLightColor;
uniform float uLightIntensity;
uniform vec3 uViewPos;
uniform vec3 uFogColor;
uniform vec4 uFogParams;
uniform vec3 uSkyZenith;
uniform vec3 uSkyGround;
uniform int uUseTerrainAlbedo;
uniform float uTerrainTileScale;
uniform int uAntiTile;
uniform int uUseControlMap;
uniform vec4 uControlBounds;
uniform sampler2D texture0;
uniform sampler2D texture1;
uniform sampler2D texture2;
uniform sampler2D texture3;
uniform sampler2D uControlMap;
// ludo:include shadow_sampling.glsl.inc

out vec4 finalColor;

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

float InterleavedGradientNoise(vec2 n)
{
    return fract(52.9829189 * fract(dot(n, vec2(0.06711056, 0.00583715))));
}

vec2 Hash2(vec2 p)
{
    vec3 p3 = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.xx + p3.yz) * p3.zy);
}

// IQ / Héctor: 2×2 hash-rotated UV samples blended with IGN-softened weights.
vec3 SampleAlbedoAntiTiled(sampler2D samp, vec2 uv)
{
    if (uAntiTile == 0)
    {
        return texture(samp, uv).rgb;
    }

    vec2 iuv = floor(uv);
    vec2 fuv = fract(uv);
    vec2 blend = fuv * fuv * (3.0 - 2.0 * fuv);

    vec3 accum = vec3(0.0);
    float wSum = 0.0;
    for (int y = 0; y < 2; y++)
    {
        for (int x = 0; x < 2; x++)
        {
            vec2 cell = iuv + vec2(float(x), float(y));
            vec2 h = Hash2(cell);
            float ang = h.x * 6.28318530718;
            float ca = cos(ang);
            float sa = sin(ang);
            mat2 rot = mat2(ca, -sa, sa, ca);
            // Larger per-cell translate + mild scale break repeating lattice from aerial.
            float cellScale = mix(0.82, 1.28, h.y);
            vec2 sampleUv = rot * ((uv + h * 19.7) * cellScale);

            float wx = (x == 0) ? (1.0 - blend.x) : blend.x;
            float wy = (y == 0) ? (1.0 - blend.y) : blend.y;
            float ign = InterleavedGradientNoise(cell + fuv * 17.0);
            float w = wx * wy * mix(0.85, 1.15, ign);
            accum += texture(samp, sampleUv).rgb * w;
            wSum += w;
        }
    }

    return accum / max(wSum, 1e-5);
}

vec3 BlendLayerAlbedos(vec2 uv, vec4 weights)
{
    vec3 sand = SampleAlbedoAntiTiled(texture0, uv);
    vec3 grass = SampleAlbedoAntiTiled(texture1, uv);
    vec3 dirt = SampleAlbedoAntiTiled(texture2, uv);
    vec3 rock = SampleAlbedoAntiTiled(texture3, uv);
    float sum = max(weights.r + weights.g + weights.b + weights.a, 1e-5);
    vec4 w = weights / sum;
    return sand * w.r + grass * w.g + dirt * w.b + rock * w.a;
}

vec4 HeightBandWeights(float h)
{
    // Bands mirror ResolveAbsoluteIslandTerrainColor land stops (sand→grass→dirt→rock).
    float wSand = 1.0 - smoothstep(0.0, 0.045, h);
    float wGrass = smoothstep(0.0, 0.045, h) * (1.0 - smoothstep(0.045, 0.32, h));
    float wDirt = smoothstep(0.045, 0.32, h) * (1.0 - smoothstep(0.32, 0.58, h));
    float wRock = smoothstep(0.32, 0.58, h);
    return vec4(wSand, wGrass, wDirt, wRock);
}

vec4 SampleControlWeights(vec3 worldPos)
{
    vec2 size = max(uControlBounds.zw, vec2(1e-5));
    vec2 uv = (worldPos.xz - uControlBounds.xy) / size;
    uv = clamp(uv, vec2(0.0), vec2(1.0));
    vec4 w = texture(uControlMap, uv);
    float sum = max(w.r + w.g + w.b + w.a, 1e-5);
    return w / sum;
}


void main()
{
    vec3 albedo = fragColor.rgb;
    if (uUseTerrainAlbedo != 0)
    {
        float scale = max(uTerrainTileScale, 1e-5);
        vec2 uv = fragPos.xz * scale;
        vec4 weights = (uUseControlMap != 0)
            ? SampleControlWeights(fragPos)
            : HeightBandWeights(clamp(fragHeightBand, 0.0, 1.0));
        vec3 textured = BlendLayerAlbedos(uv, weights);
        // Keep biome tint without crushing tiling detail (dark rock vertex RGB was washing maps flat).
        albedo = textured * (0.55 + 0.45 * fragColor.rgb);
    }

    vec3 N = normalize(fragNormal);
    vec3 L = normalize(uLightDir);
    vec3 V = normalize(uViewPos - fragPos);
    vec3 H = normalize(L + V);
    float ndl = max(dot(N, L), 0.0);
    // Single-light Blinn highlight (not metallic-roughness PBR). Rock/peak bands get a bit more gloss.
    float gloss = mix(0.04, 0.18, smoothstep(0.32, 0.85, clamp(fragHeightBand, 0.0, 1.0)));
    float spec = pow(max(dot(N, H), 0.0), 48.0) * gloss * step(0.02, ndl);
    float shadow = SampleShadow(fragPos, N);
    // Hemisphere irradiance stays outside albedo so lit = albedo * (ambient + direct)
    // matches model_lit's sky+ramp fill without squaring the terrain tint.
    float hemisphere = N.y * 0.5 + 0.5;
    vec3 skyIrradiance = mix(uSkyGround, uSkyZenith, hemisphere);
    vec3 ambient = skyIrradiance + (uAmbient.rgb * uAmbient.a);
    vec3 direct = uLightColor * uLightIntensity * ndl * shadow;
    vec3 lit = albedo * (ambient + direct) + (uLightColor * uLightIntensity * spec * shadow);
    float fogAmount = DistanceFogAmount(length(fragPos - uViewPos));
    vec3 fogged = mix(lit, uFogColor, fogAmount);
    finalColor = vec4(clamp(fogged, 0.0, 1.0), 1.0);
}
