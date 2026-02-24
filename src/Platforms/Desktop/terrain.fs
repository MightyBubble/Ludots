#version 330

in vec3 fragPos;
in vec3 fragNormal;
in vec4 fragColor;

uniform vec3 uLightPos;
uniform vec3 uViewPos;
uniform float uAmbient;
uniform float uLightIntensity;

out vec4 finalColor;

void main()
{
    vec3 N = normalize(fragNormal);
    vec3 L = normalize(uLightPos - fragPos);
    float ndl = abs(dot(N, L));
    vec3 lit = fragColor.rgb * (uAmbient + uLightIntensity * ndl);
    finalColor = vec4(clamp(lit, 0.0, 1.0), fragColor.a);
}

