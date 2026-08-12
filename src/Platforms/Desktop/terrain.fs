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
uniform int uUseTerrainAlbedo;
uniform float uTerrainTileScale;
uniform sampler2D texture0;
uniform sampler2D texture1;
uniform sampler2D texture2;
uniform sampler2D texture3;

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

vec3 SampleHeightBandAlbedo(float h, vec2 uv)
{
    vec3 sand = texture(texture0, uv).rgb;
    vec3 grass = texture(texture1, uv).rgb;
    vec3 dirt = texture(texture2, uv).rgb;
    vec3 rock = texture(texture3, uv).rgb;

    // Bands mirror ResolveAbsoluteIslandTerrainColor land stops (sand→grass→dirt→rock).
    float wSand = 1.0 - smoothstep(0.0, 0.045, h);
    float wGrass = smoothstep(0.0, 0.045, h) * (1.0 - smoothstep(0.045, 0.32, h));
    float wDirt = smoothstep(0.045, 0.32, h) * (1.0 - smoothstep(0.32, 0.58, h));
    float wRock = smoothstep(0.32, 0.58, h);
    float sum = max(wSand + wGrass + wDirt + wRock, 1e-5);
    return (sand * wSand + grass * wGrass + dirt * wDirt + rock * wRock) / sum;
}

void main()
{
    vec3 albedo = fragColor.rgb;
    if (uUseTerrainAlbedo != 0)
    {
        float scale = max(uTerrainTileScale, 1e-5);
        vec2 uv = fragPos.xz * scale;
        vec3 textured = SampleHeightBandAlbedo(clamp(fragHeightBand, 0.0, 1.0), uv);
        albedo = textured * fragColor.rgb;
    }

    vec3 N = normalize(fragNormal);
    vec3 L = normalize(uLightDir);
    float ndl = max(dot(N, L), 0.0);
    vec3 lighting = (uAmbient.rgb * uAmbient.a) + (uLightColor * uLightIntensity * ndl);
    vec3 lit = albedo * lighting;
    float fogAmount = DistanceFogAmount(length(fragPos - uViewPos));
    vec3 fogged = mix(lit, uFogColor, fogAmount);
    finalColor = vec4(clamp(fogged, 0.0, 1.0), 1.0);
}
