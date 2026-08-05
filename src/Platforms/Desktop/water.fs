#version 330

in vec3 fragPos;
in vec3 fragNormal;
in vec4 fragColor;
in float fragWave;

uniform vec3 uViewPos;
uniform vec3 uSunDirection;
uniform vec3 uSunColor;
uniform vec3 uAmbientColor;
uniform float uAmbient;
uniform float uLightIntensity;
uniform vec3 uFogColor;
uniform float uFogNear;
uniform float uFogFar;
uniform float uFogDensity;
uniform vec3 uWaterShallowColor;
uniform vec3 uWaterDeepColor;
uniform float uFresnelStrength;

out vec4 finalColor;

void main()
{
    vec3 N = normalize(fragNormal);
    vec3 L = normalize(uSunDirection);
    vec3 V = normalize(uViewPos - fragPos);
    vec3 H = normalize(L + V);

    float ndl = clamp(dot(N, L) * 0.5 + 0.5, 0.0, 1.0);
    float spec = pow(max(dot(N, H), 0.0), 96.0) * 0.22;
    float fresnel = pow(1.0 - clamp(dot(N, V), 0.0, 1.0), 3.0) * uFresnelStrength;

    float waterBand = clamp(fragWave * 0.5 + 0.5, 0.0, 1.0);
    vec3 base = mix(uWaterDeepColor, uWaterShallowColor, waterBand);
    vec3 lit = base * ((uAmbientColor * uAmbient) + (uSunColor * uLightIntensity * ndl));
    lit += uSunColor * (spec + fresnel * 0.24);

    float viewDistance = length(uViewPos - fragPos);
    float linearFog = smoothstep(uFogNear, uFogFar, viewDistance);
    float distanceFog = 1.0 - exp(-max(viewDistance - uFogNear, 0.0) * uFogDensity);
    float fog = clamp(max(linearFog, distanceFog), 0.0, 1.0);
    vec3 color = mix(clamp(lit, 0.0, 1.0), uFogColor, fog);
    float alpha = clamp(fragColor.a + fresnel, 0.42, 0.82);
    finalColor = vec4(color, alpha);
}
