#version 330

in vec2 fragTexCoord;
in vec4 fragColor;
in vec3 fragNormal;
in vec3 fragPos;

uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform vec4 tint;
uniform vec3 uLightDir;
uniform vec4 uAmbient;
uniform vec3 uLightColor;
uniform float uLightIntensity;
uniform vec3 uViewPos;
uniform vec3 uFogColor;
uniform vec4 uFogParams;

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

void main()
{
    vec4 texel = texture(texture0, fragTexCoord);
    vec4 color = fragColor;
    if (color.a <= 0.001)
    {
        color = vec4(1.0);
    }
    vec4 albedo = texel * colDiffuse * tint * color;
    vec3 N = normalize(fragNormal);
    vec3 L = normalize(uLightDir);
    float ndl = max(dot(N, L), 0.0);
    vec3 lighting = (uAmbient.rgb * uAmbient.a) + (uLightColor * uLightIntensity * ndl);
    vec3 lit = albedo.rgb * lighting;
    float fogAmount = DistanceFogAmount(length(fragPos - uViewPos));
    vec3 fogged = mix(lit, uFogColor, fogAmount);
    finalColor = vec4(clamp(fogged, 0.0, 1.0), albedo.a);
}
