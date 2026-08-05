#version 330

in vec3 fragPos;
in vec3 fragNormal;
in vec4 fragColor;
in vec2 fragTexCoord;

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
uniform sampler2D texture0;
uniform int uUseTexture;

out vec4 finalColor;

void main()
{
    vec3 N = normalize(fragNormal);
    vec3 L = normalize(uSunDirection);
    vec4 baseColor = uUseTexture == 1 ? texture(texture0, fragTexCoord) : fragColor;
    float wrappedDiffuse = clamp(dot(N, L) * 0.5 + 0.5, 0.0, 1.0);
    float directLight = wrappedDiffuse * wrappedDiffuse;
    vec3 lit = baseColor.rgb * ((uAmbientColor * uAmbient) + (uSunColor * uLightIntensity * directLight));

    float viewDistance = length(uViewPos - fragPos);
    float linearFog = smoothstep(uFogNear, uFogFar, viewDistance);
    float distanceFog = 1.0 - exp(-max(viewDistance - uFogNear, 0.0) * uFogDensity);
    float fog = clamp(max(linearFog, distanceFog), 0.0, 1.0);
    vec3 color = mix(clamp(lit, 0.0, 1.0), uFogColor, fog);
    finalColor = vec4(color, baseColor.a);
}
