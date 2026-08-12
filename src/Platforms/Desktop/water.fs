#version 330

in vec3 fragPos;
in vec3 fragNormal;
in vec4 fragColor;
in vec4 clipSpace;
in vec2 dudvCoords;

uniform vec3 uLightPos;
uniform vec3 uViewPos;
uniform float uAmbient;
uniform float uLightIntensity;
uniform int uSampleReflection;
uniform int uUseDudv;
uniform float uMoveFactor;
uniform float uWaveStrength;

uniform sampler2D texture0;
uniform sampler2D texture1;
uniform sampler2D texture2;

out vec4 finalColor;

void main()
{
    vec3 N = normalize(fragNormal);
    vec3 L = normalize(uLightPos - fragPos);
    vec3 V = normalize(uViewPos - fragPos);
    vec3 H = normalize(L + V);

    float ndl = abs(dot(N, L));
    float spec = pow(max(dot(N, H), 0.0), 48.0) * 0.08;
    vec3 base = fragColor.rgb;
    vec3 lit = base * (uAmbient + uLightIntensity * ndl) + vec3(spec);

    if (uSampleReflection == 0)
    {
        finalColor = vec4(clamp(lit, 0.0, 1.0), fragColor.a);
        return;
    }

    vec2 ndc = (clipSpace.xy / clipSpace.w) * 0.5 + 0.5;
    vec2 distortion = vec2(0.0);
    if (uUseDudv != 0)
    {
        vec2 distortedCoords = texture(texture2, vec2(dudvCoords.x + uMoveFactor, dudvCoords.y)).rg * 0.1;
        distortedCoords = dudvCoords + vec2(distortedCoords.x - uMoveFactor, distortedCoords.y + uMoveFactor);
        distortion = (texture(texture2, distortedCoords).rg * 2.0 - 1.0) * uWaveStrength;
    }

    vec2 reflectUv = clamp(vec2(ndc.x, 1.0 - ndc.y) + distortion, 0.01, 0.99);
    vec2 refractUv = clamp(ndc + distortion, 0.01, 0.99);

    vec3 reflectColor = texture(texture0, reflectUv).rgb;
    vec3 refractColor = texture(texture1, refractUv).rgb;

    // Lower exponent → stronger sky/terrain reflection at grazing angles (shore / aerial reads).
    float fresnel = pow(clamp(dot(V, vec3(0.0, 1.0, 0.0)), 0.0, 1.0), 0.55);
    vec3 mixed = mix(reflectColor, refractColor, fresnel);
    // Keep author vertex depth tint visible under reflection/refraction.
    mixed = mix(mixed, lit, 0.18);
    mixed += vec3(spec);

    finalColor = vec4(clamp(mixed, 0.0, 1.0), clamp(max(fragColor.a, 0.72), 0.0, 0.92));
}
