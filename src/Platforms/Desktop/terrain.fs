#version 330

in vec3 fragPos;
in vec3 fragNormal;
in vec4 fragColor;

uniform vec3 uLightDir;
uniform vec4 uAmbient;
uniform vec3 uLightColor;
uniform float uLightIntensity;
uniform vec3 uViewPos;

out vec4 finalColor;

void main()
{
    vec3 N = normalize(fragNormal);
    vec3 L = normalize(uLightDir);
    float ndl = max(dot(N, L), 0.0);
    vec3 lighting = (uAmbient.rgb * uAmbient.a) + (uLightColor * uLightIntensity * ndl);
    vec3 lit = fragColor.rgb * lighting;
    finalColor = vec4(clamp(lit, 0.0, 1.0), fragColor.a);
}
